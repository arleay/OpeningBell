using System;
using System.Collections.Generic;
using OpeningBell.Market;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    internal enum GlyphKind { Trading, News, Mail, Browser, Home, Back, Forward, Reload, Lock, Search, Bank, Store, Chart }

    /// <summary>
    /// Line icons drawn with Painter2D, so the desktop needs no icon textures and they stay sharp at any size.
    /// Shapes are laid out in a unit square and scaled to the element.
    /// </summary>
    internal sealed class Glyph : VisualElement
    {
        private const float K = 0.5523f; // cubic Bézier handle length for a quarter ellipse

        private readonly GlyphKind _kind;
        private Color _color;
        private float _w, _h;

        public Glyph(GlyphKind kind, float size, Color color, string classes = null)
        {
            _kind = kind;
            _color = color;
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            if (classes != null) AddToClassList(classes);
            generateVisualContent += Paint;
        }

        /// <summary>A rounded app tile (colour from the USS <paramref name="tileClass"/>) with the glyph in white.</summary>
        public static VisualElement Tile(GlyphKind kind, string tileClass, float size, VisualElement parent = null)
        {
            var tile = Ui.Box("app-tile " + tileClass, parent);
            tile.style.width = size;
            tile.style.height = size;
            tile.pickingMode = PickingMode.Ignore;
            tile.Add(new Glyph(kind, size * 0.62f, Color.white));
            return tile;
        }

        private Vector2 V(float x, float y) => new Vector2(x * _w, y * _h);

        private void Paint(MeshGenerationContext mgc)
        {
            _w = contentRect.width;
            _h = contentRect.height;
            if (_w < 1f || _h < 1f) return;
            Painter2D p = mgc.painter2D;
            p.strokeColor = _color;
            p.fillColor = _color;
            p.lineWidth = Mathf.Max(1.4f, _w * 0.075f);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            switch (_kind)
            {
                case GlyphKind.Trading:
                    // Three candles: wick, then a filled body.
                    Candle(p, 0.25f, 0.2f, 0.35f, 0.6f, 0.72f);
                    Candle(p, 0.5f, 0.3f, 0.45f, 0.7f, 0.85f);
                    Candle(p, 0.75f, 0.12f, 0.22f, 0.48f, 0.6f);
                    break;
                case GlyphKind.News:
                    StrokeRect(p, 0.14f, 0.2f, 0.86f, 0.8f);
                    FillRect(p, 0.25f, 0.31f, 0.47f, 0.5f);
                    Line(p, 0.56f, 0.33f, 0.75f, 0.33f);
                    Line(p, 0.56f, 0.47f, 0.75f, 0.47f);
                    Line(p, 0.25f, 0.61f, 0.75f, 0.61f);
                    Line(p, 0.25f, 0.71f, 0.62f, 0.71f);
                    break;
                case GlyphKind.Mail:
                    StrokeRect(p, 0.14f, 0.25f, 0.86f, 0.75f);
                    p.BeginPath();
                    p.MoveTo(V(0.16f, 0.28f));
                    p.LineTo(V(0.5f, 0.53f));
                    p.LineTo(V(0.84f, 0.28f));
                    p.Stroke();
                    break;
                case GlyphKind.Browser:
                    Ellipse(p, 0.5f, 0.5f, 0.34f, 0.34f);
                    Ellipse(p, 0.5f, 0.5f, 0.14f, 0.34f);
                    Line(p, 0.16f, 0.5f, 0.84f, 0.5f);
                    Line(p, 0.23f, 0.36f, 0.77f, 0.36f);
                    Line(p, 0.23f, 0.64f, 0.77f, 0.64f);
                    break;
                case GlyphKind.Home:
                    FillRect(p, 0.18f, 0.18f, 0.46f, 0.46f);
                    FillRect(p, 0.54f, 0.18f, 0.82f, 0.46f);
                    FillRect(p, 0.18f, 0.54f, 0.46f, 0.82f);
                    FillRect(p, 0.54f, 0.54f, 0.82f, 0.82f);
                    break;
                case GlyphKind.Back:
                    Chevron(p, 0.6f, 0.36f);
                    break;
                case GlyphKind.Forward:
                    Chevron(p, 0.4f, 0.64f);
                    break;
                case GlyphKind.Reload:
                    Reload(p);
                    break;
                case GlyphKind.Lock:
                    FillRect(p, 0.28f, 0.47f, 0.72f, 0.82f);
                    p.BeginPath();
                    p.Arc(V(0.5f, 0.47f), 0.15f * _w, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.Stroke();
                    break;
                case GlyphKind.Search:
                    Ellipse(p, 0.43f, 0.43f, 0.22f, 0.22f);
                    Line(p, 0.6f, 0.6f, 0.82f, 0.82f);
                    break;
                case GlyphKind.Bank:
                    p.BeginPath();
                    p.MoveTo(V(0.12f, 0.38f));
                    p.LineTo(V(0.5f, 0.16f));
                    p.LineTo(V(0.88f, 0.38f));
                    p.ClosePath();
                    p.Fill();
                    FillRect(p, 0.2f, 0.44f, 0.3f, 0.72f);
                    FillRect(p, 0.45f, 0.44f, 0.55f, 0.72f);
                    FillRect(p, 0.7f, 0.44f, 0.8f, 0.72f);
                    FillRect(p, 0.12f, 0.76f, 0.88f, 0.84f);
                    break;
                case GlyphKind.Store:
                    FillRect(p, 0.2f, 0.38f, 0.8f, 0.84f);
                    p.BeginPath();
                    p.Arc(V(0.5f, 0.38f), 0.15f * _w, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.Stroke();
                    break;
                case GlyphKind.Chart:
                    p.BeginPath();
                    p.MoveTo(V(0.14f, 0.74f));
                    p.LineTo(V(0.36f, 0.5f));
                    p.LineTo(V(0.54f, 0.62f));
                    p.LineTo(V(0.86f, 0.26f));
                    p.Stroke();
                    break;
            }
        }

        private void Candle(Painter2D p, float x, float top, float bodyTop, float bodyBottom, float bottom)
        {
            Line(p, x, top, x, bottom);
            FillRect(p, x - 0.08f, bodyTop, x + 0.08f, bodyBottom);
        }

        private void Chevron(Painter2D p, float from, float tip)
        {
            p.BeginPath();
            p.MoveTo(V(from, 0.25f));
            p.LineTo(V(tip, 0.5f));
            p.LineTo(V(from, 0.75f));
            p.Stroke();
        }

        /// <summary>An open circle running clockwise, with an arrowhead pointing the way it turns.</summary>
        private void Reload(Painter2D p)
        {
            const float r = 0.27f, start = 60f, end = 330f;
            p.BeginPath();
            p.Arc(V(0.5f, 0.5f), r * _w, Angle.Degrees(start), Angle.Degrees(end));
            p.Stroke();
            float a = end * Mathf.Deg2Rad;
            var tip = new Vector2(0.5f + r * Mathf.Cos(a), 0.5f + r * Mathf.Sin(a));
            var along = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)); // clockwise on screen (y down)
            var side = new Vector2(-along.y, along.x);
            Vector2 b1 = tip - along * 0.1f + side * 0.17f, b2 = tip - along * 0.1f - side * 0.17f;
            p.BeginPath();
            p.MoveTo(V(tip.x + along.x * 0.16f, tip.y + along.y * 0.16f));
            p.LineTo(V(b1.x, b1.y));
            p.LineTo(V(b2.x, b2.y));
            p.ClosePath();
            p.Fill();
        }

        private void Line(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(V(x0, y0));
            p.LineTo(V(x1, y1));
            p.Stroke();
        }

        private void StrokeRect(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(V(x0, y0));
            p.LineTo(V(x1, y0));
            p.LineTo(V(x1, y1));
            p.LineTo(V(x0, y1));
            p.ClosePath();
            p.Stroke();
        }

        private void FillRect(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(V(x0, y0));
            p.LineTo(V(x1, y0));
            p.LineTo(V(x1, y1));
            p.LineTo(V(x0, y1));
            p.ClosePath();
            p.Fill();
        }

        private void Ellipse(Painter2D p, float cx, float cy, float rx, float ry)
        {
            p.BeginPath();
            p.MoveTo(V(cx + rx, cy));
            p.BezierCurveTo(V(cx + rx, cy + ry * K), V(cx + rx * K, cy + ry), V(cx, cy + ry));
            p.BezierCurveTo(V(cx - rx * K, cy + ry), V(cx - rx, cy + ry * K), V(cx - rx, cy));
            p.BezierCurveTo(V(cx - rx, cy - ry * K), V(cx - rx * K, cy - ry), V(cx, cy - ry));
            p.BezierCurveTo(V(cx + rx * K, cy - ry), V(cx + rx, cy - ry * K), V(cx + rx, cy));
            p.ClosePath();
            p.Stroke();
        }
    }

    /// <summary>Today's one-minute closes as a line, green above the reference (previous close), red below.</summary>
    internal sealed class Sparkline : VisualElement
    {
        private static readonly Color Up = new Color32(0x34, 0xc7, 0x7b, 0xff);
        private static readonly Color Down = new Color32(0xe8, 0x54, 0x4c, 0xff);
        private static readonly Color Reference = new Color(1f, 1f, 1f, 0.18f);

        private readonly List<float> _closes = new List<float>();
        private readonly float _lineWidth;
        private float _reference;
        private int _knownCount = -1;
        private decimal _knownLast;

        public Sparkline(string classes, float lineWidth = 1.6f)
        {
            _lineWidth = lineWidth;
            Ui.AddClassesTo(this, "sparkline " + classes);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }

        /// <summary>Repaints only when a candle closes or the last price moves.</summary>
        public void Set(CandleSeries series, decimal reference, DateTime today)
        {
            int count = series.Count;
            decimal last = count > 0 ? series[count - 1].Close : 0m;
            if (count == _knownCount && last == _knownLast && (float)reference == _reference) return;
            _knownCount = count;
            _knownLast = last;
            _reference = (float)reference;
            _closes.Clear();
            int first = count;
            while (first > 0 && series[first - 1].Start.Date == today.Date) first--;
            for (int i = first; i < count; i++) _closes.Add((float)series[i].Close);
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext mgc)
        {
            Rect r = contentRect;
            if (_closes.Count < 2 || r.width < 2f) return;
            float lo = _reference, hi = _reference;
            foreach (float c in _closes)
            {
                lo = Mathf.Min(lo, c);
                hi = Mathf.Max(hi, c);
            }
            float span = Mathf.Max(hi - lo, hi * 0.002f);
            float Y(float v) => r.yMax - 2f - (v - lo) / span * (r.height - 4f);

            Painter2D p = mgc.painter2D;
            p.lineWidth = 1f;
            p.strokeColor = Reference;
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, Y(_reference)));
            p.LineTo(new Vector2(r.xMax, Y(_reference)));
            p.Stroke();

            p.lineWidth = _lineWidth;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = _closes[_closes.Count - 1] >= _reference ? Up : Down;
            p.BeginPath();
            float step = r.width / (_closes.Count - 1);
            for (int i = 0; i < _closes.Count; i++)
            {
                var pt = new Vector2(r.xMin + i * step, Y(_closes[i]));
                if (i == 0) p.MoveTo(pt);
                else p.LineTo(pt);
            }
            p.Stroke();
        }
    }

    /// <summary>Desktop background: a dusk sky over the Kell Valley skyline, a few windows lit.</summary>
    internal sealed class Wallpaper : VisualElement
    {
        private static readonly Color Top = new Color32(0x1d, 0x2f, 0x48, 0xff);
        private static readonly Color Horizon = new Color32(0x6b, 0x4a, 0x3e, 0xff);
        private static readonly Color Skyline = new Color32(0x0b, 0x10, 0x18, 0xff);
        private static readonly Color Window = new Color32(0xe0, 0xa9, 0x3b, 0xb0);

        // Building outlines as (left, width, height) in fractions of the screen; fixed so the town never reshuffles.
        private static readonly (float x, float w, float h)[] Buildings =
        {
            (0.00f, 0.05f, 0.14f), (0.04f, 0.04f, 0.2f), (0.08f, 0.06f, 0.12f), (0.13f, 0.03f, 0.26f), (0.16f, 0.06f, 0.17f),
            (0.22f, 0.04f, 0.11f), (0.26f, 0.05f, 0.22f), (0.31f, 0.07f, 0.15f), (0.38f, 0.03f, 0.3f), (0.41f, 0.05f, 0.19f),
            (0.46f, 0.06f, 0.13f), (0.52f, 0.04f, 0.24f), (0.56f, 0.07f, 0.16f), (0.63f, 0.03f, 0.21f), (0.66f, 0.06f, 0.12f),
            (0.72f, 0.05f, 0.27f), (0.77f, 0.04f, 0.15f), (0.81f, 0.06f, 0.19f), (0.87f, 0.04f, 0.12f), (0.91f, 0.05f, 0.23f),
            (0.96f, 0.04f, 0.14f),
        };

        public Wallpaper()
        {
            AddToClassList("wallpaper");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }

        private void Paint(MeshGenerationContext mgc)
        {
            Rect r = contentRect;
            if (r.width < 1f) return;
            Painter2D p = mgc.painter2D;

            // Sky: horizontal bands blended top → horizon (UI Toolkit has no gradient fill).
            const int bands = 48;
            for (int i = 0; i < bands; i++)
            {
                float t = i / (bands - 1f);
                p.fillColor = Color.Lerp(Top, Horizon, t * t);
                float y0 = r.height * i / bands, y1 = r.height * (i + 1) / bands + 1f;
                Rect(p, 0f, y0, r.width, y1);
            }

            p.fillColor = Skyline;
            float ground = r.height;
            foreach (var b in Buildings) Rect(p, b.x * r.width, ground - b.h * r.height, (b.x + b.w) * r.width, ground);

            // Lit windows on a fixed pattern: every building gets a sparse grid, about one pane in five lit.
            p.fillColor = Window;
            for (int bi = 0; bi < Buildings.Length; bi++)
            {
                var b = Buildings[bi];
                float left = b.x * r.width, width = b.w * r.width, top = ground - b.h * r.height;
                for (int row = 0; top + 14f + row * 16f < ground - 10f; row++)
                    for (int col = 0; 6f + col * 12f < width - 8f; col++)
                        if ((bi * 7 + row * 13 + col * 5) % 5 == 0)
                        {
                            float x = left + 6f + col * 12f, y = top + 14f + row * 16f;
                            Rect(p, x, y, x + 5f, y + 7f);
                        }
            }
        }

        private static void Rect(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();
        }
    }
}
