using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>Anything the player can aim at and use. Found via raycast on any collider under this object.</summary>
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>Verb shown in the HUD, e.g. "Sit".</summary>
        public abstract string Prompt { get; }

        public virtual bool CanInteract => isActiveAndEnabled;

        /// <summary>Optional extra lines shown under the prompt (specs of something for sale, a vehicle's state).</summary>
        public virtual string Details => null;

        public abstract void Interact();
    }
}
