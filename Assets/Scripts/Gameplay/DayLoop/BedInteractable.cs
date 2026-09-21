using UnityEngine;

namespace OpeningBell.Gameplay
{
    public sealed class BedInteractable : Interactable
    {
        [SerializeField] private SleepController sleep;

        public override string Prompt => sleep.CanSleep
            ? "Sleep"
            : "Sleep (after " + System.DateTime.Today.Add(sleep.EarliestBedtime).ToString("h tt", System.Globalization.CultureInfo.InvariantCulture) + ")";

        public override void Interact() => sleep.Sleep();
    }
}
