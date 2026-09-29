using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The casino's sound (CASINO_SPEC §34, §74–75): short spatial cues from a small pool of sources (chips, cards,
    /// reels, wins, the ball), and looping beds per zone: the floor's murmur and a bright pad, the lounge's piano, the
    /// VIP salon's quieter version. Everything scales with the casino volume setting; nothing plays far away.
    /// </summary>
    public sealed class CasinoAudio : MonoBehaviour
    {
        private static CasinoAudio _instance;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly List<(AudioSource Source, float Volume)> _beds = new List<(AudioSource, float)>();
        private Transform _player;

        public static int Played { get; private set; }

        public static CasinoAudio Build(Transform parent, Transform player)
        {
            var go = new GameObject("Casino audio");
            go.transform.SetParent(parent, false);
            _instance = go.AddComponent<CasinoAudio>();
            _instance._player = player;
            for (int i = 0; i < 12; i++)
            {
                var s = new GameObject("Cue").AddComponent<AudioSource>();
                s.transform.SetParent(go.transform, false);
                s.spatialBlend = 1f;
                s.minDistance = 1.5f;
                s.maxDistance = 25f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.playOnAwake = false;
                _instance._pool.Add(s);
            }
            return _instance;
        }

        /// <summary>A looping bed at <paramref name="at"/>, audible within <paramref name="range"/> metres.</summary>
        public void Bed(string name, Vector3 at, AudioClip clip, float volume, float range)
        {
            var s = new GameObject(name).AddComponent<AudioSource>();
            s.transform.SetParent(transform, false);
            s.transform.position = at;
            s.clip = clip;
            s.loop = true;
            s.spatialBlend = 1f;
            s.minDistance = range * 0.3f;
            s.maxDistance = range;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.volume = volume * GameSettings.CasinoVolume;
            s.playOnAwake = false;
            _beds.Add((s, volume));
        }

        /// <summary>Plays a cue at a place. <paramref name="loopFor"/> repeats it for that long (reels turning).</summary>
        public static void Play(AudioClip clip, Vector3 at, float volume, float loopFor = 0f)
        {
            Played++;
            if (_instance == null || clip == null) return;
            AudioSource free = null;
            foreach (AudioSource s in _instance._pool)
                if (!s.isPlaying)
                {
                    free = s;
                    break;
                }
            if (free == null) return; // a busy moment: skip the cue rather than cut one off
            free.transform.position = at;
            free.clip = clip;
            free.volume = volume * GameSettings.CasinoVolume;
            free.loop = loopFor > 0f;
            free.Play();
            if (loopFor > 0f) _instance.StartCoroutine(StopAfter(free, loopFor));
        }

        private static System.Collections.IEnumerator StopAfter(AudioSource s, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            s.loop = false;
            s.Stop();
        }

        private void Update()
        {
            if (_player == null) return;
            // Beds only play near the player: nothing to mix from across town.
            foreach ((AudioSource s, float volume) in _beds)
            {
                bool near = (s.transform.position - _player.position).sqrMagnitude < s.maxDistance * s.maxDistance * 1.2f;
                if (near && !s.isPlaying) s.Play();
                else if (!near && s.isPlaying) s.Stop();
                s.volume = volume * GameSettings.CasinoVolume;
            }
        }
    }
}
