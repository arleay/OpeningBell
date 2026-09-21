using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Candlestick + volume chart drawn with Painter2D. Layout is computed in Rebuild() (data change,
    /// resize, pointer input) and only read while painting; text is pooled absolute Labels on top.
    /// Wheel = zoom, drag = pan, double-click = back to live.
    /// </summary>
    public sealed class ChartView : VisualElement
    {
        private const float PriceAxisWidth = 70f;
        private const float TimeAxisHeight = 18f;
        private const float VolumeShare = 0.18f;
        private const float PaneGap = 8f;
        private const float Pad = 4f;

        private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
        private static readonly Color UpColor = new Color32(52, 199, 123, 255);
        private static readonly Color DownColor = new Color32(232, 84, 76, 255);
        private static readonly Color UpVolume = new Color32(52, 199, 123, 80);
        private static readonly Color DownVolume = new Color32(232, 84, 76, 80);
        private static readonly Color GridColor = new Color32(255, 255, 255, 16);
        private static readonly Color VwapColor = new Color32(232, 182, 60, 255);
        private static readonly Color AvgColor = new Color32(90, 156, 245, 255);
        private static readonly Color LastColor = new Color32(213, 219, 227, 110);
        private static readonly Color CrossColor = new Color32(255, 255, 255, 60);

        private readonly ChartViewport _viewport = new ChartViewport();
        private readonly double[] _vwap = new double[ChartViewport.MaxVisible];
        private readonly List<float> _gridYs = new List<float>();
        private readonly List<(float x, float y, bool buy)> _markers = new List<(float, float, bool)>();
        private readonly List<Label> _priceLabels = new List<Label>();
        private readonly List<Label> _timeLabels = new List<Label>();
        private readonly Label _lastTag, _avgTag, _crossTag, _readout, _empty;

        private CandleSeries _series;
        private Timeframe _timeframe;
        private string _ticker;
        private int _knownCount;
        private decimal _avgCost;
        private IReadOnlyList<Fill> _fills;

        private Vector2? _pointer;
        private bool _dragging;
        private float _dragAnchorX;

        // Layout for the current frame.
        private Rect _plot, _volumePane;
        private int _first, _count, _hovered = -1;
        private double _min, _max;
        private long _maxVolume;
        private float _slot;
        private bool _showVwap;

        public ChartViewport Viewport => _viewport;

        public ChartView()
        {
            AddToClassList("chart-view");
            _readout = Overlay("chart-readout");
            _empty = Overlay("chart-empty");
            _lastTag = Overlay("chart-tag last-tag");
            _avgTag = Overlay("chart-tag avg-tag");
            _crossTag = Overlay("chart-tag cross-tag");

            generateVisualContent += Paint;
            RegisterCallback<GeometryChangedEvent>(_ => Rebuild());
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _pointer = null;
                Rebuild();
            });
        }

        public void SetSeries(CandleSeries series, Timeframe timeframe, string ticker)
        {
            _series = series;
            _timeframe = timeframe;
            _ticker = ticker;
            _knownCount = series.Count;
            _viewport.FollowLive();
            Rebuild();
        }

        public void SetOverlays(decimal averageCost, IReadOnlyList<Fill> fills)
        {
            _avgCost = averageCost;
            _fills = fills;
        }

        /// <summary>Call when new market data may have arrived.</summary>
        public void Refresh()
        {
            if (_series == null) return;
            int added = _series.Count - _knownCount;
            if (added > 0) _viewport.SeriesGrew(added);
            _knownCount = _series.Count;
            Rebuild();
        }

        // ---------------- layout ----------------

        private void Rebuild()
        {
            Rect r = contentRect;
            _count = 0;
            if (_series == null || float.IsNaN(r.width) || r.width < 120 || r.height < 80)
            {
                HideOverlays();
                MarkDirtyRepaint();
                return;
            }

            float plotRight = r.width - PriceAxisWidth;
            float chartBottom = r.height - TimeAxisHeight;
            float volumeHeight = (chartBottom - Pad) * VolumeShare;
            _plot = new Rect(Pad, Pad, plotRight - Pad, chartBottom - volumeHeight - PaneGap - Pad);
            _volumePane = new Rect(Pad, _plot.yMax + PaneGap, _plot.width, volumeHeight);
            _slot = _plot.width / _viewport.VisibleCount;

            _viewport.VisibleRange(_series.Count, out _first, out _count);
            if (_count == 0)
            {
                HideOverlays();
                _empty.text = "No trades yet";
                Ui.Show(_empty, true);
                MarkDirtyRepaint();
                return;
            }
            Ui.Show(_empty, false);

            _showVwap = _timeframe != Timeframe.Day1;
            if (_showVwap) ChartViewport.Vwap(_series, _first, _count, _vwap);

            _min = double.MaxValue;
            _max = double.MinValue;
            _maxVolume = 1;
            for (int i = 0; i < _count; i++)
            {
                Candle c = _series[_first + i];
                _min = Math.Min(_min, (double)c.Low);
                _max = Math.Max(_max, (double)c.High);
                _maxVolume = Math.Max(_maxVolume, c.Volume);
                if (_showVwap && !double.IsNaN(_vwap[i]))
                {
                    _min = Math.Min(_min, _vwap[i]);
                    _max = Math.Max(_max, _vwap[i]);
                }
            }
            double range = _max - _min;
            if (range <= 0) range = Math.Max(_max * 0.01, 0.01);
            _min -= range * 0.06;
            _max += range * 0.06;

            LayoutPriceAxis();
            LayoutTimeAxis();
            LayoutTags();
            LayoutMarkers();
            LayoutCrosshair();
            MarkDirtyRepaint();
        }

        private void LayoutPriceAxis()
        {
            _gridYs.Clear();
            double step = ChartViewport.NiceStep(_max - _min, 6);
            int n = 0;
            for (double p = Math.Ceiling(_min / step) * step; p <= _max; p += step)
            {
                float y = Y(p);
                _gridYs.Add(y);
                Place(Pooled(_priceLabels, n++, "chart-label"), _plot.xMax + 6, y - 8, Fmt.Price((decimal)p));
            }
            HideFrom(_priceLabels, n);
        }

        private void LayoutTimeAxis()
        {
            // Label every k-th candle by absolute index, so labels stay put while panning.
            int k = Math.Max(1, (int)Math.Ceiling(_viewport.VisibleCount / 7.0));
            int n = 0;
            for (int i = _first; i < _first + _count; i++)
            {
                if (i % k != 0) continue;
                DateTime t = _series[i].Start;
                bool newDay = i == 0 || _series[i - 1].Start.Date != t.Date;
                string text = t.ToString(_timeframe == Timeframe.Day1 || newDay ? "MMM d" : "HH:mm", Invariant);
                Place(Pooled(_timeLabels, n++, "chart-label"), Math.Max(0f, X(i) - 16), _volumePane.yMax + 3, text);
            }
            HideFrom(_timeLabels, n);
        }

        private void LayoutTags()
        {
            Candle latest = _series[_series.Count - 1];
            bool up = _series.Count < 2 || latest.Close >= _series[_series.Count - 2].Close;
            Place(_lastTag, _plot.xMax + 2, ClampY(Y((double)latest.Close)) - 9, Fmt.Price(latest.Close));
            _lastTag.EnableInClassList("up", up);
            _lastTag.EnableInClassList("down", !up);

            bool showAvg = _avgCost > 0m && (double)_avgCost >= _min && (double)_avgCost <= _max;
            Ui.Show(_avgTag, showAvg);
            if (showAvg) Place(_avgTag, _plot.xMax + 2, Y((double)_avgCost) - 9, "AVG " + Fmt.Price(_avgCost));
        }

        private void LayoutMarkers()
        {
            _markers.Clear();
            if (_fills == null) return;
            foreach (Fill fill in _fills)
            {
                if (fill.Ticker != _ticker) continue;
                // Fill time is the end of the tick that produced its quote; step back into that tick's bucket.
                int index = ChartViewport.FindCandle(_series, _timeframe, fill.Time.AddSeconds(-1));
                if (index < _first || index >= _first + _count) continue;
                _markers.Add((X(index), Y((double)fill.Price), fill.Side == OrderSide.Buy));
            }
        }

        private void LayoutCrosshair()
        {
            _hovered = -1;
            bool inside = _pointer.HasValue && _plot.Contains(_pointer.Value);
            if (inside)
            {
                int slot = (int)((_pointer.Value.x - _plot.x) / _slot);
                int index = _first + slot - (_viewport.VisibleCount - _count);
                if (index >= _first && index < _first + _count) _hovered = index;
                double price = _max - (_pointer.Value.y - _plot.y) / _plot.height * (_max - _min);
                Place(_crossTag, _plot.xMax + 2, _pointer.Value.y - 9, Fmt.Price((decimal)price));
            }
            Ui.Show(_crossTag, inside);

            int shown = _hovered >= 0 ? _hovered : _series.Count - 1;
            if (shown < _first || shown >= _first + _count) shown = _first + _count - 1;
            Candle c = _series[shown];
            string text = $"{c.Start.ToString("MMM d HH:mm", Invariant)}   O {Fmt.Price(c.Open)}  H {Fmt.Price(c.High)}  L {Fmt.Price(c.Low)}  C {Fmt.Price(c.Close)}  V {Fmt.Volume(c.Volume)}";
            if (_showVwap && !double.IsNaN(_vwap[shown - _first])) text += $"   VWAP {Fmt.Price((decimal)_vwap[shown - _first])}";
            Place(_readout, _plot.x + 4, _plot.y, text);
        }

        // ---------------- painting ----------------

        private void Paint(MeshGenerationContext mgc)
        {
            if (_count == 0) return;
            Painter2D p = mgc.painter2D;

            p.lineWidth = 1f;
            p.strokeColor = GridColor;
            p.BeginPath();
            foreach (float y in _gridYs)
            {
                p.MoveTo(new Vector2(_plot.xMin, y));
                p.LineTo(new Vector2(_plot.xMax, y));
            }
            p.Stroke();

            PaintVolume(p, true);
            PaintVolume(p, false);
            PaintCandles(p, true);
            PaintCandles(p, false);

            if (_showVwap) PaintVwap(p);
            HorizontalLine(p, (double)_series[_series.Count - 1].Close, LastColor);
            if (_avgCost > 0m) HorizontalLine(p, (double)_avgCost, AvgColor);
            PaintMarkers(p);

            if (_pointer.HasValue && _plot.Contains(_pointer.Value))
            {
                p.strokeColor = CrossColor;
                p.BeginPath();
                p.MoveTo(new Vector2(_plot.xMin, _pointer.Value.y));
                p.LineTo(new Vector2(_plot.xMax, _pointer.Value.y));
                if (_hovered >= 0)
                {
                    p.MoveTo(new Vector2(X(_hovered), _plot.yMin));
                    p.LineTo(new Vector2(X(_hovered), _volumePane.yMax));
                }
                p.Stroke();
            }
        }

        private void PaintVolume(Painter2D p, bool up)
        {
            float width = Math.Max(1f, _slot * 0.65f);
            p.fillColor = up ? UpVolume : DownVolume;
            p.BeginPath();
            for (int i = _first; i < _first + _count; i++)
            {
                Candle c = _series[i];
                if ((c.Close >= c.Open) != up || c.Volume == 0) continue;
                float h = Math.Max(1f, _volumePane.height * c.Volume / _maxVolume);
                Rect(p, X(i) - width / 2, _volumePane.yMax - h, width, h);
            }
            p.Fill();
        }

        private void PaintCandles(Painter2D p, bool up)
        {
            Color color = up ? UpColor : DownColor;
            float width = Math.Max(1f, _slot * 0.65f);

            p.strokeColor = color;
            p.lineWidth = 1f;
            p.BeginPath();
            for (int i = _first; i < _first + _count; i++)
            {
                Candle c = _series[i];
                if ((c.Close >= c.Open) != up) continue;
                float x = X(i);
                p.MoveTo(new Vector2(x, Y((double)c.High)));
                p.LineTo(new Vector2(x, Y((double)c.Low)));
            }
            p.Stroke();

            if (width < 2f) return;
            p.fillColor = color;
            p.BeginPath();
            for (int i = _first; i < _first + _count; i++)
            {
                Candle c = _series[i];
                if ((c.Close >= c.Open) != up) continue;
                float top = Y((double)Math.Max(c.Open, c.Close));
                float bottom = Y((double)Math.Min(c.Open, c.Close));
                Rect(p, X(i) - width / 2, top, width, Math.Max(1f, bottom - top));
            }
            p.Fill();
        }

        private void PaintVwap(Painter2D p)
        {
            p.strokeColor = VwapColor;
            p.lineWidth = 1.5f;
            p.BeginPath();
            bool drawing = false;
            for (int i = 0; i < _count; i++)
            {
                double v = _vwap[i];
                bool dayStart = i > 0 && _series[_first + i].Start.Date != _series[_first + i - 1].Start.Date;
                if (double.IsNaN(v)) { drawing = false; continue; }
                var point = new Vector2(X(_first + i), Y(v));
                if (!drawing || dayStart) p.MoveTo(point);
                else p.LineTo(point);
                drawing = true;
            }
            p.Stroke();
        }

        private void PaintMarkers(Painter2D p)
        {
            const float s = 5f;
            foreach (var (x, y, buy) in _markers)
            {
                p.fillColor = buy ? UpColor : DownColor;
                p.BeginPath();
                // Tip touches the fill price: buys point up from below, sells point down from above.
                float baseY = buy ? y + s * 1.6f : y - s * 1.6f;
                p.MoveTo(new Vector2(x, y));
                p.LineTo(new Vector2(x - s, baseY));
                p.LineTo(new Vector2(x + s, baseY));
                p.ClosePath();
                p.Fill();
            }
        }

        private void HorizontalLine(Painter2D p, double price, Color color)
        {
            if (price < _min || price > _max) return;
            float y = Y(price);
            p.strokeColor = color;
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(_plot.xMin, y));
            p.LineTo(new Vector2(_plot.xMax, y));
            p.Stroke();
        }

        private static void Rect(Painter2D p, float x, float y, float w, float h)
        {
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
        }

        // ---------------- input ----------------

        private void OnWheel(WheelEvent e)
        {
            _viewport.Zoom(e.delta.y > 0 ? 1.15f : 1f / 1.15f);
            Rebuild();
            e.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            if (e.clickCount == 2)
            {
                _viewport.FollowLive();
                Rebuild();
                return;
            }
            _dragging = true;
            _dragAnchorX = e.localPosition.x;
            this.CapturePointer(e.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            _pointer = e.localPosition;
            if (_dragging && _series != null && _slot > 0)
            {
                int candles = (int)((e.localPosition.x - _dragAnchorX) / _slot);
                if (candles != 0)
                {
                    _viewport.Pan(candles, _series.Count);
                    _dragAnchorX += candles * _slot;
                }
            }
            Rebuild();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            _dragging = false;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
        }

        // ---------------- helpers ----------------

        private float X(int index) =>
            _plot.x + (_viewport.VisibleCount - _count + (index - _first) + 0.5f) * _slot;

        private float Y(double price) => (float)(_plot.y + (_max - price) / (_max - _min) * _plot.height);

        private float ClampY(float y) => Math.Clamp(y, _plot.yMin, _plot.yMax);

        private Label Overlay(string classes)
        {
            Label label = Ui.Label(classes, this);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private Label Pooled(List<Label> pool, int index, string classes)
        {
            while (pool.Count <= index) pool.Add(Overlay(classes));
            return pool[index];
        }

        private static void Place(Label label, float x, float y, string text)
        {
            Ui.SetText(label, text);
            label.style.left = x;
            label.style.top = y;
            Ui.Show(label, true);
        }

        private static void HideFrom(List<Label> pool, int start)
        {
            for (int i = start; i < pool.Count; i++) Ui.Show(pool[i], false);
        }

        private void HideOverlays()
        {
            HideFrom(_priceLabels, 0);
            HideFrom(_timeLabels, 0);
            Ui.Show(_lastTag, false);
            Ui.Show(_avgTag, false);
            Ui.Show(_crossTag, false);
            Ui.Show(_readout, false);
            Ui.Show(_empty, false);
            _gridYs.Clear();
            _markers.Clear();
        }
    }
}
