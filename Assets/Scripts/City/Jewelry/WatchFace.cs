using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// A watch that tells the game's time: hour, minute and (sweeping) second hands turned from the clock every frame,
    /// or, on the digital one, the LCD redrawn each minute. Local frame as built by <see cref="Jewelry"/>: dial facing
    /// +y, 12 o'clock at +z, 3 o'clock at +x.
    /// </summary>
    public sealed class WatchFace : MonoBehaviour
    {
        private CityContext _c;
        private JewelryItem _item;
        private Transform _hour, _minute, _second;
        private Texture2D _lcd;
        private int _shownMinute = -1;

        public void Configure(CityContext c, JewelryItem item, Vector3 centre, float radius, Material handMaterial, Material dial)
        {
            _c = c;
            _item = item;
            if (item.Kind == JewelryKind.DigitalWatch)
            {
                // Its own texture: the digits differ from watch to watch.
                _lcd = WatchDial.Blank(item);
                dial.mainTexture = _lcd;
                dial.SetTexture("_BaseMap", _lcd);
                return;
            }
            _hour = Hand("Hour hand", centre, radius * 0.52f, radius * 0.11f, radius * 0.028f, handMaterial);
            _minute = Hand("Minute hand", centre + Vector3.up * (radius * 0.03f), radius * 0.8f, radius * 0.07f, radius * 0.026f, handMaterial);
            if (item.Kind != JewelryKind.DressWatch)
                _second = Hand("Second hand", centre + Vector3.up * (radius * 0.055f), radius * 0.86f, radius * 0.022f, radius * 0.018f,
                    c.P.Lit(item.Kind == JewelryKind.DiverWatch ? new Color(0.85f, 0.2f, 0.12f) : new Color(0.9f, 0.9f, 0.88f), 0.7f));
            // Centre cap over the hands.
            var cap = new Forge(1);
            cap.Lathe(centre + Vector3.up * (radius * 0.07f), Vector3.up, new[] { new Vector2(radius * 0.06f, 0f), new Vector2(radius * 0.05f, radius * 0.02f), new Vector2(0f, radius * 0.025f) }, 12);
            cap.Build(transform, "Cap", handMaterial);
            Update();
        }

        /// <summary>A copy of another watch (a mirror's double) that shows the same time.</summary>
        public void Follow(WatchFace original)
        {
            _c = original._c;
            _item = original._item;
            _hour = transform.Find("Hour hand");
            _minute = transform.Find("Minute hand");
            _second = transform.Find("Second hand");
            if (_item != null && _item.Kind == JewelryKind.DigitalWatch) enabled = false; // shares the original's LCD material
        }

        /// <summary>A tapered hand from the centre toward 12 (+z), with a short tail; turned about +y to show the time.</summary>
        private Transform Hand(string name, Vector3 centre, float length, float width, float thickness, Material m)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = centre;
            var f = new Forge(1);
            f.Block(new Vector3(0f, 0f, length * 0.4f), Vector3.right, Vector3.up, new Vector3(width, thickness, length * 1.0f));
            f.Block(new Vector3(0f, 0f, length * 0.88f), Vector3.right, Vector3.up, new Vector3(width * 0.55f, thickness, length * 0.24f));
            f.Block(new Vector3(0f, 0f, -length * 0.12f), Vector3.right, Vector3.up, new Vector3(width * 0.8f, thickness, length * 0.2f));
            f.Build(pivot, name, m);
            return pivot;
        }

        private void Update()
        {
            if (_c?.Game?.Clock == null || (_lcd == null && _hour == null)) return;
            System.DateTime now = _c.Game.Clock.Now;
            if (_lcd != null)
            {
                if (now.Minute == _shownMinute) return;
                _shownMinute = now.Minute;
                WatchDial.DrawDigits(_lcd, _item, now.Hour, now.Minute);
                return;
            }
            float seconds = now.Second + now.Millisecond / 1000f;
            float minutes = now.Minute + seconds / 60f;
            float hours = now.Hour % 12 + minutes / 60f;
            // Positive yaw is clockwise seen from above the dial: 12 (+z) toward 3 (+x).
            _hour.localRotation = Quaternion.Euler(0f, hours * 30f, 0f);
            _minute.localRotation = Quaternion.Euler(0f, minutes * 6f, 0f);
            if (_second != null) _second.localRotation = Quaternion.Euler(0f, seconds * 6f, 0f);
        }

        private void OnDestroy()
        {
            if (_lcd != null) Destroy(_lcd);
        }
    }

    /// <summary>
    /// Watch dials painted into textures. The dial disc's UVs run u toward 3 o'clock and v toward 6, so a point
    /// (right, up) on the face, in dial radii from the centre, is at uv (0.5 + right/2, 0.5 - up/2).
    /// </summary>
    public static class WatchDial
    {
        private const int Size = 256;
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Face(JewelryItem item)
        {
            if (item.Kind == JewelryKind.DigitalWatch) return Blank(item);
            if (Cache.TryGetValue(item.Id, out Texture2D t) && t != null) return t;
            var px = new Color32[Size * Size];
            Color bg = item.Accent;
            Color marker = item.Kind == JewelryKind.DressWatch ? Jewelry.MetalColor(item.Metal) * 0.8f
                : item.Kind == JewelryKind.IcedWatch ? new Color(1f, 1f, 1f) : new Color(0.95f, 0.95f, 0.92f);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Dial coordinates: right toward 3, up toward 12 (v runs toward 6, so up = -(v - 0.5)).
                float right = (x + 0.5f) / Size * 2f - 1f, up = 1f - (y + 0.5f) / Size * 2f;
                float r = Mathf.Sqrt(right * right + up * up);
                float angle = Mathf.Atan2(right, up) * Mathf.Rad2Deg; // 0 at 12, clockwise
                if (angle < 0f) angle += 360f;
                Color c = bg;
                // Sunburst: brightness varying with angle, the way brushed dials catch light.
                if (item.Kind == JewelryKind.Chronograph || item.Kind == JewelryKind.DressWatch)
                    c *= 0.9f + 0.1f * Mathf.Cos(angle * Mathf.Deg2Rad * 24f) * r;
                // Minute track: fine ticks round the edge.
                float minuteAngle = Mathf.Repeat(angle + 3f, 6f) - 3f;
                if (r > 0.86f && r < 0.95f && Mathf.Abs(minuteAngle) < 0.7f) c = marker * 0.85f;
                // Hour markers: batons (a double one at 12), lume dots on the diver, stones on the iced dial.
                float hourAngle = Mathf.Repeat(angle + 15f, 30f) - 15f;
                float halfWidth = (angle < 15f || angle > 345f) ? 4.5f : 2.4f;
                bool baton = r > 0.66f && r < 0.86f && Mathf.Abs(hourAngle) < halfWidth * (0.8f / Mathf.Max(0.3f, r));
                if (item.Kind == JewelryKind.DiverWatch)
                {
                    float hx = Mathf.Sin(Mathf.Round(angle / 30f) * 30f * Mathf.Deg2Rad) * 0.76f, hy = Mathf.Cos(Mathf.Round(angle / 30f) * 30f * Mathf.Deg2Rad) * 0.76f;
                    float d = Mathf.Sqrt((right - hx) * (right - hx) + (up - hy) * (up - hy));
                    if (d < 0.075f) c = d > 0.06f ? new Color(0.75f, 0.75f, 0.72f) : new Color(0.85f, 0.95f, 0.8f); // lume in a surround
                }
                else if (baton) c = marker;
                if (item.Kind == JewelryKind.IcedWatch && r < 0.62f)
                {
                    // Pavé dial: a grid of tiny facets glinting at random.
                    int gx = Mathf.FloorToInt((right + 1f) * 22f), gy = Mathf.FloorToInt((up + 1f) * 22f);
                    float glint = Mathf.Abs(Mathf.Sin(gx * 12.9898f + gy * 78.233f) * 43758.5453f) % 1f;
                    c = Color.Lerp(new Color(0.78f, 0.82f, 0.9f), Color.white, glint);
                }
                if (item.Kind == JewelryKind.Chronograph)
                    foreach (float sa in new[] { 90f, 180f, 270f })
                    {
                        float sx = Mathf.Sin(sa * Mathf.Deg2Rad) * 0.42f, sy = Mathf.Cos(sa * Mathf.Deg2Rad) * 0.42f;
                        float d = Mathf.Sqrt((right - sx) * (right - sx) + (up - sy) * (up - sy));
                        if (d < 0.2f) c = bg * 0.7f;
                        if (d > 0.18f && d < 0.2f) c = marker * 0.8f;
                        // A little hand in each sub-dial, pointing somewhere plausible.
                        float ha = sa * 1.7f;
                        Vector2 dir = new Vector2(Mathf.Sin(ha * Mathf.Deg2Rad), Mathf.Cos(ha * Mathf.Deg2Rad));
                        Vector2 rel = new Vector2(right - sx, up - sy);
                        float along = Vector2.Dot(rel, dir), off = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                        if (along > 0f && along < 0.16f && off < 0.012f) c = marker;
                    }
                // Date window at 3 o'clock on the diver and chrono.
                if ((item.Kind == JewelryKind.DiverWatch) && right > 0.5f && right < 0.66f && Mathf.Abs(up) < 0.07f)
                    c = Mathf.Abs(up) > 0.055f || right < 0.515f || right > 0.645f ? marker * 0.7f : Color.white;
                // Brand line under 12: a thin bar standing in for the name.
                if (r > 0.3f && r < 0.46f && Mathf.Abs(right) < 0.16f && up > 0.3f && up < 0.36f) c = marker * 0.9f;
                c.a = 1f;
                px[y * Size + x] = c;
            }
            t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Dial " + item.Id, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true);
            Cache[item.Id] = t;
            return t;
        }

        public static Texture2D Blank(JewelryItem item)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "LCD", wrapMode = TextureWrapMode.Clamp };
            DrawDigits(t, item, 12, 0);
            return t;
        }

        /// <summary>
        /// A digital watch face: grey-green LCD, HH:MM in seven-segment digits (unlit segments faintly visible, as on a
        /// real LCD), "AM/PM" pip. Drawn upright when the dial is read with 12 at the top.
        /// </summary>
        public static void DrawDigits(Texture2D t, JewelryItem item, int hour, int minute)
        {
            var px = new Color32[Size * Size];
            Color lcd = item.Accent, on = new Color(0.08f, 0.09f, 0.08f), off = Color.Lerp(item.Accent, on, 0.12f);
            int h12 = hour % 12 == 0 ? 12 : hour % 12;
            int[] digits = { h12 / 10, h12 % 10, minute / 10, minute % 10 };
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float right = (x + 0.5f) / Size * 2f - 1f, up = 1f - (y + 0.5f) / Size * 2f;
                Color c = Mathf.Abs(right) < 0.8f && Mathf.Abs(up) < 0.5f ? lcd : new Color(0.07f, 0.07f, 0.08f);
                if (Mathf.Abs(right) < 0.8f && Mathf.Abs(up) < 0.5f)
                {
                    // Four digit cells across, a colon in the middle.
                    for (int d = 0; d < 4; d++)
                    {
                        if (d == 0 && digits[0] == 0) continue;
                        float cx = -0.6f + d * 0.36f + (d >= 2 ? 0.1f : 0f);
                        int seg = Segment((right - cx) / 0.14f, up / 0.3f);
                        if (seg >= 0) c = (Segments[digits[d]] & (1 << seg)) != 0 ? on : off;
                    }
                    if (Mathf.Abs(right - 0.02f) < 0.025f && (Mathf.Abs(up - 0.12f) < 0.03f || Mathf.Abs(up + 0.12f) < 0.03f)) c = on;
                    if (hour >= 12 && right > 0.6f && right < 0.72f && up > 0.3f && up < 0.4f) c = on; // PM
                }
                c.a = 1f;
                px[y * Size + x] = c;
            }
            t.SetPixels32(px);
            t.Apply(false);
        }

        // Seven segments as bits: 0 top, 1 top-right, 2 bottom-right, 3 bottom, 4 bottom-left, 5 top-left, 6 middle.
        private static readonly int[] Segments = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

        /// <summary>Which segment a point in a digit cell (x, y in [-1, 1], y up) falls on, or -1.</summary>
        private static int Segment(float x, float y)
        {
            const float t = 0.2f; // stroke half-width
            if (Mathf.Abs(x) > 1f + t || Mathf.Abs(y) > 1f + t) return -1;
            bool horizontal = Mathf.Abs(x) < 0.8f;
            if (horizontal && Mathf.Abs(y - 1f) < t) return 0;
            if (horizontal && Mathf.Abs(y) < t) return 6;
            if (horizontal && Mathf.Abs(y + 1f) < t) return 3;
            bool vertical = Mathf.Abs(Mathf.Abs(x) - 1f) < t;
            if (vertical && y > 0.15f && y < 0.9f) return x > 0f ? 1 : 5;
            if (vertical && y < -0.15f && y > -0.9f) return x > 0f ? 2 : 4;
            return -1;
        }
    }
}
