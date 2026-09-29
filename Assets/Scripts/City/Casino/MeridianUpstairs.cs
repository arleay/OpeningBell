using OpeningBell.Casino;
using OpeningBell.Casino.Poker;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The Meridian's second floor (CASINO_SPEC §43–44, §47, §105, §107, §109), inside the casino's shell and reached by
    /// the hotel elevators: a landing, the Sixty-Two steakhouse along the street windows (dinner 5–11 PM), and the VIP
    /// salon behind a door the host keeps for Gold members, big bankrolls and VIP Night. The salon has high-limit
    /// blackjack, roulette and a $5/$10 poker game, a private bar and quiet. Kitchen and surveillance doors are staff only.
    /// </summary>
    public static class MeridianUpstairs
    {
        /// <summary>Second-floor floor level and ceiling (local y).</summary>
        public const float Floor = 6.4f, CeilingY = 10.6f;
        private const float X0 = 2f, Xl = 28f, Xe = 34f, Xr = Meridian.HalfWidth - Meridian.Wall / 2f;
        private const float Z0 = Meridian.Wall / 2f, Z1 = Meridian.Depth - Meridian.Wall / 2f, Split = 14f, LandingEnd = 28f;
        /// <summary>The elevator shaft passes through here (x 34–36.8, z 19.4–22.6).</summary>
        public const float ShaftZ0 = 19.4f, ShaftZ1 = 22.6f;

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        public static void Build(CityContext c, Transform m, Transform dyn, CasinoLife life, CasinoAudio audio)
        {
            Transform up = Kit.Group(m, "Interior");
            up.name = "Interior";
            Structure(c, up, dyn);
            Restaurant(c, up, dyn, life);
            Vip(c, up, dyn, life);
            Landing(c, up, dyn);
            CityLayers.Set(up, CityLayers.Interior);
            audio.Bed("Restaurant music", m.TransformPoint(V(14f, Floor + 2f, 7f)), CasinoArt.Music(true), 0.25f, 14f);
            audio.Bed("VIP music", m.TransformPoint(V(14f, Floor + 2f, 23f)), CasinoArt.Music(true), 0.18f, 14f);
        }

        private static void Structure(CityContext c, Transform up, Transform dyn)
        {
            Kit k = c.Kit;
            Material slab = c.P.Lit(new Color(0.12f, 0.11f, 0.11f));
            Material wall = c.P.Surface(Finish.PaintedPlaster, new Color(0.18f, 0.12f, 0.1f), 0.05f);
            // The floor, with a hole where the elevator runs through.
            k.Span(up, "Floor slab", V(X0, Floor - 0.2f, Z0), V(Xe, Floor, Z1), slab);
            k.Span(up, "Floor slab", V(Xe, Floor - 0.2f, Z0), V(Xr, Floor, ShaftZ0), slab);
            k.Span(up, "Floor slab", V(Xe, Floor - 0.2f, ShaftZ1), V(Xr, Floor, Z1), slab);
            k.Span(up, "Upstairs ceiling", V(X0, CeilingY - 0.1f, Z0), V(Xr, CeilingY, Z1), c.P.Lit(new Color(0.1f, 0.09f, 0.09f), 0.2f), collider: false);

            // Walls: the dead space west of x = 2, the restaurant/VIP split, the landing, the shaft side.
            float h0 = Floor, h1 = CeilingY - 0.1f;
            k.WallZ(up, "Kitchen wall", Z0, Z1, X0, h0, h1, 0.2f, wall, Opening.Door(6f, 1f, h0, h0 + 2.2f));
            k.WallX(up, "Restaurant wall", X0, Xl, Split, h0, h1, 0.2f, wall);
            k.WallZ(up, "Landing wall", Z0, Z1, Xl, h0, h1, 0.2f, wall, new Opening(9f, 2.4f, h0, h0 + 3f), Opening.Door(22f, 1.6f, h0, h0 + 2.6f));
            k.WallZ(up, "Shaft wall", Z0, Z1, Xe, h0, h1, 0.2f, wall, new Opening(21f, 2.8f, h0, h0 + 2.6f));
            k.WallX(up, "Landing end", Xl, Xe, LandingEnd, h0, h1, 0.2f, wall, Opening.Door(31f, 1f, h0, h0 + 2.2f));
            // The street wall's inner face, with the window openings, and the back wall's.
            var windows = new System.Collections.Generic.List<Opening>();
            foreach (float x in Meridian.UpstairsWindows) windows.Add(new Opening(x, 3f, 7.2f, 9.8f));
            k.WallX(up, "Street wall", X0, Xe, Z0 + 0.01f, h0, h1, 0.02f, wall, windows.ToArray());
            k.Span(up, "Back wall", V(X0, h0, Z1 - 0.02f), V(Xl, h1, Z1), wall, collider: false);

            // Staff-only doors: the kitchen and surveillance (§105, §107). They stay shut.
            Material staffDoor = c.P.Lit(new Color(0.2f, 0.18f, 0.17f), 0.4f);
            Door kitchen = c.SwingDoor(up, "kitchen", V(X0, h0, 6.5f), 1f, 2.2f, staffDoor, 90f);
            kitchen.LockReason = () => "staff only (kitchen)";
            Door cameras = c.SwingDoor(up, "surveillance", V(30.5f, h0, LandingEnd), 1f, 2.2f, staffDoor);
            cameras.LockReason = () => "staff only (surveillance)";
            k.Text(up, "STAFF ONLY", V(X0 + 0.12f, h0 + 2.4f, 6f), -90f, 0.08f, new Color(0.9f, 0.9f, 0.9f));
            k.Text(up, "SURVEILLANCE · STAFF ONLY", V(31f, h0 + 2.4f, LandingEnd - 0.12f), 0f, 0.07f, new Color(0.9f, 0.9f, 0.9f));
        }

        private static void Restaurant(CityContext c, Transform up, Transform dyn, CasinoLife life)
        {
            // Sixty-Two: a steakhouse along the windows (§47). Tables for two, a wine wall, pendant lamps.
            Kit k = c.Kit;
            k.Span(up, "Restaurant floor", V(X0 + 0.1f, Floor, Z0), V(Xl - 0.1f, Floor + 0.02f, Split - 0.1f),
                c.P.Surface(Finish.WoodFloor, new Color(0.35f, 0.2f, 0.12f), 0.4f), collider: false);
            var dinner = new WorkSchedule { Shift = Hours.Of(17, 23) };
            StaffNpc host = StaffNpc.Create(k, dyn, "Maître d'", 9200, new Color(0.08f, 0.08f, 0.1f), dinner,
                new[] { V(26.4f, Floor, 10.5f), V(26.4f, Floor, 12.5f) }, 270f, new[] { NpcPose.Stand, NpcPose.Typing },
                () => "Good evening. Table for one?",
                Meridian.Cycle("Sit anywhere by the windows.", "The ribeye's dry-aged twenty-eight days.", "We seat until half past ten."),
                c.Game, c.Hud, c.Player, look: "Suit");
            var venue = new MenuVenue { Name = "Sixty-Two", Where = "Order dinner", Items = CasinoMenus.Steakhouse, Staff = host, Open = Hours.Of(17, 23) };
            k.Box(up, "Host stand", V(26.4f, Floor + 0.55f, 9.6f), V(0.55f, 1.1f, 0.9f), c.P.Lit(new Color(0.2f, 0.1f, 0.06f), 0.45f));
            Material cloth = c.P.Lit(new Color(0.95f, 0.94f, 0.9f), 0.1f);
            int seat = 9210;
            foreach (float z in new[] { 4.2f, 10f })
                foreach (float x in new[] { 6f, 12f, 18f, 23.5f })
                {
                    // A table for two, set with white linen, one chair each side.
                    k.Cylinder(up, "Table", V(x, Floor + 0.74f, z), 1f, 0.04f, cloth, collider: false);
                    k.Box(up, "Table base", V(x, Floor + 0.37f, z), V(0.12f, 0.74f, 0.12f), c.P.Lit(new Color(0.1f, 0.1f, 0.1f), 0.6f));
                    k.Real(up, "dining_chair_02", V(x - 0.7f, Floor, z), 90f);
                    k.Real(up, "dining_chair_02", V(x + 0.7f, Floor, z), -90f);
                    k.Prop(up, "food_sm_wine_bottle", V(x + 0.15f, Floor + 0.76f, z + 0.2f), 0.28f);
                    Meridian.Chair(c, dyn, V(x - 0.7f, Floor, z), 90f, "Sit down for dinner", 1.15f, venue);
                    life.AddSpot(k, dyn, V(x + 0.7f, Floor, z), -90f, NpcPose.Sit, seat++, 0.2f + 0.1f * (seat % 7));
                    Meridian.Hang(c, up, "modern_ceiling_lamp_01", V(x, 0f, z), 0.8f, CeilingY - 0.1f);
                }
            // Wine wall along the kitchen side.
            for (float z = 1f; z < 13f; z += 0.5f)
                for (float y = Floor + 0.4f; y < Floor + 2.4f; y += 0.4f)
                    k.Prop(up, "food_sm_wine_bottle", V(X0 + 0.25f, y, z), 0.3f);
            TextMesh name = k.Text(up, "SIXTY-TWO", V(Xl + 0.12f, Floor + 3.2f, 9f), -90f, 0.3f, Color.white);
            name.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(1f, 0.82f, 0.45f));
            k.Text(up, "STEAKHOUSE · 5 PM – 11 PM", V(Xl + 0.12f, Floor + 2.85f, 9f), -90f, 0.09f, new Color(0.95f, 0.9f, 0.8f));
            var serverRoute = new System.Collections.Generic.List<Vector3> { V(25f, Floor, 7f), V(9f, Floor, 7f), V(9f, Floor, 12.5f), V(21f, Floor, 12.5f) };
            life.AddWalker(Wanderer.Create(k, dyn, "Server", 9220, new Color(0.9f, 0.9f, 0.9f), "Casual", serverRoute, 5f, 1f, c.Player,
                Meridian.Cycle("Can I get you anything else?", "The crème brûlée is worth it.", "Enjoy your evening."), c.Game, c.Hud));
            c.PointLight(up, V(9f, Floor + 3.2f, 7f), 10f, 1.2f, new Color(1f, 0.85f, 0.65f));
            c.PointLight(up, V(20f, Floor + 3.2f, 7f), 10f, 1.2f, new Color(1f, 0.85f, 0.65f));
            c.Anchor("casino_restaurant", up.TransformPoint(V(29.5f, Floor, 9f)));
        }

        private static void Vip(CityContext c, Transform up, Transform dyn, CasinoLife life)
        {
            // The VIP salon (§43): darker, quieter, high limits; its own bar and host.
            Kit k = c.Kit;
            k.Span(up, "VIP carpet", V(X0 + 0.1f, Floor, Split + 0.1f), V(Xl - 0.1f, Floor + 0.02f, Z1),
                c.P.Pattern("VIP carpet", VipCarpet(), 1.4f), collider: false);
            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            Meridian.BuildBlackjack(c, up, dyn, "meridian-bj-vip", "High Limit", V(9f, Floor, 21.5f),
                new BlackjackRules { MinBet = 100m, MaxBet = 10_000m }, new Color(0.05f, 0.1f, 0.3f), 9230,
                new[] { "Hundred minimum, ten thousand max.", "Same rules as downstairs. Just bigger chips.", "Take your time." });
            Meridian.BuildRoulette(c, up, dyn, "High Limit Roulette", V(19f, Floor, 27.4f),
                new RouletteRules { TableMin = 25m, InsideMax = 5_000m, OutsideMax = 25_000m }, 9231, new[] { 25m, 100m, 500m, 1_000m, 5_000m });
            Meridian.BuildPoker(c, up, dyn, "VIP Poker", V(20f, Floor, 19.3f), PokerStakes.FiveTen, always, 9232);

            // The private bar on the west wall.
            Material wood = c.P.Lit(new Color(0.2f, 0.1f, 0.06f), 0.5f);
            k.Span(up, "VIP bar", V(3.6f, Floor, 16f), V(4.4f, Floor + 1.08f, 24f), wood);
            k.Span(up, "VIP bar top", V(3.5f, Floor + 1.08f, 15.9f), V(4.5f, Floor + 1.14f, 24.1f), c.P.Lit(new Color(0.85f, 0.82f, 0.76f), 0.8f), collider: false);
            for (float z = 16.5f; z < 24f; z += 0.45f)
                for (float y = Floor + 1.3f; y < Floor + 2.6f; y += 0.5f)
                    k.Prop(up, "food_sm_wine_bottle", V(X0 + 0.25f, y, z), 0.3f);
            StaffNpc bartender = StaffNpc.Create(k, dyn, "Bartender", 9233, new Color(0.08f, 0.08f, 0.1f), always,
                new[] { V(2.9f, Floor, 20f), V(2.9f, Floor, 24.5f) }, 90f, new[] { NpcPose.Stand, NpcPose.Drink },
                () => "Good evening. The usual?", Meridian.Cycle("Platinum members drink on the house up here.", "Eighteen-year single malt tonight.", "Quiet in here, the way people like it."),
                c.Game, c.Hud, c.Player, look: "Suit");
            var bar = new MenuVenue { Name = "The Salon Bar", Where = "Order", Items = CasinoMenus.VipBar, Staff = bartender, VipBar = true };
            Meridian.Counter(dyn, V(4f, Floor + 1.25f, 20f), V(0.7f, 0.3f, 6f), bar);
            for (float z = 17f; z < 24f; z += 1.4f)
                k.Real(up, "bar_chair_round_01", V(4.95f, Floor, z), 90f);
            Meridian.Chair(c, dyn, V(4.95f, Floor, 19.8f), -90f, "Sit at the salon bar", 1.25f, bar);

            // Lounge corner: sofas, low light.
            Meridian.Solid(k, k.Real(up, "sofa_03", V(8f, Floor, 30.6f), 180f));
            Meridian.Solid(k, k.Real(up, "mid_century_lounge_chair", V(11.5f, Floor, 29.2f), -120f));
            k.Real(up, "coffee_table_round_01", V(8f, Floor, 29.2f), 0f);
            k.Real(up, "potted_plant_04", V(26.8f, Floor, 30.8f), 0f, 1.2f);
            Meridian.Hang(c, up, "Chandelier_02", V(14f, 0f, 23f), 1.2f, CeilingY - 0.1f);
            TextMesh sign = k.Text(up, "THE SALON", V(Xl + 0.12f, Floor + 3.2f, 22f), -90f, 0.3f, Color.white);
            sign.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(0.75f, 0.85f, 1f));
            k.Text(up, "VIP · MEMBERS GOLD AND ABOVE", V(Xl + 0.12f, Floor + 2.85f, 22f), -90f, 0.08f, new Color(0.95f, 0.9f, 0.8f));

            // The door and the host who keeps it (§44).
            Door door = c.SwingDoor(up, "VIP salon", V(Xl, Floor, 22.8f), 1.6f, 2.6f, c.P.Lit(new Color(0.12f, 0.06f, 0.05f), 0.6f), 90f);
            door.LockReason = () => c.Game.Casino.VipAllowed(c.Game.Clock.Now) ? null : "VIP salon: Gold members (or $25,000 in chips)";
            StaffNpc.Create(k, dyn, "VIP host", 9234, new Color(0.3f, 0.05f, 0.08f), always, new[] { V(29.2f, Floor, 23.6f), V(29.8f, Floor, 25f) }, 270f,
                new[] { NpcPose.Stand, NpcPose.Phone }, () => VipGreeting(c),
                () => VipTalk(c), c.Game, c.Hud, c.Player, look: "Suit");

            c.PointLight(up, V(9f, Floor + 2.9f, 21.5f), 7f, 1.1f, new Color(1f, 0.9f, 0.75f));
            c.PointLight(up, V(19f, Floor + 2.9f, 27.4f), 7f, 1.1f, new Color(1f, 0.9f, 0.75f));
            c.PointLight(up, V(20f, Floor + 2.9f, 19.3f), 7f, 1.1f, new Color(1f, 0.9f, 0.75f));
            c.PointLight(up, V(5f, Floor + 3f, 20f), 8f, 0.9f, new Color(1f, 0.75f, 0.5f));
            life.AddSpot(k, dyn, V(4.95f, Floor, 17f), -90f, NpcPose.Drink, 9235, 0.5f);
            NpcBody mara = life.AddRegular(k, dyn, V(10.5f, Floor, 27.8f), 200f, NpcPose.Drink, 9236, "Mara Quint", "Suit");
            mara.GetComponent<PatronTalk>().Configure(c.Game, c.Hud, 9236, "Mara Quint", Meridian.TraderLines(c.Game, 9236,
                "I build companies. The tables are where I go to stop thinking about them.",
                "Anyone who says they've beaten this place is selling something.",
                "Met my last investor at that bar. Didn't win a hand all night, closed the round anyway."));
            c.Anchor("casino_vip_door", up.TransformPoint(V(29.6f, Floor, 22f)));
            c.Anchor("casino_vip", up.TransformPoint(V(24f, Floor, 22f)));
        }

        private static string VipGreeting(CityContext c)
        {
            CasinoFloor f = c.Game.Casino;
            if (f.VipAllowed(c.Game.Clock.Now)) return f.Rewards.Tier >= RewardTier.Gold ? $"Welcome back. {f.Rewards.Tier} members are always welcome in the salon." : "Welcome. The salon's yours tonight.";
            return $"The salon's for Gold members. You're {f.Rewards.ToNextTier:N0} tier credits from the next tier.";
        }

        private static string VipTalk(CityContext c)
        {
            CasinoFloor f = c.Game.Casino;
            string[] lines =
            {
                "Gold gets the salon and soft drinks on us; Platinum, the salon bar; Diamond, a quarter off any room.",
                "If you're staying the night, ask the front desk: members get a rate. The penthouse is something else.",
                "Friday is VIP Night: Silver members are welcome up here after six.",
                "I can't tell you which table's lucky. None of them are. They're all fair, and the house still keeps its share.",
            };
            return lines[(int)(c.Game.Clock.Now.Ticks / System.TimeSpan.TicksPerMinute) % lines.Length] + (f.Rewards.Points > 0 ? $" You have {f.Rewards.Points:N0} points." : "");
        }

        private static void Landing(CityContext c, Transform up, Transform dyn)
        {
            // Where the elevators open: marble, a bench, the way to the restaurant and the salon.
            Kit k = c.Kit;
            k.Span(up, "Landing marble", V(Xl + 0.1f, Floor, Z0), V(Xe - 0.1f, Floor + 0.02f, LandingEnd - 0.1f),
                c.P.Surface(Finish.Tiles, new Color(0.86f, 0.83f, 0.77f), 0.65f), collider: false);
            k.Real(up, "Ottoman_01", V(31f, Floor, 14f), 0f);
            k.Real(up, "potted_plant_04", V(33.3f, Floor, 1f), 0f, 1.2f);
            k.Real(up, "calathea_orbifolia_01", V(33.3f, Floor, 27f), 0f, 1.2f);
            Meridian.HangingSign(c, up, "< SIXTY-TWO", V(31f, Floor + 2.9f, 10.5f), new Color(1f, 0.85f, 0.5f), 2.4f, CeilingY - 0.1f);
            Meridian.HangingSign(c, up, "THE SALON >", V(31f, Floor + 2.9f, 18f), new Color(0.75f, 0.85f, 1f), 2.4f, CeilingY - 0.1f);
            c.PointLight(up, V(31f, Floor + 3f, 8f), 10f, 1.2f, new Color(1f, 0.93f, 0.82f));
            c.PointLight(up, V(31f, Floor + 3f, 21f), 10f, 1.2f, new Color(1f, 0.93f, 0.82f));
            c.Anchor("casino_upstairs", up.TransformPoint(V(32.5f, Floor, 21f)));
        }

        /// <summary>The salon's carpet: deep navy with a fine gold grid and small medallions.</summary>
        private static Texture2D VipCarpet()
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "VIP carpet", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color[n * n];
            Color navy = new Color(0.05f, 0.07f, 0.16f), gold = new Color(0.7f, 0.55f, 0.25f), blue = new Color(0.12f, 0.2f, 0.4f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color col = navy;
                    if (x % 48 == 0 || y % 48 == 0) col = gold;
                    float dx = (x % 48) - 24f, dy = (y % 48) - 24f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r < 9f) col = blue;
                    if (Mathf.Abs(r - 9f) < 1f || r < 2.5f) col = gold;
                    px[y * n + x] = col;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
