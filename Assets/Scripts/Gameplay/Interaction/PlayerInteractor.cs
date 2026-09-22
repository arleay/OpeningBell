using System;
using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>Raycasts from the view centre to find the focused Interactable; Interact uses it.</summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private GameInput input;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private float reach = 2.2f;
        [SerializeField] private LayerMask mask = ~0;

        public Interactable Current { get; private set; }
        public event Action<Interactable> FocusChanged;

        private readonly RaycastHit[] _hits = new RaycastHit[8];

        private void Update()
        {
            Interactable found = null;
            Transform view = viewCamera.transform;
            // Nearest hit that isn't the player's own body (the camera can sit inside the controller's skin).
            int count = Physics.RaycastNonAlloc(view.position, view.forward, _hits, reach, mask, QueryTriggerInteraction.Collide);
            float nearest = float.MaxValue;
            Collider first = null;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform.root) || _hits[i].distance >= nearest) continue;
                nearest = _hits[i].distance;
                first = _hits[i].collider;
            }
            if (first != null)
            {
                found = first.GetComponentInParent<Interactable>();
                if (found != null && !found.CanInteract) found = null;
            }

            SetFocus(found);
            if (Current != null && input.Interact.WasPressedThisFrame()) Current.Interact();
        }

        private void OnDisable() => SetFocus(null);

        private void SetFocus(Interactable target)
        {
            if (target == Current) return;
            Current = target;
            FocusChanged?.Invoke(target);
        }
    }
}
