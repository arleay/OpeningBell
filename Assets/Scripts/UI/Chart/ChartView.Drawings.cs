using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// The player's drawings: horizontal lines, trendlines, rays, rectangles (zones), Fibonacci retracements and
    /// vertical lines. Pick a tool and drag on the chart to draw; click a drawing to select it, drag it (or its
    /// handles) to move or resize, Delete to remove. Drawings are anchored in time and price and saved per symbol.
    /// </summary>
    public sealed partial class ChartView
    {
        private static readonly double[] FibLevels = { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1 };
        private const float HitDistance = 6f, HandleRadius = 5f;

        private ChartDrawings _drawings;
        private ChartDrawing _selected, _creating;
        private DrawingKind? _tool;
        private int _handle; // 1 or 2 while dragging a handle
        private Vector2 _lastDrag;

        /// <summary>Raised when the selection changes (the panel shows colour/width/delete for it).</summary>
        public event Action<ChartDrawing> SelectionChanged;
        public event Action<DrawingKind?> ToolChanged;

        public ChartDrawing SelectedDrawing => _selected;

        /// <summary>The active drawing tool; null = the cursor (pan, select, drag orders).</summary>
        public DrawingKind? Tool
        {
            get => _tool;
            set
            {
                _tool = value;
                if (value != null) Select(null);
                ToolChanged?.Invoke(value);
            }
        }

        public void SetDrawings(ChartDrawings drawings)
        {
            if (_drawings != null) _drawings.Changed -= Rebuild;
            _drawings = drawings;
            if (_drawings != null) _drawings.Changed += Rebuild;
            Rebuild();
        }

        public void Select(ChartDrawing d)
        {
            if (_selected == d) return;
            _selected = d;
            SelectionChanged?.Invoke(d);
        }

        public void DeleteSelected()
        {
            if (_selected == null || _drawings == null) return;
            long id = _selected.Id;
            Select(null);
            _drawings.Remove(id);
        }

        private static string DefaultColor(DrawingKind k) => k switch
        {
            DrawingKind.HorizontalLine => "#E8B63C",
            DrawingKind.Rectangle => "#5A9CF5",
            DrawingKind.Fibonacci => "#9598A1",
            DrawingKind.VerticalLine => "#9598A1",
            _ => "#4FC3F7",
        };

        // ---------------- creating and editing

        private void BeginCreate(Vector2 pos)
        {
            long t = TimeOfIndex(IndexAt(pos.x));
            double price = PriceAt(pos.y);
            _creating = new ChartDrawing
            {
                Ticker = _ticker, Kind = _tool.Value, T1 = t, T2 = t, P1 = price, P2 = price, Color = DefaultColor(_tool.Value),
                Width = _tool.Value == DrawingKind.Rectangle ? 1f : 1.5f,
            };
        }

        private void EndCreate()
        {
            ChartDrawing d = _creating;
            _creating = null;
            if (d == null || _drawings == null) return;
            bool pointLike = d.Kind == DrawingKind.HorizontalLine || d.Kind == DrawingKind.VerticalLine;
            // A two-point drawing needs an actual drag; a plain click just cancels.
            if (!pointLike && Math.Abs(X(IndexOfTime(d.T2)) - X(IndexOfTime(d.T1))) < 4 && Math.Abs(Y(d.P2) - Y(d.P1)) < 4) return;
            _drawings.Add(d);
            Tool = null; // one drawing per pick, like most charting platforms
            Select(d);
        }

        private bool TryBeginDrawingDrag(Vector2 pos, out bool handle)
        {
            handle = false;
            _lastDrag = pos;
            if (_drawings == null || !_plot.Contains(pos)) return false;
            if (_selected != null && _selected.Ticker == _ticker)
            {
                _handle = HandleAt(_selected, pos);
                if (_handle != 0)
                {
                    handle = true;
                    return true;
                }
            }
            ChartDrawing hit = null;
            foreach (ChartDrawing d in _drawings.For(_ticker))
                if (Hit(d, pos)) hit = d; // topmost = last drawn
            if (hit == null) return false;
            Select(hit);
            return true;
        }

        private void DragDrawing(Vector2 pos, Drag mode)
        {
            if (mode == Drag.Create && _creating != null)
            {
                _creating.T2 = TimeOfIndex(IndexAt(pos.x));
                _creating.P2 = PriceAt(pos.y);
                if (_creating.Kind == DrawingKind.HorizontalLine) _creating.P1 = _creating.P2;
                if (_creating.Kind == DrawingKind.VerticalLine) _creating.T1 = _creating.T2;
                return;
            }
            ChartDrawing d = _selected;
            if (d == null) return;
            if (mode == Drag.Handle)
            {
                long t = TimeOfIndex(IndexAt(pos.x));
                double price = PriceAt(pos.y);
                if (_handle == 1) { d.T1 = t; d.P1 = price; }
                else { d.T2 = t; d.P2 = price; }
                return;
            }
            // Move the whole drawing by the pointer's travel (in candles and price).
            double dIndex = IndexAt(pos.x) - IndexAt(_lastDrag.x);
            double dPrice = PriceAt(pos.y) - PriceAt(_lastDrag.y);
            d.T1 = TimeOfIndex(IndexOfTime(d.T1) + dIndex);
            d.T2 = TimeOfIndex(IndexOfTime(d.T2) + dIndex);
            d.P1 += dPrice;
            d.P2 += dPrice;
            _lastDrag = pos;
        }

        // ---------------- geometry

        private Vector2 P1(ChartDrawing d) => new Vector2(X(IndexOfTime(d.T1)), Y(d.P1));
        private Vector2 P2(ChartDrawing d) => new Vector2(X(IndexOfTime(d.T2)), Y(d.P2));

        /// <summary>The visible segment of a line-like drawing (rays and extended trendlines run to the right edge).</summary>
        private (Vector2 A, Vector2 B) Segment(ChartDrawing d)
        {
            Vector2 a = P1(d), b = P2(d);
            switch (d.Kind)
            {
                case DrawingKind.HorizontalLine:
                    return (new Vector2(_plot.xMin, a.y), new Vector2(_plot.xMax, a.y));
                case DrawingKind.VerticalLine:
                    return (new Vector2(a.x, _plot.yMin), new Vector2(a.x, _plot.yMax));
                case DrawingKind.Ray:
                case DrawingKind.Trendline when d.ExtendRight:
                    if (Math.Abs(b.x - a.x) < 0.01f) return (a, b);
                    float slope = (b.y - a.y) / (b.x - a.x);
                    float edge = b.x >= a.x ? _plot.xMax : _plot.xMin;
                    return (a, new Vector2(edge, a.y + slope * (edge - a.x)));
                default:
                    return (a, b);
            }
        }

        private Rect Box(ChartDrawing d)
        {
            Vector2 a = P1(d), b = P2(d);
            float right = d.ExtendRight ? _plot.xMax : Math.Max(a.x, b.x);
            return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), right, Math.Max(a.y, b.y));
        }

        private bool Hit(ChartDrawing d, Vector2 p)
        {
            switch (d.Kind)
            {
                case DrawingKind.Rectangle:
                {
                    Rect r = Box(d);
                    return r.Contains(p) || DistanceToRect(r, p) < HitDistance;
                }
                case DrawingKind.Fibonacci:
                {
                    Vector2 a = P1(d), b = P2(d);
                    float x0 = Math.Min(a.x, b.x), x1 = d.ExtendRight ? _plot.xMax : Math.Max(a.x, b.x);
                    if (p.x < x0 - HitDistance || p.x > x1 + HitDistance) return false;
                    foreach (double level in FibLevels)
                        if (Math.Abs(Y(FibPrice(d, level)) - p.y) < HitDistance) return true;
                    return false;
                }
                default:
                {
                    var (a, b) = Segment(d);
                    return DistanceToSegment(p, a, b) < HitDistance;
                }
            }
        }

        private int HandleAt(ChartDrawing d, Vector2 p)
        {
            if (d.Kind == DrawingKind.HorizontalLine || d.Kind == DrawingKind.VerticalLine) return 0;
            if ((P1(d) - p).magnitude < HandleRadius + 3) return 1;
            if ((P2(d) - p).magnitude < HandleRadius + 3) return 2;
            return 0;
        }

        private static double FibPrice(ChartDrawing d, double level) => d.P2 + (d.P1 - d.P2) * level;

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + t * ab)).magnitude;
        }

        private static float DistanceToRect(Rect r, Vector2 p)
        {
            float dx = Math.Max(Math.Max(r.xMin - p.x, 0), p.x - r.xMax);
            float dy = Math.Max(Math.Max(r.yMin - p.y, 0), p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // ---------------- layout and paint

        private IEnumerable<ChartDrawing> Visible()
        {
            if (_drawings != null)
                foreach (ChartDrawing d in _drawings.For(_ticker)) yield return d;
            if (_creating != null) yield return _creating;
        }

        private void LayoutDrawings()
        {
            foreach (ChartDrawing d in Visible())
            {
                Color c = Hex(d.Color, Color.white);
                if (d.Kind == DrawingKind.HorizontalLine && d.P1 >= _min && d.P1 <= _max)
                    Text(_plot.xMax - 64, Y(d.P1) - 17, Fmt.Price((decimal)d.P1), Color.black, c);
                else if (d.Kind == DrawingKind.Fibonacci)
                {
                    float x = Math.Min(P1(d).x, P2(d).x) + 3;
                    foreach (double level in FibLevels)
                    {
                        double price = FibPrice(d, level);
                        if (price < _min || price > _max) continue;
                        Text(x, Y(price) - 15, $"{level:0.###}  {Fmt.Price((decimal)price)}", c);
                    }
                }
            }
        }

        /// <summary>Zone fills go behind the candles.</summary>
        private void PaintDrawingFills(Painter2D p)
        {
            foreach (ChartDrawing d in Visible())
            {
                if (d.Kind != DrawingKind.Rectangle) continue;
                Color c = Hex(d.Color, Color.white);
                p.fillColor = new Color(c.r, c.g, c.b, 0.14f);
                p.BeginPath();
                Rect r = Box(d);
                PathRect(p, r.x, r.y, r.width, r.height);
                p.Fill();
            }
        }

        private void PaintDrawings(Painter2D p)
        {
            foreach (ChartDrawing d in Visible())
            {
                Color c = Hex(d.Color, Color.white);
                bool selected = d == _selected;
                p.strokeColor = c;
                p.lineWidth = d.Width + (selected ? 0.5f : 0f);
                p.BeginPath();
                switch (d.Kind)
                {
                    case DrawingKind.Rectangle:
                        Rect r = Box(d);
                        PathRect(p, r.x, r.y, r.width, r.height);
                        break;
                    case DrawingKind.Fibonacci:
                        float x0 = Math.Min(P1(d).x, P2(d).x), x1 = d.ExtendRight ? _plot.xMax : Math.Max(P1(d).x, P2(d).x);
                        foreach (double level in FibLevels)
                        {
                            float y = Y(FibPrice(d, level));
                            p.MoveTo(new Vector2(x0, y));
                            p.LineTo(new Vector2(x1, y));
                        }
                        // The swing it was drawn on, faint.
                        p.MoveTo(P1(d));
                        p.LineTo(P2(d));
                        break;
                    default:
                        var (a, b) = Segment(d);
                        p.MoveTo(a);
                        p.LineTo(b);
                        break;
                }
                p.Stroke();

                if (selected && d.Kind != DrawingKind.HorizontalLine && d.Kind != DrawingKind.VerticalLine)
                    foreach (Vector2 h in new[] { P1(d), P2(d) })
                    {
                        p.fillColor = Theme.Background;
                        p.strokeColor = c;
                        p.lineWidth = 1.5f;
                        p.BeginPath();
                        p.Arc(h, HandleRadius, Angle.Degrees(0), Angle.Degrees(360));
                        p.Fill();
                        p.Stroke();
                    }
            }
        }
    }
}
