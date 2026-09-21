using UnityEngine;
using UnityEngine.InputSystem;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// All gameplay input actions, defined in one place. Two maps: Player (walking) and Workstation (seated;
    /// the terminal UI owns the mouse and keyboard). Gamepad bindings can be added here without touching consumers.
    /// </summary>
    public sealed class GameInput : MonoBehaviour
    {
        private InputActionMap _player;
        private InputActionMap _workstation;

        public InputAction Move { get; private set; }
        public InputAction Look { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Interact { get; private set; }
        public InputAction Leave { get; private set; }

        private void Awake()
        {
            _player = new InputActionMap("Player");
            Move = _player.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Look = _player.AddAction("Look", InputActionType.PassThrough, "<Mouse>/delta");
            Sprint = _player.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            Interact = _player.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");

            _workstation = new InputActionMap("Workstation");
            Leave = _workstation.AddAction("Leave", InputActionType.Button, "<Keyboard>/escape");

            UsePlayerControls();
        }

        public void UsePlayerControls()
        {
            _workstation.Disable();
            _player.Enable();
        }

        public void UseWorkstationControls()
        {
            _player.Disable();
            _workstation.Enable();
        }

        private void OnDestroy()
        {
            _player?.Dispose();
            _workstation?.Dispose();
        }
    }
}
