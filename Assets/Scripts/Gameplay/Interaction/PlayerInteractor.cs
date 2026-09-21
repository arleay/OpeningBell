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

        private void Update()
        {
            Interactable found = null;
            Transform view = viewCamera.transform;
            if (Physics.Raycast(view.position, view.forward, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Collide))
            {
                found = hit.collider.GetComponentInParent<Interactable>();
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
