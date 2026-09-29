using System.Collections.Generic;
using OpeningBell.Fund;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Harborview Tower, Level 26 (FUND_SPEC §7): the full floor directly below the penthouse, the hedge fund's
    /// headquarters. Finished and lit when you take it: floor-to-ceiling glass, carpet and oak, an acoustic ceiling with
    /// linear lights, power and data boxes in the floor, a reception counter and brand wall, glass-walled meeting room and
    /// private office, a kitchenette for the coffee point, restrooms, a storage and data room, and a partitioned east wing
    /// for later. The desks, chairs, screens, coffee machine and seating are the player's to buy and place.
    /// Plan coordinates: metres from the tower's centre, +z north (the lift is on the north side; the trading floor faces
    /// the water to the south).
    /// </summary>
    public static class HarborviewOffice
    {
        /// <summary>Floor level above grade (the penthouse slab sits on the office ceiling).</summary>
        public const float Floor = HarborviewTower.PenthouseFloor - 4.4f;
        public const float Ceiling = 3.8f;
        private const float HW = 14f, HD = 12f;
        /// <summary>The partition closing off the east wing until it's fitted out (plan x).</summary>
        public const float WingX = 6f;

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>The office floor's frame (plan-aligned, origin at the tower's centre at floor level).</summary>
        public static Transform Frame { get; private set; }

        /// <summary>Inside the principal's (the owner's) private office, north-east behind the glass (world point)?</summary>
        public static bool InPrincipalOffice(Vector3 world)
        {
            if (Frame == null) return false;
            Vector3 p = Frame.InverseTransformPoint(world);
            return p.x > 7f && p.x < HW && p.z > 3.5f && p.z < HD && p.y > -1f && p.y < 4f;
        }

        /// <summary>A plan point (x, z) on the office floor in world space.</summary>
        public static Vector3 World(float x, float z) => Frame != null ? Frame.TransformPoint(new Vector3(x, 0f, z)) : V(HarborviewTower.X + x, HarborviewTower.Grade + Floor, HarborviewTower.Z + z);

        public static bool Contains(Vector3 world)
        {
            if (Frame == null) return false;
            Vector3 p = Frame.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < HW && Mathf.Abs(p.z) < HD && p.y > -0.5f && p.y < Ceiling;
        }

        // ------------------------------------------------------------------ where people go (plan x, z)

        /// <summary>Standing just out of the lift doors.</summary>
        public static readonly Vector2 LiftLanding = new Vector2(0f, 8.2f);
        /// <summary>Inside the glass entrance.</summary>
        public static readonly Vector2 Entrance = new Vector2(0f, 5.8f);
        /// <summary>A marked spot to stand and wait when every seat's taken.</summary>
        public static readonly Vector2[] StandingWait = { new Vector2(-4.2f, 5.9f), new Vector2(-5.2f, 5.9f), new Vector2(-6.2f, 5.9f), new Vector2(4.4f, 5.9f), new Vector2(5.4f, 5.9f) };
        /// <summary>Where the coffee machine goes (a kitchenette counter) and where people stand to use it.</summary>
        public static readonly Vector2 CoffeePoint = new Vector2(-11.2f, 2.3f);
        /// <summary>Somewhere to stand and chat in the break area.</summary>
        public static readonly Vector2[] BreakSpots = { new Vector2(-10.5f, 0.6f), new Vector2(-11.6f, 0.2f), new Vector2(-12.4f, -0.8f), new Vector2(-9.6f, -0.9f) };
        public static readonly Vector2[] Restrooms = { new Vector2(4f, 7.4f), new Vector2(6f, 7.4f) };
        public static readonly Vector2 MeetingRoom = new Vector2(-10.5f, 7.5f);
        public static readonly Vector2 Receptionist = new Vector2(0f, 3.6f);

        // ------------------------------------------------------------------ building it

        public static HomeSpec Build(CityContext c, HomeWorld world, Transform root, Transform dyn)
        {
            float g = HarborviewTower.Grade, fy = g + Floor;
            // Like the penthouse, the home frame faces north (-z) where you come in from the lift; Rooms is plan-aligned.
            Transform home = Kit.Group(root, "Harborview Office", V(HarborviewTower.X, fy, HarborviewTower.Z), 180f);
            Transform p = Kit.Group(home, "Rooms", Vector3.zero, 180f);
            Frame = p;
            var m = new Mats(c);
            Shell(c, p, m);
            Core(c, p, m);
            Rooms(c, p, m);
            Kitchenette(c, p, m);
            Ceilings(c, p, m);
            Services(c, p, m);
            Transform live = Kit.Group(dyn, "Harborview Office (live)", V(HarborviewTower.X, fy, HarborviewTower.Z));
            Wing(c, live, m, world);
            Signs(c, live, m, world);

            var spec = new HomeSpec
            {
                Id = HedgeFund.OfficeId, Name = HedgeFund.OfficeName, Kind = HomeKind.Office, Price = 0m,
                Root = home, Size = new Vector2(HW * 2f, HD * 2f), Floors = 1, Height = Ceiling, DoorLocal = V(0f, 0f, -6.5f),
            };
            c.Anchor("office_lift", World(LiftLanding.x, LiftLanding.y));
            c.Anchor("office_inside", World(0f, 0f));
            c.Anchor("office_reception", World(Entrance.x, Entrance.y));
            return spec;
        }

        private sealed class Mats
        {
            public readonly Material Carpet, Oak, Tile, Plaster, Ceiling, Frame, Glass, Walnut, Stone, Glow, Steel, White, Dark, Marble, Brass;

            public Mats(CityContext c)
            {
                Carpet = c.P.Textured("office carpet", OfficeSurfaces.Carpet(), new Color(0.62f, 0.64f, 0.67f), 0.02f);
                Oak = c.P.Textured("oak", Surfaces.Oak(), Color.white, 0.35f);
                Tile = c.P.Textured("office tile", Surfaces.Tiles(), new Color(0.88f, 0.88f, 0.87f), 0.5f);
                Plaster = c.P.Bounce(new Color(0.94f, 0.94f, 0.93f), 0.12f);
                Ceiling = c.P.Textured("acoustic ceiling", OfficeSurfaces.CeilingTiles(), new Color(0.97f, 0.97f, 0.96f), 0.02f);
                Frame = c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f);
                Glass = c.P.Glass(new Color(0.78f, 0.86f, 0.9f, 0.1f));
                Walnut = c.P.Lit(new Color(0.3f, 0.19f, 0.12f), 0.35f);
                Stone = c.P.Lit(new Color(0.8f, 0.78f, 0.74f), 0.25f);
                Glow = c.P.Glow(new Color(1f, 0.97f, 0.92f), 1.8f);
                Steel = c.P.Lit(new Color(0.62f, 0.63f, 0.65f), 0.7f);
                White = c.P.Lit(new Color(0.96f, 0.96f, 0.95f), 0.85f);
                Dark = c.P.Lit(new Color(0.16f, 0.17f, 0.19f), 0.3f);
                Marble = c.P.Textured("marble", Surfaces.Marble(), Color.white, 0.75f);
                Brass = c.P.Lit(new Color(0.75f, 0.6f, 0.35f), 0.75f);
            }
        }

        private static void Quad(MeshBuilder mb, float x0, float z0, float x1, float z1, float y) =>
            mb.Quad(V(x0, y, z0), V(x1, y, z0), V(x1, y, z1), V(x0, y, z1));

        /// <summary>Floors, curtain wall and the slab that carries the penthouse.</summary>
        private static void Shell(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            // Carpet on the trading floor and the meeting and private offices, oak in reception, tiles in the wet rooms.
            var carpet = new MeshBuilder();
            var oak = new MeshBuilder();
            var tile = new MeshBuilder();
            Quad(carpet, -HW, -HD, HW, 3.5f, 0.006f);
            Quad(carpet, -HW, 3.5f, -7f, HD, 0.006f);
            Quad(carpet, 7f, 3.5f, HW, HD, 0.006f);
            Quad(oak, -7f, 3.5f, 7f, 8f, 0.006f);
            Quad(oak, -3f, 8f, 3f, 9f, 0.006f);
            Quad(tile, 3f, 8f, 7f, HD, 0.006f);
            Quad(tile, -7f, 8f, -3f, HD, 0.006f);
            Quad(tile, -14f, -3f, -8f, 3.5f, 0.007f);
            carpet.Build(p, "Carpet", m.Carpet, collider: false);
            oak.Build(p, "Oak floor", m.Oak, collider: false);
            tile.Build(p, "Tile floor", m.Tile, collider: false);

            // The slab above: the office ceiling's structure and the penthouse's floor (walkable from above).
            k.Span(p, "Penthouse slab", V(-HW, Ceiling, -HD), V(HW, HarborviewTower.PenthouseFloor - Floor, HD), m.Dark);

            // Floor-to-ceiling glass all round with slim dark mullions; the core's north face is solid.
            void Side(Vector3 a, Vector3 b)
            {
                GameObject pane = k.Span(p, "Window", Vector3.Min(a, b), Vector3.Max(a, b) + V(0f, Ceiling, 0f), m.Glass);
                pane.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float len = Vector3.Distance(a, b);
                int bays = Mathf.Max(1, Mathf.RoundToInt(len / 1.5f));
                for (int i = 0; i <= bays; i++)
                {
                    Vector3 at = Vector3.Lerp(a, b, i / (float)bays);
                    k.Box(p, "Mullion", at + V(0f, Ceiling / 2f, 0f), V(0.07f, Ceiling, 0.07f), m.Frame, collider: false);
                }
                k.Span(p, "Sill", Vector3.Min(a, b) + V(-0.04f, 0f, -0.04f), Vector3.Max(a, b) + V(0.04f, 0.1f, 0.04f), m.Frame, collider: false);
                k.Span(p, "Head", Vector3.Min(a, b) + V(-0.04f, Ceiling - 0.12f, -0.04f), Vector3.Max(a, b) + V(0.04f, Ceiling, 0.04f), m.Frame, collider: false);
            }
            const float t = 0.02f;
            Side(V(-HW, 0f, -HD - t), V(HW, 0f, -HD + t));
            Side(V(-HW - t, 0f, -HD), V(-HW + t, 0f, HD));
            Side(V(HW - t, 0f, -HD), V(HW + t, 0f, HD));
            Side(V(-HW, 0f, HD - t), V(-7f, 0f, HD + t));
            Side(V(7f, 0f, HD - t), V(HW, 0f, HD + t));
            k.Span(p, "Core face", V(-7f, 0f, HD - 0.15f), V(7f, Ceiling, HD), m.Plaster);
            // Structural columns in the open floor.
            foreach (Vector2 col in new[] { new Vector2(-7f, -5f), new Vector2(0f, -5f), new Vector2(7f, -5f) })
                k.Box(p, "Column", V(col.x, Ceiling / 2f, col.y), V(0.6f, Ceiling, 0.6f), m.Plaster);
        }

        /// <summary>The lift core in the middle of the north side, the lift lobby and the glass entrance to reception.</summary>
        private static void Core(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            k.WallX(p, "Lift wall", -3f, 3f, 9f, 0f, Ceiling, 0.2f, m.Walnut, Opening.Door(0f, 1.3f, 0f, 2.3f));
            k.Span(p, "Core", V(-3f, 0f, 9.1f), V(-1.45f, Ceiling, HD - 0.15f), m.Plaster);
            k.Span(p, "Core", V(1.45f, 0f, 9.1f), V(3f, Ceiling, HD - 0.15f), m.Plaster);
            k.Span(p, "Core", V(-1.45f, 0f, 11.45f), V(1.45f, Ceiling, HD - 0.15f), m.Plaster);
            k.Text(p, "26", V(0f, 2.75f, 8.88f), 0f, 0.18f, new Color(0.85f, 0.7f, 0.42f));
            // Lift lobby: stone floor band and a pendant; glass wall with double doors into reception.
            k.WallX(p, "Entrance glass", -7f, 7f, 6.5f, 0f, Ceiling, 0.03f, m.Glass, Opening.Door(0f, 2f, 0f, 2.4f));
            for (float x = -7f; x <= 7.01f; x += 1.75f)
                if (Mathf.Abs(x) > 1.1f) k.Box(p, "Entrance mullion", V(x, Ceiling / 2f, 6.5f), V(0.06f, Ceiling, 0.08f), m.Frame, collider: false);
            k.Box(p, "Entrance transom", V(0f, 2.45f, 6.5f), V(2.1f, 0.1f, 0.1f), m.Frame, collider: false);
            c.SlidingDoor(p, "Level 26", V(0f, 0f, 6.5f), 2f, 2.4f);
            k.Sphere(p, "Lobby pendant", V(0f, 2.9f, 7.8f), 0.5f, m.Glow);
            c.PointLight(p, V(0f, 3.3f, 7.8f), 7f, 0.8f, new Color(1f, 0.92f, 0.82f));
        }

        /// <summary>Reception, the brand wall, meeting room, private office, restrooms and the storage/data room.</summary>
        private static void Rooms(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            // Reception counter facing the entrance, a stone top, a glowing plinth; the brand wall behind it.
            k.Span(p, "Reception counter", V(-1.9f, 0f, 4.1f), V(1.9f, 1.05f, 4.7f), m.Walnut);
            k.Span(p, "Reception top", V(-2f, 1.05f, 4.05f), V(2f, 1.1f, 4.8f), m.Marble, collider: false);
            k.Span(p, "Reception glow", V(-1.85f, 0.02f, 4.7f), V(1.85f, 0.08f, 4.75f), c.P.Glow(new Color(1f, 0.85f, 0.66f), 1.4f), collider: false);
            k.Span(p, "Brand wall", V(-3.2f, 0f, 3.3f), V(3.2f, 2.9f, 3.5f), m.Walnut);
            // The waiting area is marked out west of reception: people stand here if no seats have been bought.
            k.Span(p, "Waiting rug", V(-6.8f, 0.008f, 3.8f), V(-3.3f, 0.012f, 6.3f), c.P.Lit(new Color(0.5f, 0.52f, 0.55f), 0.02f), collider: false);

            // Meeting and training room (north-west), glass on two sides, a door onto the waiting area.
            k.WallZ(p, "Meeting glass", 3.5f, HD, -7f, 0f, Ceiling, 0.03f, m.Glass, Opening.Door(5f, 1f, 0f, 2.4f));
            k.WallX(p, "Meeting glass", -HW, -7f, 3.5f, 0f, Ceiling, 0.03f, m.Glass);
            c.SwingDoor(p, "Meeting room", V(-7f, 0f, 4.5f), 1f, 2.4f, m.Glass, 270f, glass: true);
            k.Span(p, "Meeting screen", V(-13.9f, 1.1f, 6.2f), V(-13.84f, 2.3f, 8.8f), m.Dark, collider: false);
            k.Span(p, "Whiteboard", V(-12.8f, 1f, HD - 0.18f), V(-9f, 2.2f, HD - 0.15f), m.White, collider: false);
            DoorSign(c, p, "MEETING / TRAINING", V(-6.97f, 2.55f, 5f), 270f, 0.07f);

            // Private office (north-east) for the owner.
            k.WallZ(p, "Office glass", 3.5f, HD, 7f, 0f, Ceiling, 0.03f, m.Glass, Opening.Door(5f, 1f, 0f, 2.4f));
            k.WallX(p, "Office glass", 7f, HW, 3.5f, 0f, Ceiling, 0.03f, m.Glass);
            c.SwingDoor(p, "Private office", V(7f, 0f, 5.5f), 1f, 2.4f, m.Glass, 90f, glass: true);
            DoorSign(c, p, "PRINCIPAL", V(6.97f, 2.55f, 5f), 90f, 0.07f);

            // Restrooms: two single rooms north-east of the lobby.
            k.WallX(p, "Restroom wall", 3f, 7f, 8f, 0f, Ceiling, 0.15f, m.Plaster, Opening.Door(4f, 0.9f), Opening.Door(6f, 0.9f));
            k.WallZ(p, "Restroom wall", 8f, HD - 0.15f, 5f, 0f, Ceiling, 0.12f, m.Plaster);
            k.WallZ(p, "Restroom wall", 8f, HD - 0.15f, 7f, 0f, Ceiling, 0.15f, m.Plaster);
            foreach (float x in new[] { 4f, 6f })
            {
                c.SwingDoor(p, "Restroom " + (x < 5f ? "A" : "B"), V(x - 0.45f, 0f, 8f), 0.9f, 2.15f, m.White);
                k.Span(p, "WC", V(x - 0.25f, 0f, 10.9f), V(x + 0.25f, 0.42f, 11.6f), m.White);
                k.Span(p, "Basin", V(x + 0.45f, 0.8f, 9f), V(x + 0.85f, 0.9f, 9.5f), m.White);
                k.Span(p, "Mirror", V(x + 0.9f, 1.15f, 8.9f), V(x + 0.93f, 1.95f, 9.6f), m.White, collider: false);
                c.PointLight(p, V(x, 3.2f, 10f), 4f, 0.5f, new Color(1f, 0.97f, 0.92f));
                DoorSign(c, p, "RESTROOM", V(x, 2.3f, 7.91f), 0f, 0.06f, person: true);
            }

            // Storage and data room north-west of the lobby: shelving and the comms rack that feeds the floor boxes.
            k.WallX(p, "Storage wall", -7f, -3f, 8f, 0f, Ceiling, 0.15f, m.Plaster, Opening.Door(-5f, 1f));
            k.WallZ(p, "Storage wall", 8f, HD - 0.15f, -7f, 0f, Ceiling, 0.15f, m.Plaster);
            c.SwingDoor(p, "Storage", V(-5.5f, 0f, 8f), 1f, 2.15f, m.Plaster);
            k.Span(p, "Shelving", V(-6.8f, 0f, 8.6f), V(-6.3f, 2.2f, 11.6f), m.Steel);
            k.Span(p, "Comms rack", V(-4.3f, 0f, 10.8f), V(-3.3f, 2f, 11.7f), m.Dark);
            for (int i = 0; i < 6; i++)
                k.Box(p, "Rack light", V(-3.8f, 0.4f + i * 0.28f, 10.78f), V(0.5f, 0.02f, 0.01f), c.P.Glow(new Color(0.3f, 0.9f, 0.5f), 1.5f), collider: false);
            DoorSign(c, p, "STORAGE / DATA", V(-5f, 2.3f, 7.91f), 0f, 0.06f);
            c.PointLight(p, V(-5f, 3.2f, 10f), 5f, 0.5f, new Color(0.95f, 0.97f, 1f));
        }

        /// <summary>
        /// A door sign to match the lift signs: a brushed-steel frame standing off the wall, a dark blue face and white
        /// letters, with the restrooms' person pictogram beside the word. <paramref name="at"/> is the letters' centre
        /// just off the wall; the sign faces its reader (local -z after <paramref name="yaw"/>).
        /// </summary>
        private static void DoorSign(CityContext c, Transform p, string text, Vector3 at, float yaw, float size, bool person = false)
        {
            Kit k = c.Kit;
            Transform s = Kit.Group(p, text + " sign", at, yaw);
            float icon = person ? size * 1.6f : 0f;
            float w = text.Length * size * 0.72f + icon + size * 1.6f, h = size * 2.6f;
            k.Box(s, "Frame", new Vector3(0f, 0f, 0.014f), new Vector3(w, h, 0.012f), c.P.Lit(new Color(0.72f, 0.73f, 0.75f), 0.8f), collider: false);
            k.Box(s, "Face", new Vector3(0f, 0f, 0.006f), new Vector3(w - 0.02f, h - 0.02f, 0.006f), c.P.Lit(new Color(0.07f, 0.13f, 0.27f), 0.45f), collider: false);
            k.Text(s, text, new Vector3(icon / 2f, 0f, 0.001f), 0f, size, Color.white);
            if (!person) return;
            // The standard WC figure: a round head over a body with arms and legs, in white.
            Material white = c.P.Lit(Color.white, 0.3f);
            float x = -w / 2f + size * 1.3f;
            k.Cylinder(s, "Head", new Vector3(x, size * 0.72f, 0.001f), size * 0.36f, 0.004f, white).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            k.Box(s, "Body", new Vector3(x, size * 0.12f, 0.001f), new Vector3(size * 0.5f, size * 0.72f, 0.004f), white, collider: false);
            foreach (float side in new[] { -1f, 1f })
                k.Box(s, "Leg", new Vector3(x + side * size * 0.13f, -size * 0.62f, 0.001f), new Vector3(size * 0.18f, size * 0.8f, 0.004f), white, collider: false);
        }

        /// <summary>The coffee point: a built-in counter with a sink along the meeting room's back (the machine is bought).</summary>
        private static void Kitchenette(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            k.Span(p, "Kitchenette", V(-13.8f, 0f, 2.75f), V(-9.4f, 0.9f, 3.45f), m.Dark);
            k.Span(p, "Kitchenette top", V(-13.85f, 0.9f, 2.7f), V(-9.35f, 0.94f, 3.47f), m.Stone, collider: false);
            k.Span(p, "Sink", V(-13.2f, 0.94f, 2.95f), V(-12.5f, 0.945f, 3.35f), m.Steel, collider: false);
            k.Box(p, "Tap", V(-12.85f, 1.1f, 3.38f), V(0.04f, 0.3f, 0.04f), m.Steel, collider: false);
            k.Span(p, "Upper cabinets", V(-13.8f, 1.6f, 3.1f), V(-9.4f, 2.4f, 3.45f), m.Dark, collider: false);
            k.Span(p, "Under light", V(-13.8f, 1.58f, 3.1f), V(-9.4f, 1.6f, 3.4f), m.Glow, collider: false);
            DoorSign(c, p, "COFFEE", V(-11.6f, 2.55f, 3.47f), 0f, 0.07f);
            c.PointLight(p, V(-11.5f, 3.2f, 0.5f), 7f, 0.7f, new Color(1f, 0.93f, 0.84f));
        }

        /// <summary>An acoustic tile ceiling with long linear lights over the trading floor.</summary>
        private static void Ceilings(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            var ceiling = new MeshBuilder();
            // Faces down (built from below: the quad winds the other way).
            ceiling.Quad(V(-HW, Ceiling - 0.01f, HD), V(HW, Ceiling - 0.01f, HD), V(HW, Ceiling - 0.01f, -HD), V(-HW, Ceiling - 0.01f, -HD));
            ceiling.Build(p, "Ceiling", m.Ceiling, collider: false);
            foreach (float z in new[] { -10f, -6.8f, -3.6f, -0.4f, 2.4f })
                for (float x = -11f; x < 13f; x += 6f)
                    k.Box(p, "Linear light", V(x, Ceiling - 0.03f, z), V(4.2f, 0.04f, 0.14f), m.Glow, collider: false);
            // The light itself: a grid of room lights (only those near the player burn).
            foreach (float z in new[] { -9f, -3.5f, 1.5f })
                foreach (float x in new[] { -10f, -3.5f, 3f, 9.5f })
                    c.PointLight(p, V(x, 3.3f, z), 9.5f, 0.95f, new Color(1f, 0.97f, 0.92f));
            c.PointLight(p, V(-10.5f, 3.3f, 7.5f), 8f, 0.8f, new Color(1f, 0.97f, 0.92f));
            c.PointLight(p, V(10.5f, 3.3f, 7.5f), 8f, 0.8f, new Color(1f, 0.93f, 0.84f));
            c.PointLight(p, V(0f, 3.3f, 5f), 8f, 0.9f, new Color(1f, 0.93f, 0.84f));
        }

        /// <summary>Power and data: floor boxes in a grid over the trading floor, sockets along the walls.</summary>
        private static void Services(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            Material box = c.P.Lit(new Color(0.42f, 0.43f, 0.45f), 0.4f);
            for (float x = -12f; x <= 12.1f; x += 2.8f)
                for (float z = -10.5f; z <= 1.6f; z += 3.2f)
                    k.Box(p, "Floor box", V(x, 0.011f, z), V(0.3f, 0.01f, 0.3f), box, collider: false);
            foreach (float z in new[] { 4.5f, 8.5f, 10.5f })
            {
                k.Box(p, "Socket", V(-13.93f, 0.35f, z), V(0.02f, 0.08f, 0.12f), m.White, collider: false);
                k.Box(p, "Socket", V(13.93f, 0.35f, z), V(0.02f, 0.08f, 0.12f), m.White, collider: false);
            }
        }

        /// <summary>
        /// The east wing: a temporary partition with a notice while it isn't fitted out; gone (and the space open) once
        /// the fund pays for the fit-out.
        /// </summary>
        private static void Wing(CityContext c, Transform live, Mats m, HomeWorld world)
        {
            Kit k = c.Kit;
            Transform t = Kit.Group(live, "East wing partition");
            k.Span(t, "Partition", V(WingX - 0.06f, 0f, -HD + 0.05f), V(WingX + 0.06f, Ceiling - 0.02f, 3.45f), m.Plaster);
            k.Span(t, "Hoarding", V(WingX - 0.08f, 0.9f, -4.5f), V(WingX - 0.065f, 2.3f, -1.5f), c.P.Lit(new Color(0.18f, 0.24f, 0.33f), 0.3f), collider: false);
            k.Text(t, "EAST WING", V(WingX - 0.09f, 1.9f, -3f), 90f, 0.14f, Color.white);
            k.Text(t, "FIT-OUT AVAILABLE · SPACE FOR TWO MORE DESK ROWS", V(WingX - 0.09f, 1.45f, -3f), 90f, 0.045f, new Color(0.85f, 0.9f, 1f));
            t.gameObject.AddComponent<WingPartition>().Configure(world.Game);
        }

        /// <summary>
        /// The fund's name on the brand wall as a real sign: dimensional brass letters on hidden standoffs (a stack of
        /// darker layers makes the returns, so they read as solid from any angle), brushed-steel subtitle letters, the
        /// logo on a framed plaque pinned off the wall, and two ceiling spots washing the wall. Also the entrance glass
        /// and the lobby directory by the lift.
        /// </summary>
        private static void Signs(CityContext c, Transform live, Mats m, HomeWorld world)
        {
            Kit k = c.Kit;
            // The wall's face is at z 3.5 (readers stand to the north): letters stand 30 mm proud of it.
            TextMesh[] wall = Dimensional(k, live, V(0f, 2.1f, 3.5f), 0.26f, 0.03f, 0.028f, new Color(0.9f, 0.76f, 0.47f), new Color(0.42f, 0.32f, 0.17f));
            TextMesh[] wallSub = Dimensional(k, live, V(0f, 1.62f, 3.5f), 0.07f, 0.015f, 0.01f, new Color(0.86f, 0.87f, 0.88f), new Color(0.38f, 0.39f, 0.41f));
            TextMesh glass = k.Text(live, "", V(0f, 2.6f, 6.47f), 180f, 0.08f, new Color(0.95f, 0.95f, 0.95f));

            // Logo plaque: brushed frame, the brand-coloured face, a pin at each corner.
            Transform plaque = Kit.Group(live, "Logo plaque", V(0f, 2.68f, 3.5f));
            k.Box(plaque, "Frame", V(0f, 0f, 0.03f), V(0.44f, 0.44f, 0.02f), m.Steel, collider: false);
            GameObject tile = k.Box(plaque, "Brand tile", V(0f, 0f, 0.042f), V(0.38f, 0.38f, 0.008f), c.P.Lit(Color.white, 0.55f), collider: false);
            foreach (float x in new[] { -0.17f, 0.17f })
                foreach (float y in new[] { -0.17f, 0.17f })
                    k.Cylinder(plaque, "Standoff", V(x, y, 0.047f), 0.022f, 0.006f, m.Brass).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Accent lighting: two warm spots in the ceiling, aimed down the wall.
            foreach (float x in new[] { -1.6f, 1.6f })
            {
                var go = new GameObject("Brand wall spot");
                go.transform.SetParent(live, false);
                go.transform.localPosition = V(x, Ceiling - 0.1f, 4.3f);
                go.transform.localRotation = Quaternion.LookRotation(V(-x * 0.25f, -2.2f, -0.8f).normalized);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 70f;
                l.range = 5f;
                l.intensity = 2.2f;
                l.color = new Color(1f, 0.9f, 0.76f);
                k.Cylinder(live, "Downlight", V(x, Ceiling - 0.01f, 4.3f), 0.12f, 0.02f, m.Glow);
            }

            // In the ground-floor lobby, beside the lift: the directory line for Level 26.
            float down = -Floor;
            TextMesh directory = k.Text(live, "", V(-2.6f, down + 1.6f, 8.84f), 0f, 0.05f, new Color(0.85f, 0.7f, 0.42f));
            live.gameObject.AddComponent<OfficeSigns>().Configure(world.Game, wall, wallSub, glass, directory, tile.GetComponent<Renderer>());
        }

        /// <summary>
        /// Dimensional letters: the face layer in <paramref name="face"/> <paramref name="proud"/> off the wall at
        /// <paramref name="wall"/> (world-aligned, reader to the north), and layers stepping back to <paramref name="depth"/>
        /// in <paramref name="side"/> for the letters' returns. All layers show the same text.
        /// </summary>
        private static TextMesh[] Dimensional(Kit k, Transform parent, Vector3 wall, float size, float proud, float depth, Color face, Color side)
        {
            const int layers = 8;
            var all = new TextMesh[layers + 1];
            for (int i = layers; i >= 1; i--)
            {
                float z = wall.z + proud - depth * i / layers;
                all[i] = k.Text(parent, "", V(wall.x, wall.y, z + 0.002f), 180f, size, Color.Lerp(side, face * 0.7f, 1f - i / (float)layers));
            }
            all[0] = k.Text(parent, "", V(wall.x, wall.y, wall.z + proud + 0.004f), 180f, size, face);
            return all;
        }
    }

    /// <summary>Shows the partition while the east wing isn't fitted out.</summary>
    public sealed class WingPartition : MonoBehaviour
    {
        private GameBootstrap _game;
        private bool _shown = true;

        public void Configure(GameBootstrap game) => _game = game;

        private void Update()
        {
            bool want = _game == null || !_game.Fund.Exists || !_game.Fund.ExpansionOpen;
            if (want == _shown) return;
            _shown = want;
            foreach (Transform child in transform) child.gameObject.SetActive(want);
        }
    }

    /// <summary>Keeps the office's signage in step with the fund's name and brand (or "available to lease" without one).</summary>
    public sealed class OfficeSigns : MonoBehaviour
    {
        private GameBootstrap _game;
        private TextMesh[] _wall, _wallSub;
        private TextMesh _glass, _directory;
        private Renderer _tile;
        private string _shown;

        public void Configure(GameBootstrap game, TextMesh[] wall, TextMesh[] wallSub, TextMesh glass, TextMesh directory, Renderer tile)
        {
            _game = game;
            _wall = wall;
            _wallSub = wallSub;
            _glass = glass;
            _directory = directory;
            _tile = tile;
        }

        private void Update()
        {
            if (_game == null) return;
            HedgeFund f = _game.Fund;
            string key = f.Exists ? f.Name + f.Colour + f.Logo : "";
            if (key == _shown) return;
            _shown = key;
            foreach (TextMesh t in _wall) t.text = f.Exists ? f.Name.ToUpperInvariant() : "";
            foreach (TextMesh t in _wallSub) t.text = f.Exists ? "INVESTMENT MANAGEMENT" : "";
            _glass.text = f.Exists ? f.Name : "LEVEL 26 · FULL FLOOR · AVAILABLE TO LEASE";
            _directory.text = f.Exists ? "26  " + f.Name.ToUpperInvariant() : "26  OFFICE FLOOR · TO LET";
            _tile.transform.parent.gameObject.SetActive(f.Exists);
            if (f.Exists)
            {
                var c = FundBrand.Colours[FundBrand.Clamp(f.Colour, FundBrand.Colours.Length)];
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(c.R, c.G, c.B));
                _tile.SetPropertyBlock(block);
            }
        }
    }

    /// <summary>Office finishes painted at start-up: tweed carpet tiles and a 600 mm acoustic ceiling grid.</summary>
    public static class OfficeSurfaces
    {
        private static Texture2D _carpet, _ceiling;

        private static Texture2D New(string name, int size) =>
            new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };

        /// <summary>Half-metre carpet tiles laid quarter-turned (the pile direction shows), fine tweed in each.</summary>
        public static Texture2D Carpet()
        {
            if (_carpet != null) return _carpet;
            const int size = 512, tile = 64; // 4 m tile: 0.5 m carpet tiles
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int tx = x / tile, ty = y / tile;
                    bool turned = ((tx + ty) & 1) == 1;
                    float along = turned ? x : y;
                    float tweed = 0.9f + 0.06f * Mathf.Sin(along * 1.7f) * Mathf.Sin((turned ? y : x) * 0.35f) + (Mathf.PerlinNoise(x * 0.4f, y * 0.4f) - 0.5f) * 0.12f;
                    float shade = turned ? 0.97f : 1f;
                    bool seam = x % tile == 0 || y % tile == 0;
                    float k = (seam ? 0.86f : tweed * shade);
                    px[y * size + x] = new Color(k * 0.98f, k, k * 1.02f);
                }
            _carpet = New("Carpet", size);
            _carpet.SetPixels32(px);
            _carpet.Apply(true, true);
            return _carpet;
        }

        /// <summary>600 mm acoustic ceiling tiles in a thin white grid, with a faint fissured texture.</summary>
        public static Texture2D CeilingTiles()
        {
            if (_ceiling != null) return _ceiling;
            const int size = 512;
            const float tile = size / (4f / 0.6f); // 4 m tile
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool grid = x % tile < 2f || y % tile < 2f;
                    float k = grid ? 0.99f : 0.92f - Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.05f;
                    if (grid) k = 1f;
                    else if ((x * 7 + y * 13) % 29 == 0) k -= 0.06f; // pinholes
                    px[y * size + x] = new Color(k, k, k * 0.99f);
                }
            _ceiling = New("Ceiling tiles", size);
            _ceiling.SetPixels32(px);
            _ceiling.Apply(true, true);
            return _ceiling;
        }
    }
}
