using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

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
        /// <summary>Extra metres the body slides back when looking straight down (see LateUpdate).</summary>
        private const float LookDownShift = 0.24f;
        private const string DefaultLook = "Casual2_Male";

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
        /// <summary>A Tiny costume body: big mitts on short arms, so fists are shrunk and thrown further out.</summary>
        private bool _tiny;
        /// <summary>The head bone's forward offset from the feet in the model's rest pose, metres (scaled).</summary>
        private float _restHeadZ;

        public bool Visible => _model != null && _model.activeSelf;
        /// <summary>The body model (mirrors copy its pose).</summary>
        public GameObject Model => _model;
        /// <summary>The prefab the body was made from, and the look applied to it (mirrors build a double from these).</summary>
        public GameObject Source { get; private set; }
        public OpeningBell.PlayerLook Look { get; private set; }
        /// <summary>Holding [Z]: the left wrist comes up into view to read the watch.</summary>
        public bool CheckingWatch { get; private set; }
        /// <summary>Holds the watch up without the key (tests, cutscenes).</summary>
        public bool ForceWatch { get; set; }
        private float _watchWeight;
        /// <summary>
        /// Checking the watch is steered by feedback rather than by the rig's bone conventions: each frame the dial's
        /// actual facing and position are measured and the IK goal's roll (about the forearm) and offset nudged until
        /// the dial faces the eyes in the middle of the view.
        /// </summary>
        private WatchFace _watch;
        private float _watchRoll;
        private Vector3 _watchOffset; // from the dial to the hand, in view space
        private Vector3 _forearm = Vector3.right; // elbow to hand, in view space (last frame's pose)
        private CityContext _c;

        public void Configure(FirstPersonController player, CityArt art, OpeningBell.PlayerLook look, CityContext c = null)
        {
            _player = player;
            _controller = player.GetComponent<CharacterController>();
            _art = art;
            _c = c;
            SetLook(look);
        }

        /// <summary>Puts the body back together with the look's jewellery (measured on a fresh model in its rest pose).</summary>
        public void RefreshJewelry(OpeningBell.PlayerLook look = null) => SetLook(look ?? Look);

        /// <summary>Rebuilds the body as the creator's choice (null: the default casual look).</summary>
        public void SetLook(OpeningBell.PlayerLook look)
        {
            GameObject source = null;
            // Only the Tiny set: an old save's realistic look falls back to the default.
            int index = look != null && _art.People.Count > 0 ? CharacterStyle.ModelIndex(_art, look) : -1;
            if (index >= 0 && _art.IsTiny(index)) source = _art.People[index];
            if (source == null)
                foreach (GameObject p in _art.People)
                    if (p.name == DefaultLook) { source = p; break; }
            if (source == null && _art.People.Count > 0) source = _art.People[0];
            if (source == null || _art.PeopleAnimator == null) return;
            if (_model != null) Destroy(_model);
            Source = source;
            Look = look;

            _model = Instantiate(source, transform, false);
            _model.name = "PlayerBody";
            CharacterStyle.Apply(_model, look);
            // Scale so the eyes land at the camera: they sit about 0.1 m above the head bone.
            Vector3 head = source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head)?.position ?? new Vector3(0f, 1.62f, 0f);
            float eyes = _player.CameraPivot.localPosition.y;
            float scale = Mathf.Clamp(eyes / (head.y + 0.1f), 0.8f, 1.1f);
            _model.transform.localScale = Vector3.one * scale;
            _restHeadZ = head.z * scale;
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
            _tiny = source.name != "Pug" && source.name != "Cow"; // Tiny bodies: big mitts (the animals have paws)
            if (_tiny)
                foreach (HumanBodyBones b in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                    _animator.GetBoneTransform(b).localScale = Vector3.one * 0.6f; // humanoid clips never animate scale
            foreach (SkinnedMeshRenderer r in _model.GetComponentsInChildren<SkinnedMeshRenderer>())
                r.updateWhenOffscreen = true; // the camera sits inside the bounds, which can cull limbs at the edges
            // Jewellery last: it's measured on this rest pose, hands already shrunk.
            if (look != null) Adornments.Apply(_c, _model, look.Worn);
            _watch = _model.GetComponentInChildren<WatchFace>(true);
            _watchRoll = 0f;
            _watchOffset = Vector3.zero;
            SetLayer(_model.transform, OwnBodyLayer);
        }

        /// <summary>
        /// The first-person body's layer: the main camera draws it, mirrors don't (they show a double with a head, see
        /// <see cref="Mirror"/>).
        /// </summary>
        public const int OwnBodyLayer = 14;

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
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
            bool show = (_player.ControlEnabled || _player.Browsing) && !_player.Suspended;
            if (_model.activeSelf != show) _model.SetActive(show);
            if (!show)
            {
                _watchWeight = 0f;
                _player.GlanceWeight = 0f;
                return;
            }

            // A real head pivots at the neck, so looking down carries the eyes forward over the chest. The camera
            // pivots in place instead, so the body slides back to match: looking at your feet shows chest, belly and
            // shoes from the front rather than the top of your own shoulders and the cut-off neck.
            // The shift pins the head bone (the cut-off neck) at a fixed spot behind the eyes, measured from this
            // frame's pose: walking and jogging lean the torso forward, which would otherwise carry the neck back
            // under the camera.
            float pitch = Mathf.DeltaAngle(0f, _player.CameraPivot.localEulerAngles.x);
            float down = Mathf.Clamp01(pitch / 85f);
            float headTarget = _restHeadZ - (SetBack + LookDownShift * Mathf.Sin(down * Mathf.PI * 0.5f));
            float headNow = transform.InverseTransformPoint(_head.position).z;
            Vector3 at = _model.transform.localPosition;
            at.z = Mathf.Clamp(at.z + headTarget - headNow, -0.8f, 0.2f);
            _model.transform.localPosition = at;

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

            Keyboard k = Keyboard.current;
            CheckingWatch = (ForceWatch || (k != null && k.zKey.isPressed && !k.ctrlKey.isPressed)) && _player.ControlEnabled && Time.time >= _upperUntil;
            _watchWeight = Mathf.MoveTowards(_watchWeight, CheckingWatch ? 1f : 0f, Time.deltaTime * 5f);
            // Short arms can't lift a wrist to eye level: the eyes go down to meet it.
            _player.GlanceWeight = Mathf.SmoothStep(0f, 1f, _watchWeight);
            _upperWeight = Mathf.MoveTowards(_upperWeight, Time.time < _upperUntil ? 1f : 0f, Time.deltaTime * (Time.time < _upperUntil ? 20f : 4f));
            _animator.SetLayerWeight(1, _upperWeight);
            SteerWatch();
            // The head (hair especially) sits in front of the eyes; collapse it so it never fills the view. The cost
            // is a headless shadow, which reads fine.
            if (_head != null) _head.localScale = Vector3.one * 0.001f;
        }

        /// <summary>
        /// Measures this frame's pose (IK already applied) and corrects the watch goal for the next: roll the hand about
        /// the forearm until the dial faces the eyes, and remember where the hand sits relative to the dial so the dial,
        /// not the hand, is put at the aim point.
        /// </summary>
        private void SteerWatch()
        {
            if (_watch == null || _watchWeight < 0.3f)
            {
                if (_watchWeight <= 0f) _watchRoll = Mathf.MoveTowards(_watchRoll, 0f, 360f * Time.deltaTime);
                return;
            }
            Transform view = _player.CameraPivot;
            Transform hand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform elbow = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform dialCentre = _watch.transform.Find("Hour hand") ?? _watch.transform;
            _forearm = view.InverseTransformDirection((hand.position - elbow.position).normalized);
            Vector3 axis = hand.position - elbow.position;
            Vector3 dial = Vector3.ProjectOnPlane(_watch.transform.up, axis), want = Vector3.ProjectOnPlane(-view.forward, axis);
            if (dial.sqrMagnitude > 1e-4f && want.sqrMagnitude > 1e-4f)
                _watchRoll = Mathf.Repeat(_watchRoll + Vector3.SignedAngle(dial, want, axis) * 0.5f + 180f, 360f) - 180f;
            _watchOffset = Vector3.Lerp(_watchOffset, view.InverseTransformVector(hand.position - dialCentre.position), 0.5f);
        }

        /// <summary>
        /// Steers the punching fist through the view. The clips throw at chest height, which from the eyes is off the
        /// bottom of the screen, so IK pulls the hand to a point just ahead of the camera, peaking as the blow lands.
        /// </summary>
        internal void OnIK()
        {
            if (_watchWeight > 0f)
            {
                // Checking the watch: with the view tipped down (GlanceWeight), the left forearm comes across in front of
                // the chest, back of the wrist turned to the eyes, the hand off to the right so the wrist (and the dial)
                // sits in the middle of the view. Close enough for short Tiny arms to reach.
                Transform view = _player.CameraPivot;
                float w = Mathf.SmoothStep(0f, 1f, _watchWeight);
                Vector3 dialAt = view.position + view.forward * (_tiny ? 0.38f : 0.34f) + view.right * 0.04f;
                _animator.SetIKPosition(AvatarIKGoal.LeftHand, dialAt + view.TransformVector(_watchOffset));
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
                // Wrist straight (fingers carry on along the forearm, so the strap stays round it), back of the hand
                // toward the eyes, then the measured roll correction about the forearm.
                Vector3 along = view.TransformDirection(_forearm);
                Vector3 back = Vector3.ProjectOnPlane(-view.forward, along);
                Quaternion straight = Quaternion.LookRotation(along, back.sqrMagnitude > 1e-4f ? back : view.up) * Quaternion.Euler(0f, 90f, 0f);
                _animator.SetIKRotation(AvatarIKGoal.LeftHand, Quaternion.AngleAxis(_watchRoll, along) * straight);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, w);
                // Elbow out to the left and forward: the forearm lies across the view, as when you glance at a watch.
                _animator.SetIKHintPosition(AvatarIKHint.LeftElbow, view.position - view.right * 0.5f + view.forward * 0.3f - view.up * 0.1f);
                _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, w);
            }
            float t = Time.time - _punchStart;
            if (t < 0f || t > _punchLand + 0.25f) return;
            float weight = t < _punchLand ? Mathf.SmoothStep(0f, 1f, t / _punchLand) : 1f - Mathf.SmoothStep(0f, 1f, (t - _punchLand) / 0.25f);
            Transform eye = _player.CameraPivot;
            AvatarIKGoal hand = _punchCross ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
            float side = _punchCross ? 1f : -1f;
            float reach = _tiny ? FistReach + 0.15f : FistReach;
            Vector3 target = eye.position + eye.forward * reach - eye.up * (_tiny ? 0.2f : 0.12f) + eye.right * (0.06f * side);
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
