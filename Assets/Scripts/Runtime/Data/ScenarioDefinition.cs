using System.Collections.Generic;
using OpeningBell.Market;
using UnityEngine;

namespace OpeningBell
{
    /// <summary>Scripted content for a run (e.g. the onboarding day). Random news still happens around it.</summary>
    [CreateAssetMenu(menuName = "Opening Bell/Scenario", fileName = "Scenario")]
    public sealed class ScenarioDefinition : ScriptableObject
    {
        [SerializeField] private List<ScheduledNews> scheduledNews = new List<ScheduledNews>();

        public IReadOnlyList<ScheduledNews> ScheduledNews => scheduledNews;
    }
}
