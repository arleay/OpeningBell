using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Walkable houses along <see cref="CityPlan.Frontages"/> (built by <see cref="HouseBuilder"/>), each on its own
    /// levelled lot facing its street at whatever angle the street runs: yard, driveway, side and back fences,
    /// trees. Lots are planned first (pure) so the terrain can be levelled under them before anything is built.
    /// </summary>
    public static class TownBuilder
    {
        private static float LotWidth(HouseTier t) => t switch { HouseTier.Starter => 17f, HouseTier.Family => 21f, _ => 42f };
        private static float Setback(HouseTier t) => t switch { HouseTier.Starter => 6.5f, HouseTier.Family => 7f, _ => 9f };
        /// <summary>From the sidewalk to the back fence.</summary>
        private static float YardDepth(HouseTier t) => t switch { HouseTier.Starter => 28f, HouseTier.Family => 30f, _ => 38f };
        private static float DoorX(HouseTier t) => t switch { HouseTier.Starter => -2f, HouseTier.Family => 3.2f, _ => 0f };

        public readonly struct Lot
        {
            /// <summary>Where the lot meets the sidewalk (centre of its frontage).</summary>
            public readonly Vector2 Front;
            /// <summary>Unit vector from the street into the lot.</summary>
            public readonly Vector2 Inward;
            public readonly float Width;
            public readonly float Y;
            public readonly HouseTier Tier;

            public Lot(Vector2 front, Vector2 inward, float width, float y, HouseTier tier)
            {
                Front = front;
                Inward = inward;
                Width = width;
                Y = y;
                Tier = tier;
            }

            /// <summary>Yaw that turns local +z into the lot (houses face local -z, the street).</summary>
            public float Yaw => Mathf.Atan2(Inward.x, Inward.y) * Mathf.Rad2Deg;
            public float Depth => YardDepth(Tier);
            public Pad Pad => new Pad(Front + Inward * (Depth / 2f), new Vector2(Width / 2f - 0.2f, Depth / 2f), Yaw, Y);
        }

        public static List<Lot> PlanLots(StreetMap map)
        {
            var lots = new List<Lot>();
            foreach (Frontage f in CityPlan.Frontages)
            {
                Vector2 dir = (f.To - f.From).normalized;
                Vector2 inward = new Vector2(-dir.y, dir.x) * f.Side;
                StreetMap.Segment street = map.Nearest((f.From + f.To) / 2f, out _, out _);
                float edge = street.HalfWidth + street.Sidewalk;
                // Keep clear of the cross streets at both ends.
                const float inset = 12f;
                float length = Vector2.Distance(f.From, f.To) - 2f * inset;
                if (length < 10f) continue;
                int count = Mathf.Max(1, Mathf.FloorToInt(length / LotWidth(f.Tier)));
                float width = length / count;
                for (int i = 0; i < count; i++)
                {
                    Vector2 centre = f.From + dir * (inset + width * (i + 0.5f));
                    street = map.Nearest(centre, out float s, out _);
                    lots.Add(new Lot(centre + inward * edge, inward, width, street.GradeAt(s), f.Tier));
                }
            }
            return lots;
        }

        public static void AddPads(CityContext c)
        {
            foreach (Lot lot in PlanLots(c.Roads.Map)) c.Pads.Add(lot.Pad);
        }

        public static void Build(CityContext c, Transform player)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Town");
            var lights = root.gameObject.AddComponent<HouseLights>();
            lights.Configure(player);
            var rng = new System.Random(2210);
            Material drive = c.P.Lit(new Color(0.55f, 0.55f, 0.53f), 0.05f);
            Material fence = c.P.Lit(new Color(0.86f, 0.84f, 0.78f), 0.1f);
            int houseNumber = 0;

            foreach (Lot lot in PlanLots(c.Roads.Map))
            {
                HouseTier tier = lot.Tier;
                Vector2 size = HouseBuilder.Footprint(tier);
                float setback = Setback(tier), depth = lot.Depth, half = lot.Width / 2f;
                // Local frame: origin on the sidewalk edge, +z into the lot, x along the street.
                Transform plot = Kit.Group(root, $"Lot {houseNumber + 1}", new Vector3(lot.Front.x, lot.Y, lot.Front.y), lot.Yaw);
                Transform house = Kit.Group(plot, $"House {++houseNumber} {tier}", new Vector3(0f, 0f, setback + size.y / 2f));
                HouseBuilder.Build(c, house, tier, rng, lights);
                c.Place(house.TransformPoint(new Vector3(DoorX(tier), 0f, -size.y / 2f - 1.5f)), PlaceKind.Door, "house");

                // Driveway from the sidewalk up the side of the lot.
                float side = rng.Next(2) == 0 ? -1f : 1f;
                float dx = side * (half - 2.2f);
                k.Span(plot, "Driveway", new Vector3(dx - 1.5f, -0.02f, 0f), new Vector3(dx + 1.5f, 0.015f, setback + 8f), drive, collider: false);
                // Side fence on one side (the neighbour has the other), from behind the house to the back fence.
                float fenceFrom = setback + size.y + 1.5f;
                if (depth > fenceFrom + 1f)
                {
                    k.Span(plot, "Fence", new Vector3(half - 0.05f, 0f, fenceFrom), new Vector3(half + 0.05f, 1.2f, depth), fence);
                    k.Span(plot, "Back fence", new Vector3(-half, 0f, depth - 0.05f), new Vector3(half, 1.2f, depth + 0.05f), fence);
                }
                // A tree in the back yard (clear of a mansion's pool), sometimes one out front.
                float yard = tier == HouseTier.Mansion ? half * 0.84f : (float)(rng.NextDouble() - 0.5) * lot.Width * 0.6f;
                YardTree(c, plot, rng, new Vector3(yard, 0f, depth - 3f));
                if (rng.NextDouble() < 0.35)
                    YardTree(c, plot, rng, new Vector3(-side * lot.Width * 0.3f, 0f, 2.5f), small: true);
            }
        }

        private static void YardTree(CityContext c, Transform parent, System.Random rng, Vector3 local, bool small = false)
        {
            float height = (small ? 4.5f : 8f) * (0.85f + 0.3f * (float)rng.NextDouble());
            string[] kinds = StreetBuilder.ParkTrees;
            GameObject tree = c.Kit.Model(parent, kinds[rng.Next(kinds.Length)], local, (float)rng.NextDouble() * 360f, height);
            if (tree == null) return;
            StreetBuilder.Trunk(tree);
        }
    }
}
