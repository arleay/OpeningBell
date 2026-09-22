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
            foreach (var (name, a, ground) in CityPlan.Blocks)
            {
                Transform b = Kit.Group(root, name);
                const float bottom = -0.9f;
                Tag(k.Span(b, "Sidewalk S", new Vector3(a.xMin, bottom, a.yMin), new Vector3(a.xMax, 0f, a.yMin + sw), walk), 0.2f);
                Tag(k.Span(b, "Sidewalk N", new Vector3(a.xMin, bottom, a.yMax - sw), new Vector3(a.xMax, 0f, a.yMax), walk), 0.2f);
                Tag(k.Span(b, "Sidewalk W", new Vector3(a.xMin, bottom, a.yMin + sw), new Vector3(a.xMin + sw, 0f, a.yMax - sw), walk), 0.2f);
                Tag(k.Span(b, "Sidewalk E", new Vector3(a.xMax - sw, bottom, a.yMin + sw), new Vector3(a.xMax, 0f, a.yMax - sw), walk), 0.2f);
                // The residential block is lawn and park; the others are paved.
                Tag(k.Span(b, "Ground", new Vector3(a.xMin + sw, bottom, a.yMin + sw), new Vector3(a.xMax - sw, -0.01f, a.yMax - sw), c.P.Lit(ground, 0.04f)),
                    name == "Residential" ? 1f : 0.25f);
            }

            Markings(c, root, walks);
            CurbRamps(c, root, walks);
            Furniture(c, root);
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
                k.Cylinder(f, "Bin", p + bench.right * 1.4f + new Vector3(0f, 0.45f, 0f), 0.5f, 0.9f, c.P.Lit(new Color(0.2f, 0.28f, 0.22f)), collider: true);
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
            foreach (Vector3 p in new[] { new Vector3(49f, 0f, -8.3f), new Vector3(141f, 0f, -8.3f), new Vector3(-39f, 0f, 64f) })
                k.Cylinder(f, "Hydrant", p + new Vector3(0f, 0.35f, 0f), 0.24f, 0.7f, c.P.Lit(new Color(0.7f, 0.15f, 0.1f), 0.4f), collider: true);
        }

        private static void StreetLight(CityContext c, Transform parent, Vector2 p, Vector2 towardRoad, Material pole, Material lamp)
        {
            Kit k = c.Kit;
            Transform light = Kit.Group(parent, "Street light", new Vector3(p.x, 0f, p.y), Yaw(towardRoad));
            k.Cylinder(light, "Pole", new Vector3(0f, 3f, 0f), 0.14f, 6f, pole, collider: true);
            k.Box(light, "Arm", new Vector3(0f, 5.9f, 0.7f), new Vector3(0.08f, 0.08f, 1.5f), pole, collider: false);
            k.Box(light, "Lamp", new Vector3(0f, 5.8f, 1.35f), new Vector3(0.32f, 0.12f, 0.55f), lamp, collider: false);
        }

        private static void Tree(Kit k, Transform parent, Vector2 p, Material bark, Material leaves)
        {
            Transform tree = Kit.Group(parent, "Tree", new Vector3(p.x, 0f, p.y));
            k.Box(tree, "Pit", new Vector3(0f, 0.005f, 0f), new Vector3(1.2f, 0.02f, 1.2f), k.P.Lit(new Color(0.26f, 0.2f, 0.15f)), collider: false);
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
                c.Kit.Facade(b, "Skyline", new Vector3(centre.x - size / 2f, CityPlan.RoadY, centre.y - size / 2f),
                    new Vector3(centre.x + size / 2f, height, centre.y + size / 2f), i % 3 == 0 ? farGlass : far, roof, collider: false);
            }
        }
    }
}
