using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Getting up high (TOWN_SPEC A16): service ladders on the building backs along the alleys, found by probing for a
    /// plain wall under a flat roof; and the Lantern Parkade on Canal Row, three decks and a roof you can drive or walk
    /// up for the view over the canal.
    /// </summary>
    public static class Rooftops
    {
        /// <summary>The parkade's footprint (south side on Neon Row, entrance off it).</summary>
        public static readonly Rect Parkade = new Rect(380f, 59f, 30f, 42f);

        /// <summary>Deck heights above the ground floor; ramps climb one storey over <see cref="RampRun"/> metres.</summary>
        public static readonly float[] Decks = { 0f, 3.2f, 6.4f, 9.6f };
        private const float RampFrom = 8f, RampRun = 26f, RampX = 24f, Slab = 0.3f;

        public static void AddPads(CityContext c) =>
            c.Pads.Add(Pad.FromRect(new Rect(Parkade.x - 1f, Parkade.y - 1f, Parkade.width + 2f, Parkade.height + 2f), StreetMap.Plan.StreetGrade(Parkade.center)));

        public static void Build(CityContext c) => BuildParkade(c, Kit.Group(c.Static, "Parkade"));

        /// <summary>After the street pass: the probes need the ground and the alley paving in place.</summary>
        public static void Ladders(CityContext c)
        {
            Physics.SyncTransforms();
            AlleyLadders(c, Kit.Group(c.Static, "Alley ladders"));
        }

        // ---- ladders ----

        /// <summary>
        /// Walks the alleys looking for a wall square to the lane that runs unbroken from the ground to a flat roof
        /// 3.5–14 m up with room to stand; puts a ladder on the best ones, well apart.
        /// </summary>
        private static void AlleyLadders(CityContext c, Transform root)
        {
            const int most = 10;
            const float apart = 35f;
            float half = CityPlan.HalfWidth(RoadClass.Alley);
            var placed = new List<Vector3>();
            foreach (StreetDef d in c.Roads.Map.Driveways)
            {
                if (d.Class != RoadClass.Alley) continue;
                for (int i = 0; i + 1 < d.Points.Length; i++)
                {
                    Vector2 a = d.Points[i], b = d.Points[i + 1];
                    float len = Vector2.Distance(a, b);
                    Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                    for (float t = 12f; t < len - 12f; t += 9f)
                        foreach (float side in new[] { 1f, -1f })
                        {
                            if (placed.Count >= most) return;
                            Vector2 mid = a + dir * t;
                            if (placed.Exists(p => new Vector2(p.x - mid.x, p.z - mid.y).magnitude < apart)) continue;
                            if (!Ground(mid, out float ground)) continue;
                            var outDir = new Vector3(left.x * side, 0f, left.y * side);
                            if (!Wall(new Vector3(mid.x, ground, mid.y), outDir, out Vector3 wall, out float roof)) continue;
                            if (Vector3.Distance(new Vector3(mid.x, ground, mid.y), new Vector3(wall.x, ground, wall.z)) > half + 4f) continue;
                            placed.Add(Place(c, root, wall, outDir, ground, roof).Foot);
                        }
                }
            }
        }

        /// <summary>
        /// A wall facing back down <paramref name="outDir"/> within 8 m, solid at every height up to a flat, clear roof.
        /// <paramref name="wall"/> is its foot; <paramref name="roof"/> the height of the roof surface.
        /// </summary>
        private static bool Wall(Vector3 at, Vector3 outDir, out Vector3 wall, out float roof)
        {
            wall = default;
            roof = 0f;
            if (!Physics.Raycast(at + Vector3.up * 1.2f, outDir, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider is TerrainCollider || Vector3.Dot(hit.normal, -outDir) < 0.97f) return false;
            float distance = hit.distance;
            wall = new Vector3(hit.point.x, at.y, hit.point.z);
            // Roof: flat and level a metre and a half in, and over a 3 m square.
            Vector3 inside = wall + outDir * 1.5f;
            if (!Physics.Raycast(new Vector3(inside.x, at.y + 40f, inside.z), Vector3.down, out RaycastHit top, 60f, ~0, QueryTriggerInteraction.Ignore)) return false;
            roof = top.point.y;
            float rise = roof - at.y;
            if (top.normal.y < 0.95f || rise < 3.5f || rise > 14f) return false;
            Vector3 along = Vector3.Cross(Vector3.up, outDir);
            foreach (Vector3 probe in new[] { inside + outDir * 1.5f + along * 1.2f, inside + outDir * 1.5f - along * 1.2f, inside + along * 1.2f, inside - along * 1.2f })
            {
                if (!Physics.Raycast(new Vector3(probe.x, roof + 3f, probe.z), Vector3.down, out RaycastHit p, 6f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(p.point.y - roof) > 0.25f) return false;
            }
            // Headroom where the player lands.
            if (Physics.CheckCapsule(new Vector3(inside.x, roof + 0.5f, inside.z), new Vector3(inside.x, roof + 1.7f, inside.z), 0.4f, ~0, QueryTriggerInteraction.Ignore)) return false;
            // No windows, doors or overhangs up the line of the ladder: the same face at every height.
            for (float y = 0.5f; y < rise - 0.4f; y += 1.1f)
            {
                if (!Physics.Raycast(at + Vector3.up * y, outDir, out RaycastHit h, 8f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(h.distance - distance) > 0.15f) return false;
            }
            return true;
        }

        /// <summary>Rails and rungs against the wall, a solid box you can aim at, and the <see cref="Ladder"/> that climbs it.</summary>
        private static Ladder Place(CityContext c, Transform root, Vector3 wall, Vector3 outDir, float ground, float roof)
        {
            Kit k = c.Kit;
            float yaw = Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg;
            Transform g = Kit.Group(root, "Ladder", wall - outDir * 0.2f, yaw);
            Material steel = c.P.Lit(new Color(0.3f, 0.31f, 0.32f), 0.45f);
            float h = roof - ground + 1f; // rails stand a metre over the roof edge, to grab
            foreach (float x in new[] { -0.24f, 0.24f })
                k.Box(g, "Rail", new Vector3(x, h / 2f, 0f), new Vector3(0.05f, h, 0.05f), steel, collider: false);
            for (float y = 0.3f; y < h - 0.8f; y += 0.3f)
                k.Box(g, "Rung", new Vector3(0f, y, 0f), new Vector3(0.48f, 0.03f, 0.03f), steel, collider: false);
            var box = g.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, h / 2f, -0.05f);
            box.size = new Vector3(0.6f, h, 0.2f);
            // Ladders live under the static root with their rails; the component only moves the player.
            var ladder = g.gameObject.AddComponent<Ladder>();
            ladder.Configure(c.Player, wall - outDir * 0.9f, new Vector3(wall.x, roof, wall.z) + outDir * 1.5f, wall - outDir * 0.45f, yaw);
            return ladder;
        }

        /// <summary>
        /// The roof ladder at the top of a fire escape, from its top landing (local height <paramref name="landing"/>) to
        /// the roof. The escape's +z points into the wall; the wall face is its z = 0.
        /// </summary>
        public static void FireEscapeLadder(CityContext c, Transform escape, float landing, float roof)
        {
            Vector3 wall = escape.TransformPoint(new Vector3(2.6f, landing, 0f));
            Place(c, Kit.Group(c.Static, "Fire escape ladder"), wall, escape.forward, wall.y, escape.TransformPoint(new Vector3(0f, roof, 0f)).y);
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

        // ---- the parkade ----

        /// <summary>
        /// Open decks round a stacked ramp: every ramp runs north up the east strip, so each one sits a storey above the
        /// last with full headroom; you come off at the north end, drive back south down the aisles and turn onto the
        /// next. Barriers keep cars off the ramp void; parapets round every deck.
        /// </summary>
        private static void BuildParkade(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Rect r = Parkade;
            float y0 = StreetMap.Plan.StreetGrade(r.center);
            Transform g = Kit.Group(root, "Lantern Parkade", new Vector3(r.xMin, y0, r.yMin));
            Material concrete = c.P.Lit(new Color(0.62f, 0.61f, 0.58f), 0.08f);
            Material deck = c.P.Lit(new Color(0.46f, 0.46f, 0.45f), 0.1f);
            Material paint = c.P.Lit(new Color(0.9f, 0.9f, 0.88f), 0.1f);
            Material stripe = c.P.Lit(new Color(0.95f, 0.75f, 0.15f), 0.2f);
            Material light = c.P.Lamp(new Color(0.7f, 0.7f, 0.68f), new Color(1f, 0.95f, 0.85f));
            float w = r.width, l = r.height, top = RampFrom + RampRun;

            for (int level = 0; level < Decks.Length; level++)
            {
                float y = Decks[level];
                if (level == 0) k.Span(g, "Ground floor", new Vector3(0f, -0.3f, 0f), new Vector3(w, 0.02f, l), deck).AddComponent<SurfaceTag>().Roughness = 0.2f;
                else
                {
                    // The deck, less the void over the ramp strip between the ramp ends.
                    k.Span(g, "Deck", new Vector3(0f, y - Slab, 0f), new Vector3(RampX, y, l), deck).AddComponent<SurfaceTag>().Roughness = 0.2f;
                    k.Span(g, "Deck south", new Vector3(RampX, y - Slab, 0f), new Vector3(w, y, RampFrom), deck).AddComponent<SurfaceTag>().Roughness = 0.2f;
                    k.Span(g, "Deck north", new Vector3(RampX, y - Slab, top), new Vector3(w, y, l), deck).AddComponent<SurfaceTag>().Roughness = 0.2f;
                    // Parapets round the edge.
                    k.Span(g, "Parapet W", new Vector3(-0.2f, y, 0f), new Vector3(0f, y + 1.05f, l), concrete);
                    k.Span(g, "Parapet E", new Vector3(w, y, 0f), new Vector3(w + 0.2f, y + 1.05f, l), concrete);
                    k.Span(g, "Parapet S", new Vector3(-0.2f, y, -0.2f), new Vector3(w + 0.2f, y + 1.05f, 0f), concrete);
                    k.Span(g, "Parapet N", new Vector3(-0.2f, y, l), new Vector3(w + 0.2f, y + 1.05f, l + 0.2f), concrete);
                }
                // Barrier along the ramp void (the ground floor too: nothing drives under the first ramp).
                k.Span(g, "Ramp barrier", new Vector3(RampX - 0.25f, y, RampFrom + (level == 0 ? 3f : 0f)), new Vector3(RampX, y + 1f, top), stripe);

                if (level + 1 < Decks.Length)
                {
                    // The ramp up to the next deck, and the pillars carrying it.
                    float rise = Decks[level + 1] - y;
                    GameObject ramp = k.Box(g, "Ramp", new Vector3(RampX + (w - RampX) / 2f, y + rise / 2f - Slab / 2f, RampFrom + RampRun / 2f),
                        new Vector3(w - RampX, Slab, Mathf.Sqrt(RampRun * RampRun + rise * rise)), deck);
                    ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, RampRun) * Mathf.Rad2Deg, 0f, 0f);
                    ramp.AddComponent<SurfaceTag>().Roughness = 0.25f;
                    float headroom = Decks[level + 1] - Slab - y;
                    foreach (float x in new[] { 0.3f, 11.5f, RampX - 0.5f, w - 0.3f })
                        foreach (float z in new[] { 0.3f, 10.5f, 21f, 31.5f, l - 0.3f })
                        {
                            if (x > RampX && z > RampFrom && z < top) continue; // not through the ramps
                            k.Box(g, "Pillar", new Vector3(x, y + headroom / 2f, z), new Vector3(0.5f, headroom, 0.5f), concrete);
                        }
                    // Ceiling lights over the aisles.
                    foreach (float z in new[] { 6f, 18f, 30f })
                        foreach (float x in new[] { 8.5f, 19.5f })
                            k.Box(g, "Light", new Vector3(x, Decks[level + 1] - Slab - 0.05f, z), new Vector3(1.2f, 0.08f, 0.2f), light, collider: false);
                }

                // Bays in two rows, ends clear for turning: west wall (facing it) and the middle row (facing west).
                for (float z = 9.5f; z + 2.7f < top - 1f; z += 2.8f)
                {
                    k.Decal(g, "Bay line", new Vector3(2.7f, y + 0.015f, z), new Vector2(5f, 0.1f), 0f, paint);
                    k.Decal(g, "Bay line", new Vector3(14f, y + 0.015f, z), new Vector2(5f, 0.1f), 0f, paint);
                    Vector3 west = g.TransformPoint(new Vector3(2.8f, y, z + 1.4f)), middle = g.TransformPoint(new Vector3(14f, y, z + 1.4f));
                    c.ParkingSpots.Add((west, 270f, ParkingKind.Lot));
                    c.ParkingSpots.Add((middle, 90f, ParkingKind.Lot));
                }
                if (level + 1 < Decks.Length)
                    k.Text(g, level == 0 ? "LEVEL G" : "LEVEL " + level, new Vector3(11.5f, y + 1.6f, 10.2f), 0f, 0.3f, new Color(0.95f, 0.75f, 0.15f));
            }

            // The street front: name over the entrance, the P sign, and a kerb ramp off Neon Row.
            k.Text(g, "LANTERN PARKADE", new Vector3(w / 2f, Decks[1] + 0.55f, -0.25f), 0f, 0.6f, new Color(0.95f, 0.95f, 0.9f));
            k.Box(g, "P sign", new Vector3(w - 1f, Decks[2] + 1.8f, -0.3f), new Vector3(1.4f, 1.4f, 0.1f), c.P.Sign(new Color(0.1f, 0.35f, 0.7f)), collider: false);
            k.Text(g, "P", new Vector3(w - 1f, Decks[2] + 1.8f, -0.36f), 0f, 0.9f, Color.white);
            const float run = 1.4f, kerbRise = -CityPlan.RoadY;
            float kerb = 50f + CityPlan.HalfWidth(RoadClass.Street) - r.yMin; // Neon Row's kerb, local z
            GameObject cut = k.Box(g, "Kerb ramp", new Vector3(w - 4f, CityPlan.RoadY + kerbRise / 2f - 0.03f, kerb - run / 2f),
                new Vector3(8f, 0.06f, Mathf.Sqrt(run * run + kerbRise * kerbRise)), concrete);
            cut.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(kerbRise, run) * Mathf.Rad2Deg, 0f, 0f);
            k.Span(g, "Driveway", new Vector3(w - 8f, -0.02f, kerb), new Vector3(w, 0.02f, 0f), concrete, collider: false);
            c.Place(g.TransformPoint(new Vector3(w / 2f, 0f, -1f)), PlaceKind.Door, "Lantern Parkade");
        }
    }
}
