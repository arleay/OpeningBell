using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The player's own animated character, seen in first person: look down and there are legs, throw a punch and
    /// the arms come up into view, and the shadow is a whole person. The body stands a little behind the camera so
    /// the eyes are just behind the lens and the head never fills the view. Hidden while someone else has the
    /// camera (desk, bikes, cars).
    /// </summary>
    public sealed class PlayerBody : MonoBehaviour
    {
        /// <summary>Metres the body stands behind the camera: the camera sits just in front of the face.</summary>
        private const float SetBack = 0.12f;
        /// <summary>How far ahead of the eyes a punch lands, metres.</summary>
        private const float FistReach = 0.45f;
        private const string DefaultLook = "Casual2";

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int Idle = Animator.StringToHash("Idle"), Walk = Animator.StringToHash("Walk"), Jog = Animator.StringToHash("Jog");
        private static readonly int PunchJab = Animator.StringToHash("PunchJab"), PunchCross = Animator.StringToHash("PunchCross");

        private FirstPersonController _player;
        private CityArt _art;
        private CharacterController _controller;
        private GameObject _model;
        private Animator _animator;
        private float _walkClipSpeed, _jogClipSpeed;
        private int _state;
        private float _upperUntil, _upperWeight;
        private float _punchStart = -99f, _punchLand;
        private bool _punchCross;
        private Transform _head;

        public bool Visible => _model != null && _model.activeSelf;

        public void Configure(FirstPersonController player, CityArt art, OpeningBell.PlayerLook look)
        {
            _player = player;
            _controller = player.GetComponent<CharacterController>();
            _art = art;
            SetLook(look);
        }

        /// <summary>Rebuilds the body as the creator's choice (null: the default casual look).</summary>
        public void SetLook(OpeningBell.PlayerLook look)
        {
            GameObject source = null;
            if (look != null && _art.People.Count > 0) source = _art.People[CharacterStyle.ModelIndex(_art, look)];
            if (source == null)
                foreach (GameObject p in _art.People)
                    if (p.name == DefaultLook) { source = p; break; }
            if (source == null && _art.People.Count > 0) source = _art.People[0];
            if (source == null || _art.PeopleAnimator == null) return;
            if (_model != null) Destroy(_model);

            _model = Instantiate(source, transform, false);
            _model.name = "PlayerBody";
            CharacterStyle.Apply(_model, look);
            // Scale so the eyes land at the camera: they sit about 0.1 m above the head bone.
            Vector3 head = source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head)?.position ?? new Vector3(0f, 1.62f, 0f);
            float eyes = _player.CameraPivot.localPosition.y;
            float scale = Mathf.Clamp(eyes / (head.y + 0.1f), 0.8f, 1.1f);
            _model.transform.localScale = Vector3.one * scale;
            _model.transform.localPosition = new Vector3(0f, 0f, -SetBack);
            _animator = _model.GetComponent<Animator>();
            _animator.runtimeAnimatorController = _art.PeopleAnimator;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _walkClipSpeed = _art.WalkClipSpeed * scale;
            _jogClipSpeed = 4.83f * scale;
            _state = Idle;
            _model.AddComponent<IKRelay>().Body = this;
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            foreach (SkinnedMeshRenderer r in _model.GetComponentsInChildren<SkinnedMeshRenderer>())
                r.updateWhenOffscreen = true; // the camera sits inside the bounds, which can cull limbs at the edges
        }

        /// <summary>Plays a punch on the upper body; returns seconds until the fist lands.</summary>
        public float Punch(bool cross)
        {
            if (_animator == null || !_model.activeSelf) return 0.15f;
            _animator.CrossFadeInFixedTime(cross ? PunchCross : PunchJab, 0.05f, 1, 0f);
            _upperUntil = Time.time + 0.6f;
            _punchStart = Time.time;
            _punchCross = cross;
            _punchLand = cross ? 0.22f : 0.15f;
            return _punchLand;
        }

        private void LateUpdate()
        {
            if (_model == null) return;
            bool show = _player.ControlEnabled && !_player.Suspended;
            if (_model.activeSelf != show) _model.SetActive(show);
            if (!show) return;

            Vector3 v = _controller.velocity;
            v.y = 0f;
            float speed = v.magnitude;
            float forward = Vector3.Dot(v, transform.forward);
            // Walking backwards plays the walk in reverse; sideways reads as a slow walk.
            float direction = forward < -0.2f ? -1f : 1f;
            int state;
            float rate;
            if (speed < 0.25f) { state = Idle; rate = 1f; }
            else if (speed < 2.4f) { state = Walk; rate = direction * Mathf.Min(speed / _walkClipSpeed, 2.2f); }
            else { state = Jog; rate = direction * Mathf.Max(0.6f, speed / _jogClipSpeed); }
            if (state != _state)
            {
                _state = state;
                _animator.CrossFadeInFixedTime(state, 0.2f, 0);
            }
            _animator.SetFloat(SpeedParam, rate);

            _upperWeight = Mathf.MoveTowards(_upperWeight, Time.time < _upperUntil ? 1f : 0f, Time.deltaTime * (Time.time < _upperUntil ? 20f : 4f));
            _animator.SetLayerWeight(1, _upperWeight);
            // The head (hair especially) sits in front of the eyes; collapse it so it never fills the view. The cost
            // is a headless shadow, which reads fine.
            if (_head != null) _head.localScale = Vector3.one * 0.001f;
        }

        /// <summary>
        /// Steers the punching fist through the view. The clips throw at chest height, which from the eyes is off the
        /// bottom of the screen, so IK pulls the hand to a point just ahead of the camera, peaking as the blow lands.
        /// </summary>
        internal void OnIK()
        {
            float t = Time.time - _punchStart;
            if (t < 0f || t > _punchLand + 0.25f) return;
            float weight = t < _punchLand ? Mathf.SmoothStep(0f, 1f, t / _punchLand) : 1f - Mathf.SmoothStep(0f, 1f, (t - _punchLand) / 0.25f);
            Transform eye = _player.CameraPivot;
            AvatarIKGoal hand = _punchCross ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
            float side = _punchCross ? 1f : -1f;
            Vector3 target = eye.position + eye.forward * FistReach - eye.up * 0.12f + eye.right * (0.06f * side);
            _animator.SetIKPosition(hand, target);
            _animator.SetIKPositionWeight(hand, weight);
            // Knuckles forward: palm down, fist pointing where the camera looks.
            _animator.SetIKRotation(hand, Quaternion.LookRotation(eye.forward, eye.up) * Quaternion.Euler(0f, _punchCross ? -90f : 90f, 0f));
            _animator.SetIKRotationWeight(hand, weight * 0.8f);
            // The clips raise the other fist to guard the face, which from the eyes is a blur against the lens.
            AvatarIKGoal guard = _punchCross ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
            _animator.SetIKPosition(guard, eye.position + eye.forward * 0.28f - eye.up * 0.34f - eye.right * (0.22f * side));
            _animator.SetIKPositionWeight(guard, weight);
        }

        /// <summary>Forwards the animator's IK callback (it only goes to scripts on the animator's own object).</summary>
        private sealed class IKRelay : MonoBehaviour
        {
            public PlayerBody Body;
            private void OnAnimatorIK(int layerIndex) => Body.OnIK();
        }
    }
}
