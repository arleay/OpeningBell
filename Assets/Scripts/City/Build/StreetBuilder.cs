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

    /// <summary>
    /// Streets from <see cref="StreetMap"/> (spec §9–10, TOWN_SPEC A3): the terrain, road surfaces and junction
    /// boxes, kerbed sidewalks with their corners, bridge decks, alleys and dirt roads, markings, kerb ramps,
    /// water, canal walls, street lights and trees, and the junction hardware (signals, stop signs, name blades).
    /// </summary>
    public static class StreetBuilder
    {
        private static readonly Color Asphalt = new Color(0.17f, 0.17f, 0.18f);
        private static readonly Color Concrete = new Color(0.64f, 0.63f, 0.6f);
        private static readonly Color PoleGray = new Color(0.26f, 0.27f, 0.28f);

        public static void Build(CityContext c, SidewalkGraph walks, List<SignalHead> signals, List<WalkSignal> walkSignals)
        {
            StreetMap map = c.Roads.Map;
            Transform root = Kit.Group(c.Static, "Streets");
            foreach (Rect r in CityPlan.Paved) c.Pads.Add(Pad.FromRect(r, 0f));
            c.Pads.Add(Pad.FromRect(CityPlan.MaplePark, 0f));
            c.Terrain = TerrainBuilder.Build(c.Dynamic, map, c.Pads);

            Roads(c, root, map);
            Driveways(c, root, map);
            foreach (Rect r in CityPlan.Paved)
                Tag(c.Kit.Span(root, "Paving", new Vector3(r.xMin, -0.05f, r.yMin), new Vector3(r.xMax, 0.005f, r.yMax), c.P.Lit(new Color(0.46f, 0.46f, 0.45f), 0.04f)), 0.25f);
            Water(c, root);
            Markings(c, root, map);
            CurbRamps(c, root, walks);
            Furniture(c, root, map);
            Park(c, root);
            Junctions(c, walks, signals, walkSignals);
            Boundary(c, root);
        }

        private static float Yaw(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        private static void Tag(GameObject ground, float roughness) => ground.AddComponent<SurfaceTag>().Roughness = roughness;
        private static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        // ---- roads, junctions, sidewalks, bridges ----

        private static void Roads(CityContext c, Transform root, StreetMap map)
        {
            var road = new MeshBuilder();
            var walk = new MeshBuilder();
            var deck = new MeshBuilder();
            const float drop = -CityPlan.RoadY, walkDepth = 0.6f;

            foreach (StreetMap.Segment s in map.Segments)
            {
                float t0 = s.AtA.Trim, t1 = s.Length - s.AtB.Trim;
                if (t1 > t0)
                {
                    Vector2 l = s.Left * s.HalfWidth;
                    road.Quad(V(s.At(t0) - l, s.GradeAt(t0) - drop), V(s.At(t1) - l, s.GradeAt(t1) - drop),
                        V(s.At(t1) + l, s.GradeAt(t1) - drop), V(s.At(t0) + l, s.GradeAt(t0) - drop));
                }
                if (s.Sidewalk > 0f)
                {
                    // Left side: from A's left start to B's right start; right side: A's right start to B's left start.
                    SidewalkStrip(walk, s, s.AtA.LeftStart, s.Length - s.AtB.RightStart, 1f, walkDepth);
                    SidewalkStrip(walk, s, s.AtA.RightStart, s.Length - s.AtB.LeftStart, -1f, walkDepth);
                }
                if (s.IsBridge) Bridge(deck, s);
            }

            foreach (StreetMap.Node n in map.Nodes)
            {
                if (n.Degree < 2) continue;
                float y = n.Grade - drop;
                // Junction box: each approach's road end, then the kerb corner to the next approach.
                var ring = new List<Vector3>();
                foreach (StreetMap.Corner corner in n.Corners)
                {
                    StreetMap.Approach a = corner.Left;
                    ring.Add(V(a.At(a.Trim, -a.HalfWidth), y));
                    ring.Add(V(a.At(a.Trim, a.HalfWidth), y));
                    ring.Add(V(corner.Curb, y));
                }
                road.Fan(V(n.P, y), ring);
                foreach (StreetMap.Corner corner in n.Corners)
                {
                    StreetMap.Approach i = corner.Left, j = corner.Right;
                    if (!corner.Walkable) continue;
                    float g = n.Grade;
                    // Corner: kerb corner, the left strip's end, the outer corner, the right strip's end.
                    var top = new List<Vector3>
                    {
                        V(corner.Curb, g),
                        V(i.At(i.LeftStart, i.HalfWidth), g), V(i.At(i.LeftStart, i.HalfWidth + i.Sidewalk), g),
                        V(corner.Outer, g),
                        V(j.At(j.RightStart, -(j.HalfWidth + j.Sidewalk)), g), V(j.At(j.RightStart, -j.HalfWidth), g),
                    };
                    Dedupe(top);
                    if (top.Count >= 3 && Area(top) > 0.05f) walk.Slab(top, walkDepth);
                }
            }

            road.Build(root, "Road surface", c.P.Lit(Asphalt, 0.05f), collider: true, roughness: 0.3f);
            walk.Build(root, "Sidewalks", c.P.Lit(Concrete, 0.08f), collider: true, roughness: 0.2f);
            deck.Build(root, "Bridge decks", c.P.Lit(new Color(0.55f, 0.54f, 0.51f), 0.05f), collider: true);
        }

        private static void SidewalkStrip(MeshBuilder walk, StreetMap.Segment s, float from, float to, float side, float depth)
        {
            if (to - from < 0.05f) return;
            Vector2 n = s.Left * side;
            float inner = s.HalfWidth, outer = s.HalfWidth + s.Sidewalk;
            Vector3 a0 = V(s.At(from) + n * inner, s.GradeAt(from)), a1 = V(s.At(from) + n * outer, s.GradeAt(from));
            Vector3 b0 = V(s.At(to) + n * inner, s.GradeAt(to)), b1 = V(s.At(to) + n * outer, s.GradeAt(to));
            // Counter-clockwise from above whichever side it's on.
            var top = side > 0f ? new List<Vector3> { a0, b0, b1, a1 } : new List<Vector3> { a1, b1, b0, a0 };
            walk.Slab(top, depth);
        }

        /// <summary>Deck under the road and sidewalks, parapets along the outer edges, over the water stretch.</summary>
        private static void Bridge(MeshBuilder deck, StreetMap.Segment s)
        {
            float half = s.HalfWidth + s.Sidewalk + 0.3f;
            float from = s.BridgeFrom, to = s.BridgeTo;
            Vector2 l = s.Left * half;
            float g0 = s.GradeAt(from), g1 = s.GradeAt(to);
            // Underside and sides of the deck (the road and sidewalk meshes are its top).
            deck.Quad(V(s.At(from) + l, g0 - 1.4f), V(s.At(to) + l, g1 - 1.4f), V(s.At(to) - l, g1 - 1.4f), V(s.At(from) - l, g0 - 1.4f));
            deck.Quad(V(s.At(from) - l, g0 - 1.4f), V(s.At(to) - l, g1 - 1.4f), V(s.At(to) - l, g1 + 0.95f), V(s.At(from) - l, g0 + 0.95f));
            deck.Quad(V(s.At(to) + l, g1 - 1.4f), V(s.At(from) + l, g0 - 1.4f), V(s.At(from) + l, g0 + 0.95f), V(s.At(to) + l, g1 + 0.95f));
            // Parapets: a 0.3 m wall, 0.95 m above the sidewalk, both faces and a top.
            foreach (float side in new[] { 1f, -1f })
            {
                Vector2 o = s.Left * side * (half - 0.3f);
                Vector2 edge = s.Left * side * half;
                deck.Quad(V(s.At(from) + o, g0), V(s.At(to) + o, g1), V(s.At(to) + edge, g1 + 0.95f), V(s.At(from) + edge, g0 + 0.95f));
                Vector3 p0 = V(s.At(from) + o, g0), p1 = V(s.At(to) + o, g1);
                if (side > 0f) deck.Quad(p1, p0, p0 + Vector3.up * 0.95f, p1 + Vector3.up * 0.95f);
                else deck.Quad(p0, p1, p1 + Vector3.up * 0.95f, p0 + Vector3.up * 0.95f);
            }
        }

        private static void Dedupe(List<Vector3> pts)
        {
            for (int i = pts.Count - 1; i >= 0 && pts.Count > 0; i--)
                if ((pts[i] - pts[(i + 1) % pts.Count]).sqrMagnitude < 1e-4f) pts.RemoveAt(i);
        }

        private static float Area(List<Vector3> pts)
        {
            float a = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i], q = pts[(i + 1) % pts.Count];
                a += p.x * q.z - q.x * p.z;
            }
            return a * 0.5f;
        }

        // ---- alleys and dirt roads ----

        private static void Driveways(CityContext c, Transform root, StreetMap map)
        {
            var alley = new MeshBuilder();
            var dirt = new MeshBuilder();
            var ramps = new List<(Vector2 P, Vector2 Dir, float Width, float Grade)>();
            foreach (StreetDef d in map.Driveways)
            {
                MeshBuilder mb = d.Class == RoadClass.Alley ? alley : dirt;
                float hw = CityPlan.HalfWidth(d.Class);
                float[] grade = TerrainBuilder.DrivewayGrades(map, d);
                var pts = new List<Vector2>(d.Points);
                // Ends on a street: start at the sidewalk's outer edge (a kerb ramp takes cars down to the road).
                for (int end = 0; end < 2; end++)
                {
                    int i = end == 0 ? 0 : pts.Count - 1, k = end == 0 ? 1 : pts.Count - 2;
                    StreetMap.Segment street = map.Nearest(pts[i], out float s, out float lateral);
                    if (street == null || Mathf.Abs(lateral) > 1f) continue;
                    Vector2 dir = (pts[k] - pts[i]).normalized;
                    float cos = Mathf.Abs(Vector2.Dot(dir, street.Left));
                    float edge = (street.HalfWidth + street.Sidewalk) / Mathf.Max(0.3f, cos);
                    float kerb = street.HalfWidth / Mathf.Max(0.3f, cos);
                    ramps.Add((pts[i] + dir * (kerb - 0.7f), dir, hw * 2f, street.GradeAt(s)));
                    pts[i] += dir * Mathf.Min(edge, Vector2.Distance(pts[i], pts[k]) - 1f);
                }
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    Vector2 a = pts[i], b = pts[i + 1];
                    Vector2 left = new Vector2(-(b - a).normalized.y, (b - a).normalized.x) * hw;
                    float ga = grade[i] + 0.01f, gb = grade[i + 1] + 0.01f;
                    // Overlap the next piece a little so bends don't show gaps.
                    Vector2 ext = (b - a).normalized * (i + 2 < pts.Count ? hw * 0.6f : 0f);
                    mb.Quad(V(a - left, ga), V(b + ext - left, gb), V(b + ext + left, gb), V(a + left, ga));
                }
            }
            alley.Build(root, "Alleys", c.P.Lit(new Color(0.25f, 0.25f, 0.25f), 0.05f), collider: true, roughness: 0.35f);
            dirt.Build(root, "Dirt roads", c.P.Lit(new Color(0.42f, 0.36f, 0.28f), 0.02f), collider: true, roughness: 0.75f);
            Material concrete = c.P.Lit(new Color(0.58f, 0.57f, 0.54f), 0.08f);
            foreach (var (p, dir, width, g) in ramps) Ramp(c.Kit, root, p, dir, width, g, concrete);
        }

        /// <summary>A slope up the 15 cm kerb, 1.4 m long, rising along <paramref name="up"/>.</summary>
        private static void Ramp(Kit k, Transform parent, Vector2 center, Vector2 up, float width, float grade, Material m)
        {
            const float run = 1.4f, rise = -CityPlan.RoadY, thickness = 0.06f;
            GameObject ramp = k.Box(parent, "Kerb ramp", new Vector3(center.x, grade + CityPlan.RoadY + rise / 2f - thickness / 2f, center.y),
                new Vector3(width, thickness, Mathf.Sqrt(run * run + rise * rise)), m);
            ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, Yaw(up), 0f);
            Tag(ramp, 0.2f);
        }

        // ---- water ----

        private static void Water(CityContext c, Transform root)
        {
            Transform w = Kit.Group(root, "Water");
            Material water = c.P.Glass(new Color(0.16f, 0.26f, 0.3f, 0.82f));
            Rect e = TownTerrain.Extent;
            var bay = new MeshBuilder();
            bay.Quad(new Vector3(e.xMin - 400f, TownTerrain.SeaLevel, e.yMin - 600f), new Vector3(e.xMax + 400f, TownTerrain.SeaLevel, e.yMin - 600f),
                new Vector3(e.xMax + 400f, TownTerrain.SeaLevel, -120f), new Vector3(e.xMin - 400f, TownTerrain.SeaLevel, -120f));
            bay.Build(w, "Bay", water, collider: false);
            var canal = new MeshBuilder();
            Rect cr = TownTerrain.Canal;
            canal.Quad(new Vector3(cr.xMin - 12f, TownTerrain.CanalWater, cr.yMin), new Vector3(cr.xMax + 12f, TownTerrain.CanalWater, cr.yMin),
                new Vector3(cr.xMax + 12f, TownTerrain.CanalWater, cr.yMax), new Vector3(cr.xMin - 12f, TownTerrain.CanalWater, cr.yMax));
            for (float x = e.xMin; x < e.xMax; x += 20f)
            {
                float z0 = TownTerrain.River(x), z1 = TownTerrain.River(x + 20f), hw = TownTerrain.RiverHalfWidth + 8f;
                canal.Quad(new Vector3(x, TownTerrain.RiverWater, z0 - hw), new Vector3(x + 20f, TownTerrain.RiverWater, z1 - hw),
                    new Vector3(x + 20f, TownTerrain.RiverWater, z1 + hw), new Vector3(x, TownTerrain.RiverWater, z0 + hw));
            }
            canal.Build(w, "Canal and river", water, collider: false);

            // Canal walls through town: concrete from the bank down to the bed, a low parapet on top (open at bridges).
            var walls = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side < 0f ? cr.xMin : cr.xMax;
                for (float z = cr.yMin; z < TownTerrain.CanalWallsNorth; z += 10f)
                {
                    float z1 = Mathf.Min(z + 10f, TownTerrain.CanalWallsNorth);
                    float b0 = Bank(x - side * 1.5f, z), b1 = Bank(x - side * 1.5f, z1);
                    Vector3 a = new Vector3(x, TownTerrain.CanalFloor - 0.5f, z), b = new Vector3(x, TownTerrain.CanalFloor - 0.5f, z1);
                    // Water side face, facing into the canal (a quad faces right of its first edge seen from above).
                    if (side < 0f) walls.Quad(a, b, new Vector3(x, b1, z1), new Vector3(x, b0, z));
                    else walls.Quad(b, a, new Vector3(x, b0, z), new Vector3(x, b1, z1));
                    if (NearBridge(z, z1) || Waterfront.ParapetGap(side < 0f, z, z1)) continue;
                    // Parapet 0.4 wide, 0.8 high, set back on the bank.
                    float px = x - side * 0.4f;
                    walls.Quad(new Vector3(x, b0, z), new Vector3(x, b1, z1), new Vector3(x, b1 + 0.8f, z1), new Vector3(x, b0 + 0.8f, z));
                    walls.Quad(new Vector3(px, b1, z1), new Vector3(px, b0, z), new Vector3(px, b0 + 0.8f, z), new Vector3(px, b1 + 0.8f, z1));
                    walls.Quad(new Vector3(px, b0 + 0.8f, z), new Vector3(x, b0 + 0.8f, z), new Vector3(x, b1 + 0.8f, z1), new Vector3(px, b1 + 0.8f, z1));
                }
            }
            walls.Build(w, "Canal walls", c.P.Lit(new Color(0.5f, 0.49f, 0.46f), 0.05f), collider: true);
            w.gameObject.AddComponent<WaterVolume>().Configure(c);
        }

        private static float Bank(float x, float z) => TownTerrain.Natural(x, z);

        private static bool NearBridge(float z0, float z1)
        {
            foreach (StreetMap.Segment s in StreetMap.Plan.Segments)
            {
                if (!s.IsBridge) continue;
                float z = s.At((s.BridgeFrom + s.BridgeTo) / 2f).y, half = s.HalfWidth + s.Sidewalk + 0.5f;
                if (z1 > z - half && z0 < z + half) return true;
            }
            return false;
        }

        // ---- markings, ramps ----

        private static void Markings(CityContext c, Transform root, StreetMap map)
        {
            var yellow = new MeshBuilder();
            var white = new MeshBuilder();
            const float lift = CityPlan.RoadY + 0.012f;
            foreach (StreetMap.Segment s in map.Segments)
            {
                if (!s.Traffic) continue;
                float t0 = s.AtA.StopLine, t1 = s.Length - s.AtB.StopLine;
                if (t1 - t0 > 2f && s.Class != RoadClass.Residential)
                    foreach (float side in new[] { -0.12f, 0.12f })
                        Strip(yellow, s, t0, t1, side, 0.1f, lift);
                foreach (StreetMap.Approach a in new[] { s.AtA, s.AtB })
                {
                    StreetMap.Node n = a.Node;
                    if (!n.IsJunction) continue;
                    bool stops = n.Control == NodeControl.Lights || (n.Control == NodeControl.StopOnStem && !n.IsMain(a));
                    // Stop bar across the lane coming in (the right half, seen from the approach).
                    if (stops)
                    {
                        float sl = a.StopLine - 0.3f;
                        Vector3 p0 = V(a.At(sl - 0.2f, 0f), a.GradeAt(sl) + lift), p1 = V(a.At(sl + 0.2f, 0f), a.GradeAt(sl) + lift);
                        Vector3 q0 = V(a.At(sl - 0.2f, a.HalfWidth - 0.3f), a.GradeAt(sl) + lift), q1 = V(a.At(sl + 0.2f, a.HalfWidth - 0.3f), a.GradeAt(sl) + lift);
                        white.Quad(p0, p1, q1, q0);
                    }
                    if (!a.HasCrosswalk) continue;
                    // Zebra stripes across the approach.
                    for (float o = -a.HalfWidth + 0.75f; o <= a.HalfWidth - 0.5f; o += 1f)
                    {
                        float g = a.GradeAt(a.Crosswalk) + lift;
                        float h = StreetMap.CrosswalkWidth / 2f - 0.05f;
                        white.Quad(V(a.At(a.Crosswalk - h, o - 0.25f), g), V(a.At(a.Crosswalk + h, o - 0.25f), g),
                            V(a.At(a.Crosswalk + h, o + 0.25f), g), V(a.At(a.Crosswalk - h, o + 0.25f), g));
                    }
                }
            }
            yellow.Build(root, "Centre lines", c.P.Lit(new Color(0.85f, 0.68f, 0.18f), 0.1f), collider: false);
            white.Build(root, "Road markings", c.P.Lit(new Color(0.88f, 0.88f, 0.86f), 0.1f), collider: false);
        }

        /// <summary>A painted line along a segment between two distances, offset sideways.</summary>
        private static void Strip(MeshBuilder mb, StreetMap.Segment s, float from, float to, float offset, float width, float lift)
        {
            Vector2 l = s.Left * (offset + width / 2f), r = s.Left * (offset - width / 2f);
            mb.Quad(V(s.At(from) + r, s.GradeAt(from) + lift), V(s.At(to) + r, s.GradeAt(to) + lift),
                V(s.At(to) + l, s.GradeAt(to) + lift), V(s.At(from) + l, s.GradeAt(from) + lift));
        }

        /// <summary>
        /// Ramps from the road up to the kerb at both ends of every crosswalk (spec §9): wheelchairs, bikes and
        /// skateboards (which can't climb a 15 cm kerb) use them.
        /// </summary>
        private static void CurbRamps(CityContext c, Transform root, SidewalkGraph walks)
        {
            Transform ramps = Kit.Group(root, "Curb ramps");
            Material concrete = c.P.Lit(new Color(0.6f, 0.59f, 0.56f), 0.08f);
            foreach (StreetMap.Node n in c.Roads.Map.Nodes)
            foreach (StreetMap.Approach a in n.Approaches)
            {
                if (!a.HasCrosswalk) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 mid = a.At(a.Crosswalk, side * (a.HalfWidth - 0.7f));
                    Ramp(c.Kit, ramps, mid, a.LeftDir * side, StreetMap.CrosswalkWidth - 0.2f, n.Grade, concrete);
                }
            }
        }

        // ---- furniture ----

        private static void Furniture(CityContext c, Transform root, StreetMap map)
        {
            Kit k = c.Kit;
            Transform f = Kit.Group(root, "Furniture");
            Material pole = c.P.Lit(PoleGray, 0.35f);
            Material lamp = c.P.Lamp(new Color(0.55f, 0.55f, 0.5f), new Color(1f, 0.86f, 0.6f));
            Material bark = c.P.Lit(new Color(0.3f, 0.22f, 0.15f));
            Material leaves = c.P.Lit(new Color(0.22f, 0.38f, 0.18f));
            Material leavesDark = c.P.Lit(new Color(0.18f, 0.32f, 0.16f));

            // Driveway mouths: nothing may stand across them.
            var mouths = new List<Vector2>();
            foreach (StreetDef d in map.Driveways) { mouths.Add(d.Points[0]); mouths.Add(d.Points[d.Points.Length - 1]); }

            foreach (StreetMap.Segment s in map.Segments)
            {
                if (s.Sidewalk < 2f) continue;
                bool leafy = s.Class != RoadClass.Industrial && !(s.At(s.Length / 2f).x > 135f && s.At(s.Length / 2f).x < 300f && s.At(s.Length / 2f).y < 170f);
                int index = 0;
                foreach (float side in new[] { -1f, 1f })
                {
                    float from = (side > 0f ? s.AtA.LeftStart : s.AtA.RightStart) + 6f;
                    float to = s.Length - (side > 0f ? s.AtB.RightStart : s.AtB.LeftStart) - 6f;
                    for (float t = from + 6f; t < to; t += 12f, index++)
                    {
                        if (s.IsBridge && t > s.BridgeFrom - 3f && t < s.BridgeTo + 3f) continue;
                        Vector2 kerb = s.At(t) + s.Left * side * (s.HalfWidth + 0.65f);
                        if (mouths.Exists(m => Vector2.Distance(m, s.At(t)) < 9f)) continue;
                        float g = s.GradeAt(t);
                        bool lightHere = index % 4 == (side > 0f ? 0 : 2);
                        if (lightHere) StreetLight(c, f, kerb, g, -s.Left * side, pole, lamp);
                        else if (leafy && index % 2 == 1 && s.Sidewalk >= 3f) Tree(k, f, kerb, g, bark, (index + (int)side) % 3 == 0 ? leavesDark : leaves);
                    }
                }
            }

            // Benches (pedestrians sit on them), bins and bike racks.
            Material wood = c.P.Lit(new Color(0.45f, 0.32f, 0.2f));
            (Vector3 P, float Yaw)[] benches =
            {
                // Yaw 0 = back to the north, facing south.
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
            // Dumpsters behind the Maple shops.
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

        private static void StreetLight(CityContext c, Transform parent, Vector2 p, float y, Vector2 towardRoad, Material pole, Material lamp)
        {
            Kit k = c.Kit;
            Transform light = Kit.Group(parent, "Street light", new Vector3(p.x, y, p.y), Yaw(towardRoad));
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

        private static void Tree(Kit k, Transform parent, Vector2 p, float y, Material bark, Material leaves)
        {
            Transform tree = Kit.Group(parent, "Tree", new Vector3(p.x, y, p.y));
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

        // ---- junction hardware ----

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
            var lanes = new Dictionary<StreetMap.Approach, RoadNetwork.Lane>();
            foreach (RoadNetwork.Lane lane in c.Roads.Lanes)
                lanes[lane.Segment.AtA.Node == lane.To.Map ? lane.Segment.AtA : lane.Segment.AtB] = lane;

            foreach (StreetMap.Node n in c.Roads.Map.Nodes)
            {
                if (!n.IsJunction) continue;
                // Street name sign on the first walkable corner.
                StreetMap.Corner signCorner = n.Corners.Find(x => x.Left.Sidewalk > 0f && x.Right.Sidewalk > 0f);
                if (signCorner != null)
                {
                    Vector2 at = Vector2.Lerp(signCorner.Curb, signCorner.Outer, 0.3f);
                    Vector3 corner = V(at, n.Grade);
                    k.Cylinder(names, "Name pole", corner + new Vector3(0f, 1.6f, 0f), 0.08f, 3.2f, pole, collider: true);
                    var streets = new List<(string Name, Vector2 Dir)>();
                    foreach (StreetMap.Approach a in n.Approaches)
                        if (!streets.Exists(s => s.Name == a.Segment.Street)) streets.Add((a.Segment.Street, a.Out));
                    for (int i = 0; i < streets.Count; i++)
                    {
                        float yaw = Yaw(streets[i].Dir) - 90f; // blade runs along the street
                        Transform b = Kit.Group(names, "Blade", corner + new Vector3(0f, 3.05f + 0.24f * i, 0f), yaw);
                        k.Box(b, "Plate", Vector3.zero, new Vector3(1.5f, 0.2f, 0.03f), blade, collider: false);
                        k.Text(b, streets[i].Name, new Vector3(0f, 0f, -0.02f), 0f, 0.11f, signText);
                        k.Text(b, streets[i].Name, new Vector3(0f, 0f, 0.02f), 180f, 0.11f, signText);
                    }
                }

                foreach (StreetMap.Approach a in n.Approaches)
                {
                    if (!a.Segment.Traffic || !lanes.TryGetValue(a, out RoadNetwork.Lane lane)) continue;
                    // Incoming traffic drives against Out, on the approach's left side (seen from the node).
                    Vector2 d = -a.Out;
                    float s = a.StopLine + 0.6f;
                    Vector2 at = a.At(s, a.HalfWidth + 0.6f);
                    if (n.Control == NodeControl.Lights)
                    {
                        Transform head = Kit.Group(j, "Signal", V(at, n.Grade), Yaw(d));
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
                        Transform sign = Kit.Group(names, "Stop sign", V(at, n.Grade), Yaw(d));
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
                StreetMap.Segment seg = c.Roads.Map.Nearest(cw.Center, out float t, out _);
                float hw = seg != null ? seg.HalfWidth : CityPlan.RoadHalfWidth, g = seg != null ? seg.GradeAt(t) : 0f;
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 end = cw.Center + across * side * (hw + 0.5f) + cw.Along * 1.9f;
                    Transform head = Kit.Group(j, "Walk signal", V(end, g), Yaw(-across * side));
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

        /// <summary>
        /// Walls only at the terrain's edge, far out in the forest and hills; the town itself ends at water,
        /// hills and trees.
        /// </summary>
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
            Wall(new Vector3(w.xMin - 1f, -30f, w.yMin - 1f), new Vector3(w.xMax + 1f, 120f, w.yMin));
            Wall(new Vector3(w.xMin - 1f, -30f, w.yMax), new Vector3(w.xMax + 1f, 120f, w.yMax + 1f));
            Wall(new Vector3(w.xMin - 1f, -30f, w.yMin), new Vector3(w.xMin, 120f, w.yMax));
            Wall(new Vector3(w.xMax, -30f, w.yMin), new Vector3(w.xMax + 1f, 120f, w.yMax));
        }
    }
}
