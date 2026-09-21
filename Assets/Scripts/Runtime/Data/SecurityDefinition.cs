using OpeningBell.Market;
using UnityEngine;

namespace OpeningBell
{
    [CreateAssetMenu(menuName = "Opening Bell/Security Definition", fileName = "Security")]
    public sealed class SecurityDefinition : ScriptableObject
    {
        [SerializeField] private SecuritySpec spec = new SecuritySpec();

        /// <summary>Static data. The simulation copies it; never write live values here.</summary>
        public SecuritySpec Spec => spec;
    }
}
