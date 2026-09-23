using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>Traffic signal lenses for one approach lane; the traffic view switches their materials.</summary>
    public sealed class SignalHead
    {
        public RoadNetwork.Lane Lane;
        public Renderer Red, Yellow, Green;
    }

    /// <summary>Walk / don't-walk lamps at one end of a signalised crosswalk.</summary>
    public sealed class WalkSignal
    {
        public SidewalkGraph.Crosswalk Crosswalk;
        public Renderer Walk, Wait;
    }

    /// <summary>Ground, roads, sidewalks, markings, street furniture and junction hardware (spec §9, §10).</summary>
    public static class StreetBuilder
    {
        private static readonly Color Asphalt = new Color(0.17f, 0.17f, 0.18f);
        private static readonly Color Concrete = new Color(0.64f, 0.63f, 0.6f);
        private static readonly Color PoleGray = new Color(0.26f, 0.27f, 0.28f);

        public static void Build(CityContext c, SidewalkGraph walks, List<SignalHead> signals, List<WalkSignal> walkSignals)
        {
            Kit k = c.Kit;
            Rect w = CityPlan.World;
            Transform root = Kit.Group(c.Static, "Streets");
            Tag(k.Span(root, "Roads", new Vector3(w.xMin, CityPlan.RoadY - 1f, w.yMin), new Vector3(w.xMax, CityPlan.RoadY, w.yMax), c.P.Lit(Asphalt, 0.05f)), 0.3f);
            // Land beyond the playable area, so fog shows ground instead of void.
            k.Span(root, "Outskirts", new Vector3(-700f, -1.3f, -600f), new Vector3(850f, CityPlan.RoadY - 0.02f, 650f),
                c.P.Lit(new Color(0.28f, 0.31f, 0.24f)), collider: false);

            Material walk = c.P.Lit(Concrete, 0.08f);
            float sw = CityPlan.SidewalkWidth;
            foreach (var (name, a, ground, lawn) in CityPlan.Blocks)
            {
                Transform b = Kit.Group(root, name);
                const float bottom = -0.9f;
                Tag(k.Span(b, "Sidewalk S", new Vector3(a.xMin, bottom, a.yMin), new Vector3(a.xMax, 0f, a.yMin + sw), walk), 0.2f);
                Tag(k.Span(b, "Sidewalk N", new Vector3(a.xMin, bottom, a.yMax - sw), new Vector3(a.xMax, 0f, a.yMax), walk), 0.2f);
                Tag(k.Span(b, "Sidewalk W", new Vector3(a.xMin, bottom, a.yMin + sw), new Vector3(a.xMin + sw, 0f, a.yMax - sw), walk), 0.2f);
                Tag(k.Span(b, "Sidewalk E", new Vector3(a.xMax - sw, bottom, a.yMin + sw), new Vector3(a.xMax, 0f, a.yMax - sw), walk), 0.2f);
                Tag(k.Span(b, "Ground", new Vector3(a.xMin + sw, bottom, a.yMin + sw), new Vector3(a.xMax - sw, -0.01f, a.yMax - sw), c.P.Lit(ground, 0.04f)),
                    lawn ? 1f : 0.25f);
            }

            Markings(c, root, walks);
            CurbRamps(c, root, walks);
            Furniture(c, root);
            Park(c, root);
            Junctions(c, walks, signals, walkSignals);
            Boundary(c, root);
        }

        private static float Yaw(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        private static void Tag(GameObject ground, float roughness) => ground.AddComponent<SurfaceTag>().Roughness = roughness;

        /// <summary>
        /// Ramps from the road up to the kerb at both ends of every crosswalk (spec §9): wheelchairs, bikes and
        /// skateboards (which can't climb a 15 cm kerb) use them. They sit in the kerb-side metre of the road,
        /// clear of the lanes.
        /// </summary>
        private static void CurbRamps(CityContext c, Transform root, SidewalkGraph walks)
        {
            Transform ramps = Kit.Group(root, "Curb ramps");
            Material concrete = c.P.Lit(new Color(0.6f, 0.59f, 0.56f), 0.08f);
            const float run = 1.2f, rise = -CityPlan.RoadY, thickness = 0.06f;
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            foreach (SidewalkGraph.Crosswalk cw in walks.Crosswalks)
            {
                Vector2 across = RoadNetwork.RightOf(cw.Along);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 toKerb = across * side;
                    Vector2 mid = cw.Center + toKerb * (CityPlan.RoadHalfWidth - run / 2f);
                    GameObject ramp = c.Kit.Box(ramps, "Ramp", new Vector3(mid.x, CityPlan.RoadY + rise / 2f - thickness / 2f, mid.y),
                        new Vector3(CityPlan.SidewalkWidth - 1f, thickness, Mathf.Sqrt(run * run + rise * rise)), concrete);
                    ramp.transform.localRotation = Quaternion.Euler(-angle, Yaw(toKerb), 0f);
                    Tag(ramp, 0.2f);
                }
            }
        }

        private static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        private static void Markings(CityContext c, Transform root, SidewalkGraph walks)
        {
            Kit k = c.Kit;
            Transform m = Kit.Group(root, "Markings");
            Material yellow = c.P.Lit(new Color(0.85f, 0.68f, 0.18f), 0.1f);
            Material white = c.P.Lit(new Color(0.88f, 0.88f, 0.86f), 0.1f);
            float y = CityPlan.RoadY + 0.006f;
            foreach (var (ai, bi, _) in CityPlan.Streets)
            {
                Vector2 a = CityPlan.Nodes[ai].P, b = CityPlan.Nodes[bi].P;
                Vector2 d = (b - a).normalized, right = RoadNetwork.RightOf(d);
                float length = Vector2.Distance(a, b) - 2f * CityPlan.StopLine;
                Vector2 mid = (a + b) / 2f;
                foreach (float side in new[] { -0.12f, 0.12f })
                    k.Decal(m, "Centre line", V(mid + right * side, y), new Vector2(0.1f, length), Yaw(d), yellow);
            }
            foreach (RoadNetwork.Lane lane in c.Roads.Lanes)
            {
                RoadNetwork.Node n = lane.To;
                bool stops = n.Lights != null || (n.Control == NodeControl.StopOnStem && lane.IsStem);
                if (!stops) continue;
                Vector2 p = n.P - lane.Dir * (CityPlan.StopLine - 0.3f) + RoadNetwork.RightOf(lane.Dir) * (CityPlan.RoadHalfWidth / 2f);
                k.Decal(m, "Stop line", V(p, y), new Vector2(CityPlan.RoadHalfWidth - 0.3f, 0.4f), Yaw(lane.Dir), white);
            }
            foreach (SidewalkGraph.Crosswalk cw in walks.Crosswalks)
            {
                Vector2 across = RoadNetwork.RightOf(cw.Along);
                for (float o = -4.25f; o <= 4.26f; o += 1f)
                    k.Decal(m, "Crosswalk", V(cw.Center + across * o, y), new Vector2(0.5f, 2.9f), Yaw(cw.Along), white);
            }
        }

        private static void Furniture(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Transform f = Kit.Group(root, "Furniture");
            Material pole = c.P.Lit(PoleGray, 0.35f);
            Material lamp = c.P.Lamp(new Color(0.55f, 0.55f, 0.5f), new Color(1f, 0.86f, 0.6f));
            Material bark = c.P.Lit(new Color(0.3f, 0.22f, 0.15f));
            Material leaves = c.P.Lit(new Color(0.22f, 0.38f, 0.18f));
            Material leavesDark = c.P.Lit(new Color(0.18f, 0.32f, 0.16f));

            foreach (var (ai, bi, _) in CityPlan.Streets)
            {
                Vector2 a = CityPlan.Nodes[ai].P, b = CityPlan.Nodes[bi].P;
                Vector2 d = (b - a).normalized, right = RoadNetwork.RightOf(d);
                float length = Vector2.Distance(a, b);
                bool leafy = a.x < 130f || b.x < 130f; // downtown has fewer trees
                int index = 0;
                for (float s = 15f; s < length - 14f; s += 12f, index++)
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 kerb = a + d * s + right * side * (CityPlan.RoadHalfWidth + 0.65f);
                    bool lightHere = (index + (side > 0 ? 0 : 1)) % 4 == 0;
                    if (lightHere) StreetLight(c, f, kerb, -right * side, pole, lamp);
                    else if (leafy && index % 2 == 1) Tree(k, f, kerb, bark, (index + (int)side) % 3 == 0 ? leavesDark : leaves);
                }
            }

            // Benches (pedestrians sit on them), bins and bike racks.
            Material wood = c.P.Lit(new Color(0.45f, 0.32f, 0.2f));
            (Vector3 P, float Yaw)[] benches =
            {
                // Yaw 0 = back to the north, facing south.
                // Clear of the shop doors.
                (new Vector3(91.8f, 0f, -6.1f), 0f), (new Vector3(96.5f, 0f, -6.1f), 0f), (new Vector3(188f, 0f, -6.1f), 0f),
                (new Vector3(30f, 0f, -6.1f), 0f), (new Vector3(-34.5f, 0f, 24f), 90f), (new Vector3(-34.5f, 0f, 38f), 90f),
                (new Vector3(152f, 0f, -20.9f), 180f), (new Vector3(60f, 0f, -20.9f), 180f),
            };
            foreach (var (p, yaw) in benches)
            {
                Transform bench = Kit.Group(f, "Bench", p, yaw);
                k.Box(bench, "Seat", new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.07f, 0.5f), wood);
                k.Box(bench, "Back", new Vector3(0f, 0.75f, 0.22f), new Vector3(1.8f, 0.4f, 0.06f), wood, collider: false);
                k.Box(bench, "LegL", new Vector3(-0.8f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.45f), pole, collider: false);
                k.Box(bench, "LegR", new Vector3(0.8f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.45f), pole, collider: false);
                Vector3 binAt = p + bench.right * 1.4f;
                GameObject bin = k.Model(f, "trashcan", binAt, yaw, 0.22f);
                if (bin != null)
                {
                    var solid = bin.AddComponent<CapsuleCollider>(); // in model units: ×0.22 is a 0.24 m radius, 0.95 m tall
                    solid.radius = 1.1f;
                    solid.height = 4.3f;
                    solid.center = new Vector3(0f, 2.15f, 0f);
                }
                else k.Cylinder(f, "Bin", binAt + new Vector3(0f, 0.45f, 0f), 0.5f, 0.9f, c.P.Lit(new Color(0.2f, 0.28f, 0.22f)), collider: true);
                // Sit spot: on the seat, facing out of the bench.
                c.Place(p, PlaceKind.Bench, "bench");
            }
            foreach (Vector3 p in new[] { new Vector3(80.5f, 0f, -6.2f), new Vector3(114f, 0f, -6.2f) })
            {
                Transform rack = Kit.Group(f, "Bike rack", p);
                for (int i = 0; i < 4; i++)
                    k.Box(rack, "Hoop", new Vector3(-0.9f + i * 0.6f, 0.45f, 0f), new Vector3(0.05f, 0.9f, 0.7f), pole, collider: false);
            }
            // Parking meters downtown.
            for (float x = 174f; x < 206f; x += 6f)
            {
                k.Cylinder(f, "Meter", new Vector3(x, 0.6f, -8.4f), 0.07f, 1.2f, pole);
                k.Box(f, "Meter head", new Vector3(x, 1.3f, -8.4f), new Vector3(0.2f, 0.3f, 0.14f), c.P.Lit(new Color(0.35f, 0.38f, 0.4f), 0.5f), collider: false);
            }
            // Dumpsters behind the Maple shops (clear of the back doors and the fuel station forecourt).
            foreach (Vector3 p in new[] { new Vector3(68f, 0f, 10.8f), new Vector3(88.5f, 0f, 11f), new Vector3(121f, 0f, 9.8f) })
            {
                GameObject dumpster = k.Model(f, "dumpster", p, 90f, KitBuildings.Scale);
                if (dumpster == null) continue;
                var solid = dumpster.AddComponent<BoxCollider>(); // model units (×7.4 ≈ 2.1 × 1.6 × 2.7 m)
                solid.center = new Vector3(0f, 0.105f, 0f);
                solid.size = new Vector3(0.28f, 0.21f, 0.37f);
            }
            foreach (Vector3 p in new[] { new Vector3(49f, 0f, -8.3f), new Vector3(141f, 0f, -8.3f), new Vector3(-39f, 0f, 64f) })
                k.Cylinder(f, "Hydrant", p + new Vector3(0f, 0.35f, 0f), 0.24f, 0.7f, c.P.Lit(new Color(0.7f, 0.15f, 0.1f), 0.4f), collider: true);
        }

        /// <summary>
        /// The residential block's lawn: park trees (trunks are solid), bushes, flowers and grass tufts
        /// (walk-through), kept clear of buildings and the bench paths. Nothing without the nature kit.
        /// </summary>
        private static void Park(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            if (k.Art == null || k.Art.Model(BroadTrees[0]) == null) return;
            Rect lawn = Rect.MinMaxRect(-36f, -5f, 46f, 61f);
            var keepClear = new List<Rect> { Grow(CityPlan.ApartmentBuilding, 3f), Rect.MinMaxRect(-40f, 21f, -30f, 27f), Rect.MinMaxRect(-40f, 35f, -30f, 41f) };
            foreach (Shell s in CityPlan.Shells)
                if (s.Footprint.Overlaps(lawn)) keepClear.Add(Grow(s.Footprint, 1.5f));
            Transform park = Kit.Group(root, "Park");
            var rng = new System.Random(4242);
            var trees = new List<Vector2>();

            bool Free(Vector2 p, float margin)
            {
                foreach (Rect r in keepClear)
                    if (Grow(r, margin).Contains(p)) return false;
                return true;
            }

            (string[] Names, int Count, float Scale, float Spacing, bool Solid)[] layers =
            {
                // Quaternius plants: the scale is the height in metres.
                (ParkTrees, 16, 9.5f, 9f, true),
                (new[] { "q_bush_leafy", "q_bush_flowers", "q_bush_small", "q_bush_small_flowers" }, 30, 1.1f, 0f, false),
                (new[] { "q_plant", "q_bush_small_flowers", "q_fern" }, 40, 0.55f, 0f, false),
                (new[] { "q_grass_tall" }, 90, 0.6f, 0f, false),
            };
            foreach (var (names, count, scale, spacing, solid) in layers)
            {
                for (int i = 0, tries = 0; i < count && tries < count * 20; tries++)
                {
                    var p = new Vector2(Mathf.Lerp(lawn.xMin, lawn.xMax, (float)rng.NextDouble()), Mathf.Lerp(lawn.yMin, lawn.yMax, (float)rng.NextDouble()));
                    if (!Free(p, solid ? 2f : 0.5f) || (spacing > 0f && trees.Exists(t => (t - p).sqrMagnitude < spacing * spacing))) continue;
                    GameObject go = k.Model(park, names[rng.Next(names.Length)], new Vector3(p.x, 0f, p.y), (float)rng.NextDouble() * 360f,
                        scale * (0.85f + 0.3f * (float)rng.NextDouble()));
                    Naturalize(k, go);
                    i++;
                    if (!solid || go == null) continue;
                    trees.Add(p);
                    Trunk(go);
                }
            }
        }

        /// <summary>Street and yard trees (Quaternius, 1 unit tall: scale by the height in metres).</summary>
        internal static readonly string[] BroadTrees = { "q_tree_a", "q_tree_b", "q_tree_common" };
        /// <summary>Broad trees and pines together, pines twice over: the town's parks and yards lean evergreen.
        /// (q_tree_c/d are squat and thick-trunked: open ground only, not a sidewalk.)</summary>
        internal static readonly string[] ParkTrees = { "q_tree_a", "q_tree_b", "q_tree_c", "q_tree_d", "q_tree_common", "q_pine_a", "q_pine_b", "q_pine_c", "q_pine_a", "q_pine_b", "q_pine_c" };

        /// <summary>
        /// A solid trunk on a tree placed at <c>scale = height</c>: the collider is in model units, so metres are
        /// divided by the scale (0.2 m radius, the lower 3 m).
        /// </summary>
        internal static void Trunk(GameObject tree)
        {
            float s = tree.transform.localScale.y;
            var trunk = tree.AddComponent<CapsuleCollider>();
            trunk.radius = 0.2f / s;
            trunk.height = 3f / s;
            trunk.center = new Vector3(0f, 1.5f / s, 0f);
        }

        /// <summary>The nature kit's own palette is teal and orange; the city wants ordinary greens and browns.</summary>
        internal static void Naturalize(Kit k, GameObject go)
        {
            if (go == null) return;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Color? natural = mats[i] == null ? null : mats[i].name switch
                    {
                        "leafsGreen" => new Color(0.3f, 0.5f, 0.2f),
                        "leafsDark" => new Color(0.21f, 0.39f, 0.17f),
                        "woodBark" => new Color(0.36f, 0.25f, 0.17f),
                        "woodBarkDark" => new Color(0.27f, 0.19f, 0.13f),
                        "woodInner" => new Color(0.62f, 0.48f, 0.32f),
                        "grass" => new Color(0.28f, 0.46f, 0.19f),
                        "dirt" => new Color(0.5f, 0.48f, 0.45f),
                        _ => (Color?)null,
                    };
                    if (natural.HasValue) mats[i] = k.P.Lit(natural.Value, 0.1f);
                }
                r.sharedMaterials = mats;
            }
        }

        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        private static void StreetLight(CityContext c, Transform parent, Vector2 p, Vector2 towardRoad, Material pole, Material lamp)
        {
            Kit k = c.Kit;
            Transform light = Kit.Group(parent, "Street light", new Vector3(p.x, 0f, p.y), Yaw(towardRoad));
            k.Cylinder(light, "Pole", new Vector3(0f, 3f, 0f), 0.14f, 6f, pole, collider: true);
            k.Box(light, "Arm", new Vector3(0f, 5.9f, 0.7f), new Vector3(0.08f, 0.08f, 1.5f), pole, collider: false);
            k.Box(light, "Lamp", new Vector3(0f, 5.8f, 1.35f), new Vector3(0.32f, 0.12f, 0.55f), lamp, collider: false);
            // A real pool of light on the pavement at night (Forward+ handles dozens of these).
            var pool = new GameObject("Light pool");
            pool.transform.SetParent(light, false);
            pool.transform.localPosition = new Vector3(0f, 5.7f, 1.35f);
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var spot = pool.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 15f;
            spot.spotAngle = 115f;
            spot.innerSpotAngle = 60f;
            spot.intensity = 30f; // URP falls off with distance²: the lamp is ~5.7 m up
            spot.color = new Color(1f, 0.84f, 0.6f);
            spot.shadows = LightShadows.None;
            spot.enabled = false;
            c.NightLights.Add(spot);
        }

        private static void Tree(Kit k, Transform parent, Vector2 p, Material bark, Material leaves)
        {
            Transform tree = Kit.Group(parent, "Tree", new Vector3(p.x, 0f, p.y));
            k.Box(tree, "Pit", new Vector3(0f, 0.005f, 0f), new Vector3(1.2f, 0.02f, 1.2f), k.P.Lit(new Color(0.26f, 0.2f, 0.15f)), collider: false);
            // 6–8 m street trees; the pick and turn are stable per spot.
            float hash = Mathf.Abs(Mathf.Sin(p.x * 12.9898f + p.y * 78.233f) * 43758.5453f) % 1f;
            GameObject model = k.Model(tree, BroadTrees[(int)(hash * BroadTrees.Length)], Vector3.zero, hash * 360f, 6f + 2f * hash);
            if (model != null)
            {
                var trunk = tree.gameObject.AddComponent<CapsuleCollider>();
                trunk.center = new Vector3(0f, 1.3f, 0f);
                trunk.radius = 0.14f;
                trunk.height = 2.6f;
                return;
            }
            k.Cylinder(tree, "Trunk", new Vector3(0f, 1.3f, 0f), 0.22f, 2.6f, bark, collider: true);
            k.Sphere(tree, "Canopy", new Vector3(0f, 3.6f, 0f), 3f, leaves);
            k.Sphere(tree, "Canopy top", new Vector3(0.3f, 4.6f, -0.2f), 2f, leaves);
        }

        private static void Junctions(CityContext c, SidewalkGraph walks, List<SignalHead> signals, List<WalkSignal> walkSignals)
        {
            Kit k = c.Kit;
            Transform j = Kit.Group(c.Dynamic, "Junctions");
            Transform names = Kit.Group(c.Static, "Street signs");
            Material pole = c.P.Lit(PoleGray, 0.35f);
            Material housing = c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.3f);
            Material off = c.P.Unlit(new Color(0.12f, 0.12f, 0.12f));
            Material blade = c.P.Lit(new Color(0.12f, 0.36f, 0.2f), 0.3f);
            Color signText = new Color(0.95f, 0.95f, 0.92f);

            foreach (RoadNetwork.Node n in c.Roads.Nodes)
            {
                // Street name sign on the corner.
                Vector3 corner = new Vector3(n.P.x + 6.2f, 0f, n.P.y + 6.2f);
                k.Cylinder(names, "Name pole", corner + new Vector3(0f, 1.6f, 0f), 0.08f, 3.2f, pole, collider: true);
                var streets = new List<(string Name, Vector2 Dir)>();
                foreach (RoadNetwork.Lane lane in n.Outgoing)
                    if (!streets.Exists(s => s.Name == lane.Street)) streets.Add((lane.Street, lane.Dir));
                for (int i = 0; i < streets.Count; i++)
                {
                    float yaw = Yaw(streets[i].Dir) - 90f; // blade runs along the street
                    Transform b = Kit.Group(names, "Blade", corner + new Vector3(0f, 3.05f + 0.24f * i, 0f), yaw);
                    k.Box(b, "Plate", Vector3.zero, new Vector3(1.5f, 0.2f, 0.03f), blade, collider: false);
                    k.Text(b, streets[i].Name, new Vector3(0f, 0f, -0.02f), 0f, 0.11f, signText);
                    k.Text(b, streets[i].Name, new Vector3(0f, 0f, 0.02f), 180f, 0.11f, signText);
                }

                foreach (RoadNetwork.Lane lane in n.Incoming)
                {
                    Vector2 d = lane.Dir, right = RoadNetwork.RightOf(d);
                    Vector2 at = n.P - d * (CityPlan.CrosswalkFar + 0.6f) + right * (CityPlan.RoadHalfWidth + 0.6f);
                    if (n.Lights != null)
                    {
                        Transform head = Kit.Group(j, "Signal", new Vector3(at.x, 0f, at.y), Yaw(d));
                        k.Cylinder(head, "Pole", new Vector3(0f, 1.75f, 0f), 0.12f, 3.5f, pole, collider: true);
                        k.Box(head, "Housing", new Vector3(0f, 3.1f, 0f), new Vector3(0.36f, 1.02f, 0.3f), housing, collider: false);
                        signals.Add(new SignalHead
                        {
                            Lane = lane,
                            Red = Lens(k, head, 3.42f, off),
                            Yellow = Lens(k, head, 3.1f, off),
                            Green = Lens(k, head, 2.78f, off),
                        });
                    }
                    else if (n.Control == NodeControl.StopOnStem && lane.IsStem)
                    {
                        Transform sign = Kit.Group(names, "Stop sign", new Vector3(at.x, 0f, at.y), Yaw(d));
                        k.Cylinder(sign, "Pole", new Vector3(0f, 1.2f, 0f), 0.07f, 2.4f, pole, collider: true);
                        GameObject plate = k.Cylinder(sign, "Plate", new Vector3(0f, 2.3f, -0.05f), 0.75f, 0.03f, c.P.Lit(new Color(0.75f, 0.1f, 0.08f), 0.3f));
                        plate.transform.localRotation = Quaternion.Euler(90f, 0f, 22.5f);
                        k.Text(sign, "STOP", new Vector3(0f, 2.3f, -0.08f), 0f, 0.16f, Color.white);
                    }
                }
            }

            // Pedestrian heads at both ends of signalised crosswalks, facing across.
            foreach (SidewalkGraph.Crosswalk cw in walks.Crosswalks)
            {
                if (!cw.Signalized) continue;
                Vector2 across = RoadNetwork.RightOf(cw.Along);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 end = cw.Center + across * side * (CityPlan.RoadHalfWidth + 0.5f) + cw.Along * 1.9f;
                    Transform head = Kit.Group(j, "Walk signal", new Vector3(end.x, 0f, end.y), Yaw(-across * side));
                    k.Cylinder(head, "Pole", new Vector3(0f, 1.25f, 0f), 0.09f, 2.5f, pole, collider: true);
                    k.Box(head, "Housing", new Vector3(0f, 2.35f, 0f), new Vector3(0.34f, 0.5f, 0.2f), housing, collider: false);
                    walkSignals.Add(new WalkSignal
                    {
                        Crosswalk = cw,
                        Wait = k.Box(head, "Hand", new Vector3(0f, 2.46f, 0.11f), new Vector3(0.22f, 0.18f, 0.02f), off, collider: false).GetComponent<Renderer>(),
                        Walk = k.Box(head, "Walker", new Vector3(0f, 2.24f, 0.11f), new Vector3(0.22f, 0.18f, 0.02f), off, collider: false).GetComponent<Renderer>(),
                    });
                }
            }
        }

        /// <summary>A clump of big trees far off in the fog (and sometimes a house among them).</summary>
        private static bool DistantTrees(CityContext c, Transform parent, Vector2 centre, float size, System.Random rng)
        {
            string[] kinds = ParkTrees;
            if (c.Kit.Art == null || c.Kit.Art.Model(kinds[0]) == null) return false;
            for (int t = 0; t < 5; t++)
            {
                Vector2 p = centre + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * size;
                c.Kit.Model(parent, kinds[rng.Next(kinds.Length)], new Vector3(p.x, CityPlan.RoadY, p.y),
                    (float)rng.NextDouble() * 360f, 10f + (float)rng.NextDouble() * 7f);
            }
            return true;
        }

        /// <summary>A kit skyscraper stretched to a skyline block's size (it's far off in the fog, proportions don't show).</summary>
        private static bool SkylineTower(CityContext c, Transform parent, Vector2 centre, float size, float height, int i)
        {
            string[] towers = { "building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c", "building-skyscraper-d", "building-skyscraper-e", "building-m", "building-n" };
            GameObject prefab = c.Kit.Art != null ? c.Kit.Art.Model(towers[i % towers.Length]) : null;
            if (prefab == null) return false;
            Bounds bounds = KitBuildings.Measure(prefab);
            GameObject go = c.Kit.Model(parent, prefab.name, new Vector3(centre.x, CityPlan.RoadY, centre.y), (i * 90f) % 360f,
                new Vector3(size / bounds.size.x, height / bounds.size.y, size / bounds.size.z));
            Texture2D palette = c.Kit.Art.Palette(i % 3 == 0 ? "CityCommercial/variation-b" : "CityCommercial/colormap");
            Material m = c.P.KitPalette(palette, c.Kit.Art.Palette(i % 3 == 0 ? "CityCommercial/variation-b-glow" : "CityCommercial/colormap-glow"), Color.white);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
            return true;
        }

        private static Renderer Lens(Kit k, Transform head, float y, Material off) =>
            k.Box(head, "Lens", new Vector3(0f, y, -0.16f), new Vector3(0.22f, 0.22f, 0.04f), off, collider: false).GetComponent<Renderer>();

        /// <summary>Invisible walls around the playable area, plus distant skyline blocks that sit in the fog.</summary>
        private static void Boundary(CityContext c, Transform root)
        {
            Rect w = CityPlan.World;
            Transform b = Kit.Group(root, "Boundary");
            void Wall(Vector3 min, Vector3 max)
            {
                var go = new GameObject("Boundary wall");
                go.transform.SetParent(b, false);
                var box = go.AddComponent<BoxCollider>();
                box.center = (min + max) / 2f;
                box.size = max - min;
            }
            Wall(new Vector3(w.xMin - 1f, -2f, w.yMin - 1f), new Vector3(w.xMax + 1f, 60f, w.yMin));
            Wall(new Vector3(w.xMin - 1f, -2f, w.yMax), new Vector3(w.xMax + 1f, 60f, w.yMax + 1f));
            Wall(new Vector3(w.xMin - 1f, -2f, w.yMin), new Vector3(w.xMin, 60f, w.yMax));
            Wall(new Vector3(w.xMax, -2f, w.yMin), new Vector3(w.xMax + 1f, 60f, w.yMax));

            var rng = new System.Random(7);
            Material far = c.P.Facade(FacadeStyle.Concrete, false), farGlass = c.P.Facade(FacadeStyle.Glass, false);
            Material roof = c.P.Lit(new Color(0.25f, 0.25f, 0.26f));
            for (int i = 0; i < 46; i++)
            {
                float angle = i / 46f * Mathf.PI * 2f;
                Vector2 centre = w.center + new Vector2(Mathf.Cos(angle) * (w.width / 2f + 90f + rng.Next(0, 90)), Mathf.Sin(angle) * (w.height / 2f + 90f + rng.Next(0, 90)));
                float size = 22f + rng.Next(0, 26), height = 18f + rng.Next(0, 60);
                // A town's horizon is trees and the odd roof, not towers.
                if (DistantTrees(c, b, centre, size, rng)) continue;
                if (SkylineTower(c, b, centre, size, height, i)) continue;
                c.Kit.Facade(b, "Skyline", new Vector3(centre.x - size / 2f, CityPlan.RoadY, centre.y - size / 2f),
                    new Vector3(centre.x + size / 2f, height, centre.y + size / 2f), i % 3 == 0 ? farGlass : far, roof, collider: false);
            }
        }
    }
}
