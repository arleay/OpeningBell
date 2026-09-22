using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>The phone's apps (home screen order).</summary>
    public enum PhoneAppId { Home, Messages, Calls, Maps, PennyBridge, News }

    /// <summary>
    /// Look and building blocks for the phone: dark-mode system colours, rounded glass panels, text, and the app
    /// icons painted in code (so no image assets are needed).
    /// </summary>
    internal static class PhoneKit
    {
        public static readonly Color Text = new Color(0.96f, 0.96f, 0.97f);
        public static readonly Color Muted = new Color(0.6f, 0.6f, 0.64f);
        public static readonly Color Screen = new Color(0.02f, 0.02f, 0.03f);
        public static readonly Color Card = new Color(0.11f, 0.11f, 0.13f);
        public static readonly Color Separator = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Green = new Color(0.2f, 0.78f, 0.35f);
        public static readonly Color Red = new Color(1f, 0.27f, 0.23f);
        public static readonly Color Blue = new Color(0.04f, 0.52f, 1f);
        public static readonly Color Glass = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color BrokerNavy = new Color(0.05f, 0.12f, 0.24f);

        public static VisualElement Box(VisualElement parent = null, string name = null)
        {
            var e = new VisualElement();
            if (name != null) e.name = name;
            parent?.Add(e);
            return e;
        }

        public static Label Label(VisualElement parent, string text, float size, Color color, bool bold = false)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.whiteSpace = WhiteSpace.Normal;
            parent?.Add(l);
            return l;
        }

        public static void Radius(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;

        public static void Border(VisualElement e, float width, Color color)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = color;
        }

        public static void Pad(VisualElement e, float x, float y)
        {
            e.style.paddingLeft = e.style.paddingRight = x;
            e.style.paddingTop = e.style.paddingBottom = y;
        }

        public static void Absolute(VisualElement e, float? left = null, float? top = null, float? right = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        public static VisualElement Row(VisualElement parent, Justify justify = Justify.SpaceBetween)
        {
            var row = Box(parent);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = justify;
            return row;
        }

        /// <summary>A tappable element (no Button chrome): rounded, with a pressed tint.</summary>
        public static VisualElement Tap(VisualElement e, Action onTap)
        {
            e.RegisterCallback<ClickEvent>(ev =>
            {
                ev.StopPropagation();
                onTap();
            });
            e.RegisterCallback<PointerEnterEvent>(_ => e.style.opacity = 0.85f);
            e.RegisterCallback<PointerLeaveEvent>(_ => e.style.opacity = 1f);
            return e;
        }

        public static VisualElement Pill(VisualElement parent, string text, Color background, Color color, Action onTap, float size = 14f)
        {
            var pill = Box(parent);
            pill.style.backgroundColor = background;
            Radius(pill, 12f);
            Pad(pill, 12f, 7f);
            pill.style.alignItems = Align.Center;
            var l = Label(pill, text, size, color, true);
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (onTap != null) Tap(pill, onTap);
            return pill;
        }

        /// <summary>A text field styled for the dark phone (Unity's default is a light editor-style box).</summary>
        public static TextField Field(VisualElement parent, string value, string name)
        {
            var f = new TextField { value = value, name = name };
            f.style.marginLeft = f.style.marginRight = 0;
            f.style.flexGrow = 1;
            VisualElement input = f.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = new Color(0.17f, 0.17f, 0.19f);
                Border(input, 0f, Color.clear);
                Radius(input, 10f);
                input.style.color = Text;
                input.style.fontSize = 17;
                input.style.paddingLeft = 10;
                input.style.minHeight = 36;
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
            parent.Add(f);
            return f;
        }

        public static Color SignColor(decimal value) => value > 0m ? Green : value < 0m ? Red : Muted;

        // ------------------------------------------------------------------ icons

        public static string Title(PhoneAppId app) => app switch
        {
            PhoneAppId.Messages => "Messages",
            PhoneAppId.Calls => "Phone",
            PhoneAppId.Maps => "Maps",
            PhoneAppId.PennyBridge => "PennyBridge",
            PhoneAppId.News => "News",
            _ => "",
        };

        /// <summary>An app icon: a rounded tile with its glyph painted on top.</summary>
        public static VisualElement Icon(PhoneAppId app, float size)
        {
            var tile = Box();
            tile.name = "icon-" + app;
            tile.style.width = tile.style.height = size;
            Radius(tile, size * 0.24f);
            tile.style.overflow = Overflow.Hidden;
            (Color top, Color bottom) = app switch
            {
                PhoneAppId.Messages => (new Color(0.4f, 0.9f, 0.45f), new Color(0.13f, 0.72f, 0.26f)),
                PhoneAppId.Calls => (new Color(0.4f, 0.9f, 0.45f), new Color(0.13f, 0.72f, 0.26f)),
                PhoneAppId.Maps => (new Color(0.93f, 0.92f, 0.86f), new Color(0.84f, 0.9f, 0.78f)),
                PhoneAppId.PennyBridge => (new Color(0.1f, 0.22f, 0.4f), BrokerNavy),
                PhoneAppId.News => (new Color(1f, 0.33f, 0.38f), new Color(0.93f, 0.16f, 0.25f)),
                _ => (Color.gray, Color.gray),
            };
            tile.generateVisualContent += ctx => Paint(ctx.painter2D, app, size, top, bottom);
            return tile;
        }

        private static void Paint(Painter2D p, PhoneAppId app, float s, Color top, Color bottom)
        {
            // Background: a soft vertical gradient in horizontal bands (Painter2D has no gradients).
            const int bands = 12;
            for (int i = 0; i < bands; i++)
            {
                p.fillColor = Color.Lerp(top, bottom, i / (bands - 1f));
                p.BeginPath();
                p.MoveTo(new Vector2(0f, s * i / bands));
                p.LineTo(new Vector2(s, s * i / bands));
                p.LineTo(new Vector2(s, s * (i + 1) / bands + 0.5f));
                p.LineTo(new Vector2(0f, s * (i + 1) / bands + 0.5f));
                p.ClosePath();
                p.Fill();
            }

            float u = s / 60f; // glyphs are drawn on a 60-unit grid
            switch (app)
            {
                case PhoneAppId.Messages:
                    p.fillColor = Color.white;
                    p.BeginPath();
                    Ellipse(p, new Vector2(30f * u, 28f * u), 19f * u, 15.5f * u);
                    p.Fill();
                    p.BeginPath(); // the tail, bottom left
                    p.MoveTo(new Vector2(17f * u, 36f * u));
                    p.LineTo(new Vector2(13f * u, 46f * u));
                    p.LineTo(new Vector2(25f * u, 41f * u));
                    p.ClosePath();
                    p.Fill();
                    break;

                case PhoneAppId.Calls:
                    // A handset: a thick arc with rounded ends, tilted like the classic glyph.
                    p.strokeColor = Color.white;
                    p.lineWidth = 8.5f * u;
                    p.lineCap = LineCap.Round;
                    p.BeginPath();
                    p.Arc(new Vector2(36f * u, 24f * u), 17f * u, Angle.Degrees(110f), Angle.Degrees(230f));
                    p.Stroke();
                    p.lineWidth = 11f * u;
                    p.BeginPath();
                    p.MoveTo(new Vector2(17.5f * u, 17f * u));
                    p.LineTo(new Vector2(21f * u, 14f * u));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(new Vector2(40f * u, 41f * u));
                    p.LineTo(new Vector2(44f * u, 38f * u));
                    p.Stroke();
                    break;

                case PhoneAppId.Maps:
                    p.strokeColor = Color.white;
                    p.lineWidth = 6f * u;
                    p.BeginPath();
                    p.MoveTo(new Vector2(-2f * u, 44f * u));
                    p.LineTo(new Vector2(62f * u, 20f * u));
                    p.Stroke();
                    p.strokeColor = new Color(0.98f, 0.8f, 0.2f);
                    p.lineWidth = 5f * u;
                    p.BeginPath();
                    p.MoveTo(new Vector2(22f * u, -2f * u));
                    p.LineTo(new Vector2(34f * u, 62f * u));
                    p.Stroke();
                    // A pin.
                    p.fillColor = new Color(0.95f, 0.26f, 0.22f);
                    p.BeginPath();
                    p.Arc(new Vector2(40f * u, 22f * u), 9f * u, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(new Vector2(32f * u, 26f * u));
                    p.LineTo(new Vector2(40f * u, 40f * u));
                    p.LineTo(new Vector2(48f * u, 26f * u));
                    p.ClosePath();
                    p.Fill();
                    p.fillColor = Color.white;
                    p.BeginPath();
                    p.Arc(new Vector2(40f * u, 22f * u), 3.5f * u, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    break;

                case PhoneAppId.PennyBridge:
                    // A bridge arch over a rising line: the broker's mark.
                    p.strokeColor = new Color(0.35f, 0.9f, 0.55f);
                    p.lineWidth = 4.5f * u;
                    p.lineCap = LineCap.Round;
                    p.lineJoin = LineJoin.Round;
                    p.BeginPath();
                    p.MoveTo(new Vector2(11f * u, 44f * u));
                    p.LineTo(new Vector2(23f * u, 33f * u));
                    p.LineTo(new Vector2(31f * u, 38f * u));
                    p.LineTo(new Vector2(48f * u, 18f * u));
                    p.Stroke();
                    p.BeginPath(); // arrow head
                    p.MoveTo(new Vector2(40f * u, 18f * u));
                    p.LineTo(new Vector2(48f * u, 18f * u));
                    p.LineTo(new Vector2(48f * u, 26f * u));
                    p.Stroke();
                    p.strokeColor = new Color(1f, 1f, 1f, 0.85f);
                    p.lineWidth = 3f * u;
                    p.BeginPath();
                    p.Arc(new Vector2(30f * u, 52f * u), 20f * u, Angle.Degrees(200f), Angle.Degrees(340f));
                    p.Stroke();
                    break;

                case PhoneAppId.News:
                    // A bold "N" made of strokes.
                    p.strokeColor = Color.white;
                    p.lineWidth = 8f * u;
                    p.lineCap = LineCap.Butt;
                    p.lineJoin = LineJoin.Miter;
                    p.BeginPath();
                    p.MoveTo(new Vector2(19f * u, 46f * u));
                    p.LineTo(new Vector2(19f * u, 14f * u));
                    p.LineTo(new Vector2(41f * u, 46f * u));
                    p.LineTo(new Vector2(41f * u, 14f * u));
                    p.Stroke();
                    break;
            }
        }

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry)
        {
            const int n = 28;
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var pt = new Vector2(c.x + Mathf.Cos(a) * rx, c.y + Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt);
                else p.LineTo(pt);
            }
            p.ClosePath();
        }

        /// <summary>Round avatar with an initial (message threads, contacts, call screen).</summary>
        public static VisualElement Avatar(VisualElement parent, string name, float size, Color color)
        {
            var a = Box(parent);
            a.style.width = a.style.height = size;
            a.style.flexShrink = 0;
            Radius(a, size / 2f);
            a.style.backgroundColor = color;
            a.style.justifyContent = Justify.Center;
            var l = Label(a, string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(), size * 0.45f, Color.white, true);
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            return a;
        }

        /// <summary>The wallpaper: a tall gradient texture (dusk blue into violet into a warm glow).</summary>
        public static Texture2D Wallpaper()
        {
            const int h = 128;
            var t = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Phone wallpaper" };
            Color a = new Color(0.05f, 0.09f, 0.26f), b = new Color(0.33f, 0.16f, 0.47f), c = new Color(0.93f, 0.49f, 0.35f);
            for (int y = 0; y < h; y++)
            {
                float v = 1f - y / (h - 1f); // texture row 0 is the bottom
                t.SetPixel(0, y, v < 0.6f ? Color.Lerp(a, b, v / 0.6f) : Color.Lerp(b, c, (v - 0.6f) / 0.4f));
            }
            t.Apply();
            return t;
        }
    }
}
