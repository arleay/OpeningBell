using System;
using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// WASD + mouse look on a CharacterController. Yaw turns the body, pitch turns the camera pivot. Movement has a
    /// little weight (ground acceleration, weak air control, a jump), and the camera moves like a head: a bob and
    /// sway in step with the feet, a lean into strafes, a dip on landing and a slightly wider view at a sprint.
    /// [C] switches to a third-person camera behind the shoulder (it follows the same look); holding the right button
    /// there orbits the camera freely round the body, up, down and all the way round, and letting go eases it back.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private GameInput input;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float sprintSpeed = 5f;
        [Tooltip("Degrees per mouse count.")]
        [SerializeField] private float lookSensitivity = 0.1f;
        [SerializeField] private float pitchLimit = 85f;
        [SerializeField] private float gravity = -18f;

        [Header("Feel")]
        [Tooltip("m/s² toward the wanted velocity on the ground: a walk in about 0.2 s, a stop a little quicker.")]
        [SerializeField] private float groundAcceleration = 16f;
        [SerializeField] private float airAcceleration = 3f;
        [SerializeField] private float jumpHeight = 0.9f;
        [Tooltip("Metres per step at a walk (x) and a sprint (y); sets the bob and footstep cadence.")]
        [SerializeField] private Vector2 stepLength = new Vector2(1.3f, 1.8f);
        [Tooltip("Vertical head bob per step, metres, walk / sprint.")]
        [SerializeField] private Vector2 bobHeight = new Vector2(0.028f, 0.05f);
        [Tooltip("Side-to-side sway over a stride, metres, walk / sprint.")]
        [SerializeField] private Vector2 bobSway = new Vector2(0.018f, 0.03f);
        [SerializeField] private float strafeLean = 1.2f;
        [SerializeField] private float sprintFovKick = 6f;

        private CharacterController _body;
        private Camera _camera;
        private float _pitch;
        private float _verticalSpeed;
        private bool _controlEnabled = true;
        private bool _suspended;
        private bool _wasGrounded = true;

        private Vector3 _planar; // smoothed horizontal velocity
        private Vector3 _pivotHome;
        private bool _homeKnown;
        private float _baseFov;
        private float _stepPhase; // radians; one footfall every π
        private int _lastStep;
        private float _bobWeight;
        private float _roll;
        private float _dip, _dipVelocity; // landing spring, metres

        /// <summary>Third person: camera distance behind the head, how far it may tip, and the shoulder offset.</summary>
        private const float ThirdDistance = 3.2f, OrbitPitchMin = -70f, OrbitPitchMax = 80f;
        private static readonly Vector3 Shoulder = new Vector3(0.35f, 0.15f, 0f);
        private bool _thirdPerson;
        private float _orbitYaw, _orbitPitch; // free-look orbit on top of the body's yaw and the head's pitch
        private float _cameraBack;            // current distance (pulled in by walls)
        private Vector3 _cameraHome;
        private bool _orbiting;

        public Transform CameraPivot => cameraPivot;

        /// <summary>Third-person view on ([C] toggles).</summary>
        public bool ThirdPerson
        {
            get => _thirdPerson;
            set
            {
                if (_thirdPerson == value) return;
                _thirdPerson = value;
                _orbitYaw = _orbitPitch = 0f;
                _cameraBack = 0.5f; // swings out from the head
                if (!value && _camera != null)
                {
                    _camera.transform.localPosition = _cameraHome;
                    _camera.transform.localRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>How far the camera sits behind the head (0 in first person): aiming from the camera reaches this much further.</summary>
        public float CameraBack => _thirdPerson ? _cameraBack : 0f;

        /// <summary>
        /// Something the player is looking at below them for a moment (their watch): the view tips down to at least
        /// <see cref="GlanceDownTo"/> degrees, by <see cref="GlanceWeight"/> (0 none, 1 fully). Mouse look is untouched
        /// underneath, so letting go returns the view to where it was.
        /// </summary>
        public float GlanceDownTo { get; set; } = 66f;
        public float GlanceWeight { get; set; }
        public GameInput Input => input;
        public float LookSensitivity => lookSensitivity * GameSettings.MouseSensitivity;

        /// <summary>A foot hit the ground while walking; the argument is the walking speed in m/s.</summary>
        public event Action<float> Footstep;

        /// <summary>Touched down after a fall or jump; the argument is the downward speed at impact.</summary>
        public event Action<float> Landed;

        /// <summary>
        /// Something else (a bike, a board) is moving the body and handling look. The cursor stays locked,
        /// unlike <see cref="ControlEnabled"/> = false, which frees it for UI.
        /// </summary>
        public bool Suspended
        {
            get => _suspended;
            set
            {
                _suspended = value;
                if (value) ResetCameraMotion();
            }
        }

        /// <summary>
        /// Control is off for a hand-held screen (the phone): the cursor is free, but the player is still standing in
        /// the world, so their body and the minimap stay up.
        /// </summary>
        public bool Browsing { get; set; }

        /// <summary>0 sober … 1 very drunk: slower, drifting steps and a swaying view (CASINO_SPEC §46).</summary>
        public float Impairment { get; set; }

        /// <summary>Both hands on something you're carrying: no punching, no riding off.</summary>
        public bool HandsFull { get; set; }
        /// <summary>The left button belongs to what's aimed at (a moving box), not the fists.</summary>
        public bool ClickClaimed { get; set; }

        /// <summary>Off while seated: no movement or look, cursor released for the terminal.</summary>
        public bool ControlEnabled
        {
            get => _controlEnabled;
            set
            {
                _controlEnabled = value;
                ApplyCursor();
                // Whoever takes the camera next (the desk glide) starts from a head at rest.
                if (!value) ResetCameraMotion();
            }
        }

        private void Awake()
        {
            _body = GetComponent<CharacterController>();
            _camera = cameraPivot.GetComponentInChildren<Camera>();
            _baseFov = _camera != null ? _camera.fieldOfView : 60f;
            _pivotHome = cameraPivot.localPosition;
            _homeKnown = true;
            if (_camera != null) _cameraHome = _camera.transform.localPosition;
        }

        private void Start() => ApplyCursor();

        private void Update()
        {
            if (!_controlEnabled || _suspended) return;
            float dt = Time.deltaTime;

            if (input.CameraToggle.WasPressedThisFrame()) ThirdPerson = !ThirdPerson;
            Vector2 look = input.Look.ReadValue<Vector2>() * (lookSensitivity * GameSettings.MouseSensitivity);
            // Third person, right button held (and hands free: carrying, it cancels): the mouse orbits the camera
            // instead of turning the body.
            _orbiting = _thirdPerson && !HandsFull && input.FreeLook.IsPressed();
            if (_orbiting)
            {
                _orbitYaw = Mathf.Repeat(_orbitYaw + look.x + 180f, 360f) - 180f;
                _orbitPitch = Mathf.Clamp(_orbitPitch - look.y, OrbitPitchMin - _pitch, OrbitPitchMax - _pitch);
            }
            else
            {
                transform.Rotate(0f, look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y, -pitchLimit, pitchLimit);
                // Let go: the camera swings back behind.
                float k = 1f - Mathf.Exp(-dt / 0.15f);
                _orbitYaw = Mathf.Abs(_orbitYaw) < 0.1f ? 0f : Mathf.Lerp(_orbitYaw, 0f, k);
                _orbitPitch = Mathf.Abs(_orbitPitch) < 0.1f ? 0f : Mathf.Lerp(_orbitPitch, 0f, k);
            }

            Vector2 move = Vector2.ClampMagnitude(input.Move.ReadValue<Vector2>(), 1f);
            float speed = (input.Sprint.IsPressed() ? sprintSpeed : walkSpeed) * (1f - 0.35f * Impairment);
            Vector3 wanted = (transform.right * move.x + transform.forward * move.y) * speed;
            // Drunk: the feet wander a little to the side of where you're heading.
            if (Impairment > 0f && move.sqrMagnitude > 0.01f)
                wanted += transform.right * (Mathf.Sin(Time.time * 1.3f) * 0.9f * Impairment * speed * 0.3f);
            bool grounded = _body.isGrounded;
            _planar = Vector3.MoveTowards(_planar, wanted, (grounded ? groundAcceleration : airAcceleration) * dt);

            if (grounded && _verticalSpeed < 0f)
            {
                if (!_wasGrounded) Land(-_verticalSpeed);
                // Small constant downward speed while grounded keeps the controller snapped to the floor.
                _verticalSpeed = -1f;
            }
            else _verticalSpeed += gravity * dt;
            if (grounded && input.Jump.WasPressedThisFrame()) _verticalSpeed = Mathf.Sqrt(2f * -gravity * jumpHeight);
            _wasGrounded = grounded;

            _body.Move((_planar + Vector3.up * _verticalSpeed) * dt);
            Vector3 moved = _body.velocity;
            moved.y = 0f;
            AnimateHead(moved.magnitude, _body.isGrounded, move.x, dt);
        }

        /// <summary>
        /// Third person: the camera behind the shoulder, looking where the head looks (plus any orbit), pulled in
        /// short of walls so it never sees through them. After the head's own motion, so the view follows the bob.
        /// </summary>
        private void LateUpdate()
        {
            if (!_thirdPerson || !_controlEnabled || _suspended || _camera == null) return;
            float pitch = Mathf.Clamp(_pitch + _orbitPitch, OrbitPitchMin, OrbitPitchMax);
            Quaternion rot = Quaternion.Euler(pitch, transform.eulerAngles.y + _orbitYaw, 0f);
            Vector3 focus = cameraPivot.position + rot * Shoulder;
            Vector3 back = rot * Vector3.back;
            float want = ThirdDistance;
            if (Physics.SphereCast(focus, 0.2f, back, out RaycastHit hit, ThirdDistance, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
                want = Mathf.Max(0.4f, hit.distance - 0.1f);
            // In fast (never through a wall), out slowly (no pumping past lamp posts).
            _cameraBack = want < _cameraBack ? want : Mathf.Lerp(_cameraBack, want, 1f - Mathf.Exp(-Time.deltaTime / 0.25f));
            _camera.transform.SetPositionAndRotation(focus + back * _cameraBack, rot);
        }

        private void Land(float impactSpeed)
        {
            // Stepping off a kerb barely registers; a real drop pushes the head down (a damped spring brings it back).
            // Sized so a jump's landing sinks the head about 4 cm.
            if (impactSpeed > 2f) _dipVelocity -= Mathf.Min(impactSpeed, 12f) * 0.16f;
            Landed?.Invoke(impactSpeed);
        }

        private void AnimateHead(float planarSpeed, bool grounded, float strafe, float dt)
        {
            float sprint = Mathf.InverseLerp(walkSpeed, sprintSpeed, planarSpeed);
            _bobWeight = Mathf.MoveTowards(_bobWeight, grounded ? Mathf.Clamp01(planarSpeed / walkSpeed) : 0f, dt * 4f);
            if (grounded) _stepPhase += planarSpeed / Mathf.Lerp(stepLength.x, stepLength.y, sprint) * Mathf.PI * dt;

            // The head is lowest as each foot lands: that's where sin(2φ) bottoms out, so the footfall fires there.
            int step = Mathf.FloorToInt((2f * _stepPhase + 0.5f * Mathf.PI) / (2f * Mathf.PI));
            if (step != _lastStep)
            {
                _lastStep = step;
                if (grounded && planarSpeed > 0.6f) Footstep?.Invoke(planarSpeed);
            }

            // The spring is stiff: explicit steps are only stable under ~0.1 s, so integrate it in small steps, and at
            // most a tenth of a second per frame (a hitch or a fast-forwarded clock would otherwise blow it up to NaN).
            const float stiffness = 170f;
            for (float left = Mathf.Min(dt, 0.1f); left > 1e-5f;)
            {
                float h = Mathf.Min(left, 1f / 120f);
                _dipVelocity += (-stiffness * _dip - 2f * 0.8f * Mathf.Sqrt(stiffness) * _dipVelocity) * h;
                _dip += _dipVelocity * h;
                left -= h;
            }

            float height = Mathf.Lerp(bobHeight.x, bobHeight.y, sprint) * _bobWeight;
            float sway = Mathf.Lerp(bobSway.x, bobSway.y, sprint) * _bobWeight;
            float breathe = 0.004f * Mathf.Sin(Time.time * 1.7f) * (1f - _bobWeight);
            cameraPivot.localPosition = _pivotHome + new Vector3(Mathf.Sin(_stepPhase) * sway, Mathf.Sin(2f * _stepPhase) * height + breathe + _dip, 0f);

            float lean = -strafe * strafeLean + Mathf.Sin(_stepPhase) * 0.35f * _bobWeight;
            _roll = Mathf.Lerp(_roll, lean, 1f - Mathf.Exp(-10f * dt));
            float pitch = Mathf.Lerp(_pitch, Mathf.Max(_pitch, GlanceDownTo), Mathf.Clamp01(GlanceWeight));
            float swayRoll = Impairment * 5f * Mathf.Sin(Time.time * 0.63f), swayPitch = Impairment * 2f * Mathf.Sin(Time.time * 0.41f + 1f);
            float swayYaw = Impairment * 2.5f * Mathf.Sin(Time.time * 0.29f + 2f);
            cameraPivot.localRotation = Quaternion.Euler(pitch + swayPitch, swayYaw, _roll + swayRoll);

            if (_camera != null)
            {
                float kick = sprintFovKick * Mathf.InverseLerp(walkSpeed + 0.5f, sprintSpeed, planarSpeed);
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, _baseFov + kick, 1f - Mathf.Exp(-6f * dt));
            }
        }

        /// <summary>
        /// Taking a hit: the head snaps down and rolls away from the blow (the landing spring and lean smoothing bring
        /// it back), and the body is shoved by <paramref name="push"/> (m/s, worn off by ground friction).
        /// </summary>
        public void Jolt(float strength, float side, Vector3 push)
        {
            if (!_controlEnabled || _suspended) return;
            _dipVelocity -= 0.5f * strength;
            _roll += side * 7f * strength;
            push.y = 0f;
            _planar += push;
        }

        private void ResetCameraMotion()
        {
            _planar = Vector3.zero;
            _bobWeight = _roll = _dip = _dipVelocity = 0f;
            if (!_homeKnown) return;
            cameraPivot.localPosition = _pivotHome;
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            if (_camera != null) _camera.fieldOfView = _baseFov;
        }

        /// <summary>Teleports the player; a CharacterController must be disabled to be moved directly.</summary>
        public void PlaceAt(Vector3 position, float yaw, float pitch = 0f)
        {
            _body.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _body.enabled = true;
            _pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);
            _verticalSpeed = 0f;
            ResetCameraMotion();
        }

        private void ApplyCursor()
        {
            Cursor.lockState = _controlEnabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_controlEnabled;
        }
    }
}
