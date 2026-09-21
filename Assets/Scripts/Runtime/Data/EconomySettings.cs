using System.Collections.Generic;
using OpeningBell.Economy;
using UnityEngine;

namespace OpeningBell
{
    [CreateAssetMenu(menuName = "Opening Bell/Economy Settings", fileName = "EconomySettings")]
    public sealed class EconomySettings : ScriptableObject
    {
        [SerializeField] private EconomyConfig config = new EconomyConfig();
        [SerializeField] private List<StoreItem> storeItems = new List<StoreItem>();

        public EconomyConfig Config => config;
        public IReadOnlyList<StoreItem> StoreItems => storeItems;
    }
}
