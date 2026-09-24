using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Where the town's businesses are (TOWN_SPEC A5): the working-class strip on Maple St between Pine and Cedar,
    /// both sides; the highway strip (gas, burgers, motel, bikes, car wash, the supermarket); and Canal Row's night
    /// spots across the canal. Rows are laid shop by shop along a sidewalk edge; a null name leaves a gap (a
    /// passage between buildings).
    /// </summary>
    public static class BusinessPlan
    {
        private static readonly Color Red = new Color(0.7f, 0.15f, 0.12f), Blue = new Color(0.12f, 0.3f, 0.55f), Green = new Color(0.15f, 0.42f, 0.28f),
            Gold = new Color(0.85f, 0.65f, 0.2f), Teal = new Color(0.1f, 0.45f, 0.5f), Purple = new Color(0.45f, 0.2f, 0.55f), Orange = new Color(0.85f, 0.42f, 0.12f),
            Cream = new Color(0.92f, 0.88f, 0.76f), Pink = new Color(0.9f, 0.35f, 0.55f), Cyan = new Color(0.2f, 0.75f, 0.9f), Grey = new Color(0.35f, 0.36f, 0.38f);

        private readonly struct Shop
        {
            public readonly string Name, Tagline;
            public readonly Trade Trade;
            public readonly float Width;
            public readonly int Floors;
            public readonly Color Brand;

            public Shop(string name, Trade trade, float width, int floors, Color brand, string tagline = null)
            {
                Name = name;
                Trade = trade;
                Width = width;
                Floors = floors;
                Brand = brand;
                Tagline = tagline;
            }
        }

        private static Shop S(string name, Trade trade, float width, int floors, Color brand, string tagline = null) => new Shop(name, trade, width, floors, brand, tagline);
        private static Shop Gap(float width) => new Shop(null, Trade.Vacant, width, 0, Color.clear);

        /// <summary>A row along a sidewalk edge at <paramref name="edge"/> (z), starting at x0 and running east; north side faces south.</summary>
        private static IEnumerable<Business> Row(float x0, float edge, bool northSide, float depth, FacadeStyle style, params Shop[] shops)
        {
            float x = x0;
            foreach (Shop s in shops)
            {
                float centre = x + s.Width / 2f;
                x += s.Width;
                if (s.Name == null) continue;
                yield return new Business
                {
                    Name = s.Name, Trade = s.Trade, Width = s.Width, Depth = depth, Floors = s.Floors, Brand = s.Brand, Tagline = s.Tagline,
                    Style = s.Floors > 0 ? style : FacadeStyle.Brick,
                    Front = new Vector2(centre, edge), Inward = northSide ? Vector2.up : Vector2.down,
                };
            }
        }

        private static Business One(string name, Trade trade, float x, float z, Vector2 inward, float width, float depth, int floors, Color brand, string tagline = null) =>
            new Business { Name = name, Trade = trade, Front = new Vector2(x, z), Inward = inward, Width = width, Depth = depth, Floors = floors, Brand = brand, Tagline = tagline, Style = FacadeStyle.Stucco };

        public static List<Business> All()
        {
            var all = new List<Business>();
            // Maple St, north side (shops face Maple; the alley behind them at z 16).
            all.AddRange(Row(-241.5f, -5.5f, true, 14f, FacadeStyle.Brick,
                S("Suds & Duds", Trade.Laundromat, 14f, 1, Teal, "LAUNDROMAT"), S("Kell Pawn & Loan", Trade.Pawn, 12f, 2, Gold, "WE BUY GOLD"),
                S("Maple Liquor", Trade.Liquor, 10f, 1, Red), Gap(3f), S("Sharp Edges", Trade.Barber, 9f, 2, Blue, "BARBER"),
                S("Second Chance", Trade.Thrift, 14f, 1, Green, "THRIFT"), S("Rosie's Diner", Trade.Diner, 16f, 0, Red, "BREAKFAST ALL DAY"),
                S("Northwest Pharmacy", Trade.Pharmacy, 16.5f, 1, Green)));
            all.AddRange(Row(-133.5f, -5.5f, true, 14f, FacadeStyle.Townhouse,
                S("Harlow Hardware", Trade.Hardware, 18f, 1, Orange, "KEYS CUT · PAINT MIXED"), S("Ink Tide", Trade.Tattoo, 9f, 2, Purple, "TATTOO"),
                Gap(3f), S("Crumb Bakery", Trade.Bakery, 10f, 1, Cream), S("Smoke Signals", Trade.SmokeShop, 8f, 1, Grey),
                S("Kell Auto Parts", Trade.AutoParts, 16f, 0, Red), S("For Lease", Trade.Vacant, 16f, 1, Grey, "FOR LEASE · 555-0142")));
            // Maple St, south side (the alley behind at z -48, then Rail Row under the viaduct).
            all.AddRange(Row(-241.5f, -22.5f, false, 14f, FacadeStyle.Stucco,
                S("Kell Valley Credit Union", Trade.Bank, 18f, 1, Blue, "ATM INSIDE"), S("Golden Wok", Trade.Restaurant, 14f, 1, Red, "CHINESE"),
                Gap(3f), S("Page Turner", Trade.Books, 12f, 2, Green, "USED BOOKS"), S("Fix-It Phones", Trade.Repair, 12f, 1, Cyan, "REPAIRS"),
                S("Budget Furniture", Trade.UsedFurniture, 18f, 0, Orange), S("The Anchor", Trade.Bar, 17f, 1, Cyan, "COCKTAILS · POOL")));
            all.AddRange(Row(-133.5f, -22.5f, false, 14f, FacadeStyle.Brick,
                S("Dollar Den", Trade.Discount, 16f, 1, Gold), S("Taqueria Luz", Trade.Restaurant, 12f, 1, Orange, "TACOS"),
                Gap(3f), S("Maple Florist", Trade.Florist, 10f, 1, Pink), S("Iron Kell Gym", Trade.Gym, 20f, 1, Grey, "OPEN 5 AM"),
                S("Video King", Trade.Vacant, 19f, 0, Grey, "VIDEO KING · CLOSED · THANKS FOR 22 YEARS")));
            all.AddRange(Row(-36.5f, -22.5f, false, 14f, FacadeStyle.Brick,
                S("Sal's Pizza", Trade.Pizza, 12f, 2, Red, "SLICES")));

            // The highway strip.
            all.Add(One("Kell Burger", Trade.FastFood, -405f, -5.5f, Vector2.up, 20f, 16f, 0, Red, "DRIVE-THRU"));
            all.Add(One("Timberline Motel", Trade.MotelOffice, -381f, -5.5f, Vector2.up, 10f, 10f, 0, Green, "VACANCY"));
            all.Add(One("Ridgeline Moto", Trade.Moto, -404f, -22.5f, Vector2.down, 24f, 20f, 0, Orange, "MOTORCYCLES · GEAR"));
            all.Add(One("Blue Wave", Trade.CarWash, -372f, -22.5f, Vector2.down, 12f, 10f, 0, Blue, "CAR WASH"));
            all.Add(One("FreshWay Market", Trade.Supermarket, -315f, 104f, Vector2.up, 48f, 32f, 0, Green, "GROCERY · DELI · BAKERY"));
            // Downtown: the police station on Exchange St, at Grove.
            all.Add(One("Kell Valley Police", Trade.Police, 143.5f, 44.25f, Vector2.right, 34f, 24f, 1, Blue, "POLICE"));
            // The Foundry's tyre shop, on Foundry St by the mechanic.
            all.Add(One("Treadwell Tires", Trade.AutoParts, -300f, -151.5f, Vector2.up, 18f, 14f, 0, Orange, "TYRES · ALIGNMENT"));

            // Canal Row: the night side of the canal.
            all.AddRange(Row(353.5f, -5.5f, true, 14f, FacadeStyle.Brick,
                S("Pixel Pier", Trade.Arcade, 22f, 1, Cyan, "ARCADE"), S("Lantern Diner", Trade.Diner, 18f, 0, Gold, "OPEN LATE"),
                Gap(3f), S("The Low Tide", Trade.Bar, 16f, 1, Blue, "LIVE MUSIC FRI"), S("Pulse", Trade.Nightclub, 26f, 1, Pink, "CLUB")));
            all.AddRange(Row(353.5f, 41.5f, false, 14f, FacadeStyle.Townhouse,
                S("Tiki Tom's", Trade.Bar, 16f, 1, Orange, "TIKI BAR"), S("Night Owl", Trade.Tattoo, 10f, 1, Purple, "TATTOO"),
                S("Canal Pizza", Trade.Pizza, 12f, 1, Red), S("Starlite", Trade.Bar, 18f, 1, Pink, "KARAOKE"), S("For Lease", Trade.Vacant, 20f, 1, Grey)));
            all.AddRange(Row(353.5f, -22.5f, false, 14f, FacadeStyle.Stucco,
                S("Canal Motel", Trade.MotelOffice, 12f, 0, Teal, "VACANCY"), S("Sunrise Donuts", Trade.Bakery, 10f, 0, Pink),
                S("Quay Liquor", Trade.Liquor, 10f, 1, Red), S("Blue Note", Trade.Bar, 16f, 1, Blue, "JAZZ")));
            return all;
        }

        /// <summary>Lots that sit off the flat core get their ground levelled first.</summary>
        public static void AddPads(CityContext c)
        {
            foreach (Business b in All())
            {
                float y = StreetMap.Plan.StreetGrade(b.Front);
                Vector2 centre = b.Front + b.Inward * (b.Depth / 2f);
                c.Pads.Add(new Pad(centre, new Vector2(b.Width / 2f, b.Depth / 2f + 1f), b.Yaw, y));
            }
            c.Pads.Add(Pad.FromRect(Rect.MinMaxRect(-462f, -5.5f, -419f, 42f), 0f));   // Northstar forecourt
            c.Pads.Add(Pad.FromRect(Rect.MinMaxRect(-345f, 78.5f, -285f, 104f), 0f));  // FreshWay lot
        }

        public static void Build(CityContext c)
        {
            int seed = 9100;
            foreach (Business b in All()) Businesses.Build(c, b, seed++);
            Transform root = Kit.Group(c.Static, "Highway strip");
            Northstar(c, root);
            ParkingLot(c, root, Rect.MinMaxRect(-345f, 78.5f, -285f, 104f), 0f);
            MotelWing(c, root, new Vector3(-372f, 0f, 7f), -90f, 48f, "TIMBERLINE"); // runs north, rooms facing Lumber Rd
            MotelWing(c, root, new Vector3(372f, 1.5f, -48f), 0f, 40f, "CANAL MOTEL");
            CarWashBay(c, root, new Vector3(-384f, 0f, -38f));
        }

        // ---- the highway gas station: a canopy you can see from the tunnel, a tall sign, a shop ----

        public const decimal NorthstarPrice = 1.72m;

        private static void Northstar(CityContext c, Transform parent)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(parent, "Northstar Fuel", new Vector3(-441f, 0f, -5.5f));
            Transform dyn = Kit.Group(c.Dynamic, "Northstar Fuel", new Vector3(-441f, 0f, -5.5f));
            Material slab = c.P.Lit(new Color(0.36f, 0.36f, 0.37f), 0.1f);
            Material white = c.P.Lit(new Color(0.94f, 0.94f, 0.92f), 0.2f);
            Material brand = c.P.Lit(new Color(0.75f, 0.12f, 0.12f), 0.3f);
            k.Span(root, "Forecourt", new Vector3(-21f, -0.05f, 0f), new Vector3(22f, 0.005f, 47f), slab).AddComponent<SurfaceTag>().Roughness = 0.3f;
            CurbCut(c, root, -12f, 9f);
            CurbCut(c, root, 12f, 9f);
            // Canopy on six columns, three islands with a pump each side.
            foreach (float x in new[] { -12f, 0f, 12f })
            foreach (float z in new[] { 9f, 21f })
                k.Box(root, "Column", new Vector3(x, 2.4f, z), new Vector3(0.5f, 4.8f, 0.5f), white);
            k.Span(root, "Canopy", new Vector3(-15f, 4.8f, 7f), new Vector3(15f, 5.4f, 23f), white, collider: false);
            k.Span(root, "Fascia", new Vector3(-15.1f, 4.75f, 6.9f), new Vector3(15.1f, 5.45f, 7f), brand, collider: false);
            k.Text(root, "NORTHSTAR", new Vector3(0f, 5.1f, 6.85f), 0f, 0.36f, Color.white);
            Material glow = c.P.Lamp(new Color(0.75f, 0.75f, 0.72f), new Color(1f, 0.98f, 0.95f), 2.4f);
            foreach (float x in new[] { -9f, 0f, 9f })
            {
                k.Box(root, "Canopy light", new Vector3(x, 4.78f, 15f), new Vector3(2f, 0.03f, 6f), glow, collider: false);
                var lamp = new GameObject("Canopy spot");
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = new Vector3(x, 4.7f, 15f);
                lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 14f;
                light.spotAngle = 120f;
                light.intensity = 30f;
                light.color = new Color(0.95f, 0.97f, 1f);
                light.enabled = false;
                c.NightLights.Add(light);
            }
            foreach (float x in new[] { -6f, 6f })
            {
                k.Span(root, "Island", new Vector3(x - 0.6f, 0f, 12f), new Vector3(x + 0.6f, 0.15f, 18f), c.P.Lit(new Color(0.62f, 0.6f, 0.56f)));
                Transform pump = Kit.Group(dyn, "Pump", new Vector3(x, 0.15f, 15f));
                k.Box(pump, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 1.7f, 0.45f), white);
                k.Box(pump, "Band", new Vector3(0f, 1.45f, 0f), new Vector3(0.72f, 0.35f, 0.47f), brand, collider: false);
                pump.gameObject.AddComponent<FuelPump>().Configure(c.Game, c.Hud);
            }
            // Air pump, bins, the shop at the back.
            k.Box(root, "Air pump", new Vector3(18f, 0.7f, 30f), new Vector3(0.6f, 1.4f, 0.5f), c.P.Lit(new Color(0.2f, 0.5f, 0.75f), 0.3f));
            k.Text(root, "AIR", new Vector3(18f, 1.2f, 29.72f), 0f, 0.12f, Color.white);
            foreach (float x in new[] { -13f, 13f })
                k.Cylinder(root, "Bin", new Vector3(x, 0.45f, 11f), 0.55f, 0.9f, c.P.Lit(new Color(0.2f, 0.28f, 0.22f)), collider: true);
            Businesses.Build(c, new Business
            {
                Name = "Northstar Mart", Trade = Trade.Discount, Front = new Vector2(-441f, -5.5f + 30f), Inward = Vector2.up, Width = 20f, Depth = 14f,
                Brand = new Color(0.75f, 0.12f, 0.12f), Tagline = "SNACKS · COFFEE · ICE", Style = FacadeStyle.Stucco,
            }, 9001);
            // Bagged ice by the shop door, as every station has.
            k.Solid(k.Fit(root, "gas_ice", new Vector3(6.5f, 0f, 29.45f), new Vector3(1.8f, 0f, 0f), 180f)); // the pack faces +z
            // The tall sign on the corner: seen from the tunnel mouth, lit at night.
            Transform sign = Kit.Group(root, "Northstar sign", new Vector3(-18f, 0f, 3f), 45f);
            k.Box(sign, "Pole", new Vector3(0f, 11f, 0f), new Vector3(1f, 22f, 1f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.4f));
            k.Box(sign, "Panel", new Vector3(0f, 21f, 0f), new Vector3(6f, 4f, 0.8f), c.P.Lamp(new Color(0.75f, 0.12f, 0.12f), new Color(1f, 0.25f, 0.2f), 1.6f), collider: false);
            k.Text(sign, "NORTHSTAR", new Vector3(0f, 21.6f, -0.45f), 0f, 0.55f, Color.white);
            k.Text(sign, "NORTHSTAR", new Vector3(0f, 21.6f, 0.45f), 180f, 0.55f, Color.white);
            string price = "REGULAR $" + NorthstarPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/L";
            k.Box(sign, "Prices", new Vector3(0f, 18f, 0f), new Vector3(5f, 1.6f, 0.6f), c.P.Lit(new Color(0.08f, 0.08f, 0.09f)), collider: false);
            k.Text(sign, price, new Vector3(0f, 18f, -0.32f), 0f, 0.3f, new Color(1f, 0.85f, 0.3f));
            k.Text(sign, price, new Vector3(0f, 18f, 0.32f), 180f, 0.3f, new Color(1f, 0.85f, 0.3f));
            c.Anchor("northstar_pump", root.TransformPoint(new Vector3(-6f, 0f, 15f)));
        }

        /// <summary>A driveway across the sidewalk in front of a lot (local frame: the lot starts at z 0, the sidewalk runs z -3.5..0).</summary>
        private static void CurbCut(CityContext c, Transform root, float x, float width)
        {
            const float run = 1.4f, rise = -CityPlan.RoadY, sidewalk = CityPlan.SidewalkWidth;
            GameObject ramp = c.Kit.Box(root, "Kerb ramp", new Vector3(x, CityPlan.RoadY + rise / 2f - 0.03f, -sidewalk - run / 2f),
                new Vector3(width, 0.06f, Mathf.Sqrt(run * run + rise * rise)), c.P.Lit(new Color(0.58f, 0.57f, 0.54f), 0.08f));
            ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f);
            ramp.AddComponent<SurfaceTag>().Roughness = 0.2f;
        }

        private static void ParkingLot(CityContext c, Transform root, Rect r, float y)
        {
            Kit k = c.Kit;
            k.Span(root, "Parking lot", new Vector3(r.xMin, y - 0.05f, r.yMin), new Vector3(r.xMax, y + 0.006f, r.yMax), c.P.Lit(new Color(0.25f, 0.25f, 0.26f), 0.1f))
                .AddComponent<SurfaceTag>().Roughness = 0.3f;
            Material paint = c.P.Lit(new Color(0.88f, 0.88f, 0.86f), 0.1f);
            for (float x = r.xMin + 2f; x < r.xMax - 1f; x += 3f)
            {
                k.Decal(root, "Bay line", new Vector3(x, y + 0.012f, r.yMin + 5f), new Vector2(0.1f, 5f), 0f, paint);
                k.Decal(root, "Bay line", new Vector3(x, y + 0.012f, r.yMax - 5f), new Vector2(0.1f, 5f), 0f, paint);
                if (x + 3f < r.xMax - 1f)
                {
                    c.ParkingSpots.Add((new Vector3(x + 1.5f, y, r.yMin + 5f), 180f, ParkingKind.Lot));
                    c.ParkingSpots.Add((new Vector3(x + 1.5f, y, r.yMax - 5f), 0f, ParkingKind.Lot));
                }
            }
        }

        /// <summary>Two storeys of motel rooms with a walkway and numbered doors (outside only).</summary>
        private static void MotelWing(CityContext c, Transform parent, Vector3 at, float yaw, float length, string name)
        {
            Kit k = c.Kit;
            Transform w = Kit.Group(parent, name + " rooms", at, yaw);
            Material wall = c.P.Lit(new Color(0.8f, 0.72f, 0.58f), 0.08f);
            Material door = c.P.Lit(new Color(0.3f, 0.45f, 0.4f), 0.3f);
            Material rail = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.4f);
            // Local x along the wing, doors face local -z.
            k.Span(w, "Rooms", new Vector3(0f, 0f, 0f), new Vector3(length, 6f, 8f), wall);
            k.Span(w, "Roof", new Vector3(-0.3f, 6f, -2.6f), new Vector3(length + 0.3f, 6.3f, 8.3f), c.P.Lit(new Color(0.3f, 0.26f, 0.22f)));
            k.Span(w, "Balcony", new Vector3(0f, 2.9f, -2.2f), new Vector3(length, 3.1f, 0f), c.P.Lit(new Color(0.55f, 0.54f, 0.5f)));
            k.Span(w, "Balcony rail", new Vector3(0f, 3.1f, -2.25f), new Vector3(length, 4.1f, -2.15f), rail);
            int n = 1;
            for (float x = 2f; x < length - 1f; x += 4f)
                foreach (float floor in new[] { 0f, 3.1f })
                {
                    k.Span(w, "Door", new Vector3(x - 0.45f, floor, -0.03f), new Vector3(x + 0.45f, floor + 2.1f, 0f), door, collider: false);
                    k.Text(w, (floor > 0f ? 200 : 100 + 0).ToString().Substring(0, 1) + (n % 20).ToString("00"), new Vector3(x, floor + 2.3f, -0.05f), 0f, 0.1f, Color.white);
                    k.Pane(w, "Window", new Vector3(x + 0.7f, floor + 1f, -0.03f), new Vector3(x + 1.8f, floor + 2f, 0f), new Color(0.4f, 0.5f, 0.55f, 0.6f), collider: false);
                    n++;
                }
            k.Box(w, "Stairs", new Vector3(length + 1.2f, 1.5f, -1f), new Vector3(2f, 3f, 2f), c.P.Lit(new Color(0.55f, 0.54f, 0.5f)));
        }

        private static void CarWashBay(CityContext c, Transform parent, Vector3 at)
        {
            Kit k = c.Kit;
            Transform w = Kit.Group(parent, "Car wash bay", at);
            Material wall = c.P.Lit(new Color(0.2f, 0.4f, 0.7f), 0.3f);
            k.Span(w, "Wall", new Vector3(-3.5f, 0f, -10f), new Vector3(-3.2f, 4f, 10f), wall);
            k.Span(w, "Wall", new Vector3(3.2f, 0f, -10f), new Vector3(3.5f, 4f, 10f), wall);
            k.Span(w, "Roof", new Vector3(-3.5f, 4f, -10f), new Vector3(3.5f, 4.3f, 10f), c.P.Lit(new Color(0.9f, 0.9f, 0.9f)), collider: false);
            for (float z = -6f; z < 7f; z += 6f)
                k.Cylinder(w, "Brush", new Vector3(0f, 2.2f, z), 1.2f, 3.4f, c.P.Lit(new Color(0.2f, 0.55f, 0.9f)), collider: false)
                    .transform.localPosition = new Vector3(-2.2f, 2f, z);
        }
    }
}
