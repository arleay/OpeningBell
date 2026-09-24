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
        public InputAction Jump { get; private set; }
        public InputAction Interact { get; private set; }
        /// <summary>Throw a punch (left mouse).</summary>
        public InputAction Attack { get; private set; }
        public InputAction OpenMenu { get; private set; }
        /// <summary>First-person / chase camera while riding.</summary>
        public InputAction CameraToggle { get; private set; }
        /// <summary>Held while driving: look freely around the car (orbit in chase view, turn the head in first person).</summary>
        public InputAction FreeLook { get; private set; }
        /// <summary>Cycle e-bike assist level.</summary>
        public InputAction Assist { get; private set; }
        /// <summary>Car handbrake.</summary>
        public InputAction Handbrake { get; private set; }
        public InputAction Leave { get; private set; }
        public InputAction CloseMenu { get; private set; }
        /// <summary>Take out / put away the phone (Tab). In its own always-on map: it works whichever map is active.</summary>
        public InputAction Phone { get; private set; }

        private InputActionMap _global;

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
            // Space is also the car handbrake; the two never apply at once (walking is suspended while driving).
            Jump = _player.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            Interact = _player.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            Attack = _player.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            OpenMenu = _player.AddAction("Menu", InputActionType.Button, "<Keyboard>/escape");
            CameraToggle = _player.AddAction("Camera", InputActionType.Button, "<Keyboard>/c");
            FreeLook = _player.AddAction("FreeLook", InputActionType.Button, "<Mouse>/rightButton");
            Assist = _player.AddAction("Assist", InputActionType.Button, "<Keyboard>/q");
            Handbrake = _player.AddAction("Handbrake", InputActionType.Button, "<Keyboard>/space");

            // Esc means "back out one level": stand up when seated, close the menu when it's open.
            _workstation = new InputActionMap("Workstation");
            Leave = _workstation.AddAction("Leave", InputActionType.Button, "<Keyboard>/escape");

            _menu = new InputActionMap("Menu");
            CloseMenu = _menu.AddAction("Close", InputActionType.Button, "<Keyboard>/escape");

            _global = new InputActionMap("Global");
            Phone = _global.AddAction("Phone", InputActionType.Button, "<Keyboard>/tab");
            _global.Enable();

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
            _global?.Dispose();
        }
    }
}
