using System.Collections.Generic;
using OpeningBell.Casino;
using OpeningBell.Casino.Poker;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>The Meridian's games: slot banks, the pit (blackjack, roulette, baccarat) and the poker room.</summary>
    public static partial class Meridian
    {
        /// <summary>The blackjack tables: id, name, where (local), rules, felt colour. The VIP one is upstairs.</summary>
        public static readonly (string Id, string Name, Vector3 At, BlackjackRules Rules, Color Felt)[] Tables =
        {
            ("meridian-bj-1", "Table 1", new Vector3(-6f, 0f, 20.5f), new BlackjackRules { MinBet = 5m, MaxBet = 500m }, new Color(0.06f, 0.33f, 0.2f)),
            ("meridian-bj-2", "Table 2", new Vector3(6f, 0f, 20.5f), new BlackjackRules { MinBet = 25m, MaxBet = 2_500m }, new Color(0.4f, 0.05f, 0.09f)),
        };

        /// <summary>Slot banks: centre (local x, z), the machine, its theme colour.</summary>
        public static readonly (float X, float Z, SlotDefinition Game, Color Theme)[] Banks =
        {
            (17.5f, 6.5f, SlotMachines.Meridian7s, new Color(0.9f, 0.15f, 0.2f)),
            (24f, 6.5f, SlotMachines.TripleKell, new Color(0.6f, 0.25f, 0.9f)),
            (17.5f, 11.8f, SlotMachines.GoldTide, new Color(0.95f, 0.72f, 0.15f)),
            (-17.5f, 6.5f, SlotMachines.LuckyHarbor, new Color(0.15f, 0.55f, 0.95f)),
            (-17.5f, 11.8f, SlotMachines.DiamondDusk, new Color(0.3f, 0.85f, 1f)),
        };

        private static void Slots(CityContext c, Transform inner, Transform dyn, CasinoLife life)
        {
            // Banks of six back to back, one game each; a lit sign over every bank naming it.
            var rng = new System.Random(62);
            int seed = 9600;
            foreach ((float bx, float bz, SlotDefinition game, Color theme) in Banks)
            {
                for (int i = 0; i < 6; i++)
                {
                    float x = bx + (i - 2.5f) * 0.72f;
                    foreach ((float z, float yaw) in new[] { (bz - 0.31f, 0f), (bz + 0.31f, 180f) })
                    {
                        int[] idle = { rng.Next(game.Strip.Length), rng.Next(game.Strip.Length), rng.Next(game.Strip.Length) };
                        CasinoProps.SlotCabinet(c.Kit, inner, V(x, 0.02f, z), yaw, game, theme, idle);
                        SlotMachineView.Build(c, dyn, V(x, 0.02f, z), yaw, game, theme, idle);
                        // Some machines have someone at them, more as the night gets busier.
                        if ((i + (yaw > 0f ? 1 : 0)) % 3 == 0)
                        {
                            Vector3 stool = Quaternion.Euler(0f, yaw, 0f) * V(0f, 0f, -0.95f);
                            life.AddSpot(c.Kit, dyn, V(x, 0.02f, z) + stool, yaw, NpcPose.Typing, seed++, (float)rng.NextDouble());
                        }
                    }
                }
                HangingSign(c, inner, game.Name.ToUpperInvariant(), V(bx, 3.5f, bz), theme, 2.6f);
                if (game.Progressive)
                {
                    // The progressive meter over its bank, updated live.
                    TextMesh meter = c.Kit.Text(dyn, "", V(bx, 4.15f, bz - 0.05f), 0f, 0.2f, Color.white);
                    meter.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(0.5f, 0.95f, 1f));
                    TextMesh back = c.Kit.Text(dyn, "", V(bx, 4.15f, bz + 0.05f), 180f, 0.2f, Color.white);
                    back.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(0.5f, 0.95f, 1f));
                    c.Kit.Box(inner, "Meter board", V(bx, 4.15f, bz), V(2.6f, 0.45f, 0.06f), c.P.Lit(new Color(0.03f, 0.03f, 0.05f), 0.6f), collider: false);
                    meter.gameObject.AddComponent<JackpotMeter>().Configure(c.Game, meter, back);
                }
            }
            c.Anchor("casino_slots", inner.TransformPoint(V(17.5f, 0f, 4.5f)));
        }

        private static void Pit(CityContext c, Transform inner, Transform dyn, CasinoLife life)
        {
            Kit k = c.Kit;
            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            string[][] chatter =
            {
                new[] { "Dealer stands on seventeen here.", "Five to five hundred at this table.", "Blackjack pays three to two." },
                new[] { "Twenty-five minimum, twenty-five hundred max.", "Split up to four hands. Double after splits.", "Good luck." },
            };
            for (int i = 0; i < Tables.Length; i++)
            {
                var (id, tableName, at, rules, felt) = Tables[i];
                BuildBlackjack(c, inner, dyn, id, tableName, at, rules, felt, 9110 + i, chatter[i]);
                c.Anchor("casino_table_" + (i + 1), inner.TransformPoint(at + CasinoProps.StoolSpot(2) + V(0f, 0f, -0.6f)));
                life.AddSpot(k, dyn, at + V(1.9f, 0f, -1.2f), -60f, NpcPose.Stand, 9700 + i, 0.4f + i * 0.2f);
            }

            // Roulette: single zero on the left, double zero on the right; mini-baccarat between them.
            decimal[] chips = { 1m, 5m, 25m, 100m, 500m };
            BuildRoulette(c, inner, dyn, "Roulette 1", V(-8.5f, 0f, 27.3f), new RouletteRules { TableMin = 5m, InsideMax = 500m, OutsideMax = 5_000m }, 9120, chips);
            BuildRoulette(c, inner, dyn, "Roulette 2", V(8.5f, 0f, 27.3f), new RouletteRules { DoubleZero = true, TableMin = 5m, InsideMax = 500m, OutsideMax = 5_000m }, 9121, chips);
            c.Anchor("casino_roulette", inner.TransformPoint(V(-8.9f, 0f, 26f)));
            life.AddSpot(k, dyn, V(-7.2f, 0f, 25.7f), 10f, NpcPose.Stand, 9710, 0.25f);
            life.AddSpot(k, dyn, V(9.8f, 0f, 25.7f), -10f, NpcPose.Stand, 9711, 0.55f);

            Transform bac = Kit.Group(inner, "Baccarat", V(0f, 0f, 27.3f));
            var rulesB = new BaccaratRules { MinBet = 25m, MaxBet = 5_000m };
            CasinoProps.BaccaratTable(k, bac, rulesB, new Color(0.35f, 0.08f, 0.1f), "Mini-baccarat");
            Stools(c, bac);
            Transform liveB = Kit.Group(dyn, "Baccarat (play)", V(0f, 0f, 27.3f));
            StaffNpc dealerB = StaffNpc.Create(k, dyn, "Dealer", 9122, new Color(0.08f, 0.08f, 0.1f), always,
                new[] { V(0f, 0f, 27.3f + CasinoProps.DealerEdge + 0.5f), V(0f, 0f, 27.3f + CasinoProps.DealerEdge + 1.5f) }, 180f,
                new[] { NpcPose.Stand }, () => "Player, banker or tie?",
                Cycle("Banker pays ninety-five cents on the dollar.", "Tie pays eight to one.", "The cards do the work here."),
                c.Game, c.Hud, c.Player, look: "Suit");
            BaccaratTableView.Build(c, liveB, "Mini-baccarat", rulesB, dealerB);
            c.Anchor("casino_baccarat", inner.TransformPoint(V(0f, 0f, 27.3f) + CasinoProps.StoolSpot(2) + V(0f, 0f, -0.6f)));
            Hang(c, inner, "Chandelier_02", V(0f, 0f, 16f), 1.5f);

            // The pit boss walks the tables (§104).
            var bossRoute = new List<Vector3> { V(0f, 0f, 24f), V(-11f, 0f, 24f), V(-11f, 0f, 18f), V(0f, 0f, 17.5f), V(11f, 0f, 18f), V(11f, 0f, 24f) };
            life.AddWalker(Wanderer.Create(k, dyn, "Pit boss", 9130, new Color(0.1f, 0.1f, 0.12f), "Suit", bossRoute, 6f, 1.1f, c.Player,
                Cycle("Everything all right at the tables?", "If you want a higher limit, the VIP salon's upstairs.",
                    "Table two's hot tonight. Or so they tell me. Cards don't remember.", "Need a marker? Kidding. Chips at the cage."), c.Game, c.Hud));
        }

        public static void BuildBlackjack(CityContext c, Transform inner, Transform dyn, string id, string tableName, Vector3 at, BlackjackRules rules, Color felt, int seed, string[] chatter)
        {
            Kit k = c.Kit;
            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            Transform table = Kit.Group(inner, tableName, at);
            CasinoProps.BlackjackTable(k, table, rules, felt, tableName);
            Stools(c, table);
            LowLamp(c, table, at.y);
            Transform live = Kit.Group(dyn, tableName + " (play)", at);
            StaffNpc dealer = StaffNpc.Create(k, dyn, "Dealer", seed, new Color(0.08f, 0.08f, 0.1f), always,
                new[] { at + V(0f, 0f, CasinoProps.DealerEdge + 0.5f), at + V(0f, 0f, CasinoProps.DealerEdge + 1.5f) }, 180f,
                new[] { NpcPose.Stand }, () => "Place your bets.", Cycle(chatter), c.Game, c.Hud, c.Player, look: "Suit");
            BlackjackTable.Build(c, table, live, id, tableName, rules, dealer);
        }

        public static void BuildRoulette(CityContext c, Transform inner, Transform dyn, string tableName, Vector3 at, RouletteRules rules, int seed, decimal[] chips)
        {
            Kit k = c.Kit;
            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            Transform t = Kit.Group(inner, tableName, at);
            CasinoProps.RouletteTable(k, t, rules, tableName);
            for (int s = 0; s < 5; s++)
                if (k.Real(t, "metal_stool_02", V(-1.05f + s * 0.4f, 0f, -0.95f), 0f) == null)
                    k.Cylinder(t, "Stool", V(-1.05f + s * 0.4f, 0.34f, -0.95f), 0.36f, 0.68f, c.P.Lit(new Color(0.12f, 0.07f, 0.05f)));
            LowLamp(c, t, at.y);
            Transform live = Kit.Group(dyn, tableName + " (play)", at);
            StaffNpc croupier = StaffNpc.Create(k, dyn, "Croupier", seed, new Color(0.08f, 0.08f, 0.1f), always,
                new[] { at + V(0.6f, 0f, 1.05f), at + V(0.6f, 0f, 2f) }, 180f, new[] { NpcPose.Stand }, () => "Place your bets.",
                Cycle("Red or black, it's all the same wheel.", rules.DoubleZero ? "Zero and double zero on this one." : "Single zero here: better odds than double.",
                    "Last call for bets."), c.Game, c.Hud, c.Player, look: "Suit");
            RouletteTableView.Build(c, live, tableName, rules, croupier, chips);
        }

        private static void Stools(CityContext c, Transform table)
        {
            for (int s = 0; s < CasinoProps.Seats; s++)
                if (c.Kit.Real(table, "metal_stool_02", CasinoProps.StoolSpot(s), CasinoProps.SeatYaw(s)) == null)
                    c.Kit.Cylinder(table, "Stool", CasinoProps.StoolSpot(s) + Vector3.up * 0.34f, 0.36f, 0.68f, c.P.Lit(new Color(0.12f, 0.07f, 0.05f)));
        }

        /// <summary>A lamp hanging low over a table from the ceiling above it (upstairs rooms are lower).</summary>
        private static void LowLamp(CityContext c, Transform table, float floorY)
        {
            Kit k = c.Kit;
            float ceiling = floorY > 3f ? 4.2f : Ceiling;
            k.Box(table, "Lamp cord", V(0f, (ceiling + 2.9f) / 2f, 0f), V(0.02f, ceiling - 2.9f, 0.02f), c.P.Lit(new Color(0.05f, 0.05f, 0.05f)), collider: false);
            if (k.Real(table, "modern_ceiling_lamp_01", V(0f, 2.4f, 0f), 0f, 1.2f) == null)
                k.Cylinder(table, "Lamp shade", V(0f, 2.8f, 0f), 0.6f, 0.25f, c.P.Metal(new Color(0.72f, 0.53f, 0.3f)));
        }

        private static void PokerRoom(CityContext c, Transform inner, Transform dyn, CasinoLife life)
        {
            // Back left, through the arch (§35): two cash tables and the Sit & Go table. Open 10 AM to 4 AM.
            Kit k = c.Kit;
            Material wall = c.P.Surface(Finish.PaintedPlaster, new Color(0.08f, 0.2f, 0.14f), 0.05f);
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            const float x = -14f, z = 14f;
            k.WallZ(inner, "Poker room wall", z, Depth - Wall / 2f, x, 0f, Ceiling, 0.25f, wall, new Opening(22f, 3.2f, 0f, 3.2f));
            k.WallX(inner, "Poker room wall", -HalfWidth, x + 0.125f, z, 0f, Ceiling, 0.25f, wall);
            k.Span(inner, "Arch", V(x - 0.2f, 3.2f, 20.3f), V(x + 0.2f, 3.5f, 23.7f), bronze, collider: false);
            TextMesh sign = k.Text(inner, "POKER ROOM", V(x + 0.15f, 4.1f, 22f), -90f, 0.34f, Color.white);
            sign.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(1f, 0.82f, 0.45f));
            k.Text(inner, "NO-LIMIT HOLD'EM · SIT & GO · 10 AM – 4 AM", V(x + 0.15f, 3.7f, 22f), -90f, 0.11f, new Color(0.95f, 0.9f, 0.8f));
            k.Span(inner, "Poker carpet", V(-HalfWidth + Wall / 2f, 0.021f, z + 0.15f), V(x - 0.15f, 0.03f, Depth - Wall / 2f),
                c.P.Lit(new Color(0.09f, 0.15f, 0.12f), 0.05f), collider: false);

            var hours = new WorkSchedule { Shift = Hours.Of(10, 4) };
            (string Name, Vector3 At, PokerStakes Stakes)[] tables =
            {
                ("Poker 1", V(-30.5f, 0f, 19f), PokerStakes.OneTwo),
                ("Poker 2", V(-20.5f, 0f, 19f), PokerStakes.TwoFive),
                ("Sit & Go", V(-25.5f, 0f, 27.2f), null),
            };
            for (int i = 0; i < tables.Length; i++)
            {
                var (name, at, stakes) = tables[i];
                BuildPoker(c, inner, dyn, name, at, stakes, hours, 9140 + i);
                c.PointLight(inner, at + V(0f, 3.2f, 0f), 7f, 1.2f, new Color(1f, 0.93f, 0.82f));
            }
            c.Anchor("casino_poker", inner.TransformPoint(V(-30.5f, 0f, 19f) + CasinoProps.PokerOnEllipse(270f, 1.9f, 0f)));
            c.Anchor("casino_sng", inner.TransformPoint(V(-25.5f, 0f, 27.2f) + CasinoProps.PokerOnEllipse(270f, 1.9f, 0f)));
            life.AddSpot(k, dyn, V(-16f, 0f, 16f), 45f, NpcPose.Phone, 9720, 0.6f);
        }

        public static PokerTableView BuildPoker(CityContext c, Transform inner, Transform dyn, string name, Vector3 at, PokerStakes stakes, WorkSchedule hours, int seed)
        {
            Kit k = c.Kit;
            Transform t = Kit.Group(inner, name, at);
            CasinoProps.PokerTable(k, t, name, stakes != null ? stakes.Name.ToUpperInvariant() : "SIT & GO · TOP TWO PAID");
            LowLamp(c, t, at.y);
            Transform live = Kit.Group(dyn, name + " (play)", at);
            StaffNpc dealer = StaffNpc.Create(k, dyn, "Poker dealer", seed, new Color(0.08f, 0.08f, 0.1f), hours,
                new[] { at + V(0f, 0f, CasinoProps.PokerB * 1.45f), at + V(0f, 0f, CasinoProps.PokerB * 1.45f + 1.2f) }, 180f,
                new[] { NpcPose.Stand }, () => "Seat open if you want it.",
                Cycle("No-limit hold'em. The house takes a small rake, the rest is between the players.", "Blinds go round clockwise.", "Nobody sees your cards but you."),
                c.Game, c.Hud, c.Player, look: "Suit");
            return PokerTableView.Build(c, live, name, stakes, dealer);
        }
    }
}
