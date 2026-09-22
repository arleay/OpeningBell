using System;
using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// WASD + mouse look on a CharacterController. Yaw turns the body, pitch turns the camera pivot. Movement has a
    /// little weight (ground acceleration, weak air control, a jump), and the camera moves like a head: a bob and
    /// sway in step with the feet, a lean into strafes, a dip on landing and a slightly wider view at a sprint.
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

        public Transform CameraPivot => cameraPivot;
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
        }

        private void Start() => ApplyCursor();

        private void Update()
        {
            if (!_controlEnabled || _suspended) return;
            float dt = Time.deltaTime;

            Vector2 look = input.Look.ReadValue<Vector2>() * (lookSensitivity * GameSettings.MouseSensitivity);
            transform.Rotate(0f, look.x, 0f);
            _pitch = Mathf.Clamp(_pitch - look.y, -pitchLimit, pitchLimit);

            Vector2 move = Vector2.ClampMagnitude(input.Move.ReadValue<Vector2>(), 1f);
            float speed = input.Sprint.IsPressed() ? sprintSpeed : walkSpeed;
            Vector3 wanted = (transform.right * move.x + transform.forward * move.y) * speed;
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

            const float stiffness = 170f;
            _dipVelocity += (-stiffness * _dip - 2f * 0.8f * Mathf.Sqrt(stiffness) * _dipVelocity) * dt;
            _dip += _dipVelocity * dt;

            float height = Mathf.Lerp(bobHeight.x, bobHeight.y, sprint) * _bobWeight;
            float sway = Mathf.Lerp(bobSway.x, bobSway.y, sprint) * _bobWeight;
            float breathe = 0.004f * Mathf.Sin(Time.time * 1.7f) * (1f - _bobWeight);
            cameraPivot.localPosition = _pivotHome + new Vector3(Mathf.Sin(_stepPhase) * sway, Mathf.Sin(2f * _stepPhase) * height + breathe + _dip, 0f);

            float lean = -strafe * strafeLean + Mathf.Sin(_stepPhase) * 0.35f * _bobWeight;
            _roll = Mathf.Lerp(_roll, lean, 1f - Mathf.Exp(-10f * dt));
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, _roll);

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
