using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>Chair or computer: using it sits the player down at the workstation.</summary>
    public sealed class SeatInteractable : Interactable
    {
        [SerializeField] private string prompt = "Sit";
        [SerializeField] private WorkstationController workstation;
        [Tooltip("Empty = the home desk.")]
        [SerializeField, Optional] private Desk desk;

        /// <summary>For seats built in code (city generator).</summary>
        public void Configure(WorkstationController owner, Desk target, string verb = "Sit")
        {
            workstation = owner;
            desk = target;
            prompt = verb;
        }

        public override string Prompt => prompt;
        public override bool CanInteract => base.CanInteract && workstation.State == WorkstationState.Standing;
        public override void Interact() => workstation.SitDown(desk);
    }
}
