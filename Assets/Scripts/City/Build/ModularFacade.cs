using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// An outside-only building volume (the floors above a shop, an office tower, a background block): a solid core
    /// clad in a photographed finish (brick, painted render, siding, concrete) and dressed bay by bay with real trim:
    /// framed glass, sills, lintels, string courses between storeys, a cornice, a darker plinth where it meets the
    /// ground, and a shopfront storey when asked. All trim of one material is merged into one mesh per building, so a
    /// block costs a handful of renderers however many windows it has. Replaces the Kenney modular wall kit, whose
    /// flat palette colours and chunky frames read as toy-like next to the textured streets.
    /// </summary>
    public static class ModularFacade
    {
        private const float StoreyHeight = 3.2f;
        private const float BayWidth = 3.4f;
        /// <summary>Ground storey of a shopfront building (matches ShellBuilder's storefront band).</summary>
        public const float ShopStorey = 4.2f;

        /// <summary>What a style is made of and how its windows are proportioned (fractions of a bay / storey).</summary>
        private sealed class Look
        {
            public Material Wall, Trim, Frame, Plinth, Roof;
            public float WindowWidth, WindowHeight, SillHeight;
            public bool Courses, Lintels;
        }

        public static void Build(CityContext c, Transform parent, string name, Vector3 min, Vector3 max, FacadeStyle style, int seed, bool storefront = false)
        {
            Kit k = c.Kit;
            var rng = new System.Random(seed);
            Look look = LookFor(c.P, style, rng);
            Transform root = Kit.Group(parent, name);
            k.Span(root, "Walls", min, max, look.Wall);
            k.Span(root, "Roof", new Vector3(min.x + 0.05f, max.y, min.z + 0.05f), new Vector3(max.x - 0.05f, max.y + 0.03f, max.z - 0.05f), look.Roof, collider: false);

            var frames = new MeshBuilder();
            var trim = new MeshBuilder();
            var plinth = new MeshBuilder();
            var dark = new MeshBuilder();
            var lit = new MeshBuilder();
            var units = new MeshBuilder();
            var pipes = new MeshBuilder();
            bool homes = style != FacadeStyle.Concrete && style != FacadeStyle.Glass;

            float height = max.y - min.y;
            bool shop = storefront && height > ShopStorey + 2f;
            float upperFrom = shop ? ShopStorey : 0f;
            int storeys = Mathf.Max(1, Mathf.RoundToInt((height - upperFrom) / StoreyHeight));
            float storey = (height - upperFrom) / storeys;
            bool grounded = min.y < 0.5f;

            var faces = new (Vector2 From, Vector2 Along, Vector2 Out, float Length)[]
            {
                (new Vector2(min.x, min.z), Vector2.right, Vector2.down, max.x - min.x),
                (new Vector2(max.x, max.z), Vector2.left, Vector2.up, max.x - min.x),
                (new Vector2(min.x, max.z), Vector2.down, Vector2.left, max.z - min.z),
                (new Vector2(max.x, min.z), Vector2.up, Vector2.right, max.z - min.z),
            };
            foreach (var (from, along, outward, length) in faces)
            {
                Vector2 mid = from + along * (length / 2f);
                int bays = Mathf.Max(1, Mathf.RoundToInt(length / BayWidth));
                float bay = length / bays;
                if (shop) Shopfront(frames, trim, dark, from, along, outward, length, bays, min.y);
                for (int s = 0; s < storeys; s++)
                {
                    float y0 = min.y + upperFrom + s * storey;
                    if (look.Courses && (s > 0 || shop)) Band(trim, mid, along, outward, length / 2f + 0.04f, 0.04f, y0 - 0.06f, y0 + 0.08f);
                    for (int b = 0; b < bays; b++)
                    {
                        Vector2 at = from + along * ((b + 0.5f) * bay);
                        float w = bay * look.WindowWidth, h = storey * look.WindowHeight;
                        float sill = y0 + storey * look.SillHeight;
                        // Roughly a third of the rooms have someone home after dark.
                        Window(rng.NextDouble() < 0.35 ? lit : dark, frames, trim, look, at, along, outward, w, sill, sill + h);
                        // Flats: the odd window air conditioner hung under the sill.
                        if (homes && rng.NextDouble() < 0.12)
                            Box(units, at + outward * 0.25f, along, outward, 0.33f, 0.25f, sill - 0.5f, sill - 0.1f);
                    }
                }
                // Cornice along the top, proud of the wall; a darker plinth course where the wall meets the ground.
                Band(trim, mid, along, outward, length / 2f + 0.22f, 0.22f, max.y - 0.32f, max.y + 0.08f);
                if (grounded) Band(plinth, mid, along, outward, length / 2f + 0.05f, 0.05f, min.y - 0.1f, min.y + 0.45f);
                // A downpipe from the roof at one corner of each face (render and brick buildings drain outside).
                if (homes)
                {
                    Vector2 corner = from + along * 0.35f + outward * 0.08f;
                    Box(pipes, corner, along, outward, 0.05f, 0.05f, min.y, max.y - 0.3f);
                }
            }
            // Roof plant: condensers and vent boxes, seen from the taller buildings and the penthouse.
            int plant = 1 + rng.Next(Mathf.Clamp(Mathf.RoundToInt((max.x - min.x) * (max.z - min.z) / 150f), 1, 5));
            for (int i = 0; i < plant; i++)
            {
                float x = Mathf.Lerp(min.x + 1.5f, max.x - 1.5f, (float)rng.NextDouble());
                float z = Mathf.Lerp(min.z + 1.5f, max.z - 1.5f, (float)rng.NextDouble());
                float sx = 0.6f + (float)rng.NextDouble() * 0.8f, sz = 0.6f + (float)rng.NextDouble() * 0.8f;
                units.Cuboid(new Vector3(x - sx, max.y, z - sz), new Vector3(x + sx, max.y + 0.8f + (float)rng.NextDouble() * 0.9f, z + sz));
            }

            frames.Build(root, "Window frames", look.Frame, collider: false);
            trim.Build(root, "Trim", look.Trim, collider: false);
            plinth.Build(root, "Plinth", look.Plinth, collider: false);
            dark.Build(root, "Glass", c.P.Pane(false), collider: false);
            lit.Build(root, "Glass (lit at night)", c.P.Pane(true), collider: false);
            units.Build(root, "Units", c.P.Lit(new Color(0.7f, 0.71f, 0.69f), 0.3f), collider: false);
            pipes.Build(root, "Downpipes", c.P.Lit(new Color(0.26f, 0.26f, 0.27f), 0.3f), collider: false);
        }

        /// <summary>One framed window on a face: glass just proud of the wall, a frame round it, a sill and (brick) a lintel.</summary>
        private static void Window(MeshBuilder glass, MeshBuilder frames, MeshBuilder trim, Look look, Vector2 at, Vector2 along, Vector2 outward, float w, float y0, float y1)
        {
            const float t = 0.07f;
            float hw = w / 2f;
            Box(glass, at + outward * 0.015f, along, outward, hw, 0.015f, y0, y1);
            // Jambs, head and a bottom rail, 8 cm proud; a mullion splits wide windows into a pair of sashes.
            Box(frames, at + along * (hw - t / 2f) + outward * 0.04f, along, outward, t / 2f, 0.04f, y0, y1);
            Box(frames, at - along * (hw - t / 2f) + outward * 0.04f, along, outward, t / 2f, 0.04f, y0, y1);
            Box(frames, at + outward * 0.04f, along, outward, hw, 0.04f, y1 - t, y1);
            Box(frames, at + outward * 0.04f, along, outward, hw, 0.04f, y0, y0 + t);
            if (w > 1.3f) Box(frames, at + outward * 0.04f, along, outward, t / 2f, 0.04f, y0, y1);
            if (y1 - y0 > 1.6f) Box(frames, at + outward * 0.04f, along, outward, hw, 0.04f, y1 - (y1 - y0) * 0.3f - t / 2f, y1 - (y1 - y0) * 0.3f + t / 2f);
            Box(trim, at + outward * 0.09f, along, outward, hw + 0.1f, 0.09f, y0 - 0.08f, y0);
            if (look.Lintels) Box(trim, at + outward * 0.02f, along, outward, hw + 0.12f, 0.02f, y1, y1 + 0.22f);
        }

        /// <summary>A shop storey: wide glazed bays over a low stall riser, and a fascia band above for signs.</summary>
        private static void Shopfront(MeshBuilder frames, MeshBuilder trim, MeshBuilder glass, Vector2 from, Vector2 along, Vector2 outward, float length, int bays, float y)
        {
            float bay = length / bays;
            for (int b = 0; b < bays; b++)
            {
                Vector2 at = from + along * ((b + 0.5f) * bay);
                float hw = bay * 0.42f;
                Box(glass, at + outward * 0.015f, along, outward, hw, 0.015f, y + 0.55f, y + 3.1f);
                Box(frames, at + outward * 0.06f, along, outward, hw + 0.06f, 0.06f, y + 0.05f, y + 0.55f);
                Box(frames, at + outward * 0.05f, along, outward, hw, 0.05f, y + 3.1f, y + 3.22f);
                Box(frames, at + along * hw + outward * 0.05f, along, outward, 0.05f, 0.05f, y + 0.55f, y + 3.1f);
                Box(frames, at - along * hw + outward * 0.05f, along, outward, 0.05f, 0.05f, y + 0.55f, y + 3.1f);
            }
            Vector2 mid = from + along * (length / 2f);
            Band(trim, mid, along, outward, length / 2f + 0.06f, 0.12f, y + 3.3f, y + 4.05f);
        }

        /// <summary>A course running the whole face (string course, cornice, plinth).</summary>
        private static void Band(MeshBuilder mb, Vector2 mid, Vector2 along, Vector2 outward, float halfLength, float proud, float y0, float y1) =>
            Box(mb, mid + outward * (proud / 2f), along, outward, halfLength, proud / 2f, y0, y1);

        /// <summary>An axis-aligned box centred on (x, z) <paramref name="centre"/>, sized along the face and out of it.</summary>
        private static void Box(MeshBuilder mb, Vector2 centre, Vector2 along, Vector2 outward, float halfAlong, float halfOut, float y0, float y1)
        {
            Vector2 e = new Vector2(Mathf.Abs(along.x), Mathf.Abs(along.y)) * halfAlong + new Vector2(Mathf.Abs(outward.x), Mathf.Abs(outward.y)) * halfOut;
            mb.Cuboid(new Vector3(centre.x - e.x, y0, centre.y - e.y), new Vector3(centre.x + e.x, y1, centre.y + e.y));
        }

        /// <summary>
        /// Materials and proportions per style, with a colour picked per building: red and tan brick with stone
        /// trim, pastel render with white trim, clapboard or brick townhouses, board-marked concrete offices with
        /// ribbon windows, and near-full-height glazing for the glass towers.
        /// </summary>
        private static Look LookFor(Palette p, FacadeStyle style, System.Random rng)
        {
            T Pick<T>(params T[] options) => options[rng.Next(options.Length)];
            var look = new Look
            {
                Plinth = p.Surface(Finish.Concrete, new Color(0.36f, 0.35f, 0.33f), 0.05f),
                Roof = p.Surface(Finish.Concrete, new Color(0.3f, 0.3f, 0.31f), 0.05f),
                WindowWidth = 0.46f, WindowHeight = 0.5f, SillHeight = 0.3f,
            };
            Material stoneTrim = p.Surface(Finish.Concrete, new Color(0.82f, 0.78f, 0.7f), 0.1f);
            Material whiteTrim = p.Surface(Finish.Plaster, new Color(0.9f, 0.89f, 0.86f), 0.1f);
            switch (style)
            {
                case FacadeStyle.Brick:
                    look.Wall = p.Surface(Finish.Brick, Pick(new Color(0.55f, 0.3f, 0.24f), new Color(0.6f, 0.36f, 0.28f), new Color(0.48f, 0.27f, 0.22f),
                        new Color(0.66f, 0.5f, 0.4f), new Color(0.42f, 0.27f, 0.23f)), 0.05f);
                    look.Trim = stoneTrim;
                    look.Frame = p.Lit(Pick(new Color(0.9f, 0.89f, 0.85f), new Color(0.14f, 0.24f, 0.18f), new Color(0.1f, 0.1f, 0.11f), new Color(0.84f, 0.8f, 0.7f)), 0.3f);
                    look.Courses = true;
                    look.Lintels = true;
                    break;
                case FacadeStyle.Stucco:
                    look.Wall = p.Surface(Finish.PaintedPlaster, Pick(new Color(0.86f, 0.8f, 0.66f), new Color(0.76f, 0.82f, 0.78f), new Color(0.86f, 0.72f, 0.58f),
                        new Color(0.8f, 0.78f, 0.74f), new Color(0.7f, 0.76f, 0.82f)), 0.05f);
                    look.Trim = whiteTrim;
                    look.Frame = p.Lit(Pick(new Color(0.92f, 0.91f, 0.88f), new Color(0.4f, 0.28f, 0.18f)), 0.3f);
                    look.Courses = true;
                    look.WindowWidth = 0.5f;
                    break;
                case FacadeStyle.Townhouse:
                    look.Wall = rng.Next(2) == 0
                        ? p.Surface(Finish.Siding, Pick(new Color(0.7f, 0.76f, 0.8f), new Color(0.82f, 0.78f, 0.62f), new Color(0.62f, 0.7f, 0.6f), new Color(0.85f, 0.84f, 0.8f)), 0.08f)
                        : p.Surface(Finish.Brick, Pick(new Color(0.55f, 0.3f, 0.24f), new Color(0.48f, 0.27f, 0.22f)), 0.05f);
                    look.Trim = whiteTrim;
                    look.Frame = p.Lit(new Color(0.92f, 0.91f, 0.88f), 0.3f);
                    look.Lintels = true;
                    look.WindowWidth = 0.42f;
                    look.WindowHeight = 0.52f;
                    break;
                case FacadeStyle.Glass:
                    look.Wall = p.Surface(Finish.Concrete, new Color(0.34f, 0.36f, 0.39f), 0.2f);
                    look.Trim = p.Surface(Finish.Concrete, new Color(0.28f, 0.29f, 0.31f), 0.2f);
                    look.Frame = p.Lit(new Color(0.22f, 0.24f, 0.27f), 0.6f);
                    look.WindowWidth = 0.94f;
                    look.WindowHeight = 0.8f;
                    look.SillHeight = 0.1f;
                    break;
                default: // Concrete offices: ribbon windows between plain spandrels
                    look.Wall = p.Surface(Finish.Concrete, Pick(new Color(0.62f, 0.61f, 0.58f), new Color(0.52f, 0.52f, 0.52f), new Color(0.7f, 0.67f, 0.6f)), 0.08f);
                    look.Trim = p.Surface(Finish.Concrete, new Color(0.46f, 0.46f, 0.45f), 0.08f);
                    look.Frame = p.Lit(new Color(0.2f, 0.21f, 0.23f), 0.5f);
                    look.WindowWidth = 0.84f;
                    look.WindowHeight = 0.52f;
                    look.SillHeight = 0.28f;
                    break;
            }
            return look;
        }
    }
}
