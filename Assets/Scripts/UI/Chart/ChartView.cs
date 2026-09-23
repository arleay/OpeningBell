using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Candlestick chart drawn with Painter2D: themed colours, overlay indicators (EMA, SMA, VWAP, Bollinger), indicator
    /// panes below (volume, RSI, MACD, ATR), the player's drawings, position/order lines (TP/SL draggable onto the real
    /// orders), optional ICT/liquidity markings, and a developer view of the hidden market. Layout is computed in
    /// Rebuild() (data change, resize, input) and only read while painting; text is pooled absolute Labels on top.
    /// Wheel = zoom, drag = pan (or draw, with a tool), double-click = back to live.
    /// </summary>
    public sealed partial class ChartView : VisualElement
    {
        private const float PriceAxisWidth = 70f;
        private const float TimeAxisHeight = 18f;
        private const float PaneGap = 6f;
        private const float Pad = 4f;

        private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
        private static readonly Color LastColor = new Color32(213, 219, 227, 110);
        private static readonly Color MarkerOutline = new Color32(13, 16, 20, 255);
        private static readonly Color NewsColor = new Color32(224, 169, 59, 255);
        private static readonly Color NewsLineColor = new Color32(224, 169, 59, 45);

        private readonly ChartViewport _viewport = new ChartViewport();
        private readonly List<float> _gridYs = new List<float>();
        private readonly List<(float x, float y, bool buy)> _markers = new List<(float, float, bool)>();
        private readonly List<float> _newsXs = new List<float>();
        private readonly List<Label> _priceLabels = new List<Label>();
        private readonly List<Label> _timeLabels = new List<Label>();
        private readonly List<Label> _textPool = new List<Label>();
        private int _textUsed;
        private readonly Label _lastTag, _crossTag, _readout, _empty, _status;
        private float _statusUntil;

        private CandleSeries _series;
        private Timeframe _timeframe;
        private string _ticker;
        private int _knownCount;
        private IReadOnlyList<Fill> _fills;
        private IReadOnlyList<NewsItem> _news;
        private ChartPreferences _prefs = new ChartPreferences();

        private Vector2? _pointer;

        // Layout for the current frame.
        private Rect _plot;
        private int _first, _count, _hovered = -1;
        private double _min, _max;
        private float _slot;

        /// <summary>An indicator pane below the price pane (volume, RSI, MACD, ATR).</summary>
        private sealed class Pane
        {
            public IndicatorConfig Config;
            public Rect Rect;
            public double Min, Max;
            public double[] A = new double[ChartViewport.MaxVisible], B = new double[ChartViewport.MaxVisible], C = new double[ChartViewport.MaxVisible];
        }

        private readonly List<Pane> _panes = new List<Pane>();
        private readonly List<(IndicatorConfig Config, double[] A, double[] B, double[] C)> _overlays =
            new List<(IndicatorConfig, double[], double[], double[])>();
        private readonly Stack<double[]> _spare = new Stack<double[]>();

        public ChartViewport Viewport => _viewport;
        public ChartTheme Theme => _prefs.Theme;

        /// <summary>Text colour that reads on the theme's background (light themes get dark text).</summary>
        private Color Ink
        {
            get
            {
                Color b = Theme.Background;
                return 0.299f * b.r + 0.587f * b.g + 0.114f * b.b > 0.5f ? new Color(0.25f, 0.27f, 0.3f) : new Color(0.6f, 0.64f, 0.7f);
            }
        }

        public ChartView()
        {
            AddToClassList("chart-view");
            focusable = true;
            _readout = Overlay("chart-readout");
            _empty = Overlay("chart-empty");
            _lastTag = Overlay("chart-tag last-tag");
            _crossTag = Overlay("chart-tag cross-tag");
            _status = Overlay("chart-tag");
            _status.style.backgroundColor = new Color(0.85f, 0.3f, 0.25f, 0.95f);
            _status.style.color = Color.white;

            generateVisualContent += Paint;
            RegisterCallback<GeometryChangedEvent>(_ => Rebuild());
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _pointer = null;
                Rebuild();
            });
        }

        public void SetPreferences(ChartPreferences prefs)
        {
            _prefs = prefs;
            Rebuild();
        }

        public void SetSeries(CandleSeries series, Timeframe timeframe, string ticker)
        {
            if (ticker != _ticker) _selected = null;
            _series = series;
            _timeframe = timeframe;
            _ticker = ticker;
            _knownCount = series.Count;
            _viewport.FollowLive();
            Rebuild();
        }

        public void SetOverlays(IReadOnlyList<Fill> fills, IReadOnlyList<NewsItem> news)
        {
            _fills = fills;
            _news = news;
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

        /// <summary>Where a moment and price sit on the chart, in the chart's local coordinates (tools and tests).</summary>
        public Vector2 PointOf(DateTime time, double price) => new Vector2(X(IndexOfTime(time.Ticks)), Y(price));

        /// <summary>A short message over the chart (e.g. why an order couldn't be moved).</summary>
        public void ShowStatus(string message)
        {
            _statusUntil = Time.realtimeSinceStartup + 3f;
            Place(_status, Pad + 8, Pad + 22, message);
        }

        // ---------------- layout ----------------

        private void Rebuild()
        {
            Rect r = contentRect;
            _count = 0;
            _textUsed = 0;
            style.backgroundColor = Theme.Background;
            if (_series == null || float.IsNaN(r.width) || r.width < 120 || r.height < 80)
            {
                HideOverlays();
                MarkDirtyRepaint();
                return;
            }

            // Panes: the price chart takes what the indicator panes leave.
            BuildPanes(r);
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

            ComputeIndicators();
            _min = double.MaxValue;
            _max = double.MinValue;
            for (int i = 0; i < _count; i++)
            {
                Candle c = _series[_first + i];
                _min = Math.Min(_min, (double)c.Low);
                _max = Math.Max(_max, (double)c.High);
            }
            // Overlays widen the range only a little (a far-off SMA shouldn't squash the candles).
            double candleSpan = _max - _min;
            foreach (var o in _overlays)
            {
                Widen(o.A, candleSpan);
                Widen(o.B, candleSpan);
                Widen(o.C, candleSpan);
            }
            // Keep the entry and working order lines on screen when they're reasonably close to the candles.
            if (_trading != null)
            {
                double span = _max - _min;
                if (_trading.Quantity > 0) Include((double)_trading.AveragePrice, span);
                foreach (Order o in _trading.Orders) Include((double)(o.IsStop && !o.Triggered ? o.StopPrice : o.LimitPrice), span);
            }
            double range = _max - _min;
            if (range <= 0) range = Math.Max(_max * 0.01, 0.01);
            _min -= range * 0.06;
            _max += range * 0.06;

            LayoutPriceAxis();
            LayoutTimeAxis();
            LayoutTags();
            LayoutMarkers();
            LayoutPanes();
            LayoutTrading();
            LayoutStructure();
            LayoutDrawings();
            LayoutCrosshair();
            HideFrom(_textPool, _textUsed);
            if (Time.realtimeSinceStartup > _statusUntil) Ui.Show(_status, false);
            MarkDirtyRepaint();
        }

        private void Include(double price, double span)
        {
            if (price <= 0 || price > _max + span * 1.5 || price < _min - span * 1.5) return;
            _max = Math.Max(_max, price);
            _min = Math.Min(_min, price);
        }

        private void Widen(double[] values, double span)
        {
            if (values == null) return;
            for (int i = 0; i < _count; i++)
            {
                double v = values[i];
                if (double.IsNaN(v)) continue;
                if (v > _max && v - _max < span * 0.5) _max = v;
                if (v < _min && _min - v < span * 0.5) _min = v;
            }
        }

        private void BuildPanes(Rect r)
        {
            // Reuse Pane objects for the configured sub-panes, in the configured order.
            var wanted = new List<IndicatorConfig>();
            foreach (IndicatorConfig c in _prefs.Indicators)
                if (c.Visible && !c.IsOverlay) wanted.Add(c);
            while (_panes.Count > wanted.Count) _panes.RemoveAt(_panes.Count - 1);
            while (_panes.Count < wanted.Count) _panes.Add(new Pane());

            float plotRight = r.width - PriceAxisWidth;
            float bottom = r.height - TimeAxisHeight;
            float total = bottom - Pad;
            // Indicator panes share at most a third of the height; the price pane always keeps the most room.
            float each = wanted.Count == 0 ? 0 : Math.Min(total * 0.14f, Math.Max(44f, total * 0.34f / wanted.Count));
            float mainHeight = total - wanted.Count * (each + PaneGap);
            _plot = new Rect(Pad, Pad, plotRight - Pad, mainHeight);
            float y = _plot.yMax + PaneGap;
            for (int i = 0; i < wanted.Count; i++)
            {
                _panes[i].Config = wanted[i];
                _panes[i].Rect = new Rect(Pad, y, _plot.width, each);
                y += each + PaneGap;
            }
        }

        private double[] Buffer() => _spare.Count > 0 ? _spare.Pop() : new double[ChartViewport.MaxVisible];

        private void ComputeIndicators()
        {
            foreach (var o in _overlays)
            {
                if (o.A != null) _spare.Push(o.A);
                if (o.B != null) _spare.Push(o.B);
                if (o.C != null) _spare.Push(o.C);
            }
            _overlays.Clear();
            foreach (IndicatorConfig c in _prefs.Indicators)
            {
                if (!c.Visible || !c.IsOverlay) continue;
                switch (c.Kind)
                {
                    case IndicatorKind.Vwap:
                        if (_timeframe == Timeframe.Day1) continue;
                        double[] v = Buffer();
                        ChartViewport.Vwap(_series, _first, _count, v);
                        _overlays.Add((c, v, null, null));
                        break;
                    case IndicatorKind.Ema:
                        double[] e = Buffer();
                        Indicators.Ema(_series, Math.Max(1, c.Period), _first, _count, e);
                        _overlays.Add((c, e, null, null));
                        break;
                    case IndicatorKind.Sma:
                        double[] m = Buffer();
                        Indicators.Sma(_series, Math.Max(1, c.Period), _first, _count, m);
                        _overlays.Add((c, m, null, null));
                        break;
                    case IndicatorKind.Bollinger:
                        double[] mid = Buffer(), up = Buffer(), lo = Buffer();
                        Indicators.Bollinger(_series, Math.Max(2, c.Period), Math.Max(1, c.Period2) / 10.0, _first, _count, mid, up, lo);
                        _overlays.Add((c, mid, up, lo));
                        break;
                }
            }

            foreach (Pane p in _panes)
            {
                IndicatorConfig c = p.Config;
                switch (c.Kind)
                {
                    case IndicatorKind.Volume:
                        p.Min = 0;
                        p.Max = 1;
                        for (int i = 0; i < _count; i++) p.Max = Math.Max(p.Max, _series[_first + i].Volume);
                        break;
                    case IndicatorKind.Rsi:
                        Indicators.Rsi(_series, Math.Max(2, c.Period), _first, _count, p.A);
                        p.Min = 0;
                        p.Max = 100;
                        break;
                    case IndicatorKind.Macd:
                        Indicators.Macd(_series, Math.Max(1, c.Period), Math.Max(2, c.Period2), Math.Max(1, c.Period3), _first, _count, p.A, p.B, p.C);
                        Range(p, p.A, p.B, p.C, symmetric: true);
                        break;
                    case IndicatorKind.Atr:
                        Indicators.Atr(_series, Math.Max(1, c.Period), _first, _count, p.A);
                        Range(p, p.A, null, null, symmetric: false);
                        p.Min = 0;
                        break;
                }
            }
        }

        private void Range(Pane p, double[] a, double[] b, double[] c, bool symmetric)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double[] arr in new[] { a, b, c })
            {
                if (arr == null) continue;
                for (int i = 0; i < _count; i++)
                {
                    if (double.IsNaN(arr[i])) continue;
                    lo = Math.Min(lo, arr[i]);
                    hi = Math.Max(hi, arr[i]);
                }
            }
            if (lo > hi) { lo = 0; hi = 1; }
            if (symmetric)
            {
                double m = Math.Max(Math.Abs(lo), Math.Abs(hi));
                lo = -m;
                hi = m;
            }
            if (hi <= lo) hi = lo + 1e-9;
            p.Min = lo;
            p.Max = hi;
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
                Label label = Pooled(_priceLabels, n++, "chart-label");
                Place(label, _plot.xMax + 6, y - 8, Fmt.Price((decimal)p));
                label.style.color = Ink;
            }
            HideFrom(_priceLabels, n);
        }

        private void LayoutTimeAxis()
        {
            // Label every k-th candle by absolute index, so labels stay put while panning.
            int k = Math.Max(1, (int)Math.Ceiling(_viewport.VisibleCount / 7.0));
            int n = 0;
            float axisY = (_panes.Count > 0 ? _panes[_panes.Count - 1].Rect.yMax : _plot.yMax) + 3;
            for (int i = _first; i < _first + _count; i++)
            {
                if (i % k != 0) continue;
                DateTime t = _series[i].Start;
                bool newDay = i == 0 || _series[i - 1].Start.Date != t.Date;
                string text = t.ToString(_timeframe == Timeframe.Day1 || newDay ? "MMM d" : "HH:mm", Invariant);
                Label label = Pooled(_timeLabels, n++, "chart-label");
                Place(label, Math.Max(0f, X(i) - 16), axisY, text);
                label.style.color = Ink;
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
        }

        private void LayoutMarkers()
        {
            _newsXs.Clear();
            if (_news != null)
            {
                foreach (NewsItem item in _news)
                {
                    if (!item.Mentions(_ticker)) continue;
                    int index = ChartViewport.FirstCandleAtOrAfter(_series, _timeframe, item.Time);
                    if (index >= _first && index < _first + _count) _newsXs.Add(X(index));
                }
            }

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

        /// <summary>Each pane's title with its latest (or hovered) value.</summary>
        private void LayoutPanes()
        {
            int shown = Shown();
            foreach (Pane p in _panes)
            {
                int o = shown - _first;
                string value = p.Config.Kind switch
                {
                    IndicatorKind.Volume => Fmt.Volume(_series[shown].Volume),
                    IndicatorKind.Macd => $"{F(p.A[o])}  {F(p.B[o])}  {F(p.C[o])}",
                    _ => F(p.A[o]),
                };
                Text(p.Rect.x + 4, p.Rect.y + 1, $"{p.Config.Title}  {value}", p.Config.Kind == IndicatorKind.Volume ? Ink : p.Config.Color);
            }

            // Overlay legend under the OHLC readout.
            float x = _plot.x + 4;
            foreach (var ov in _overlays)
            {
                double v = ov.A[shown - _first];
                Label l = Text(x, _plot.y + 18, $"{ov.Config.Title} {F(v)}", ov.Config.Color);
                x += 14 + 7.2f * l.text.Length;
            }
        }

        private static string F(double v) =>
            double.IsNaN(v) ? "–" : Math.Abs(v) >= 1000 ? v.ToString("N0", Invariant) : Math.Abs(v) >= 1 ? v.ToString("0.00", Invariant) : v.ToString("0.0000", Invariant);

        private int Shown()
        {
            int shown = _hovered >= 0 ? _hovered : _series.Count - 1;
            if (shown < _first || shown >= _first + _count) shown = _first + _count - 1;
            return shown;
        }

        private void LayoutCrosshair()
        {
            _hovered = -1;
            bool inside = _pointer.HasValue && InAnyPane(_pointer.Value);
            if (inside)
            {
                double idx = IndexAt(_pointer.Value.x);
                int index = (int)Math.Round(idx);
                if (index >= _first && index < _first + _count) _hovered = index;
                if (_plot.Contains(_pointer.Value))
                    Place(_crossTag, _plot.xMax + 2, _pointer.Value.y - 9, Fmt.Price((decimal)PriceAt(_pointer.Value.y)));
            }
            Ui.Show(_crossTag, inside && _plot.Contains(_pointer.Value));

            Candle c = _series[Shown()];
            string text = $"{_ticker}  {c.Start.ToString("MMM d HH:mm", Invariant)}   O {Fmt.Price(c.Open)}  H {Fmt.Price(c.High)}  L {Fmt.Price(c.Low)}  C {Fmt.Price(c.Close)}  V {Fmt.Volume(c.Volume)}";
            Place(_readout, _plot.x + 4, _plot.y, text);
            _readout.style.color = Ink;
        }

        private bool InAnyPane(Vector2 p)
        {
            if (_plot.Contains(p)) return true;
            foreach (Pane pane in _panes)
                if (pane.Rect.Contains(p)) return true;
            return false;
        }

        // ---------------- painting ----------------

        private void Paint(MeshGenerationContext mgc)
        {
            if (_count == 0) return;
            Painter2D p = mgc.painter2D;
            ChartTheme t = Theme;

            p.lineWidth = 1f;
            p.strokeColor = t.Grid;
            p.BeginPath();
            foreach (float y in _gridYs)
            {
                p.MoveTo(new Vector2(_plot.xMin, y));
                p.LineTo(new Vector2(_plot.xMax, y));
            }
            foreach (Pane pane in _panes)
            {
                p.MoveTo(new Vector2(pane.Rect.xMin, pane.Rect.yMin - PaneGap / 2));
                p.LineTo(new Vector2(pane.Rect.xMax, pane.Rect.yMin - PaneGap / 2));
            }
            p.Stroke();

            PaintNews(p);
            PaintStructureBehind(p);
            PaintDrawingFills(p);
            PaintCandles(p, true);
            PaintCandles(p, false);
            PaintOverlays(p);
            foreach (Pane pane in _panes) PaintPane(p, pane);

            HorizontalLine(p, (double)_series[_series.Count - 1].Close, LastColor);
            PaintStructureFront(p);
            PaintDrawings(p);
            PaintTrading(p);
            PaintMarkers(p);
            PaintDebug(p);

            if (_pointer.HasValue && InAnyPane(_pointer.Value))
            {
                p.strokeColor = t.Crosshair;
                p.lineWidth = 1f;
                p.BeginPath();
                if (_plot.Contains(_pointer.Value))
                {
                    p.MoveTo(new Vector2(_plot.xMin, _pointer.Value.y));
                    p.LineTo(new Vector2(_plot.xMax, _pointer.Value.y));
                }
                if (_hovered >= 0)
                {
                    float bottom = _panes.Count > 0 ? _panes[_panes.Count - 1].Rect.yMax : _plot.yMax;
                    p.MoveTo(new Vector2(X(_hovered), _plot.yMin));
                    p.LineTo(new Vector2(X(_hovered), bottom));
                }
                p.Stroke();
            }
        }

        private void PaintCandles(Painter2D p, bool up)
        {
            ChartTheme t = Theme;
            float width = Math.Max(1f, _slot * 0.65f);

            p.strokeColor = up ? t.UpWick : t.DownWick;
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
            p.fillColor = up ? t.Up : t.Down;
            p.BeginPath();
            for (int i = _first; i < _first + _count; i++)
            {
                Candle c = _series[i];
                if ((c.Close >= c.Open) != up) continue;
                float top = Y((double)Math.Max(c.Open, c.Close));
                float bottom = Y((double)Math.Min(c.Open, c.Close));
                PathRect(p, X(i) - width / 2, top, width, Math.Max(1f, bottom - top));
            }
            p.Fill();
        }

        private void PaintOverlays(Painter2D p)
        {
            foreach (var o in _overlays)
            {
                Color color = o.Config.Kind == IndicatorKind.Vwap && o.Config.Color == IndicatorConfig.Default(IndicatorKind.Vwap).Color
                    ? Theme.Vwap : o.Config.Color;
                if (o.Config.Kind == IndicatorKind.Bollinger)
                {
                    // Faint band between upper and lower.
                    p.fillColor = new Color(color.r, color.g, color.b, 0.07f);
                    p.BeginPath();
                    bool started = false;
                    int last = -1;
                    for (int i = 0; i < _count; i++)
                    {
                        if (double.IsNaN(o.B[i])) continue;
                        var pt = new Vector2(X(_first + i), Y(o.B[i]));
                        if (!started) { p.MoveTo(pt); started = true; }
                        else p.LineTo(pt);
                        last = i;
                    }
                    if (started)
                    {
                        for (int i = last; i >= 0; i--)
                            if (!double.IsNaN(o.C[i])) p.LineTo(new Vector2(X(_first + i), Y(o.C[i])));
                        p.ClosePath();
                        p.Fill();
                    }
                    Line(p, o.B, color, o.Config.Width, false);
                    Line(p, o.C, color, o.Config.Width, false);
                    Line(p, o.A, new Color(color.r, color.g, color.b, 0.6f), o.Config.Width, false);
                }
                else
                {
                    Line(p, o.A, color, o.Config.Width, o.Config.Kind == IndicatorKind.Vwap);
                }
            }
        }

        /// <summary>A polyline over the price pane (breaks at NaN; VWAP also breaks at each new day).</summary>
        private void Line(Painter2D p, double[] values, Color color, float width, bool breakAtDays)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            bool drawing = false;
            for (int i = 0; i < _count; i++)
            {
                double v = values[i];
                bool dayStart = breakAtDays && i > 0 && _series[_first + i].Start.Date != _series[_first + i - 1].Start.Date;
                if (double.IsNaN(v)) { drawing = false; continue; }
                var point = new Vector2(X(_first + i), Y(v));
                if (!drawing || dayStart) p.MoveTo(point);
                else p.LineTo(point);
                drawing = true;
            }
            p.Stroke();
        }

        private void PaintPane(Painter2D p, Pane pane)
        {
            Rect r = pane.Rect;
            float PY(double v) => (float)(r.yMax - (v - pane.Min) / (pane.Max - pane.Min) * r.height);
            ChartTheme t = Theme;
            float width = Math.Max(1f, _slot * 0.65f);

            void PaneLine(double[] values, Color color, float w)
            {
                p.strokeColor = color;
                p.lineWidth = w;
                p.BeginPath();
                bool drawing = false;
                for (int i = 0; i < _count; i++)
                {
                    if (double.IsNaN(values[i])) { drawing = false; continue; }
                    var pt = new Vector2(X(_first + i), PY(values[i]));
                    if (!drawing) p.MoveTo(pt);
                    else p.LineTo(pt);
                    drawing = true;
                }
                p.Stroke();
            }

            switch (pane.Config.Kind)
            {
                case IndicatorKind.Volume:
                    foreach (bool up in new[] { true, false })
                    {
                        p.fillColor = up ? t.UpVolume : t.DownVolume;
                        p.BeginPath();
                        for (int i = _first; i < _first + _count; i++)
                        {
                            Candle c = _series[i];
                            if ((c.Close >= c.Open) != up || c.Volume == 0) continue;
                            float h = Math.Max(1f, (float)(r.height * c.Volume / pane.Max));
                            PathRect(p, X(i) - width / 2, r.yMax - h, width, h);
                        }
                        p.Fill();
                    }
                    break;

                case IndicatorKind.Rsi:
                    p.strokeColor = t.Grid;
                    p.lineWidth = 1f;
                    p.BeginPath();
                    foreach (double level in new[] { 30.0, 50.0, 70.0 })
                    {
                        p.MoveTo(new Vector2(r.xMin, PY(level)));
                        p.LineTo(new Vector2(r.xMax, PY(level)));
                    }
                    p.Stroke();
                    PaneLine(pane.A, pane.Config.Color, pane.Config.Width);
                    break;

                case IndicatorKind.Macd:
                    for (int i = 0; i < _count; i++)
                    {
                        double h = pane.C[i];
                        if (double.IsNaN(h)) continue;
                        p.fillColor = h >= 0 ? t.UpVolume : t.DownVolume;
                        p.BeginPath();
                        float y0 = PY(0), y1 = PY(h);
                        PathRect(p, X(_first + i) - width / 2, Math.Min(y0, y1), width, Math.Max(1f, Math.Abs(y1 - y0)));
                        p.Fill();
                    }
                    PaneLine(pane.A, pane.Config.Color, pane.Config.Width);
                    PaneLine(pane.B, new Color(1f, 0.6f, 0.2f), pane.Config.Width);
                    break;

                case IndicatorKind.Atr:
                    PaneLine(pane.A, pane.Config.Color, pane.Config.Width);
                    break;
            }
        }

        /// <summary>Faint vertical line plus a diamond at the top of the plot where each headline landed.</summary>
        private void PaintNews(Painter2D p)
        {
            if (_newsXs.Count == 0) return;
            p.strokeColor = NewsLineColor;
            p.lineWidth = 1f;
            p.BeginPath();
            foreach (float x in _newsXs)
            {
                p.MoveTo(new Vector2(x, _plot.yMin));
                p.LineTo(new Vector2(x, _plot.yMax));
            }
            p.Stroke();

            const float s = 5f;
            p.fillColor = NewsColor;
            p.BeginPath();
            foreach (float x in _newsXs)
            {
                float y = _plot.yMin + s;
                p.MoveTo(new Vector2(x, y - s));
                p.LineTo(new Vector2(x + s, y));
                p.LineTo(new Vector2(x, y + s));
                p.LineTo(new Vector2(x - s, y));
                p.ClosePath();
            }
            p.Fill();
        }

        private void PaintMarkers(Painter2D p)
        {
            foreach (var (x, y, buy) in _markers)
            {
                // Dark outline first so a green buy marker stays visible on a green candle.
                Triangle(p, x, y, buy, 7.5f, MarkerOutline);
                Triangle(p, x, y, buy, 5f, buy ? Theme.Up : Theme.Down);
            }
        }

        /// <summary>Tip touches the fill price: buys point up from below, sells point down from above.</summary>
        private static void Triangle(Painter2D p, float x, float y, bool up, float size, Color color)
        {
            float tipY = up ? y - (size - 5f) : y + (size - 5f);
            float baseY = up ? y + size * 1.6f : y - size * 1.6f;
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(new Vector2(x, tipY));
            p.LineTo(new Vector2(x - size, baseY));
            p.LineTo(new Vector2(x + size, baseY));
            p.ClosePath();
            p.Fill();
        }

        private void HorizontalLine(Painter2D p, double price, Color color, float width = 1f, bool dashed = false, float fromX = float.NaN)
        {
            if (price < _min || price > _max) return;
            float y = Y(price);
            float x0 = float.IsNaN(fromX) ? _plot.xMin : fromX;
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            if (!dashed)
            {
                p.MoveTo(new Vector2(x0, y));
                p.LineTo(new Vector2(_plot.xMax, y));
            }
            else
            {
                for (float x = x0; x < _plot.xMax; x += 9)
                {
                    p.MoveTo(new Vector2(x, y));
                    p.LineTo(new Vector2(Math.Min(x + 5, _plot.xMax), y));
                }
            }
            p.Stroke();
        }

        private static void PathRect(Painter2D p, float x, float y, float w, float h)
        {
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
        }

        // ---------------- input ----------------

        private enum Drag { None, Pan, Order, Create, MoveDrawing, Handle }

        private Drag _drag;
        private float _dragAnchorX;

        private void OnWheel(WheelEvent e)
        {
            _viewport.Zoom(e.delta.y > 0 ? 1.15f : 1f / 1.15f);
            Rebuild();
            e.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0 || _series == null || _count == 0) return;
            Focus();
            Vector2 pos = e.localPosition;
            if (e.clickCount == 2 && Tool == null)
            {
                _viewport.FollowLive();
                Rebuild();
                return;
            }

            if (TryBeginTradingDrag(pos)) _drag = Drag.Order;
            else if (Tool != null && _plot.Contains(pos)) { BeginCreate(pos); _drag = Drag.Create; }
            else if (TryBeginDrawingDrag(pos, out bool handle)) _drag = handle ? Drag.Handle : Drag.MoveDrawing;
            else
            {
                Select(null);
                _drag = Drag.Pan;
                _dragAnchorX = pos.x;
            }
            this.CapturePointer(e.pointerId);
            Rebuild();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            _pointer = e.localPosition;
            switch (_drag)
            {
                case Drag.Pan:
                    if (_series != null && _slot > 0)
                    {
                        int candles = (int)((e.localPosition.x - _dragAnchorX) / _slot);
                        if (candles != 0)
                        {
                            _viewport.Pan(candles, _series.Count);
                            _dragAnchorX += candles * _slot;
                        }
                    }
                    break;
                case Drag.Order:
                    DragOrder(e.localPosition);
                    break;
                case Drag.Create:
                case Drag.MoveDrawing:
                case Drag.Handle:
                    DragDrawing(e.localPosition, _drag);
                    break;
            }
            Rebuild();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            switch (_drag)
            {
                case Drag.Order:
                    EndOrderDrag();
                    break;
                case Drag.Create:
                    EndCreate();
                    break;
                case Drag.MoveDrawing:
                case Drag.Handle:
                    _drawings?.Touch();
                    break;
            }
            _drag = Drag.None;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            Rebuild();
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && _selected != null)
            {
                DeleteSelected();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape && (Tool != null || _selected != null))
            {
                Tool = null;
                Select(null);
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.F10 && Debug.isDebugBuild)
            {
                _prefs.ShowDebug = !_prefs.ShowDebug;
                _prefs.Notify();
            }
            Rebuild();
        }

        // ---------------- helpers ----------------

        private float X(double index) =>
            (float)(_plot.x + (_viewport.VisibleCount - _count + (index - _first) + 0.5) * _slot);

        /// <summary>Fractional candle index under an x position (inverse of X).</summary>
        private double IndexAt(float x) => (x - _plot.x) / _slot - 0.5 - (_viewport.VisibleCount - _count) + _first;

        private float Y(double price) => (float)(_plot.y + (_max - price) / (_max - _min) * _plot.height);

        private double PriceAt(float y) => _max - (y - _plot.y) / _plot.height * (_max - _min);

        private float ClampY(float y) => Math.Clamp(y, _plot.yMin, _plot.yMax);

        /// <summary>Fractional candle index of a moment (drawings are anchored in time, not candles).</summary>
        private double IndexOfTime(long ticks)
        {
            var t = new DateTime(ticks);
            double duration = _timeframe.Duration().Ticks;
            int n = _series.Count;
            if (n == 0) return 0;
            if (t <= _series[0].Start) return (t - _series[0].Start).Ticks / duration;
            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_series[mid].Start <= t) lo = mid;
                else hi = mid - 1;
            }
            double frac = (t - _series[lo].Start).Ticks / duration;
            return lo == n - 1 ? lo + frac : lo + Math.Min(frac, 0.999);
        }

        /// <summary>The moment at a fractional candle index (beyond the last candle, time runs on at the timeframe's pace).</summary>
        private long TimeOfIndex(double index)
        {
            int n = _series.Count;
            long duration = _timeframe.Duration().Ticks;
            int i = (int)Math.Floor(Math.Clamp(index, 0, n - 1));
            return _series[i].Start.Ticks + (long)((index - i) * duration);
        }

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

        /// <summary>Free-floating chart text (pane titles, drawing and level labels), pooled per rebuild.</summary>
        private Label Text(float x, float y, string text, Color color, Color? background = null)
        {
            Label l = Pooled(_textPool, _textUsed++, "chart-label");
            Place(l, x, y, text);
            l.style.color = color;
            l.style.backgroundColor = background ?? Color.clear;
            l.style.paddingLeft = l.style.paddingRight = background.HasValue ? 4 : 0;
            l.style.borderTopLeftRadius = l.style.borderTopRightRadius = l.style.borderBottomLeftRadius = l.style.borderBottomRightRadius = 3;
            return l;
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
            HideFrom(_textPool, 0);
            Ui.Show(_lastTag, false);
            Ui.Show(_crossTag, false);
            Ui.Show(_readout, false);
            Ui.Show(_empty, false);
            _gridYs.Clear();
            _markers.Clear();
        }

        private static Color Hex(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
    }
}
