using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum PlaceCategory
    {
        Home,
        Work,
        Shop,
        Food,
        Night,
        Leisure,
        Stand,
    }

    /// <summary>
    /// Where people are headed (TOWN_SPEC A11): not random wandering but a day. Mornings go to work, coffee and
    /// the bus; late mornings and afternoons to the shops and the park; lunch to food; evenings home or out to
    /// eat; nights to the bars, the club and the casino (and home). Only places that are open, and not across
    /// town: somewhere a short walk away.
    /// </summary>
    public sealed class Routines
    {
        private readonly List<(SidewalkGraph.Node Node, PlaceCategory Category, Hours? Hours)> _places = new List<(SidewalkGraph.Node, PlaceCategory, Hours?)>();
        private readonly Func<DateTime> _now;

        public Routines(SidewalkGraph graph, IReadOnlyDictionary<string, (PlaceCategory Category, Hours? Hours)> known, Func<DateTime> now)
        {
            _now = now;
            foreach (SidewalkGraph.Node n in graph.Nodes)
            {
                if (n.Kind == PlaceKind.Walkway) continue;
                if (n.Kind == PlaceKind.Stand) { _places.Add((n, PlaceCategory.Stand, known.TryGetValue(n.Tag ?? "", out var s) ? s.Hours : null)); continue; }
                if (n.Kind == PlaceKind.Bench) { _places.Add((n, PlaceCategory.Leisure, null)); continue; }
                var (category, hours) = Classify(n.Tag, known);
                _places.Add((n, category, hours));
            }
        }

        public static (PlaceCategory, Hours?) Classify(string tag, IReadOnlyDictionary<string, (PlaceCategory Category, Hours? Hours)> known)
        {
            if (tag != null && known.TryGetValue(tag, out var info)) return (info.Category, info.Hours);
            switch (tag)
            {
                case "house": case "apartments": case "apartment": case "Grove Terrace": case "Harborview Tower": case "trailer": case "cabin":
                    return (PlaceCategory.Home, null);
                case "bench": case "pier": case "overlook": case "campsite":
                    return (PlaceCategory.Leisure, null);
            }
            // Unnamed office fronts and the like: somewhere people work, office hours.
            return (PlaceCategory.Work, Hours.Of(8, 18));
        }

        /// <summary>What people want at this hour (weights per category).</summary>
        public static float Weight(PlaceCategory c, double hour)
        {
            if (hour < 6) return c switch { PlaceCategory.Home => 6f, PlaceCategory.Night => 3f, PlaceCategory.Stand => 1f, _ => 0.1f };
            if (hour < 9) return c switch { PlaceCategory.Work => 4f, PlaceCategory.Food => 2.5f, PlaceCategory.Stand => 2f, PlaceCategory.Shop => 1f, PlaceCategory.Home => 0.5f, _ => 0.3f };
            if (hour < 12) return c switch { PlaceCategory.Shop => 4f, PlaceCategory.Leisure => 2f, PlaceCategory.Food => 1.5f, PlaceCategory.Work => 1f, PlaceCategory.Home => 1.5f, _ => 0.5f };
            if (hour < 14) return c switch { PlaceCategory.Food => 4.5f, PlaceCategory.Shop => 2f, PlaceCategory.Leisure => 2.5f, PlaceCategory.Home => 1f, _ => 0.5f };
            if (hour < 17) return c switch { PlaceCategory.Shop => 3.5f, PlaceCategory.Leisure => 2.5f, PlaceCategory.Home => 2f, PlaceCategory.Food => 1f, PlaceCategory.Work => 1f, _ => 0.5f };
            if (hour < 20) return c switch { PlaceCategory.Home => 4f, PlaceCategory.Food => 2.5f, PlaceCategory.Shop => 1.5f, PlaceCategory.Night => 1f, PlaceCategory.Stand => 1f, _ => 0.3f };
            return c switch { PlaceCategory.Night => 4.5f, PlaceCategory.Home => 3f, PlaceCategory.Food => 1.5f, PlaceCategory.Stand => 1f, _ => 0.1f };
        }

        public static bool IsOpen(Hours? hours, DateTime now) => hours == null || hours.Value.Contains(now);

        /// <summary>A destination from <paramref name="from"/>: open, 40–320 m away, by what people want now. Null if nothing fits.</summary>
        public SidewalkGraph.Node Pick(SidewalkGraph.Node from, System.Random rng, bool thinning)
        {
            DateTime now = _now();
            double hour = now.TimeOfDay.TotalHours;
            var near = new List<(SidewalkGraph.Node Node, float Weight)>();
            float total = 0f;
            foreach (var (node, category, hours) in _places)
            {
                if (node == from) continue;
                float d = Vector2.Distance(node.P, from.P);
                if (d < 40f || d > 320f || !IsOpen(hours, now)) continue;
                // Thinning out (too many people about): send them home or indoors.
                float w = thinning ? (category == PlaceCategory.Home || category == PlaceCategory.Work ? 3f : 0.2f) : Weight(category, hour);
                near.Add((node, w));
                total += w;
            }
            if (near.Count == 0) return null;
            float roll = (float)rng.NextDouble() * total;
            foreach (var (node, w) in near)
            {
                roll -= w;
                if (roll <= 0f) return node;
            }
            return near[near.Count - 1].Node;
        }
    }
}
