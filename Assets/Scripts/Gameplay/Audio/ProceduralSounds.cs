using System;
using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// TODO(audio): placeholder sounds synthesized at startup so the game has audio feedback before real assets
    /// exist. Replace each clip with a recorded/designed asset later; call sites won't change.
    /// </summary>
    public static class ProceduralSounds
    {
        public const int SampleRate = 44100;

        /// <summary>Inharmonic partials with a long decay: an exchange-style bell.</summary>
        public static AudioClip Bell() => Make("bell", 2.8f, t =>
            Decay(t, 0.9f) * (0.6f * Sine(520f, t) + 0.3f * Sine(520f * 2.76f, t) + 0.15f * Sine(520f * 5.4f, t) * Decay(t, 0.3f)));

        /// <summary>Two quick rising tones: an order filled.</summary>
        public static AudioClip Fill() => Make("fill", 0.26f, t =>
            (t < 0.11f ? Sine(880f, t) * Decay(t, 0.05f) : Sine(1320f, t) * Decay(t - 0.11f, 0.07f)) * 0.5f);

        public static AudioClip NewsPing() => Make("news", 0.35f, t => Sine(1046f, t) * Decay(t, 0.12f) * 0.35f);

        /// <summary>Two falling tones: money went out.</summary>
        public static AudioClip Bill() => Make("bill", 0.5f, t =>
            (t < 0.22f ? Sine(523f, t) * Decay(t, 0.1f) : Sine(392f, t) * Decay(t - 0.22f, 0.12f)) * 0.45f);

        public static AudioClip Mail() => Make("mail", 0.6f, t =>
            (Sine(784f, t) + 0.5f * Sine(1175f, t)) * Decay(t, 0.2f) * 0.3f);

        /// <summary>Elevator arrival: a soft high-low ding.</summary>
        public static AudioClip Chime() => Make("chime", 1.2f, t =>
            (t < 0.35f ? Sine(1318f, t) * Decay(t, 0.25f) : Sine(1046f, t) * Decay(t - 0.35f, 0.3f)) * 0.35f);

        /// <summary>
        /// Engine loop at 1800 rpm (a four-stroke four fires at rpm/30 Hz = 60 Hz): odd-heavy harmonics plus a
        /// little roughness. Pitch it with the revs. Whole cycles, so it loops cleanly.
        /// </summary>
        public static AudioClip Engine()
        {
            var rng = new System.Random(77);
            float rough = 0f;
            return Make("engine", 1f, t =>
            {
                rough = rough * 0.97f + ((float)rng.NextDouble() - 0.5f) * 0.06f;
                return 0.45f * Sine(60f, t) + 0.28f * Sine(120f, t) + 0.2f * Sine(180f, t) + 0.1f * Sine(300f, t) + rough;
            });
        }

        /// <summary>Fridge compressor: mains hum with harmonics. Loops seamlessly (whole number of cycles).</summary>
        public static AudioClip FridgeHum() => Make("fridge-hum", 1f, t =>
            0.5f * Sine(60f, t) + 0.25f * Sine(120f, t) + 0.12f * Sine(180f, t));

        /// <summary>Soft brown noise for room tone / PC fan. Deterministic seed so it never pops between runs.</summary>
        public static AudioClip Noise(string name, float smoothing)
        {
            var rng = new System.Random(1234);
            float state = 0f;
            return Make(name, 4f, _ =>
            {
                state += ((float)rng.NextDouble() * 2f - 1f) * smoothing;
                state *= 0.995f;
                return Mathf.Clamp(state, -1f, 1f);
            });
        }

        private static AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = wave((float)i / SampleRate);
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        private static float Decay(float t, float halfLife) => Mathf.Pow(0.5f, t / halfLife);
    }
}
