using System;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    public enum DoorKind
    {
        /// <summary>Hinged; [E] opens it away from the player, it closes itself once the player has moved on.</summary>
        Swing,
        /// <summary>Glass sliding doors that open when someone approaches (lobbies, shops).</summary>
        AutoSlide,
    }

    /// <summary>
    /// A door the player can use, optionally locked by a rule (business hours, a lease). Locked doors still take
    /// focus so the prompt can say why.
    /// </summary>
    public sealed class Door : Interactable
    {
        [SerializeField] private DoorKind kind = DoorKind.Swing;
        [Tooltip("Swing: the hinge (leaf rotates about its Y). Slide: left panel.")]
        [SerializeField] private Transform leaf;
        [Tooltip("Slide only: right panel.")]
        [SerializeField, Optional] private Transform leafB;
        [SerializeField] private string label = "door";
        [SerializeField] private float swingAngle = 100f;
        [SerializeField] private float slideDistance = 0.9f;
        [SerializeField] private float seconds = 0.4f;
        [SerializeField] private float autoCloseSeconds = 6f;
        [SerializeField] private float sensorRange = 2.6f;

        private Transform _player;
        private float _open;          // 0 closed … 1 open
        private float _direction = 1f; // swing sign, chosen away from the opener
        private bool _wantOpen;
        private float _openedAt;
        private Vector3 _closedA, _closedB;

        /// <summary>Returns why the door is locked, or null when it opens.</summary>
        public Func<string> LockReason { get; set; }
        public string Label => label;
        public bool IsOpen => _open > 0.95f;
        /// <summary>Opening or open (as opposed to closing or closed).</summary>
        public bool WantsOpen => _wantOpen;
        public bool IsLocked => LockReason?.Invoke() != null;

        public void Configure(DoorKind doorKind, Transform leafA, Transform leafRight, string name, Transform player)
        {
            kind = doorKind;
            leaf = leafA;
            leafB = leafRight;
            label = name;
            _player = player;
            _closedA = leaf.localPosition;
            if (leafB != null) _closedB = leafB.localPosition;
        }

        private void Awake()
        {
            if (leaf == null) return;
            _closedA = leaf.localPosition;
            if (leafB != null) _closedB = leafB.localPosition;
        }

        private void Start()
        {
            // Doors authored in the scene (the apartment's own) find the player themselves.
            if (_player == null) _player = FindAnyObjectByType<FirstPersonController>()?.transform;
        }

        public override string Prompt
        {
            get
            {
                string reason = LockReason?.Invoke();
                if (reason != null) return $"{Capitalized(label)} · {reason}";
                return (_wantOpen ? "Close " : "Open ") + label;
            }
        }

        // Automatic doors only take focus to explain a lock.
        public override bool CanInteract => base.CanInteract && (kind == DoorKind.Swing || IsLocked);

        public override void Interact()
        {
            if (IsLocked || kind != DoorKind.Swing) return;
            if (_wantOpen) _wantOpen = false;
            else Open();
        }

        public void Open()
        {
            if (IsLocked) return;
            if (!_wantOpen && _open < 0.05f && _player != null)
            {
                // Swing away from whoever opens it. Leaves extend along the hinge's +x, so a positive yaw swings
                // the free edge toward -z: use it when the opener stands on the +z side.
                Vector3 local = leaf.parent.InverseTransformPoint(_player.position);
                _direction = local.z > leaf.localPosition.z ? 1f : -1f;
            }
            _wantOpen = true;
            _openedAt = Time.time;
        }

        private void Update()
        {
            if (leaf == null) return;
            if (kind == DoorKind.AutoSlide)
            {
                bool near = _player != null && (_player.position - transform.position).sqrMagnitude < sensorRange * sensorRange;
                _wantOpen = near && !IsLocked;
            }
            else if (_wantOpen && Time.time - _openedAt > autoCloseSeconds && _player != null &&
                     (_player.position - leaf.position).sqrMagnitude > 2.2f * 2.2f)
            {
                _wantOpen = false;
            }

            float target = _wantOpen ? 1f : 0f;
            if (Mathf.Approximately(_open, target)) return;
            _open = Mathf.MoveTowards(_open, target, Time.deltaTime / seconds);
            float k = Mathf.SmoothStep(0f, 1f, _open);
            if (kind == DoorKind.Swing)
            {
                leaf.localRotation = Quaternion.Euler(0f, _direction * swingAngle * k, 0f);
            }
            else
            {
                leaf.localPosition = _closedA + Vector3.left * (slideDistance * k);
                if (leafB != null) leafB.localPosition = _closedB + Vector3.right * (slideDistance * k);
            }
        }

        private static string Capitalized(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
