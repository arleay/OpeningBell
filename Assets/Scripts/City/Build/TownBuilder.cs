using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Houses along <see cref="CityPlan.Frontages"/>, each on its own levelled lot facing its street at whatever angle
    /// the street runs (TOWN_SPEC A7). Some are walkable (<see cref="HouseBuilder"/>, the ones that can come up
    /// for sale); the rest are Kenney suburban houses, so the streets vary. Every lot gets a yard (driveway, fences,
    /// a tree) and its own clutter, kept up or let go (<see cref="Residential.Yard"/>). Lots are planned first
    /// (pure) so the terrain can be levelled under them before anything is built.
    /// </summary>
    public static class TownBuilder
    {
        private static float LotWidth(HouseTier t) => t switch { HouseTier.Starter => 17f, HouseTier.Family => 21f, _ => 42f };
        private static float TierSetback(HouseTier t) => t switch { HouseTier.Starter => 6.5f, HouseTier.Family => 7f, _ => 9f };
        /// <summary>From the sidewalk to the back fence.</summary>
        private static float YardDepth(HouseTier t) => t switch { HouseTier.Starter => 28f, HouseTier.Family => 30f, _ => 38f };
        private static float DoorX(HouseTier t) => t switch { HouseTier.Starter => -2f, HouseTier.Family => 3.2f, _ => 0f };
        /// <summary>Share of lots with a walkable house (percent); mansions all are.</summary>
        private static int WalkablePercent(HouseTier t) => t switch { HouseTier.Starter => 22, HouseTier.Family => 28, _ => 100 };

        private static readonly string[] KitHouses =
        {
            "building-type-a", "building-type-b", "building-type-c", "building-type-d", "building-type-e", "building-type-f", "building-type-g",
            "building-type-h", "building-type-i", "building-type-j", "building-type-k", "building-type-l", "building-type-m", "building-type-n",
            "building-type-o", "building-type-p", "building-type-q", "building-type-r", "building-type-s", "building-type-t", "building-type-u",
        };

        public readonly struct Lot
        {
            /// <summary>Where the lot meets the sidewalk (centre of its frontage).</summary>
            public readonly Vector2 Front;
            /// <summary>Unit vector from the street into the lot.</summary>
            public readonly Vector2 Inward;
            public readonly float Width, Depth, Y;
            public readonly HouseTier Tier;
            public readonly int Index;

            public Lot(Vector2 front, Vector2 inward, float width, float depth, float y, HouseTier tier, int index)
            {
                Front = front;
                Inward = inward;
                Width = width;
                Depth = depth;
                Y = y;
                Tier = tier;
                Index = index;
            }

            /// <summary>Yaw that turns local +z into the lot (houses face local -z, the street).</summary>
            public float Yaw => Mathf.Atan2(Inward.x, Inward.y) * Mathf.Rad2Deg;
            public Pad Pad => new Pad(Front + Inward * (Depth / 2f), new Vector2(Width / 2f - 0.2f, Depth / 2f), Yaw, Y);
            public bool Walkable => (Index * 7919 + 13) % 100 < WalkablePercent(Tier);
            /// <summary>Set back from the sidewalk, less on shallow lots so the house fits.</summary>
            public float Setback => Mathf.Clamp(Depth - HouseBuilder.Footprint(Tier).y - 1f, 3.5f, TierSetback(Tier));
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
                float depth = f.Depth > 0f ? f.Depth : YardDepth(f.Tier);
                for (int i = 0; i < count; i++)
                {
                    Vector2 centre = f.From + dir * (inset + width * (i + 0.5f));
                    street = map.Nearest(centre, out float s, out _);
                    lots.Add(new Lot(centre + inward * edge, inward, width, depth, street.GradeAt(s), f.Tier, lots.Count));
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
            Material fenceOld = c.P.Lit(new Color(0.55f, 0.5f, 0.42f), 0.05f);
            int houseNumber = 0;

            foreach (Lot lot in PlanLots(c.Roads.Map))
            {
                HouseTier tier = lot.Tier;
                Vector2 size = HouseBuilder.Footprint(tier);
                float setback = lot.Setback, depth = lot.Depth, half = lot.Width / 2f;
                // Poorer streets are more often let go; the south side more than the hill.
                bool rundown = rng.NextDouble() < (tier == HouseTier.Starter ? 0.35 : tier == HouseTier.Family ? 0.12 : 0.0);
                // Local frame: origin on the sidewalk edge, +z into the lot, x along the street.
                Transform plot = Kit.Group(root, $"Lot {houseNumber + 1}", new Vector3(lot.Front.x, lot.Y, lot.Front.y), lot.Yaw);
                Transform house = Kit.Group(plot, $"House {++houseNumber} {tier}", new Vector3(0f, 0f, setback + size.y / 2f));
                if (lot.Walkable || !KitHouse(c, house, size, rng))
                {
                    HouseBuilder.Build(c, house, tier, rng, lights);
                    c.Place(house.TransformPoint(new Vector3(DoorX(tier), 0f, -size.y / 2f - 1.5f)), PlaceKind.Door, "house");
                }
                else c.Place(house.TransformPoint(new Vector3(0f, 0f, -size.y / 2f - 1.5f)), PlaceKind.Door, "house");

                // Driveway from the sidewalk up the side of the lot.
                float side = rng.Next(2) == 0 ? -1f : 1f;
                float dx = side * (half - 2.2f);
                k.Span(plot, "Driveway", new Vector3(dx - 1.5f, -0.02f, 0f), new Vector3(dx + 1.5f, 0.015f, Mathf.Min(depth - 1f, setback + 8f)), drive, collider: false);
                // Side fence on one side (the neighbour has the other), from behind the house to the back fence.
                float fenceFrom = setback + size.y + 1.5f;
                if (depth > fenceFrom + 1f)
                {
                    Material fm = rundown ? fenceOld : fence;
                    k.Span(plot, "Fence", new Vector3(half - 0.05f, 0f, fenceFrom), new Vector3(half + 0.05f, 1.2f, depth), fm);
                    if (!rundown || rng.NextDouble() < 0.5) k.Span(plot, "Back fence", new Vector3(-half, 0f, depth - 0.05f), new Vector3(half, 1.2f, depth + 0.05f), fm);
                }
                // A tree in the back yard (clear of a mansion's pool), sometimes one out front.
                float yard = tier == HouseTier.Mansion ? half * 0.84f : (float)(rng.NextDouble() - 0.5) * lot.Width * 0.6f;
                if (depth > setback + size.y + 4f) YardTree(c, plot, rng, new Vector3(yard, 0f, depth - 3f));
                if (rng.NextDouble() < 0.35)
                    YardTree(c, plot, rng, new Vector3(-side * lot.Width * 0.3f, 0f, 2.5f), small: true);
                Residential.Yard(c, plot, lot.Width, depth, setback, size, dx, rundown, rng);
            }
        }

        /// <summary>A Kenney suburban house fitted to the footprint, street side facing -z. False if the kit is missing.</summary>
        private static bool KitHouse(CityContext c, Transform house, Vector2 size, System.Random rng)
        {
            string model = KitHouses[rng.Next(KitHouses.Length)];
            // The kit's houses face +z: turn them round to face the street.
            GameObject go = c.Kit.Fit(house, model, new Vector3(0f, 0f, 0f), new Vector3(size.x + 1f, 0f, size.y + 1f), 180f);
            if (go == null) return false;
            var bounds = new Bounds(house.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            var box = house.gameObject.AddComponent<BoxCollider>();
            box.center = house.InverseTransformPoint(bounds.center);
            Vector3 local = house.InverseTransformVector(bounds.size);
            box.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            return true;
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
