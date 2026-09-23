using System.Collections;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// A fixed ladder up a wall (TOWN_SPEC A16). Use it at the foot to climb onto the roof, at the top to climb back
    /// down. The climb is a scripted move up the rungs rather than free movement: a character controller can't grip a
    /// wall, and a scripted climb can't leave the player hanging halfway.
    /// </summary>
    public sealed class Ladder : Interactable
    {
        /// <summary>Metres a second along the rungs.</summary>
        public const float ClimbSpeed = 2.4f;

        private FirstPersonController _player;
        private Vector3 _foot, _roof, _rungs;
        private float _yawToWall;

        public bool Climbing { get; private set; }
        /// <summary>Where you stand at the bottom, and on the roof at the top.</summary>
        public Vector3 Foot => _foot;
        public Vector3 Roof => _roof;

        /// <param name="rungs">The foot of the rung line, against the wall.</param>
        /// <param name="yawToWall">Facing the wall from the foot (and facing into the roof from the top).</param>
        public void Configure(Transform player, Vector3 foot, Vector3 roof, Vector3 rungs, float yawToWall)
        {
            _player = player != null ? player.GetComponent<FirstPersonController>() : null;
            _foot = foot;
            _roof = roof;
            _rungs = rungs;
            _yawToWall = yawToWall;
        }

        private bool PlayerUp => _player != null && _player.transform.position.y > (_foot.y + _roof.y) / 2f;

        public override string Prompt => PlayerUp ? "Climb down" : "Climb up";
        public override bool CanInteract => base.CanInteract && !Climbing && _player != null;

        public override void Interact()
        {
            if (!CanInteract) return;
            StartCoroutine(Climb(!PlayerUp));
        }

        private IEnumerator Climb(bool up)
        {
            Climbing = true;
            _player.Suspended = true;
            var body = _player.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            // Onto the rungs, along them, then off the other end (over the edge at the top: feet clear the parapet).
            Vector3 low = _rungs, high = new Vector3(_rungs.x, _roof.y + 0.6f, _rungs.z);
            Vector3 start = _player.transform.position;
            Vector3[] path = up ? new[] { start, low, high, _roof } : new[] { start, high, low, _foot };
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 from = path[i - 1], to = path[i];
                float len = Mathf.Max(0.01f, Vector3.Distance(from, to));
                for (float t = 0f; t < 1f;)
                {
                    t = Mathf.Min(1f, t + Time.deltaTime * ClimbSpeed / len);
                    _player.transform.position = Vector3.Lerp(from, to, t);
                    yield return null;
                }
            }
            _player.PlaceAt(up ? _roof : _foot, up ? _yawToWall : _yawToWall + 180f);
            _player.Suspended = false;
            Climbing = false;
        }

        /// <summary>Cut off mid-climb (scene unload): put the player back on the ground with control.</summary>
        private void OnDisable()
        {
            if (!Climbing || _player == null) return;
            _player.PlaceAt(_foot, _yawToWall + 180f);
            _player.Suspended = false;
            Climbing = false;
        }
    }
}
