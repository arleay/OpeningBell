using OpeningBell.Casino;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>The hotel tower: its outside, the guest floor, the penthouse and the helipad.</summary>
    public static partial class MeridianHotel
    {
        /// <summary>The tower's footprint on the casino roof (local x, z).</summary>
        public const float TX0 = 21f, TX1 = 37f, TZ0 = 10f, TZ1 = 32f;
        private const float In = 0.3f;

        private static void Tower(CityContext c, Transform m, Transform tower, Transform dyn)
        {
            Kit k = c.Kit;
            // The outside: glass bands between the built floors (never entered, so no colliders), the name toward the
            // street, a bronze crown. Floors 5 and PH have their own walls inside.
            Material glass = c.P.Facade(FacadeStyle.Glass, false);
            Material roof = c.P.Lit(new Color(0.16f, 0.16f, 0.17f));
            k.Facade(tower, "Tower 3–4", V(TX0, Meridian.Height, TZ0), V(TX1, Guest - 0.02f, TZ1), glass, roof, collider: false);
            k.Facade(tower, "Tower 5", V(TX0, Guest - 0.02f, TZ0), V(TX1, Guest + 3.48f, TZ1), glass, roof, collider: false);
            k.Facade(tower, "Tower 6–8", V(TX0, Guest + 3.48f, TZ0), V(TX1, Penthouse - 0.02f, TZ1), glass, roof, collider: false);
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            k.Span(tower, "Crown", V(TX0 - 0.2f, Roof - 0.35f, TZ0 - 0.2f), V(TX1 + 0.2f, Roof, TZ1 + 0.2f), bronze, collider: false);
            TextMesh name = k.Text(tower, "THE MERIDIAN HOTEL", V((TX0 + TX1) / 2f, Penthouse - 2.2f, TZ0 - 0.06f), 0f, 1f, Color.white);
            name.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(1f, 0.85f, 0.55f));
        }

        // ---------------------------------------------------------------- the guest floor

        private static readonly (string Room, RoomClass Class, float Z0, float Z1)[] Rooms =
        {
            ("501", RoomClass.Standard, TZ0 + In, 17f),
            ("502", RoomClass.Suite, 17f, 24.2f),
            ("503", RoomClass.LuxurySuite, 24.2f, TZ1 - In),
        };
        private const float Corridor = 31.5f, ShaftSide = 34f;

        /// <summary>Slab with a hole for the elevator (x 34–36.7 at z 19.4–22.6).</summary>
        private static void Slab(Kit k, Transform t, float y, Material m)
        {
            k.Span(t, "Slab", V(TX0 + In, y - 0.22f, TZ0 + In), V(ShaftSide, y, TZ1 - In), m);
            k.Span(t, "Slab", V(ShaftSide, y - 0.22f, TZ0 + In), V(TX1 - In, y, MeridianUpstairs.ShaftZ0), m);
            k.Span(t, "Slab", V(ShaftSide, y - 0.22f, MeridianUpstairs.ShaftZ1), V(TX1 - In, y, TZ1 - In), m);
        }

        private static void GuestFloor(CityContext c, Transform t, Transform dyn)
        {
            Kit k = c.Kit;
            float y0 = Guest, y1 = Guest + 3.2f;
            Material wall = c.P.Surface(Finish.PaintedPlaster, new Color(0.8f, 0.76f, 0.7f), 0.05f);
            Material corridor = c.P.Lit(new Color(0.3f, 0.12f, 0.12f), 0.05f);
            Slab(k, t, y0, c.P.Lit(new Color(0.25f, 0.2f, 0.17f), 0.1f));
            k.Span(t, "Ceiling", V(TX0 + In, y1, TZ0 + In), V(TX1 - In, y1 + 0.1f, TZ1 - In), c.P.Bounce(new Color(0.9f, 0.88f, 0.84f), 0.15f), collider: false);
            k.Span(t, "Corridor carpet", V(Corridor, y0, TZ0 + In), V(ShaftSide, y0 + 0.02f, TZ1 - In), corridor, collider: false);

            // Outside walls with real windows (the facade's outer faces don't show from in here).
            var west = new System.Collections.Generic.List<Opening>();
            foreach (var r in Rooms) west.Add(new Opening((r.Z0 + r.Z1) / 2f, 3.2f, y0 + 0.8f, y0 + 2.6f));
            k.WallZ(t, "Outside wall", TZ0 + In, TZ1 - In, TX0 + In, y0, y1, 0.2f, wall, west.ToArray());
            foreach (var r in Rooms)
                k.Pane(t, "Window", V(TX0 + In - 0.02f, y0 + 0.8f, (r.Z0 + r.Z1) / 2f - 1.6f), V(TX0 + In + 0.02f, y0 + 2.6f, (r.Z0 + r.Z1) / 2f + 1.6f), new Color(0.6f, 0.7f, 0.8f, 0.25f));
            k.WallX(t, "Outside wall", TX0 + In, TX1 - In, TZ0 + In, y0, y1, 0.2f, wall, new Opening(26f, 3f, y0 + 0.8f, y0 + 2.6f));
            k.Pane(t, "Window", V(24.5f, y0 + 0.8f, TZ0 + In - 0.02f), V(27.5f, y0 + 2.6f, TZ0 + In + 0.02f), new Color(0.6f, 0.7f, 0.8f, 0.25f));
            k.WallX(t, "Outside wall", TX0 + In, TX1 - In, TZ1 - In, y0, y1, 0.2f, wall, new Opening(26f, 4f, y0 + 0.8f, y0 + 2.6f));
            k.Pane(t, "Window", V(24f, y0 + 0.8f, TZ1 - In - 0.02f), V(28f, y0 + 2.6f, TZ1 - In + 0.02f), new Color(0.6f, 0.7f, 0.8f, 0.25f));
            k.WallZ(t, "Outside wall", TZ0 + In, TZ1 - In, TX1 - In, y0, y1, 0.2f, wall);

            // The corridor: rooms to the west, the elevator to the east.
            var doors = new System.Collections.Generic.List<Opening>();
            foreach (var r in Rooms) doors.Add(Opening.Door(DoorZ(r.Z0, r.Z1), 1f, y0, y0 + 2.2f));
            k.WallZ(t, "Corridor wall", TZ0 + In, TZ1 - In, Corridor, y0, y1, 0.15f, wall, doors.ToArray());
            k.WallZ(t, "Shaft wall", TZ0 + In, TZ1 - In, ShaftSide, y0, y1, 0.15f, wall, new Opening(LiftAt.z, 2.8f, y0, y0 + 2.6f));
            k.WallX(t, "Room wall", TX0 + In, Corridor, 17f, y0, y1, 0.15f, wall);
            k.WallX(t, "Room wall", TX0 + In, Corridor, 24.2f, y0, y1, 0.15f, wall);
            Material door = c.P.Lit(new Color(0.28f, 0.17f, 0.1f), 0.45f);
            foreach (var r in Rooms)
            {
                float dz = DoorZ(r.Z0, r.Z1);
                string room = r.Room;
                Door d = c.SwingDoor(t, "room " + room, V(Corridor, y0, dz + 0.5f), 1f, 2.2f, door, 90f);
                d.LockReason = () => c.Game.Casino.Hotel.HasKey(room, c.Game.Clock.Now) ? null : $"locked (room {room}: key card needed)";
                k.Text(t, room, V(Corridor + 0.09f, y0 + 1.6f, dz - 0.75f), -90f, 0.09f, new Color(0.85f, 0.7f, 0.4f));
                Room(c, t, dyn, room, r.Class, r.Z0, r.Z1, y0);
                c.Anchor("hotel_" + room, t.TransformPoint(V(32.7f, y0, dz)));
            }
            k.Real(t, "potted_plant_04", V(33.4f, y0, TZ0 + 0.8f), 0f, 1.1f);
            k.Real(t, "potted_plant_04", V(33.4f, y0, TZ1 - 0.8f), 0f, 1.1f);
            k.Text(t, "501 – 503 ·  ELEVATORS", V(ShaftSide - 0.09f, y0 + 1.7f, 17.5f), 90f, 0.08f, new Color(0.3f, 0.2f, 0.1f));
            for (float z = 12f; z < TZ1; z += 6f) c.PointLight(t, V(32.7f, y1 - 0.4f, z), 6f, 0.8f, new Color(1f, 0.93f, 0.82f));
        }

        private static float DoorZ(float z0, float z1) => z0 + 1.2f;

        /// <summary>
        /// A hotel room between <paramref name="z0"/> and <paramref name="z1"/>, the corridor on its east side: bed and
        /// nightstands, a desk with a laptop you can trade from, a TV, a wardrobe, a bathroom with a shower (a tub in
        /// the luxury suite), the room phone for room service; suites add a sofa, the luxury suite a dining table.
        /// </summary>
        private static void Room(CityContext c, Transform t, Transform dyn, string room, RoomClass cls, float z0, float z1, float y)
        {
            Kit k = c.Kit;
            Material wall = c.P.Surface(Finish.PaintedPlaster, new Color(0.8f, 0.76f, 0.7f), 0.05f);
            Material blanket = c.P.Lit(cls == RoomClass.Standard ? new Color(0.85f, 0.83f, 0.78f) : new Color(0.25f, 0.3f, 0.45f), 0.1f);
            k.Span(t, "Room floor", V(TX0 + In + 0.1f, y, z0 + 0.08f), V(Corridor - 0.08f, y + 0.02f, z1 - 0.08f),
                c.P.Surface(Finish.WoodFloor, new Color(0.45f, 0.32f, 0.22f), 0.3f), collider: false);

            // Bathroom in the corner by the door: shower (and a tub in 503), toilet, basin, mirror.
            float bx = Corridor - 2.8f, bz = z1 - 2.6f;
            k.WallX(t, "Bathroom wall", bx, Corridor, bz, y, y + 2.6f, 0.1f, wall, Opening.Door(bx + 0.7f, 0.8f, y, y + 2.1f));
            k.WallZ(t, "Bathroom wall", bz, z1, bx, y, y + 2.6f, 0.1f, wall);
            k.Span(t, "Bath tiles", V(bx, y + 0.021f, bz), V(Corridor - 0.1f, y + 0.03f, z1 - 0.1f), c.P.Surface(Finish.Tiles, new Color(0.9f, 0.9f, 0.9f), 0.5f), collider: false);
            GameObject shower = k.Fit(t, cls == RoomClass.LuxurySuite ? "bathtub" : "shower", V(Corridor - 0.6f, y, z1 - 0.7f), V(0f, cls == RoomClass.LuxurySuite ? 0.6f : 2f, 0f), 180f);
            k.Fit(t, "toilet", V(bx + 0.45f, y, z1 - 0.5f), V(0f, 0.8f, 0f), 180f);
            k.Fit(t, "bathroomSink", V(bx + 1.35f, y, z1 - 0.35f), V(0.55f, 0f, 0f), 180f);
            k.Span(t, "Mirror", V(bx + 1f, y + 1.1f, z1 - 0.13f), V(bx + 1.7f, y + 1.9f, z1 - 0.11f), c.P.Metal(new Color(0.85f, 0.87f, 0.9f), 0.95f), collider: false);
            Fitting(t, V(Corridor - 0.6f, y + 1.2f, z1 - 0.7f), V(1f, 1.5f, 1f), cls == RoomClass.LuxurySuite ? "Run a bath" : "Take a shower", () =>
            {
                c.Game.Casino.Drinks.Recover(1, c.Game.Clock.Now);
                c.Hud.ShowToast(cls == RoomClass.LuxurySuite ? "A long hot bath. You feel clearer." : "A hot shower. You feel more awake.", 3f);
            }, () => c.Game.Casino.Hotel.HasKey(room, c.Game.Clock.Now));

            // The bed against the inner wall, nightstands, lamps; the sleep spot beside it.
            float bedZ = cls == RoomClass.Standard ? z0 + 1.3f : z0 + 1.4f, bedX = 25f;
            GameObject bed = k.Fit(t, "bedDouble", V(bedX, y, bedZ), V(cls == RoomClass.Standard ? 1.7f : 2f, 0f, 2.2f), 0f, stretch: true);
            if (bed != null) Kit.Recolor(bed, "carpet", blanket);
            foreach (float nx in new[] { bedX - 1.35f, bedX + 1.35f })
            {
                k.Fit(t, "cabinetBedDrawerTable", V(nx, y, z0 + 0.35f), V(0.5f, 0f, 0f), 0f);
                k.Fit(t, "lampRoundTable", V(nx, y + 0.52f, z0 + 0.35f), V(0f, 0.45f, 0f), 0f);
            }
            Transform wake = Kit.Group(dyn, "Wake " + room, V(bedX + 1.7f, y, bedZ + 1.2f), 0f);
            var bedTap = new GameObject("Bed " + room);
            bedTap.transform.SetParent(dyn, false);
            bedTap.transform.localPosition = V(bedX, y + 0.6f, bedZ);
            var bedBox = bedTap.AddComponent<BoxCollider>();
            bedBox.isTrigger = true;
            bedBox.size = V(1.8f, 0.5f, 2.1f);
            bedTap.AddComponent<HotelBed>().Configure(c.Game, room, wake);

            // Room phone on the nightstand: room service.
            var service = new GameObject("Room service " + room).AddComponent<RoomService>();
            service.transform.SetParent(dyn, false);
            GameObject tray = k.Box(dyn, "Room service tray", V(27.8f, y + 0.78f, z1 - 3.4f), V(0.5f, 0.04f, 0.35f), c.P.Metal(new Color(0.8f, 0.8f, 0.82f), 0.8f), collider: false);
            k.Cylinder(tray.transform, "Cloche", V(0f, 3f, 0f), 0.6f, 4f, c.P.Metal(new Color(0.85f, 0.85f, 0.87f), 0.9f));
            service.Configure(c.Game, c.Hud, tray);
            k.Box(t, "Room phone", V(bedX + 1.35f, y + 0.56f, z0 + 0.25f), V(0.18f, 0.06f, 0.14f), c.P.Lit(new Color(0.1f, 0.1f, 0.1f), 0.5f), collider: false);
            var roomService = new MenuVenue { Name = "Room service", Where = "Room phone", Items = CasinoMenus.RoomService, Delivered = service.Ordered };
            Meridian.Counter(dyn, V(bedX + 1.35f, y + 0.6f, z0 + 0.3f), V(0.4f, 0.3f, 0.4f), roomService,
                () => c.Game.Casino.Hotel.HasKey(room, c.Game.Clock.Now) ? null : "Room phone");

            // The desk under the window with a laptop: the trading terminal, same as any desk.
            k.Box(t, "Room table", V(27.8f, y + 0.37f, z1 - 3.4f), V(1.1f, 0.74f, 0.7f), c.P.Lit(new Color(0.3f, 0.2f, 0.12f), 0.4f));
            Desk(c, t, dyn, V(TX0 + In + 0.55f, y, (z0 + z1) / 2f - 1.3f), -90f, room); // facing the window (west)

            // Wardrobe: change your top.
            k.Box(t, "Wardrobe", V(bx - 0.9f, y + 1f, z1 - 0.35f), V(1.2f, 2f, 0.6f), c.P.Lit(new Color(0.3f, 0.2f, 0.12f), 0.4f));
            Fitting(t, V(bx - 0.9f, y + 1f, z1 - 0.75f), V(1.2f, 1.8f, 0.3f), "Change clothes", () => ChangeClothes(c), () => c.Game.Casino.Hotel.HasKey(room, c.Game.Clock.Now));
            k.Fit(t, "televisionModern", V(bedX, y + 0.8f, z1 - 0.2f), V(1.2f, 0f, 0f), 180f);
            k.Box(t, "TV cabinet", V(bedX, y + 0.4f, z1 - 0.3f), V(1.6f, 0.8f, 0.45f), c.P.Lit(new Color(0.3f, 0.2f, 0.12f), 0.4f));
            if (cls != RoomClass.Standard)
            {
                Meridian.Solid(k, k.Real(t, "sofa_03", V(22.6f, y, (z0 + z1) / 2f + 1.3f), 90f));
                k.Real(t, "coffee_table_round_01", V(23.9f, y, (z0 + z1) / 2f + 1.3f), 0f);
            }
            if (cls == RoomClass.LuxurySuite)
            {
                k.Real(t, "round_wooden_table_01", V(28.2f, y, z0 + 1.6f), 0f);
                k.Real(t, "dining_chair_02", V(27.4f, y, z0 + 1.6f), 90f);
                k.Real(t, "dining_chair_02", V(29f, y, z0 + 1.6f), -90f);
                k.Prop(t, "food_sm_wine_bottle", V(28.2f, y + 0.76f, z0 + 1.6f), 0.3f);
            }
            k.Real(t, "mid_century_lounge_chair", V(22.4f, y, z1 - 1f), 135f);
            c.PointLight(t, V(26f, y + 2.8f, (z0 + z1) / 2f), 7f, 1f, new Color(1f, 0.92f, 0.8f));
        }

        /// <summary>A trading desk: laptop screen, chair, the seat view (the terminal on the screen, like the office desk).</summary>
        private static void Desk(CityContext c, Transform t, Transform dyn, Vector3 at, float yaw, string room)
        {
            Kit k = c.Kit;
            Transform desk = Kit.Group(dyn, "Laptop desk " + room, t.TransformPoint(at), t.eulerAngles.y + yaw);
            k.Box(desk, "Desk", V(0f, 0.37f, 0f), V(1.2f, 0.74f, 0.6f), c.P.Lit(new Color(0.3f, 0.2f, 0.12f), 0.4f));
            k.Box(desk, "Laptop base", V(0f, 0.755f, -0.05f), V(0.36f, 0.02f, 0.25f), c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.6f), collider: false);
            k.Box(desk, "Laptop lid", V(0f, 0.87f, 0.08f), V(0.36f, 0.24f, 0.015f), c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.6f), collider: false);
            GameObject screen = k.Quad(desk, "Screen", V(0f, 0.87f, 0.071f), new Vector2(0.33f, 0.2f), 0f, new Material(c.P.Unlit(Color.black)));
            k.Fit(desk, "chairDesk", V(0f, 0f, -0.75f), V(0.55f, 0f, 0f), 180f);
            Transform view = Kit.Group(desk, "Seat view", V(0f, 1.12f, -0.62f));
            view.localRotation = Quaternion.Euler(12f, 0f, 0f);
            Transform stand = Kit.Group(desk, "Stand point", V(0f, 0f, -1.2f));
            var d = desk.gameObject.AddComponent<OpeningBell.Gameplay.Desk>();
            d.Configure(c.Workstation, view, stand, screen.GetComponent<Renderer>());
            var seat = new GameObject("Laptop seat");
            seat.transform.SetParent(desk, false);
            seat.transform.localPosition = V(0f, 0.8f, -0.1f);
            var box = seat.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = V(0.5f, 0.4f, 0.4f);
            seat.AddComponent<SeatInteractable>().Configure(c.Workstation, d, "Use the laptop");
        }

        private static void Fitting(Transform t, Vector3 at, Vector3 size, string prompt, System.Action use, System.Func<bool> usable)
        {
            var go = new GameObject(prompt);
            go.transform.SetParent(t, false);
            go.transform.localPosition = at;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            go.AddComponent<RoomFitting>().Configure(prompt, use, usable);
        }

        /// <summary>The wardrobe: the next top colour in the set (§50), saved with your look.</summary>
        private static void ChangeClothes(CityContext c)
        {
            OpeningBell.PlayerLook look = c.Game.Look;
            if (look == null) return;
            look.Top = look.Top % CharacterStyle.Tops.Length + 1;
            PlayerBody body = Object.FindAnyObjectByType<PlayerBody>();
            if (body != null) body.SetLook(look);
            c.Hud.ShowToast("Fresh clothes from the wardrobe.", 2.5f);
        }

        // ---------------------------------------------------------------- the penthouse

        private static void PenthouseFloor(CityContext c, Transform t, Transform dyn)
        {
            // The whole top floor (§51): glass all round, living room, dining, a bar, an office desk with screens, the
            // bedroom suite; the private elevator opens straight into it.
            Kit k = c.Kit;
            float y0 = Penthouse, y1 = Roof - 0.35f;
            Slab(k, t, y0, c.P.Lit(new Color(0.25f, 0.2f, 0.17f), 0.1f));
            k.Span(t, "Penthouse floor", V(TX0 + In, y0, TZ0 + In), V(TX1 - In, y0 + 0.02f, TZ1 - In),
                c.P.Surface(Finish.WoodFloor, new Color(0.55f, 0.42f, 0.3f), 0.45f), collider: false);
            k.Span(t, "Penthouse ceiling", V(TX0, y1, TZ0), V(TX1, y1 + 0.1f, TZ1), c.P.Bounce(new Color(0.92f, 0.9f, 0.86f), 0.2f), collider: false);
            Color tint = new Color(0.6f, 0.72f, 0.8f, 0.2f);
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            k.Pane(t, "Glass", V(TX0, y0, TZ0), V(TX0 + 0.05f, y1, TZ1), tint);
            k.Pane(t, "Glass", V(TX1 - 0.05f, y0, TZ0), V(TX1, y1, TZ1), tint);
            k.Pane(t, "Glass", V(TX0, y0, TZ0), V(TX1, y1, TZ0 + 0.05f), tint);
            k.Pane(t, "Glass", V(TX0, y0, TZ1 - 0.05f), V(TX1, y1, TZ1), tint);
            for (float x = TX0; x <= TX1 + 0.01f; x += 2f)
            {
                k.Box(t, "Mullion", V(x, (y0 + y1) / 2f, TZ0), V(0.08f, y1 - y0, 0.1f), bronze, collider: false);
                k.Box(t, "Mullion", V(x, (y0 + y1) / 2f, TZ1), V(0.08f, y1 - y0, 0.1f), bronze, collider: false);
            }
            for (float z = TZ0; z <= TZ1 + 0.01f; z += 2f)
            {
                k.Box(t, "Mullion", V(TX0, (y0 + y1) / 2f, z), V(0.1f, y1 - y0, 0.08f), bronze, collider: false);
                k.Box(t, "Mullion", V(TX1, (y0 + y1) / 2f, z), V(0.1f, y1 - y0, 0.08f), bronze, collider: false);
            }

            // Living room: sofas facing across a table, lounge chairs, rug, TV, chandelier.
            k.Span(t, "Rug", V(22f, y0 + 0.02f, 12.5f), V(28f, y0 + 0.035f, 20f), c.P.Lit(new Color(0.75f, 0.7f, 0.62f), 0.05f), collider: false);
            Meridian.Solid(k, k.Real(t, "sofa_03", V(25f, y0, 13.8f), 0f));
            Meridian.Solid(k, k.Real(t, "sofa_03", V(25f, y0, 18.6f), 180f));
            k.Real(t, "coffee_table_round_01", V(25f, y0, 16.2f), 0f);
            Meridian.Solid(k, k.Real(t, "mid_century_lounge_chair", V(22.6f, y0, 16.2f), 90f));
            k.Real(t, "Ottoman_01", V(27.5f, y0, 16.2f), 0f);
            k.Fit(t, "televisionModern", V(29.4f, y0 + 0.5f, 16.2f), V(0f, 0f, 1.6f), -90f);
            Meridian.Hang(c, t, "Chandelier_03", V(25f, 0f, 16.2f), 1.3f, y1);
            Meridian.Chair(c, dyn, V(25f, y0, 13.8f), 0f, "Sit on the sofa", 0.95f, null);
            k.Real(t, "potted_plant_04", V(21.8f, y0, 10.8f), 0f, 1.3f);
            k.Real(t, "anthurium_botany_01", V(21.8f, y0, 23.5f), 0f, 1.3f);

            // Dining for six.
            k.Real(t, "round_wooden_table_01", V(26f, y0, 22.5f), 0f, 1.3f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                Vector3 p = V(26f, y0, 22.5f) + Quaternion.Euler(0f, a, 0f) * V(0f, 0f, 1.05f);
                k.Real(t, "dining_chair_02", p, a + 180f);
            }

            // The bar, stocked and on the house for the penthouse guest.
            Material wood = c.P.Lit(new Color(0.2f, 0.1f, 0.06f), 0.5f);
            k.Span(t, "Penthouse bar", V(29.5f, y0, 11f), V(33.5f, y0 + 1.08f, 11.8f), wood);
            k.Span(t, "Bar top", V(29.4f, y0 + 1.08f, 10.9f), V(33.6f, y0 + 1.14f, 11.9f), c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.85f), collider: false);
            for (float x = 29.8f; x < 33.4f; x += 0.35f) k.Prop(t, "food_sm_wine_bottle", V(x, y0 + 1.14f, 11.1f), 0.3f);
            for (float x = 30.2f; x < 33.4f; x += 1.3f) k.Real(t, "bar_chair_round_01", V(x, y0, 12.5f), 180f);
            var bar = new MenuVenue { Name = "Penthouse bar", Where = "Pour", Items = CasinoMenus.VipBar, Complimentary = true };
            Meridian.Counter(dyn, V(31.5f, y0 + 1.25f, 11.4f), V(4f, 0.3f, 0.8f), bar, () => PenthouseGuest(c) ? null : "Penthouse bar (guests only)");

            // The office: a wide desk facing the west glass with the trading terminal.
            k.Span(t, "Office rug", V(21.8f, y0 + 0.02f, 25.5f), V(25.5f, y0 + 0.035f, 30.8f), c.P.Lit(new Color(0.2f, 0.18f, 0.16f), 0.05f), collider: false);
            Desk(c, t, dyn, V(22.3f, y0, 28f), -90f, "PH");

            // The bedroom: partitioned off in the north-east corner, the bed facing the glass, a bath by the window.
            Material wall = c.P.Surface(Finish.PaintedPlaster, new Color(0.85f, 0.82f, 0.76f), 0.05f);
            k.WallX(t, "Bedroom wall", 29f, TX1 - 0.05f, 24f, y0, y1, 0.15f, wall, Opening.Door(30.5f, 1.3f, y0, y0 + 2.4f));
            k.WallZ(t, "Bedroom wall", 24f, TZ1 - 0.05f, 29f, y0, y1, 0.15f, wall);
            GameObject bed = k.Fit(t, "bedDouble", V(33f, y0, 27.2f), V(2.2f, 0f, 2.3f), 0f, stretch: true);
            if (bed != null) Kit.Recolor(bed, "carpet", c.P.Lit(new Color(0.9f, 0.88f, 0.84f), 0.1f));
            k.Fit(t, "bathtub", V(30.6f, y0, 30.6f), V(0f, 0.6f, 0f), 90f);
            k.Box(t, "Wardrobe", V(36.3f, y0 + 1f, 30.5f), V(0.6f, 2f, 1.6f), wood);
            Fitting(t, V(35.8f, y0 + 1f, 30.5f), V(0.4f, 1.8f, 1.4f), "Change clothes", () => ChangeClothes(c), () => PenthouseGuest(c));
            Fitting(t, V(30.6f, y0 + 0.8f, 30.6f), V(1.8f, 1f, 1f), "Run a bath", () =>
            {
                c.Game.Casino.Drinks.Recover(1, c.Game.Clock.Now);
                c.Hud.ShowToast("A bath with the whole town below. You feel clearer.", 3f);
            }, () => PenthouseGuest(c));
            Transform wake = Kit.Group(dyn, "Wake PH", V(34.8f, y0, 25.2f), 0f);
            var bedTap = new GameObject("Bed PH");
            bedTap.transform.SetParent(dyn, false);
            bedTap.transform.localPosition = V(33f, y0 + 0.6f, 27.2f);
            var bedBox = bedTap.AddComponent<BoxCollider>();
            bedBox.isTrigger = true;
            bedBox.size = V(2.2f, 0.5f, 2.3f);
            bedTap.AddComponent<HotelBed>().Configure(c.Game, "PH", wake);

            c.PointLight(t, V(25f, y1 - 0.5f, 16f), 10f, 1.2f, new Color(1f, 0.92f, 0.8f));
            c.PointLight(t, V(31.5f, y1 - 0.5f, 13f), 7f, 1f, new Color(1f, 0.85f, 0.65f));
            c.PointLight(t, V(24f, y1 - 0.5f, 27f), 8f, 1f, new Color(1f, 0.92f, 0.8f));
            c.PointLight(t, V(33f, y1 - 0.5f, 28f), 8f, 0.9f, new Color(1f, 0.9f, 0.75f));
            c.Anchor("hotel_ph", t.TransformPoint(V(31f, y0, 18f)));
        }

        // ---------------------------------------------------------------- the roof

        private static void RoofTop(CityContext c, Transform t, Transform dyn)
        {
            // A helipad on the tower roof (§70): somewhere for a helicopter to come to, once the town has them.
            Kit k = c.Kit;
            float y = Roof;
            Material deck = c.P.Surface(Finish.Concrete, new Color(0.35f, 0.35f, 0.36f), 0.2f);
            k.Span(t, "Roof deck", V(TX0, y - 0.3f, TZ0), V(ShaftSide - 0.1f, y, TZ1), deck);
            k.Span(t, "Roof deck", V(ShaftSide - 0.1f, y - 0.3f, TZ0), V(TX1, y, MeridianUpstairs.ShaftZ0 - 0.2f), deck);
            k.Span(t, "Roof deck", V(ShaftSide - 0.1f, y - 0.3f, MeridianUpstairs.ShaftZ1 + 0.2f), V(TX1, y, TZ1), deck);
            Material parapet = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.2f);
            k.Span(t, "Parapet", V(TX0, y, TZ0), V(TX1, y + 1.1f, TZ0 + 0.2f), parapet);
            k.Span(t, "Parapet", V(TX0, y, TZ1 - 0.2f), V(TX1, y + 1.1f, TZ1), parapet);
            k.Span(t, "Parapet", V(TX0, y, TZ0), V(TX0 + 0.2f, y + 1.1f, TZ1), parapet);
            k.Span(t, "Parapet", V(TX1 - 0.2f, y, TZ0), V(TX1, y + 1.1f, TZ1), parapet);

            // The pad: a dark circle, a yellow ring, a big H, lights round the edge.
            Vector3 pad = V(27.5f, y, 21f);
            k.Cylinder(t, "Helipad", pad + V(0f, 0.02f, 0f), 11f, 0.04f, c.P.Lit(new Color(0.12f, 0.13f, 0.14f), 0.2f));
            var ring = new MeshBuilder();
            for (int i = 0; i < 48; i++)
            {
                float a0 = i * Mathf.PI * 2f / 48f, a1 = (i + 1) * Mathf.PI * 2f / 48f;
                Vector3 P(float a, float r) => pad + V(Mathf.Cos(a) * r, 0.045f, Mathf.Sin(a) * r);
                ring.Quad(P(a0, 4.8f), P(a1, 4.8f), P(a1, 4.4f), P(a0, 4.4f));
            }
            ring.Build(t, "Pad ring", c.P.Lit(new Color(0.95f, 0.8f, 0.1f), 0.2f), false);
            Material white = c.P.Lit(new Color(0.95f, 0.95f, 0.95f), 0.2f);
            k.Box(t, "H", pad + V(-1f, 0.05f, 0f), V(0.4f, 0.01f, 3f), white, collider: false);
            k.Box(t, "H", pad + V(1f, 0.05f, 0f), V(0.4f, 0.01f, 3f), white, collider: false);
            k.Box(t, "H", pad + V(0f, 0.05f, 0f), V(2f, 0.01f, 0.4f), white, collider: false);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                k.Cylinder(t, "Pad light", pad + V(Mathf.Cos(a) * 5.4f, 0.08f, Mathf.Sin(a) * 5.4f), 0.14f, 0.12f, c.P.Glow(new Color(0.3f, 1f, 0.4f), 2f));
            }
            // Windsock.
            k.Cylinder(t, "Windsock pole", V(35.5f, y + 2f, 12f), 0.08f, 4f, c.P.Lit(new Color(0.7f, 0.7f, 0.72f), 0.5f));
            k.Box(t, "Windsock", V(35f, y + 3.8f, 12f), V(1f, 0.35f, 0.35f), c.P.Lit(new Color(1f, 0.45f, 0.1f), 0.2f), collider: false);

            // The housing over the elevator, open toward the pad.
            k.Span(t, "Lift housing", V(ShaftSide - 0.1f, y, MeridianUpstairs.ShaftZ0 - 0.2f), V(TX1, y + 3f, MeridianUpstairs.ShaftZ0), parapet);
            k.Span(t, "Lift housing", V(ShaftSide - 0.1f, y, MeridianUpstairs.ShaftZ1), V(TX1, y + 3f, MeridianUpstairs.ShaftZ1 + 0.2f), parapet);
            k.Span(t, "Lift housing roof", V(ShaftSide - 0.1f, y + 3f, MeridianUpstairs.ShaftZ0 - 0.2f), V(TX1, y + 3.2f, MeridianUpstairs.ShaftZ1 + 0.2f), parapet);
            k.Text(t, "HELIPAD · THE MERIDIAN", V(ShaftSide - 0.15f, y + 2.8f, 21f), 90f, 0.18f, new Color(0.95f, 0.85f, 0.55f));
            c.PointLight(t, pad + V(0f, 4f, 0f), 12f, 1.2f, new Color(1f, 0.95f, 0.9f));
            c.Anchor("hotel_roof", t.TransformPoint(V(28f, y, 26f)));
        }
    }
}
