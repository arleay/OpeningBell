using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>WASD + mouse look on a CharacterController. Yaw turns the body, pitch turns the camera pivot.</summary>
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

        private CharacterController _body;
        private float _pitch;
        private float _verticalSpeed;
        private bool _controlEnabled = true;

        public Transform CameraPivot => cameraPivot;

        /// <summary>Off while seated: no movement or look, cursor released for the terminal.</summary>
        public bool ControlEnabled
        {
            get => _controlEnabled;
            set
            {
                _controlEnabled = value;
                ApplyCursor();
            }
        }

        private void Awake() => _body = GetComponent<CharacterController>();

        private void Start() => ApplyCursor();

        private void Update()
        {
            if (!_controlEnabled) return;

            Vector2 look = input.Look.ReadValue<Vector2>() * (lookSensitivity * GameSettings.MouseSensitivity);
            transform.Rotate(0f, look.x, 0f);
            _pitch = Mathf.Clamp(_pitch - look.y, -pitchLimit, pitchLimit);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            Vector2 move = Vector2.ClampMagnitude(input.Move.ReadValue<Vector2>(), 1f);
            float speed = input.Sprint.IsPressed() ? sprintSpeed : walkSpeed;
            Vector3 velocity = (transform.right * move.x + transform.forward * move.y) * speed;

            // Small constant downward speed while grounded keeps the controller snapped to the floor.
            _verticalSpeed = _body.isGrounded && _verticalSpeed < 0f ? -1f : _verticalSpeed + gravity * Time.deltaTime;
            velocity.y = _verticalSpeed;
            _body.Move(velocity * Time.deltaTime);
        }

        /// <summary>Teleports the player; a CharacterController must be disabled to be moved directly.</summary>
        public void PlaceAt(Vector3 position, float yaw)
        {
            _body.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _body.enabled = true;
            _pitch = 0f;
            cameraPivot.localRotation = Quaternion.identity;
        }

        private void ApplyCursor()
        {
            Cursor.lockState = _controlEnabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_controlEnabled;
        }
    }
}
