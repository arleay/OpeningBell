using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// A recorded engine: every loop of each layer (engine bay, exhaust) plays continuously on its own source, and
    /// the two recordings either side of the live rpm are faded up (equal-power crossfade) and pitched by live rpm /
    /// recorded rpm; the rest sit silent. Nothing ever swaps clips, so crossing a recording point (a gearshift sweeps
    /// across several) is a smooth blend, not a cut. Rpm and throttle are glided (~0.1 s) because the physics' rpm
    /// jumps on a shift where a real flywheel takes a moment to fall.
    /// </summary>
    public sealed class EngineAudio : MonoBehaviour
    {
        private sealed class Layer
        {
            public float[] Rpm;
            public AudioSource[] Sources;
        }

        /// <summary>Seconds for rpm/throttle to cover ~63% of a change.</summary>
        private const float RpmGlide = 0.09f, LoadGlide = 0.12f;

        private Layer _engine, _exhaust;
        private AudioSource _startup;
        private float _level, _rpm, _load;

        public void Configure(EngineSoundSet set)
        {
            _engine = MakeLayer(set.Engine, set.EngineRpm);
            _exhaust = MakeLayer(set.Exhaust, set.ExhaustRpm);
            if (set.Startup != null)
            {
                _startup = Source(set.Startup);
                _startup.loop = false;
            }
        }

        public void StartEngine()
        {
            if (_startup != null) _startup.Play();
            _level = 0f;
            _rpm = 0f;
            foreach (Layer l in new[] { _engine, _exhaust })
                if (l != null)
                    foreach (AudioSource s in l.Sources)
                    {
                        s.volume = 0f;
                        // Random start points so the loops don't phase against each other.
                        s.time = Random.value * s.clip.length;
                        s.Play();
                    }
        }

        public void Stop()
        {
            foreach (Layer l in new[] { _engine, _exhaust })
                if (l != null)
                    foreach (AudioSource s in l.Sources) s.Stop();
        }

        /// <param name="rpm">Engine speed (already floored at idle by the caller).</param>
        /// <param name="load">Throttle 0–1.</param>
        /// <param name="running">False when the engine has died (no fuel): the note fades out.</param>
        public void Step(float rpm, float load, bool running)
        {
            float dt = Time.deltaTime;
            _rpm = _rpm <= 0f ? rpm : Mathf.Lerp(_rpm, rpm, 1f - Mathf.Exp(-dt / RpmGlide));
            _load = Mathf.Lerp(_load, load, 1f - Mathf.Exp(-dt / LoadGlide));
            // Fade in after the starter, out when the engine dies.
            _level = Mathf.MoveTowards(_level, running ? 1f : 0f, dt * (running ? 1.5f : 3f));
            Blend(_engine, _rpm, _level * (0.35f + 0.15f * _load));
            Blend(_exhaust, _rpm, _level * (0.15f + 0.55f * _load));
        }

        private static void Blend(Layer l, float rpm, float volume)
        {
            if (l == null) return;
            int n = l.Rpm.Length;
            int i = 0;
            while (i < n - 2 && rpm > l.Rpm[i + 1]) i++;
            int j = Mathf.Min(i + 1, n - 1);
            float t = j == i ? 0f : Mathf.Clamp01((rpm - l.Rpm[i]) / (l.Rpm[j] - l.Rpm[i]));
            for (int k = 0; k < n; k++)
            {
                // Equal-power weights: the sum of the pair stays equally loud through the blend.
                float w = k == i ? Mathf.Cos(t * Mathf.PI * 0.5f) : k == j ? Mathf.Sin(t * Mathf.PI * 0.5f) : 0f;
                AudioSource s = l.Sources[k];
                s.volume = volume * w;
                s.pitch = Mathf.Clamp(rpm / l.Rpm[k], 0.5f, 2f);
            }
        }

        private Layer MakeLayer(AudioClip[] clips, float[] rpm)
        {
            if (clips == null || clips.Length == 0) return null;
            var sources = new AudioSource[clips.Length];
            for (int k = 0; k < clips.Length; k++) sources[k] = Source(clips[k]);
            return new Layer { Rpm = rpm, Sources = sources };
        }

        private AudioSource Source(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0.7f;
            s.minDistance = 2f;
            s.maxDistance = 40f;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            return s;
        }
    }
}
