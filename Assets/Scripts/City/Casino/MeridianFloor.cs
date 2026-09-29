using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>The Meridian's ground floor fabric: carpet, walls, columns, the lobby, the cage and the restrooms.</summary>
    public static partial class Meridian
    {
        private static void Floor(CityContext c, Transform inner)
        {
            Kit k = c.Kit;
            float hw = HalfWidth - Wall / 2f, z0 = Wall / 2f, z1 = Depth - Wall / 2f;
            Material carpet = c.P.Pattern("Meridian carpet", CasinoProps.Carpet(), 1.8f);
            Material wood = c.P.Lit(new Color(0.24f, 0.12f, 0.07f), 0.45f);
            Material upper = c.P.Surface(Finish.PaintedPlaster, new Color(0.06f, 0.17f, 0.19f), 0.05f);
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            Material warm = c.P.Glow(new Color(1f, 0.78f, 0.42f), 1.5f);

            k.Span(inner, "Carpet", V(-hw, -0.05f, z0), V(hw, 0.02f, z1), carpet).AddComponent<SurfaceTag>().Roughness = 0.9f;
            k.Span(inner, "Ceiling", V(-hw, Ceiling, z0), V(hw, Ceiling + 0.1f, z1), c.P.Lit(new Color(0.07f, 0.06f, 0.07f), 0.2f), collider: false);
            // Coffered ceiling: dark wood beams on a 6 m grid.
            for (float x = -30f; x <= 30f; x += 6f)
                k.Span(inner, "Beam", V(x - 0.15f, Ceiling - 0.3f, z0), V(x + 0.15f, Ceiling, z1), wood, collider: false);
            for (float z = 6f; z < z1; z += 6f)
                k.Span(inner, "Beam", V(-hw, Ceiling - 0.3f, z - 0.15f), V(hw, Ceiling, z + 0.15f), wood, collider: false);

            // Walls: wood wainscot to the rail, deep teal above, a warm cove light under the ceiling.
            Opening door = new Opening(0f, 8f, 0f, 3.8f), hotelDoor = new Opening(HotelDoorX, 2.6f, 0f, 3f);
            k.WallX(inner, "Wainscot", -hw, hw, z0 + 0.02f, 0f, 1.1f, 0.04f, wood, door, hotelDoor);
            k.WallX(inner, "Wall", -hw, hw, z0 + 0.01f, 1.1f, Ceiling, 0.02f, upper, door, hotelDoor);
            k.Span(inner, "Wainscot", V(-hw, 0f, z1 - 0.04f), V(hw, 1.1f, z1), wood, collider: false);
            k.Span(inner, "Wall", V(-hw, 1.1f, z1 - 0.02f), V(hw, Ceiling, z1), upper, collider: false);
            foreach (float x in new[] { -hw, hw })
            {
                float s = Mathf.Sign(x);
                k.Span(inner, "Wainscot", V(Mathf.Min(x, x - s * 0.04f), 0f, z0), V(Mathf.Max(x, x - s * 0.04f), 1.1f, z1), wood, collider: false);
                k.Span(inner, "Wall", V(Mathf.Min(x, x - s * 0.02f), 1.1f, z0), V(Mathf.Max(x, x - s * 0.02f), Ceiling, z1), upper, collider: false);
                k.Span(inner, "Rail", V(Mathf.Min(x, x - s * 0.07f), 1.08f, z0), V(Mathf.Max(x, x - s * 0.07f), 1.14f, z1), bronze, collider: false);
                k.Span(inner, "Cove", V(Mathf.Min(x, x - s * 0.06f), Ceiling - 0.45f, z0), V(Mathf.Max(x, x - s * 0.06f), Ceiling - 0.4f, z1), warm, collider: false);
            }
            k.Span(inner, "Rail", V(-hw, 1.08f, z1 - 0.07f), V(hw, 1.14f, z1), bronze, collider: false);
            k.Span(inner, "Cove", V(-hw, Ceiling - 0.45f, z1 - 0.06f), V(hw, Ceiling - 0.4f, z1), warm, collider: false);

            // Columns: dark marble with bronze capitals and a light band.
            Material column = c.P.Lit(new Color(0.07f, 0.07f, 0.08f), 0.85f);
            foreach (float x in new[] { -24f, -12f, 12f, 24f })
                foreach (float z in new[] { 12f, 24f })
                {
                    if (x == -24f && z == 24f) continue; // inside the poker room: left out so it reads as one space
                    k.Box(inner, "Column", V(x, Ceiling / 2f, z), V(0.9f, Ceiling, 0.9f), column);
                    k.Box(inner, "Capital", V(x, Ceiling - 0.45f, z), V(1.1f, 0.3f, 1.1f), bronze, collider: false);
                    k.Box(inner, "Base", V(x, 0.12f, z), V(1.05f, 0.24f, 1.05f), bronze, collider: false);
                    k.Box(inner, "Band", V(x, 2.6f, z), V(0.94f, 0.06f, 0.94f), warm, collider: false);
                }
        }

        private static void Lobby(CityContext c, Transform inner, Transform dyn)
        {
            Kit k = c.Kit;
            Material marble = c.P.Surface(Finish.Tiles, new Color(0.86f, 0.83f, 0.77f), 0.65f);
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            Material dark = c.P.Lit(new Color(0.07f, 0.07f, 0.08f), 0.85f);
            k.Span(inner, "Lobby marble", V(-9f, 0.02f, Wall / 2f), V(9f, 0.03f, 11.5f), marble, collider: false);
            k.Span(inner, "Lobby border", V(-9.15f, 0.021f, Wall / 2f), V(9.15f, 0.029f, 11.65f), bronze, collider: false);

            // The bronze whale on a dark plinth, a gold ring set in the marble round it.
            var ring = new MeshBuilder();
            for (int i = 0; i < 48; i++)
            {
                float a0 = i * Mathf.PI * 2f / 48f, a1 = (i + 1) * Mathf.PI * 2f / 48f;
                Vector3 P(float a, float r) => V(Mathf.Cos(a) * r, 0.031f, 7f + Mathf.Sin(a) * r);
                ring.Quad(P(a0, 2.3f), P(a1, 2.3f), P(a1, 2.15f), P(a0, 2.15f));
            }
            ring.Build(inner, "Inlay ring", bronze, false);
            k.Cylinder(inner, "Plinth", V(0f, 0.45f, 7f), 2.4f, 0.9f, dark, collider: true);
            k.Cylinder(inner, "Plinth cap", V(0f, 0.92f, 7f), 2.5f, 0.05f, bronze);
            Bounds whale = k.RealBounds("bronze_whale_statue");
            float length = Mathf.Max(whale.size.x, whale.size.z);
            if (length > 0.01f) k.Real(inner, "bronze_whale_statue", V(0f, 0.95f, 7f), 35f, 2.1f / length);
            k.Text(inner, "THE MERIDIAN · EST. 1962", V(0f, 0.55f, 5.77f), 0f, 0.09f, new Color(0.9f, 0.75f, 0.45f));

            // Host stand and plants by the doors; brass vases on tall tables.
            k.Box(inner, "Host stand", V(-5.5f, 0.55f, 3f), V(1.1f, 1.1f, 0.55f), c.P.Lit(new Color(0.24f, 0.12f, 0.07f), 0.45f));
            k.Box(inner, "Host top", V(-5.5f, 1.12f, 3f), V(1.2f, 0.04f, 0.65f), bronze, collider: false);
            k.Text(inner, "WELCOME", V(-5.5f, 0.75f, 2.71f), 0f, 0.12f, new Color(0.95f, 0.8f, 0.5f));
            foreach (float x in new[] { -7.8f, 7.8f })
            {
                k.Real(inner, "potted_plant_04", V(x, 0.03f, 1.1f), 0f, 1.3f);
                if (k.Real(inner, "side_table_tall_01", V(x, 0.03f, 10.3f), 0f) != null)
                    k.Real(inner, "brass_vase_02", V(x, 0.03f + k.RealBounds("side_table_tall_01").size.y, 10.3f), 0f, 1.2f);
            }
            Hang(c, inner, "Chandelier_01", V(0f, 0f, 7f), 1.6f);

            // Security by the door, the host at the stand.
            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            StaffNpc.Create(k, dyn, "Security", 9101, new Color(0.08f, 0.08f, 0.1f), always, new[] { V(6f, 0f, 2.6f), V(6.5f, 0f, 3.5f) }, 250f,
                new[] { NpcPose.Stand, NpcPose.Stand, NpcPose.Phone }, () => "Evening.",
                Cycle("Cashier's on your left. Tables at the back.", "Enjoy your night.", "No photos on the floor, please.", "The hotel's through on the right."),
                c.Game, c.Hud, c.Player, look: "Suit");
            StaffNpc.Create(k, dyn, "Host", 9102, new Color(0.5f, 0.08f, 0.12f), always, new[] { V(-5.5f, 0f, 3.8f), V(-6.5f, 0f, 4.5f) }, 180f,
                new[] { NpcPose.Stand, NpcPose.Typing }, () => "Welcome to The Meridian.",
                Cycle("Chips at the cage, just to your left.", "Blackjack, roulette and baccarat are at the back; the poker room's back left.",
                    "Slots either side of me. Diamond Dusk has the progressive.", "The Tide Bar's open all night, and Sixty-Two upstairs serves dinner from five.",
                    "Gold members have the VIP salon upstairs.", "Rooms at the hotel desk, on the right."), c.Game, c.Hud, c.Player, look: "Suit");
        }

        private static void Cage(CityContext c, Transform inner, Transform dyn)
        {
            // South-west corner: a glass-fronted booth, brass grilles at two windows, the vault wall behind.
            Kit k = c.Kit;
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            Material wood = c.P.Lit(new Color(0.24f, 0.12f, 0.07f), 0.45f);
            Material marble = c.P.Lit(new Color(0.85f, 0.82f, 0.76f), 0.7f);
            float x = -26f, zA = 0.2f, zB = 9f;
            k.WallX(inner, "Cage wall", -HalfWidth, x, zB, 0f, Ceiling, 0.2f, wood);
            k.Span(inner, "Cage counter", V(x - 0.3f, 0f, zA), V(x + 0.3f, 1.05f, zB), wood);
            k.Span(inner, "Counter top", V(x - 0.35f, 1.05f, zA), V(x + 0.4f, 1.1f, zB), marble, collider: false);
            k.Span(inner, "Cage header", V(x - 0.1f, 2.7f, zA), V(x + 0.1f, Ceiling, zB), wood);
            foreach (float wz in new[] { 3.2f, 6.2f })
            {
                // A teller window: an opening in the glass with vertical brass bars.
                for (float bz = wz - 0.6f; bz <= wz + 0.61f; bz += 0.15f)
                    k.Cylinder(inner, "Grille bar", V(x, 1.9f, bz), 0.02f, 1.6f, bronze);
                k.Span(inner, "Grille frame", V(x - 0.05f, 2.66f, wz - 0.65f), V(x + 0.05f, 2.72f, wz + 0.65f), bronze, collider: false);
                k.Span(inner, "Pass-through", V(x - 0.25f, 1.1f, wz - 0.3f), V(x + 0.25f, 1.13f, wz + 0.3f), bronze, collider: false);
            }
            // Glass between and beside the windows (the bars block the openings themselves).
            Color glass = new Color(0.6f, 0.65f, 0.65f, 0.25f);
            k.Pane(inner, "Cage glass", V(x - 0.02f, 1.1f, zA), V(x + 0.02f, 2.7f, 2.6f), glass);
            k.Pane(inner, "Cage glass", V(x - 0.02f, 1.1f, 3.8f), V(x + 0.02f, 2.7f, 5.6f), glass);
            k.Pane(inner, "Cage glass", V(x - 0.02f, 1.1f, 6.8f), V(x + 0.02f, 2.7f, zB), glass);
            var bars = new GameObject("Grille collider");
            bars.transform.SetParent(inner, false);
            var barsBox = bars.AddComponent<BoxCollider>();
            barsBox.center = V(x, 1.9f, 4.7f);
            barsBox.size = V(0.1f, 1.6f, 3.6f);

            TextMesh sign = k.Text(inner, "CASHIER", V(x + 0.12f, 3.4f, 4.7f), -90f, 0.42f, Color.white);
            sign.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(1f, 0.82f, 0.45f));
            k.Text(inner, "CHIPS · CASH OUT · REWARDS · ACCOUNT", V(x + 0.12f, 2.95f, 4.7f), -90f, 0.12f, new Color(0.95f, 0.9f, 0.8f));
            // Behind the glass: a back counter with the register and a wall of safe-deposit doors.
            k.Span(inner, "Back counter", V(-HalfWidth + 0.2f, 0f, 1f), V(-HalfWidth + 1f, 0.95f, 8.5f), wood);
            k.Real(inner, "CashRegister_01", V(-HalfWidth + 0.6f, 0.95f, 4.7f), 90f);
            Material steel = c.P.Metal(new Color(0.55f, 0.56f, 0.58f), 0.6f);
            for (float vz = 1.2f; vz < 8.4f; vz += 0.62f)
                for (float vy = 1.3f; vy < 2.9f; vy += 0.4f)
                    k.Box(inner, "Deposit box", V(-HalfWidth + 0.24f, vy, vz), V(0.05f, 0.34f, 0.56f), steel, collider: false);

            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            StaffNpc cashier = StaffNpc.Create(c.Kit, dyn, "Cashier", 9103, new Color(0.08f, 0.08f, 0.1f), always,
                new[] { V(x - 1.1f, 0f, 3.2f), V(-33f, 0f, 3.2f) }, 90f, new[] { NpcPose.Stand, NpcPose.Typing }, () => "Next at the window.",
                Cycle("Chips at this window, cash-outs too.", "Your players' card keeps your record. The account tab has the lot.",
                    "You can set yourself a limit for the night here, if you like."), c.Game, c.Hud, c.Player, look: "Suit");
            var window = new GameObject("Cashier window");
            window.transform.SetParent(dyn, false);
            window.transform.localPosition = V(x + 0.45f, 1.35f, 3.2f);
            var box = window.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = V(0.5f, 0.8f, 1.3f);
            window.AddComponent<CasinoCage>().Configure(c, cashier);
            c.Anchor("casino_cage", window.transform.TransformPoint(V(1.2f, -1.35f, 0f)));
        }

        private static void Restrooms(CityContext c, Transform inner)
        {
            // Beside the cage (§106): tiled, three stalls, a row of basins under a mirror.
            Kit k = c.Kit;
            Material tile = c.P.Surface(Finish.Tiles, new Color(0.78f, 0.8f, 0.8f), 0.5f);
            Material wall = c.P.Lit(new Color(0.5f, 0.45f, 0.4f), 0.3f);
            Material stall = c.P.Lit(new Color(0.22f, 0.12f, 0.08f), 0.5f);
            const float x0 = -HalfWidth + Wall / 2f, x1 = -29f, z0 = 9.1f, z1 = 13.9f;
            k.Span(inner, "Restroom floor", V(x0, 0.02f, z0), V(x1, 0.03f, z1), tile, collider: false);
            k.WallZ(inner, "Restroom wall", z0, z1, x1, 0f, Ceiling, 0.2f, wall, Opening.Door(11.5f, 1f, 0f, 2.2f));
            c.SwingDoor(inner, "restrooms", V(x1, 0f, 12f), 1f, 2.2f, stall, 90f);
            for (int i = 0; i < 3; i++)
            {
                float sz = z0 + 0.1f + i * 1.3f;
                k.Box(inner, "Stall wall", V(x0 + 1.2f, 1f, sz + 1.25f), V(2.3f, 1.9f, 0.05f), stall);
                k.Fit(inner, "toilet", V(x0 + 0.4f, 0.03f, sz + 0.6f), V(0f, 0.8f, 0f), 90f);
            }
            k.Box(inner, "Stall fronts", V(x0 + 2.35f, 1f, 11f), V(0.05f, 1.9f, 3.8f), stall);
            k.Span(inner, "Basin counter", V(x1 - 2.8f, 0f, z1 - 0.6f), V(x1 - 0.3f, 0.85f, z1), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.8f));
            for (float bx = x1 - 2.4f; bx < x1 - 0.5f; bx += 0.9f)
                k.Fit(inner, "bathroomSink", V(bx, 0.85f, z1 - 0.3f), V(0.5f, 0f, 0f), 180f);
            k.Span(inner, "Mirror", V(x1 - 2.8f, 1.1f, z1 - 0.03f), V(x1 - 0.3f, 2.2f, z1 - 0.01f), c.P.Metal(new Color(0.85f, 0.87f, 0.9f), 0.95f), collider: false);
            c.PointLight(inner, V(-33f, 2.8f, 11.5f), 6f, 0.8f, new Color(1f, 0.97f, 0.9f));
        }
    }
}
