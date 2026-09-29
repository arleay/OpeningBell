using System.Collections.Generic;
using OpeningBell.Casino;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>The Meridian's Tide Bar and lounge, its signs and lights, and the helpers the other parts share.</summary>
    public static partial class Meridian
    {
        private static void Bar(CityContext c, Transform inner, Transform dyn, CasinoLife life)
        {
            Kit k = c.Kit;
            Material wood = c.P.Lit(new Color(0.24f, 0.12f, 0.07f), 0.45f);
            Material marble = c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.85f);
            Material warm = c.P.Glow(new Color(1f, 0.7f, 0.35f), 1.4f);
            float z1 = Depth - Wall / 2f;
            // The bar: a long counter with a dark marble top and a glowing footrail line, the back bar on the wall.
            k.Span(inner, "Bar", V(16f, 0f, 26.2f), V(33f, 1.08f, 27f), wood);
            k.Span(inner, "Bar top", V(15.9f, 1.08f, 26.05f), V(33.1f, 1.14f, 27.1f), marble, collider: false);
            k.Span(inner, "Bar glow", V(16f, 0.25f, 26.17f), V(33f, 0.29f, 26.2f), warm, collider: false);
            k.Span(inner, "Back counter", V(16f, 0f, z1 - 0.6f), V(33f, 0.95f, z1), wood);
            Material glass = c.P.Glass(new Color(0.6f, 0.62f, 0.65f, 0.5f));
            k.Span(inner, "Mirror", V(16.5f, 1.1f, z1 - 0.03f), V(32.5f, 3.2f, z1 - 0.01f), c.P.Metal(new Color(0.8f, 0.82f, 0.85f), 0.95f), collider: false);
            for (int s = 0; s < 3; s++)
            {
                float y = 1.35f + s * 0.55f;
                k.Span(inner, "Shelf", V(16.5f, y - 0.03f, z1 - 0.35f), V(32.5f, y, z1 - 0.03f), glass, collider: false);
                for (float x = 17f; x < 32.2f; x += 0.9f)
                    k.Prop(inner, (int)(x * 3 + s) % 3 == 0 ? "food_sm_wine_bottle" : "food_sm_bottle", V(x + (s % 2) * 0.3f, y, z1 - 0.2f), 0.3f);
            }
            TextMesh name = k.Text(inner, "THE TIDE BAR", V(24.5f, 3.75f, z1 - 0.05f), 0f, 0.4f, Color.white);
            name.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(0.4f, 0.9f, 1f));

            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            StaffNpc bartender = StaffNpc.Create(k, dyn, "Bartender", 9150, new Color(0.08f, 0.08f, 0.1f), always,
                new[] { V(24.5f, 0f, 28.3f), V(33.5f, 0f, 28.5f) }, 180f, new[] { NpcPose.Stand, NpcPose.Typing, NpcPose.Drink }, () => "What are we drinking?",
                Cycle("House special's the Meridian Old Fashioned.", "Soft drinks are on us for Gold members.", "Big night?", "Water's always free. Have one between rounds."),
                c.Game, c.Hud, c.Player, look: "Casual");
            var venue = new MenuVenue { Name = "The Tide Bar", Where = "Order", Items = CasinoMenus.TideBar, Staff = bartender };
            foreach (float x in new[] { 20.5f, 24.5f, 28.5f }) Counter(dyn, V(x, 1.25f, 26.55f), V(1.6f, 0.3f, 0.7f), venue);
            k.Prop(inner, "food_sm_bottle", V(22.5f, 1.14f, 26.55f), 0.22f);
            // Stools you can sit on (and order from), the rest just stools.
            int stool = 0;
            for (float x = 17f; x <= 32.1f; x += 1.5f, stool++)
            {
                if (k.Real(inner, "bar_chair_round_01", V(x, 0.02f, 25.4f), 0f) == null)
                    k.Cylinder(inner, "Bar stool", V(x, 0.38f, 25.4f), 0.38f, 0.76f, c.P.Lit(new Color(0.12f, 0.07f, 0.05f)), collider: true);
                if (stool % 3 == 1) Chair(c, dyn, V(x, 0f, 25.4f), 0f, "Sit at the bar", 1.25f, venue);
            }
            c.Anchor("casino_bar", inner.TransformPoint(V(24.5f, 0f, 24.6f)));

            // The lounge: a rug, two sofas facing across a round table, lounge chairs, plants, the piano.
            k.Span(inner, "Lounge rug", V(15f, 0.02f, 17.6f), V(31f, 0.035f, 24.4f), c.P.Lit(new Color(0.12f, 0.1f, 0.09f), 0.05f), collider: false);
            foreach (float lx in new[] { 19.5f, 27f })
            {
                Solid(k, k.Real(inner, "sofa_03", V(lx, 0.035f, 19.4f), 0f));
                Solid(k, k.Real(inner, "sofa_03", V(lx, 0.035f, 23.4f), 180f));
                Solid(k, k.Real(inner, "coffee_table_round_01", V(lx, 0.035f, 21.4f), 0f));
                Chair(c, dyn, V(lx, 0f, 19.4f), 0f, "Sit on the sofa", 0.9f, null);
            }
            Solid(k, k.Real(inner, "mid_century_lounge_chair", V(22.2f, 0.035f, 21.4f), -90f));
            Solid(k, k.Real(inner, "mid_century_lounge_chair", V(24.3f, 0.035f, 21.4f), 90f));
            k.Real(inner, "calathea_orbifolia_01", V(30.6f, 0.03f, 17.9f), 0f, 1.3f);
            k.Real(inner, "anthurium_botany_01", V(15.4f, 0.03f, 24f), 0f, 1.3f);
            Hang(c, inner, "Chandelier_03", V(23.2f, 0f, 21.4f), 1.4f);
            Piano(c, inner, V(16.4f, 0.035f, 20.2f));
            NpcBody pianist = NpcBody.Create(k, dyn, "Pianist", 9160, new Color(0.08f, 0.08f, 0.1f), "Suit");
            pianist.transform.localPosition = V(16.4f - 1.05f, 0.035f, 20.2f);
            pianist.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            life.SetPianist(pianist);

            // Regulars, two of them trader types (§56).
            life.AddSpot(k, dyn, V(19.2f, 0f, 25.1f), 0f, NpcPose.Drink, 9161, 0.1f);
            life.AddSpot(k, dyn, V(30.4f, 0f, 25.1f), 0f, NpcPose.Drink, 9162, 0.45f);
            life.AddSpot(k, dyn, V(26.2f, 0f, 25.1f), 0f, NpcPose.Drink, 9163, 0.8f);
            life.AddSpot(k, dyn, V(28.6f, 0.035f, 21.2f), -90f, NpcPose.Phone, 9164, 0.35f);
            NpcBody dana = life.AddRegular(k, dyn, V(21.3f, 0.035f, 17.2f), 20f, NpcPose.Drink, 9165, "Dana Whitlock", "Suit");
            dana.GetComponent<PatronTalk>().Configure(c.Game, c.Hud, 9165, "Dana Whitlock", TraderLines(c.Game, 9165,
                "Ran a fund for twenty years. Now I lose money slowly, at the bar, on purpose.",
                "Tables have rules. Markets pretend to. I'll take the tables tonight.",
                "Never mistake a good run for a good system. Took me a decade to learn that one."));
            NpcBody rick = life.AddRegular(k, dyn, V(31.6f, 0f, 25.1f), -20f, NpcPose.Phone, 9166, "Rick Sato", "Casual");
            rick.GetComponent<PatronTalk>().Configure(c.Game, c.Hud, 9166, "Rick Sato", TraderLines(c.Game, 9166,
                "I day-trade. Well. I did. Margin call Tuesday. Blackjack's cheaper.",
                "You trade? Don't do what I did. Whatever it is, don't.",
                "Up big this morning, gave it all back by lunch. So, same as here."));
        }

        /// <summary>A grand piano, built here: a lacquered case on three legs, the lid propped, a bench.</summary>
        private static void Piano(CityContext c, Transform parent, Vector3 at)
        {
            Kit k = c.Kit;
            Transform piano = Kit.Group(parent, "Piano", at, 90f);
            Material lacquer = c.P.Lit(new Color(0.02f, 0.02f, 0.025f), 0.95f);
            k.Box(piano, "Case", V(0f, 0.85f, 0.35f), V(1.45f, 0.3f, 1.6f), lacquer);
            k.Box(piano, "Tail", V(0.25f, 0.85f, 1.35f), V(0.95f, 0.3f, 0.6f), lacquer, collider: false);
            k.Box(piano, "Lid", V(0f, 1.25f, 0.55f), V(1.4f, 0.03f, 1.7f), lacquer, collider: false).transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            k.Box(piano, "Keys", V(0f, 0.8f, -0.5f), V(1.35f, 0.04f, 0.18f), c.P.Lit(new Color(0.95f, 0.94f, 0.9f), 0.5f), collider: false);
            foreach (Vector3 leg in new[] { V(-0.6f, 0.35f, -0.35f), V(0.6f, 0.35f, -0.35f), V(0.2f, 0.35f, 1.5f) })
                k.Box(piano, "Leg", leg, V(0.08f, 0.7f, 0.08f), lacquer, collider: false);
            k.Box(piano, "Bench", V(0f, 0.25f, -1.05f), V(0.9f, 0.5f, 0.35f), lacquer);
        }

        /// <summary>A trader's lines: their own, and today's market talk (never a tip, §56).</summary>
        public static System.Func<string> TraderLines(GameBootstrap game, int seed, params string[] own)
        {
            var rng = new System.Random(seed);
            int n = 0;
            return () => n++ % 2 == 0 ? own[(n / 2) % own.Length] : PatronTalk.MarketLine(game, rng);
        }

        /// <summary>An ordering spot (bar counter, café counter, room phone): a trigger with a menu.</summary>
        public static MenuCounter Counter(Transform dyn, Vector3 at, Vector3 size, MenuVenue venue, System.Func<string> locked = null)
        {
            var tap = new GameObject(venue.Name + " order");
            tap.transform.SetParent(dyn, false);
            tap.transform.localPosition = at;
            var box = tap.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            MenuCounter counter = tap.AddComponent<MenuCounter>();
            counter.Configure(venue, locked);
            return counter;
        }

        /// <summary>A seat to sit on (optionally ordering from <paramref name="venue"/>).</summary>
        public static CasinoChair Chair(CityContext c, Transform dyn, Vector3 at, float yaw, string verb, float eyeHeight, MenuVenue venue)
        {
            var go = new GameObject("Seat");
            go.transform.SetParent(dyn, false);
            go.transform.localPosition = at + V(0f, 0.5f, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = V(0.55f, 0.6f, 0.55f);
            var chair = go.AddComponent<CasinoChair>();
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            chair.Configure(c, verb, at + r * V(0f, 0f, -0.45f), at + r * V(0f, eyeHeight, -0.05f), at + r * V(0f, eyeHeight - 0.15f, 2f), venue);
            return chair;
        }

        private static void Signs(CityContext c, Transform inner)
        {
            // Finding your way without a map (§72): lit signs hung where the routes split.
            HangingSign(c, inner, "< CASHIER · RESTROOMS", V(-10.5f, 3.9f, 9.5f), new Color(1f, 0.85f, 0.5f), 3.4f);
            HangingSign(c, inner, "HOTEL · ELEVATORS >", V(10.5f, 3.9f, 9.5f), new Color(1f, 0.85f, 0.5f), 3.4f);
            HangingSign(c, inner, "TABLE GAMES", V(0f, 4.2f, 23.8f), new Color(1f, 0.82f, 0.45f), 3f);
            HangingSign(c, inner, "< POKER ROOM", V(-10f, 3.9f, 21.8f), new Color(0.5f, 1f, 0.7f), 2.6f);
            HangingSign(c, inner, "TIDE BAR · LOUNGE >", V(10f, 3.9f, 21.8f), new Color(0.4f, 0.9f, 1f), 3.2f);
        }

        /// <summary>A lit sign hung from the ceiling, readable from both sides (a board between the faces).</summary>
        public static void HangingSign(CityContext c, Transform parent, string text, Vector3 at, Color color, float width, float ceiling = Ceiling)
        {
            Kit k = c.Kit;
            k.Box(parent, "Sign board", at, V(width, 0.62f, 0.04f), c.P.Lit(new Color(0.05f, 0.04f, 0.05f), 0.6f), collider: false);
            k.Box(parent, "Sign hanger", V(at.x, (ceiling + at.y + 0.31f) / 2f, at.z), V(0.03f, ceiling - at.y - 0.31f, 0.03f), c.P.Lit(new Color(0.05f, 0.05f, 0.05f)), collider: false);
            foreach ((float side, float yaw) in new[] { (-0.035f, 0f), (0.035f, 180f) })
            {
                TextMesh s = CasinoProps.Fit(k.Text(parent, text, at + V(0f, 0f, side), yaw, 0.3f, Color.white), width - 0.25f);
                s.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(color);
            }
        }

        private static void Lights(CityContext c, Transform m)
        {
            // Pools of warm light over the games, dim between them: the room reads by its glow (slots, coves, bar).
            var warm = new Color(1f, 0.86f, 0.66f);
            var table = new Color(1f, 0.93f, 0.82f);
            c.PointLight(m, V(0f, 5.2f, 6f), 15f, 2.2f, warm);
            c.PointLight(m, V(0f, 5f, 16f), 15f, 1.4f, warm);
            c.PointLight(m, V(-6f, 3.1f, 20.2f), 7f, 1.3f, table);
            c.PointLight(m, V(6f, 3.1f, 20.2f), 7f, 1.3f, table);
            c.PointLight(m, V(-8.5f, 3.1f, 27.3f), 7f, 1.2f, table);
            c.PointLight(m, V(8.5f, 3.1f, 27.3f), 7f, 1.2f, table);
            c.PointLight(m, V(0f, 3.1f, 27.3f), 6f, 1f, table);
            c.PointLight(m, V(-31f, 3.5f, 4.5f), 10f, 1.6f, warm);
            c.PointLight(m, V(20.5f, 4.5f, 9f), 15f, 1.5f, new Color(0.9f, 0.85f, 1f));
            c.PointLight(m, V(-17.5f, 4.5f, 9f), 12f, 1.4f, new Color(0.9f, 0.85f, 1f));
            c.PointLight(m, V(24.5f, 4f, 28f), 13f, 1.7f, new Color(1f, 0.75f, 0.45f));
            c.PointLight(m, V(23f, 4.5f, 21f), 13f, 1.5f, warm);
            c.PointLight(m, V(32f, 4.5f, 8f), 12f, 1.6f, new Color(1f, 0.95f, 0.88f));
        }

        /// <summary>A light fitting hung so its top touches the ceiling, scaled by <paramref name="scale"/>.</summary>
        public static void Hang(CityContext c, Transform parent, string model, Vector3 at, float scale, float ceiling = Ceiling)
        {
            Bounds b = c.Kit.RealBounds(model);
            if (b.size.y <= 0f) return;
            c.Kit.Real(parent, model, V(at.x, ceiling - b.size.y * scale, at.z), 0f, scale);
        }

        public static void Solid(Kit k, GameObject model)
        {
            if (model != null) k.Solid(model);
        }

        public static System.Func<string> Cycle(params string[] lines)
        {
            var queue = new Queue<string>(lines);
            return () =>
            {
                string l = queue.Dequeue();
                queue.Enqueue(l);
                return l;
            };
        }
    }

    /// <summary>The Diamond Dusk progressive meter: the live pool over the bank.</summary>
    public sealed class JackpotMeter : MonoBehaviour
    {
        private GameBootstrap _game;
        private TextMesh _front, _back;

        public void Configure(GameBootstrap game, TextMesh front, TextMesh back)
        {
            _game = game;
            _front = front;
            _back = back;
        }

        private void Update()
        {
            string text = "PROGRESSIVE " + CasinoMoney.Cents(_game.Casino.Jackpot.Pool);
            if (_front.text == text) return;
            _front.text = text;
            _back.text = text;
        }
    }
}
