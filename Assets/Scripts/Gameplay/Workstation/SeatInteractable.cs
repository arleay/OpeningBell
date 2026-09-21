using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>Chair or computer: using it sits the player down at the workstation.</summary>
    public sealed class SeatInteractable : Interactable
    {
        [SerializeField] private string prompt = "Sit";
        [SerializeField] private WorkstationController workstation;

        public override string Prompt => prompt;
        public override bool CanInteract => base.CanInteract && workstation.State == WorkstationState.Standing;
        public override void Interact() => workstation.SitDown();
    }
}
