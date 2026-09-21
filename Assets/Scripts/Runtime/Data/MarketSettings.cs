using OpeningBell.Market;
using UnityEngine;

namespace OpeningBell
{
    [CreateAssetMenu(menuName = "Opening Bell/Market Settings", fileName = "MarketSettings")]
    public sealed class MarketSettings : ScriptableObject
    {
        [SerializeField] private MarketConfig config = new MarketConfig();

        public MarketConfig Config => config;
    }
}
