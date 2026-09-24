using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The downtown office building (spec §3–§6): stone base with a glass lobby, reception desk and receptionist,
    /// mailboxes, directory, restroom, elevator and stairs up to floor 2, where the player can lease Suite 204
    /// (a starter office with one desk). Floors 3+ are outside-only. Lobby opens 6 AM–9 PM; tenants' key cards
    /// work any time.
    /// </summary>
    public static class CalderBuilding
    {
        // World coordinates. Footprint x 144..168, z -5.5..18.5; walls 0.3 thick.
        private const float X0 = 144f, X1 = 168f, Z0 = -5.5f, Z1 = 18.5f;
        private const float Wall = 0.3f;
        private const float Floor2 = 4.8f;      // walking surface of floor 2
        private const float Floor2Top = 8.3f;   // underside of the upper volume
        private const float Height = 26f;
        private const float CoreZ = 8f;         // lobby / corridor north wall
        private const float ElevatorX = 156f;
        private const float StairDoorGround = 161.2f, StairDoorUpper = 164.4f;
        private const float SuiteWallZ = 4.8f;
        public static readonly Hours LobbyHours = Hours.Of(6, 21);

        public static void Build(CityContext c, out Desk officeDesk)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Calder Building");
            Transform dyn = Kit.Group(c.Dynamic, "Calder Building");
            Material stone = c.P.Surface(Finish.Concrete, new Color(0.7f, 0.68f, 0.63f), 0.15f);
            Material plaster = c.P.Surface(Finish.Plaster, new Color(0.84f, 0.8f, 0.72f), 0.05f);
            Material marble = c.P.Lit(new Color(0.5f, 0.47f, 0.43f), 0.6f);
            Material carpet = c.P.Lit(new Color(0.27f, 0.3f, 0.35f), 0.02f);
            Material ceiling = c.P.Lit(new Color(0.92f, 0.92f, 0.9f));
            Material wood = c.P.Lit(new Color(0.3f, 0.2f, 0.13f), 0.35f);
            Material metal = c.P.Lit(new Color(0.55f, 0.56f, 0.58f), 0.7f);
            Material tinted = c.P.Lit(new Color(0.16f, 0.2f, 0.26f), 0.85f);
            Color glassTint = new Color(0.62f, 0.75f, 0.82f, 0.28f);

            // ---- ground floor shell: stone with a glass front ----
            const float groundTop = 4.5f;
            k.WallX(root, "Front", X0, X1, Z0 + Wall / 2f, 0f, groundTop, Wall, stone,
                new Opening(149.5f, 8.6f, 0.45f, 3.6f), Opening.Door(ElevatorX, 2.4f, 0f, 2.7f), new Opening(162.5f, 8.6f, 0.45f, 3.6f));
            k.Pane(root, "Lobby glass W", new Vector3(145.2f, 0.45f, Z0 + 0.13f), new Vector3(153.8f, 3.6f, Z0 + 0.17f), glassTint);
            k.Pane(root, "Lobby glass E", new Vector3(158.2f, 0.45f, Z0 + 0.13f), new Vector3(166.8f, 3.6f, Z0 + 0.17f), glassTint);
            k.Box(root, "Transom", new Vector3(ElevatorX, 2.85f, Z0 + 0.15f), new Vector3(2.4f, 0.3f, 0.1f), metal, collider: false);
            k.WallX(root, "Back", X0, X1, Z1 - Wall / 2f, 0f, groundTop, Wall, stone);
            k.WallZ(root, "West", Z0, Z1, X0 + Wall / 2f, 0f, groundTop, Wall, stone);
            k.WallZ(root, "East", Z0, Z1, X1 - Wall / 2f, 0f, groundTop, Wall, stone);
            k.Span(root, "Lobby floor", new Vector3(X0 + Wall, 0f, Z0 + Wall), new Vector3(X1 - Wall, 0.02f, Z1 - Wall), marble);
            k.Span(root, "Lobby ceiling", new Vector3(X0 + Wall, 4.2f, Z0 + Wall), new Vector3(X1 - Wall, 4.5f, CoreZ), ceiling, collider: false);

            // Canopy and sign over the entrance.
            k.Box(root, "Canopy", new Vector3(ElevatorX, 3.2f, Z0 - 0.9f), new Vector3(4.4f, 0.14f, 1.8f), c.P.Lit(new Color(0.18f, 0.19f, 0.2f), 0.5f), collider: false);
            foreach (float dx in new[] { -1.2f, 1.2f })
                k.Box(root, "Canopy light", new Vector3(ElevatorX + dx, 3.12f, Z0 - 0.9f), new Vector3(0.3f, 0.04f, 0.3f),
                    c.P.Lamp(new Color(0.55f, 0.55f, 0.5f), new Color(1f, 0.9f, 0.7f)), collider: false);
            k.Box(root, "Sign band", new Vector3(ElevatorX, 3.95f, Z0 - 0.04f), new Vector3(7.5f, 0.8f, 0.08f), c.P.Lit(new Color(0.16f, 0.14f, 0.12f), 0.5f), collider: false);
            k.Text(root, "CALDER BUILDING", new Vector3(ElevatorX, 3.95f, Z0 - 0.09f), 0f, 0.46f, new Color(0.9f, 0.76f, 0.45f));
            k.Text(root, "400 MAPLE", new Vector3(163.5f, 3.95f, Z0 - 0.02f), 0f, 0.18f, new Color(0.25f, 0.24f, 0.22f));

            // Panels run just inside the wall so they slide behind the stone piers.
            Door entrance = c.SlidingDoor(root, "lobby doors", new Vector3(ElevatorX, 0.02f, Z0 + Wall + 0.05f), 2.4f, 2.66f);
            entrance.LockReason = () => c.IsTenant || LobbyHours.Contains(c.Game.Clock.Now) ? null : "closed (opens 6 AM, tenants use a key card)";
            k.Text(root, "OPEN 6 AM – 9 PM", new Vector3(ElevatorX + 1.6f, 2.2f, Z0 - 0.02f), 0f, 0.07f, new Color(0.9f, 0.9f, 0.88f));

            // ---- lobby furnishings ----
            Transform desk = Kit.Group(root, "Reception", new Vector3(ElevatorX, 0.02f, 1.7f));
            k.Box(desk, "Front", new Vector3(0f, 0.55f, 0f), new Vector3(5f, 1.1f, 0.35f), wood);
            k.Box(desk, "Top", new Vector3(0f, 1.12f, 0.2f), new Vector3(5.1f, 0.05f, 0.8f), marble, collider: false);
            k.Box(desk, "Return W", new Vector3(-2.35f, 0.55f, 0.6f), new Vector3(0.3f, 1.1f, 1.2f), wood);
            k.Box(desk, "Return E", new Vector3(2.35f, 0.55f, 0.6f), new Vector3(0.3f, 1.1f, 1.2f), wood);
            k.Box(desk, "Work top", new Vector3(0f, 0.75f, 0.55f), new Vector3(4.2f, 0.04f, 0.6f), wood, collider: false);
            k.Box(desk, "Monitor", new Vector3(-0.6f, 1.05f, 0.62f), new Vector3(0.55f, 0.34f, 0.04f), c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.6f), collider: false);
            k.Box(desk, "Phone", new Vector3(0.7f, 0.8f, 0.5f), new Vector3(0.2f, 0.07f, 0.16f), c.P.Lit(new Color(0.1f, 0.1f, 0.1f)), collider: false);
            k.Text(desk, "RECEPTION", new Vector3(0f, 0.72f, -0.19f), 0f, 0.12f, new Color(0.85f, 0.75f, 0.5f));

            Material couch = c.P.Lit(new Color(0.22f, 0.26f, 0.3f), 0.1f);
            foreach (float z in new[] { -2.8f, 1.2f })
            {
                Transform sofa = Kit.Group(root, "Sofa", new Vector3(147.3f, 0.02f, z), z < 0f ? 0f : 180f);
                k.Box(sofa, "Seat", new Vector3(0f, 0.25f, 0f), new Vector3(2.2f, 0.45f, 0.85f), couch);
                k.Box(sofa, "Back", new Vector3(0f, 0.65f, -0.35f), new Vector3(2.2f, 0.5f, 0.2f), couch, collider: false);
            }
            k.Box(root, "Coffee table", new Vector3(147.3f, 0.23f, -0.8f), new Vector3(1.2f, 0.42f, 0.7f), marble);
            Plant(c, root, new Vector3(145.2f, 0.02f, -4.5f));
            Plant(c, root, new Vector3(166.8f, 0.02f, -4.5f));
            Plant(c, root, new Vector3(152.8f, 0.02f, 7.3f));

            Transform boxes = Kit.Group(root, "Mailboxes", new Vector3(X1 - Wall - 0.12f, 0.02f, 0.5f), 90f);
            k.Box(boxes, "Cabinet", new Vector3(0f, 1.3f, 0f), new Vector3(2.4f, 1f, 0.24f), metal, collider: false);
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 8; col++)
                k.Box(boxes, "Box", new Vector3(-1.05f + col * 0.3f, 0.93f + row * 0.24f, -0.13f), new Vector3(0.26f, 0.2f, 0.02f),
                    c.P.Lit(new Color(0.66f, 0.62f, 0.5f), 0.7f), collider: false);
            k.Text(boxes, "MAIL", new Vector3(0f, 1.95f, -0.13f), 0f, 0.14f, new Color(0.3f, 0.3f, 0.3f));

            // Lights.
            foreach (Vector3 p in new[] { new Vector3(149f, 3.9f, -1f), new Vector3(156f, 3.9f, 3f), new Vector3(163f, 3.9f, -1f) })
            {
                c.PointLight(root, p, 11f, 1.1f, new Color(1f, 0.93f, 0.82f));
                k.Box(root, "Fixture", p + new Vector3(0f, 0.28f, 0f), new Vector3(1.2f, 0.04f, 1.2f), c.P.Unlit(new Color(1f, 0.97f, 0.9f)), collider: false);
            }

            // ---- core: lobby north wall, restroom, staff room, elevator, stairs ----
            k.WallX(root, "Core wall", X0 + Wall, X1 - Wall, CoreZ, 0f, groundTop, 0.2f, plaster,
                Opening.Door(146.8f), new Opening(151f, 1.1f, 0f, 2.3f), Opening.Door(ElevatorX, 1.3f, 0f, 2.3f), Opening.Door(StairDoorGround));
            TextMesh directory = Directory(c, root, new Vector3(153.4f, 0.02f, CoreZ - 0.11f));
            k.Text(root, "STAFF", new Vector3(151f, 2.5f, CoreZ - 0.11f), 0f, 0.09f, new Color(0.3f, 0.3f, 0.3f));
            k.Text(root, "RESTROOM", new Vector3(146.8f, 2.5f, CoreZ - 0.11f), 0f, 0.09f, new Color(0.3f, 0.3f, 0.3f));
            k.Text(root, "STAIRS", new Vector3(StairDoorGround, 2.5f, CoreZ - 0.11f), 0f, 0.09f, new Color(0.3f, 0.3f, 0.3f));

            // Restroom: x 144.3..149.5, z 8.1..12.
            k.WallZ(root, "Restroom wall", CoreZ + 0.1f, 12f, 149.6f, 0f, groundTop, 0.2f, plaster);
            k.WallX(root, "Restroom back", X0 + Wall, 149.7f, 12.1f, 0f, groundTop, 0.2f, plaster);
            k.Span(root, "Restroom ceiling", new Vector3(X0 + Wall, 2.8f, CoreZ + 0.1f), new Vector3(149.5f, 2.9f, 12f), ceiling, collider: false);
            k.Box(root, "Sink", new Vector3(145f, 0.85f, 10f), new Vector3(0.5f, 0.15f, 0.9f), c.P.Lit(Color.white, 0.6f));
            k.Box(root, "Mirror", new Vector3(144.33f, 1.6f, 10f), new Vector3(0.02f, 0.8f, 0.8f), c.P.Lit(new Color(0.75f, 0.8f, 0.82f), 0.95f), collider: false);
            k.Box(root, "Stall", new Vector3(147.9f, 1.05f, 10.6f), new Vector3(0.05f, 1.9f, 2.6f), c.P.Lit(new Color(0.5f, 0.55f, 0.6f)));
            k.Box(root, "Toilet", new Vector3(148.8f, 0.25f, 11.4f), new Vector3(0.4f, 0.5f, 0.6f), c.P.Lit(Color.white, 0.5f));
            c.PointLight(root, new Vector3(147f, 2.6f, 10f), 5f, 0.8f, new Color(0.95f, 0.97f, 1f));
            c.SwingDoor(dyn, "restroom door", new Vector3(146.3f, 0.02f, CoreZ), 1f, 2.15f, plaster);

            // Staff room: x 149.7..154.4 (receptionist's break room; the doorway has no door).
            k.WallZ(root, "Staff wall", CoreZ + 0.1f, 13f, 154.3f, 0f, groundTop, 0.2f, plaster);
            k.WallX(root, "Staff back", 149.7f, 154.4f, 13.1f, 0f, groundTop, 0.2f, plaster);
            k.WallX(root, "North closet", X0 + Wall, 160f, 13.1f, 0f, groundTop, 0.2f, plaster);

            Elevator elevator = BuildElevator(c, root, dyn, plaster, metal);
            BuildStairs(c, root, dyn, plaster, ceiling);

            // ---- floor 2 ----
            // Slab with holes for the elevator shaft (x 154.4..157.6, z 8.1..10.7) and stairwell (x 160.1..167.7, z 8.1..18.2).
            k.Span(root, "Slab 2 south", new Vector3(X0 + Wall, groundTop, Z0 + Wall), new Vector3(X1 - Wall, Floor2, CoreZ + 0.1f), carpet);
            k.Span(root, "Slab 2 north W", new Vector3(X0 + Wall, groundTop, CoreZ + 0.1f), new Vector3(154.4f, Floor2, Z1 - Wall), plaster);
            k.Span(root, "Slab 2 north M", new Vector3(157.6f, groundTop, CoreZ + 0.1f), new Vector3(160.1f, Floor2, Z1 - Wall), plaster);
            k.Span(root, "Slab 2 shaft N", new Vector3(154.4f, groundTop, 10.7f), new Vector3(157.6f, Floor2, Z1 - Wall), plaster);

            // Outer walls of floor 2: suites 201-203 get tinted (opaque) glass, 204 real windows.
            var southWindows = new List<Opening>();
            foreach (float x in new[] { 147.2f, 153.1f, 159f, 164.9f }) southWindows.Add(new Opening(x, 4.2f, Floor2 + 0.9f, Floor2 + 2.8f));
            k.WallX(root, "Front 2", X0, X1, Z0 + Wall / 2f, groundTop, Floor2Top, Wall, stone, southWindows.ToArray());
            foreach (float x in new[] { 147.2f, 153.1f, 159f })
                k.Span(root, "Tinted glass", new Vector3(x - 2.1f, Floor2 + 0.9f, Z0 + 0.1f), new Vector3(x + 2.1f, Floor2 + 2.8f, Z0 + 0.2f), tinted, collider: true);
            k.Pane(root, "Suite 204 window S", new Vector3(162.8f, Floor2 + 0.9f, Z0 + 0.13f), new Vector3(167f, Floor2 + 2.8f, Z0 + 0.17f), glassTint);
            k.WallZ(root, "East 2", Z0, Z1, X1 - Wall / 2f, groundTop, Floor2Top, Wall, stone, new Opening(-0.5f, 7.6f, Floor2 + 0.9f, Floor2 + 2.8f));
            k.Pane(root, "Suite 204 window E", new Vector3(X1 - 0.17f, Floor2 + 0.9f, -4.3f), new Vector3(X1 - 0.13f, Floor2 + 2.8f, 3.3f), glassTint);
            k.WallZ(root, "West 2", Z0, Z1, X0 + Wall / 2f, groundTop, Floor2Top, Wall, stone);
            k.WallX(root, "Back 2", X0, X1, Z1 - Wall / 2f, groundTop, Floor2Top, Wall, stone);
            k.Span(root, "Ceiling 2", new Vector3(X0 + Wall, 7.95f, Z0 + Wall), new Vector3(X1 - Wall, Floor2Top, Z1 - Wall), ceiling, collider: false);

            // Corridor (z 4.9..7.9) with the suites to the south.
            k.WallX(root, "Corridor north", X0 + Wall, X1 - Wall, CoreZ, Floor2, 7.95f, 0.2f, plaster,
                Opening.Door(ElevatorX, 1.3f, Floor2, 2.3f), Opening.Door(StairDoorUpper, 1f, Floor2));
            k.WallX(root, "Suite wall", X0 + Wall, X1 - Wall, SuiteWallZ, Floor2, 7.95f, 0.2f, plaster,
                Opening.Door(147.2f, 1f, Floor2), Opening.Door(153.1f, 1f, Floor2), Opening.Door(159f, 1f, Floor2), Opening.Door(164.8f, 1f, Floor2));
            foreach (float x in new[] { 150.15f, 156.05f, 161.95f })
                k.WallZ(root, "Suite partition", Z0 + Wall, SuiteWallZ - 0.1f, x, Floor2, 7.95f, 0.2f, plaster);
            c.PointLight(root, new Vector3(152f, 7.6f, 6.4f), 10f, 0.9f, new Color(1f, 0.94f, 0.85f));
            c.PointLight(root, new Vector3(162f, 7.6f, 6.4f), 10f, 0.9f, new Color(1f, 0.94f, 0.85f));

            string[] tenants = { "BRIGHTLINE BILLING", "OSEI & PARK CPA", "HARLOW DESIGN" };
            float[] suiteX = { 147.2f, 153.1f, 159f };
            for (int i = 0; i < 3; i++)
            {
                Door d = c.SwingDoor(dyn, $"suite {201 + i}", new Vector3(suiteX[i] - 0.5f, Floor2, SuiteWallZ), 1f, 2.15f, wood);
                d.LockReason = () => "locked (private office)";
                k.Text(root, $"{201 + i}  {tenants[i]}", new Vector3(suiteX[i], Floor2 + 2.4f, SuiteWallZ + 0.11f), 180f, 0.08f, new Color(0.25f, 0.25f, 0.25f));
            }

            officeDesk = BuildSuite204(c, root, dyn, wood, metal);
            Door suite = c.SwingDoor(dyn, "suite 204", new Vector3(164.3f, Floor2, SuiteWallZ), 1f, 2.15f, wood);
            suite.LockReason = () => c.IsTenant ? null : "locked (lease it in the STORE app)";
            TextMesh plate = k.Text(dyn, "204", new Vector3(164.8f, Floor2 + 2.4f, SuiteWallZ + 0.11f), 180f, 0.1f, new Color(0.25f, 0.25f, 0.25f));
            c.Dynamic.gameObject.AddComponent<TenantSigns>().Configure(c, directory, plate);

            // Upper floors: outside only.
            ModularFacade.Build(c, root, "Tower", new Vector3(X0, Floor2Top, Z0), new Vector3(X1, Height, Z1), FacadeStyle.Concrete, 51);

            // Receptionist.
            Vector3 station = new Vector3(ElevatorX, 0.02f, 2.9f);
            var route = new List<Vector3> { station, new Vector3(151f, 0.02f, 2.9f), new Vector3(151f, 0.02f, 7.4f), new Vector3(151f, 0.02f, 11.2f) };
            var lines = new ReceptionistLines(c);
            StaffNpc.Create(k, dyn, "Receptionist", 4101, new Color(0.18f, 0.2f, 0.28f),
                new WorkSchedule { Shift = Hours.Of(7.5, 17.5), Break = Hours.Of(12, 12.75), HasBreak = true },
                route, 180f, new[] { NpcPose.Typing, NpcPose.Typing, NpcPose.Phone, NpcPose.Drink, NpcPose.Stand },
                lines.Greeting, lines.Talk, c.Game, c.Hud, c.Player, look: "Formal");

            c.Place(new Vector3(ElevatorX, 0f, Z0 - 0.8f), PlaceKind.Door, "Calder Building");
            c.Anchor("calder_front_out", new Vector3(ElevatorX, 0f, Z0 - 2f));
            c.Anchor("calder_lobby", new Vector3(ElevatorX, 0.02f, -2.5f));
            c.Anchor("calder_elevator_hall_L", new Vector3(ElevatorX, 0.02f, CoreZ - 1.2f));
            c.Anchor("calder_elevator_car_L", new Vector3(ElevatorX, 0.02f, 9.6f));
            c.Anchor("calder_elevator_car_2", new Vector3(ElevatorX, Floor2, 9.6f));
            c.Anchor("calder_elevator_hall_2", new Vector3(ElevatorX, Floor2, CoreZ - 1.2f));
            c.Anchor("calder_corridor_204", new Vector3(164.8f, Floor2, 6.4f));
            c.Anchor("suite_204_inside", new Vector3(164.8f, Floor2, 3.2f));
            c.Anchor("calder_stairs_ground", new Vector3(StairDoorGround, 0.02f, 8.9f));
            c.Anchor("calder_stairs_top", new Vector3(StairDoorUpper, Floor2, 8.9f));
            c.Anchor("calder_stairs_door_ground", new Vector3(StairDoorGround, 0.02f, CoreZ - 1f));
            c.Anchor("calder_stairs_door_upper", new Vector3(StairDoorUpper, Floor2, CoreZ - 1f));
        }

        private static void Plant(CityContext c, Transform parent, Vector3 p)
        {
            c.Kit.Cylinder(parent, "Planter", p + new Vector3(0f, 0.3f, 0f), 0.55f, 0.6f, c.P.Lit(new Color(0.25f, 0.25f, 0.27f), 0.4f), collider: true);
            c.Kit.Sphere(parent, "Plant", p + new Vector3(0f, 1.05f, 0f), 0.95f, c.P.Lit(new Color(0.2f, 0.4f, 0.2f)));
        }

        private static TextMesh Directory(CityContext c, Transform root, Vector3 at)
        {
            Transform board = Kit.Group(root, "Directory", at);
            c.Kit.Box(board, "Board", new Vector3(0f, 1.6f, 0f), new Vector3(1.1f, 1.2f, 0.04f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f), collider: false);
            c.Kit.Text(board, "CALDER BUILDING", new Vector3(0f, 2.05f, -0.03f), 0f, 0.07f, new Color(0.9f, 0.8f, 0.55f));
            TextMesh list = c.Kit.Text(board, "", new Vector3(-0.47f, 1.9f, -0.03f), 0f, 0.058f, new Color(0.9f, 0.9f, 0.88f), TextAnchor.UpperLeft);
            list.alignment = TextAlignment.Left;
            return list;
        }

        private static Elevator BuildElevator(CityContext c, Transform root, Transform dyn, Material wall, Material metal)
        {
            Kit k = c.Kit;
            // Shaft x 154.4..157.6, z 8.1..10.7. Car interiors x 154.6..157.4, z 8.1..10.5 at both floors.
            k.WallZ(root, "Shaft W", CoreZ + 0.1f, 10.7f, 154.5f, 0f, Floor2Top, 0.2f, wall);
            k.WallZ(root, "Shaft E", CoreZ + 0.1f, 10.7f, 157.5f, 0f, Floor2Top, 0.2f, wall);
            k.WallX(root, "Shaft back", 154.4f, 157.6f, 10.6f, 0f, Floor2Top, 0.2f, wall);
            Material panel = c.P.Lit(new Color(0.62f, 0.6f, 0.56f), 0.65f);
            Material lampOff = c.P.Unlit(new Color(0.25f, 0.25f, 0.24f));
            Material lampOn = c.P.Unlit(new Color(1f, 0.75f, 0.35f));
            Transform shaft = Kit.Group(dyn, "Elevator", new Vector3(ElevatorX, 0.02f, 9.3f));
            var elevator = shaft.gameObject.AddComponent<Elevator>();
            var stops = new Elevator.FloorStop[2];
            string[] labels = { "L", "2" };
            float[] ys = { 0f, Floor2 - 0.02f };
            for (int f = 0; f < 2; f++)
            {
                float y = ys[f];
                k.Span(shaft, "Car floor", new Vector3(-1.4f, y - 0.1f, -1.2f), new Vector3(1.4f, y, 1.2f), metal);
                k.Span(shaft, "Car ceiling", new Vector3(-1.4f, y + 2.6f, -1.2f), new Vector3(1.4f, y + 2.7f, 1.2f), panel, collider: false);
                k.Span(shaft, "Car back", new Vector3(-1.4f, y, 1.15f), new Vector3(1.4f, y + 2.6f, 1.2f), panel, collider: false);
                k.Span(shaft, "Car rail", new Vector3(-1.2f, y + 0.9f, 1.08f), new Vector3(1.2f, y + 0.95f, 1.15f), metal, collider: false);
                c.PointLight(shaft, new Vector3(0f, y + 2.4f, 0f), 3.5f, 0.7f, new Color(1f, 0.95f, 0.85f));
                k.Box(shaft, "Car light", new Vector3(0f, y + 2.58f, 0f), new Vector3(1.4f, 0.03f, 1.2f), c.P.Unlit(new Color(1f, 0.97f, 0.9f)), collider: false);

                var stop = new Elevator.FloorStop { Label = labels[f], Y = y };
                stop.DoorLeft = k.Box(shaft, "Door L", new Vector3(-0.33f, y + 1.15f, -1.3f), new Vector3(0.66f, 2.3f, 0.05f), metal).transform;
                stop.DoorRight = k.Box(shaft, "Door R", new Vector3(0.33f, y + 1.15f, -1.3f), new Vector3(0.66f, 2.3f, 0.05f), metal).transform;
                stop.HallIndicator = k.Text(shaft, labels[f], new Vector3(0f, y + 2.55f, -1.42f), 0f, 0.14f, new Color(1f, 0.55f, 0.2f));
                stop.CarIndicator = k.Text(shaft, labels[f], new Vector3(0f, y + 2.42f, -1.14f), 180f, 0.12f, new Color(1f, 0.55f, 0.2f));

                // Hall call button (corridor side, right of the doors).
                GameObject call = k.Box(shaft, "Call", new Vector3(1.05f, y + 1.1f, -1.43f), new Vector3(0.14f, 0.2f, 0.04f), panel);
                stop.HallLamp = k.Box(shaft, "Call lamp", new Vector3(1.05f, y + 1.1f, -1.46f), new Vector3(0.06f, 0.06f, 0.02f), lampOff, collider: false).GetComponent<Renderer>();
                call.AddComponent<ElevatorButton>().Configure(elevator, f, inCar: false);

                // Car buttons on the right-hand wall inside.
                Transform board = Kit.Group(shaft, "Car panel", new Vector3(1.37f, y, -0.6f), 90f); // faces into the car (-x)
                k.Box(board, "Plate", new Vector3(0f, 1.2f, 0f), new Vector3(0.26f, 0.55f, 0.02f), panel, collider: false);
                for (int b = 0; b < 2; b++)
                {
                    GameObject button = k.Box(board, "Button " + labels[b], new Vector3(0f, 1.05f + b * 0.25f, -0.02f), new Vector3(0.12f, 0.12f, 0.03f), metal);
                    var floorButton = button.AddComponent<ElevatorButton>();
                    floorButton.Configure(elevator, b, inCar: true);
                    if (b > 0) floorButton.LockReason = () => c.IsTenant ? null : "tenants only (key card)";
                    k.Text(board, labels[b], new Vector3(-0.11f, 1.05f + b * 0.25f, -0.03f), 0f, 0.05f, new Color(0.1f, 0.1f, 0.1f));
                    if (b != f)
                    {
                        // Lit while the car is on its way to the other floor.
                        stop.CarLamp = k.Box(board, "Lamp", new Vector3(0.09f, 1.05f + b * 0.25f, -0.03f), new Vector3(0.03f, 0.03f, 0.02f), lampOff, collider: false).GetComponent<Renderer>();
                        stop.CarLampFloor = b;
                    }
                }
                stops[f] = stop;
            }
            elevator.Configure(stops, new Vector2(1.4f, 1.2f), 1.3f, c.Player, lampOn, lampOff);
            return elevator;
        }

        private static void BuildStairs(CityContext c, Transform root, Transform dyn, Material wall, Material ceiling)
        {
            Kit k = c.Kit;
            // Stairwell x 160.1..167.7, z 8.1..18.2. Flight 1 north along x 160.2..162.2, landing, flight 2 south along x 165.5..167.6.
            k.WallZ(root, "Stair W", CoreZ + 0.1f, Z1 - Wall, 160f, 0f, Floor2Top, 0.2f, wall);
            Material step = c.P.Lit(new Color(0.55f, 0.55f, 0.53f));
            Material rail = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.5f);
            Ramp(k, root, new Vector3(161.2f, 0f, 9.6f), new Vector3(161.2f, 2.4f, 15.6f), 2f, step);
            k.Span(root, "Mid landing", new Vector3(160.1f, 2.28f, 15.6f), new Vector3(X1 - Wall, 2.4f, Z1 - Wall), step);
            Ramp(k, root, new Vector3(166.55f, 2.4f, 15.6f), new Vector3(166.55f, Floor2, 9.6f), 2.1f, step);
            k.Span(root, "Top landing", new Vector3(162.4f, Floor2 - 0.12f, CoreZ + 0.1f), new Vector3(X1 - Wall, Floor2, 9.6f), step);
            // Solid core between the flights, rails on the open edges.
            k.Span(root, "Stair core", new Vector3(162.3f, 0f, 9.6f), new Vector3(165.4f, Floor2Top, 15.6f), wall);
            k.Span(root, "Under stairs", new Vector3(165.4f, 0f, 9.5f), new Vector3(X1 - Wall, 2.3f, 9.6f), wall);
            k.Span(root, "Landing rail", new Vector3(162.3f, Floor2, 9.55f), new Vector3(162.4f, Floor2 + 1f, 9.65f), rail);
            k.Span(root, "Top rail", new Vector3(162.35f, Floor2, CoreZ + 0.1f), new Vector3(162.45f, Floor2 + 1f, 9.6f), rail);
            c.PointLight(root, new Vector3(164f, 3.9f, 16.9f), 9f, 0.8f, new Color(0.95f, 0.97f, 1f));
            c.PointLight(root, new Vector3(163.6f, 7.3f, 8.9f), 6f, 0.7f, new Color(0.95f, 0.97f, 1f));

            Door ground = c.SwingDoor(dyn, "stairwell door", new Vector3(StairDoorGround - 0.5f, 0.02f, CoreZ), 1f, 2.15f, c.P.Lit(new Color(0.45f, 0.47f, 0.5f), 0.4f));
            ground.LockReason = () => c.IsTenant ? null : "tenants only (key card)";
            c.SwingDoor(dyn, "stairwell door", new Vector3(StairDoorUpper - 0.5f, Floor2, CoreZ), 1f, 2.15f, c.P.Lit(new Color(0.45f, 0.47f, 0.5f), 0.4f));
        }

        /// <summary>Walkable stair flight: a sloped collider plus visible steps (no colliders on the steps).</summary>
        private static void Ramp(Kit k, Transform parent, Vector3 bottom, Vector3 top, float width, Material step)
        {
            Vector3 run = top - bottom;
            float horizontal = new Vector2(run.x, run.z).magnitude;
            float angle = Mathf.Atan2(run.y, horizontal) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(run.x, run.z) * Mathf.Rad2Deg;
            var slope = new GameObject("Stair slope");
            slope.transform.SetParent(parent, false);
            slope.transform.localPosition = (bottom + top) / 2f;
            slope.transform.localRotation = Quaternion.Euler(-angle, yaw, 0f);
            var box = slope.AddComponent<BoxCollider>();
            box.size = new Vector3(width, 0.05f, run.magnitude);

            int steps = Mathf.RoundToInt(run.y / 0.185f);
            Vector3 dir = new Vector3(run.x, 0f, run.z).normalized;
            for (int i = 0; i < steps; i++)
            {
                float h = run.y * (i + 1) / steps;
                Vector3 centre = bottom + dir * (horizontal * (i + 0.5f) / steps) + Vector3.up * (h / 2f);
                GameObject s = k.Box(parent, "Step", centre, new Vector3(width, h, horizontal / steps), step, collider: false);
                s.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private static Desk BuildSuite204(CityContext c, Transform root, Transform dyn, Material wood, Material metal)
        {
            Kit k = c.Kit;
            // Suite 204: x 162.05..167.7, z -5.2..4.7, floor at 4.8. One desk facing the south window.
            Transform suite = Kit.Group(root, "Suite 204");
            Material top = c.P.Lit(new Color(0.55f, 0.55f, 0.53f), 0.2f);
            k.Box(suite, "Desk top", new Vector3(164.9f, Floor2 + 0.73f, -2.6f), new Vector3(1.5f, 0.04f, 0.72f), top);
            foreach (float dx in new[] { -0.7f, 0.7f })
            foreach (float dz in new[] { -0.3f, 0.3f })
                k.Box(suite, "Leg", new Vector3(164.9f + dx, Floor2 + 0.36f, -2.6f + dz), new Vector3(0.04f, 0.72f, 0.04f), metal, collider: false);
            // Office pack art where it's there (keyboard facing the chair, the cabinet's drawers the room, a water
            // cooler in the corner); plain boxes otherwise.
            if (k.Fit(suite, "computerKeyboard", new Vector3(164.9f, Floor2 + 0.75f, -2.43f), new Vector3(0.45f, 0.025f, 0.15f), 180f, stretch: true) == null)
                k.Box(suite, "Keyboard", new Vector3(164.9f, Floor2 + 0.76f, -2.43f), new Vector3(0.45f, 0.02f, 0.15f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f)), collider: false);
            GameObject cabinet = k.Fit(suite, "office_drawer_cube", new Vector3(167.3f, Floor2, 3.9f), new Vector3(0.7f, 1f, 0f));
            if (cabinet != null) k.Solid(cabinet);
            else k.Box(suite, "Filing cabinet", new Vector3(167.3f, Floor2 + 0.5f, 3.9f), new Vector3(0.5f, 1f, 0.6f), c.P.Lit(new Color(0.45f, 0.47f, 0.5f), 0.4f));
            k.Solid(k.Fit(suite, "office_watercooler_cube_246_cube", new Vector3(162.5f, Floor2, 4.3f), new Vector3(0f, 1.25f, 0f), 90f));
            Plant(c, suite, new Vector3(162.6f, Floor2, -4.6f));
            k.Box(suite, "Whiteboard", new Vector3(162.17f, Floor2 + 1.5f, 0.5f), new Vector3(0.03f, 0.9f, 1.6f), c.P.Lit(new Color(0.95f, 0.95f, 0.95f), 0.6f), collider: false);
            // Poor lighting, as advertised: one dim bulb.
            c.PointLight(suite, new Vector3(164.9f, 7.6f, 0f), 7f, 0.55f, new Color(1f, 0.9f, 0.75f));

            // Monitor facing the chair (north). Built like the home monitor, turned around.
            Transform monitor = Kit.Group(dyn, "Office monitor", new Vector3(164.9f, Floor2, -2.78f), 180f);
            k.Box(monitor, "Stand", new Vector3(0f, 0.88f, 0.02f), new Vector3(0.08f, 0.24f, 0.06f), c.P.Lit(new Color(0.1f, 0.1f, 0.11f)));
            k.Box(monitor, "Bezel", new Vector3(0f, 1.12f, 0f), new Vector3(0.62f, 0.38f, 0.04f), c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.5f));
            // Its own material instance: the terminal image gets assigned to it.
            GameObject screen = k.Quad(monitor, "Screen", new Vector3(0f, 1.12f, -0.021f), new Vector2(0.58f, 0.326f), 0f, new Material(c.P.Unlit(Color.black)));

            // Same chair/monitor/camera spacing as the home desk, so the terminal fills the view the same way.
            Transform chair = Kit.Group(dyn, "Office chair", new Vector3(164.9f, Floor2, -1.98f));
            Material fabric = c.P.Lit(new Color(0.2f, 0.2f, 0.22f));
            k.Box(chair, "Seat", new Vector3(0f, 0.46f, 0f), new Vector3(0.45f, 0.06f, 0.45f), fabric);
            k.Box(chair, "Back", new Vector3(0f, 0.76f, 0.21f), new Vector3(0.45f, 0.5f, 0.05f), fabric);
            foreach (float dx in new[] { -0.2f, 0.2f })
            foreach (float dz in new[] { -0.2f, 0.2f })
                k.Box(chair, "Leg", new Vector3(dx, 0.22f, dz), new Vector3(0.03f, 0.44f, 0.03f), metal, collider: false);

            Transform view = Kit.Group(dyn, "Office seat view", new Vector3(164.9f, Floor2 + 1.17f, -2.11f), 180f);
            view.localRotation = Quaternion.Euler(4.4f, 180f, 0f);
            Transform stand = Kit.Group(dyn, "Office stand point", new Vector3(164.9f, Floor2, -1.38f), 180f);
            var desk = monitor.gameObject.AddComponent<Desk>();
            desk.Configure(c.Workstation, view, stand, screen.GetComponent<Renderer>());
            chair.gameObject.AddComponent<SeatInteractable>().Configure(c.Workstation, desk);
            monitor.gameObject.AddComponent<SeatInteractable>().Configure(c.Workstation, desk, "Use computer");
            c.Anchor("office_desk", stand.position);
            return desk;
        }

        /// <summary>What the receptionist says: time of day, the lease, the market.</summary>
        private sealed class ReceptionistLines
        {
            private readonly CityContext _c;
            private bool _welcomed;
            private int _line;

            public ReceptionistLines(CityContext c) => _c = c;

            public string Greeting()
            {
                DateTime now = _c.Game.Clock.Now;
                if (_c.IsTenant && !_welcomed)
                {
                    _welcomed = true;
                    return "You must be our new tenant in 204. Your key card's active. Elevator's right behind me.";
                }
                string hello = now.Hour < 12 ? "Morning." : now.Hour < 17 ? "Afternoon." : "Evening.";
                return _c.IsTenant ? hello : hello + " Can I help you?";
            }

            public string Talk()
            {
                var lines = new List<string>();
                if (!_c.IsTenant) lines.Add("Floors upstairs are tenants only. Suite 204 is available. The listing's in the online store.");
                var market = _c.Game.Market;
                if (market.Session == OpeningBell.Market.MarketSession.Regular)
                {
                    decimal change = market.Index.ChangePercent;
                    string move = Math.Abs(change) < 0.2m ? "Market's flat so far today."
                        : $"Market's {(change > 0 ? "up" : "down")} {Math.Abs(change).ToString("0.0", CultureInfo.InvariantCulture)}% today.";
                    lines.Add(move + (change < -1m ? " Rough one for the folks upstairs." : ""));
                }
                if (_c.Game.Clock.Now.Hour >= 16) lines.Add("I'm out at five thirty. Doors lock at nine, but your card works after hours.");
                lines.Add("Mailboxes are on the right if you're expecting anything.");
                lines.Add("Elevator's slow, but it gets there.");
                return lines[_line++ % lines.Count];
            }
        }

        /// <summary>Keeps the directory and the suite plate in step with the lease.</summary>
        private sealed class TenantSigns : MonoBehaviour
        {
            private CityContext _c;
            private TextMesh _directory, _plate;
            private bool? _shown;

            public void Configure(CityContext c, TextMesh directory, TextMesh plate)
            {
                _c = c;
                _directory = directory;
                _plate = plate;
            }

            private void Update()
            {
                bool tenant = _c.IsTenant;
                if (_shown == tenant) return;
                _shown = tenant;
                _directory.text = "201  Brightline Billing\n202  Osei & Park CPA\n203  Harlow Design\n204  " +
                                  (tenant ? "(your office)" : "AVAILABLE") + "\n\nL  Reception · Mail";
                _plate.text = tenant ? "204  YOUR OFFICE" : "204  AVAILABLE";
            }
        }
    }
}
