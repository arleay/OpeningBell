using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Phase 11 car dealers, both open 9 AM–7 PM (spec §74):
    /// First Street Motors, new cars on the paved lot behind the Maple shops (driveway off First St, showroom
    /// office with the sales desk), and Harbor Auto Sales, a used lot behind the Main Street shops (driveway off
    /// Harbor Ave, sales trailer). The lots themselves are <see cref="DealerLot"/>s, wired to the player's driving
    /// once it exists.
    /// </summary>
    public static class Dealerships
    {
        public static readonly Hours DealerHours = Hours.Of(9, 19);

        /// <summary>New-car lineup (mainstream and performance; luxury and exotic dealers are a later phase).</summary>
        public static readonly string[] NewLineup = { "car_sedan", "car_hatch", "car_suv", "car_pickup", "car_van", "car_gt", "car_luxury", "car_havoc", "car_predator" };
        public static readonly string[] UsedLineup = { "car_sedan", "car_sedan", "car_hatch", "car_suv", "car_pickup", "car_van", "car_gt", "car_luxury", "car_havoc" };

        public static readonly Rect NewLot = Rect.MinMaxRect(63.5f, 13f, 103f, 47.5f);
        public static readonly Rect UsedLot = Rect.MinMaxRect(170f, -70.5f, 206.5f, -46.5f);

        /// <summary>Display spots: two rows of three, noses to the street.</summary>
        private static readonly (Vector3 P, float Yaw)[] NewSpots =
        {
            (new Vector3(70f, 0f, 17f), 270f), (new Vector3(80f, 0f, 17f), 270f), (new Vector3(90f, 0f, 17f), 270f),
            (new Vector3(70f, 0f, 25f), 270f), (new Vector3(80f, 0f, 25f), 270f), (new Vector3(90f, 0f, 25f), 270f),
        };
        private static readonly (Vector3 P, float Yaw)[] UsedSpots =
        {
            (new Vector3(177f, 0f, -49.5f), 90f), (new Vector3(186.5f, 0f, -49.5f), 90f), (new Vector3(196f, 0f, -49.5f), 90f),
            (new Vector3(177f, 0f, -57f), 90f), (new Vector3(186.5f, 0f, -57f), 90f), (new Vector3(196f, 0f, -57f), 90f),
        };

        public static List<DealerLot> Build(CityContext c) => new List<DealerLot> { FirstStreetMotors(c), HarborAutoSales(c) };

        private static DealerLot FirstStreetMotors(CityContext c)
        {
            Kit k = c.Kit;
            Rect lot = NewLot;
            Transform root = Kit.Group(c.Static, "First Street Motors");
            Transform dyn = Kit.Group(c.Dynamic, "First Street Motors");
            Material asphalt = c.P.Lit(new Color(0.24f, 0.24f, 0.25f), 0.15f);
            Material brand = c.P.Lit(new Color(0.1f, 0.3f, 0.62f), 0.4f);
            Material white = c.P.Lit(new Color(0.92f, 0.92f, 0.9f), 0.2f);

            k.Span(root, "Lot", new Vector3(lot.xMin, -0.05f, lot.yMin), new Vector3(lot.xMax, 0.005f, lot.yMax), asphalt).AddComponent<SurfaceTag>().Roughness = 0.25f;
            Driveway(c, root, asphalt, 43f, west: true);

            // Showroom office at the back of the lot, door facing the cars.
            Rect office = Rect.MinMaxRect(84f, 36f, 103f, 47.5f);
            const float doorX = 93.5f, top = 4.5f;
            Shops.ShopShell(c, root, office, top, 0.25f, white, c.P.Lit(new Color(0.95f, 0.95f, 0.94f)), c.P.Lit(new Color(0.8f, 0.8f, 0.78f), 0.5f),
                doorX, 1.6f, new[] { (88.4f, 6.2f), (98.8f, 6.6f) });
            k.Span(root, "Roof", new Vector3(office.xMin - 0.3f, top, office.yMin - 0.3f), new Vector3(office.xMax + 0.3f, top + 0.35f, office.yMax + 0.3f), brand);
            k.Span(root, "Sign band", new Vector3(office.xMin, 3.6f, office.yMin - 0.12f), new Vector3(office.xMax, 4.4f, office.yMin), brand, collider: false);
            k.Text(root, "FIRST STREET MOTORS", new Vector3(doorX, 4f, office.yMin - 0.14f), 0f, 0.36f, Color.white);
            Door door = c.SwingDoor(dyn, "door", new Vector3(doorX - 0.8f, 0f, office.yMin + 0.125f), 1.6f, 2.25f, c.P.Glass(new Color(0.6f, 0.7f, 0.75f, 0.35f)), glass: true);
            door.LockReason = () => DealerHours.Contains(c.Game.Clock.Now) ? null : "closed (opens 9 AM)";
            k.Span(root, "Sales desk", new Vector3(92f, 0f, 42f), new Vector3(95f, 0.76f, 42.9f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.4f));
            k.Box(root, "Monitor", new Vector3(93.5f, 1.0f, 42.7f), new Vector3(0.55f, 0.35f, 0.04f), c.P.Lit(new Color(0.05f, 0.05f, 0.06f), 0.6f), collider: false);
            for (int i = 0; i < 2; i++)
                k.Box(root, "Chair", new Vector3(92.8f + i * 1.4f, 0.24f, 41.3f), new Vector3(0.5f, 0.48f, 0.5f), brand);
            c.PointLight(root, new Vector3(88.5f, 4f, 41.5f), 9f, 1f, new Color(1f, 0.96f, 0.9f));
            c.PointLight(root, new Vector3(98.5f, 4f, 41.5f), 9f, 1f, new Color(1f, 0.96f, 0.9f));

            // Pylon at the driveway, read from First St.
            Pylon(c, root, new Vector3(64.6f, 0f, 37.6f), 90f, brand, "FIRST STREET", "MOTORS", "NEW CARS · OPEN 9 AM – 7 PM");
            // Floodlights over the display rows.
            foreach (float x in new[] { 66f, 85f })
                LotLight(c, root, new Vector3(x, 0f, 21f));
            Bays(c, root, new[] { new Vector3(99f, 0f, 16f), new Vector3(99f, 0f, 22f), new Vector3(99f, 0f, 28f) }, 270f, "DELIVERY");
            Bays(c, root, NewSpots.Select(s => s.P).ToArray(), 270f, null);

            Vector3 station = new Vector3(doorX, 0f, 43.7f);
            StaffNpc seller = StaffNpc.Create(k, dyn, "Car salesman", 8101, new Color(0.1f, 0.3f, 0.62f),
                new WorkSchedule { Shift = DealerHours }, new List<Vector3> { station, new Vector3(101.5f, 0f, 43.7f), new Vector3(102f, 0f, 46.8f) },
                180f, new[] { NpcPose.Typing, NpcPose.Stand, NpcPose.Phone },
                () => "Welcome to First Street Motors! Everything outside is new, full warranty. Test drives are free.",
                () => "New stock comes in every Monday. Got a car to trade? Park it on the lot and I'll make you an offer.",
                c.Game, c.Hud, c.Player, look: "Suit");
            TradeInDesk(c, new Vector3(93.5f, 0.95f, 42.45f), new Vector3(3.2f, 0.6f, 1.4f), out GameObject desk);

            var dealer = new GameObject("First Street Motors lot").AddComponent<DealerLot>();
            dealer.transform.SetParent(c.Dynamic, false);
            dealer.Configure(c.Game, c.Hud, k, seller, "fsm", "First Street Motors", used: false, NewLineup, lot,
                NewSpots,
                new[] { new Vector3(99f, 0f, 16f), new Vector3(99f, 0f, 22f), new Vector3(99f, 0f, 28f) }, 270f,
                (new Vector3(69f, 0f, 43f), 270f), DealerHours);
            desk.AddComponent<TradeInDesk>().Configure(dealer);

            c.Place(new Vector3(doorX, 0f, office.yMin - 0.8f), PlaceKind.Door, "First Street Motors");
            c.Anchor("dealer_new_lot", new Vector3(75f, 0f, 21f));
            c.Anchor("dealer_new_desk", new Vector3(doorX, 0f, 41f));
            return dealer;
        }

        private static DealerLot HarborAutoSales(CityContext c)
        {
            Kit k = c.Kit;
            Rect lot = UsedLot;
            Transform root = Kit.Group(c.Static, "Harbor Auto Sales");
            Transform dyn = Kit.Group(c.Dynamic, "Harbor Auto Sales");
            Material asphalt = c.P.Lit(new Color(0.34f, 0.33f, 0.32f), 0.08f);
            Material brand = c.P.Lit(new Color(0.8f, 0.12f, 0.08f), 0.3f);
            Material cream = c.P.Lit(new Color(0.9f, 0.86f, 0.74f), 0.15f);

            k.Span(root, "Lot", new Vector3(lot.xMin, -0.05f, lot.yMin), new Vector3(lot.xMax, 0.005f, lot.yMax), asphalt).AddComponent<SurfaceTag>().Roughness = 0.4f;
            Driveway(c, root, asphalt, -65f, west: false);

            // Sales trailer in the corner, the salesman at a table under its awning.
            k.Span(root, "Sales trailer", new Vector3(171f, 0f, -70f), new Vector3(178f, 2.8f, -66f), cream);
            k.Span(root, "Trailer skirt", new Vector3(170.9f, 0f, -70.1f), new Vector3(178.1f, 0.35f, -65.9f), c.P.Lit(new Color(0.3f, 0.3f, 0.3f)), collider: false);
            k.Span(root, "Awning", new Vector3(170.6f, 2.45f, -66f), new Vector3(178.4f, 2.55f, -64.3f), brand, collider: false);
            k.Span(root, "Trailer door", new Vector3(176.4f, 0.35f, -66.05f), new Vector3(177.4f, 2.3f, -65.98f), c.P.Lit(new Color(0.45f, 0.42f, 0.38f)), collider: false);
            k.Pane(root, "Trailer window", new Vector3(172f, 1.1f, -66.05f), new Vector3(174.6f, 2f, -65.97f), new Color(0.5f, 0.6f, 0.65f, 0.6f), collider: false);
            k.Text(root, "SALES", new Vector3(174.5f, 2.72f, -64.28f), 0f, 0.12f, Color.white);
            k.Span(root, "Table", new Vector3(173.4f, 0f, -64.1f), new Vector3(175.6f, 0.74f, -63.4f), c.P.Lit(new Color(0.55f, 0.55f, 0.55f), 0.3f));
            c.PointLight(root, new Vector3(174.5f, 2.3f, -64.8f), 7f, 0.9f, new Color(1f, 0.9f, 0.75f));

            Pylon(c, root, new Vector3(205.2f, 0f, -48.5f), 270f, brand, "HARBOR", "AUTO SALES", "USED CARS · OPEN 9 AM – 7 PM");
            // Pennant strings over the rows, the used-lot trademark.
            Pennants(c, root, new Vector3(172f, 3.6f, -53.2f), new Vector3(205f, 3.6f, -53.2f));
            Pennants(c, root, new Vector3(172f, 3.6f, -60.8f), new Vector3(205f, 3.6f, -60.8f));
            LotLight(c, root, new Vector3(191.5f, 0f, -53.2f));
            Bays(c, root, new[] { new Vector3(184f, 0f, -67.5f), new Vector3(191f, 0f, -67.5f) }, 90f, "SOLD");
            Bays(c, root, UsedSpots.Select(s => s.P).ToArray(), 90f, null);

            Vector3 station = new Vector3(174.5f, 0f, -64.95f);
            StaffNpc seller = StaffNpc.Create(k, dyn, "Used car dealer", 8202, new Color(0.8f, 0.12f, 0.08f),
                new WorkSchedule { Shift = DealerHours }, new List<Vector3> { station, new Vector3(179f, 0f, -65f), new Vector3(179f, 0f, -67.5f) },
                0f, new[] { NpcPose.Stand, NpcPose.Phone, NpcPose.Drink },
                () => "Harbor Auto Sales! Every car's got its sticker, every flaw's on it. Take one out, no charge.",
                () => "Cheap and honest, pick one. Low condition means a tune-up soon. Selling? Park it here and I'll make an offer.",
                c.Game, c.Hud, c.Player, look: "Casual");
            TradeInDesk(c, new Vector3(174.5f, 0.9f, -63.75f), new Vector3(2.4f, 0.5f, 1f), out GameObject desk);

            var dealer = new GameObject("Harbor Auto Sales lot").AddComponent<DealerLot>();
            dealer.transform.SetParent(c.Dynamic, false);
            dealer.Configure(c.Game, c.Hud, k, seller, "has", "Harbor Auto Sales", used: true, UsedLineup, lot,
                UsedSpots,
                new[] { new Vector3(184f, 0f, -67.5f), new Vector3(191f, 0f, -67.5f) }, 90f,
                (new Vector3(199.5f, 0f, -65f), 90f), DealerHours);
            desk.AddComponent<TradeInDesk>().Configure(dealer);

            c.Anchor("dealer_used_lot", new Vector3(186.5f, 0f, -53.2f));
            c.Anchor("dealer_used_desk", new Vector3(174.5f, 0f, -62.5f));
            return dealer;
        }

        /// <summary>A driveway across the sidewalk and a ramp down the kerb, 8 m wide, centred on <paramref name="z"/>.
        /// West: the block's west edge (First St at x 60); otherwise the east edge (Harbor Ave at x 210).</summary>
        private static void Driveway(CityContext c, Transform root, Material m, float z, bool west)
        {
            Kit k = c.Kit;
            float kerb = west ? 60f : 210f, inner = west ? kerb + CityPlan.SidewalkWidth : kerb - CityPlan.SidewalkWidth;
            k.Span(root, "Driveway", new Vector3(Mathf.Min(kerb, inner), -0.05f, z - 4f), new Vector3(Mathf.Max(kerb, inner), 0.006f, z + 4f), m)
                .AddComponent<SurfaceTag>().Roughness = 0.3f;
            // Like the fuel station's: a sloped slab in the kerb-side metre and a half of the road.
            const float run = 1.4f, rise = -CityPlan.RoadY;
            float x = west ? kerb - run / 2f : kerb + run / 2f;
            GameObject ramp = k.Box(root, "Driveway ramp", new Vector3(x, CityPlan.RoadY + rise / 2f - 0.03f, z),
                new Vector3(8f, 0.06f, Mathf.Sqrt(run * run + rise * rise)), m);
            ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, west ? 90f : -90f, 0f);
            ramp.AddComponent<SurfaceTag>().Roughness = 0.3f;
        }

        /// <summary>A tall sign on two legs; <paramref name="yaw"/> turns the readable face (0 = read from the south).</summary>
        private static void Pylon(CityContext c, Transform root, Vector3 at, float yaw, Material brand, string line1, string line2, string small)
        {
            Kit k = c.Kit;
            Transform p = Kit.Group(root, "Pylon", at, yaw);
            Material pole = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.4f);
            k.Box(p, "Leg", new Vector3(-1.1f, 2.5f, 0f), new Vector3(0.18f, 5f, 0.18f), pole);
            k.Box(p, "Leg", new Vector3(1.1f, 2.5f, 0f), new Vector3(0.18f, 5f, 0.18f), pole);
            k.Box(p, "Panel", new Vector3(0f, 4.4f, 0f), new Vector3(2.8f, 1.6f, 0.25f), brand, collider: false);
            k.Text(p, line1, new Vector3(0f, 4.72f, -0.14f), 0f, 0.3f, Color.white);
            k.Text(p, line2, new Vector3(0f, 4.2f, -0.14f), 0f, 0.3f, Color.white);
            k.Box(p, "Hours board", new Vector3(0f, 1.3f, 0f), new Vector3(2.2f, 0.4f, 0.08f), c.P.Lit(new Color(0.95f, 0.95f, 0.93f)), collider: false);
            k.Text(p, small, new Vector3(0f, 1.3f, -0.05f), 0f, 0.09f, new Color(0.12f, 0.12f, 0.14f));
            // The same on the back, for traffic from the other way.
            k.Text(p, line1, new Vector3(0f, 4.72f, 0.14f), 180f, 0.3f, Color.white);
            k.Text(p, line2, new Vector3(0f, 4.2f, 0.14f), 180f, 0.3f, Color.white);
        }

        private static void LotLight(CityContext c, Transform root, Vector3 at)
        {
            Material pole = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.4f);
            c.Kit.Cylinder(root, "Light pole", at + new Vector3(0f, 3.5f, 0f), 0.16f, 7f, pole, collider: true);
            c.Kit.Box(root, "Floodlight", at + new Vector3(0f, 7.05f, 0f), new Vector3(1.4f, 0.18f, 0.5f), c.P.Lamp(new Color(0.55f, 0.55f, 0.5f), new Color(1f, 0.9f, 0.7f)), collider: false);
            Light l = c.PointLight(root, at + new Vector3(0f, 6.6f, 0f), 18f, 1.4f, new Color(1f, 0.9f, 0.75f));
            l.enabled = false;
            c.NightLights.Add(l);
        }

        /// <summary>Painted bays where bought cars wait.</summary>
        private static void Bays(CityContext c, Transform root, Vector3[] bays, float yaw, string label)
        {
            Material paint = c.P.Lit(new Color(0.9f, 0.9f, 0.88f), 0.1f);
            Transform g = Kit.Group(root, "Bays");
            foreach (Vector3 b in bays)
            {
                Transform bay = Kit.Group(g, "Bay", b, yaw);
                // Local +z is the car's forward: side lines 3 m apart, 6.4 m long.
                c.Kit.Decal(bay, "Line", new Vector3(-1.5f, 0.012f, 0f), new Vector2(0.1f, 6.4f), 0f, paint);
                c.Kit.Decal(bay, "Line", new Vector3(1.5f, 0.012f, 0f), new Vector2(0.1f, 6.4f), 0f, paint);
                if (label == null) continue;
                TextMesh t = c.Kit.Text(bay, label, new Vector3(0f, 0.013f, -2.4f), 0f, 0.35f, paint.color);
                t.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // painted flat, read from behind the car
            }
        }

        private static void Pennants(CityContext c, Transform root, Vector3 from, Vector3 to)
        {
            Color[] colours = { new Color(0.85f, 0.15f, 0.1f), new Color(0.95f, 0.8f, 0.15f), new Color(0.15f, 0.4f, 0.8f), new Color(0.95f, 0.95f, 0.95f) };
            Material cord = c.P.Lit(new Color(0.2f, 0.2f, 0.2f));
            float length = Vector3.Distance(from, to);
            Vector3 mid = (from + to) / 2f;
            c.Kit.Box(root, "Cord", mid, new Vector3(length, 0.02f, 0.02f), cord, collider: false);
            foreach (Vector3 end in new[] { from, to })
                c.Kit.Cylinder(root, "Pennant pole", new Vector3(end.x, end.y / 2f, end.z), 0.08f, end.y, cord, collider: true);
            int n = Mathf.FloorToInt(length / 0.8f);
            for (int i = 1; i < n; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)n);
                c.Kit.Box(root, "Pennant", p + new Vector3(0f, -0.18f, 0f), new Vector3(0.3f, 0.34f, 0.01f), c.P.Lit(colours[i % colours.Length], 0.3f), collider: false);
            }
        }

        private static void TradeInDesk(CityContext c, Vector3 at, Vector3 size, out GameObject desk)
        {
            desk = new GameObject("Trade-in desk");
            desk.transform.SetParent(c.Dynamic, false);
            desk.transform.position = at;
            var box = desk.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
        }
    }
}
