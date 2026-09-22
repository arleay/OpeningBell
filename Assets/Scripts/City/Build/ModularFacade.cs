using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// An outside-only box (the floors above a shop, an office tower) clad in Kenney Modular Buildings wall
    /// modules: one module per bay per storey on every face, a window style chosen per building with the odd
    /// balcony or awning, and a cornice on top. The box itself stays as the collider and the roof. Without the kit
    /// it falls back to the procedural facade volume.
    /// </summary>
    public static class ModularFacade
    {
        /// <summary>Kit units per metre: a module is one unit wide and 0.63 tall, so 5 m per unit gives 3.15 m storeys.</summary>
        private const float Scale = 5f;
        private const float StoreyHeight = 3.15f;
        private const float BayWidth = 3.6f;
        private const float Thickness = 0.4f;

        /// <summary>The modules' window side faces local +z.</summary>
        private const float ModelYaw = 180f;

        private static readonly string[][] WindowSets =
        {
            new[] { "building-window-sill", "building-window-sill", "building-window-balcony" },
            new[] { "building-windows", "building-windows-sills", "building-windows" },
            new[] { "building-window", "building-window", "building-window-awnings" },
            new[] { "building-windows-round", "building-windows-sills-round", "building-windows-round" },
            new[] { "building-window-wide", "building-window-wide-sill", "building-window-wide" },
        };

        private static readonly string[] Office = { "building-window-large", "building-window-large", "building-windows-high-middle" };

        public static void Build(CityContext c, Transform parent, string name, Vector3 min, Vector3 max, FacadeStyle style, int seed)
        {
            Kit k = c.Kit;
            Material roof = c.P.Lit(new Color(0.24f, 0.24f, 0.25f));
            CityArt art = k.Art;
            if (art == null || art.Model("building-window") == null)
            {
                k.Facade(parent, name, min, max, c.P.Facade(style, false), roof);
                return;
            }

            Transform root = Kit.Group(parent, name);
            Material wall = Palette(c, art, style);
            // The core: collider, roof and whatever shows between modules. Its sides match the kit's wall colour.
            k.Span(root, "Core", min + new Vector3(0.05f, 0f, 0.05f), max - new Vector3(0.05f, 0f, 0.05f), roof);
            k.Span(root, "Roof", new Vector3(min.x + 0.1f, max.y - 0.02f, min.z + 0.1f), new Vector3(max.x - 0.1f, max.y + 0.01f, max.z - 0.1f), roof, collider: false);

            var rng = new System.Random(seed);
            string[] set = style == FacadeStyle.Concrete || style == FacadeStyle.Glass ? Office : WindowSets[rng.Next(WindowSets.Length)];
            float height = max.y - min.y;
            int storeys = Mathf.Max(1, Mathf.RoundToInt(height / StoreyHeight));
            float storey = height / storeys;

            // Faces: (start corner, direction along the face, outward normal).
            var faces = new (Vector2 From, Vector2 Along, Vector2 Out, float Length)[]
            {
                (new Vector2(min.x, min.z), Vector2.right, Vector2.down, max.x - min.x),
                (new Vector2(max.x, max.z), Vector2.left, Vector2.up, max.x - min.x),
                (new Vector2(min.x, max.z), Vector2.down, Vector2.left, max.z - min.z),
                (new Vector2(max.x, min.z), Vector2.up, Vector2.right, max.z - min.z),
            };
            foreach (var (from, along, outward, length) in faces)
            {
                int bays = Mathf.Max(1, Mathf.RoundToInt(length / BayWidth));
                float bay = length / bays;
                float yaw = Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg + ModelYaw;
                for (int s = 0; s < storeys; s++)
                {
                    for (int b = 0; b < bays; b++)
                    {
                        // Mostly the building's main window; accents on upper storeys only, never at the ends.
                        bool accent = s > 0 && b > 0 && b < bays - 1 && rng.NextDouble() < 0.18;
                        string module = accent ? set[2] : set[rng.NextDouble() < 0.7 ? 0 : 1];
                        Vector2 p = from + along * ((b + 0.5f) * bay) + outward * (Thickness / 2f - 0.02f);
                        GameObject go = k.Model(root, module, new Vector3(p.x, min.y + s * storey, p.y), yaw,
                            new Vector3(bay, storey / 0.63f, Thickness));
                        if (go == null) continue;
                        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = wall;
                    }
                }
            }

            // Cornice: a trim band proud of the modules, like the kit's roof borders.
            Material trim = c.P.Lit(style == FacadeStyle.Concrete ? new Color(0.36f, 0.37f, 0.4f) : new Color(0.72f, 0.42f, 0.3f));
            k.Span(root, "Cornice", new Vector3(min.x - Thickness - 0.1f, max.y - 0.05f, min.z - Thickness - 0.1f),
                new Vector3(max.x + Thickness + 0.1f, max.y + 0.35f, max.z + Thickness + 0.1f), trim, collider: false);
        }

        /// <summary>Warm beige for brick and stucco, the grey variant for offices.</summary>
        private static Material Palette(CityContext c, CityArt art, FacadeStyle style)
        {
            string variant = style == FacadeStyle.Concrete || style == FacadeStyle.Glass ? "variation-b" : "colormap";
            Texture2D palette = art.Palette("ModularBuildings/" + variant) ?? art.Palette("ModularBuildings/colormap");
            return c.P.KitPalette(palette, art.Palette("ModularBuildings/" + variant + "-glow"), Color.white);
        }
    }
}
