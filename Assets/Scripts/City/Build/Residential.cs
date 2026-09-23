using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// What makes a street lived in (TOWN_SPEC A7): mailboxes, bins, cars in driveways, hoops, kids' bikes, sheds,
    /// patio sets, air conditioners and meters; kept-up yards with flowers, let-go ones with long grass, junk and a
    /// car up on blocks. And the apartment blocks: cheap walk-ups along Rail Row under the trains (fire escapes you
    /// can climb to the roof) and Grove Terrace, the nicer building north of downtown.
    /// </summary>
    public static class Residential
    {
        /// <summary>Walk-up blocks on Rail Row's south side (x from, x to), clear of the station stairs.</summary>
        private static readonly (float X0, float X1)[] WalkUps =
        {
            (-240f, -152f), (-128f, -56f), (-34f, 4f), (66f, 124f), (146f, 204f), (226f, 280f),
        };
        private const float RailRowSouthEdge = -72.5f, WalkUpDepth = 17f;
        public static readonly Rect GroveTerrace = Rect.MinMaxRect(142f, 78.5f, 207f, 116f);

        public static void AddPads(CityContext c)
        {
            foreach (var (x0, x1) in WalkUps)
                c.Pads.Add(Pad.FromRect(Rect.MinMaxRect(x0, RailRowSouthEdge - WalkUpDepth - 4f, x1, RailRowSouthEdge), 0f));
            c.Pads.Add(Pad.FromRect(Rect.MinMaxRect(GroveTerrace.xMin, GroveTerrace.yMin, GroveTerrace.xMax, GroveTerrace.yMax + 16f), StreetMap.Plan.StreetGrade(GroveTerrace.center)));
        }

        /// <summary>A lot's clutter, in the lot's frame (origin on the sidewalk edge, +z into the lot).</summary>
        public static void Yard(CityContext c, Transform plot, float width, float depth, float setback, Vector2 house, float driveX, bool rundown, System.Random rng)
        {
            Kit k = c.Kit;
            float half = width / 2f;
            Material post = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.3f);
            // Mailbox by the sidewalk, on the far side of the driveway.
            float mx = driveX + (driveX > 0f ? -2.2f : 2.2f);
            k.Box(plot, "Mailbox post", new Vector3(mx, 0.55f, 0.6f), new Vector3(0.08f, 1.1f, 0.08f), post, collider: false);
            Color[] boxes = { new Color(0.15f, 0.15f, 0.16f), new Color(0.2f, 0.3f, 0.55f), new Color(0.6f, 0.15f, 0.12f), new Color(0.85f, 0.85f, 0.82f) };
            k.Box(plot, "Mailbox", new Vector3(mx, 1.15f, 0.6f), new Vector3(0.22f, 0.24f, 0.48f), c.P.Lit(boxes[rng.Next(boxes.Length)], 0.4f), collider: false);
            // Bins at the top of the driveway.
            float binZ = Mathf.Min(depth - 1.5f, setback + 7f);
            k.Box(plot, "Bin", new Vector3(driveX + Mathf.Sign(driveX) * 1.9f, 0.55f, binZ), new Vector3(0.6f, 1.1f, 0.7f), c.P.Lit(new Color(0.15f, 0.3f, 0.2f), 0.3f));
            if (rng.NextDouble() < 0.6)
                k.Box(plot, "Recycling", new Vector3(driveX + Mathf.Sign(driveX) * 1.9f, 0.5f, binZ - 0.9f), new Vector3(0.55f, 1f, 0.65f), c.P.Lit(new Color(0.15f, 0.35f, 0.6f), 0.3f));
            // A car in the driveway about half the time.
            var library = c.Game.VehicleLibrary;
            if (library != null && library.TrafficMix.Count > 0 && rng.NextDouble() < 0.55)
            {
                GameObject mesh = library.CarMesh(library.TrafficMix[rng.Next(library.TrafficMix.Count)]);
                if (mesh != null)
                {
                    GameObject car = Object.Instantiate(mesh, plot, false);
                    car.name = "Parked car";
                    car.transform.localPosition = new Vector3(driveX, 0.02f, Mathf.Min(depth - 3f, setback + 3.2f));
                    car.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    car.transform.localScale = Vector3.one * CarFactory.Scale;
                    var solid = car.AddComponent<BoxCollider>();
                    solid.center = new Vector3(0f, 0.55f, 0f);
                    solid.size = new Vector3(1.45f, 1.1f, 3.3f);
                    if (rundown && rng.NextDouble() < 0.3)
                    {
                        // Up on blocks: wheels gone, rust showing.
                        car.transform.localPosition += Vector3.up * 0.35f;
                        foreach (string wheel in CarFactory.WheelNames)
                        {
                            Transform w = FindDeep(car.transform, wheel);
                            if (w != null) w.gameObject.SetActive(false);
                        }
                        foreach (float x in new[] { -0.7f, 0.7f })
                        foreach (float z in new[] { -1.3f, 1.3f })
                            k.Box(plot, "Cinder block", car.transform.localPosition + new Vector3(x, -0.2f, z), new Vector3(0.4f, 0.4f, 0.2f), c.P.Lit(new Color(0.55f, 0.55f, 0.53f)), collider: false);
                    }
                }
            }
            // Basketball hoop at the top of the driveway.
            if (rng.NextDouble() < 0.22 && depth > setback + 9f)
            {
                Vector3 at = new Vector3(driveX, 0f, Mathf.Min(depth - 0.8f, setback + 8.6f));
                k.Box(plot, "Hoop pole", at + new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 3.2f, 0.12f), post);
                k.Box(plot, "Backboard", at + new Vector3(0f, 3.2f, -0.35f), new Vector3(1.2f, 0.8f, 0.05f), c.P.Lit(new Color(0.9f, 0.9f, 0.88f)), collider: false);
                k.Cylinder(plot, "Rim", at + new Vector3(0f, 3.0f, -0.62f), 0.46f, 0.03f, c.P.Lit(new Color(0.85f, 0.35f, 0.1f), 0.4f));
            }
            // A kid's bike dropped on the lawn.
            if (rng.NextDouble() < 0.12)
            {
                Transform bike = Kit.Group(plot, "Kid's bike", new Vector3(-driveX * 0.4f, 0.03f, setback - 2f), (float)rng.NextDouble() * 360f);
                Material paint = c.P.Lit(Color.HSVToRGB((float)rng.NextDouble(), 0.7f, 0.8f), 0.4f);
                k.Box(bike, "Frame", new Vector3(0f, 0.08f, 0f), new Vector3(0.7f, 0.06f, 0.06f), paint, collider: false);
                foreach (float x in new[] { -0.35f, 0.35f })
                    k.Cylinder(bike, "Wheel", new Vector3(x, 0.05f, 0f), 0.4f, 0.05f, c.P.Lit(new Color(0.08f, 0.08f, 0.08f)));
            }
            float back = setback + house.y + 1.5f;
            if (depth > back + 5f)
            {
                // Shed in a back corner, patio set behind the house.
                if (rng.NextDouble() < 0.35)
                {
                    float sx = -Mathf.Sign(driveX) * (half - 2f);
                    k.Box(plot, "Shed", new Vector3(sx, 1.1f, depth - 2f), new Vector3(2.6f, 2.2f, 2.2f), c.P.Lit(rundown ? new Color(0.45f, 0.4f, 0.32f) : new Color(0.7f, 0.6f, 0.45f)));
                    k.Box(plot, "Shed roof", new Vector3(sx, 2.3f, depth - 2f), new Vector3(2.9f, 0.15f, 2.5f), c.P.Lit(new Color(0.3f, 0.25f, 0.22f)), collider: false);
                }
                if (!rundown && rng.NextDouble() < 0.35)
                {
                    k.Fit(plot, "tableRound", new Vector3(0f, 0f, back + 2f), new Vector3(1f, 0f, 0f));
                    for (int i = 0; i < 3; i++)
                        k.Fit(plot, "chair", new Vector3(Mathf.Cos(i * 2.1f) * 1f, 0f, back + 2f + Mathf.Sin(i * 2.1f) * 1f), new Vector3(0.5f, 0f, 0f), i * 120f);
                }
            }
            // Air conditioner and meter on the side wall.
            float wallX = -Mathf.Sign(driveX) * (house.x / 2f + 0.35f);
            k.Box(plot, "AC unit", new Vector3(wallX, 0.4f, setback + house.y * 0.6f), new Vector3(0.7f, 0.8f, 0.7f), c.P.Lit(new Color(0.75f, 0.76f, 0.74f), 0.3f));
            k.Box(plot, "Meter", new Vector3(Mathf.Sign(driveX) * (house.x / 2f + 0.08f), 1.3f, setback + 1.2f), new Vector3(0.15f, 0.35f, 0.25f), c.P.Lit(new Color(0.55f, 0.56f, 0.55f), 0.4f), collider: false);
            // Kept up: flowers along the front. Let go: long grass, junk.
            if (!rundown)
            {
                for (int i = 0; i < 3; i++)
                {
                    GameObject bush = k.Model(plot, i % 2 == 0 ? "q_bush_flowers" : "q_bush_small_flowers",
                        new Vector3(-house.x / 2f + 1f + i * (house.x - 2f) / 2f, 0f, setback - 0.8f), rng.Next(360), 0.9f);
                    StreetBuilder.Naturalize(k, bush);
                }
            }
            else
            {
                for (int i = 0; i < 8; i++)
                {
                    GameObject grass = k.Model(plot, "q_grass_tall", new Vector3((float)(rng.NextDouble() - 0.5) * width * 0.9f, 0f, (float)rng.NextDouble() * setback), rng.Next(360), 0.9f);
                    StreetBuilder.Naturalize(k, grass);
                }
                if (rng.NextDouble() < 0.5)
                    for (int i = 0; i < 3; i++)
                        k.Cylinder(plot, "Old tyre", new Vector3(-driveX * 0.5f, 0.12f + i * 0.24f, setback - 2f), 0.7f, 0.24f, c.P.Lit(new Color(0.07f, 0.07f, 0.07f)));
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public static void BuildApartments(CityContext c)
        {
            Transform root = Kit.Group(c.Static, "Apartments");
            int seed = 4400;
            foreach (var (x0, x1) in WalkUps)
                WalkUp(c, root, x0, x1, seed++);
            GroveTerraceBuilding(c, root);
        }

        /// <summary>Three storeys of cheap flats, brick, a recessed entrance, window ACs, a fire escape up the end wall.</summary>
        private static void WalkUp(CityContext c, Transform root, float x0, float x1, int seed)
        {
            Kit k = c.Kit;
            var rng = new System.Random(seed);
            float z1 = RailRowSouthEdge, z0 = z1 - WalkUpDepth;
            const float height = 10.5f;
            Transform b = Kit.Group(root, "Walk-up " + seed);
            FacadeStyle style = rng.Next(2) == 0 ? FacadeStyle.Brick : FacadeStyle.Townhouse;
            k.Facade(b, "Walk-up", new Vector3(x0, 0f, z0), new Vector3(x1, height, z1), c.P.Facade(style, false), c.P.Lit(new Color(0.26f, 0.25f, 0.25f)));
            Material metal = c.P.Lit(new Color(0.2f, 0.2f, 0.22f), 0.4f);
            // Entrances on the Rail Row side (north), each with a canopy and a number.
            for (float x = x0 + 8f; x < x1 - 6f; x += 20f)
            {
                k.Box(b, "Entrance", new Vector3(x, 1.2f, z1 + 0.03f), new Vector3(1.6f, 2.4f, 0.06f), c.P.Lit(new Color(0.25f, 0.2f, 0.16f), 0.3f), collider: false);
                k.Box(b, "Canopy", new Vector3(x, 2.7f, z1 + 0.6f), new Vector3(2.4f, 0.12f, 1.2f), metal, collider: false);
                k.Text(b, (100 + seed % 40 + (int)((x - x0) / 20f) * 2).ToString(), new Vector3(x, 3f, z1 + 0.05f), 180f, 0.2f, Color.white);
                c.Place(new Vector3(x, 0f, z1 + 1.2f), PlaceKind.Door, "apartments");
            }
            // Window air conditioners scattered over the facade.
            for (int i = 0; i < (int)((x1 - x0) / 6f); i++)
                if (rng.NextDouble() < 0.5)
                    k.Box(b, "Window AC", new Vector3(x0 + 3f + i * 6f, 4f + rng.Next(2) * 3.2f, z1 + 0.35f), new Vector3(0.7f, 0.45f, 0.7f), c.P.Lit(new Color(0.8f, 0.8f, 0.78f), 0.3f), collider: false);
            FireEscape(c, b, new Vector3(x1 + 0.1f, 0f, z0 + WalkUpDepth / 2f), -90f, height); // on the east end wall, facing out
            k.Box(b, "Dumpster", new Vector3(x0 + 3f, 0.8f, z0 - 2.5f), new Vector3(2.2f, 1.6f, 1.6f), c.P.Lit(new Color(0.2f, 0.35f, 0.25f), 0.3f));
        }

        /// <summary>
        /// A zigzag fire escape on an end wall: landings every storey joined by steep stairs, a ladder up to the roof
        /// (the stairs are ramps you can walk up). Local x runs along the wall, -z away from it.
        /// </summary>
        public static void FireEscape(CityContext c, Transform parent, Vector3 at, float yaw, float height)
        {
            Kit k = c.Kit;
            Transform f = Kit.Group(parent, "Fire escape", at, yaw);
            Material metal = c.P.Lit(new Color(0.18f, 0.18f, 0.2f), 0.4f);
            const float storey = 3.3f, landing = 1.3f, run = 4f;
            int floors = Mathf.FloorToInt(height / storey);
            for (int i = 0; i <= floors; i++)
            {
                float y = i * storey;
                if (i > 0) k.Span(f, "Landing", new Vector3(-3f, y - 0.08f, -landing), new Vector3(3f, y, 0f), metal);
                k.Span(f, "Railing", new Vector3(-3f, y, -landing - 0.03f), new Vector3(3f, y + 1f, -landing + 0.03f), metal, collider: false);
                if (i == floors) break;
                // A stair from this level to the next, alternating direction.
                float dir = i % 2 == 0 ? 1f : -1f;
                float len = Mathf.Sqrt(run * run + storey * storey);
                GameObject stair = k.Box(f, "Stair", new Vector3(dir * (run / 2f - 1f), y + storey / 2f, -landing / 2f), new Vector3(len, 0.08f, 0.9f), metal);
                stair.transform.localRotation = Quaternion.Euler(0f, 0f, dir * Mathf.Atan2(storey, run) * Mathf.Rad2Deg);
            }
            // Ladder to the roof.
            float top = floors * storey;
            k.Box(f, "Ladder", new Vector3(2.6f, (top + height) / 2f + 0.5f, -0.2f), new Vector3(0.5f, height - top + 1f, 0.05f), metal);
            GameObject climb = k.Box(f, "Ladder ramp", new Vector3(2.6f, (top + height) / 2f + 0.5f, -0.6f), new Vector3(0.6f, 0.05f, height - top + 1.5f), metal);
            climb.transform.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            climb.GetComponent<Renderer>().enabled = false;
        }

        /// <summary>Grove Terrace: five storeys of newer flats north of downtown, balconies, a lobby, parking behind.</summary>
        private static void GroveTerraceBuilding(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Rect r = GroveTerrace;
            float y = StreetMap.Plan.StreetGrade(r.center);
            Transform b = Kit.Group(root, "Grove Terrace", new Vector3(0f, y, 0f));
            k.Facade(b, "Grove Terrace", new Vector3(r.xMin, 0f, r.yMin + 1f), new Vector3(r.xMax, 16.5f, r.yMax), c.P.Facade(FacadeStyle.Stucco, false), c.P.Lit(new Color(0.24f, 0.24f, 0.25f)));
            Material slab = c.P.Lit(new Color(0.82f, 0.82f, 0.8f), 0.1f);
            Material glass = c.P.Glass(new Color(0.6f, 0.7f, 0.75f, 0.3f));
            for (float floor = 3.3f; floor < 16f; floor += 3.3f)
                for (float x = r.xMin + 4f; x < r.xMax - 3f; x += 8f)
                {
                    k.Span(b, "Balcony", new Vector3(x - 1.6f, floor - 0.15f, r.yMin - 0.5f), new Vector3(x + 1.6f, floor, r.yMin + 1f), slab, collider: false);
                    k.Span(b, "Balcony glass", new Vector3(x - 1.6f, floor, r.yMin - 0.52f), new Vector3(x + 1.6f, floor + 1f, r.yMin - 0.48f), glass, collider: false);
                }
            k.Span(b, "Lobby canopy", new Vector3(r.center.x - 4f, 3f, r.yMin - 2f), new Vector3(r.center.x + 4f, 3.3f, r.yMin + 1f), c.P.Lit(new Color(0.2f, 0.3f, 0.35f), 0.4f), collider: false);
            k.Text(b, "GROVE TERRACE", new Vector3(r.center.x, 3.8f, r.yMin + 0.95f), 0f, 0.4f, new Color(0.2f, 0.3f, 0.35f));
            k.Span(b, "Parking", new Vector3(r.xMin, -0.04f, r.yMax), new Vector3(r.xMax, 0.008f, r.yMax + 16f), c.P.Lit(new Color(0.25f, 0.25f, 0.26f), 0.1f));
            for (float x = r.xMin + 2f; x < r.xMax - 2f; x += 3f)
                c.ParkingSpots.Add((new Vector3(x, y, r.yMax + 3.5f), 180f, ParkingKind.Kerb));
            c.Place(new Vector3(r.center.x, y, r.yMin - 1f), PlaceKind.Door, "Grove Terrace");
        }
    }
}
