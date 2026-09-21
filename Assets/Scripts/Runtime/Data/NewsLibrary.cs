using System.Collections.Generic;
using OpeningBell.Market;
using UnityEngine;

namespace OpeningBell
{
    /// <summary>All headline templates the news engine can draw from.</summary>
    [CreateAssetMenu(menuName = "Opening Bell/News Library", fileName = "NewsLibrary")]
    public sealed class NewsLibrary : ScriptableObject
    {
        [SerializeField] private List<NewsTemplate> templates = new List<NewsTemplate>();

        public IReadOnlyList<NewsTemplate> Templates => templates;
    }
}
