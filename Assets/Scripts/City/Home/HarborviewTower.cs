using System.Collections.Generic;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Harborview Tower (TOWN_SPEC A4, B): 88 m of glass with balconies on every floor, a stone lobby with a
    /// concierge desk and the residents' lift, and on top the Harborview Penthouse: the whole floor behind
    /// floor-to-ceiling glass (great room facing the harbour, marble kitchen, office corner, master suite with a
    /// sunset view, marble bath, walk-in closet), stairs up to a roof terrace with an infinity pool and a cabana.
    /// Plan coordinates below are metres from the tower's centre, +z north; the south side looks over downtown to
    /// the water, the west side gets the sunset.
    /// </summary>
    public static class HarborviewTower
    {
        public const float X = 252f, Z = 112f;
        /// <summary>The penthouse floor, above street grade.</summary>
        public const float PenthouseFloor = 88f;
        /// <summary>Penthouse ceiling, and the roof deck on the slab above it.</summary>
        public const float Ceiling = 3.9f, RoofDeck = 4.1f;
        private const float HW = 14f, HD = 12f;
        /// <summary>Lift shaft centre (plan z): the car's doors open south at z 9 on both stops.</summary>
        private const float LiftZ = 10.2f;
        private const float PodiumTop = 7f, FloorHeight = 3.2f;
        /// <summary>Where the stairwell's opening in the roof slab starts (plan z): the stairs start at 5.6.</summary>
        private const float StairHole = 6.6f;

        public static float Grade => StreetMap.Plan.StreetGrade(Landmarks.HarborviewLot.center);

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // ------------------------------------------------------------------ the building

        public static void Shell(CityContext c, Transform root)
        {
            Kit k = c.Kit;
            Rect lot = Landmarks.HarborviewLot;
            float g = Grade;
            Transform t = Kit.Group(root, "Harborview Tower", V(0f, g, 0f));
            Material roof = c.P.Lit(new Color(0.22f, 0.23f, 0.25f));
            Material concrete = c.P.Facade(FacadeStyle.Concrete, true);
            k.Span(t, "Forecourt", V(lot.xMin, -0.05f, lot.yMin), V(lot.xMax, 0.01f, lot.yMax), c.P.Lit(new Color(0.5f, 0.5f, 0.49f), 0.06f));
            // The podium wraps the lobby on three sides; the tower rises from it.
            k.Facade(t, "Podium W", V(234f, 0f, 96f), V(244f, PodiumTop, 128f), concrete, roof);
            k.Facade(t, "Podium E", V(260f, 0f, 96f), V(270f, PodiumTop, 128f), concrete, roof);
            k.Facade(t, "Podium N", V(244f, 0f, 124f), V(260f, PodiumTop, 128f), concrete, roof);
            k.Span(t, "Lobby roof", V(244f, 6.5f, 96f), V(260f, PodiumTop, 124f), roof);
            k.Facade(t, "Tower", V(X - HW, PodiumTop, Z - HD), V(X + HW, PenthouseFloor, Z + HD), c.P.Facade(FacadeStyle.Glass, false), roof);
            Balconies(c, t);
            Lobby(c, t);
            // Canopy over the doors with the name on its edge.
            Material dark = c.P.Lit(new Color(0.16f, 0.16f, 0.17f), 0.5f);
            k.Span(t, "Canopy", V(247.5f, 3.2f, 92.8f), V(256.5f, 3.45f, 96f), dark);
            k.Text(t, "HARBORVIEW", V(252f, 3.33f, 92.74f), 0f, 0.2f, new Color(0.92f, 0.82f, 0.6f));
            foreach (float x in new[] { 249f, 252f, 255f })
                k.Cylinder(t, "Canopy light", V(x, 3.19f, 94.4f), 0.18f, 0.02f, c.P.Lamp(new Color(0.8f, 0.8f, 0.78f), new Color(1f, 0.88f, 0.7f), 2.5f));
            c.Place(V(252f, g, 94.5f), PlaceKind.Door, "Harborview Tower");
        }

        /// <summary>
        /// Balconies with glass rails on every floor and a slab line round each one, a cream fin up the middle of the
        /// long faces, and a lit crown under the penthouse: three merged meshes for the lot.
        /// </summary>
        private static void Balconies(CityContext c, Transform t)
        {
            var slab = new MeshBuilder();
            var glass = new MeshBuilder();
            var rail = new MeshBuilder();
            float x0 = X - HW, x1 = X + HW, z0 = Z - HD, z1 = Z + HD;
            const float deep = 1.8f, wide = 5.2f;
            float[] alongX = { 241.5f, 248.3f, 255.7f, 262.5f }, alongZ = { 104f, 112f, 120f };
            for (float y = PodiumTop + FloorHeight; y < PenthouseFloor - 2f; y += FloorHeight)
            {
                // The floor line all round.
                slab.Cuboid(V(x0 - 0.08f, y - 0.1f, z0 - 0.08f), V(x1 + 0.08f, y + 0.08f, z0));
                slab.Cuboid(V(x0 - 0.08f, y - 0.1f, z1), V(x1 + 0.08f, y + 0.08f, z1 + 0.08f));
                slab.Cuboid(V(x0 - 0.08f, y - 0.1f, z0), V(x0, y + 0.08f, z1));
                slab.Cuboid(V(x1, y - 0.1f, z0), V(x1 + 0.08f, y + 0.08f, z1));
                foreach (float x in alongX)
                {
                    Balcony(slab, glass, rail, V(x - wide / 2f, y, z0 - deep), V(x + wide / 2f, y, z0), Vector3.back);
                    Balcony(slab, glass, rail, V(x - wide / 2f, y, z1), V(x + wide / 2f, y, z1 + deep), Vector3.forward);
                }
                foreach (float z in alongZ)
                {
                    Balcony(slab, glass, rail, V(x0 - deep, y, z - wide / 2f), V(x0, y, z + wide / 2f), Vector3.left);
                    Balcony(slab, glass, rail, V(x1, y, z - wide / 2f), V(x1 + deep, y, z + wide / 2f), Vector3.right);
                }
            }
            // The fins: floor to crown, just proud of the balconies.
            slab.Cuboid(V(X - 0.8f, PodiumTop, z0 - 0.5f), V(X + 0.8f, PenthouseFloor - 0.4f, z0));
            slab.Cuboid(V(X - 0.8f, PodiumTop, z1), V(X + 0.8f, PenthouseFloor - 0.4f, z1 + 0.5f));
            slab.Build(t, "Balcony slabs", c.P.Lit(new Color(0.86f, 0.83f, 0.74f), 0.15f), collider: false);
            glass.Build(t, "Balcony glass", c.P.Glass(new Color(0.55f, 0.7f, 0.78f, 0.35f)), collider: false)
                .GetComponent<MeshRenderer>()?.SetShadows(false);
            rail.Build(t, "Balcony rails", c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.5f), collider: false);
            // A lit band under the penthouse glass: the crown you see across town at night.
            var crown = new MeshBuilder();
            crown.Cuboid(V(x0 - 0.1f, PenthouseFloor - 0.45f, z0 - 0.1f), V(x1 + 0.1f, PenthouseFloor, z1 + 0.1f));
            crown.Build(t, "Crown", c.P.Lamp(new Color(0.7f, 0.72f, 0.74f), new Color(0.6f, 0.85f, 1f), 1.6f), collider: false);
        }

        /// <summary>One balcony: slab, glass on the three open sides, a top rail. <paramref name="out_"/> points away from the tower.</summary>
        private static void Balcony(MeshBuilder slab, MeshBuilder glass, MeshBuilder rail, Vector3 min, Vector3 max, Vector3 out_)
        {
            float y = min.y;
            slab.Cuboid(V(min.x, y - 0.1f, min.z), V(max.x, y + 0.12f, max.z));
            const float h = 1.05f;
            var corners = new List<(Vector3 A, Vector3 B)>();
            // The sides that aren't against the wall, as a→b runs round the outside.
            Vector3 a = V(min.x, y + 0.12f, min.z), b = V(max.x, y + 0.12f, min.z), cc = V(max.x, y + 0.12f, max.z), d = V(min.x, y + 0.12f, max.z);
            if (out_ == Vector3.back) corners.AddRange(new[] { (d, a), (a, b), (b, cc) });
            else if (out_ == Vector3.forward) corners.AddRange(new[] { (b, cc), (cc, d), (d, a) });
            else if (out_ == Vector3.left) corners.AddRange(new[] { (cc, d), (d, a), (a, b) });
            else corners.AddRange(new[] { (a, b), (b, cc), (cc, d) });
            foreach (var (p, q) in corners)
            {
                // Glass seen from both sides.
                glass.Wall(p, q, h);
                glass.Wall(q, p, h);
                Vector3 lo = Vector3.Min(p, q), hi = Vector3.Max(p, q);
                rail.Cuboid(lo + V(-0.03f, h, -0.03f), hi + V(0.03f, h + 0.05f, 0.03f));
            }
        }

        /// <summary>The lobby: stone floor, glass front, a slatted lift wall, the concierge desk and a sitting area.</summary>
        private static void Lobby(CityContext c, Transform t)
        {
            Kit k = c.Kit;
            Material glass = c.P.Glass(new Color(0.72f, 0.82f, 0.86f, 0.18f));
            Material frame = c.P.Lit(new Color(0.14f, 0.13f, 0.12f), 0.5f);
            Material stone = c.P.Lit(new Color(0.78f, 0.73f, 0.66f), 0.2f);
            Material walnut = c.P.Lit(new Color(0.3f, 0.19f, 0.12f), 0.35f);
            Material marble = c.P.Textured("marble", Surfaces.Marble(), Color.white, 0.75f);
            Material leather = c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.35f);

            var floor = new MeshBuilder();
            floor.Quad(V(244f, 0.02f, 96f), V(260f, 0.02f, 96f), V(260f, 0.02f, 121f), V(244f, 0.02f, 121f));
            floor.Build(t, "Lobby floor", c.P.Textured("lobby stone", Surfaces.Tiles(), new Color(0.9f, 0.87f, 0.82f), 0.55f), collider: false);
            k.Span(t, "Lobby ceiling", V(244f, 6.4f, 96f), V(260f, 6.5f, 121f), c.P.Lit(new Color(0.95f, 0.95f, 0.94f)), collider: false);

            // Glass front with the doors in the middle.
            k.WallX(t, "Lobby glass", 244f, 260f, 96f, 0f, 6.4f, 0.04f, glass, new Opening(252f, 2.4f, 0f, 2.7f));
            foreach (float x in new[] { 244.05f, 246f, 248f, 250.8f, 253.2f, 256f, 258f, 259.95f })
                k.Box(t, "Mullion", V(x, 3.2f, 96f), V(0.08f, 6.4f, 0.12f), frame, collider: false);
            k.Box(t, "Transom", V(252f, 2.75f, 96f), V(16f, 0.1f, 0.12f), frame, collider: false);
            c.SlidingDoor(t, "Harborview lobby", V(252f, 0f, 96f), 2.4f, 2.6f);

            // Stone side walls (the podium's facade faces are outside).
            k.Span(t, "Lobby wall W", V(244f, 0f, 96.1f), V(244.12f, 6.4f, 121f), stone);
            k.Span(t, "Lobby wall E", V(259.88f, 0f, 96.1f), V(260f, 6.4f, 121f), stone);

            // The lift wall: timber slats round the doors, the name in brass above.
            k.WallX(t, "Lift wall", 244f, 260f, 121f, 0f, 6.4f, 0.2f, stone, Opening.Door(252f, 1.3f, 0f, 2.3f));
            var slats = new MeshBuilder();
            slats.Panel(V(244.12f, 0f, 120.88f), V(251.3f, 0f, 120.88f), 6.4f, 1f);
            slats.Panel(V(252.7f, 0f, 120.88f), V(259.88f, 0f, 120.88f), 6.4f, 1f);
            slats.Panel(V(251.3f, 2.3f, 120.88f), V(252.7f, 2.3f, 120.88f), 4.1f, 1f);
            slats.Build(t, "Slats", c.P.Textured("slats", Surfaces.Slats(), Color.white, 0.3f), collider: false);
            k.Text(t, "HARBORVIEW", V(252f, 4.2f, 120.84f), 0f, 0.42f, new Color(0.85f, 0.7f, 0.42f));
            k.Box(t, "Lift surround", V(252f, 2.36f, 120.86f), V(1.6f, 0.12f, 0.06f), c.P.Lit(new Color(0.75f, 0.6f, 0.35f), 0.7f), collider: false);

            // Concierge desk on the east side, facing the doors.
            k.Span(t, "Concierge desk", V(256.4f, 0f, 103f), V(257.5f, 1.05f, 108.5f), walnut);
            k.Span(t, "Desk top", V(256.3f, 1.05f, 102.9f), V(257.6f, 1.1f, 108.6f), marble, collider: false);
            k.Span(t, "Desk glow", V(256.35f, 0.04f, 103.05f), V(256.4f, 0.1f, 108.45f), c.P.Glow(new Color(1f, 0.82f, 0.6f), 1.4f), collider: false);
            k.Box(t, "Desk screen", V(257.2f, 1.3f, 105f), V(0.04f, 0.3f, 0.5f), leather, collider: false);

            // Sitting area on the west side: two sofas round a low table, on a rug.
            k.Cylinder(t, "Lobby rug", V(248f, 0.03f, 107f), 5.2f, 0.01f, c.P.Lit(new Color(0.55f, 0.52f, 0.48f), 0.02f));
            foreach (float z in new[] { 104.6f, 109.4f })
            {
                float s = z < 107f ? -1f : 1f;
                k.Span(t, "Sofa", V(246.4f, 0f, z - 0.45f), V(249.6f, 0.42f, z + 0.45f), leather);
                k.Span(t, "Sofa back", V(246.4f, 0.42f, z + s * 0.3f - 0.15f), V(249.6f, 0.85f, z + s * 0.3f + 0.15f), leather, collider: false);
            }
            k.Span(t, "Lobby table", V(247.3f, 0f, 106.4f), V(248.7f, 0.4f, 107.6f), marble);
            foreach (Vector3 at in new[] { V(245f, 0f, 97.3f), V(259f, 0f, 97.3f), V(245f, 0f, 119.8f), V(259f, 0f, 119.8f) })
                Plant(c, t, at, 2.2f);

            // Big disc pendants down the middle.
            Material glow = c.P.Glow(new Color(1f, 0.86f, 0.66f), 1.5f);
            foreach (float z in new[] { 101f, 108f, 115f })
            {
                k.Cylinder(t, "Pendant", V(252f, 4.9f, z), 1.6f, 0.08f, glow);
                k.Box(t, "Pendant rod", V(252f, 5.65f, z), V(0.02f, 1.5f, 0.02f), frame, collider: false);
                c.PointLight(t, V(252f, 4.6f, z), 11f, 1f, new Color(1f, 0.9f, 0.76f));
            }
        }

        /// <summary>A planter: a dark pot with a clump of leaves.</summary>
        private static void Plant(CityContext c, Transform t, Vector3 at, float height)
        {
            Kit k = c.Kit;
            k.Cylinder(t, "Pot", at + V(0f, 0.3f, 0f), 0.6f, 0.6f, c.P.Lit(new Color(0.18f, 0.18f, 0.19f), 0.3f), collider: true);
            Material leaf = c.P.Lit(new Color(0.2f, 0.42f, 0.22f), 0.2f);
            k.Sphere(t, "Leaves", at + V(0f, height * 0.62f, 0f), height * 0.45f, leaf);
            k.Sphere(t, "Leaves", at + V(0.15f, height * 0.88f, -0.1f), height * 0.32f, leaf);
        }

        // ------------------------------------------------------------------ the penthouse

        /// <summary>Builds the penthouse, the lift up to it and its for-sale sign; returns its <see cref="HomeSpec"/>.</summary>
        public static HomeSpec Penthouse(CityContext c, HomeWorld world, Transform root, Transform dyn)
        {
            float g = Grade, fy = g + PenthouseFloor;
            // The home's frame faces -z = north, where you come in from the lift (houses face their street the same way).
            Transform home = Kit.Group(root, "Harborview Penthouse", V(X, fy, Z), 180f);
            Transform p = Kit.Group(home, "Rooms", Vector3.zero, 180f); // plan-aligned again: +z north
            var m = new Mats(c);

            Floors(c, p, m);
            Glass(c, p, m);
            Walls(c, p, m);
            GreatRoom(c, p, m);
            Kitchen(c, p, m);
            Suite(c, p, m);
            Stairs(c, p, m);
            Terrace(c, p, m);
            c.SwingDoor(p, "Penthouse", V(-0.55f, 0f, 5.5f), 1.1f, 2.4f, m.Walnut);

            Elevator lift = Lift(c, dyn, V(X, g + 0.02f, Z + LiftZ), PenthouseFloor - 0.02f);
            // Only residents (or open-house visitors) ride up; anyone can come down.
            foreach (ElevatorButton button in lift.GetComponentsInChildren<ElevatorButton>())
            {
                bool inCar = button.name.StartsWith("Button");
                if ((inCar && button.Floor == 1) || (!inCar && button.Floor == 0))
                    button.LockReason = () => world.Estate.Owns(HomeSales.PenthouseId) || HomeSales.OpenHouse.Contains(world.Game.Clock.Now) ? null : "residents only";
            }

            var spec = new HomeSpec
            {
                Id = HomeSales.PenthouseId, Name = "Harborview Penthouse", Kind = HomeKind.Penthouse, Price = HomeSpec.PriceOf(HomeKind.Penthouse),
                Root = home, Size = new Vector2(HW * 2f, HD * 2f), Floors = 1, Height = Ceiling,
                Plot = Kit.Group(dyn, "Penthouse terrace", V(X, fy + RoofDeck, Z - HD)), LotWidth = HW * 2f, LotDepth = HD * 2f,
                DoorLocal = V(0f, 0f, -5.5f),
            };
            Stage(spec, fy);

            // The sign's in the lobby, by the lift.
            Kit k = c.Kit;
            Transform sign = Kit.Group(dyn, "For sale penthouse", V(X + 2.6f, g, 118.4f));
            GameObject board = k.Box(sign, "Board", V(0f, 1.3f, 0f), V(0.9f, 0.6f, 0.04f), c.P.Lit(new Color(0.1f, 0.2f, 0.35f), 0.3f));
            TextMesh text = k.Text(sign, "PENTHOUSE", V(0f, 1.4f, -0.03f), 0f, 0.09f, Color.white);
            k.Text(sign, "88 M UP · POOL · FURNISHED", V(0f, 1.2f, -0.03f), 0f, 0.035f, new Color(0.85f, 0.9f, 1f));
            k.Box(sign, "Post", V(0f, 0.5f, 0f), V(0.08f, 1f, 0.08f), m.Frame, collider: false);
            board.AddComponent<ForSaleSign>().Configure(world, spec, text);

            c.Anchor("penthouse_lobby", V(X, g, Z + LiftZ - 4f));
            c.Anchor("penthouse_inside", V(X + 1f, fy + 0.05f, Z - 3f));
            c.Anchor("penthouse_terrace", V(X - 1f, fy + RoofDeck + 0.05f, Z - 5f));
            return spec;
        }

        /// <summary>What comes with it (yours to keep, move or sell once it's bought): plan x/z, facing yaw.</summary>
        private static void Stage(HomeSpec spec, float fy)
        {
            void Put(string id, float x, float z, float yaw, int variant = 0) =>
                spec.Staging.Add((id, variant, V(X + x, fy + 0.01f, Z + z), yaw));
            // Living, facing the harbour.
            Put("sofa_premium", 0.8f, -5.3f, 0f, 0);
            Put("coffee_table", 0.8f, -7.7f, 0f, 1);
            Put("armchair", -2f, -8.1f, 270f, 1);
            Put("armchair", 3.6f, -8.1f, 90f, 1);
            Put("floor_lamp", -1.7f, -4.6f, 0f);
            Put("plant", -3.4f, -11.3f, 0f);
            Put("plant", 6f, -11.3f, 0f);
            // Dining by the kitchen.
            Put("dining_table", 8.5f, 0.8f, 0f, 1);
            Put("dining_chair", 8.1f, 0.1f, 180f, 1);
            Put("dining_chair", 8.9f, 0.1f, 180f, 1);
            Put("dining_chair", 8.1f, 1.5f, 0f, 1);
            Put("dining_chair", 8.9f, 1.5f, 0f, 1);
            // The office corner: a trading desk looking out over the water.
            Put("desk_trading", 11.2f, -10.9f, 180f, 1);
            Put("chair_ergo", 11.2f, -9.8f, 0f);
            Put("plant", 13.3f, -6.4f, 0f);
            // The master suite: a king bed against the slatted wall, facing the sunset corner.
            Put("bed_king", -8.5f, 0.75f, 0f, 1);
            Put("nightstand", -10.1f, 1.6f, 0f, 1);
            Put("nightstand", -6.9f, 1.6f, 0f, 1);
            Put("shoe_bench", -8.5f, -0.95f, 0f, 1);
            Put("armchair", -12.6f, -10.6f, 45f, 1);
        }

        /// <summary>The penthouse's materials, made once.</summary>
        private sealed class Mats
        {
            public readonly Material Oak, Marble, Stone, Slats, Plaster, Ceiling, Frame, Glass, Walnut, Cabinet, Glow, Brass, White, Leather;

            public Mats(CityContext c)
            {
                Oak = c.P.Textured("oak", Surfaces.Oak(), Color.white, 0.35f);
                Marble = c.P.Textured("marble", Surfaces.Marble(), Color.white, 0.75f);
                Stone = c.P.Textured("terrace stone", Surfaces.Tiles(), new Color(0.86f, 0.83f, 0.78f), 0.12f);
                Slats = c.P.Textured("slats", Surfaces.Slats(), Color.white, 0.3f);
                Plaster = c.P.Bounce(new Color(0.93f, 0.92f, 0.9f), 0.14f);
                Ceiling = c.P.Bounce(new Color(0.96f, 0.96f, 0.95f), 0.3f, 0.02f);
                Frame = c.P.Lit(new Color(0.13f, 0.12f, 0.11f), 0.5f);
                Glass = c.P.Glass(new Color(0.78f, 0.86f, 0.9f, 0.1f));
                Walnut = c.P.Lit(new Color(0.3f, 0.19f, 0.12f), 0.35f);
                Cabinet = c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.45f);
                Glow = c.P.Glow(new Color(1f, 0.85f, 0.66f), 1.6f);
                Brass = c.P.Lit(new Color(0.75f, 0.6f, 0.35f), 0.75f);
                White = c.P.Lit(new Color(0.96f, 0.96f, 0.95f), 0.85f);
                Leather = c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.35f);
            }
        }

        private static void Floors(CityContext c, Transform p, Mats m)
        {
            var oak = new MeshBuilder();
            var marble = new MeshBuilder();
            void Tile(MeshBuilder mb, float x0, float z0, float x1, float z1, float y) =>
                mb.Quad(V(x0, y, z0), V(x1, y, z0), V(x1, y, z1), V(x0, y, z1));
            Tile(oak, -HW, -HD, HW, 2f, 0.006f);
            Tile(oak, -7f, 2f, -3f, HD, 0.006f);
            Tile(oak, -3f, 2f, HW, 5.5f, 0.006f);
            Tile(oak, 3f, 5.5f, HW, HD, 0.006f);
            Tile(marble, -HW, 2f, -7f, HD, 0.006f);
            Tile(marble, -3f, 5.5f, 3f, 9f, 0.006f);
            oak.Build(p, "Oak floor", m.Oak, collider: false);
            marble.Build(p, "Marble floor", m.Marble, collider: false);

            // The ceiling slab is the roof deck; the stairwell comes up through it from StairHole north, so the slab
            // is always well above a climbing player's head (1.75 m and a step's lift over the tread).
            Kit k = c.Kit;
            k.Span(p, "Ceiling", V(-HW, Ceiling, -HD), V(3f, RoofDeck, HD), m.Ceiling);
            k.Span(p, "Ceiling", V(4.6f, Ceiling, -HD), V(HW, RoofDeck, HD), m.Ceiling);
            k.Span(p, "Ceiling", V(3f, Ceiling, -HD), V(4.6f, RoofDeck, StairHole), m.Ceiling);
        }

        /// <summary>Floor-to-ceiling glass round the whole floor, slim bronze mullions; the core's north face is solid.</summary>
        private static void Glass(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            void Side(Vector3 a, Vector3 b)
            {
                GameObject pane = k.Span(p, "Window", Vector3.Min(a, b) + V(0f, 0f, 0f), Vector3.Max(a, b) + V(0f, Ceiling, 0f), m.Glass);
                pane.GetComponent<MeshRenderer>().SetShadows(false);
                float len = Vector3.Distance(a, b);
                int bays = Mathf.Max(1, Mathf.RoundToInt(len / 2.3f));
                for (int i = 0; i <= bays; i++)
                {
                    Vector3 at = Vector3.Lerp(a, b, i / (float)bays);
                    k.Box(p, "Mullion", at + V(0f, Ceiling / 2f, 0f), V(0.09f, Ceiling, 0.09f), m.Frame, collider: false);
                }
                k.Span(p, "Sill", Vector3.Min(a, b) + V(-0.05f, 0f, -0.05f), Vector3.Max(a, b) + V(0.05f, 0.05f, 0.05f), m.Frame, collider: false);
            }
            const float t = 0.02f;
            Side(V(-HW, 0f, -HD - t), V(HW, 0f, -HD + t));
            Side(V(-HW - t, 0f, -HD), V(-HW + t, 0f, HD));
            Side(V(HW - t, 0f, -HD), V(HW + t, 0f, HD));
            Side(V(-HW, 0f, HD - t), V(-7f, 0f, HD + t));
            Side(V(3f, 0f, HD - t), V(HW, 0f, HD + t));
            k.Span(p, "Core face", V(-7f, 0f, HD - 0.12f), V(3f, Ceiling, HD), m.Frame);
        }

        private static void Walls(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            const float t = 0.15f;
            // Living | bedroom, with the bedroom door near the north end.
            k.WallZ(p, "Partition", -HD, 2f, -4f, 0f, Ceiling, t, m.Plaster, Opening.Door(-0.4f, 1f, 0f, 2.4f));
            // Bedroom | bath and closet.
            k.WallX(p, "Suite wall", -HW, -3f, 2f, 0f, Ceiling, t, m.Plaster, Opening.Door(-11f, 0.95f, 0f, 2.4f), Opening.Door(-5.5f, 1.2f, 0f, 2.4f));
            k.WallZ(p, "Bath wall", 2f, HD, -7f, 0f, Ceiling, t, m.Plaster);
            k.WallZ(p, "Closet wall", 2f, HD, -3f, 0f, Ceiling, t, m.Plaster);
            // The foyer: front door south into the great room, the lift north.
            k.WallX(p, "Foyer wall", -3f, 3f, 5.5f, 0f, Ceiling, t, m.Plaster, Opening.Door(0f, 1.1f, 0f, 2.4f));
            k.WallX(p, "Lift wall", -3f, 3f, 9f, 0f, Ceiling, 0.2f, m.Walnut, Opening.Door(0f, 1.3f, 0f, 2.3f));
            k.Span(p, "Core", V(-3f, 0f, 9.1f), V(-1.45f, Ceiling, HD - 0.12f), m.Plaster);
            k.Span(p, "Core", V(1.45f, 0f, 9.1f), V(3f, Ceiling, HD - 0.12f), m.Plaster);
            k.Span(p, "Core", V(-1.45f, 0f, 11.45f), V(1.45f, Ceiling, HD - 0.12f), m.Plaster);
            // Stairwell, open to the great room at its south end.
            k.WallZ(p, "Stair wall W", 5.5f, HD, 3f, 0f, Ceiling, t, m.Plaster);
            k.WallZ(p, "Stair wall E", 5.5f, HD, 4.6f, 0f, Ceiling, t, m.Plaster);

            // Foyer: a console, a round mirror and a pendant.
            k.Span(p, "Console", V(-2.9f, 0.75f, 6.6f), V(-2.5f, 0.8f, 8.2f), m.Marble, collider: false);
            k.Span(p, "Console legs", V(-2.85f, 0f, 6.7f), V(-2.55f, 0.75f, 8.1f), m.Brass);
            GameObject mirror = k.Cylinder(p, "Mirror", V(-2.91f, 1.7f, 7.4f), 0.9f, 0.02f, m.White);
            mirror.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            k.Sphere(p, "Foyer pendant", V(0f, 2.9f, 7.2f), 0.45f, m.Glow);
            c.PointLight(p, V(0f, 3.3f, 7.2f), 6f, 0.7f, new Color(1f, 0.9f, 0.78f));
        }

        /// <summary>A tray ceiling with cove light and downlights, a stone fireplace wall, a big round rug.</summary>
        private static void GreatRoom(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            const float x0 = -4f, x1 = HW, z0 = -HD, z1 = 5.5f, band = 1.1f, drop = 0.4f;
            float y = Ceiling - drop;
            k.Span(p, "Soffit", V(x0, y, z0), V(x1, Ceiling, z0 + band), m.Ceiling, collider: false);
            k.Span(p, "Soffit", V(x0, y, z1 - band), V(x1, Ceiling, z1), m.Ceiling, collider: false);
            k.Span(p, "Soffit", V(x0, y, z0 + band), V(x0 + band, Ceiling, z1 - band), m.Ceiling, collider: false);
            k.Span(p, "Soffit", V(x1 - band, y, z0 + band), V(x1, Ceiling, z1 - band), m.Ceiling, collider: false);
            // Cove light: a warm line along the soffit's inner lip, all round the tray.
            float ix0 = x0 + band, ix1 = x1 - band, iz0 = z0 + band, iz1 = z1 - band, ly = Ceiling - 0.08f;
            k.Span(p, "Cove", V(ix0, ly, iz0 - 0.03f), V(ix1, ly + 0.05f, iz0), m.Glow, collider: false);
            k.Span(p, "Cove", V(ix0, ly, iz1), V(ix1, ly + 0.05f, iz1 + 0.03f), m.Glow, collider: false);
            k.Span(p, "Cove", V(ix0 - 0.03f, ly, iz0), V(ix0, ly + 0.05f, iz1), m.Glow, collider: false);
            k.Span(p, "Cove", V(ix1, ly, iz0), V(ix1 + 0.03f, ly + 0.05f, iz1), m.Glow, collider: false);
            // Downlights in the soffit.
            for (float x = x0 + 1.6f; x < x1 - 1f; x += 2.4f)
            {
                k.Cylinder(p, "Downlight", V(x, y - 0.01f, z0 + band / 2f), 0.14f, 0.02f, m.Glow);
                k.Cylinder(p, "Downlight", V(x, y - 0.01f, z1 - band / 2f), 0.14f, 0.02f, m.Glow);
            }
            foreach (Vector3 at in new[] { V(0.5f, 3.4f, -7f), V(8.5f, 3.4f, -7.5f), V(8.5f, 3.4f, 0.5f), V(0.5f, 3.4f, 0f) })
                c.PointLight(p, at, 10.5f, 1.15f, new Color(1f, 0.92f, 0.8f));

            // Fireplace wall on the partition: a marble slab with a long, low fire.
            var slab = new MeshBuilder();
            slab.Panel(V(-3.92f, 0f, -10f), V(-3.92f, 0f, -4.4f), Ceiling - drop, 0.25f);
            slab.Build(p, "Fireplace wall", m.Marble, collider: false);
            k.Span(p, "Firebox", V(-3.92f, 0.35f, -8.6f), V(-3.86f, 0.85f, -5.8f), m.Leather, collider: false);
            k.Span(p, "Flames", V(-3.93f, 0.4f, -8.4f), V(-3.85f, 0.5f, -6f), c.P.Glow(new Color(1f, 0.55f, 0.2f), 2.2f), collider: false);
            k.Span(p, "Hearth", V(-3.92f, 0f, -9.2f), V(-3.4f, 0.28f, -5.2f), m.Marble);
            k.Cylinder(p, "Rug", V(0.8f, 0.012f, -7.4f), 4.2f, 0.01f, c.P.Lit(new Color(0.72f, 0.7f, 0.67f), 0.02f));
        }

        /// <summary>Dark cabinets under a marble top along the glass, a tall pantry wall, and a waterfall island with stools.</summary>
        private static void Kitchen(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            k.Span(p, "Pantry", V(4.68f, 0f, 6.2f), V(5.3f, 2.6f, 11.85f), m.Cabinet);
            k.Span(p, "Pantry glow", V(5.3f, 2.55f, 6.2f), V(5.33f, 2.6f, 11.85f), m.Glow, collider: false);
            k.Span(p, "Counter", V(5.3f, 0f, 11.3f), V(13.3f, 0.86f, 11.9f), m.Cabinet);
            k.Span(p, "Counter top", V(5.3f, 0.86f, 11.25f), V(13.3f, 0.9f, 11.9f), m.Marble, collider: false);
            k.Span(p, "Counter", V(13.3f, 0f, 7f), V(13.9f, 0.86f, 11.9f), m.Cabinet);
            k.Span(p, "Counter top", V(13.25f, 0.86f, 7f), V(13.9f, 0.9f, 11.9f), m.Marble, collider: false);
            k.Span(p, "Sink", V(8.6f, 0.9f, 11.4f), V(9.6f, 0.905f, 11.8f), m.Leather, collider: false);
            k.Box(p, "Tap", V(9.1f, 1.1f, 11.78f), V(0.04f, 0.4f, 0.04f), m.Brass, collider: false);
            k.Span(p, "Cooktop", V(13.4f, 0.9f, 8.6f), V(13.8f, 0.905f, 9.6f), m.Leather, collider: false);
            // The island: one marble block, waterfall ends.
            k.Span(p, "Island", V(7.4f, 0f, 7.8f), V(11.6f, 0.94f, 8.9f), m.Marble);
            foreach (float x in new[] { 8.2f, 9.5f, 10.8f })
            {
                k.Cylinder(p, "Stool seat", V(x, 0.74f, 7.25f), 0.42f, 0.07f, m.Leather, collider: true);
                k.Box(p, "Stool post", V(x, 0.37f, 7.25f), V(0.05f, 0.72f, 0.05f), m.Brass, collider: false);
                k.Cylinder(p, "Stool foot", V(x, 0.01f, 7.25f), 0.38f, 0.02f, m.Brass);
                k.Sphere(p, "Pendant", V(x, 2.55f, 8.35f), 0.3f, m.Glow);
                k.Box(p, "Pendant cord", V(x, 3.25f, 8.35f), V(0.01f, 1.3f, 0.01f), m.Frame, collider: false);
            }
            c.PointLight(p, V(9.5f, 3.3f, 8.5f), 8f, 0.9f, new Color(1f, 0.92f, 0.8f));
        }

        /// <summary>Master bedroom (slatted headboard wall, sconces), marble bath with a tub at the glass, walk-in closet.</summary>
        private static void Suite(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            var slats = new MeshBuilder();
            slats.Panel(V(-10.7f, 0f, 1.91f), V(-6.3f, 0f, 1.91f), Ceiling, 1f);
            slats.Build(p, "Headboard wall", m.Slats, collider: false);
            foreach (float x in new[] { -10.4f, -6.6f })
                k.Box(p, "Sconce", V(x, 1.35f, 1.86f), V(0.12f, 0.3f, 0.06f), m.Glow, collider: false);
            c.PointLight(p, V(-9f, 3.4f, -4.5f), 9f, 0.75f, new Color(1f, 0.88f, 0.72f));
            for (float x = -12.5f; x < -4.5f; x += 2.5f)
                for (float z = -10f; z < 0f; z += 3f)
                    k.Cylinder(p, "Downlight", V(x, Ceiling - 0.01f, z), 0.12f, 0.02f, m.Glow);

            // Bath: tub by the west glass, a double vanity on the closet wall, a glass shower in the corner.
            k.Span(p, "Tub", V(-13.3f, 0f, 7.4f), V(-12.35f, 0.6f, 9.3f), m.White);
            k.Span(p, "Tub water", V(-13.2f, 0.6f, 7.5f), V(-12.45f, 0.605f, 9.2f), c.P.Lit(new Color(0.78f, 0.86f, 0.9f), 0.95f), collider: false);
            k.Box(p, "Tub filler", V(-12.1f, 0.5f, 8.35f), V(0.05f, 1f, 0.05f), m.Brass, collider: false);
            k.Span(p, "Vanity", V(-7.6f, 0.2f, 3.4f), V(-7.08f, 0.85f, 6.8f), m.Walnut);
            k.Span(p, "Vanity top", V(-7.62f, 0.85f, 3.38f), V(-7.08f, 0.9f, 6.82f), m.Marble, collider: false);
            foreach (float z in new[] { 4.3f, 5.9f })
            {
                k.Cylinder(p, "Basin", V(-7.35f, 0.96f, z), 0.4f, 0.12f, m.White);
                k.Box(p, "Tap", V(-7.12f, 1.12f, z), V(0.04f, 0.25f, 0.04f), m.Brass, collider: false);
            }
            k.Span(p, "Mirror", V(-7.1f, 1.2f, 3.5f), V(-7.08f, 2.3f, 6.7f), m.White, collider: false);
            k.Span(p, "Mirror light", V(-7.1f, 2.33f, 3.5f), V(-7.08f, 2.38f, 6.7f), m.Glow, collider: false);
            k.WallX(p, "Shower glass", -9.5f, -8.3f, 10f, 0f, 2.3f, 0.02f, m.Glass);
            k.WallZ(p, "Shower glass", 10f, HD - 0.05f, -9.5f, 0f, 2.3f, 0.02f, m.Glass);
            k.Cylinder(p, "Rain head", V(-8.3f, Ceiling - 0.03f, 11f), 0.35f, 0.02f, m.Brass);
            c.PointLight(p, V(-10.5f, 3.4f, 7f), 7f, 0.75f, new Color(1f, 0.94f, 0.86f));

            // Closet: lit shelving down both sides, a dresser island.
            foreach (float x in new[] { -6.93f, -3.5f })
            {
                k.Span(p, "Shelving", V(x, 0f, 3.2f), V(x + 0.43f, 2.4f, 11.6f), m.Walnut);
                k.Span(p, "Shelf light", V(x, 2.4f, 3.2f), V(x + 0.43f, 2.44f, 11.6f), m.Glow, collider: false);
            }
            k.Span(p, "Dresser", V(-5.6f, 0f, 5.8f), V(-4.6f, 0.9f, 8.6f), m.Walnut);
            k.Span(p, "Dresser top", V(-5.62f, 0.9f, 5.78f), V(-4.58f, 0.92f, 8.62f), m.Glass, collider: false);
            c.PointLight(p, V(-5f, 3.4f, 7f), 7f, 0.6f, new Color(1f, 0.94f, 0.86f));
        }

        /// <summary>A straight oak flight from the great room up the stairwell to a glass pavilion on the roof.</summary>
        private static void Stairs(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            const float z0 = 5.6f, run = 0.28f;
            int steps = Mathf.CeilToInt(RoofDeck / 0.19f);
            float rise = RoofDeck / steps;
            for (int i = 0; i < steps; i++)
            {
                float zEnd = i == steps - 1 ? HD - 0.05f : z0 + (i + 1) * run;
                k.Span(p, "Step", V(3.08f, i * rise - 0.02f, z0 + i * run), V(4.52f, (i + 1) * rise, zEnd), m.Walnut);
            }
            k.Box(p, "Handrail", V(4.48f, 2.1f, 8.7f), V(0.04f, 0.04f, 6.3f), m.Brass, collider: false);
            c.PointLight(p, V(3.8f, 3.4f, 7f), 6f, 0.6f, new Color(1f, 0.94f, 0.86f));
            // The pavilion over the stairwell; step out east at the top.
            // Its walls start at the ceiling, so there's no slot through the slab's edge.
            float y0 = Ceiling, y1 = RoofDeck + 2.6f;
            k.WallX(p, "Pavilion", 3f, 4.6f, StairHole, y0, y1, 0.03f, m.Glass);
            k.WallZ(p, "Pavilion", StairHole, HD, 3f, y0, y1, 0.03f, m.Glass);
            k.WallZ(p, "Pavilion", StairHole, HD, 4.6f, y0, y1, 0.03f, m.Glass, new Opening(10.9f, 1.8f, RoofDeck, RoofDeck + 2.3f));
            k.WallX(p, "Pavilion", 3f, 4.6f, HD - 0.02f, y0, y1, 0.03f, m.Glass);
            k.Span(p, "Pavilion roof", V(2.9f, y1, StairHole - 0.1f), V(4.7f, y1 + 0.12f, HD), m.Frame);
        }

        /// <summary>
        /// The roof: stone deck, glass balustrade all round, an infinity pool along the harbour edge (it glows at
        /// night), sun loungers, a slatted cabana with a sofa, planters, and uplights.
        /// </summary>
        private static void Terrace(CityContext c, Transform p, Mats m)
        {
            Kit k = c.Kit;
            float y = RoofDeck;
            var deck = new MeshBuilder();
            void Tile(float x0, float z0, float x1, float z1) => deck.Quad(V(x0, y + 0.004f, z0), V(x1, y + 0.004f, z0), V(x1, y + 0.004f, z1), V(x0, y + 0.004f, z1));
            Tile(-HW, -HD, 3f, HD);
            Tile(4.6f, -HD, HW, HD);
            Tile(3f, -HD, 4.6f, StairHole);
            deck.Build(p, "Terrace deck", m.Stone, collider: false);

            // Glass balustrade on the edge, a slim rail on top.
            const float h = 1.15f, i = 0.08f;
            foreach (var (a, b) in new[] { (V(-HW + i, y, -HD + i), V(HW - i, y, -HD + i)), (V(-HW + i, y, HD - i), V(HW - i, y, HD - i)),
                                            (V(-HW + i, y, -HD + i), V(-HW + i, y, HD - i)), (V(HW - i, y, -HD + i), V(HW - i, y, HD - i)) })
            {
                GameObject pane = k.Span(p, "Balustrade", Vector3.Min(a, b) - V(0.015f, 0f, 0.015f), Vector3.Max(a, b) + V(0.015f, h, 0.015f), m.Glass);
                pane.GetComponent<MeshRenderer>().SetShadows(false);
                k.Span(p, "Rail", Vector3.Min(a, b) + V(-0.03f, h, -0.03f), Vector3.Max(a, b) + V(0.03f, h + 0.05f, 0.03f), m.Frame, collider: false);
            }
            // An invisible wall 4 m high just inside the glass: nobody goes over the edge, however they jump.
            const float guard = 4f, gt = 0.3f;
            foreach (var (min, max) in new[] { (V(-HW, y, -HD), V(HW, y + guard, -HD + gt)), (V(-HW, y, HD - gt), V(HW, y + guard, HD)),
                                                (V(-HW, y, -HD), V(-HW + gt, y + guard, HD)), (V(HW - gt, y, -HD), V(HW, y + guard, HD)) })
            {
                var wall = new GameObject("Edge guard");
                wall.transform.SetParent(p, false);
                var box = wall.AddComponent<BoxCollider>();
                box.center = (min + max) / 2f;
                box.size = max - min;
            }
            Material uplight = c.P.Lamp(new Color(0.5f, 0.5f, 0.48f), new Color(1f, 0.85f, 0.62f), 2.4f);
            for (float x = -HW + 2f; x < HW; x += 4f)
            {
                k.Box(p, "Uplight", V(x, y + 0.04f, HD - 0.25f), V(0.2f, 0.06f, 0.1f), uplight, collider: false);
                k.Box(p, "Uplight", V(x, y + 0.04f, -HD + 0.25f), V(0.2f, 0.06f, 0.1f), uplight, collider: false);
            }

            // The pool: a stone basin with the water up to its lip on the harbour side.
            const float px0 = -9.5f, px1 = 6.5f, pz0 = -11.5f, pz1 = -8f, lip = 0.45f;
            k.Span(p, "Pool coping", V(px0, y, pz1 - 0.3f), V(px1, y + lip, pz1), m.Marble);
            k.Span(p, "Pool coping", V(px0, y, pz0), V(px0 + 0.3f, y + lip, pz1 - 0.3f), m.Marble);
            k.Span(p, "Pool coping", V(px1 - 0.3f, y, pz0), V(px1, y + lip, pz1 - 0.3f), m.Marble);
            k.Span(p, "Pool edge", V(px0 + 0.3f, y, pz0), V(px1 - 0.3f, y + lip - 0.04f, pz0 + 0.12f), m.Marble);
            k.Span(p, "Pool water", V(px0 + 0.3f, y, pz0 + 0.12f), V(px1 - 0.3f, y + lip - 0.05f, pz1 - 0.3f),
                c.P.Lamp(new Color(0.22f, 0.58f, 0.68f), new Color(0.12f, 0.8f, 0.9f), 1.6f), collider: false);
            c.PointLight(p, V(-1.5f, y + 1.2f, -9.8f), 9f, 0.5f, new Color(0.4f, 0.9f, 1f));

            // Loungers facing the water.
            foreach (float x in new[] { -8f, -5.5f, -3f, -0.5f })
            {
                k.Span(p, "Lounger", V(x - 0.35f, y, -7.3f), V(x + 0.35f, y + 0.32f, -5.4f), m.White);
                GameObject back = k.Box(p, "Lounger back", V(x, y + 0.6f, -5.3f), V(0.7f, 0.08f, 0.75f), m.White, collider: false);
                back.transform.localRotation = Quaternion.Euler(-50f, 0f, 0f);
            }

            // The cabana: four posts, a slatted roof, an outdoor sofa round a table.
            const float cx0 = 8.4f, cx1 = 13.5f, cz0 = -11.5f, cz1 = -5.2f, ch = 2.8f;
            foreach (float x in new[] { cx0, cx1 }) foreach (float z in new[] { cz0, cz1 })
                k.Box(p, "Cabana post", V(x, y + ch / 2f, z), V(0.14f, ch, 0.14f), m.Walnut);
            for (float z = cz0; z <= cz1 + 0.01f; z += 0.45f)
                k.Span(p, "Cabana slat", V(cx0 - 0.1f, y + ch, z - 0.06f), V(cx1 + 0.1f, y + ch + 0.12f, z + 0.06f), m.Walnut, collider: false);
            Material cushion = c.P.Lit(new Color(0.9f, 0.88f, 0.84f), 0.05f);
            k.Span(p, "Outdoor sofa", V(cx1 - 1f, y, cz0 + 0.3f), V(cx1 - 0.2f, y + 0.42f, cz1 - 0.3f), cushion);
            k.Span(p, "Outdoor sofa back", V(cx1 - 0.35f, y + 0.42f, cz0 + 0.3f), V(cx1 - 0.2f, y + 0.85f, cz1 - 0.3f), cushion, collider: false);
            k.Span(p, "Outdoor sofa", V(cx0 + 0.4f, y, cz0 + 0.3f), V(cx1 - 1f, y + 0.42f, cz0 + 1.1f), cushion);
            k.Span(p, "Outdoor table", V(10.2f, y, -9.2f), V(11.8f, y + 0.4f, -7.6f), m.Stone);
            c.PointLight(p, V(11f, y + 2.5f, -8.3f), 7f, 0.6f, new Color(1f, 0.86f, 0.66f));
            c.PointLight(p, V(-2f, y + 3.5f, 2f), 14f, 0.5f, new Color(1f, 0.9f, 0.76f));

            foreach (Vector3 at in new[] { V(-12.8f, y, -6.5f), V(-12.8f, y, 10.8f), V(12.8f, y, 10.8f), V(12.8f, y, -3.8f), V(7.2f, y, 10.8f) })
                Plant(c, p, at, 1.8f);
        }

        /// <summary>
        /// The residents' lift: lobby (L) and penthouse (PH), a walnut-and-brass car with a mirror at each stop, doors
        /// south into the lobby and the penthouse foyer.
        /// </summary>
        private static Elevator Lift(CityContext c, Transform dyn, Vector3 at, float rise)
        {
            Kit k = c.Kit;
            Material walnut = c.P.Lit(new Color(0.3f, 0.19f, 0.12f), 0.4f);
            Material brass = c.P.Lit(new Color(0.75f, 0.6f, 0.35f), 0.75f);
            Material steel = c.P.Lit(new Color(0.55f, 0.55f, 0.57f), 0.75f);
            Material mirror = c.P.Lit(new Color(0.9f, 0.92f, 0.94f), 0.95f);
            Material lampOff = c.P.Unlit(new Color(0.25f, 0.25f, 0.24f));
            Material lampOn = c.P.Unlit(new Color(1f, 0.75f, 0.35f));
            Transform shaft = Kit.Group(dyn, "Harborview lift", at);
            var elevator = shaft.gameObject.AddComponent<Elevator>();
            var stops = new Elevator.FloorStop[2];
            string[] labels = { "L", "PH" };
            const float doorZ = -1.2f;
            for (int f = 0; f < 2; f++)
            {
                float y = f == 0 ? 0f : rise;
                k.Span(shaft, "Car floor", V(-1.4f, y - 0.1f, -1.2f), V(1.4f, y, 1.2f), c.P.Lit(new Color(0.2f, 0.19f, 0.18f), 0.5f));
                k.Span(shaft, "Car ceiling", V(-1.4f, y + 2.6f, -1.2f), V(1.4f, y + 2.7f, 1.2f), steel);
                k.Span(shaft, "Car back", V(-1.4f, y, 1.17f), V(1.4f, y + 2.6f, 1.23f), walnut);
                k.Span(shaft, "Car mirror", V(-1f, y + 0.9f, 1.15f), V(1f, y + 2.2f, 1.17f), mirror, collider: false);
                k.Span(shaft, "Car side W", V(-1.45f, y, -1.2f), V(-1.4f, y + 2.6f, 1.2f), walnut);
                k.Span(shaft, "Car side E", V(1.4f, y, -1.2f), V(1.45f, y + 2.6f, 1.2f), walnut);
                k.Span(shaft, "Car rail", V(-1.38f, y + 0.9f, -0.9f), V(-1.33f, y + 0.95f, 0.9f), brass, collider: false);
                k.Span(shaft, "Car front", V(-1.4f, y, doorZ - 0.03f), V(-0.66f, y + 2.6f, doorZ + 0.03f), steel);
                k.Span(shaft, "Car front", V(0.66f, y, doorZ - 0.03f), V(1.4f, y + 2.6f, doorZ + 0.03f), steel);
                k.Span(shaft, "Car front", V(-0.66f, y + 2.3f, doorZ - 0.03f), V(0.66f, y + 2.6f, doorZ + 0.03f), steel);
                c.PointLight(shaft, V(0f, y + 2.4f, 0f), 3.5f, 0.7f, new Color(1f, 0.93f, 0.82f));
                var stop = new Elevator.FloorStop { Label = labels[f], Y = y };
                stop.DoorLeft = k.Box(shaft, "Door L", V(-0.33f, y + 1.15f, doorZ), V(0.66f, 2.3f, 0.05f), steel).transform;
                stop.DoorRight = k.Box(shaft, "Door R", V(0.33f, y + 1.15f, doorZ), V(0.66f, 2.3f, 0.05f), steel).transform;
                stop.HallIndicator = k.Text(shaft, labels[f], V(0f, y + 2.55f, doorZ - 0.13f), 0f, 0.14f, new Color(1f, 0.7f, 0.35f));
                stop.CarIndicator = k.Text(shaft, labels[f], V(0f, y + 2.42f, doorZ + 0.08f), 180f, 0.12f, new Color(1f, 0.7f, 0.35f));
                GameObject call = k.Box(shaft, "Call", V(1.05f, y + 1.1f, doorZ - 0.15f), V(0.14f, 0.2f, 0.04f), brass);
                stop.HallLamp = k.Box(shaft, "Call lamp", V(1.05f, y + 1.1f, doorZ - 0.18f), V(0.06f, 0.06f, 0.02f), lampOff, collider: false).GetComponent<Renderer>();
                call.AddComponent<ElevatorButton>().Configure(elevator, f, inCar: false);
                Transform board = Kit.Group(shaft, "Car panel", V(1.37f, y, 0f), 90f);
                for (int b = 0; b < 2; b++)
                {
                    GameObject button = k.Box(board, "Button " + labels[b], V(0f, 1.05f + b * 0.25f, -0.02f), V(0.12f, 0.12f, 0.03f), brass);
                    button.AddComponent<ElevatorButton>().Configure(elevator, b, inCar: true);
                    k.Text(board, labels[b], V(-0.11f, 1.05f + b * 0.25f, -0.03f), 0f, 0.05f, new Color(0.1f, 0.1f, 0.1f));
                    if (b != f)
                    {
                        stop.CarLamp = k.Box(board, "Lamp", V(0.09f, 1.05f + b * 0.25f, -0.03f), V(0.03f, 0.03f, 0.02f), lampOff, collider: false).GetComponent<Renderer>();
                        stop.CarLampFloor = b;
                    }
                }
                stops[f] = stop;
            }
            elevator.Configure(stops, new Vector2(1.4f, 1.2f), 1.3f, c.Player, lampOn, lampOff);
            // One stop is 88 m: about 12 s at an express lift's pace, not one storey's hop.
            elevator.Logic.TravelPerFloor = 9.5f;
            return elevator;
        }

        private static void SetShadows(this MeshRenderer r, bool on)
        {
            if (r != null) r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>
    /// Tiling surfaces painted at start-up: oak planks, white marble, stone tiles, timber slats. The floors and walls
    /// they go on have UVs in metres (4 m per tile for <see cref="MeshBuilder.Quad"/>; 1 m for slat panels).
    /// </summary>
    public static class Surfaces
    {
        private static Texture2D _oak, _marble, _tiles, _slats;

        private static Texture2D New(string name, int size) =>
            new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };

        private static float Hash(int a, int b)
        {
            uint h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        /// <summary>Light oak planks 0.19 m wide running north–south, in staggered lengths, with grain.</summary>
        public static Texture2D Oak()
        {
            if (_oak != null) return _oak;
            const int size = 1024, plank = 48; // 4 m tile: 3.9 mm a pixel
            var px = new Color32[size * size];
            for (int x = 0; x < size; x++)
            {
                int row = x / plank;
                float offset = Hash(row, 7) * size;
                for (int y = 0; y < size; y++)
                {
                    float along = (y + offset) % size;
                    int board = (int)(along / (size * 0.4f));
                    float tone = 0.93f + Hash(row, board) * 0.1f;
                    float grain = 0.965f + 0.035f * Mathf.Sin((x % plank) * 0.9f + Mathf.PerlinNoise(row * 3.1f, along * 0.02f) * 9f);
                    bool seam = x % plank == 0 || Mathf.Abs(along % (size * 0.4f)) < 1.5f;
                    float k = seam ? 0.7f : tone * grain;
                    px[y * size + x] = new Color(0.86f * k, 0.69f * k, 0.51f * k);
                }
            }
            _oak = New("Oak", size);
            _oak.SetPixels32(px);
            _oak.Apply(true, true);
            return _oak;
        }

        /// <summary>White marble with soft grey veins (turbulent sine veins over Perlin noise).</summary>
        public static Texture2D Marble()
        {
            if (_marble != null) return _marble;
            const int size = 512;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float turb = 0f, amp = 1f, f = 2f;
                    for (int o = 0; o < 5; o++) { turb += amp * (Mathf.PerlinNoise(u * f + 13f, v * f * 0.5f + 7f) - 0.5f); amp *= 0.55f; f *= 2f; }
                    // Veins run diagonally: thin where the warped sine crosses zero, a fainter second set across them.
                    float vein = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((u * 2f + v + turb * 0.9f) * Mathf.PI * 2f)), 28f);
                    float fine = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((u * 5f - v * 3f + turb * 1.6f) * Mathf.PI * 2f)), 40f);
                    float k = 0.96f - vein * 0.3f - fine * 0.12f - Mathf.PerlinNoise(u * 12f, v * 12f) * 0.03f;
                    px[y * size + x] = new Color(k, k * 0.995f, k * 0.985f);
                }
            _marble = New("Marble", size);
            _marble.SetPixels32(px);
            _marble.Apply(true, true);
            return _marble;
        }

        /// <summary>Large stone tiles (0.8 m) with fine joints and a little variation tile to tile.</summary>
        public static Texture2D Tiles()
        {
            if (_tiles != null) return _tiles;
            const int size = 512, tile = 102; // 4 m tile: five tiles across
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool joint = x % tile < 2 || y % tile < 2;
                    float k = joint ? 0.7f : 0.93f + Hash(x / tile, y / tile) * 0.07f + (Mathf.PerlinNoise(x * 0.05f, y * 0.05f) - 0.5f) * 0.05f;
                    px[y * size + x] = new Color(k, k, k);
                }
            _tiles = New("Tiles", size);
            _tiles.SetPixels32(px);
            _tiles.Apply(true, true);
            return _tiles;
        }

        /// <summary>Vertical timber slats, eight to the metre, dark grooves between.</summary>
        public static Texture2D Slats()
        {
            if (_slats != null) return _slats;
            const int size = 256, slat = 32;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = x / slat, s = x % slat;
                    float k = s < 4 ? 0.25f : (0.85f + Hash(i, 3) * 0.15f) * (0.95f + 0.05f * Mathf.Sin(y * 0.15f + i * 2f));
                    px[y * size + x] = new Color(0.55f * k, 0.38f * k, 0.25f * k);
                }
            _slats = New("Slats", size);
            _slats.SetPixels32(px);
            _slats.Apply(true, true);
            return _slats;
        }
    }
}
