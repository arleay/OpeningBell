using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The canal and the bay (TOWN_SPEC A8). Along the canal: a towpath at water level on the west bank, stairs
    /// down from Canal St, the way under every bridge (and a camp under Maple's), drain outlets, a footbridge, a
    /// tide gate at the bay and a weir at the river. On the bay: a boardwalk along the beach, the fishing pier,
    /// the marina with its boats and the fish market, quay walls along the working waterfront.
    /// </summary>
    public static class Waterfront
    {
        /// <summary>Stairs from Canal St's sidewalk down to the towpath (z along the west bank).</summary>
        public static readonly float[] CanalStairs = { -250f, -110f, 25f, 130f, 250f };
        public const float Towpath = -4f, TowFrom = 300f, TowTo = 304f;
        private const float StairRun = 7f;

        public static readonly Rect Boardwalk = Rect.MinMaxRect(-45f, -286f, 140f, -280f);
        public static readonly Rect Pier = Rect.MinMaxRect(77.5f, -365f, 82.5f, -280f);
        public static readonly Rect Marina = Rect.MinMaxRect(150f, -330f, 286f, -292f);
        public const float BoardY = 0.45f, PierY = 0.9f;

        public const float FootbridgeZ = 40f;

        /// <summary>Where the canal's parapet opens: stair tops on the west bank, the footbridge on both.</summary>
        public static bool ParapetGap(bool west, float z0, float z1)
        {
            if (z1 > FootbridgeZ - 2f && z0 < FootbridgeZ + 2f) return true;
            if (!west) return false;
            foreach (float z in CanalStairs)
                if (z1 > z - 1f && z0 < z + StairRun + 1f) return true;
            return false;
        }

        public static void AddPads(CityContext c)
        {
            c.Pads.Add(Pad.FromRect(Rect.MinMaxRect(160f, -292f, 250f, -278.5f), 0f)); // fish market and marina office on the quay
        }

        public static void Build(CityContext c)
        {
            Transform root = Kit.Group(c.Static, "Waterfront");
            var rng = new System.Random(3131);
            TowpathAndStairs(c, root, rng);
            Footbridge(c, root);
            Gates(c, root);
            BoardwalkAndPier(c, root);
            MarinaAndMarket(c, root, rng);
            QuayWalls(c, root);
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        private static void TowpathAndStairs(CityContext c, Transform root, System.Random rng)
        {
            Kit k = c.Kit;
            Material concrete = c.P.Lit(new Color(0.5f, 0.49f, 0.46f), 0.06f);
            Material pipe = c.P.Lit(new Color(0.25f, 0.24f, 0.23f), 0.3f);
            float z0 = TownTerrain.Canal.yMin + 22f, z1 = TownTerrain.CanalWallsNorth;
            Tag(k.Span(root, "Towpath", V(TowFrom, TownTerrain.CanalFloor, z0), V(TowTo, Towpath, z1), concrete), 0.25f);
            // Kerb along the water's edge.
            k.Span(root, "Towpath edge", V(TowTo - 0.25f, Towpath, z0), V(TowTo, Towpath + 0.25f, z1), concrete, collider: false);
            foreach (float z in CanalStairs)
            {
                float bank = TownTerrain.Natural(TowFrom - 1.5f, z + StairRun);
                float rise = bank - Towpath;
                int steps = Mathf.CeilToInt(rise / 0.2f);
                for (int i = 0; i < steps; i++)
                {
                    float h = rise * (i + 1) / steps;
                    k.Span(root, "Step", V(TowFrom, Towpath, z + StairRun * i / steps), V(TowFrom + 1.8f, Towpath + h, z + StairRun * (i + 1) / steps), concrete, collider: false);
                }
                GameObject ramp = k.Box(root, "Stair ramp", V(TowFrom + 0.9f, Towpath + rise / 2f, z + StairRun / 2f), V(1.8f, 0.05f, Mathf.Sqrt(StairRun * StairRun + rise * rise)), concrete);
                ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, StairRun) * Mathf.Rad2Deg, 0f, 0f);
                ramp.GetComponent<Renderer>().enabled = false;
            }
            // Walkers: between each pair of stairs, down, along, and up.
            for (int i = 0; i + 1 < CanalStairs.Length; i++)
            {
                float a = CanalStairs[i], b = CanalStairs[i + 1];
                float ba = TownTerrain.Natural(TowFrom - 1.5f, a + StairRun), bb = TownTerrain.Natural(TowFrom - 1.5f, b + StairRun);
                c.WalkPaths.Add(new[]
                {
                    V(TowFrom - 2f, ba, a + StairRun + 0.5f), V(TowFrom + 0.9f, ba, a + StairRun), V(TowFrom + 0.9f, Towpath, a - 0.5f),
                    V(TowFrom + 2.6f, Towpath, a - 1f), V(TowFrom + 2.6f, Towpath, b - 1f), V(TowFrom + 0.9f, Towpath, b - 0.5f),
                    V(TowFrom + 0.9f, bb, b + StairRun), V(TowFrom - 2f, bb, b + StairRun + 0.5f),
                });
            }
            // Drain outlets in both walls, some trickling.
            for (float z = z0 + 20f; z < z1 - 10f; z += 55f)
                foreach (float x in new[] { TownTerrain.Canal.xMin, TownTerrain.Canal.xMax })
                {
                    GameObject outlet = k.Cylinder(root, "Drain outlet", V(x + (x < 310f ? 0.3f : -0.3f), Towpath + 0.9f + (float)rng.NextDouble() * 0.5f, z), 1.2f, 0.8f, pipe);
                    outlet.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            // Under Maple's bridge: a camp, tagged walls, a cart.
            Transform camp = Kit.Group(root, "Bridge camp", V(TowFrom + 1.8f, Towpath, -16f));
            k.Box(camp, "Tent", V(0f, 0.55f, 0f), V(1.8f, 1.1f, 2.2f), c.P.Lit(new Color(0.2f, 0.35f, 0.25f)));
            k.Box(camp, "Tarp", V(0f, 1.3f, 2.4f), V(2.4f, 0.04f, 2f), c.P.Lit(new Color(0.15f, 0.3f, 0.55f)), collider: false);
            k.Box(camp, "Crate", V(0.4f, 0.3f, 3.6f), V(0.6f, 0.6f, 0.6f), c.P.Lit(new Color(0.55f, 0.42f, 0.28f)));
            k.Box(camp, "Cart", V(-0.6f, 0.5f, 5f), V(0.6f, 0.9f, 0.9f), c.P.Lit(new Color(0.6f, 0.62f, 0.64f), 0.6f));
            string[] tags = { "CANAL KINGS", "NO HOME", "PK 4EVER" };
            for (int i = 0; i < tags.Length; i++)
                k.Text(root, tags[i], V(TowFrom + 0.05f, Towpath + 1.3f + i * 0.3f, -24f + i * 8f), 90f, 0.35f, i % 2 == 0 ? new Color(0.9f, 0.3f, 0.5f) : new Color(0.3f, 0.85f, 0.9f));
            GameObject cart = k.Box(root, "Shopping cart", V(318f, TownTerrain.CanalWater - 0.2f, 85f), V(0.6f, 0.9f, 0.9f), c.P.Lit(new Color(0.6f, 0.62f, 0.64f), 0.6f), collider: false);
            cart.transform.localRotation = Quaternion.Euler(20f, 30f, 60f);
            c.Anchor("towpath_under_maple", V(TowFrom + 2.6f, Towpath, -8f));
        }

        /// <summary>A footbridge across the canal at z 40, arched over the towpath.</summary>
        private static void Footbridge(CityContext c, Transform root)
        {
            Material deck = c.P.Lit(new Color(0.45f, 0.35f, 0.25f), 0.05f);
            Material rail = c.P.Lit(new Color(0.2f, 0.22f, 0.24f), 0.4f);
            const float z = FootbridgeZ, half = 1.6f, x0 = 297f, x1 = 336f;
            float y0 = 0f, y1 = 1.5f;
            var mb = new MeshBuilder();
            var railM = new MeshBuilder();
            const int n = 12;
            var path = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                float xa = Mathf.Lerp(x0, x1, t0), xb = Mathf.Lerp(x0, x1, t1);
                float ya = Mathf.Lerp(y0, y1, t0) + 1.6f * Mathf.Sin(t0 * Mathf.PI), yb = Mathf.Lerp(y0, y1, t1) + 1.6f * Mathf.Sin(t1 * Mathf.PI);
                mb.Quad(V(xa, ya, z - half), V(xb, yb, z - half), V(xb, yb, z + half), V(xa, ya, z + half));
                mb.Quad(V(xa, ya - 0.4f, z + half), V(xb, yb - 0.4f, z + half), V(xb, yb - 0.4f, z - half), V(xa, ya - 0.4f, z - half));
                foreach (float s in new[] { -half, half })
                {
                    railM.Quad(V(xa, ya, z + s), V(xb, yb, z + s), V(xb, yb + 1.05f, z + s), V(xa, ya + 1.05f, z + s));
                    railM.Quad(V(xb, yb, z + s), V(xa, ya, z + s), V(xa, ya + 1.05f, z + s), V(xb, yb + 1.05f, z + s));
                }
                path.Add(V(xa, ya, z));
            }
            path.Add(V(x1, y1, z));
            mb.Build(root, "Footbridge", deck, collider: true, roughness: 0.3f);
            railM.Build(root, "Footbridge railings", rail, collider: true);
            c.WalkPaths.Add(new[] { V(x0 - 1.5f, y0, z), path[3], path[6], path[9], V(x1 + 1.5f, y1, z) });
        }

        /// <summary>The tide gate at the canal's mouth and the weir where it leaves the river (white water over the lip).</summary>
        private static void Gates(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Material concrete = c.P.Lit(new Color(0.5f, 0.49f, 0.46f), 0.06f);
            Material steel = c.P.Lit(new Color(0.3f, 0.33f, 0.35f), 0.5f);
            Rect cr = TownTerrain.Canal;
            float gz = cr.yMin + 20f;
            k.Span(root, "Tide gate", V(cr.xMin, TownTerrain.CanalFloor, gz - 1f), V(cr.xMax, TownTerrain.SeaLevel + 0.6f, gz + 1f), concrete);
            k.Span(root, "Gate leaves", V(cr.xMin + 1f, TownTerrain.CanalFloor, gz - 1.2f), V(cr.xMax - 1f, TownTerrain.SeaLevel + 0.3f, gz - 1f), steel, collider: false);
            k.Span(root, "Gate walkway", V(cr.xMin, TownTerrain.SeaLevel + 0.6f, gz - 1f), V(cr.xMax, TownTerrain.SeaLevel + 0.8f, gz + 1f), steel);
            float wz = cr.yMax - 16f;
            k.Span(root, "Weir", V(cr.xMin - 6f, TownTerrain.CanalFloor, wz - 1.5f), V(cr.xMax + 6f, TownTerrain.RiverWater + 0.05f, wz + 1.5f), concrete);
            k.Span(root, "Weir foam", V(cr.xMin - 6f, TownTerrain.CanalWater + 0.02f, wz - 4f), V(cr.xMax + 6f, TownTerrain.CanalWater + 0.06f, wz - 1.5f), c.P.Lit(new Color(0.9f, 0.93f, 0.95f), 0.3f), collider: false);
        }

        private static void BoardwalkAndPier(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Material wood = c.P.Lit(new Color(0.5f, 0.38f, 0.26f), 0.05f);
            Material dark = c.P.Lit(new Color(0.3f, 0.22f, 0.16f), 0.05f);
            Material rail = c.P.Lit(new Color(0.85f, 0.85f, 0.82f), 0.2f);
            Material pole = c.P.Lit(new Color(0.26f, 0.27f, 0.28f), 0.35f);
            Material lamp = c.P.Lamp(new Color(0.55f, 0.55f, 0.5f), new Color(1f, 0.86f, 0.6f));
            Rect b = Boardwalk;
            Tag(k.Span(root, "Boardwalk", V(b.xMin, BoardY - 0.12f, b.yMin), V(b.xMax, BoardY, b.yMax), wood), 0.3f);
            for (float x = b.xMin; x < b.xMax; x += 4f)
                k.Box(root, "Pile", V(x, BoardY - 2.5f, b.yMin + 0.3f), V(0.3f, 5f, 0.3f), dark, collider: false);
            k.Span(root, "Boardwalk rail", V(b.xMin, BoardY, b.yMin), V(Pier.xMin, BoardY + 1f, b.yMin + 0.08f), rail);
            k.Span(root, "Boardwalk rail", V(Pier.xMax, BoardY, b.yMin), V(b.xMax, BoardY + 1f, b.yMin + 0.08f), rail);
            for (float x = b.xMin + 10f; x < b.xMax; x += 30f)
            {
                Transform bench = Kit.Group(root, "Bench", V(x, BoardY, b.yMin + 1.2f), 180f);
                k.Box(bench, "Seat", V(0f, 0.45f, 0f), V(1.8f, 0.07f, 0.5f), wood);
                k.Box(bench, "Back", V(0f, 0.75f, -0.22f), V(1.8f, 0.4f, 0.06f), wood, collider: false);
                c.Place(V(x, BoardY, b.yMin + 1.2f), PlaceKind.Bench, "bench");
                LampPost(c, root, V(x + 15f, BoardY, b.yMin + 0.6f), pole, lamp);
            }
            // Steps from the Harbor Rd sidewalk down onto the boardwalk.
            c.WalkPaths.Add(new[] { V(b.xMin + 2f, 0f, -278f), V(b.xMin + 2f, BoardY, b.center.y), V(Pier.center.x, BoardY, b.center.y), V(b.xMax - 2f, BoardY, b.center.y), V(b.xMax - 2f, 0f, -278f) });

            // The fishing pier.
            Rect p = Pier;
            Tag(k.Span(root, "Pier", V(p.xMin, PierY - 0.15f, p.yMin), V(p.xMax, PierY, b.yMin), wood), 0.3f);
            GameObject ramp = k.Box(root, "Pier ramp", V(p.center.x, (BoardY + PierY) / 2f - 0.07f, b.yMin - 1.5f), V(p.width, 0.1f, 3.05f), wood);
            ramp.transform.localRotation = Quaternion.Euler(Mathf.Atan2(PierY - BoardY, 3f) * Mathf.Rad2Deg, 0f, 0f);
            for (float z = p.yMin; z < b.yMin; z += 5f)
                foreach (float x in new[] { p.xMin + 0.3f, p.xMax - 0.3f })
                    k.Box(root, "Pile", V(x, PierY - 3.5f, z), V(0.35f, 7f, 0.35f), dark, collider: false);
            foreach (float x in new[] { p.xMin, p.xMax - 0.08f })
                k.Span(root, "Pier rail", V(x, PierY, p.yMin), V(x + 0.08f, PierY + 1f, b.yMin), rail);
            k.Span(root, "Pier end rail", V(p.xMin, PierY, p.yMin), V(p.xMax, PierY + 1f, p.yMin + 0.08f), rail);
            for (float z = p.yMin + 8f; z < b.yMin - 5f; z += 16f)
            {
                LampPost(c, root, V(p.xMin + 0.3f, PierY, z), pole, lamp);
                // Somewhere to sit and fish.
                Transform bench = Kit.Group(root, "Pier bench", V(p.xMax - 0.8f, PierY, z + 4f), 90f);
                k.Box(bench, "Seat", V(0f, 0.45f, 0f), V(1.6f, 0.07f, 0.45f), wood);
                c.Place(V(p.xMax - 0.8f, PierY, z + 4f), PlaceKind.Bench, "pier");
            }
            k.Box(root, "Bait shack", V(p.xMin - 2.5f, BoardY + 1.3f, b.yMin + 3f), V(3f, 2.6f, 2.6f), c.P.Lit(new Color(0.3f, 0.45f, 0.5f)));
            k.Text(root, "BAIT & TACKLE", V(p.xMin - 2.5f, BoardY + 2.3f, b.yMin + 1.65f), 180f, 0.14f, Color.white);
            c.WalkPaths.Add(new[] { V(p.center.x, BoardY, b.center.y), V(p.center.x, PierY, b.yMin - 3f), V(p.center.x, PierY, p.yMin + 3f) });
            c.Anchor("pier_end", V(p.center.x, PierY, p.yMin + 4f));
        }

        private static void LampPost(CityContext c, Transform parent, Vector3 at, Material pole, Material lamp)
        {
            c.Kit.Cylinder(parent, "Lamp post", at + V(0f, 1.8f, 0f), 0.12f, 3.6f, pole, collider: true);
            c.Kit.Sphere(parent, "Globe", at + V(0f, 3.75f, 0f), 0.45f, lamp);
            Light l = c.PointLight(parent, at + V(0f, 3.7f, 0f), 10f, 1.2f, new Color(1f, 0.85f, 0.6f));
            l.enabled = false;
            c.NightLights.Add(l);
        }

        private static void MarinaAndMarket(CityContext c, Transform root, System.Random rng)
        {
            Kit k = c.Kit;
            Rect m = Marina;
            Material dock = c.P.Lit(new Color(0.55f, 0.47f, 0.36f), 0.05f);
            float y = TownTerrain.SeaLevel + 0.45f;
            // A main float along the quay, finger piers out into the bay, boats between them.
            Tag(k.Span(root, "Marina float", V(m.xMin, y - 0.3f, m.yMax - 3f), V(m.xMax, y, m.yMax), dock), 0.3f);
            Transform boats = Kit.Group(c.Dynamic, "Boats"); // they bob, so not static
            GameObject gangway = k.Box(root, "Gangway", V(m.xMin + 20f, (y + 0f) / 2f, m.yMax + 1.5f), V(1.4f, 0.1f, 3.6f), dock);
            gangway.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(0f - y, 3f) * Mathf.Rad2Deg, 0f, 0f);
            for (float x = m.xMin + 8f; x < m.xMax - 4f; x += 12f)
            {
                k.Span(root, "Finger pier", V(x - 0.7f, y - 0.3f, m.yMin + 6f), V(x + 0.7f, y, m.yMax - 3f), dock);
                if (rng.NextDouble() < 0.75) Boat(c, boats, V(x + 5f, TownTerrain.SeaLevel, m.yMin + 14f + (float)rng.NextDouble() * 6f), 180f, rng);
            }
            // Boats out in the bay, at anchor.
            for (int i = 0; i < 5; i++)
                Boat(c, boats,V(-150f + i * 110f + (float)rng.NextDouble() * 40f, TownTerrain.SeaLevel, -360f - (float)rng.NextDouble() * 50f), (float)rng.NextDouble() * 360f, rng);
            // The fish market and the marina office on the quay.
            Businesses.Build(c, new Business
            {
                Name = "Kell Fish Market", Trade = Trade.FishMarket, Front = new Vector2(200f, -278.5f), Inward = Vector2.down, Width = 22f, Depth = 12f,
                Brand = new Color(0.12f, 0.3f, 0.5f), Tagline = "OFF THE BOATS DAILY", Style = FacadeStyle.Stucco,
            }, 9401);
            k.Span(root, "Marina office", V(168f, 0f, -291f), V(180f, 3.2f, -283f), c.P.Lit(new Color(0.85f, 0.85f, 0.82f)));
            k.Text(root, "PORT KELL MARINA", V(174f, 3.6f, -282.9f), 0f, 0.3f, new Color(0.12f, 0.3f, 0.5f));
            c.WalkPaths.Add(new[] { V(m.xMin + 20f, 0f, -279f), V(m.xMin + 20f, y, m.yMax - 1.5f), V(m.xMax - 10f, y, m.yMax - 1.5f) });
        }

        /// <summary>A small boat: hull, deck, cabin, a mast on some.</summary>
        private static void Boat(CityContext c, Transform parent, Vector3 at, float yaw, System.Random rng)
        {
            Kit k = c.Kit;
            Transform b = Kit.Group(parent, "Boat", at, yaw);
            float len = 6f + (float)rng.NextDouble() * 5f;
            Color[] hulls = { new Color(0.9f, 0.9f, 0.88f), new Color(0.15f, 0.25f, 0.45f), new Color(0.6f, 0.15f, 0.12f), new Color(0.2f, 0.4f, 0.3f) };
            k.Box(b, "Hull", V(0f, 0.2f, 0f), V(2.4f, 1.1f, len), c.P.Lit(hulls[rng.Next(hulls.Length)], 0.3f));
            GameObject bow = k.Box(b, "Bow", V(0f, 0.2f, len / 2f + 0.6f), V(1.7f, 1.1f, 1.7f), c.P.Lit(hulls[rng.Next(hulls.Length)], 0.3f));
            bow.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            k.Box(b, "Cabin", V(0f, 1.3f, -len * 0.1f), V(1.9f, 1.2f, len * 0.35f), c.P.Lit(new Color(0.92f, 0.92f, 0.9f), 0.3f));
            if (rng.NextDouble() < 0.4) k.Cylinder(b, "Mast", V(0f, 4f, len * 0.15f), 0.12f, 6f, c.P.Lit(new Color(0.85f, 0.85f, 0.85f), 0.3f));
            b.gameObject.AddComponent<Bob>().Configure((float)rng.NextDouble() * 6f);
        }

        /// <summary>Quay walls where the working waterfront meets the bay (the terminal, the marina).</summary>
        private static void QuayWalls(CityContext c, Transform root)
        {
            Material concrete = c.P.Lit(new Color(0.46f, 0.45f, 0.43f), 0.05f);
            var mb = new MeshBuilder();
            foreach (var (x0, x1, z) in new[] { (Industrial.Terminal.xMin, Industrial.Terminal.xMax, Industrial.Terminal.yMin), (Marina.xMin - 10f, Marina.xMax, Marina.yMax) })
            {
                mb.Quad(V(x0, -7f, z), V(x1, -7f, z), V(x1, 0.2f, z), V(x0, 0.2f, z));
                mb.Quad(V(x0, 0.2f, z), V(x1, 0.2f, z), V(x1, 0.2f, z + 1f), V(x0, 0.2f, z + 1f));
            }
            mb.Build(root, "Quay walls", concrete, collider: true);
            foreach (var (x0, x1, z) in new[] { (Industrial.Terminal.xMin, Industrial.Terminal.xMax, Industrial.Terminal.yMin) })
                for (float x = x0 + 5f; x < x1; x += 12f)
                    c.Kit.Cylinder(root, "Bollard", V(x, 0.5f, z + 0.5f), 0.4f, 0.6f, c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.3f), collider: true);
        }

        private static void Tag(GameObject go, float roughness) => go.AddComponent<SurfaceTag>().Roughness = roughness;
    }

    /// <summary>Boats rock a little on the water.</summary>
    public sealed class Bob : MonoBehaviour
    {
        private float _phase;
        private Vector3 _base;
        private Quaternion _rot;

        public void Configure(float phase)
        {
            _phase = phase;
            _base = transform.localPosition;
            _rot = transform.localRotation;
        }

        private void Update()
        {
            float t = Time.time * 0.8f + _phase;
            transform.localPosition = _base + Vector3.up * Mathf.Sin(t) * 0.12f;
            transform.localRotation = _rot * Quaternion.Euler(Mathf.Sin(t * 0.7f) * 2f, 0f, Mathf.Sin(t) * 3f);
        }
    }
}
