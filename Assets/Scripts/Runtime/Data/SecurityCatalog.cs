using System.Collections.Generic;
using OpeningBell.Market;
using UnityEngine;

namespace OpeningBell
{
    /// <summary>The tradable universe and its index.</summary>
    [CreateAssetMenu(menuName = "Opening Bell/Security Catalog", fileName = "SecurityCatalog")]
    public sealed class SecurityCatalog : ScriptableObject
    {
        [SerializeField] private IndexSpec index = new IndexSpec();
        [SerializeField] private List<SecurityDefinition> securities = new List<SecurityDefinition>();

        public IndexSpec Index => index;
        public IReadOnlyList<SecurityDefinition> Securities => securities;

        public List<SecuritySpec> CreateSpecs()
        {
            var specs = new List<SecuritySpec>(securities.Count);
            foreach (var definition in securities)
            {
                if (definition == null)
                {
                    Debug.LogError($"{name} has an empty security slot.", this);
                    continue;
                }
                specs.Add(definition.Spec);
            }
            return specs;
        }
    }
}
