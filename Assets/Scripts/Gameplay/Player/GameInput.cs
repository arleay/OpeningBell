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
        private InputActionMap _menu;

        public InputAction Move { get; private set; }
        public InputAction Look { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Interact { get; private set; }
        public InputAction OpenMenu { get; private set; }
        public InputAction Leave { get; private set; }
        public InputAction CloseMenu { get; private set; }

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
            OpenMenu = _player.AddAction("Menu", InputActionType.Button, "<Keyboard>/escape");

            // Esc means "back out one level": stand up when seated, close the menu when it's open.
            _workstation = new InputActionMap("Workstation");
            Leave = _workstation.AddAction("Leave", InputActionType.Button, "<Keyboard>/escape");

            _menu = new InputActionMap("Menu");
            CloseMenu = _menu.AddAction("Close", InputActionType.Button, "<Keyboard>/escape");

            UsePlayerControls();
        }

        public void UsePlayerControls() => Use(_player);
        public void UseWorkstationControls() => Use(_workstation);
        public void UseMenuControls() => Use(_menu);

        private void Use(InputActionMap active)
        {
            foreach (InputActionMap map in new[] { _player, _workstation, _menu })
                if (map != active) map.Disable();
            active.Enable();
        }

        private void OnDestroy()
        {
            _player?.Dispose();
            _workstation?.Dispose();
            _menu?.Dispose();
        }
    }
}
