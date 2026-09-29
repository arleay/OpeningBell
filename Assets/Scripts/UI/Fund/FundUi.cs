using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Fund;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Building blocks for the fund's pages (Ledgerline, the registry, the employee panel). Styling in Terminal.uss (".fund-*").</summary>
    internal static class FundUi
    {
        public static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static string Money(decimal d) => (d < 0m ? "-$" : "$") + Math.Abs(d).ToString("N2", C);
        public static string Money0(decimal d) => (d < 0m ? "-$" : "$") + Math.Abs(Math.Round(d)).ToString("N0", C);
        public static string Signed(decimal d) => (d > 0m ? "+" : d < 0m ? "-" : "") + "$" + Math.Abs(d).ToString("N2", C);
        public static string Pct(double v) => (v * 100).ToString("0.0", C) + "%";

        public static Color BrandColour(int i)
        {
            var c = FundBrand.Colours[FundBrand.Clamp(i, FundBrand.Colours.Length)];
            return new Color(c.R, c.G, c.B);
        }

        /// <summary>A card with a small caps title; returns the body to fill.</summary>
        public static VisualElement Card(VisualElement parent, string title, string classes = "")
        {
            var card = Ui.Box("fund-card " + classes, parent);
            if (!string.IsNullOrEmpty(title))
            {
                var head = Ui.Box("fund-card-head", card);
                Ui.Label("fund-card-title", head, title.ToUpperInvariant());
            }
            return Ui.Box("fund-card-body", card);
        }

        /// <summary>A headline number with its caption and an optional small line under it. Returns (value, sub).</summary>
        public static (Label Value, Label Sub) Kpi(VisualElement parent, string caption, string classes = "")
        {
            var box = Ui.Box("fund-kpi " + classes, parent);
            Ui.Label("fund-kpi-caption", box, caption.ToUpperInvariant());
            Label value = Ui.Label("fund-kpi-value", box);
            Label sub = Ui.Label("fund-kpi-sub", box);
            return (value, sub);
        }

        public static void SetSigned(Label l, decimal v, string text = null)
        {
            Ui.SetText(l, text ?? Signed(v));
            l.EnableInClassList("fund-up", v > 0m);
            l.EnableInClassList("fund-down", v < 0m);
        }

        /// <summary>A coloured status pill: kind is ok, warn, bad, info or muted.</summary>
        public static Label Pill(VisualElement parent, string text, string kind)
        {
            var l = Ui.Label("fund-pill", parent, text);
            SetPill(l, text, kind);
            return l;
        }

        public static void SetPill(Label l, string text, string kind)
        {
            Ui.SetText(l, text);
            foreach (string k in new[] { "ok", "warn", "bad", "info", "muted" }) l.EnableInClassList("fund-pill-" + k, k == kind);
        }

        public static Button Primary(string text, Action click, VisualElement parent, string name = null) => Ui.Button(text, click, "fund-btn fund-btn-primary", parent, name);
        public static Button Secondary(string text, Action click, VisualElement parent, string name = null) => Ui.Button(text, click, "fund-btn fund-btn-secondary", parent, name);
        public static Button Danger(string text, Action click, VisualElement parent, string name = null) => Ui.Button(text, click, "fund-btn fund-btn-danger", parent, name);
        public static Button Collect(string text, Action click, VisualElement parent, string name = null) => Ui.Button(text, click, "fund-btn fund-btn-collect", parent, name);

        /// <summary>
        /// A button that needs a second click to act: the first turns it into "CONFIRM …" for a few seconds, so money
        /// never moves on a stray click.
        /// </summary>
        public static Button Armed(string text, string confirm, Action act, VisualElement parent, string classes, string name = null)
        {
            Button b = Ui.Button(text, null, "fund-btn " + classes, parent, name);
            double armedAt = -10;
            b.clicked += () =>
            {
                if (Time.realtimeSinceStartupAsDouble - armedAt > 6)
                {
                    armedAt = Time.realtimeSinceStartupAsDouble;
                    Ui.SetText(b, confirm);
                    b.AddToClassList("fund-btn-armed");
                    return;
                }
                armedAt = -10;
                Ui.SetText(b, text);
                b.RemoveFromClassList("fund-btn-armed");
                act();
            };
            return b;
        }

        /// <summary>A label: value row (definition lists in cards).</summary>
        public static Label Line(VisualElement parent, string caption, string value = "", string classes = "")
        {
            var row = Ui.Box("fund-line " + classes, parent);
            Ui.Label("fund-line-caption", row, caption);
            // No spacer: the value itself takes the rest of the row (basis 0, grow 1) and right-aligns, so a long value
            // wraps inside a known width and the row grows to fit. A percentage max-width wrapped the text without
            // telling layout, and the extra lines spilled over the rows below.
            return Ui.Label("fund-line-value", row, value);
        }

        /// <summary>One table row of fixed-width cells (widths in px; 0 = grow).</summary>
        public static List<Label> Row(VisualElement parent, string classes, params (string Text, float Width)[] cells)
        {
            var row = Ui.Box("fund-row " + classes, parent);
            var labels = new List<Label>();
            foreach (var (text, width) in cells)
            {
                Label l = Ui.Label("fund-cell", row, text);
                if (width > 0f) l.style.width = width;
                else l.style.flexGrow = 1;
                labels.Add(l);
            }
            return labels;
        }

        public static TextField Field(VisualElement parent, string value, string name, string allowed = null, float width = 140f)
        {
            var f = new TextField { name = name, value = value };
            f.AddToClassList("fund-field");
            f.style.width = width;
            if (allowed != null) TicketInput.Restrict(f, allowed);
            parent.Add(f);
            return f;
        }

        public static bool TryMoney(string text, out decimal value) =>
            decimal.TryParse((text ?? "").Replace("$", "").Replace(",", "").Replace("%", "").Trim(), NumberStyles.Number, C, out value);

        /// <summary>Initials in a brand-coloured circle.</summary>
        public static VisualElement Avatar(VisualElement parent, Person p, float size = 34f)
        {
            var a = Ui.Box("fund-avatar", parent);
            a.style.width = a.style.height = size;
            a.style.borderTopLeftRadius = a.style.borderTopRightRadius = a.style.borderBottomLeftRadius = a.style.borderBottomRightRadius = size / 2f;
            Color[] tones = { new Color(0.16f, 0.3f, 0.55f), new Color(0.2f, 0.45f, 0.35f), new Color(0.5f, 0.3f, 0.2f), new Color(0.35f, 0.25f, 0.5f), new Color(0.2f, 0.4f, 0.5f) };
            a.style.backgroundColor = tones[(int)(Math.Abs(p.Id) % tones.Length)];
            Label l = Ui.Label("fund-avatar-text", a, p.Initials);
            l.style.fontSize = size * 0.38f;
            return a;
        }

        /// <summary>The dashboard's words for where someone is.</summary>
        public static (string Text, string Kind) Status(HedgeFund f, Employee e)
        {
            switch (e.Activity)
            {
                case Activity.Trading:
                    if (!e.Policy.Authorized) return ("Paused by management", "warn");
                    return ("Trading", "ok");
                case Activity.WaitingForWorkstation: return ("Waiting for workstation", "bad");
                case Activity.Training: return (f.CurrentTrainingLabel(e) ?? "Training", "info");
                case Activity.RiskLocked: return ("Risk locked", "bad");
                case Activity.OnBreak: return (e.Break == BreakKind.Lunch ? "At lunch" : e.Break == BreakKind.Restroom ? "Restroom break" : "On a break", "muted");
                case Activity.Preparing: return (f.CurrentTrainingLabel(e) ?? "Preparing for the open", "info");
                case Activity.WrappingUp: return ("End-of-day tasks", "info");
                case Activity.SettlingIn: return ("Walking to their desk", "info");
                case Activity.Admin: return ("At the front desk", "ok");
                case Activity.Commuting: return ("Commuting", "muted");
                case Activity.Arriving: return ("Arriving", "muted");
                case Activity.Leaving: return (e.Former ? "Leaving the company" : "Leaving for the day", "muted");
                case Activity.AwaitingStart: return ($"Starts {new DateTime(e.StartsOn):ddd MMM d}", "info");
                case Activity.Former: return ("Former employee", "muted");
                default: return ("Off duty", "muted");
            }
        }
    }

    /// <summary>A fund's logo mark in its brand colour, drawn with Painter2D.</summary>
    internal sealed class FundLogo : VisualElement
    {
        public int Mark, Colour;
        public bool OnDark;

        public FundLogo(int mark, int colour, float size)
        {
            Mark = mark;
            Colour = colour;
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }

        public void Set(int mark, int colour)
        {
            Mark = mark;
            Colour = colour;
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 2f) return;
            Painter2D p = mgc.painter2D;
            Color brand = FundUi.BrandColour(Colour);
            Vector2 V(float x, float y) => new Vector2(x * w, y * h);
            // A rounded tile in the brand colour, the mark in white.
            p.fillColor = OnDark ? new Color(1, 1, 1, 0.12f) : brand;
            p.BeginPath();
            p.MoveTo(V(0.2f, 0f)); p.LineTo(V(0.8f, 0f)); p.ArcTo(V(1f, 0f), V(1f, 0.2f), 0.2f * w);
            p.LineTo(V(1f, 0.8f)); p.ArcTo(V(1f, 1f), V(0.8f, 1f), 0.2f * w);
            p.LineTo(V(0.2f, 1f)); p.ArcTo(V(0f, 1f), V(0f, 0.8f), 0.2f * w);
            p.LineTo(V(0f, 0.2f)); p.ArcTo(V(0f, 0f), V(0.2f, 0f), 0.2f * w);
            p.ClosePath();
            p.Fill();
            p.fillColor = Color.white;
            p.strokeColor = Color.white;
            p.lineWidth = Mathf.Max(1.5f, w * 0.07f);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            void Rect(float x0, float y0, float x1, float y1)
            {
                p.BeginPath(); p.MoveTo(V(x0, y0)); p.LineTo(V(x1, y0)); p.LineTo(V(x1, y1)); p.LineTo(V(x0, y1)); p.ClosePath(); p.Fill();
            }
            switch (FundBrand.Clamp(Mark, FundBrand.Logos.Length))
            {
                case 0: // Pillars
                    p.BeginPath(); p.MoveTo(V(0.18f, 0.36f)); p.LineTo(V(0.5f, 0.18f)); p.LineTo(V(0.82f, 0.36f)); p.ClosePath(); p.Fill();
                    Rect(0.24f, 0.42f, 0.32f, 0.72f); Rect(0.46f, 0.42f, 0.54f, 0.72f); Rect(0.68f, 0.42f, 0.76f, 0.72f);
                    Rect(0.18f, 0.76f, 0.82f, 0.82f);
                    break;
                case 1: // Ascent
                    Rect(0.2f, 0.58f, 0.32f, 0.8f); Rect(0.44f, 0.44f, 0.56f, 0.8f); Rect(0.68f, 0.26f, 0.8f, 0.8f);
                    break;
                case 2: // Compass
                    p.BeginPath(); p.MoveTo(V(0.5f, 0.14f)); p.LineTo(V(0.58f, 0.42f)); p.LineTo(V(0.86f, 0.5f)); p.LineTo(V(0.58f, 0.58f));
                    p.LineTo(V(0.5f, 0.86f)); p.LineTo(V(0.42f, 0.58f)); p.LineTo(V(0.14f, 0.5f)); p.LineTo(V(0.42f, 0.42f)); p.ClosePath(); p.Fill();
                    break;
                case 3: // Shield
                    p.BeginPath(); p.MoveTo(V(0.24f, 0.2f)); p.LineTo(V(0.76f, 0.2f)); p.LineTo(V(0.76f, 0.5f));
                    p.BezierCurveTo(V(0.76f, 0.7f), V(0.6f, 0.8f), V(0.5f, 0.86f)); p.BezierCurveTo(V(0.4f, 0.8f), V(0.24f, 0.7f), V(0.24f, 0.5f)); p.ClosePath(); p.Stroke();
                    p.BeginPath(); p.MoveTo(V(0.38f, 0.5f)); p.LineTo(V(0.48f, 0.6f)); p.LineTo(V(0.64f, 0.4f)); p.Stroke();
                    break;
                case 4: // Hexagon
                    p.BeginPath();
                    for (int i = 0; i < 6; i++)
                    {
                        float a = Mathf.PI / 3f * i + Mathf.PI / 6f;
                        Vector2 q = V(0.5f + 0.32f * Mathf.Cos(a), 0.5f + 0.32f * Mathf.Sin(a));
                        if (i == 0) p.MoveTo(q); else p.LineTo(q);
                    }
                    p.ClosePath(); p.Stroke();
                    Rect(0.44f, 0.36f, 0.56f, 0.64f);
                    break;
                default: // Arc
                    p.BeginPath(); p.Arc(V(0.5f, 0.72f), 0.3f * w, Angle.Degrees(180f), Angle.Degrees(360f)); p.Stroke();
                    p.BeginPath(); p.Arc(V(0.5f, 0.72f), 0.14f * w, Angle.Degrees(180f), Angle.Degrees(360f)); p.Stroke();
                    break;
            }
        }
    }

    /// <summary>A line chart of a series (equity curve), with a zero/start baseline, drawn with Painter2D.</summary>
    internal sealed class LineChart : VisualElement
    {
        private readonly List<double> _values = new List<double>();
        public Color Stroke = new Color(0.15f, 0.39f, 0.92f);

        public LineChart(float height)
        {
            style.height = height;
            style.flexGrow = 1;
            generateVisualContent += Paint;
        }

        public void Set(IEnumerable<double> values)
        {
            _values.Clear();
            _values.AddRange(values);
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            Painter2D p = mgc.painter2D;
            // Grid.
            p.strokeColor = new Color(0.86f, 0.88f, 0.91f);
            p.lineWidth = 1f;
            for (int i = 1; i < 4; i++)
            {
                p.BeginPath(); p.MoveTo(new Vector2(0, h * i / 4f)); p.LineTo(new Vector2(w, h * i / 4f)); p.Stroke();
            }
            if (_values.Count < 2) return;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double v in _values) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            if (hi - lo < 1e-9) { hi += 1; lo -= 1; }
            double pad = (hi - lo) * 0.08;
            lo -= pad; hi += pad;
            float Y(double v) => (float)(h - (v - lo) / (hi - lo) * h);
            float X(int i) => w * i / (_values.Count - 1);
            // Start line.
            p.strokeColor = new Color(0.6f, 0.64f, 0.7f, 0.8f);
            p.BeginPath(); p.MoveTo(new Vector2(0, Y(_values[0]))); p.LineTo(new Vector2(w, Y(_values[0]))); p.Stroke();
            // Area and line.
            bool up = _values[_values.Count - 1] >= _values[0];
            Color line = up ? new Color(0.09f, 0.64f, 0.29f) : new Color(0.86f, 0.15f, 0.15f);
            p.fillColor = new Color(line.r, line.g, line.b, 0.1f);
            p.BeginPath();
            p.MoveTo(new Vector2(0, h));
            for (int i = 0; i < _values.Count; i++) p.LineTo(new Vector2(X(i), Y(_values[i])));
            p.LineTo(new Vector2(w, h));
            p.ClosePath();
            p.Fill();
            p.strokeColor = line;
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < _values.Count; i++)
                if (i == 0) p.MoveTo(new Vector2(X(i), Y(_values[i]))); else p.LineTo(new Vector2(X(i), Y(_values[i])));
            p.Stroke();
        }
    }

    /// <summary>Bars around zero (daily results): green up, red down.</summary>
    internal sealed class BarChart : VisualElement
    {
        private readonly List<double> _values = new List<double>();

        public BarChart(float height)
        {
            style.height = height;
            style.flexGrow = 1;
            generateVisualContent += Paint;
        }

        public void Set(IEnumerable<double> values)
        {
            _values.Clear();
            _values.AddRange(values);
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f || _values.Count == 0) return;
            Painter2D p = mgc.painter2D;
            double max = 1e-9;
            foreach (double v in _values) max = Math.Max(max, Math.Abs(v));
            float mid = h / 2f, slot = w / _values.Count, bar = Mathf.Max(1f, slot * 0.7f);
            p.strokeColor = new Color(0.75f, 0.78f, 0.82f);
            p.lineWidth = 1f;
            p.BeginPath(); p.MoveTo(new Vector2(0, mid)); p.LineTo(new Vector2(w, mid)); p.Stroke();
            for (int i = 0; i < _values.Count; i++)
            {
                float x = i * slot + (slot - bar) / 2f;
                float len = (float)(Math.Abs(_values[i]) / max * (mid - 2f));
                p.fillColor = _values[i] >= 0 ? new Color(0.09f, 0.64f, 0.29f) : new Color(0.86f, 0.15f, 0.15f);
                float y0 = _values[i] >= 0 ? mid - len : mid, y1 = _values[i] >= 0 ? mid : mid + len;
                p.BeginPath(); p.MoveTo(new Vector2(x, y0)); p.LineTo(new Vector2(x + bar, y0)); p.LineTo(new Vector2(x + bar, y1)); p.LineTo(new Vector2(x, y1)); p.ClosePath(); p.Fill();
            }
        }
    }
}
