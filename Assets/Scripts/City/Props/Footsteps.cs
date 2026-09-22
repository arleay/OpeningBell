using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The player's footsteps, timed by the head bob's footfalls. The surface comes from what's underfoot: rough
    /// ground (the park's lawn) is grass, other tagged ground is paving, untagged floors are indoors (boards).
    /// </summary>
    public sealed class Footsteps : MonoBehaviour
    {
        private const int Variants = 4;

        private FirstPersonController _player;
        private CharacterController _body;
        private AudioSource _source;
        private AudioClip[][] _clips;
        private int _last;
        private readonly RaycastHit[] _hits = new RaycastHit[6];

        public int Played { get; private set; }
        public StepSurface LastSurface { get; private set; }

        public void Configure(FirstPersonController player)
        {
            _player = player;
            _body = player.GetComponent<CharacterController>();
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            var surfaces = (StepSurface[])System.Enum.GetValues(typeof(StepSurface));
            _clips = new AudioClip[surfaces.Length][];
            foreach (StepSurface s in surfaces)
            {
                _clips[(int)s] = new AudioClip[Variants];
                for (int i = 0; i < Variants; i++) _clips[(int)s][i] = ProceduralSounds.Footstep(s, i);
            }
            // Brisker steps land harder; a jump or drop lands with both feet.
            player.Footstep += speed => Step(Mathf.Lerp(0.3f, 0.5f, Mathf.InverseLerp(3f, 5f, speed)));
            player.Landed += impact =>
            {
                if (impact > 2.5f) Step(Mathf.Clamp(impact / 9f, 0.4f, 0.85f));
            };
        }

        private void OnDestroy()
        {
            if (_clips == null) return;
            foreach (AudioClip[] set in _clips)
                foreach (AudioClip clip in set) Destroy(clip);
        }

        private void Step(float volume)
        {
            LastSurface = Underfoot();
            Played++;
            AudioClip[] set = _clips[(int)LastSurface];
            _last = (_last + 1 + Random.Range(0, Variants - 1)) % Variants; // never the same clip twice running
            _source.pitch = Random.Range(0.94f, 1.06f);
            _source.PlayOneShot(set[_last], volume);
        }

        private StepSurface Underfoot()
        {
            Vector3 from = _player.transform.TransformPoint(_body.center);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, _body.height * 0.5f + 0.4f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider ground = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(_player.transform) || _hits[i].distance >= nearest) continue;
                nearest = _hits[i].distance;
                ground = _hits[i].collider;
            }
            if (ground == null) return StepSurface.Hard;
            SurfaceTag tag = ground.GetComponentInParent<SurfaceTag>();
            if (tag == null) return StepSurface.Wood;
            return tag.Roughness >= 0.8f ? StepSurface.Grass : StepSurface.Hard;
        }
    }
}
