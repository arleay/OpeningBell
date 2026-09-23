using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The town around the core: rows of walkable houses (<see cref="CityPlan.HouseRows"/>, built by
    /// <see cref="HouseBuilder"/>) set back behind front lawns, each with a driveway, side and back fences and yard
    /// trees, plus the open town field. Every house's front door is a place pedestrians walk to and from.
    /// </summary>
    public static class TownBuilder
    {

        private static float LotWidth(HouseTier t) => t switch { HouseTier.Starter => 17f, HouseTier.Family => 21f, _ => 42f };
        private static float Setback(HouseTier t) => t switch { HouseTier.Starter => 6.5f, HouseTier.Family => 7f, _ => 9f };
        /// <summary>From the kerb to the back fence; blocks are ~79 m deep inside the sidewalks, so two rows fit back to back.</summary>
        private static float YardDepth(HouseTier t) => t switch { HouseTier.Starter => 30f, HouseTier.Family => 32f, _ => 39f };
        private static float DoorX(HouseTier t) => t switch { HouseTier.Starter => -2f, HouseTier.Family => 3.2f, _ => 0f };

        public static void Build(CityContext c, Transform player)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Town");
            var lights = root.gameObject.AddComponent<HouseLights>();
            lights.Configure(player);
            var rng = new System.Random(2210);
            Material drive = c.P.Lit(new Color(0.55f, 0.55f, 0.53f), 0.05f);
            Material fence = c.P.Lit(new Color(0.86f, 0.84f, 0.78f), 0.1f);
            float sw = CityPlan.SidewalkWidth;
            int houseNumber = 0;

            foreach (var (blockName, x0, x1, facesNorth, tier) in CityPlan.HouseRows)
            {
                Rect block = CityPlan.Block(blockName);
                Transform row = Kit.Group(root, blockName + (facesNorth ? " north" : " south"));
                // The street side of the row, and which way is "into the block" from it.
                float kerb = facesNorth ? block.yMax - sw : block.yMin + sw;
                float inward = facesNorth ? -1f : 1f;
                float start = Mathf.Max(x0, block.xMin) + sw + 2f, end = Mathf.Min(x1, block.xMax) - sw - 2f;
                int lots = Mathf.Max(1, Mathf.FloorToInt((end - start) / LotWidth(tier)));
                float width = (end - start) / lots;
                Vector2 size = HouseBuilder.Footprint(tier);
                float setback = Setback(tier), back = kerb + inward * YardDepth(tier);

                for (int i = 0; i < lots; i++)
                {
                    float cx = start + width * (i + 0.5f);
                    // The house faces the street: local -z towards the kerb.
                    float front = kerb + inward * setback;
                    Transform house = Kit.Group(row, $"House {++houseNumber} {tier}", new Vector3(cx, 0f, front + inward * size.y / 2f), facesNorth ? 180f : 0f);
                    HouseBuilder.Build(c, house, tier, rng, lights);
                    c.Place(house.TransformPoint(new Vector3(DoorX(tier), 0f, -size.y / 2f - 1.5f)), PlaceKind.Door, "house");

                    // Driveway from the sidewalk up the side of the lot.
                    float side = rng.Next(2) == 0 ? -1f : 1f;
                    float dx = cx + side * (width / 2f - 2.2f);
                    float driveEnd = kerb + inward * (setback + 8f);
                    k.Span(row, "Driveway", new Vector3(dx - 1.5f, -0.02f, Mathf.Min(kerb, driveEnd)), new Vector3(dx + 1.5f, 0.015f, Mathf.Max(kerb, driveEnd)), drive, collider: false);
                    // Side fence between lots, from behind the houses to the back fence.
                    float fenceFrom = kerb + inward * (setback + size.y + 1.5f);
                    if (i < lots - 1)
                        k.Span(row, "Fence", new Vector3(start + width * (i + 1) - 0.05f, 0f, Mathf.Min(back, fenceFrom)),
                            new Vector3(start + width * (i + 1) + 0.05f, 1.2f, Mathf.Max(back, fenceFrom)), fence);
                    // A tree in the back yard (clear of a mansion's pool), sometimes one out front.
                    float yard = tier == HouseTier.Mansion ? width * 0.42f : (float)(rng.NextDouble() - 0.5) * width * 0.6f;
                    YardTree(c, row, rng, new Vector2(cx + yard, back - inward * 3f));
                    if (rng.NextDouble() < 0.35)
                        YardTree(c, row, rng, new Vector2(cx - side * width * 0.3f, kerb + inward * 2.5f), small: true);
                }
                k.Span(row, "Back fence", new Vector3(start, 0f, back - 0.05f), new Vector3(end, 1.2f, back + 0.05f), fence);
            }

            // The town field: open grass with a few big trees, kept clear for later (a market, a sports pitch).
            Rect field = CityPlan.TownField;
            for (int i = 0; i < 9; i++)
                YardTree(c, root, rng, new Vector2(Mathf.Lerp(field.xMin + 4f, field.xMax - 4f, (float)rng.NextDouble()),
                    Mathf.Lerp(field.yMin + 4f, field.yMax - 4f, (float)rng.NextDouble())));
        }

        private static void YardTree(CityContext c, Transform parent, System.Random rng, Vector2 p, bool small = false)
        {
            float height = (small ? 4.5f : 8f) * (0.85f + 0.3f * (float)rng.NextDouble());
            string[] kinds = StreetBuilder.ParkTrees;
            GameObject tree = c.Kit.Model(parent, kinds[rng.Next(kinds.Length)], new Vector3(p.x, 0f, p.y), (float)rng.NextDouble() * 360f, height);
            if (tree == null) return;
            StreetBuilder.Trunk(tree);
        }
    }
}
