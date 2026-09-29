using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Things you can find your way by (TOWN_SPEC A4): the elevated Northline railway with Maple Station and its
    /// trains, the tunnels at the town's edges, City Hall's clock tower, Harborview Tower, the water tower on the
    /// Crest, the old paper mill's smokestack, the radio mast on the overlook, the casino's sign and the highway
    /// billboard. Pads go in first (<see cref="AddPads"/>) so the terrain is levelled under them.
    /// </summary>
    public static class Landmarks
    {
        public static readonly Rect CityHallBlock = Rect.MinMaxRect(223.5f, -5.5f, 281.5f, 61.5f);
        public static readonly Rect HarborviewLot = Rect.MinMaxRect(228f, 88f, 276f, 136f);
        public static readonly Rect PaperMill = Rect.MinMaxRect(-458f, -152f, -392f, -110f);
        public static readonly Rect CasinoLot = Rect.MinMaxRect(455.5f, -5.5f, 554.5f, 41.5f);
        public static readonly Vector2 WaterTower = new Vector2(705f, 318f);
        public static readonly Vector2 RadioMast = new Vector2(-410f, 415f);

        public static void AddPads(CityContext c)
        {
            c.Pads.Add(Pad.FromRect(CityHallBlock, 0f));
            c.Pads.Add(Pad.FromRect(HarborviewLot, StreetMap.Plan.StreetGrade(HarborviewLot.center)));
            c.Pads.Add(Pad.FromRect(PaperMill, 0f));
            c.Pads.Add(Pad.FromRect(CasinoLot, 1.5f));
            c.Pads.Add(new Pad(WaterTower, new Vector2(9f, 9f), 0f, TownTerrain.Natural(WaterTower.x, WaterTower.y)));
            c.Pads.Add(new Pad(RadioMast, new Vector2(8f, 8f), 0f, TownTerrain.Natural(RadioMast.x, RadioMast.y)));
        }

        public static void Build(CityContext c)
        {
            Transform root = Kit.Group(c.Static, "Landmarks");
            Railway(c, root);
            Tunnels(c, root);
            CityHall(c, root);
            Harborview(c, root);
            WaterTowerOnCrest(c, root);
            Smokestack(c, root);
            Mast(c, root);
            Casino(c, root);
            Billboard(c, root);
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // ---- the railway ----

        private const float DeckHalf = 4.5f, DeckDepth = 1.3f;
        private const float PlatformHalf = 9f; // station deck half width (tracks plus platforms)

        private static void Railway(CityContext c, Transform root)
        {
            Transform rail = Kit.Group(root, "Northline");
            var concrete = new MeshBuilder();
            var ballast = new MeshBuilder();
            var steel = new MeshBuilder();
            float z = CityPlan.RailZ;
            const float step = 10f;
            // Start on a multiple of the step from the station, so the wide station deck lines up exactly.
            for (float x = CityPlan.RailWest - 45f; x < CityPlan.RailEast + 40f; x += step)
            {
                float x1 = x + step;
                bool station = x >= CityPlan.StationWest && x1 <= CityPlan.StationEast;
                float half = station ? PlatformHalf : DeckHalf;
                float y0 = CityPlan.RailDeck(x), y1 = CityPlan.RailDeck(x1);
                // Deck: top, underside, sides.
                concrete.Quad(V(x, y0, z - half), V(x1, y1, z - half), V(x1, y1, z + half), V(x, y0, z + half));
                concrete.Quad(V(x, y0 - DeckDepth, z + half), V(x1, y1 - DeckDepth, z + half), V(x1, y1 - DeckDepth, z - half), V(x, y0 - DeckDepth, z - half));
                concrete.Quad(V(x, y0 - DeckDepth, z - half), V(x1, y1 - DeckDepth, z - half), V(x1, y1, z - half), V(x, y0, z - half));
                concrete.Quad(V(x1, y1 - DeckDepth, z + half), V(x, y0 - DeckDepth, z + half), V(x, y0, z + half), V(x1, y1, z + half));
                if (!station)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        // Parapet walls: 1 m high, 0.25 thick.
                        float o = z + side * (half - 0.125f);
                        concrete.Quad(V(x, y0, o - 0.125f), V(x1, y1, o - 0.125f), V(x1, y1 + 1f, o - 0.125f), V(x, y0 + 1f, o - 0.125f));
                        concrete.Quad(V(x1, y1, o + 0.125f), V(x, y0, o + 0.125f), V(x, y0 + 1f, o + 0.125f), V(x1, y1 + 1f, o + 0.125f));
                        concrete.Quad(V(x, y0 + 1f, o - 0.125f), V(x1, y1 + 1f, o - 0.125f), V(x1, y1 + 1f, o + 0.125f), V(x, y0 + 1f, o + 0.125f));
                    }
                foreach (float track in new[] { -2.25f, 2.25f })
                {
                    float tz = z + track;
                    ballast.Quad(V(x, y0 + 0.25f, tz - 1.4f), V(x1, y1 + 0.25f, tz - 1.4f), V(x1, y1 + 0.25f, tz + 1.4f), V(x, y0 + 0.25f, tz + 1.4f));
                    foreach (float r in new[] { -0.72f, 0.72f })
                        steel.Quad(V(x, y0 + 0.42f, tz + r - 0.04f), V(x1, y1 + 0.42f, tz + r - 0.04f), V(x1, y1 + 0.42f, tz + r + 0.04f), V(x, y0 + 0.42f, tz + r + 0.04f));
                }
            }
            concrete.Build(rail, "Viaduct deck", c.P.Surface(Finish.Concrete, new Color(0.52f, 0.51f, 0.48f), 0.05f), collider: true);
            ballast.Build(rail, "Ballast", c.P.Lit(new Color(0.3f, 0.28f, 0.26f), 0.02f), collider: false);
            steel.Build(rail, "Rails", c.P.Lit(new Color(0.42f, 0.4f, 0.38f), 0.6f), collider: false);

            Piers(c, rail);
            Station(c, rail);
            var train = new GameObject("Northline train").AddComponent<RailTrain>();
            train.transform.SetParent(c.Dynamic, false);
            train.Configure(c);
        }

        /// <summary>Hammerhead piers every 25 m, in the middle of Rail Row and on the lots either side; never on a crossing street.</summary>
        private static void Piers(CityContext c, Transform rail)
        {
            Material m = c.P.Lit(new Color(0.5f, 0.49f, 0.46f), 0.05f);
            StreetMap map = StreetMap.Plan;
            for (float x = CityPlan.RailWest + 20f; x < CityPlan.RailEast - 10f; x += 25f)
            {
                var p = new Vector2(x, CityPlan.RailZ);
                if (TownTerrain.InCanal(x, CityPlan.RailZ) || TownTerrain.InCanal(x - 6f, CityPlan.RailZ) || TownTerrain.InCanal(x + 6f, CityPlan.RailZ)) continue;
                if (x > CityPlan.StationWest - 5f && x < CityPlan.StationEast + 5f) continue; // the station has its own
                bool clear = true;
                foreach (StreetMap.Segment s in map.Segments)
                {
                    // Crossing streets (not Rail Row itself, whose median the piers stand in).
                    if (s.Street == "RAIL ROW") continue;
                    float t = Mathf.Clamp(Vector2.Dot(p - s.A.P, s.Dir), 0f, s.Length);
                    if (Vector2.Distance(p, s.At(t)) < s.HalfWidth + s.Sidewalk + 3f) clear = false;
                }
                foreach (StreetMap.Node n in map.Nodes)
                    if (n.IsJunction && Vector2.Distance(n.P, p) < 16f) clear = false;
                if (!clear) continue;
                float ground = TownTerrain.Natural(x, CityPlan.RailZ) - 1f, top = CityPlan.RailDeck(x) - DeckDepth;
                c.Kit.Box(rail, "Pier", V(x, (ground + top - 1f) / 2f, CityPlan.RailZ), V(1.3f, top - 1f - ground, 1.3f), m);
                c.Kit.Box(rail, "Pier cap", V(x, top - 0.6f, CityPlan.RailZ), V(1.8f, 1.2f, 8.4f), m);
            }
        }

        private static void Station(CityContext c, Transform rail)
        {
            Kit k = c.Kit;
            Transform st = Kit.Group(rail, "Maple Station");
            float x0 = CityPlan.StationWest, x1 = CityPlan.StationEast, z = CityPlan.RailZ, y = CityPlan.RailDeck(x0);
            Material platform = c.P.Lit(new Color(0.62f, 0.6f, 0.56f), 0.08f);
            Material edge = c.P.Lit(new Color(0.9f, 0.78f, 0.2f), 0.2f);
            Material steelM = c.P.Lit(new Color(0.24f, 0.3f, 0.36f), 0.4f);
            Material roof = c.P.Lit(new Color(0.18f, 0.32f, 0.42f), 0.3f);
            Material railing = c.P.Lit(new Color(0.2f, 0.2f, 0.22f), 0.5f);
            foreach (float side in new[] { -1f, 1f })
            {
                // Platform: from the track edge (4.6 m out) to the deck edge, 1 m above the deck.
                float inner = z + side * 4.6f, outer = z + side * PlatformHalf;
                k.Span(st, "Platform", V(x0, y, Mathf.Min(inner, outer)), V(x1, y + 1f, Mathf.Max(inner, outer)), platform);
                k.Span(st, "Edge line", V(x0, y + 1f, Mathf.Min(inner, inner + side * 0.4f)), V(x1, y + 1.01f, Mathf.Max(inner, inner + side * 0.4f)), edge, collider: false);
                // Railing on the outer edge, canopy on posts.
                k.Span(st, "Railing", V(x0, y + 1f, outer - side * 0.05f - 0.03f), V(x1, y + 2.1f, outer - side * 0.05f + 0.03f), railing);
                for (float px = x0 + 4f; px < x1; px += 10f)
                    k.Box(st, "Canopy post", V(px, y + 2.6f, z + side * 7.5f), V(0.2f, 3.2f, 0.2f), steelM);
                k.Span(st, "Canopy", V(x0 + 2f, y + 4.2f, Mathf.Min(z + side * 5f, z + side * 9.2f)), V(x1 - 2f, y + 4.4f, Mathf.Max(z + side * 5f, z + side * 9.2f)), roof, collider: false);
                for (int b = 0; b < 3; b++)
                {
                    Transform bench = Kit.Group(st, "Bench", V(x0 + 15f + b * 15f, y + 1f, z + side * 8f), side > 0f ? 180f : 0f);
                    k.Box(bench, "Seat", V(0f, 0.45f, 0f), V(1.8f, 0.07f, 0.5f), c.P.Lit(new Color(0.45f, 0.32f, 0.2f)));
                    k.Box(bench, "Back", V(0f, 0.75f, 0.22f), V(1.8f, 0.4f, 0.06f), c.P.Lit(new Color(0.45f, 0.32f, 0.2f)), collider: false);
                }
                // Name boards under the canopy, facing the tracks.
                foreach (float bx in new[] { x0 + 12f, x1 - 12f })
                {
                    Transform board = Kit.Group(st, "Station sign", V(bx, y + 3.4f, z + side * 5.2f), side > 0f ? 0f : 180f);
                    k.Box(board, "Board", Vector3.zero, V(3.6f, 0.6f, 0.08f), c.P.Lit(new Color(0.12f, 0.3f, 0.45f), 0.3f), collider: false);
                    k.Text(board, "MAPLE STATION", V(0f, 0f, -0.05f), 0f, 0.22f, Color.white);
                }
                Stairs(c, st, side, y + 1f);
            }
            // Station piers: pairs under the wide deck, clear of the sidewalks' pedestrian line.
            Material m = c.P.Lit(new Color(0.5f, 0.49f, 0.46f), 0.05f);
            // In Rail Row's median, clear of the First St junction at x 55.
            foreach (float px in new[] { x0 + 8f, x0 + 19f, x1 - 19f, x1 - 8f })
                c.Kit.Box(st, "Station pier", V(px, (y - DeckDepth - 1f) / 2f, z), V(1.6f, y - DeckDepth + 1f, 1.6f), m);
            c.Anchor("station_platform", V((x0 + x1) / 2f, y + 1f, z + 7f));
            c.Anchor("station_street", V(x0 - 12f, 0f, z - 11f));
        }

        /// <summary>
        /// A straight flight up from beside the sidewalk to the platform end: steps you see, a smooth ramp you walk on.
        /// South side: behind Rail Row's south sidewalk; north side: behind the north sidewalk.
        /// </summary>
        private static void Stairs(CityContext c, Transform st, float side, float top)
        {
            Kit k = c.Kit;
            Material m = c.P.Lit(new Color(0.55f, 0.54f, 0.5f), 0.08f);
            Material rail = c.P.Lit(new Color(0.2f, 0.2f, 0.22f), 0.5f);
            float z = CityPlan.RailZ + side * 10.8f; // just outside the 3.5 m sidewalk (road edge at 5)
            const float run = 14f, width = 2.4f;
            float xTop = CityPlan.StationWest, xBottom = xTop - run;
            int steps = Mathf.CeilToInt(top / 0.19f);
            for (int i = 0; i < steps; i++)
            {
                float h = top * (i + 1) / steps, x = xBottom + run * i / steps;
                k.Box(st, "Step", V(x + run / steps / 2f, h / 2f, z), V(run / steps, h, width), m, collider: false);
            }
            // Walking surface: one slope.
            float len = Mathf.Sqrt(run * run + top * top);
            GameObject ramp = k.Box(st, "Stair ramp", V((xTop + xBottom) / 2f, top / 2f, z), V(len, 0.05f, width), m);
            ramp.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(top, run) * Mathf.Rad2Deg);
            ramp.GetComponent<Renderer>().enabled = false;
            // Landing joining the stair top to the platform end.
            k.Span(st, "Landing", V(xTop - 0.2f, top - 1f, Mathf.Min(z - width / 2f, CityPlan.RailZ + side * 4.6f)),
                V(xTop + 2.5f, top, Mathf.Max(z + width / 2f, CityPlan.RailZ + side * 4.6f)), m);
            foreach (float o in new[] { -width / 2f, width / 2f })
            {
                GameObject hand = k.Box(st, "Handrail", V((xTop + xBottom) / 2f, top / 2f + 1f, z + o), V(len, 0.06f, 0.06f), rail, collider: false);
                hand.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(top, run) * Mathf.Rad2Deg);
            }
        }

        // ---- tunnels ----

        private static void Tunnels(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Material portal = c.P.Lit(new Color(0.48f, 0.47f, 0.44f), 0.05f);
            Material tube = c.P.Lit(new Color(0.12f, 0.12f, 0.13f));
            foreach (CityPlan.TunnelDef t in CityPlan.Tunnels)
            {
                float yaw = Mathf.Atan2(t.Inward.x, t.Inward.y) * Mathf.Rad2Deg;
                Transform g = Kit.Group(root, "Tunnel", V(t.Portal.x, t.Floor, t.Portal.y), yaw);
                float w = t.Width / 2f, h = t.Height;
                // Local +z runs into the hill. Portal face: pillars and a lintel around the opening, big enough to hide the cut.
                k.Box(g, "Portal left", V(-w - 3f, h / 2f + 1f, -0.5f), V(6f, h + 8f, 1f), portal);
                k.Box(g, "Portal right", V(w + 3f, h / 2f + 1f, -0.5f), V(6f, h + 8f, 1f), portal);
                k.Box(g, "Portal lintel", V(0f, h + 3f, -0.5f), V(t.Width, 6f, 1f), portal);
                // The tube: walls and ceiling, dark inside.
                k.Box(g, "Tube left", V(-w - 0.4f, h / 2f, t.Length / 2f), V(0.8f, h + 0.5f, t.Length), tube);
                k.Box(g, "Tube right", V(w + 0.4f, h / 2f, t.Length / 2f), V(0.8f, h + 0.5f, t.Length), tube);
                k.Box(g, "Tube roof", V(0f, h + 0.4f, t.Length / 2f), V(t.Width + 1.6f, 0.8f, t.Length), tube);
                k.Box(g, "Tube floor", V(0f, -0.6f, t.Length / 2f), V(t.Width + 1.6f, 1f, t.Length), tube);
                k.Box(g, "Tube end", V(0f, h / 2f, t.Length), V(t.Width, h, 0.5f), tube);
                if (t.Closed == null) continue;
                // A barrier across the closed road, well inside, with a sign.
                Material stripes = c.P.Lit(new Color(0.9f, 0.5f, 0.1f), 0.3f);
                k.Box(g, "Barrier", V(0f, 0.6f, 14f), V(t.Width, 1.2f, 0.4f), stripes);
                k.Box(g, "Barrier board", V(0f, 2.6f, 14f), V(6f, 1.2f, 0.1f), c.P.Lit(new Color(0.95f, 0.85f, 0.2f), 0.2f), collider: false);
                k.Text(g, t.Closed, V(0f, 2.6f, 13.94f), 0f, 0.28f, new Color(0.1f, 0.1f, 0.1f));
            }
        }

        // ---- civic ----

        private static void CityHall(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Rect b = CityHallBlock;
            Transform hall = Kit.Group(root, "City Hall");
            Material stone = c.P.Surface(Finish.Concrete, new Color(0.78f, 0.74f, 0.66f), 0.1f);
            Material trim = c.P.Lit(new Color(0.9f, 0.88f, 0.82f), 0.12f);
            Material roof = c.P.Lit(new Color(0.3f, 0.36f, 0.34f), 0.2f);
            Material plaza = c.P.Surface(Finish.Paving, new Color(0.6f, 0.57f, 0.52f), 0.08f);
            k.Span(hall, "Plaza", V(b.xMin, -0.05f, b.yMin), V(b.xMax, 0.01f, b.yMax), plaza).AddComponent<SurfaceTag>().Roughness = 0.2f;
            // The hall: three storeys with a portico facing Maple.
            Rect f = Rect.MinMaxRect(230f, 26f, 275f, 57f);
            k.Facade(hall, "City Hall", V(f.xMin, 0f, f.yMin), V(f.xMax, 13f, f.yMax), c.P.Facade(FacadeStyle.Stucco, false), roof);
            k.Span(hall, "Cornice", V(f.xMin - 0.4f, 12.6f, f.yMin - 0.4f), V(f.xMax + 0.4f, 13.4f, f.yMax + 0.4f), trim, collider: false);
            k.Span(hall, "Steps", V(243f, 0f, 20f), V(262f, 0.6f, 26f), stone);
            for (int i = 0; i < 6; i++)
                k.Cylinder(hall, "Column", V(244.5f + i * 3.2f, 5.3f, 23f), 0.9f, 9.4f, trim, collider: true);
            k.Span(hall, "Pediment", V(242.5f, 10f, 21.5f), V(262.5f, 11.4f, 26f), trim, collider: false);
            k.Text(hall, "CITY HALL", V(252.5f, 10.7f, 21.4f), 0f, 0.6f, new Color(0.25f, 0.22f, 0.18f));
            // Clock tower: rises from the back of the hall to 38 m, four faces.
            float tx = 252.5f, tz = 45f;
            k.Box(hall, "Tower", V(tx, 15f, tz), V(8f, 30f, 8f), stone);
            k.Box(hall, "Belfry", V(tx, 33f, tz), V(7f, 6f, 7f), trim);
            k.Box(hall, "Spire base", V(tx, 36.4f, tz), V(8.2f, 0.8f, 8.2f), roof, collider: false);
            var spire = new MeshBuilder();
            Vector3 apex = V(tx, 45f, tz);
            Vector3[] baseRing = { V(tx - 4.1f, 36.8f, tz - 4.1f), V(tx + 4.1f, 36.8f, tz - 4.1f), V(tx + 4.1f, 36.8f, tz + 4.1f), V(tx - 4.1f, 36.8f, tz + 4.1f) };
            for (int i = 0; i < 4; i++) spire.Quad(baseRing[i], baseRing[(i + 1) % 4], apex, apex);
            spire.Build(hall, "Spire", roof, collider: false);
            Transform faces = Kit.Group(c.Dynamic, "Clock faces", V(tx, 26f, tz));
            faces.gameObject.AddComponent<ClockFaces>().Configure(c, 4.05f, 2.8f);
            // Fountain and flagpole on the plaza, benches along it.
            k.Cylinder(hall, "Fountain", V(252.5f, 0.35f, 8f), 8f, 0.7f, stone, collider: true);
            k.Cylinder(hall, "Fountain water", V(252.5f, 0.66f, 8f), 7.2f, 0.05f, c.P.Glass(new Color(0.3f, 0.45f, 0.5f, 0.7f)));
            k.Cylinder(hall, "Fountain column", V(252.5f, 1.5f, 8f), 0.8f, 2.4f, trim, collider: true);
            k.Cylinder(hall, "Flagpole", V(236f, 6f, 12f), 0.14f, 12f, c.P.Lit(new Color(0.8f, 0.8f, 0.8f), 0.6f), collider: true);
            k.Box(hall, "Flag", V(237.1f, 11f, 12f), V(2f, 1.2f, 0.03f), c.P.Lit(new Color(0.15f, 0.3f, 0.55f)), collider: false);
            foreach (float bx in new[] { 232f, 241f, 264f, 273f })
            {
                Transform bench = Kit.Group(hall, "Bench", V(bx, 0f, 15f), 180f);
                k.Box(bench, "Seat", V(0f, 0.45f, 0f), V(1.8f, 0.07f, 0.5f), c.P.Lit(new Color(0.45f, 0.32f, 0.2f)));
                k.Box(bench, "Back", V(0f, 0.75f, 0.22f), V(1.8f, 0.4f, 0.06f), c.P.Lit(new Color(0.45f, 0.32f, 0.2f)), collider: false);
                c.Place(V(bx, 0f, 15f), PlaceKind.Bench, "bench");
            }
            c.Place(V(252.5f, 0f, 18.5f), PlaceKind.Door, "City Hall");
            c.Anchor("city_hall_plaza", V(252.5f, 0f, 2f));
        }

        private static void Harborview(CityContext c, Transform root) => HarborviewTower.Shell(c, root);

        private static void WaterTowerOnCrest(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            float y = TownTerrain.Natural(WaterTower.x, WaterTower.y);
            Transform w = Kit.Group(root, "Water tower", V(WaterTower.x, y, WaterTower.y));
            Material legs = c.P.Lit(new Color(0.55f, 0.57f, 0.58f), 0.3f);
            Material tank = c.P.Lit(new Color(0.78f, 0.8f, 0.8f), 0.3f);
            foreach (Vector2 o in new[] { new Vector2(-4f, -4f), new Vector2(4f, -4f), new Vector2(-4f, 4f), new Vector2(4f, 4f) })
                k.Box(w, "Leg", V(o.x * 0.8f, 11f, o.y * 0.8f), V(0.45f, 22f, 0.45f), legs);
            for (float h = 5f; h < 22f; h += 6f)
                k.Span(w, "Brace", V(-3.4f, h, -3.4f), V(3.4f, h + 0.3f, 3.4f), legs, collider: false);
            k.Cylinder(w, "Tank", V(0f, 26f, 0f), 11f, 8f, tank, collider: true);
            GameObject cap = k.Cylinder(w, "Cap", V(0f, 30.8f, 0f), 11f, 1.6f, tank);
            cap.transform.localScale = new Vector3(8f, 0.8f, 8f);
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                Vector3 face = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 26f, -5.55f);
                k.Text(w, "PORT KELL", face, yaw, 1.1f, new Color(0.15f, 0.3f, 0.5f));
            }
            Light beacon = c.PointLight(w, V(0f, 32f, 0f), 6f, 2f, new Color(1f, 0.2f, 0.15f));
            beacon.enabled = false;
            c.NightLights.Add(beacon);
        }

        /// <summary>The old Kell Paper Co. mill: closed, windows boarded, its stack still the tallest thing in the Foundry.</summary>
        private static void Smokestack(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Rect f = PaperMill;
            Transform mill = Kit.Group(root, "Kell Paper Mill");
            Material brick = c.P.Facade(FacadeStyle.Brick, false);
            Material roof = c.P.Lit(new Color(0.28f, 0.27f, 0.27f));
            Material stack = c.P.Lit(new Color(0.52f, 0.3f, 0.24f), 0.05f);
            k.Facade(mill, "Mill hall", V(f.xMin, 0f, f.yMin + 6f), V(f.xMin + 44f, 12f, f.yMax), brick, roof);
            k.Facade(mill, "Boiler house", V(f.xMax - 20f, 0f, f.yMin), V(f.xMax, 16f, f.yMin + 20f), brick, roof);
            k.Text(mill, "KELL PAPER CO.", V(f.xMin + 22f, 10f, f.yMin + 5.9f), 0f, 1.1f, new Color(0.82f, 0.78f, 0.7f, 0.8f));
            Vector3 at = V(f.xMax - 8f, 0f, f.yMin + 10f);
            k.Cylinder(mill, "Smokestack", at + V(0f, 24f, 0f), 4.2f, 48f, stack, collider: true);
            for (float h = 8f; h < 48f; h += 10f)
                k.Cylinder(mill, "Stack band", at + V(0f, h, 0f), 4.5f, 0.5f, c.P.Lit(new Color(0.35f, 0.33f, 0.32f)), collider: false);
            k.Cylinder(mill, "Stack lip", at + V(0f, 48.3f, 0f), 4.8f, 0.6f, c.P.Lit(new Color(0.18f, 0.17f, 0.17f)), collider: false);
            Light beacon = c.PointLight(mill, at + V(0f, 49.5f, 0f), 8f, 2f, new Color(1f, 0.2f, 0.15f));
            beacon.enabled = false;
            c.NightLights.Add(beacon);
        }

        /// <summary>A 60 m lattice radio mast on the overlook (three legs, rings, a red lamp).</summary>
        private static void Mast(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            float y = TownTerrain.Natural(RadioMast.x, RadioMast.y);
            Transform m = Kit.Group(root, "Radio mast", V(RadioMast.x, y, RadioMast.y));
            Material steel = c.P.Lit(new Color(0.75f, 0.3f, 0.25f), 0.3f);
            Material white = c.P.Lit(new Color(0.9f, 0.9f, 0.88f), 0.3f);
            const float height = 60f;
            for (int leg = 0; leg < 3; leg++)
            {
                float a = leg * 120f * Mathf.Deg2Rad;
                Vector3 bottom = V(Mathf.Cos(a) * 3f, 0f, Mathf.Sin(a) * 3f), top = V(Mathf.Cos(a) * 0.6f, height, Mathf.Sin(a) * 0.6f);
                GameObject l = k.Box(m, "Leg", (bottom + top) / 2f, V(0.18f, Vector3.Distance(bottom, top), 0.18f), steel, collider: leg == 0);
                l.transform.localRotation = Quaternion.FromToRotation(Vector3.up, top - bottom);
            }
            for (float h = 5f; h < height; h += 5f)
            {
                float r = Mathf.Lerp(3f, 0.6f, h / height);
                k.Cylinder(m, "Ring", V(0f, h, 0f), r * 2f, 0.12f, (int)(h / 5f) % 2 == 0 ? steel : white, collider: false);
            }
            k.Box(m, "Hut", V(5f, 1.3f, 3f), V(3f, 2.6f, 2.4f), c.P.Lit(new Color(0.6f, 0.62f, 0.6f)));
            Renderer lamp = k.Sphere(m, "Lamp", V(0f, height + 0.4f, 0f), 0.6f, c.P.Glow(new Color(1f, 0.15f, 0.1f))).GetComponent<Renderer>();
            m.gameObject.AddComponent<Blinker>().Configure(lamp, 1.2f);
        }

        // ---- the night ----

        /// <summary>The Meridian casino's lot, marquee and 26 m sign, lit up with chasing bulbs (the district's landmark); the building is <see cref="Meridian"/>.</summary>
        private static void Casino(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Rect lot = CasinoLot;
            const float y = 1.5f;
            Transform cas = Kit.Group(root, "Casino lot", V(0f, y, 0f));
            Material wall = c.P.Lit(new Color(0.16f, 0.2f, 0.3f), 0.3f);
            Material gold = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            k.Span(cas, "Lot", V(lot.xMin, -0.05f, lot.yMin), V(lot.xMax, 0.01f, lot.yMax), c.P.Lit(new Color(0.22f, 0.22f, 0.23f), 0.1f)).AddComponent<SurfaceTag>().Roughness = 0.25f;
            k.Span(cas, "Marquee", V(496f, 4.2f, 3f), V(534f, 5f, 8f), gold, collider: false);
            k.Span(cas, "Marquee soffit", V(496.3f, 4.18f, 3.3f), V(533.7f, 4.2f, 7.9f), c.P.Glow(new Color(1f, 0.8f, 0.5f), 1.2f), collider: false);
            // Bulbs change material, so they stay out of the static batch.
            Transform lights = Kit.Group(c.Dynamic, "Casino lights", V(0f, y, 0f));
            var bulbs = new List<Renderer>();
            for (float x = 497f; x < 534f; x += 1.2f)
                bulbs.Add(k.Sphere(lights, "Bulb", V(x, 4.1f, 3.1f), 0.22f, c.P.Glow(new Color(1f, 0.85f, 0.45f))).GetComponent<Renderer>());
            // The sign: a tall pylon by Maple, its name in lights, a star on top.
            Vector3 p = V(466f, 0f, 2f);
            k.Box(cas, "Sign pole", p + V(0f, 11f, 0f), V(1.4f, 22f, 1.4f), wall);
            k.Box(cas, "Sign panel", p + V(0f, 19f, 0f), V(4f, 12f, 1f), wall);
            k.Box(cas, "Sign trim", p + V(0f, 19f, 0f), V(4.3f, 12.3f, 0.8f), gold, collider: false);
            Material neon = c.P.Glow(new Color(0.35f, 0.85f, 1f), 2.6f);
            string[] letters = { "C", "A", "S", "I", "N", "O" };
            for (int i = 0; i < letters.Length; i++)
                foreach (float face in new[] { -0.55f, 0.55f })
                    k.Text(cas, letters[i], p + V(0f, 23.5f - i * 1.8f, face), face < 0f ? 0f : 180f, 1.1f, new Color(0.45f, 0.9f, 1f));
            k.Box(cas, "Neon edge", p + V(0f, 25.2f, 0f), V(4.4f, 0.2f, 1.1f), neon, collider: false);
            k.Box(cas, "Neon edge", p + V(0f, 12.8f, 0f), V(4.4f, 0.2f, 1.1f), neon, collider: false);
            k.Sphere(cas, "Star", p + V(0f, 26.5f, 0f), 1.6f, c.P.Glow(new Color(1f, 0.9f, 0.5f), 2.4f));
            lights.gameObject.AddComponent<Chaser>().Configure(bulbs, c.P.Glow(new Color(1f, 0.85f, 0.45f)), c.P.Unlit(new Color(0.35f, 0.3f, 0.2f)));
            Meridian.Build(c, root);
        }

        /// <summary>A billboard on the highway into town, facing traffic coming out of the hills.</summary>
        private static void Billboard(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            var at = new Vector2(-600f, 62f);
            float y = TownTerrain.Natural(at.x, at.y);
            Transform b = Kit.Group(root, "Billboard", V(at.x, y, at.y), 110f);
            Material steel = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.4f);
            k.Box(b, "Post", V(-3f, 5f, 0.4f), V(0.5f, 10f, 0.5f), steel);
            k.Box(b, "Post", V(3f, 5f, 0.4f), V(0.5f, 10f, 0.5f), steel);
            k.Box(b, "Board", V(0f, 11f, 0f), V(12f, 5f, 0.3f), c.P.Lit(new Color(0.12f, 0.45f, 0.35f), 0.2f));
            k.Text(b, "PENNYBRIDGE", V(0f, 12f, -0.17f), 0f, 0.9f, Color.white);
            k.Text(b, "trade from anywhere · no fees on your first 10 trades", V(0f, 10.4f, -0.17f), 0f, 0.28f, new Color(0.9f, 1f, 0.9f));
            k.Text(b, "WELCOME TO PORT KELL", V(0f, 9.1f, -0.17f), 0f, 0.3f, new Color(1f, 0.95f, 0.6f));
        }
    }

    /// <summary>A clock's four faces with hands that follow the game clock.</summary>
    public sealed class ClockFaces : MonoBehaviour
    {
        private CityContext _c;
        private readonly List<(Transform Hour, Transform Minute)> _hands = new List<(Transform, Transform)>();

        public void Configure(CityContext c, float halfSize, float faceSize)
        {
            _c = c;
            Material face = c.P.Lamp(new Color(0.92f, 0.9f, 0.84f), new Color(1f, 0.95f, 0.8f), 1.2f);
            Material hand = c.P.Lit(new Color(0.08f, 0.08f, 0.08f));
            for (int i = 0; i < 4; i++)
            {
                float yaw = i * 90f;
                Transform f = Kit.Group(transform, "Face", Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, -halfSize), yaw);
                c.Kit.Cylinder(f, "Dial", Vector3.zero, faceSize, 0.1f, face).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Transform hour = Kit.Group(f, "Hour", new Vector3(0f, 0f, -0.08f));
                c.Kit.Box(hour, "Hand", new Vector3(0f, faceSize * 0.16f, 0f), new Vector3(0.14f, faceSize * 0.32f, 0.04f), hand, collider: false);
                Transform minute = Kit.Group(f, "Minute", new Vector3(0f, 0f, -0.12f));
                c.Kit.Box(minute, "Hand", new Vector3(0f, faceSize * 0.22f, 0f), new Vector3(0.09f, faceSize * 0.44f, 0.04f), hand, collider: false);
                _hands.Add((hour, minute));
            }
        }

        private void Update()
        {
            if (_c == null) return;
            System.DateTime now = _c.Game.Clock.Now;
            float minutes = now.Minute + now.Second / 60f;
            float hours = now.Hour % 12 + minutes / 60f;
            foreach (var (hour, minute) in _hands)
            {
                // Seen from the front (-z), clockwise is a negative roll.
                hour.localRotation = Quaternion.Euler(0f, 0f, -hours * 30f);
                minute.localRotation = Quaternion.Euler(0f, 0f, -minutes * 6f);
            }
        }
    }

    /// <summary>Blinks a lamp on and off (aircraft warning lights).</summary>
    public sealed class Blinker : MonoBehaviour
    {
        private Renderer _lamp;
        private float _period;

        public void Configure(Renderer lamp, float period)
        {
            _lamp = lamp;
            _period = period;
        }

        private void Update()
        {
            if (_lamp != null) _lamp.enabled = Mathf.Repeat(Time.time, _period) < _period * 0.4f;
        }
    }

    /// <summary>Marquee bulbs chasing along a row.</summary>
    public sealed class Chaser : MonoBehaviour
    {
        private List<Renderer> _bulbs;
        private Material _on, _off;
        private int _last = -1;

        public void Configure(List<Renderer> bulbs, Material on, Material off)
        {
            _bulbs = bulbs;
            _on = on;
            _off = off;
        }

        private void Update()
        {
            if (_bulbs == null) return;
            int phase = (int)(Time.time * 8f) % 3;
            if (phase == _last) return;
            _last = phase;
            for (int i = 0; i < _bulbs.Count; i++) _bulbs[i].sharedMaterial = i % 3 == phase ? _off : _on;
        }
    }
}
