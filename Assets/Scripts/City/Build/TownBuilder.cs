using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The town around the core: rows of Kenney suburban houses (<see cref="CityPlan.HouseRows"/>) set back behind
    /// front lawns, each with a driveway, a back fence and a tree or two in the yard, plus the open town field. One
    /// box collider per house. Without the suburban kit the lots stay lawn.
    /// </summary>
    public static class TownBuilder
    {
        /// <summary>Kit houses are ~1.3 units wide; ×8 gives 9–14 m houses with ~3 m storeys.</summary>
        private const float Scale = 8f;
        private const float LotWidth = 17f, Setback = 6.5f, YardDepth = 30f;

        private static readonly string[] Houses =
        {
            "building-type-a", "building-type-b", "building-type-c", "building-type-d", "building-type-e", "building-type-f",
            "building-type-g", "building-type-h", "building-type-i", "building-type-j", "building-type-k", "building-type-l",
            "building-type-m", "building-type-n", "building-type-o", "building-type-p", "building-type-q", "building-type-r",
            "building-type-s", "building-type-t", "building-type-u",
        };

        private static readonly string[] Palettes = { "colormap", "variation-a", "variation-b", "variation-c" };
        private static readonly string[] Trees = { "tree_oak", "tree_default", "tree_fat", "tree_detailed", "tree_oak_dark", "tree_default_dark" };

        public static void Build(CityContext c)
        {
            Kit k = c.Kit;
            if (k.Art == null || k.Art.Model(Houses[0]) == null) return;
            Transform root = Kit.Group(c.Static, "Town");
            var rng = new System.Random(2210);
            Material drive = c.P.Lit(new Color(0.55f, 0.55f, 0.53f), 0.05f);
            Material fence = c.P.Lit(new Color(0.86f, 0.84f, 0.78f), 0.1f);
            float sw = CityPlan.SidewalkWidth;

            foreach (var (blockName, x0, x1, facesNorth) in CityPlan.HouseRows)
            {
                Rect block = CityPlan.Block(blockName);
                Transform row = Kit.Group(root, blockName + (facesNorth ? " north" : " south"));
                // The street side of the row, and which way is "into the block" from it.
                float kerb = facesNorth ? block.yMax - sw : block.yMin + sw;
                float inward = facesNorth ? -1f : 1f;
                float start = Mathf.Max(x0, block.xMin) + sw + 2f, end = Mathf.Min(x1, block.xMax) - sw - 2f;
                int lots = Mathf.Max(1, Mathf.FloorToInt((end - start) / LotWidth));
                float width = (end - start) / lots;
                float back = kerb + inward * YardDepth;

                for (int i = 0; i < lots; i++)
                {
                    float cx = start + width * (i + 0.5f);
                    House(c, row, rng, cx, kerb + inward * Setback, facesNorth);
                    // Driveway from the sidewalk to the side of the house.
                    float side = rng.Next(2) == 0 ? -1f : 1f;
                    float dx = cx + side * (width / 2f - 2.2f);
                    k.Span(row, "Driveway", new Vector3(dx - 1.5f, -0.02f, Mathf.Min(kerb, kerb + inward * (Setback + 7f))),
                        new Vector3(dx + 1.5f, 0.015f, Mathf.Max(kerb, kerb + inward * (Setback + 7f))), drive, collider: false);
                    // Side fence between this lot and the next, from behind the house to the back fence.
                    if (i < lots - 1)
                        k.Span(row, "Fence", new Vector3(start + width * (i + 1) - 0.05f, 0f, Mathf.Min(back, kerb + inward * 17f)),
                            new Vector3(start + width * (i + 1) + 0.05f, 1.2f, Mathf.Max(back, kerb + inward * 17f)), fence);
                    // A tree in the back yard, sometimes one out front.
                    YardTree(c, row, rng, new Vector2(cx + (float)(rng.NextDouble() - 0.5) * width * 0.6f, kerb + inward * (22f + (float)rng.NextDouble() * 5f)));
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

        private static void House(CityContext c, Transform parent, System.Random rng, float cx, float front, bool facesNorth)
        {
            string name = Houses[rng.Next(Houses.Length)];
            Bounds b = c.Kit.Art.ModelBounds(name);
            // Kit houses face +z; turn them to the street. The front face goes on the setback line.
            float yaw = facesNorth ? 0f : 180f;
            float dir = facesNorth ? 1f : -1f;
            var at = new Vector3(cx - dir * b.center.x * Scale, 0f, front - dir * b.max.z * Scale);
            GameObject house = c.Kit.Model(parent, name, at, yaw, Scale);
            string variant = Palettes[rng.Next(Palettes.Length)];
            Material m = c.P.KitPalette(c.Kit.Art.Palette("CitySuburban/" + variant) ?? c.Kit.Art.Palette("CitySuburban/colormap"),
                c.Kit.Art.Palette("CitySuburban/" + variant + "-glow"), new Color(0.95f, 0.94f, 0.92f));
            foreach (Renderer r in house.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
            c.Kit.Solid(house);
        }

        private static void YardTree(CityContext c, Transform parent, System.Random rng, Vector2 p, bool small = false)
        {
            float scale = (small ? 2.6f : 4.2f) * (0.85f + 0.3f * (float)rng.NextDouble());
            GameObject tree = c.Kit.Model(parent, Trees[rng.Next(Trees.Length)], new Vector3(p.x, 0f, p.y), (float)rng.NextDouble() * 360f, scale);
            if (tree == null) return;
            StreetBuilder.Naturalize(c.Kit, tree);
            var trunk = tree.AddComponent<CapsuleCollider>(); // model units: the trunk's lower 0.6
            trunk.radius = 0.04f;
            trunk.height = 0.6f;
            trunk.center = new Vector3(0f, 0.3f, 0f);
        }
    }
}
