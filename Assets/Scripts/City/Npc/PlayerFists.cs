using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Punching (left mouse): jab and cross in turn. The strike lands when the animated fist would, as a short sphere
    /// cast from the eyes. Anyone hit reacts (<see cref="NpcFighter"/>); some fight back, most run, and people nearby
    /// may scatter. No consequences beyond that yet.
    /// </summary>
    public sealed class PlayerFists : MonoBehaviour
    {
        private const float Range = 1.5f, Radius = 0.28f;
        private static readonly int Targets = 1 << 0 | 1 << CityLayers.Pedestrian;

        private FirstPersonController _player;
        private PlayerBody _body;
        private PedestrianView _pedestrians;
        private Camera _camera;
        private AudioSource _audio;
        private AudioClip _whoosh;
        private readonly System.Random _rng = new System.Random(4401);
        private readonly List<NpcBody> _witnesses = new List<NpcBody>();
        private bool _cross;
        private float _ready, _landAt = -1f;

        /// <summary>Raised when a punch connects with someone.</summary>
        public event System.Action<NpcBody> Hit;

        public void Configure(FirstPersonController player, PlayerBody body, PedestrianView pedestrians)
        {
            _player = player;
            _body = body;
            _pedestrians = pedestrians;
            _camera = player.CameraPivot.GetComponentInChildren<Camera>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 0f;
            _whoosh = ProceduralSounds.Whoosh();
        }

        private void Update()
        {
            if (_player == null || !_player.ControlEnabled || _player.Suspended || _player.HandsFull) return;
            if (_player.Input.Attack.WasPressedThisFrame()) Throw();
            if (_landAt > 0f && Time.time >= _landAt)
            {
                _landAt = -1f;
                Strike();
            }
        }

        /// <summary>Starts a punch if the last one has finished.</summary>
        public void Throw()
        {
            if (Time.time < _ready) return;
            _cross = !_cross;
            float delay = _body != null ? _body.Punch(_cross) : 0.15f;
            _landAt = Time.time + delay;
            _ready = Time.time + (_cross ? 0.5f : 0.38f);
            _audio.PlayOneShot(_whoosh, 0.5f);
        }

        /// <summary>Resolves the punch now; returns who was hit, if anyone.</summary>
        public NpcBody Strike()
        {
            Transform eye = _camera.transform;
            RaycastHit[] hits = Physics.SphereCastAll(eye.position, Radius, eye.forward, Range, Targets, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(_player.transform)) continue;
                NpcBody target = hit.collider.GetComponentInParent<NpcBody>();
                if (target == null) return null; // a wall or a car in the way
                Land(target, eye.position);
                return target;
            }
            return null;
        }

        private void Land(NpcBody target, Vector3 from)
        {
            if (!target.TryGetComponent(out NpcFighter fighter))
            {
                // A third of people hit back; staff and riders (not walkers) only react where they are.
                bool brave = _rng.NextDouble() < 0.35;
                fighter = _pedestrians != null ? _pedestrians.Provoke(target, _player, brave) : null;
                if (fighter == null) fighter = NpcFighter.Engage(target, _player, mobile: false, brave: false, null);
            }
            float damage = (_cross ? 30f : 22f) * (0.85f + 0.3f * (float)_rng.NextDouble());
            fighter.TakeHit(damage, from);
            _player.Jolt(0.25f, 0f, Vector3.zero); // a little recoil in the view
            Hit?.Invoke(target);

            // Bystanders: most who saw it get out of the way.
            if (_pedestrians == null) return;
            _witnesses.Clear();
            _pedestrians.Near(target.transform.position, 8f, _witnesses);
            foreach (NpcBody w in _witnesses)
            {
                if (w == target || _rng.NextDouble() > 0.6) continue;
                _pedestrians.Provoke(w, _player, brave: false)?.Scare();
            }
        }
    }
}
