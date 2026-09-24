using System.Collections.Generic;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Phase 13 shops on Maple between the coffee shop and the mart: an empty unit to let and Hillside Cycles
    /// (bikes, e-bikes, service, fast charger, 9 AM–7 PM). Plus e-bike chargers at home and at the Calder Building.
    /// </summary>
    public static class MobilityShops
    {
        public static readonly Hours BikeHours = Hours.Of(9, 19);

        public static void Build(CityContext c)
        {
            EmptyShop(c);
            BikeShop(c);
            Charger(c, "Home charger", new Vector3(8.6f, 0f, -5.25f), 250f);
            Charger(c, "Public charger", new Vector3(161.5f, 0f, -5.9f), 250f);
        }

        /// <summary>The unit between the coffee shop and Hillside Cycles: empty, papered up, to let.</summary>
        private static void EmptyShop(CityContext c)
        {
            Kit k = c.Kit;
            Rect f = CityPlan.EmptyShop; // x 78.5..94, z -5.5..9
            Transform root = Kit.Group(c.Static, "Empty shop");
            ModularFacade.Build(c, root, "Empty shop", new Vector3(f.xMin, 0f, f.yMin), new Vector3(f.xMax, 12f, f.yMax), FacadeStyle.Brick, 31, storefront: true);
            k.Span(root, "Papered window", new Vector3(f.xMin + 0.8f, 0.6f, f.yMin - 0.05f), new Vector3(f.xMax - 0.8f, 3.05f, f.yMin - 0.03f), c.P.Lit(new Color(0.72f, 0.66f, 0.52f)), collider: false);
            k.Text(root, "FOR LEASE", new Vector3(f.center.x, 2f, f.yMin - 0.07f), 0f, 0.3f, new Color(0.7f, 0.12f, 0.1f));
        }

        private static void BikeShop(CityContext c)
        {
            Kit k = c.Kit;
            Rect f = CityPlan.BikeShop; // x 94.5..111.5, z -5.5..9
            Transform root = Kit.Group(c.Static, "Hillside Cycles");
            Transform dyn = Kit.Group(c.Dynamic, "Hillside Cycles");
            const float doorX = 103f, top = 4.4f;
            Shops.ShopShell(c, root, f, top, 0.25f, c.P.Lit(new Color(0.62f, 0.62f, 0.6f), 0.1f), c.P.Lit(new Color(0.93f, 0.93f, 0.92f)),
                c.P.Lit(new Color(0.3f, 0.32f, 0.34f), 0.4f), doorX, 1.3f, new[] { (98.3f, 6.2f), (107.7f, 6.2f) });
            ModularFacade.Build(c, root, "Flats above", new Vector3(f.xMin, top, f.yMin), new Vector3(f.xMax, 10f, f.yMax), FacadeStyle.Stucco, 41);
            k.Span(root, "Sign band", new Vector3(f.xMin, 3.5f, f.yMin - 0.12f), new Vector3(f.xMax, 4.35f, f.yMin), c.P.Lit(new Color(0.12f, 0.4f, 0.28f)), collider: false);
            k.Text(root, "HILLSIDE CYCLES", new Vector3(doorX, 3.92f, f.yMin - 0.14f), 0f, 0.36f, new Color(0.95f, 0.95f, 0.9f));
            k.Text(root, "BIKES · E-BIKES · SERVICE   9 AM – 7 PM", new Vector3(doorX, 2.6f, f.yMin - 0.03f), 0f, 0.07f, new Color(0.95f, 0.93f, 0.88f));
            Door door = c.SwingDoor(dyn, "door", new Vector3(doorX - 0.65f, 0f, f.yMin + 0.125f), 1.3f, 2.25f, c.P.Glass(new Color(0.6f, 0.7f, 0.75f, 0.35f)), glass: true);
            door.LockReason = () => BikeHours.Contains(c.Game.Clock.Now) ? null : "closed (opens 9 AM)";

            k.Span(root, "Service counter", new Vector3(98.5f, 0f, 6.1f), new Vector3(108.5f, 1.02f, 6.8f), c.P.Lit(new Color(0.2f, 0.22f, 0.24f), 0.4f));
            k.Box(root, "Work stand", new Vector3(110.2f, 0.6f, 7.8f), new Vector3(0.1f, 1.2f, 0.1f), c.P.Lit(new Color(0.8f, 0.2f, 0.15f), 0.4f));
            c.PointLight(root, new Vector3(99f, 3.8f, 1.5f), 10f, 1.1f, Color.white);
            c.PointLight(root, new Vector3(107f, 3.8f, 1.5f), 10f, 1.1f, Color.white);

            Vector3 station = new Vector3(doorX + 0.5f, 0f, 7.6f);
            StaffNpc mechanic = StaffNpc.Create(k, dyn, "Bike mechanic", 7502, new Color(0.12f, 0.4f, 0.28f),
                new WorkSchedule { Shift = BikeHours }, new List<Vector3> { station, new Vector3(110.8f, 0f, 7.6f), new Vector3(111f, 0f, 8.4f) },
                180f, new[] { NpcPose.Typing, NpcPose.Stand, NpcPose.Drink },
                () => "Hey! Looking to ride, or need a tune-up?",
                () => "Road bike's fastest on pavement, but the trail bike doesn't care about grass. E-bikes: charge 'em overnight.",
                c.Game, c.Hud, c.Player, look: "Worker");

            Vector3 shopCentre = new Vector3(f.center.x, 0f, f.center.y);
            Vector3 pickup = new Vector3(107.5f, 0f, f.yMin - 0.45f); // clear of the bench and bin at x 104–105.4
            string[] bikes = { "bike_bmx", "bike_commuter", "bike_mtb", "bike_road", "ebike_commuter", "ebike_speed" };
            // Two groups either side of the aisle from the door to the service counter.
            Vector3[] floor =
            {
                new Vector3(96.6f, 0f, 2.4f), new Vector3(99.4f, 0f, 2.4f), new Vector3(106.6f, 0f, 2.4f),
                new Vector3(109.4f, 0f, 2.4f), new Vector3(96.6f, 0f, 4.6f), new Vector3(109.4f, 0f, 4.6f),
            };
            for (int i = 0; i < bikes.Length; i++)
            {
                if (!c.Game.Vehicles.Catalog.TryGetModel(bikes[i], out VehicleModel model)) continue;
                Vector3 at = floor[i];
                Transform spot = Kit.Group(root, "Bike display", at, 90f);
                VehicleVisual.Build(k, spot, model).Animate(0, 0.6, 0);
                k.Text(root, model.Name + "  $" + model.Price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), at + new Vector3(0f, 1.45f, -0.3f), 0f, 0.07f, new Color(0.2f, 0.2f, 0.2f));
                Display(c, mechanic, SaleKind.Vehicle, bikes[i], at + new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 1.2f, 0.7f), pickup, 90f, shopCentre);
            }
            (SaleKind Kind, string Id, float X)[] services =
            {
                (SaleKind.TuneUp, "tuneup", 100.5f), (SaleKind.NewTires, "tires", 103.5f), (SaleKind.Part, "battery_extended", 106.5f),
            };
            Material dark = c.P.Lit(new Color(0.08f, 0.08f, 0.09f));
            foreach (var (kind, id, x) in services)
            {
                Vector3 at = new Vector3(x, 1.02f, 6.25f);
                if (kind == SaleKind.NewTires) k.Cylinder(root, "Tyre", at + new Vector3(0f, 0.33f, 0f), 0.66f, 0.05f, dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                else k.Box(root, kind == SaleKind.TuneUp ? "Toolbox" : "Battery", at + new Vector3(0f, 0.08f, 0f), new Vector3(0.45f, 0.16f, 0.22f),
                    kind == SaleKind.TuneUp ? c.P.Lit(new Color(0.75f, 0.12f, 0.1f), 0.4f) : dark, collider: false);
                Display(c, mechanic, kind, id, at + new Vector3(0f, 0.15f, 0f), new Vector3(0.9f, 0.5f, 0.6f), pickup, 90f, shopCentre);
            }
            Charger(c, "Fast charger", new Vector3(99.5f, 0f, f.yMin - 0.4f), 600f);

            c.Place(new Vector3(doorX, 0f, f.yMin - 0.8f), PlaceKind.Door, "Hillside Cycles");
            c.Anchor("bike_front_out", new Vector3(doorX, 0f, f.yMin - 2f));
            c.Anchor("bike_pickup", pickup);
            c.Anchor("bike_counter", new Vector3(100.5f, 0f, 5f));
            c.Anchor("bike_floor", new Vector3(doorX, 0f, 0.4f));
        }

        private static void Display(CityContext c, StaffNpc staff, SaleKind kind, string id, Vector3 at, Vector3 size, Vector3 pickup, float pickupYaw, Vector3 shop)
        {
            var aim = new GameObject("For sale: " + id);
            aim.transform.SetParent(c.Dynamic, false);
            aim.transform.position = at;
            var box = aim.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            aim.AddComponent<ForSaleDisplay>().Configure(c.Game, c.Hud, staff, kind, id, pickup, pickupYaw, shop);
        }

        private static void Charger(CityContext c, string label, Vector3 at, float watts)
        {
            Transform post = Kit.Group(c.Dynamic, label, at);
            c.Kit.Box(post, "Post", new Vector3(0f, 0.55f, 0f), new Vector3(0.22f, 1.1f, 0.16f), c.P.Lit(new Color(0.2f, 0.45f, 0.35f), 0.4f));
            Renderer lamp = c.Kit.Box(post, "Lamp", new Vector3(0f, 1.0f, -0.085f), new Vector3(0.08f, 0.04f, 0.01f), c.P.Unlit(new Color(0.2f, 0.2f, 0.2f)), collider: false).GetComponent<Renderer>();
            c.Kit.Text(post, "EV", new Vector3(0f, 0.8f, -0.09f), 0f, 0.06f, new Color(1f, 0.95f, 0.6f));
            post.gameObject.AddComponent<ChargingPoint>().Configure(c.Game, label, watts, lamp, c.P.Unlit(new Color(0.3f, 1f, 0.45f)), c.P.Unlit(new Color(0.2f, 0.2f, 0.2f)));
        }
    }
}
