using System;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Drives a person who has been punched (or saw it happen): hit reactions, a knockdown and getting back up,
    /// then running off or fighting back. Fighters chase the player at a jog (slower than a sprint), heading for
    /// where they last saw them; out of sight for a while, or far enough away, they give up. Bodies that can't
    /// leave their post (staff) only react in place. When it's over the body is handed back to its owner.
    /// </summary>
    public sealed class NpcFighter : MonoBehaviour
    {
        public enum Mood { Stagger, Down, GettingUp, Flee, Fight, Calm }

        private const float Health = 100f;
        private const float JogSpeed = 3.7f, FleeSpeed = 4.2f, CalmSpeed = 1.3f;
        private const float Reach = 1.25f, SightRange = 35f;
        /// <summary>Give up after this long without seeing the player...</summary>
        private const float ForgetAfter = 5f;
        /// <summary>...or once this far away, seen or not.</summary>
        private const float GiveUpDistance = 30f;

        private static readonly int Obstacles = 1 << 0 | 1 << CityLayers.Vehicle;
        private static AudioClip[] _punchClips;

        private NpcBody _body;
        private FirstPersonController _player;
        private bool _mobile, _brave;
        private Action<NpcFighter> _done;
        private float _health = Health;
        private float _timer, _calmTimer;
        private Vector3 _knock;
        private Vector3 _lastSeen;
        private float _lastSeenTime = -99f, _nextSight;
        private float _swingReady, _landAt = -1f, _upperUntil, _upper;
        private bool _cross;
        private AudioSource _audio;

        public Mood Current { get; private set; }
        public bool IsDown => Current == Mood.Down || Current == Mood.GettingUp;

        /// <summary>Takes over <paramref name="body"/>. <paramref name="done"/> gets it back when the fight is over.</summary>
        public static NpcFighter Engage(NpcBody body, FirstPersonController player, bool mobile, bool brave, Action<NpcFighter> done)
        {
            if (body.TryGetComponent(out NpcFighter existing)) return existing;
            var f = body.gameObject.AddComponent<NpcFighter>();
            f._body = body;
            f._player = player;
            f._mobile = mobile;
            f._brave = brave;
            f._done = done;
            f.Current = Mood.Calm;
            f._calmTimer = 0f;
            body.Overridden = true;
            f._audio = body.gameObject.AddComponent<AudioSource>();
            f._audio.spatialBlend = 1f;
            f._audio.maxDistance = 25f;
            f._audio.rolloffMode = AudioRolloffMode.Linear;
            if (_punchClips == null)
            {
                _punchClips = new AudioClip[4];
                for (int i = 0; i < _punchClips.Length; i++) _punchClips[i] = ProceduralSounds.Punch(i);
            }
            return f;
        }

        /// <summary>A punch from <paramref name="from"/>. Returns true if it knocked them down.</summary>
        public bool TakeHit(float damage, Vector3 from)
        {
            _audio.PlayOneShot(_punchClips[UnityEngine.Random.Range(0, _punchClips.Length)], 0.9f);
            if (IsDown) return false;
            Vector3 away = transform.position - from;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
            _health -= damage;
            _landAt = -1f; // a hit interrupts their own swing
            float push = _mobile ? 1f : 0f; // staff stay at their post
            if (_health <= 0f)
            {
                Current = Mood.Down;
                _timer = 5f + UnityEngine.Random.value * 2f;
                _knock = away * 2.2f * push;
                transform.rotation = Quaternion.LookRotation(-away); // fall backwards, away from the blow
                _body.PlayState("Fall", 0.08f);
                return true;
            }
            Current = Mood.Stagger;
            _timer = 0.55f;
            _knock = away * 1.6f * push;
            _body.PlayState(UnityEngine.Random.value < 0.5f ? "HitHead" : "HitChest", 0.05f);
            return false;
        }

        /// <summary>Saw something frightening nearby: run from the player.</summary>
        public void Scare()
        {
            if (IsDown || Current == Mood.Fight || !_mobile) return;
            Current = Mood.Flee;
            _timer = 5f + UnityEngine.Random.value * 3f;
        }

        private void AfterReaction()
        {
            if (!_mobile)
            {
                Finish();
                return;
            }
            if (_brave)
            {
                Current = Mood.Fight;
                _lastSeen = _player.transform.position;
                _lastSeenTime = Time.time;
            }
            else
            {
                Current = Mood.Flee;
                _timer = 6f + UnityEngine.Random.value * 3f;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 toPlayer = _player.transform.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (_knock.sqrMagnitude > 1e-4f)
            {
                Move(_knock, dt, steer: false);
                _knock = Vector3.MoveTowards(_knock, Vector3.zero, 6f * dt);
            }

            switch (Current)
            {
                case Mood.Stagger:
                    if ((_timer -= dt) <= 0f) AfterReaction();
                    break;
                case Mood.Down:
                    if ((_timer -= dt) <= 0f)
                    {
                        Current = Mood.GettingUp;
                        _timer = 1.6f;
                        _health = Health * 0.6f;
                        // Knocked down twice is enough for most people.
                        if (_brave && UnityEngine.Random.value < 0.5f) _brave = false;
                        _body.PlayState("GetUp", 0.1f, 1f);
                    }
                    break;
                case Mood.GettingUp:
                    if ((_timer -= dt) <= 0f) AfterReaction();
                    break;
                case Mood.Flee:
                    Locomote(Move(-toPlayer, dt, steer: true, FleeSpeed), true);
                    if ((_timer -= dt) <= 0f || distance > 25f) Calm();
                    break;
                case Mood.Fight:
                    Fight(toPlayer, distance, dt);
                    break;
                case Mood.Calm:
                    // Walk on, away from the player, and leave once out of the way.
                    Locomote(Move(distance > 0.1f ? -toPlayer : transform.forward, dt, steer: true, CalmSpeed), false);
                    _calmTimer += dt;
                    if (_calmTimer > 20f || distance > 40f) Finish();
                    break;
            }
            UpdateUpperBody(dt);
        }

        private void Calm()
        {
            Current = Mood.Calm;
            _calmTimer = 0f;
        }

        private void Fight(Vector3 toPlayer, float distance, float dt)
        {
            if (Time.time >= _nextSight)
            {
                _nextSight = Time.time + 0.25f;
                if (CanSee(distance))
                {
                    _lastSeen = _player.transform.position;
                    _lastSeenTime = Time.time;
                }
            }
            bool seen = Time.time - _lastSeenTime < 0.5f;
            if ((!seen && Time.time - _lastSeenTime > ForgetAfter) || distance > GiveUpDistance)
            {
                Calm();
                return;
            }

            if (seen && distance < Reach)
            {
                Face(toPlayer, dt);
                Locomote(Vector3.zero, false);
                if (Time.time >= _swingReady && _landAt < 0f) Swing();
            }
            else
            {
                Vector3 target = seen ? _player.transform.position : _lastSeen;
                Vector3 to = target - transform.position;
                to.y = 0f;
                // Reached the last sighting and nobody's there: look around until they're forgotten.
                if (!seen && to.magnitude < 0.8f) Locomote(Vector3.zero, false);
                else Locomote(Move(to, dt, steer: true, JogSpeed), true);
            }

            if (_landAt > 0f && Time.time >= _landAt)
            {
                _landAt = -1f;
                Vector3 now = _player.transform.position - transform.position;
                now.y = 0f;
                if (now.magnitude < Reach + 0.3f && Vector3.Angle(transform.forward, now) < 50f)
                {
                    _audio.PlayOneShot(_punchClips[UnityEngine.Random.Range(0, _punchClips.Length)], 1f);
                    float side = Vector3.Dot(_player.transform.right, transform.forward) > 0f ? 1f : -1f;
                    _player.Jolt(1f, side, now.normalized * 2.5f);
                }
            }
        }

        private bool CanSee(float distance)
        {
            if (distance > SightRange) return false;
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 head = _player.transform.position + Vector3.up * 1.5f;
            Vector3 dir = head - eye;
            // Stop short of the player's own collider; only walls and vehicles block the view.
            return !Physics.Raycast(eye, dir.normalized, Mathf.Max(0f, dir.magnitude - 0.5f), Obstacles, QueryTriggerInteraction.Ignore);
        }

        private void Swing()
        {
            _cross = !_cross;
            _swingReady = Time.time + 0.9f + UnityEngine.Random.value * 0.5f;
            _landAt = Time.time + (_cross ? 0.22f : 0.15f);
            _upperUntil = Time.time + 0.6f;
            Animator a = _body.Animator;
            if (a != null) a.CrossFadeInFixedTime(_cross ? "PunchCross" : "PunchJab", 0.05f, 1, 0f);
        }

        private void UpdateUpperBody(float dt)
        {
            Animator a = _body.Animator;
            if (a == null) return;
            bool on = Time.time < _upperUntil && !IsDown;
            _upper = Mathf.MoveTowards(_upper, on ? 1f : 0f, dt * (on ? 20f : 4f));
            a.SetLayerWeight(1, _upper);
        }

        /// <summary>Moves toward <paramref name="direction"/> at <paramref name="speed"/>, sliding around walls; returns the velocity used.</summary>
        private Vector3 Move(Vector3 direction, float dt, bool steer, float speed = 0f)
        {
            if (direction.sqrMagnitude < 1e-4f) return Vector3.zero;
            Vector3 velocity = steer ? Steer(direction.normalized) * speed : direction;
            if (velocity.sqrMagnitude < 1e-4f) return Vector3.zero;
            // Knockback stops at walls instead of passing through them.
            if (!steer && Physics.SphereCast(transform.position + Vector3.up * 0.9f, 0.3f, velocity.normalized, out _, velocity.magnitude * dt + 0.05f, Obstacles, QueryTriggerInteraction.Ignore))
                return Vector3.zero;
            Vector3 p = transform.position + velocity * dt;
            // Follow the ground (kerbs, road crown) rather than floating at a fixed height.
            if (Physics.Raycast(p + Vector3.up, Vector3.down, out RaycastHit ground, 2.5f, 1 << 0, QueryTriggerInteraction.Ignore))
                p.y = Mathf.MoveTowards(transform.position.y, ground.point.y, 2f * dt + 0.2f);
            transform.position = p;
            if (steer) Face(velocity, dt);
            return velocity;
        }

        private Vector3 Steer(Vector3 desired)
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f })
            {
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * desired;
                if (!Physics.SphereCast(origin, 0.3f, dir, out RaycastHit hit, 0.9f, Obstacles, QueryTriggerInteraction.Ignore)
                    || hit.collider.transform.IsChildOf(_player.transform))
                    return dir;
            }
            return Vector3.zero;
        }

        private void Face(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * dt);
        }

        private void Locomote(Vector3 velocity, bool run)
        {
            float speed = velocity.magnitude;
            _body.Pose(speed < 0.2f ? NpcPose.Stand : run ? NpcPose.Run : NpcPose.Walk, Time.time, speed / 1.35f);
        }

        private void Finish()
        {
            _body.Overridden = false;
            if (_body.Animator != null) _body.Animator.SetLayerWeight(1, 0f);
            Destroy(_audio);
            Destroy(this);
            _done?.Invoke(this);
        }
    }
}
