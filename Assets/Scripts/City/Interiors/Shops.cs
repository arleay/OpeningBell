using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The two shops on the walk downtown: Half Past Nine (coffee, 6 AM–8 PM) and Corner Mart (6 AM–midnight).
    /// Staffed, with opening hours on the door and a counter to buy from.
    /// </summary>
    public static class Shops
    {
        public static readonly Hours CoffeeHours = Hours.Of(6, 20);
        public static readonly Hours MartHours = Hours.Of(6, 24);

        public static void Build(CityContext c)
        {
            CoffeeShop(c);
            CornerMart(c);
        }

        private static void CoffeeShop(CityContext c)
        {
            Kit k = c.Kit;
            Rect f = CityPlan.CoffeeShop; // x 64..78, z -5.5..8
            Transform root = Kit.Group(c.Static, "Half Past Nine");
            Transform dyn = Kit.Group(c.Dynamic, "Half Past Nine");
            Material brick = c.P.Lit(new Color(0.46f, 0.24f, 0.18f), 0.05f);
            Material inside = c.P.Lit(new Color(0.85f, 0.8f, 0.7f), 0.05f);
            Material floor = c.P.Lit(new Color(0.42f, 0.3f, 0.2f), 0.3f);
            Material wood = c.P.Lit(new Color(0.35f, 0.23f, 0.14f), 0.3f);
            Material counterTop = c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.5f);
            const float top = 4.2f, t = 0.25f;
            const float doorX = 71f;

            ShopShell(c, root, f, top, t, brick, inside, floor, doorX, 1.1f, new[] { (67.2f, 5.2f), (74.8f, 5.2f) });
            c.Kit.Facade(root, "Flats above", new Vector3(f.xMin, top, f.yMin), new Vector3(f.xMax, 10.5f, f.yMax), c.P.Facade(FacadeStyle.Brick, false),
                c.P.Lit(new Color(0.24f, 0.24f, 0.25f)));
            k.Box(root, "Awning", new Vector3((f.xMin + f.xMax) / 2f, 3.45f, f.yMin - 0.7f), new Vector3(f.width - 0.4f, 0.1f, 1.4f), c.P.Lit(new Color(0.16f, 0.32f, 0.3f)), collider: false);
            k.Text(root, "HALF PAST NINE", new Vector3(doorX, 3.85f, f.yMin - 0.02f), 0f, 0.34f, new Color(0.95f, 0.85f, 0.6f));
            k.Text(root, "COFFEE · 6 AM – 8 PM", new Vector3(doorX, 2.6f, f.yMin - 0.03f), 0f, 0.07f, new Color(0.95f, 0.93f, 0.88f));

            // Counter along the back, espresso machine, menu, tables.
            k.Span(root, "Counter", new Vector3(66f, 0f, 3.2f), new Vector3(76f, 1.02f, 3.9f), wood);
            k.Span(root, "Counter top", new Vector3(65.9f, 1.02f, 3.1f), new Vector3(76.1f, 1.07f, 4f), counterTop, collider: false);
            k.Span(root, "Back counter", new Vector3(65.5f, 0f, 6.9f), new Vector3(76.5f, 0.95f, 7.6f), wood);
            k.Box(root, "Espresso machine", new Vector3(70f, 1.2f, 7.25f), new Vector3(0.8f, 0.5f, 0.5f), c.P.Lit(new Color(0.7f, 0.7f, 0.72f), 0.8f), collider: false);
            k.Box(root, "Grinder", new Vector3(71.1f, 1.18f, 7.25f), new Vector3(0.25f, 0.45f, 0.3f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f), collider: false);
            k.Box(root, "Menu board", new Vector3(71f, 2.5f, 7.72f), new Vector3(3.2f, 1.1f, 0.05f), c.P.Lit(new Color(0.1f, 0.1f, 0.1f)), collider: false);
            k.Text(root, "COFFEE ........ 4.50\nPASTRY ........ 3.75", new Vector3(71f, 2.5f, 7.68f), 0f, 0.11f, new Color(0.95f, 0.93f, 0.85f));
            Material table = c.P.Lit(new Color(0.8f, 0.78f, 0.74f), 0.4f);
            foreach (Vector3 p in new[] { new Vector3(66.2f, 0f, -3.8f), new Vector3(69f, 0f, -3.8f), new Vector3(73.8f, 0f, -3.8f), new Vector3(76.2f, 0f, -1.2f) })
            {
                k.Cylinder(root, "Table", p + new Vector3(0f, 0.74f, 0f), 0.7f, 0.04f, table);
                k.Cylinder(root, "Table leg", p + new Vector3(0f, 0.37f, 0f), 0.08f, 0.74f, c.P.Lit(new Color(0.2f, 0.2f, 0.2f)), collider: true);
                foreach (float dx in new[] { -0.55f, 0.55f })
                    k.Box(root, "Stool", p + new Vector3(dx, 0.23f, 0f), new Vector3(0.35f, 0.46f, 0.35f), wood);
            }
            c.PointLight(root, new Vector3(68f, 3.6f, 1f), 9f, 1f, new Color(1f, 0.85f, 0.65f));
            c.PointLight(root, new Vector3(74f, 3.6f, 1f), 9f, 1f, new Color(1f, 0.85f, 0.65f));

            Door door = c.SwingDoor(dyn, "door", new Vector3(doorX - 0.55f, 0f, f.yMin + t / 2f), 1.1f, 2.25f, c.P.Glass(new Color(0.6f, 0.7f, 0.75f, 0.35f)), glass: true);
            door.LockReason = () => CoffeeHours.Contains(c.Game.Clock.Now) ? null : "closed (opens 6 AM)";

            Vector3 station = new Vector3(71f, 0f, 5.1f);
            var route = new List<Vector3> { station, new Vector3(76.8f, 0f, 5.1f), new Vector3(76.8f, 0f, 6.4f), new Vector3(77.6f, 0f, 6.4f) };
            StaffNpc barista = StaffNpc.Create(k, dyn, "Barista", 5202, new Color(0.16f, 0.32f, 0.3f),
                new WorkSchedule { Shift = CoffeeHours }, route, 180f, new[] { NpcPose.Stand, NpcPose.Typing, NpcPose.Drink },
                () => c.Game.Clock.Now.Hour < 11 ? "Morning! What can I get you?" : "Hey, welcome in.",
                new Rotation("Pre-market crowd comes in around nine. It's a zoo.", "Oat milk's back, if you care.", "We close at eight.").Next,
                c.Game, c.Hud, c.Player);

            Counter(c, root, new Vector3(69.2f, 1.07f, 3.4f), "Coffee", 4.50m, barista, "Here you go. Careful, it's hot.", cup: true);
            Counter(c, root, new Vector3(73f, 1.07f, 3.4f), "Pastry", 3.75m, barista, "Good choice. Fresh this morning.", cup: false);

            c.Place(new Vector3(doorX, 0f, f.yMin - 0.8f), PlaceKind.Door, "Half Past Nine");
            c.Anchor("coffee_front_out", new Vector3(doorX, 0f, f.yMin - 2f));
            c.Anchor("coffee_counter", new Vector3(69.2f, 0f, 2.2f));
        }

        private static void CornerMart(CityContext c)
        {
            Kit k = c.Kit;
            Rect f = CityPlan.CornerMart; // x 112..126.5, z -5.5..7
            Transform root = Kit.Group(c.Static, "Corner Mart");
            Transform dyn = Kit.Group(c.Dynamic, "Corner Mart");
            Material wall = c.P.Lit(new Color(0.78f, 0.76f, 0.7f), 0.05f);
            Material inside = c.P.Lit(new Color(0.92f, 0.92f, 0.9f), 0.05f);
            Material floor = c.P.Lit(new Color(0.8f, 0.8f, 0.78f), 0.35f);
            const float top = 5f, t = 0.25f;
            const float doorX = 119f;

            ShopShell(c, root, f, top, t, wall, inside, floor, doorX, 1.9f, new[] { (114.6f, 4.2f), (123.8f, 4.4f) });
            k.Span(root, "Roof", new Vector3(f.xMin, top, f.yMin), new Vector3(f.xMax, top + 0.3f, f.yMax), c.P.Lit(new Color(0.24f, 0.24f, 0.25f)));
            k.Span(root, "Sign band", new Vector3(f.xMin, 3.7f, f.yMin - 0.12f), new Vector3(f.xMax, 4.7f, f.yMin), c.P.Lit(new Color(0.12f, 0.35f, 0.6f)), collider: false);
            k.Text(root, "CORNER MART", new Vector3((f.xMin + f.xMax) / 2f, 4.2f, f.yMin - 0.14f), 0f, 0.42f, new Color(1f, 0.95f, 0.85f));
            k.Text(root, "OPEN 6 AM – MIDNIGHT", new Vector3(doorX + 1.7f, 2.4f, f.yMin - 0.03f), 0f, 0.07f, new Color(0.95f, 0.93f, 0.88f));

            // Shelves, fridges along the back, counter by the door.
            Material shelf = c.P.Lit(new Color(0.55f, 0.57f, 0.6f), 0.4f);
            Color[] goods = { new Color(0.8f, 0.2f, 0.15f), new Color(0.95f, 0.75f, 0.2f), new Color(0.2f, 0.45f, 0.75f), new Color(0.3f, 0.6f, 0.3f) };
            for (int row = 0; row < 3; row++)
            {
                float x = 118.2f + row * 2.6f;
                k.Span(root, "Shelf", new Vector3(x - 0.4f, 0f, -0.5f), new Vector3(x + 0.4f, 1.6f, 4.5f), shelf);
                for (int level = 0; level < 3; level++)
                for (int i = 0; i < 4; i++)
                    k.Box(root, "Goods", new Vector3(x, 0.45f + level * 0.5f, 0f + i * 1.1f), new Vector3(0.9f, 0.3f, 0.9f), c.P.Lit(goods[(row + level + i) % goods.Length]), collider: false);
            }
            k.Span(root, "Fridges", new Vector3(116f, 0f, 6f), new Vector3(126.2f, 2.2f, 6.75f), c.P.Lit(new Color(0.85f, 0.87f, 0.9f), 0.6f));
            k.Span(root, "Fridge glow", new Vector3(116.2f, 0.3f, 5.98f), new Vector3(126f, 2f, 6f), c.P.Unlit(new Color(0.8f, 0.9f, 1f)), collider: false);
            k.Span(root, "Counter", new Vector3(112.5f, 0f, -2.6f), new Vector3(116.5f, 1f, -1.9f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.3f));
            k.Box(root, "Register", new Vector3(114f, 1.12f, -2.2f), new Vector3(0.4f, 0.25f, 0.35f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.4f), collider: false);
            c.PointLight(root, new Vector3(116f, 4.4f, 0.5f), 10f, 1.2f, new Color(0.95f, 0.98f, 1f));
            c.PointLight(root, new Vector3(123f, 4.4f, 2.5f), 10f, 1.2f, new Color(0.95f, 0.98f, 1f));

            Door doors = c.SlidingDoor(root, "doors", new Vector3(doorX, 0f, f.yMin + t + 0.05f), 1.9f, 2.4f);
            doors.LockReason = () => MartHours.Contains(c.Game.Clock.Now) ? null : "closed (opens 6 AM)";

            Vector3 station = new Vector3(114.5f, 0f, -1.1f);
            var route = new List<Vector3> { station, new Vector3(113f, 0f, -1.1f), new Vector3(113f, 0f, 6.3f), new Vector3(112.6f, 0f, 6.3f) };
            StaffNpc clerk = StaffNpc.Create(k, dyn, "Clerk", 6303, new Color(0.12f, 0.35f, 0.6f),
                new WorkSchedule { Shift = MartHours }, route, 180f, new[] { NpcPose.Stand, NpcPose.Phone, NpcPose.Stand },
                () => "Hey.",
                new Rotation("Let me know if you need anything.", "Energy drinks are two for six on Fridays.", "Cash or card? Card. Everybody's card.").Next,
                c.Game, c.Hud, c.Player);

            Counter(c, root, new Vector3(113.2f, 1f, -2.4f), "Energy drink", 3.25m, clerk, "That'll keep you up through the close.", cup: true);
            Counter(c, root, new Vector3(115.6f, 1f, -2.4f), "Sandwich", 6.50m, clerk, "Want a bag? No? Cool.", cup: false);

            c.Place(new Vector3(doorX, 0f, f.yMin - 0.8f), PlaceKind.Door, "Corner Mart");
            c.Anchor("mart_front_out", new Vector3(doorX, 0f, f.yMin - 2f));
            c.Anchor("mart_counter", new Vector3(114.4f, 0f, -3.4f));
        }

        /// <summary>One-storey shop: walls with a storefront (door + display windows) on the south, interior finish, floor, ceiling.</summary>
        private static void ShopShell(CityContext c, Transform root, Rect f, float top, float t, Material outside, Material inside, Material floor,
            float doorX, float doorWidth, (float Center, float Width)[] windows)
        {
            Kit k = c.Kit;
            var openings = new List<Opening> { Opening.Door(doorX, doorWidth, 0f, 2.3f) };
            foreach (var (x, w) in windows) openings.Add(new Opening(x, w, 0.5f, 3.1f));
            k.WallX(root, "Front", f.xMin, f.xMax, f.yMin + t / 2f, 0f, top, t, outside, openings.ToArray());
            foreach (var (x, w) in windows)
                k.Pane(root, "Display window", new Vector3(x - w / 2f, 0.5f, f.yMin + t / 2f - 0.02f), new Vector3(x + w / 2f, 3.1f, f.yMin + t / 2f + 0.02f),
                    new Color(0.6f, 0.72f, 0.78f, 0.28f));
            k.WallX(root, "Back", f.xMin, f.xMax, f.yMax - t / 2f, 0f, top, t, outside);
            k.WallZ(root, "West", f.yMin, f.yMax, f.xMin + t / 2f, 0f, top, t, outside);
            k.WallZ(root, "East", f.yMin, f.yMax, f.xMax - t / 2f, 0f, top, t, outside);
            k.Span(root, "Floor", new Vector3(f.xMin + t, 0f, f.yMin + t), new Vector3(f.xMax - t, 0.02f, f.yMax - t), floor);
            k.Span(root, "Ceiling", new Vector3(f.xMin + t, top - 0.6f, f.yMin + t), new Vector3(f.xMax - t, top, f.yMax - t), inside, collider: false);
            // Interior skin so the inside doesn't read as the outside brick.
            k.Span(root, "Inner back", new Vector3(f.xMin + t, 0f, f.yMax - t - 0.02f), new Vector3(f.xMax - t, top - 0.6f, f.yMax - t), inside, collider: false);
            k.Span(root, "Inner west", new Vector3(f.xMin + t, 0f, f.yMin + t), new Vector3(f.xMin + t + 0.02f, top - 0.6f, f.yMax - t), inside, collider: false);
            k.Span(root, "Inner east", new Vector3(f.xMax - t - 0.02f, 0f, f.yMin + t), new Vector3(f.xMax - t, top - 0.6f, f.yMax - t), inside, collider: false);
        }

        private static void Counter(CityContext c, Transform root, Vector3 at, string item, decimal price, StaffNpc staff, string thanks, bool cup)
        {
            GameObject display = cup
                ? c.Kit.Cylinder(root, item, at + new Vector3(0f, 0.08f, 0f), 0.09f, 0.16f, c.P.Lit(new Color(0.92f, 0.9f, 0.86f)), collider: true)
                : c.Kit.Box(root, item, at + new Vector3(0f, 0.05f, 0f), new Vector3(0.25f, 0.1f, 0.18f), c.P.Lit(new Color(0.8f, 0.6f, 0.35f)));
            // A generous trigger around the item so it's easy to aim at.
            var aim = new GameObject(item + " (buy)");
            aim.transform.SetParent(c.Dynamic, false);
            aim.transform.position = at + new Vector3(0f, 0.15f, 0f);
            var box = aim.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.6f, 0.4f, 0.6f);
            aim.AddComponent<ShopCounter>().Configure(c.Game, c.Hud, staff, item, price, thanks);
        }

        /// <summary>Cycles through a few lines so repeated chats don't repeat immediately.</summary>
        private sealed class Rotation
        {
            private readonly string[] _lines;
            private int _i;

            public Rotation(params string[] lines) => _lines = lines;

            public string Next() => _lines[_i++ % _lines.Length];
        }
    }
}
