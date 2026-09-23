using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// A recorded engine: for each layer (engine bay, exhaust) the two recordings either side of the live rpm play
    /// together, crossfaded by where the rpm sits between them and pitched by live rpm / recorded rpm, so the note
    /// never stretches far from a real recording. The exhaust carries the load: it swells with the throttle.
    /// </summary>
    public sealed class EngineAudio : MonoBehaviour
    {
        private sealed class Layer
        {
            public AudioClip[] Clips;
            public float[] Rpm;
            public AudioSource Low, High;
        }

        private Layer _engine, _exhaust;
        private AudioSource _startup;
        private float _level;

        public void Configure(EngineSoundSet set)
        {
            _engine = MakeLayer(set.Engine, set.EngineRpm);
            _exhaust = MakeLayer(set.Exhaust, set.ExhaustRpm);
            if (set.Startup != null)
            {
                _startup = Source();
                _startup.clip = set.Startup;
                _startup.loop = false;
            }
        }

        public void StartEngine()
        {
            if (_startup != null) _startup.Play();
            _level = 0f;
        }

        public void Stop()
        {
            foreach (Layer l in new[] { _engine, _exhaust })
                if (l != null)
                {
                    l.Low.Stop();
                    l.High.Stop();
                }
        }

        /// <param name="rpm">Engine speed (already floored at idle by the caller).</param>
        /// <param name="load">Throttle 0–1.</param>
        /// <param name="running">False when the engine has died (no fuel): the note fades out.</param>
        public void Step(float rpm, float load, bool running)
        {
            // Fade in after the starter, out when the engine dies.
            _level = Mathf.MoveTowards(_level, running ? 1f : 0f, Time.deltaTime * (running ? 1.5f : 3f));
            Blend(_engine, rpm, _level * (0.35f + 0.15f * load));
            Blend(_exhaust, rpm, _level * (0.15f + 0.55f * load));
        }

        private static void Blend(Layer l, float rpm, float volume)
        {
            if (l == null || l.Clips.Length == 0) return;
            int i = 0;
            while (i < l.Rpm.Length - 2 && rpm > l.Rpm[i + 1]) i++;
            int j = Mathf.Min(i + 1, l.Rpm.Length - 1);
            float t = j == i ? 0f : Mathf.Clamp01((rpm - l.Rpm[i]) / (l.Rpm[j] - l.Rpm[i]));
            Play(l.Low, l.Clips[i], rpm / l.Rpm[i], volume * (1f - t));
            Play(l.High, l.Clips[j], rpm / l.Rpm[j], volume * t);
        }

        private static void Play(AudioSource s, AudioClip clip, float pitch, float volume)
        {
            if (s.clip != clip)
            {
                // Keep the playhead's place when the pair moves to the next recording, so the note doesn't restart.
                float at = s.isPlaying && s.clip != null ? s.time / s.clip.length : 0f;
                s.clip = clip;
                s.time = at * clip.length;
            }
            if (!s.isPlaying) s.Play();
            s.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            s.volume = volume;
        }

        private Layer MakeLayer(AudioClip[] clips, float[] rpm) =>
            clips == null || clips.Length == 0 ? null : new Layer { Clips = clips, Rpm = rpm, Low = Source(), High = Source() };

        private AudioSource Source()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0.7f;
            s.minDistance = 2f;
            s.maxDistance = 40f;
            s.dopplerLevel = 0f;
            return s;
        }
    }
}
