using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Out past the edge of town (TOWN_SPEC A9): the trailer park and a couple of cabins off Old Mill Rd, the
    /// abandoned shed at its end, the campsite by the river, the overlook under the radio mast, the closed road
    /// at the east end of Bay Blvd, and in town, the construction site where the old dealership lot is becoming
    /// condos.
    /// </summary>
    public static class Outskirts
    {
        public static readonly Rect TrailerPark = Rect.MinMaxRect(-680f, -232f, -612f, -186f);
        public static readonly Vector2 Shed = new Vector2(-705f, -88f);
        public static readonly Vector2 Campsite = new Vector2(-300f, 432f);
        public static readonly Vector2 Overlook = new Vector2(-398f, 402f);
        public static readonly Rect ConstructionSite = Rect.MinMaxRect(63.5f, 13f, 103f, 47.5f);
        private static readonly Vector2[] Cabins = { new Vector2(-560f, -262f), new Vector2(-705f, -128f) };

        private static float Ground(Vector2 p) => TownTerrain.Natural(p.x, p.y);

        public static void AddPads(CityContext c)
        {
            c.Pads.Add(Pad.FromRect(TrailerPark, Ground(TrailerPark.center)));
            c.Pads.Add(new Pad(Shed, new Vector2(5f, 5f), 0f, Ground(Shed)));
            c.Pads.Add(new Pad(Campsite, new Vector2(12f, 10f), 0f, Ground(Campsite)));
            c.Pads.Add(new Pad(Overlook, new Vector2(7f, 7f), 0f, Ground(Overlook)));
            c.Pads.Add(Pad.FromRect(ConstructionSite, 0f));
            foreach (Vector2 cabin in Cabins) c.Pads.Add(new Pad(cabin, new Vector2(8f, 8f), 0f, Ground(cabin)));
        }

        public static void Build(CityContext c)
        {
            Transform root = Kit.Group(c.Static, "Outskirts");
            var rng = new System.Random(777);
            Trailers(c, root, rng);
            foreach (Vector2 cabin in Cabins) Cabin(c, root, cabin, rng);
            AbandonedShed(c, root);
            Camp(c, root);
            OverlookPoint(c, root);
            RoadClosed(c, root);
            Construction(c, root, rng);
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        private static void Trailers(CityContext c, Transform root, System.Random rng)
        {
            Kit k = c.Kit;
            Rect r = TrailerPark;
            float y = Ground(r.center);
            Transform park = Kit.Group(root, "Pinecrest Trailer Park", V(0f, y, 0f));
            k.Span(park, "Gravel", V(r.xMin, -0.04f, r.yMin), V(r.xMax, 0.01f, r.yMax), c.P.Lit(new Color(0.45f, 0.43f, 0.4f), 0.05f)).AddComponent<SurfaceTag>().Roughness = 0.6f;
            Color[] sidings = { new Color(0.85f, 0.83f, 0.75f), new Color(0.6f, 0.7f, 0.72f), new Color(0.75f, 0.68f, 0.5f), new Color(0.55f, 0.6f, 0.5f) };
            for (int i = 0; i < 8; i++)
            {
                float x = r.xMin + 6f + (i % 4) * 16f, z = i < 4 ? r.yMin + 6f : r.yMax - 12f;
                Transform t = Kit.Group(park, "Trailer", V(x, 0f, z), i < 4 ? 0f : 180f);
                k.Box(t, "Body", V(0f, 1.8f, 3f), V(4.2f, 2.8f, 12f), c.P.Lit(sidings[rng.Next(sidings.Length)], 0.2f));
                k.Box(t, "Skirt", V(0f, 0.25f, 3f), V(4.1f, 0.5f, 11.8f), c.P.Lit(new Color(0.35f, 0.33f, 0.3f)), collider: false);
                k.Box(t, "Roof", V(0f, 3.25f, 3f), V(4.3f, 0.1f, 12.1f), c.P.Lit(new Color(0.7f, 0.72f, 0.72f), 0.4f), collider: false);
                k.Pane(t, "Window", V(2.12f, 1.8f, 0.5f), V(2.13f, 2.6f, 2.5f), new Color(0.4f, 0.5f, 0.55f, 0.6f), collider: false);
                k.Box(t, "Steps", V(2.6f, 0.4f, 5f), V(1f, 0.8f, 1.2f), c.P.Lit(new Color(0.5f, 0.42f, 0.32f)));
                c.Place(t.TransformPoint(V(3.4f, 0f, 5f)), PlaceKind.Door, "trailer");
                if (rng.NextDouble() < 0.5)
                {
                    k.Fit(t, "chair", V(3.8f, 0f, 7f), V(0.5f, 0f, 0f), 90f);
                    k.Box(t, "Grill", V(3.8f, 0.5f, 8.5f), V(0.6f, 1f, 0.6f), c.P.Lit(new Color(0.1f, 0.1f, 0.1f), 0.4f));
                }
            }
            k.Box(park, "Sign", V(r.xMax - 2f, 1.8f, r.center.y), V(0.2f, 1.2f, 3.2f), c.P.Lit(new Color(0.35f, 0.25f, 0.18f)));
            k.Text(park, "PINECREST", V(r.xMax - 1.88f, 1.9f, r.center.y), 270f, 0.25f, new Color(0.9f, 0.85f, 0.7f));
        }

        private static void Cabin(CityContext c, Transform root, Vector2 at, System.Random rng)
        {
            Kit k = c.Kit;
            Transform t = Kit.Group(root, "Cabin", V(at.x, Ground(at), at.y), rng.Next(4) * 90f);
            Material logs = c.P.Lit(new Color(0.42f, 0.3f, 0.2f), 0.05f);
            k.Box(t, "Cabin", V(0f, 1.6f, 0f), V(7f, 3.2f, 6f), logs);
            var roof = new MeshBuilder();
            Vector3 ridgeA = V(-4f, 5f, 0f), ridgeB = V(4f, 5f, 0f);
            roof.Quad(V(-4f, 3.1f, -3.5f), V(4f, 3.1f, -3.5f), ridgeB, ridgeA);
            roof.Quad(V(4f, 3.1f, 3.5f), V(-4f, 3.1f, 3.5f), ridgeA, ridgeB);
            roof.Build(t, "Roof", c.P.Lit(new Color(0.25f, 0.28f, 0.25f)), collider: false);
            k.Box(t, "Chimney", V(2.5f, 4.5f, 1f), V(0.8f, 3f, 0.8f), c.P.Lit(new Color(0.45f, 0.42f, 0.4f)));
            k.Box(t, "Woodpile", V(-4.3f, 0.5f, 1f), V(1f, 1f, 3f), logs);
            c.Place(t.TransformPoint(V(0f, 0f, -4f)), PlaceKind.Door, "cabin");
        }

        private static void AbandonedShed(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Transform t = Kit.Group(root, "Abandoned shed", V(Shed.x, Ground(Shed), Shed.y), 20f);
            Material planks = c.P.Lit(new Color(0.38f, 0.33f, 0.28f), 0.03f);
            k.Span(t, "Back", V(-3f, 0f, 2.4f), V(3f, 3f, 2.6f), planks);
            k.Span(t, "Left", V(-3f, 0f, -2.5f), V(-2.8f, 3f, 2.5f), planks);
            k.Span(t, "Right", V(2.8f, 0f, -2.5f), V(3f, 2.4f, 2.5f), planks);
            k.Span(t, "Front", V(-3f, 0f, -2.6f), V(-1f, 3f, -2.4f), planks);
            GameObject door = k.Box(t, "Hanging door", V(0.2f, 1f, -3.1f), V(1.4f, 2f, 0.06f), planks);
            door.transform.localRotation = Quaternion.Euler(0f, 55f, 8f);
            GameObject roof = k.Box(t, "Sagging roof", V(0f, 3.1f, 0f), V(6.4f, 0.12f, 5.6f), c.P.Lit(new Color(0.3f, 0.3f, 0.3f), 0.1f), collider: false);
            roof.transform.localRotation = Quaternion.Euler(6f, 0f, 4f);
            k.Box(t, "Rusty drum", V(-2f, 0.45f, 1.5f), V(0.6f, 0.9f, 0.6f), c.P.Lit(new Color(0.45f, 0.25f, 0.14f)));
            k.Box(t, "Old workbench", V(1.5f, 0.45f, 1.8f), V(1.8f, 0.9f, 0.7f), planks);
            c.Anchor("abandoned_shed", t.position);
        }

        private static void Camp(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Transform t = Kit.Group(root, "Kell River Campground", V(Campsite.x, Ground(Campsite), Campsite.y));
            Color[] tents = { new Color(0.85f, 0.45f, 0.1f), new Color(0.2f, 0.45f, 0.3f), new Color(0.2f, 0.35f, 0.6f) };
            for (int i = 0; i < 3; i++)
            {
                GameObject tent = k.Box(t, "Tent", V(-6f + i * 6f, 0.7f, 4f), V(2.4f, 1.4f, 2.8f), c.P.Lit(tents[i], 0.2f));
                tent.transform.localRotation = Quaternion.Euler(0f, i * 25f, 45f);
                tent.transform.localScale = new Vector3(1.4f, 1.4f, 2.8f);
            }
            k.Cylinder(t, "Fire ring", V(0f, 0.15f, -1f), 1.6f, 0.3f, c.P.Lit(new Color(0.35f, 0.33f, 0.31f)), collider: true);
            k.Cylinder(t, "Embers", V(0f, 0.28f, -1f), 0.9f, 0.04f, c.P.Lamp(new Color(0.2f, 0.12f, 0.08f), new Color(1f, 0.45f, 0.15f), 2f));
            Light fire = c.PointLight(t, V(0f, 0.8f, -1f), 8f, 1.4f, new Color(1f, 0.55f, 0.25f));
            fire.enabled = false;
            c.NightLights.Add(fire);
            k.Box(t, "Picnic table", V(4f, 0.4f, -3f), V(2f, 0.8f, 0.9f), c.P.Lit(new Color(0.5f, 0.38f, 0.26f)));
            foreach (float dz in new[] { -0.9f, 0.9f }) k.Box(t, "Bench", V(4f, 0.25f, -3f + dz), V(2f, 0.5f, 0.35f), c.P.Lit(new Color(0.5f, 0.38f, 0.26f)));
            c.Place(t.TransformPoint(V(4f, 0f, -4.5f)), PlaceKind.Bench, "campsite");
            c.Anchor("campsite", t.position);
        }

        private static void OverlookPoint(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            // Just below the mast, facing the town and the bay.
            float y = Ground(Overlook);
            Transform t = Kit.Group(root, "Overlook", V(Overlook.x, y, Overlook.y), 150f);
            Material wood = c.P.Lit(new Color(0.5f, 0.38f, 0.26f), 0.05f);
            k.Span(t, "Deck", V(-5f, -0.2f, -3f), V(5f, 0.1f, 4f), wood);
            k.Span(t, "Rail", V(-5f, 0.1f, 3.9f), V(5f, 1.1f, 4f), wood);
            Transform bench = Kit.Group(t, "Bench", V(0f, 0.1f, 2.4f), 0f);
            k.Box(bench, "Seat", V(0f, 0.45f, 0f), V(1.8f, 0.07f, 0.5f), wood);
            k.Box(bench, "Board", V(-3.5f, 1f, 2f), V(1.2f, 0.8f, 0.06f), c.P.Lit(new Color(0.2f, 0.35f, 0.25f)), collider: false);
            k.Text(bench, "KELL LOOKOUT · ELEV 42 M", V(-3.5f, 1f, 1.96f), 0f, 0.07f, Color.white);
            c.Place(t.TransformPoint(V(0f, 0f, 2.4f)), PlaceKind.Bench, "overlook");
            c.Anchor("overlook", t.TransformPoint(V(0f, 0f, 2.4f)));
        }

        private static void RoadClosed(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            // Bay Blvd runs out at a washed-out culvert: barriers, cones, a sign, the woods beyond.
            var end = new Vector2(625f, -163.8f);
            Transform t = Kit.Group(root, "Road closed", V(end.x, TownTerrain.Natural(end.x, end.y) - 0.15f, end.y), 77f);
            Material stripes = c.P.Lit(new Color(0.9f, 0.5f, 0.1f), 0.3f);
            k.Box(t, "Barrier", V(0f, 0.6f, 0f), V(10f, 1.2f, 0.4f), stripes);
            k.Box(t, "Sign", V(0f, 1.9f, 0f), V(3.6f, 1f, 0.08f), c.P.Lit(new Color(0.95f, 0.85f, 0.2f)), collider: false);
            k.Text(t, "ROAD CLOSED · CULVERT WASHED OUT", V(0f, 1.9f, -0.06f), 0f, 0.13f, new Color(0.1f, 0.1f, 0.1f));
            for (int i = 0; i < 5; i++) k.Prop(t, "construction-cone", V(-4f + i * 2f, 0f, -2f), 0.7f);
        }

        /// <summary>The old lot behind the Maple shops: hoardings, a crane, an excavator, a pit (becoming condos).</summary>
        private static void Construction(CityContext c, Transform root, System.Random rng)
        {
            Kit k = c.Kit;
            Rect r = ConstructionSite;
            Transform t = Kit.Group(root, "Maple Commons site");
            k.Span(t, "Dirt", V(r.xMin, -0.04f, r.yMin), V(r.xMax, 0.012f, r.yMax), c.P.Lit(new Color(0.42f, 0.33f, 0.24f), 0.02f)).AddComponent<SurfaceTag>().Roughness = 0.8f;
            Material board = c.P.Lit(new Color(0.2f, 0.32f, 0.28f), 0.1f);
            k.Span(t, "Hoarding west", V(r.xMin, 0f, r.yMin), V(r.xMin + 0.1f, 2.4f, r.yMax), board);
            k.Span(t, "Hoarding north", V(r.xMin, 0f, r.yMax - 0.1f), V(r.xMax, 2.4f, r.yMax), board);
            k.Text(t, "COMING SOON · MAPLE COMMONS · 48 LUXURY CONDOS", V(r.xMin - 0.05f, 1.5f, r.center.y), 90f, 0.25f, Color.white);
            Material yellow = c.P.Lit(new Color(0.95f, 0.72f, 0.1f), 0.3f);
            // Tower crane.
            Vector3 cr = V(r.xMin + 10f, 0f, r.yMin + 10f);
            k.Box(t, "Crane mast", cr + V(0f, 15f, 0f), V(1.6f, 30f, 1.6f), yellow);
            k.Box(t, "Crane jib", cr + V(8f, 30f, 0f), V(30f, 1.2f, 1.2f), yellow);
            k.Box(t, "Crane counterweight", cr + V(-8f, 29.5f, 0f), V(4f, 2f, 2.4f), c.P.Lit(new Color(0.5f, 0.5f, 0.5f)));
            k.Box(t, "Crane cab", cr + V(1.5f, 28.5f, 0f), V(2f, 2f, 2f), yellow);
            // Excavator and a pile.
            Transform ex = Kit.Group(t, "Excavator", V(r.xMax - 12f, 0f, r.center.y), 30f);
            k.Box(ex, "Tracks", V(0f, 0.5f, 0f), V(3f, 1f, 4.5f), c.P.Lit(new Color(0.15f, 0.15f, 0.15f)));
            k.Box(ex, "Body", V(0f, 1.7f, 0f), V(2.8f, 1.4f, 3f), yellow);
            GameObject boom = k.Box(ex, "Boom", V(0f, 3f, 3f), V(0.5f, 0.5f, 5f), yellow);
            boom.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            GameObject pile = k.Sphere(t, "Spoil heap", V(r.xMax - 6f, 0f, r.yMax - 8f), 7f, c.P.Lit(new Color(0.4f, 0.31f, 0.22f)));
            pile.transform.localScale = new Vector3(7f, 3f, 6f);
            for (int i = 0; i < 6; i++) k.Prop(t, "construction-cone", V(r.xMin + 3f + i * 3f, 0f, r.yMin + 2f), 0.7f);
            k.Prop(t, "construction-light", V(r.xMin + 2f, 0f, r.yMax - 2f), 2.5f);
        }
    }
}
