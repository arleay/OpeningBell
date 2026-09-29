using System;
using System.Collections.Generic;
using OpeningBell.Casino;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>Fresh entropy for the casino's random streams (CASINO_SPEC §15): nothing a player could learn or replay.</summary>
    public static class CasinoRandom
    {
        public static ulong FreshSeed()
        {
            var bytes = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }
    }

    /// <summary>
    /// Pictures and sounds the casino paints for itself at start-up: reel symbols (cherries, BAR, 7, diamond), and
    /// short synthesized cues (reels, chips, wins, the roulette ball). Fictional artwork, no brands (§29).
    /// </summary>
    public static class CasinoArt
    {
        private static readonly Dictionary<SlotSymbol, Material> Symbols = new Dictionary<SlotSymbol, Material>();

        /// <summary>An unlit (screen-bright) material showing <paramref name="s"/> on the reel's cream background.</summary>
        public static Material Symbol(Palette p, SlotSymbol s)
        {
            if (Symbols.TryGetValue(s, out Material m) && m != null) return m;
            m = new Material(p.Unlit(Color.white)) { name = "Reel " + s, mainTexture = SymbolTexture(s) };
            return Symbols[s] = m;
        }

        // A 5×7 pixel font for the few letters the reels need.
        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['B'] = new[] { "1111.", "1...1", "1...1", "1111.", "1...1", "1...1", "1111." },
            ['A'] = new[] { ".111.", "1...1", "1...1", "11111", "1...1", "1...1", "1...1" },
            ['R'] = new[] { "1111.", "1...1", "1...1", "1111.", "1.1..", "1..1.", "1...1" },
        };

        public static Texture2D SymbolTexture(SlotSymbol s)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Reel " + s, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            Color cream = new Color(0.97f, 0.95f, 0.88f);
            for (int i = 0; i < px.Length; i++) px[i] = cream;

            void Set(int x, int y, Color c)
            {
                if (x >= 0 && x < n && y >= 0 && y < n) px[y * n + x] = c;
            }
            void Rect(int x0, int y0, int x1, int y1, Color c)
            {
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++) Set(x, y, c);
            }
            void Disc(float cx, float cy, float r, Color c)
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if ((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r) Set(x, y, c);
            }
            void Line(Vector2 a, Vector2 b, float w, Color c)
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f), ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                        if ((a + ab * t - p).magnitude <= w / 2f) Set(x, y, c);
                    }
            }
            void Bar(int yCentre, int h)
            {
                // A black bar with rounded ends and "BAR" knocked out in cream.
                Rect(10, yCentre - h / 2, 53, yCentre + h / 2, new Color(0.07f, 0.07f, 0.08f));
                int scale = h >= 16 ? 2 : 1, x = 32 - (3 * 6 * scale - scale) / 2, top = yCentre + 7 * scale / 2;
                foreach (char ch in "BAR")
                {
                    string[] g = Glyphs[ch];
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                            if (g[gy][gx] == '1') Rect(x + gx * scale, top - (gy + 1) * scale + 1, x + gx * scale + scale - 1, top - gy * scale, cream);
                    x += 6 * scale;
                }
            }

            switch (s)
            {
                case SlotSymbol.Cherry:
                    Line(new Vector2(22, 30), new Vector2(36, 52), 3f, new Color(0.2f, 0.55f, 0.15f));
                    Line(new Vector2(42, 26), new Vector2(36, 52), 3f, new Color(0.2f, 0.55f, 0.15f));
                    Disc(20, 22, 10, new Color(0.8f, 0.05f, 0.1f));
                    Disc(43, 19, 10, new Color(0.85f, 0.08f, 0.12f));
                    Disc(17, 26, 3, new Color(1f, 0.6f, 0.6f));
                    Disc(40, 23, 3, new Color(1f, 0.6f, 0.6f));
                    break;
                case SlotSymbol.Bar:
                    Bar(32, 18);
                    break;
                case SlotSymbol.DoubleBar:
                    Bar(43, 12);
                    Bar(21, 12);
                    break;
                case SlotSymbol.TripleBar:
                    Bar(50, 9);
                    Bar(32, 9);
                    Bar(14, 9);
                    break;
                case SlotSymbol.Seven:
                    Color red = new Color(0.85f, 0.06f, 0.08f), gold = new Color(0.95f, 0.75f, 0.2f);
                    Line(new Vector2(16, 52), new Vector2(49, 52), 11f, gold);
                    Line(new Vector2(47, 52), new Vector2(27, 9), 12f, gold);
                    Line(new Vector2(16, 52), new Vector2(49, 52), 7f, red);
                    Line(new Vector2(47, 52), new Vector2(27, 9), 8f, red);
                    break;
                case SlotSymbol.Diamond:
                    Color ice = new Color(0.45f, 0.85f, 1f), deep = new Color(0.1f, 0.45f, 0.85f);
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float dx = Mathf.Abs(x + 0.5f - 32f), dy = y + 0.5f - 34f;
                            bool crown = dy >= 0 && dy <= 12 && dx <= 22 - dy * 0.6f;
                            bool pavilion = dy < 0 && dx <= 22 + dy * (22f / 26f);
                            if (crown || pavilion) Set(x, y, ((x / 6 + y / 6) % 2 == 0) ? ice : deep);
                        }
                    Line(new Vector2(12, 34), new Vector2(52, 34), 1.5f, Color.white);
                    break;
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>The roulette wheel's pocket ring: coloured wedges in wheel order, the numbers are separate text.</summary>
        public static Texture2D WheelTexture(bool doubleZero)
        {
            int[] order = doubleZero ? Roulette.AmericanWheel : Roulette.EuropeanWheel;
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Wheel", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            Color wood = new Color(0.35f, 0.18f, 0.08f), metal = new Color(0.8f, 0.7f, 0.4f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    Color c = Color.clear;
                    if (r <= 1f)
                    {
                        // Angle measured the same way the numbers are placed (clockwise from +z, seen from above).
                        float a = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                        if (a < 0) a += 360f;
                        int pocket = order[(int)(a / (360f / order.Length) + 0.5f) % order.Length];
                        if (r > 0.72f && r <= 0.95f) c = Roulette.IsGreen(pocket) ? new Color(0.05f, 0.45f, 0.2f) : Roulette.IsRed(pocket) ? new Color(0.7f, 0.05f, 0.07f) : new Color(0.06f, 0.06f, 0.07f);
                        else if (r > 0.95f) c = wood;
                        else if (r > 0.55f) c = Color.Lerp(wood, new Color(0.5f, 0.3f, 0.12f), 0.5f);
                        else if (r > 0.12f) c = new Color(0.28f, 0.14f, 0.06f);
                        else c = metal;
                    }
                    px[y * n + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // ---------------------------------------------------------------- sounds

        private const int Rate = 44100;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        private static AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            if (Clips.TryGetValue(name, out AudioClip c) && c != null) return c;
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(wave((float)i / Rate), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return Clips[name] = clip;
        }

        private static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        private static float Decay(float t, float half) => Mathf.Pow(0.5f, t / half);
        // A full integer hash (lowbias32), not a single LCG step: consecutive LCG outputs differ by a constant, so
        // sample-by-sample they form a sawtooth whose harmonics alias to an audible ~1.2 kHz whine.
        private static float Noise(int i)
        {
            uint x = (uint)i;
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x / (float)uint.MaxValue * 2f - 1f;
        }

        /// <summary>Reels turning: a soft ratchet ticking along.</summary>
        public static AudioClip ReelSpin() => Make("reel-spin", 0.4f, t =>
        {
            float tick = (t * 22f) % 1f;
            return Noise((int)(t * Rate)) * Decay(tick / 22f, 0.004f) * 0.25f;
        });

        public static AudioClip ReelStop() => Make("reel-stop", 0.12f, t => (Sine(140f, t) * 0.6f + Noise((int)(t * Rate)) * 0.3f) * Decay(t, 0.02f) * 0.5f);

        public static AudioClip Button() => Make("button", 0.05f, t => Sine(1800f, t) * Decay(t, 0.008f) * 0.25f);

        /// <summary>A little rising arpeggio for a win; <paramref name="big"/> makes it a longer fanfare.</summary>
        public static AudioClip Win(bool big) => Make(big ? "win-big" : "win", big ? 1.8f : 0.6f, t =>
        {
            float[] notes = big ? new[] { 523f, 659f, 784f, 1046f, 784f, 1046f, 1318f } : new[] { 659f, 784f, 1046f };
            float step = big ? 0.22f : 0.15f;
            int i = Mathf.Min(notes.Length - 1, (int)(t / step));
            float local = t - i * step;
            return (Sine(notes[i], t) * 0.5f + Sine(notes[i] * 2f, t) * 0.15f) * Decay(local, 0.12f) * 0.4f;
        });

        /// <summary>Chips set down on felt: a couple of clay clicks.</summary>
        public static AudioClip Chips() => Make("chips", 0.18f, t =>
        {
            float a = Noise((int)(t * Rate)) * 0.4f + Sine(2400f, t) * 0.3f;
            return a * (Decay(t, 0.01f) + (t > 0.06f ? Decay(t - 0.06f, 0.01f) * 0.7f : 0f)) * 0.5f;
        });

        public static AudioClip Card() => Make("card", 0.09f, t => Noise((int)(t * Rate) * 7) * Decay(t, 0.015f) * 0.3f * Mathf.Min(1f, t * 400f));

        /// <summary>The ball running round the wheel, and dropping into a pocket.</summary>
        public static AudioClip BallRoll() => Make("ball-roll", 1f, t => Noise((int)(t * Rate) * 3) * 0.12f * (0.7f + 0.3f * Sine(9f, t)));
        public static AudioClip BallDrop() => Make("ball-drop", 0.5f, t =>
        {
            float bounce = (t < 0.1f ? Decay(t, 0.01f) : 0f) + (t > 0.14f && t < 0.24f ? Decay(t - 0.14f, 0.01f) * 0.6f : 0f) + (t > 0.28f ? Decay(t - 0.28f, 0.01f) * 0.35f : 0f);
            return Sine(2200f, t) * bounce * 0.4f;
        });

        /// <summary>The room: a low murmur of voices and distant machines (loops).</summary>
        public static AudioClip Murmur() => Make("murmur", 4f, t =>
        {
            int i = (int)(t * Rate);
            float n = (Noise(i) + Noise(i + 1) + Noise(i + 2) + Noise(i + 3)) * 0.25f;
            float voices = 0.5f + 0.5f * Sine(1.3f, t) * Sine(0.7f, t);
            float fade = Mathf.Min(1f, Mathf.Min(t, 4f - t) * 4f);
            return n * 0.18f * voices * fade;
        });

        /// <summary>
        /// A slow original chord loop (no copyrighted music, §75): <paramref name="soft"/> is the lounge piano, otherwise a
        /// brighter pad for the floor.
        /// </summary>
        public static AudioClip Music(bool soft) => Make(soft ? "music-lounge" : "music-floor", 8f, t =>
        {
            float[][] chords = soft
                ? new[] { new[] { 220f, 277.2f, 329.6f, 415.3f }, new[] { 196f, 246.9f, 293.7f, 370f }, new[] { 174.6f, 220f, 261.6f, 329.6f }, new[] { 164.8f, 207.7f, 246.9f, 311.1f } }
                : new[] { new[] { 261.6f, 329.6f, 392f }, new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 293.7f } };
            int c = Mathf.Min(3, (int)(t / 2f));
            float local = t - c * 2f;
            float v = 0f;
            foreach (float hz in chords[c])
                v += Sine(hz, t) * Decay(local, soft ? 0.7f : 0.45f); // struck, not held: sustained sines ring
            float edge = Mathf.Min(1f, Mathf.Min(local, 2f - local) * 20f);
            return v * (soft ? 0.12f : 0.05f) * edge;
        });
    }
}
