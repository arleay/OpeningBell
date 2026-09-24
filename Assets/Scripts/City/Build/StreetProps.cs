using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Street-side things with a purpose (TOWN_SPEC A11, A15): bus stops where people wait, and the clutter that
    /// makes a street look used.
    /// </summary>
    public static class StreetProps
    {
        /// <summary>Bus stops: a point on the street's centre line and which side (+1 left of the segment, -1 right).</summary>
        private static readonly (Vector2 At, int Side, string Name)[] Stops =
        {
            (new Vector2(-195f, -14f), 1, "Maple & Willow"), (new Vector2(-90f, -14f), -1, "Maple & Cedar"), (new Vector2(22f, -14f), 1, "Maple Park"),
            (new Vector2(100f, -14f), -1, "Main Street"), (new Vector2(250f, -14f), 1, "City Hall"), (new Vector2(400f, -14f), -1, "Canal Row"),
            (new Vector2(-100f, 70f), 1, "Grove & Cedar"), (new Vector2(100f, 70f), -1, "Grove & First"), (new Vector2(0f, -270f), 1, "Harbor Rd"),
            (new Vector2(-80f, -160f), 1, "Foundry St"), (new Vector2(-320f, -14f), 1, "FreshWay"),
        };

        public static void BusStops(CityContext c)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Bus stops");
            Material frame = c.P.Lit(new Color(0.22f, 0.24f, 0.26f), 0.4f);
            Material glass = c.P.Glass(new Color(0.7f, 0.8f, 0.85f, 0.3f));
            Material sign = c.P.Lit(new Color(0.1f, 0.35f, 0.6f), 0.3f);
            foreach (var (at, side, name) in Stops)
            {
                StreetMap.Segment s = c.Roads.Map.Nearest(at, out float t, out _);
                if (s == null || s.Sidewalk <= 0f) continue;
                Vector2 outward = s.Left * side;
                float g = s.GradeAt(t);
                // Local frame: origin on the kerb, +z away from the street (the shelter at the sidewalk's back edge).
                Vector2 kerb = s.At(t) + outward * s.HalfWidth;
                float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
                Transform stop = Kit.Group(root, "Bus stop " + name, new Vector3(kerb.x, g, kerb.y), yaw);
                float back = s.Sidewalk - 0.1f;
                k.Box(stop, "Sign pole", new Vector3(-2f, 1.4f, 0.4f), new Vector3(0.08f, 2.8f, 0.08f), frame);
                k.Box(stop, "Sign", new Vector3(-2f, 2.6f, 0.4f), new Vector3(0.5f, 0.5f, 0.04f), sign, collider: false);
                k.Text(stop, "BUS", new Vector3(-2f, 2.6f, 0.37f), 0f, 0.12f, Color.white);
                k.Box(stop, "Roof", new Vector3(0f, 2.5f, back - 0.65f), new Vector3(3.2f, 0.1f, 1.4f), frame, collider: false);
                k.Box(stop, "Back", new Vector3(0f, 1.3f, back), new Vector3(3.2f, 2.3f, 0.05f), glass);
                foreach (float x in new[] { -1.55f, 1.55f })
                    k.Box(stop, "Post", new Vector3(x, 1.25f, back - 0.65f), new Vector3(0.06f, 2.5f, 1.35f), glass, collider: false);
                k.Box(stop, "Bench", new Vector3(0f, 0.45f, back - 0.35f), new Vector3(2.4f, 0.08f, 0.45f), c.P.Lit(new Color(0.45f, 0.32f, 0.2f)));
                k.Text(stop, name.ToUpperInvariant(), new Vector3(0f, 2.3f, back - 0.03f), 0f, 0.08f, Color.white);
                string tag = "bus stop " + name;
                c.Place(stop.TransformPoint(new Vector3(0.8f, 0f, 1.2f)), PlaceKind.Stand, tag);
                c.PlaceInfo[tag] = (PlaceCategory.Stand, Hours.Of(5.5, 24));
            }
        }

        // ---- A15: clutter ----

        /// <summary>
        /// The small things that make streets look used: newspaper boxes, bins, planters, hydrants, mailboxes and
        /// parking signs on the kerb strip; manholes, drains, patches, oil stains and litter on the roads; dumpsters,
        /// bags, boxes, pallets and graffiti down the alleys. Solid pieces only go where nothing else stands, and
        /// never in front of a door, a bus stop or a driveway.
        /// </summary>
        public static void Clutter(CityContext c)
        {
            Physics.SyncTransforms();
            Transform root = Kit.Group(c.Static, "Clutter");
            var rng = new System.Random(1515);
            var clear = new List<(Vector2 P, float R)>();
            foreach (var (p, kind, _) in c.Places)
                if (kind == PlaceKind.Door || kind == PlaceKind.Stand) clear.Add((p, 3f));
            foreach (StreetDef d in c.Roads.Map.Driveways)
            {
                clear.Add((d.Points[0], 9f));
                clear.Add((d.Points[d.Points.Length - 1], 9f));
            }
            Sidewalks(c, root, rng, clear);
            RoadWear(c, root, rng);
            Alleys(c, root, rng);
            // The merged road wear spans the town (its bounds would never cull); the standing clutter can go.
            foreach (Transform group in root)
                if (group.name.EndsWith(" clutter")) CityLayers.Set(group, CityLayers.Props);
        }

        /// <summary>Nothing solid in a box of <paramref name="half"/> extents standing on <paramref name="at"/> (ground and road meshes don't count).</summary>
        private static bool Free(Vector3 at, float yaw, Vector3 half)
        {
            int n = Physics.OverlapBoxNonAlloc(at + Vector3.up * (half.y + 0.08f), half, Hits, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!(Hits[i] is TerrainCollider) && !(Hits[i] is MeshCollider)) return false;
            return true;
        }

        private static readonly Collider[] Hits = new Collider[8];

        private static void Sidewalks(CityContext c, Transform root, System.Random rng, List<(Vector2 P, float R)> clear)
        {
            Kit k = c.Kit;
            Transform f = Kit.Group(root, "Sidewalk clutter");
            Material pole = c.P.Lit(new Color(0.35f, 0.36f, 0.37f), 0.35f);
            Material blue = c.P.Lit(new Color(0.12f, 0.25f, 0.55f), 0.35f);
            Material white = c.P.Lit(new Color(0.92f, 0.92f, 0.9f), 0.2f);
            Material dark = c.P.Lit(new Color(0.08f, 0.09f, 0.1f), 0.8f);
            Material[] papers =
            {
                c.P.Lit(new Color(0.75f, 0.2f, 0.15f), 0.3f), c.P.Lit(new Color(0.15f, 0.35f, 0.65f), 0.3f),
                c.P.Lit(new Color(0.9f, 0.75f, 0.2f), 0.3f), c.P.Lit(new Color(0.2f, 0.45f, 0.25f), 0.3f),
            };
            foreach (StreetMap.Segment s in c.Roads.Map.Segments)
            {
                if (s.Sidewalk < 2f) continue;
                bool commercial = System.Array.IndexOf(ParkedCars.Commercial, s.Street) >= 0;
                foreach (float side in new[] { -1f, 1f })
                {
                    float from = (side > 0f ? s.AtA.LeftStart : s.AtA.RightStart) + 6f;
                    float to = s.Length - (side > 0f ? s.AtB.RightStart : s.AtB.LeftStart) - 6f;
                    // Halfway between the lamp and tree slots (those sit at from + 6 + 12i).
                    for (float t = from + 12f; t < to - 2f; t += 12f)
                    {
                        if (s.IsBridge && t > s.BridgeFrom - 3f && t < s.BridgeTo + 3f) continue;
                        Vector2 outward = s.Left * side;
                        Vector2 p = s.At(t) + outward * (s.HalfWidth + 0.6f);
                        double roll = rng.NextDouble();
                        string item = commercial
                            ? (roll < 0.2 ? "papers" : roll < 0.4 ? "bin" : roll < 0.52 ? "planter" : roll < 0.62 ? "sign" : roll < 0.7 ? "hydrant" : roll < 0.76 ? "mail" : roll < 0.8 ? "works" : null)
                            : (roll < 0.14 ? "hydrant" : roll < 0.19 ? "mail" : roll < 0.28 ? "bin" : roll < 0.31 ? "works" : null);
                        if (item == null || clear.Exists(x => (x.P - p).sqrMagnitude < x.R * x.R)) continue;
                        // Local frame: +z away from the street, toward the buildings.
                        float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
                        var at = new Vector3(p.x, s.GradeAt(t), p.y);
                        if (!Free(at, yaw, new Vector3(item == "papers" ? 0.75f : item == "works" ? 1.7f : 0.4f, 0.6f, 0.35f))) continue;
                        Transform g = Kit.Group(f, item, at, yaw);
                        switch (item)
                        {
                            case "hydrant":
                                Hydrant(c, g, Vector3.zero, 0f);
                                break;
                            case "mail":
                                foreach (float x in new[] { -0.2f, 0.2f })
                                    k.Box(g, "Leg", new Vector3(x, 0.12f, 0f), new Vector3(0.05f, 0.24f, 0.4f), blue, collider: false);
                                k.Box(g, "Box", new Vector3(0f, 0.7f, 0f), new Vector3(0.52f, 0.9f, 0.46f), blue);
                                GameObject top = k.Cylinder(g, "Top", new Vector3(0f, 1.15f, 0f), 0.52f, 0.46f, blue);
                                top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                                k.Text(g, "MAIL", new Vector3(0f, 0.85f, 0.24f), 180f, 0.09f, Color.white);
                                break;
                            case "papers":
                                int count = 2 + rng.Next(2);
                                for (int i = 0; i < count; i++)
                                {
                                    float x = (i - (count - 1) / 2f) * 0.48f;
                                    k.Box(g, "Newspaper box", new Vector3(x, 0.5f, 0f), new Vector3(0.44f, 1f, 0.42f), papers[rng.Next(papers.Length)]);
                                    k.Box(g, "Window", new Vector3(x, 0.72f, 0.215f), new Vector3(0.32f, 0.22f, 0.01f), dark, collider: false);
                                }
                                break;
                            case "bin":
                                TrashCan(c, g, Vector3.zero, 0f);
                                break;
                            case "works":
                                // Kerbside works: a water-filled barrier (red, now and then concrete) along the kerb, a cone at each end.
                                GameObject barrier = k.Fit(g, rng.NextDouble() < 0.7 ? "street_red_barrier" : "street_concrete_barrier",
                                    new Vector3(0f, 0f, 0.1f), new Vector3(1.8f, 0f, 0f));
                                if (barrier == null) break;
                                k.Solid(barrier);
                                foreach (float x in new[] { -1.35f, 1.35f })
                                    k.Fit(g, "street_cone", new Vector3(x, 0f, -0.1f), new Vector3(0f, 0.7f, 0f), rng.Next(4) * 90f);
                                break;
                            case "planter":
                                GameObject pot = k.Prop(g, "planter", Vector3.zero, 0.75f);
                                if (pot != null) k.Solid(pot);
                                else k.Box(g, "Planter", new Vector3(0f, 0.35f, 0f), new Vector3(0.8f, 0.7f, 0.8f), c.P.Lit(new Color(0.5f, 0.48f, 0.45f)));
                                break;
                            case "sign":
                                // Parking rules face along the kerb, printed on both sides.
                                k.Cylinder(g, "Pole", new Vector3(0f, 1.2f, 0f), 0.06f, 2.4f, pole, collider: true);
                                k.Box(g, "Sign", new Vector3(0f, 2.05f, 0f), new Vector3(0.02f, 0.46f, 0.32f), white, collider: false);
                                foreach (float x in new[] { -1f, 1f })
                                {
                                    k.Text(g, "2 HR\nPARKING", new Vector3(x * 0.015f, 2.12f, 0f), x > 0f ? -90f : 90f, 0.07f, new Color(0.1f, 0.4f, 0.15f));
                                    k.Text(g, "8AM-6PM", new Vector3(x * 0.015f, 1.93f, 0f), x > 0f ? -90f : 90f, 0.05f, new Color(0.1f, 0.1f, 0.1f));
                                }
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>A red fire hydrant (street pack, 80 cm), solid; a plain red post where the art is missing.</summary>
        internal static void Hydrant(CityContext c, Transform parent, Vector3 at, float yaw)
        {
            GameObject h = c.Kit.Fit(parent, "street_hydrant", at, new Vector3(0f, 0.8f, 0f), Mathf.Round(yaw / 90f) * 90f);
            if (h != null)
            {
                c.Kit.Solid(h);
                return;
            }
            Material red = c.P.Lit(new Color(0.7f, 0.15f, 0.1f), 0.4f);
            c.Kit.Cylinder(parent, "Body", at + new Vector3(0f, 0.3f, 0f), 0.26f, 0.6f, red, collider: true);
            c.Kit.Sphere(parent, "Dome", at + new Vector3(0f, 0.65f, 0f), 0.22f, red);
        }

        /// <summary>
        /// A street bin (Sketchfab street pack, 1 m tall) with a solid body and, now and then, a bin bag dumped beside
        /// it; the Kenney kit's bin, then a plain drum, where the art is missing.
        /// </summary>
        internal static void TrashCan(CityContext c, Transform parent, Vector3 at, float yaw)
        {
            GameObject street = c.Kit.Fit(parent, "street_bin", at, new Vector3(0f, 1f, 0f), Mathf.Round(yaw / 90f) * 90f);
            if (street != null)
            {
                c.Kit.Solid(street);
                // Deterministic per spot (no RNG stream here): about one bin in three has a bag beside it.
                if (Mathf.Abs(Mathf.Sin(at.x * 12.9898f + at.z * 78.233f)) < 0.33f)
                    c.Kit.Fit(parent, "street_trashbag", at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.75f, 0f, 0.1f), new Vector3(0f, 0.6f, 0f), Mathf.Round(yaw / 90f) * 90f + 90f);
                return;
            }
            GameObject bin = c.Kit.Model(parent, "trashcan", at, yaw, 0.22f);
            if (bin == null)
            {
                c.Kit.Cylinder(parent, "Bin", at + new Vector3(0f, 0.45f, 0f), 0.5f, 0.9f, c.P.Lit(new Color(0.2f, 0.28f, 0.22f)), collider: true);
                return;
            }
            var solid = bin.AddComponent<CapsuleCollider>(); // model units: ×0.22 is a 0.24 m radius, 0.95 m tall
            solid.radius = 1.1f;
            solid.height = 4.3f;
            solid.center = new Vector3(0f, 2.15f, 0f);
        }

        /// <summary>
        /// Wear on the traffic roads, merged into one mesh per material: iron (manholes in the lanes, drains in the
        /// gutters), darker patches (more on the old industrial and residential streets), oil stains where cars park,
        /// and paper litter in the downtown gutters. Heights sit around the markings' so nothing flickers.
        /// </summary>
        private static void RoadWear(CityContext c, Transform root, System.Random rng)
        {
            var iron = new MeshBuilder();
            var patch = new MeshBuilder();
            var stain = new MeshBuilder();
            var litter = new MeshBuilder();
            var cracks = new MeshBuilder();
            foreach (StreetMap.Segment s in c.Roads.Map.Segments)
            {
                if (!CityPlan.HasTraffic(s.Class)) continue;
                float from = s.AtA.Trim + 3f, to = s.Length - s.AtB.Trim - 3f;
                bool Deck(float t) => s.IsBridge && t > s.BridgeFrom - 2f && t < s.BridgeTo + 2f;
                int lane = 0;
                for (float t = from + 18f; t < to; t += 42f, lane++)
                {
                    if (Deck(t)) continue;
                    float lateral = (lane % 2 == 0 ? 1f : -1f) * CityPlan.LaneOffsetFor(s.Class);
                    Vector2 m = s.At(t) + s.Left * lateral;
                    var ring = new List<Vector3>();
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * Mathf.PI * 2f / 12f;
                        ring.Add(new Vector3(m.x + Mathf.Cos(a) * 0.38f, s.GradeAt(t) + CityPlan.RoadY + 0.016f, m.y + Mathf.Sin(a) * 0.38f));
                    }
                    iron.Fan(new Vector3(m.x, s.GradeAt(t) + CityPlan.RoadY + 0.016f, m.y), ring);
                }
                for (float t = from + 6f; t < to; t += 36f)
                    foreach (float side in new[] { -1f, 1f })
                        if (!Deck(t)) OnRoad(iron, s, t, side * (s.HalfWidth - 0.3f), 0.45f, 0.22f, 0.016f);
                float every = s.Class == RoadClass.Street ? 45f : 22f;
                for (float t = from + (float)rng.NextDouble() * every; t < to; t += every * (0.6f + (float)rng.NextDouble() * 0.8f))
                {
                    if (Deck(t)) continue;
                    float lateral = ((float)rng.NextDouble() - 0.5f) * s.HalfWidth * 1.4f;
                    OnRoad(patch, s, t, lateral, 0.8f + (float)rng.NextDouble() * 2.2f, 0.5f + (float)rng.NextDouble() * 1.1f, 0.006f);
                }
                // Tar-sealed cracks: meandering black lines, along the lanes or across them; more on the
                // older local streets than on the arterials.
                float crackEvery = s.Class == RoadClass.Street ? 16f : 30f;
                for (float t = from + (float)rng.NextDouble() * crackEvery; t < to; t += crackEvery * (0.5f + (float)rng.NextDouble()))
                    if (!Deck(t)) Crack(cracks, rng, s, t, ((float)rng.NextDouble() - 0.5f) * s.HalfWidth * 1.6f, 8 + rng.Next(10), rng.NextDouble() < 0.5);
                bool commercial = System.Array.IndexOf(ParkedCars.Commercial, s.Street) >= 0;
                for (float t = from + 3f; t < to; t += 6.5f)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (Deck(t)) continue;
                        if (rng.NextDouble() < 0.3)
                            OnRoad(stain, s, t + (float)(rng.NextDouble() - 0.5), side * (s.HalfWidth - 1.1f), 0.35f + (float)rng.NextDouble() * 0.3f, 0.25f + (float)rng.NextDouble() * 0.25f, 0.009f);
                        if (commercial && rng.NextDouble() < 0.35)
                            Litter(litter, rng, s.At(t) + s.Left * side * (s.HalfWidth - 0.25f), s.GradeAt(t) + CityPlan.RoadY + 0.02f);
                    }
            }
            iron.Build(root, "Manholes and drains", c.P.Lit(new Color(0.16f, 0.16f, 0.17f), 0.45f), collider: false);
            patch.Build(root, "Road patches", c.P.Wettable(c.P.Lit(new Color(0.165f, 0.165f, 0.172f), 0.08f)), collider: false);
            stain.Build(root, "Oil stains", c.P.Wettable(c.P.Lit(new Color(0.07f, 0.07f, 0.08f), 0.35f)), collider: false);
            litter.Build(root, "Litter", c.P.Lit(new Color(0.85f, 0.83f, 0.76f), 0.05f), collider: false);
            cracks.Build(root, "Sealed cracks", c.P.Wettable(c.P.Lit(new Color(0.045f, 0.045f, 0.05f), 0.5f)), collider: false);
        }

        /// <summary>
        /// One crack as a strip of short (30-60 cm), 8 cm wide steps from (t, lateral) on the segment: a random walk
        /// that keeps roughly its heading (along the road when <paramref name="along"/>, else across), held off the kerbs.
        /// </summary>
        private static void Crack(MeshBuilder mb, System.Random rng, StreetMap.Segment s, float t, float lateral, int steps, bool along)
        {
            Vector3 P(float at, float side) => To3(s.At(Mathf.Clamp(at, 0f, s.Length)) + s.Left * side, s.GradeAt(Mathf.Clamp(at, 0f, s.Length)) + CityPlan.RoadY + 0.0075f);
            float heading = (along ? 0f : Mathf.PI / 2f) + ((float)rng.NextDouble() - 0.5f) * 0.8f;
            if (rng.NextDouble() < 0.5) heading += Mathf.PI;
            float limit = s.HalfWidth - 0.35f;
            for (int i = 0; i < steps; i++)
            {
                heading += ((float)rng.NextDouble() - 0.5f) * 0.9f;
                float step = 0.3f + (float)rng.NextDouble() * 0.3f;
                float dt = Mathf.Cos(heading) * step, dl = Mathf.Sin(heading) * step;
                float nextL = Mathf.Clamp(lateral + dl, -limit, limit);
                float w = 0.04f * (1f - 0.5f * i / steps); // 8 cm of tar overband, tapering toward the end
                // Perpendicular in (t, lateral): the strip's half-width either side of the step.
                float len = Mathf.Max(0.01f, Mathf.Sqrt(dt * dt + (nextL - lateral) * (nextL - lateral)));
                float pt = -(nextL - lateral) / len * w, pl = dt / len * w;
                mb.Quad(P(t - pt, lateral - pl), P(t + dt - pt, nextL - pl), P(t + dt + pt, nextL + pl), P(t + pt, lateral + pl));
                t += dt;
                lateral = nextL;
            }
        }

        private static Vector3 To3(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        /// <summary>A flat rectangle on the road surface, <paramref name="halfL"/> along the street, following its grade.</summary>
        private static void OnRoad(MeshBuilder mb, StreetMap.Segment s, float t, float lateral, float halfL, float halfW, float lift)
        {
            Vector3 P(float dt, float dl)
            {
                Vector2 q = s.At(t + dt) + s.Left * (lateral + dl);
                return new Vector3(q.x, s.GradeAt(Mathf.Clamp(t + dt, 0f, s.Length)) + CityPlan.RoadY + lift, q.y);
            }
            mb.Quad(P(-halfL, -halfW), P(halfL, -halfW), P(halfL, halfW), P(-halfL, halfW));
        }

        /// <summary>A scrap of paper or a flattened cup, turned any which way.</summary>
        private static void Litter(MeshBuilder mb, System.Random rng, Vector2 p, float y)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var l = new Vector2(-d.y, d.x);
            float hl = 0.1f + (float)rng.NextDouble() * 0.1f, hw = 0.07f + (float)rng.NextDouble() * 0.06f;
            Vector3 V(float u, float v) { Vector2 q = p + d * u + l * v; return new Vector3(q.x, y, q.y); }
            mb.Quad(V(-hl, -hw), V(hl, -hw), V(hl, hw), V(-hl, hw));
        }

        /// <summary>
        /// Along each alley, against the building backs: dumpsters, bin bags, boxes, pallets, utility cabinets and
        /// graffiti. A sideways ray finds the wall; anything that would stick into the lane is left out.
        /// </summary>
        private static void Alleys(CityContext c, Transform root, System.Random rng)
        {
            Kit k = c.Kit;
            Transform f = Kit.Group(root, "Alley clutter");
            Material bag = c.P.Lit(new Color(0.05f, 0.07f, 0.06f), 0.3f);
            float half = CityPlan.HalfWidth(RoadClass.Alley);
            var litter = new MeshBuilder();
            foreach (StreetDef d in c.Roads.Map.Driveways)
            {
                if (d.Class != RoadClass.Alley) continue;
                for (int i = 0; i + 1 < d.Points.Length; i++)
                {
                    Vector2 a = d.Points[i], b = d.Points[i + 1];
                    float len = Vector2.Distance(a, b);
                    Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                    for (float t = 8f; t < len - 8f; t += 7f)
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Vector2 mid = a + dir * t;
                            if (!Ground(mid, out float ground)) continue;
                            if (rng.NextDouble() < 0.2)
                                Litter(litter, rng, mid + left * side * (half - 0.3f - (float)rng.NextDouble() * 1.2f) + dir * (float)rng.NextDouble() * 6f, ground + 0.02f);
                            var outDir = new Vector3(left.x * side, 0f, left.y * side);
                            bool walled = Physics.Raycast(new Vector3(mid.x, ground + 1.2f, mid.y), outDir, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)
                                          && !(hit.collider is TerrainCollider);
                            float wall = walled ? hit.distance : half + 2f;
                            if (walled && rng.NextDouble() < 0.18)
                                Industrial.Graffiti(c, f, hit.point - outDir * 0.02f + Vector3.up * (0.4f + (float)rng.NextDouble() * 0.8f),
                                    Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg, rng);
                            if (rng.NextDouble() > 0.42) continue;
                            double roll = rng.NextDouble();
                            float depth = roll < 0.3 ? 1.05f : 0.45f; // half the item's depth from the wall
                            float lateral = Mathf.Min(wall, half + 2f) - depth - 0.15f;
                            if (lateral < half - 0.4f) continue; // no room beside the lane
                            Vector2 p = mid + left * side * lateral;
                            // Local frame: +z toward the wall, so fronts face the alley.
                            float yaw = Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg;
                            var at = new Vector3(p.x, ground, p.y);
                            if (roll < 0.3)
                            {
                                if (!Free(at, yaw, new Vector3(1.4f, 0.8f, 1f))) continue;
                                // The gas-station pack's rusty blue dumpster, 2 m long along the wall; the Kenney one where it's missing.
                                GameObject rusty = k.Fit(f, "gas_dumpster", at, new Vector3(2f, 0f, 0f), yaw);
                                if (rusty != null)
                                {
                                    k.Solid(rusty);
                                    continue;
                                }
                                GameObject dumpster = k.Model(f, "dumpster", at, yaw + 90f, Kit.CommercialScale);
                                if (dumpster == null) continue;
                                var solid = dumpster.AddComponent<BoxCollider>(); // model units (×7.4 ≈ 2.1 × 1.6 × 2.7 m)
                                solid.center = new Vector3(0f, 0.105f, 0f);
                                solid.size = new Vector3(0.28f, 0.21f, 0.37f);
                            }
                            else if (roll < 0.55)
                            {
                                Transform g = Kit.Group(f, "Bin bags", at, yaw);
                                int n = 2 + rng.Next(4);
                                if (rng.NextDouble() < 0.3 && k.Fit(g, "street_barrel", Vector3.zero, new Vector3(0f, 0.95f, 0f)) is GameObject barrel)
                                {
                                    k.Solid(barrel); // an oil drum for a bin, a bag or two beside it
                                    n = 1 + rng.Next(2);
                                }
                                for (int j = 0; j < n; j++)
                                {
                                    // The street pack's bags (two shapes) at 55-75 cm; black spheres where they're missing.
                                    var spot = new Vector3(((float)rng.NextDouble() - 0.5f) * 1.2f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.5f);
                                    if (g.childCount > 0 && g.GetChild(0).name == "street_barrel") spot.x = 0.85f + j * 0.65f;
                                    GameObject model = k.Fit(g, j % 2 == 0 ? "street_trashbag" : "street_trashbag2", spot,
                                        new Vector3(0f, 0.55f + (float)rng.NextDouble() * 0.2f, 0f), rng.Next(4) * 90f);
                                    if (model != null) continue;
                                    GameObject sack = k.Sphere(g, "Bag", new Vector3(((float)rng.NextDouble() - 0.5f) * 1.2f, 0.25f, ((float)rng.NextDouble() - 0.5f) * 0.5f), 0.6f, bag);
                                    sack.transform.localScale = new Vector3(0.6f, 0.5f + (float)rng.NextDouble() * 0.2f, 0.55f);
                                }
                            }
                            else if (roll < 0.75)
                            {
                                if (!Free(at, yaw, new Vector3(0.6f, 0.4f, 0.4f))) continue;
                                Transform g = Kit.Group(f, "Boxes", at, yaw);
                                int n = 1 + rng.Next(3);
                                for (int j = 0; j < n; j++)
                                {
                                    string box = rng.Next(2) == 0 ? "cardboardBoxClosed" : "cardboardBoxOpen";
                                    var bottom = new Vector3(-0.4f + j * 0.45f, j == 2 ? 0.42f : 0f, 0f);
                                    if (j == 2) bottom.x = -0.2f;
                                    if (k.Prop(g, box, bottom, 0.4f, (float)rng.NextDouble() * 40f - 20f) == null)
                                        k.Box(g, "Box", bottom + new Vector3(0f, 0.2f, 0f), new Vector3(0.45f, 0.4f, 0.4f), c.P.Lit(new Color(0.62f, 0.48f, 0.32f)), collider: false);
                                }
                            }
                            else if (roll < 0.9)
                            {
                                if (!Free(at, yaw, new Vector3(1.5f, 0.3f, 0.55f))) continue;
                                Transform g = Kit.Group(f, "Pallets", at, yaw);
                                Industrial.Pallets(c, g, new Vector3(-1.3f, 0f, 0f), 2 + rng.Next(3), rng);
                            }
                            else if (Free(at, yaw, new Vector3(0.6f, 0.75f, 0.3f)))
                                Industrial.ElectricalBox(c, f, at, yaw); // its warning plate (-z) faces the alley
                        }
                }
            }
            litter.Build(root, "Alley litter", c.P.Lit(new Color(0.58f, 0.56f, 0.5f), 0.05f), collider: false);
        }

        /// <summary>The paved (or bare) ground under a point: roads and terrain only.</summary>
        private static bool Ground(Vector2 p, out float y)
        {
            y = 0f;
            if (!Physics.Raycast(new Vector3(p.x, 80f, p.y), Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (!(hit.collider is TerrainCollider) && !(hit.collider is MeshCollider)) return false;
            y = hit.point.y;
            return true;
        }
    }
}
