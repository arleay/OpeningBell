using System.Collections.Generic;
using OpeningBell.Casino;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The casino's own furniture, modelled here rather than boxed: the D-shaped blackjack table with its padded rail,
    /// felt, betting circles and chip tray; chips by denomination; playing cards (Kenney's CC0 faces, cropped to the
    /// card); slot cabinets; the patterned carpet. Everything is in the parent's frame, metres.
    /// </summary>
    public static class CasinoProps
    {
        // ---------------------------------------------------------------- chips

        /// <summary>Chip colours by denomination (the usual US casino scheme): $1 white … $5,000 brown.</summary>
        public static Color ChipColor(decimal value) => value switch
        {
            1m => new Color(0.92f, 0.92f, 0.9f),
            5m => new Color(0.78f, 0.1f, 0.1f),
            25m => new Color(0.1f, 0.55f, 0.22f),
            100m => new Color(0.08f, 0.08f, 0.09f),
            500m => new Color(0.45f, 0.18f, 0.6f),
            1000m => new Color(0.95f, 0.72f, 0.12f),
            _ => new Color(0.5f, 0.3f, 0.16f),
        };

        /// <summary>Chips are 39 mm × 3.3 mm; drawn a little larger so a stack reads from a seat.</summary>
        public const float ChipDiameter = 0.046f, ChipHeight = 0.0042f;

        /// <summary>
        /// A pile of chips making <paramref name="amount"/> on a spot: one stack per denomination, side by side along x,
        /// capped at <paramref name="maxPerStack"/> chips high (bigger amounts just look like tall stacks).
        /// </summary>
        public static void Stacks(Kit k, Transform parent, Vector3 spot, decimal amount, int maxPerStack = 20)
        {
            List<(decimal value, int count)> breakdown = ChipDenominations.Breakdown(amount);
            float step = ChipDiameter * 1.08f;
            float x0 = -(breakdown.Count - 1) * step / 2f;
            for (int s = 0; s < breakdown.Count; s++)
            {
                (decimal value, int count) = breakdown[s];
                int n = Mathf.Min(count, maxPerStack);
                Material face = k.P.Lit(ChipColor(value), 0.35f);
                Material edge = k.P.Lit(Color.Lerp(ChipColor(value), Color.white, 0.55f), 0.35f);
                Vector3 at = spot + new Vector3(x0 + s * step, 0f, 0f);
                // One cylinder per chip would be hundreds of objects: a stack is one body plus a stripe every few chips.
                k.Cylinder(parent, $"${value} chips", at + Vector3.up * (n * ChipHeight / 2f), ChipDiameter, n * ChipHeight, face);
                for (int i = 0; i < n; i += 3)
                    k.Cylinder(parent, "Edge spots", at + Vector3.up * ((i + 0.5f) * ChipHeight), ChipDiameter * 1.004f, ChipHeight * 0.34f, edge);
            }
        }

        // ---------------------------------------------------------------- cards

        public const float CardWidth = 0.084f, CardLength = 0.12f;
        private static Mesh _cardMesh;
        private static readonly Dictionary<string, Material> CardMaterials = new Dictionary<string, Material>();

        /// <summary>
        /// A card lying face up, flat on the felt. The Kenney faces are 64 px squares with the card itself in pixels
        /// 11–52 across and 2–61 down, so the quad's UVs crop to that and the transparent margin never shows.
        /// </summary>
        public static GameObject Card(Kit k, Transform parent, Card? card, Vector3 at, float yaw)
        {
            var go = new GameObject(card.HasValue ? card.Value.ToString() : "Card (face down)");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = CardMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = CardMaterial(k, card.HasValue ? TextureName(card.Value) : "card_back");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static string TextureName(Card c)
        {
            string suit = c.Suit switch { Suit.Clubs => "clubs", Suit.Diamonds => "diamonds", Suit.Hearts => "hearts", _ => "spades" };
            string rank = c.Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => c.Rank.ToString("00") };
            return $"card_{suit}_{rank}";
        }

        private static Material CardMaterial(Kit k, string texture)
        {
            if (CardMaterials.TryGetValue(texture, out Material m) && m != null) return m;
            var tex = Resources.Load<Texture2D>("Cards/" + texture);
            m = tex != null ? k.P.Textured(texture, tex, Color.white, 0.25f) : k.P.Lit(new Color(0.95f, 0.95f, 0.92f));
            return CardMaterials[texture] = m;
        }

        private static Mesh CardMesh()
        {
            if (_cardMesh != null) return _cardMesh;
            float w = CardWidth / 2f, l = CardLength / 2f;
            // The top of the card (the corner index) points away from the player: +z.
            const float u0 = 11f / 64f, u1 = 53f / 64f, v0 = 2f / 64f, v1 = 62f / 64f;
            _cardMesh = new Mesh { name = "Card" };
            _cardMesh.SetVertices(new[] { new Vector3(-w, 0f, -l), new Vector3(w, 0f, -l), new Vector3(w, 0f, l), new Vector3(-w, 0f, l) });
            _cardMesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            _cardMesh.SetUVs(0, new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) });
            _cardMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _cardMesh.RecalculateBounds();
            return _cardMesh;
        }

        // ---------------------------------------------------------------- the blackjack table

        /// <summary>Half-width of the table and how far the curve reaches toward the players from the dealer's edge.</summary>
        public const float TableHalfWidth = 1.2f, TableReach = 1.1f, FeltY = 0.8f;
        /// <summary>The dealer's straight edge is at +z = <see cref="DealerEdge"/>; the players sit round the curve (−z).</summary>
        public const float DealerEdge = 0.45f;
        public const int Seats = 5;

        /// <summary>Point on the table's curve at <paramref name="t"/> (0 = the dealer's right corner, 1 = the left), scaled.</summary>
        private static Vector3 Curve(float t, float scale, float y)
        {
            float a = Mathf.PI * (1f + t); // 180° → 360°
            return new Vector3(Mathf.Cos(a) * TableHalfWidth * scale, y, DealerEdge + Mathf.Sin(a) * TableReach * scale);
        }

        /// <summary>Seat <paramref name="s"/>'s place round the curve (0 = the player's far left, "third base" is the last).</summary>
        public static float SeatT(int s) => 0.14f + s * (0.72f / (Seats - 1));

        /// <summary>Where seat <paramref name="s"/>'s bet goes, on the felt.</summary>
        public static Vector3 BetSpot(int s) => Curve(SeatT(s), 0.72f, FeltY + 0.002f);

        /// <summary>Where the stool for seat <paramref name="s"/> stands (floor).</summary>
        public static Vector3 StoolSpot(int s) => Curve(SeatT(s), 1.42f, 0f);

        /// <summary>Which way a player in seat <paramref name="s"/> faces (toward the dealer).</summary>
        public static float SeatYaw(int s)
        {
            Vector3 d = new Vector3(0f, 0f, DealerEdge) - StoolSpot(s);
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        public static void BlackjackTable(Kit k, Transform parent, BlackjackRules rules, Color felt, string name) =>
            DTable(k, parent, felt, name, $"${rules.MinBet:N0} – ${rules.MaxBet:N0}",
                ("BLACKJACK PAYS " + (rules.BlackjackPays == 1.5m ? "3 TO 2" : "6 TO 5"), 0.032f),
                (rules.DealerHitsSoft17 ? "DEALER HITS SOFT 17" : "DEALER MUST STAND ON ALL 17s", 0.022f));

        public static void BaccaratTable(Kit k, Transform parent, BaccaratRules rules, Color felt, string name)
        {
            DTable(k, parent, felt, name, $"${rules.MinBet:N0} – ${rules.MaxBet:N0}", ("PUNTO BANCO", 0.03f), ("TIE PAYS 8 TO 1 · BANKER 5% COMMISSION", 0.018f));
            // Where the two hands are dealt, marked on the felt.
            Material print = k.P.Lit(Color.Lerp(felt, new Color(0.95f, 0.85f, 0.55f), 0.75f), 0.05f);
            FeltText(k, parent, "PLAYER", new Vector3(-0.3f, FeltY + 0.0015f, DealerEdge - 0.14f), 0.022f, print);
            FeltText(k, parent, "BANKER", new Vector3(0.3f, FeltY + 0.0015f, DealerEdge - 0.14f), 0.022f, print);
        }

        /// <summary>The D-shaped card table (blackjack, mini-baccarat) with two printed lines on the felt.</summary>
        public static void DTable(Kit k, Transform parent, Color felt, string name, string limits, params (string Text, float Size)[] lines)
        {
            const int segments = 28;
            Material wood = k.P.Lit(new Color(0.32f, 0.16f, 0.08f), 0.55f);
            Material leather = k.P.Lit(new Color(0.12f, 0.07f, 0.05f), 0.4f);
            Material cloth = k.P.Lit(felt, 0.02f);
            Material gold = k.P.Metal(new Color(0.85f, 0.68f, 0.32f), 0.7f);
            Material print = k.P.Lit(Color.Lerp(felt, new Color(0.95f, 0.85f, 0.55f), 0.75f), 0.05f);

            // Outline of the table: the dealer's straight edge, then the curve.
            List<Vector3> Outline(float scale, float y)
            {
                var pts = new List<Vector3> { new Vector3(0f, y, DealerEdge) };
                for (int i = 0; i <= segments; i++) pts.Add(Curve(i / (float)segments, scale, y));
                return pts;
            }

            // Apron (wood) under the felt, the felt itself, then the padded rail round the curve.
            var apron = new MeshBuilder();
            apron.Slab(Outline(1.1f, FeltY - 0.012f), 0.1f);
            apron.Build(parent, "Table apron", wood, false);
            var baize = new MeshBuilder();
            baize.Slab(Outline(1f, FeltY), 0.012f);
            baize.Build(parent, "Felt", cloth, false);

            var rail = new MeshBuilder();
            const float railTop = FeltY + 0.055f;
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments, t1 = (i + 1) / (float)segments;
                Vector3 in0 = Curve(t0, 1f, railTop), in1 = Curve(t1, 1f, railTop);
                Vector3 out0 = Curve(t0, 1.1f, railTop), out1 = Curve(t1, 1.1f, railTop);
                rail.Quad(out0, out1, in1, in0);
                rail.Wall(out0 + Vector3.down * 0.07f, out1 + Vector3.down * 0.07f, 0.07f); // outer face, outward
                rail.Wall(in1 + Vector3.down * 0.055f, in0 + Vector3.down * 0.055f, 0.055f); // inner face, toward the felt
            }
            rail.Build(parent, "Padded rail", leather, false);

            // Pedestal and the dealer's side.
            k.Cylinder(parent, "Pedestal", new Vector3(0f, 0.36f, 0f), 0.5f, 0.7f, wood);
            k.Box(parent, "Dealer apron", new Vector3(0f, FeltY - 0.12f, DealerEdge - 0.04f), new Vector3(TableHalfWidth * 2.1f, 0.2f, 0.08f), wood, collider: false);
            // One collider for the whole table, so nobody walks through it (the stools stand clear of it).
            var solid = new GameObject("Table collider");
            solid.transform.SetParent(parent, false);
            var box = solid.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, FeltY / 2f, DealerEdge - TableReach * 0.55f);
            box.size = new Vector3(TableHalfWidth * 2.1f, FeltY + 0.06f, TableReach * 1.15f);

            // Betting circles: a gold ring on the felt in front of each seat.
            var rings = new MeshBuilder();
            for (int s = 0; s < Seats; s++) Ring(rings, BetSpot(s) + Vector3.up * 0.001f, 0.075f, 0.087f);
            rings.Build(parent, "Betting circles", gold, false);

            // Chip tray at the dealer's edge: racks of each colour.
            k.Box(parent, "Chip tray", new Vector3(0f, FeltY + 0.02f, DealerEdge - 0.1f), new Vector3(0.62f, 0.04f, 0.13f), k.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.6f), collider: false);
            decimal[] tray = { 1m, 5m, 25m, 100m, 500m, 1000m };
            for (int i = 0; i < tray.Length; i++)
            {
                // Chips lie on edge in the tray's rows: drawn as a long rounded bar in the chip's colour.
                var row = k.Cylinder(parent, "Tray chips", new Vector3(-0.26f + i * 0.104f, FeltY + 0.045f, DealerEdge - 0.1f), ChipDiameter, 0.11f, k.P.Lit(ChipColor(tray[i]), 0.35f));
                row.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            // The shoe and the discard rack.
            k.Box(parent, "Shoe", new Vector3(TableHalfWidth * 0.62f, FeltY + 0.06f, DealerEdge - 0.14f), new Vector3(0.12f, 0.1f, 0.2f), k.P.Lit(new Color(0.15f, 0.08f, 0.05f), 0.5f), collider: false, yaw: -20f);
            k.Box(parent, "Discards", new Vector3(-TableHalfWidth * 0.62f, FeltY + 0.04f, DealerEdge - 0.14f), new Vector3(0.1f, 0.07f, 0.14f), k.P.Glass(new Color(0.7f, 0.8f, 0.85f, 0.35f)), collider: false, yaw: 20f);

            // Printed on the felt: the game's lines; the limits on a placard.
            for (int i = 0; i < lines.Length; i++)
                FeltText(k, parent, lines[i].Text, new Vector3(0f, FeltY + 0.0015f, DealerEdge - 0.36f - i * 0.08f), lines[i].Size, print);
            Transform placard = Kit.Group(parent, "Limit placard", new Vector3(-TableHalfWidth * 0.36f, FeltY, DealerEdge - 0.08f));
            k.Box(placard, "Stand", new Vector3(0f, 0.06f, 0f), new Vector3(0.16f, 0.12f, 0.01f), k.P.Lit(new Color(0.05f, 0.05f, 0.06f), 0.6f), collider: false);
            Fit(k.Text(placard, limits, new Vector3(0f, 0.07f, -0.007f), 0f, 0.022f, new Color(1f, 0.85f, 0.45f)), 0.15f);
            k.Text(placard, name.ToUpperInvariant(), new Vector3(0f, 0.035f, -0.007f), 0f, 0.011f, new Color(0.9f, 0.9f, 0.9f));
        }

        private static void FeltText(Kit k, Transform parent, string text, Vector3 at, float height, Material m)
        {
            TextMesh t = k.Text(parent, text, at, 0f, height, Color.white);
            // Flat on the felt, reading from the players' side (the text's top toward the dealer).
            t.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.GetComponent<MeshRenderer>().sharedMaterial = k.P.Sign(m.color);
        }

        private static void Ring(MeshBuilder mb, Vector3 centre, float inner, float outer, int segments = 24)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 i0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * inner;
                Vector3 i1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * inner;
                Vector3 o0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * outer;
                Vector3 o1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * outer;
                mb.Quad(o0, o1, i1, i0);
            }
        }

        // ---------------------------------------------------------------- slot cabinets

        /// <summary>Shrinks a sign's text until it fits <paramref name="width"/> metres (long names on narrow cabinets).</summary>
        public static TextMesh Fit(TextMesh t, float width)
        {
            var r = t.GetComponent<MeshRenderer>();
            float yaw = t.transform.eulerAngles.y % 180f;
            float w = Mathf.Abs(yaw - 90f) < 45f ? r.bounds.size.z : r.bounds.size.x;
            if (w > width && w > 0f) t.characterSize *= width / w;
            return t;
        }

        /// <summary>Reel window geometry on a cabinet (local): centre, reel pitch across, row pitch down, symbol size.</summary>
        public static readonly Vector3 ReelCentre = new Vector3(0f, 1.36f, -0.268f);
        public const float ReelPitch = 0.165f, RowPitch = 0.098f;
        public static readonly Vector2 SymbolSize = new Vector2(0.15f, 0.092f);

        public static Vector3 ReelSpot(int reel, int row) => ReelCentre + new Vector3((reel - 1) * ReelPitch, (1 - row) * RowPitch, 0f);

        /// <summary>
        /// An upright slot cabinet facing −z (CASINO_SPEC §29): base, belly glass, the three reels (showing
        /// <paramref name="stops"/>), the payline, an info screen, a dark topper with the game's name in its colour,
        /// the button deck and a stool. The live reels of a machine being played are drawn over these.
        /// </summary>
        public static void SlotCabinet(Kit k, Transform parent, Vector3 at, float yaw, SlotDefinition d, Color theme, int[] stops)
        {
            Transform s = Kit.Group(parent, "Slot cabinet " + d.Name, at, yaw);
            Material body = k.P.Lit(new Color(0.07f, 0.07f, 0.08f), 0.7f);
            Material trim = k.P.Metal(new Color(0.82f, 0.82f, 0.85f), 0.8f);
            Material glow = k.P.Glow(theme, 1.6f);
            k.Box(s, "Base", new Vector3(0f, 0.45f, 0f), new Vector3(0.66f, 0.9f, 0.6f), body);
            k.Box(s, "Belly glass", new Vector3(0f, 0.6f, -0.305f), new Vector3(0.52f, 0.34f, 0.01f), k.P.Glow(Color.Lerp(theme, Color.black, 0.55f), 1.1f), collider: false);
            Fit(k.Text(s, d.Name.ToUpperInvariant(), new Vector3(0f, 0.66f, -0.312f), 0f, 0.045f, Color.white), 0.46f);
            Fit(k.Text(s, $"{CasinoMoney.Whole(d.MinLineBet)}–{CasinoMoney.Whole(d.MaxLineBet)} A LINE · {d.LineCount} LINE{(d.LineCount > 1 ? "S" : "")}", new Vector3(0f, 0.55f, -0.312f), 0f, 0.022f, new Color(1f, 0.9f, 0.6f)), 0.46f);
            k.Box(s, "Button deck", new Vector3(0f, 0.95f, -0.3f), new Vector3(0.66f, 0.07f, 0.28f), body, collider: false).transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            for (int i = 0; i < 4; i++)
                k.Box(s, "Button", new Vector3(-0.2f + i * 0.13f, 0.995f, -0.36f), new Vector3(0.07f, 0.02f, 0.045f), k.P.Glow(i == 3 ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.9f, 0.5f), 1.3f), collider: false);
            k.Box(s, "Upper cabinet", new Vector3(0f, 1.45f, 0.02f), new Vector3(0.66f, 0.95f, 0.56f), body);
            k.Box(s, "Reel window", new Vector3(0f, 1.36f, -0.262f), new Vector3(0.52f, 0.32f, 0.01f), k.P.Unlit(new Color(0.12f, 0.1f, 0.08f)), collider: false);
            for (int r = 0; r < 3; r++)
                for (int row = 0; row < 3; row++)
                    k.Quad(s, "Reel", ReelSpot(r, row), SymbolSize, 0f, CasinoArt.Symbol(k.P, d.At(stops[r] + row - 1)));
            // Payline markers at the window's edges (not across the symbols).
            foreach (float x in new[] { -0.255f, 0.255f })
                k.Box(s, "Payline", new Vector3(x, 1.36f, -0.272f), new Vector3(0.012f, 0.03f, 0.004f), k.P.Glow(new Color(1f, 0.2f, 0.2f), 1.6f), collider: false);
            k.Box(s, "Info screen", new Vector3(0f, 1.62f, -0.262f), new Vector3(0.5f, 0.11f, 0.01f), k.P.Glow(new Color(0.03f, 0.06f, 0.16f), 1.2f), collider: false);
            Fit(k.Text(s, $"PAYS UP TO {d.ThreeOfAKind[d.TopSymbol]}×" + (d.Progressive ? " · PROGRESSIVE" : ""), new Vector3(0f, 1.62f, -0.27f), 0f, 0.026f, new Color(1f, 0.85f, 0.3f)), 0.46f);
            // Topper: a dark panel edged in the theme colour, the name in light.
            k.Box(s, "Topper", new Vector3(0f, 2.08f, 0f), new Vector3(0.66f, 0.3f, 0.5f), body, collider: false);
            k.Box(s, "Topper edge", new Vector3(0f, 2.225f, -0.251f), new Vector3(0.66f, 0.02f, 0.01f), glow, collider: false);
            k.Box(s, "Topper edge", new Vector3(0f, 1.935f, -0.251f), new Vector3(0.66f, 0.02f, 0.01f), glow, collider: false);
            TextMesh title = Fit(k.Text(s, d.Name.ToUpperInvariant(), new Vector3(0f, 2.08f, -0.256f), 0f, 0.075f, Color.white), 0.58f);
            title.GetComponent<MeshRenderer>().sharedMaterial = k.P.Sign(Color.Lerp(theme, Color.white, 0.25f));
            // Chrome strips down the front edges (full chrome sides read as white panels at the end of a bank).
            k.Box(s, "Edge trim", new Vector3(-0.33f, 1.1f, -0.27f), new Vector3(0.025f, 2.2f, 0.04f), trim, collider: false);
            k.Box(s, "Edge trim", new Vector3(0.33f, 1.1f, -0.27f), new Vector3(0.025f, 2.2f, 0.04f), trim, collider: false);
            k.Box(s, "Candle", new Vector3(0f, 2.34f, 0f), new Vector3(0.06f, 0.22f, 0.06f), glow, collider: false);
            if (k.Real(s, "metal_stool_02", new Vector3(0f, 0f, -0.75f), 0f, 1f) == null)
                k.Cylinder(s, "Stool", new Vector3(0f, 0.34f, -0.75f), 0.36f, 0.68f, body, collider: true);
        }

        // ---------------------------------------------------------------- roulette

        /// <summary>
        /// Roulette table geometry (local): the long side runs along x, players stand on −z, the croupier on +z. The
        /// wheel sits at +x; the layout runs from the zero (by the wheel) toward −x, three rows of twelve.
        /// </summary>
        public const float RouletteFelt = 0.82f;
        public static readonly Vector3 WheelCentre = new Vector3(1.0f, RouletteFelt, 0.02f);
        private const float CellW = 0.1f, CellH = 0.1f, LayoutX0 = 0.25f;

        /// <summary>Centre of a number's cell (1–36; 0 and 00 get the end cells) on the felt.</summary>
        public static Vector3 CellOf(int n, bool doubleZero)
        {
            if (n == 0) return new Vector3(LayoutX0 + CellW, RouletteFelt + 0.002f, doubleZero ? 0.025f : -0.05f);
            if (n == Roulette.DoubleZeroPocket) return new Vector3(LayoutX0 + CellW, RouletteFelt + 0.002f, -0.125f);
            int col = (n - 1) / 3, row = (n - 1) % 3;
            return new Vector3(LayoutX0 - col * CellW, RouletteFelt + 0.002f, -0.15f + row * CellH);
        }

        /// <summary>Where an outside bet's chips go.</summary>
        public static Vector3 OutsideSpot(RouletteBetKind kind, int which)
        {
            float y = RouletteFelt + 0.002f;
            switch (kind)
            {
                case RouletteBetKind.Dozen: return new Vector3(LayoutX0 - (which - 1) * 4 * CellW - 1.5f * CellW, y, -0.26f);
                case RouletteBetKind.Column: return new Vector3(LayoutX0 - 12 * CellW, y, -0.15f + (which - 1) * CellH);
                default:
                    int slot = kind switch
                    {
                        RouletteBetKind.Low => 0, RouletteBetKind.Even => 1, RouletteBetKind.Red => 2, RouletteBetKind.Black => 3, RouletteBetKind.Odd => 4, _ => 5,
                    };
                    return new Vector3(LayoutX0 - slot * 2 * CellW - 0.5f * CellW, y, -0.36f);
            }
        }

        /// <summary>Where a bet's chips sit: its cell, the line or corner between cells, or its outside box.</summary>
        public static Vector3 BetSpot(RouletteBet b, bool doubleZero)
        {
            switch (b.Kind)
            {
                case RouletteBetKind.Dozen: return OutsideSpot(b.Kind, (b.Numbers[0] - 1) / 12 + 1);
                case RouletteBetKind.Column: return OutsideSpot(b.Kind, (b.Numbers[0] - 1) % 3 + 1);
                case RouletteBetKind.Red:
                case RouletteBetKind.Black:
                case RouletteBetKind.Odd:
                case RouletteBetKind.Even:
                case RouletteBetKind.Low:
                case RouletteBetKind.High:
                    return OutsideSpot(b.Kind, 0);
            }
            Vector3 sum = Vector3.zero;
            foreach (int n in b.Numbers) sum += CellOf(n, doubleZero);
            Vector3 c = sum / b.Numbers.Length;
            // Streets and six lines sit on the players' edge of their rows, as at a real table.
            if (b.Kind == RouletteBetKind.Street || b.Kind == RouletteBetKind.SixLine) c.z = -0.2f;
            return c;
        }

        public static void RouletteTable(Kit k, Transform t, RouletteRules rules, string name)
        {
            Material wood = k.P.Lit(new Color(0.32f, 0.16f, 0.08f), 0.55f);
            Material leather = k.P.Lit(new Color(0.12f, 0.07f, 0.05f), 0.4f);
            Material felt = k.P.Lit(new Color(0.06f, 0.33f, 0.2f), 0.02f);
            Material gold = k.P.Metal(new Color(0.85f, 0.68f, 0.32f), 0.7f);
            k.Box(t, "Table body", new Vector3(0f, 0.4f, 0f), new Vector3(2.9f, 0.8f, 1.3f), wood);
            k.Box(t, "Felt", new Vector3(-0.3f, RouletteFelt - 0.01f, -0.05f), new Vector3(2.2f, 0.02f, 1.1f), felt, collider: false);
            k.Box(t, "Rail", new Vector3(-0.3f, RouletteFelt + 0.02f, -0.63f), new Vector3(2.25f, 0.06f, 0.1f), leather, collider: false);
            k.Box(t, "Rail", new Vector3(-1.43f, RouletteFelt + 0.02f, 0f), new Vector3(0.1f, 0.06f, 1.3f), leather, collider: false);

            // The layout: a painted grid (numbers as text on top), bordered in gold.
            Texture2D layout = LayoutTexture(rules.DoubleZero);
            var m = new Material(k.P.Lit(Color.white, 0.05f)) { name = "Roulette layout", mainTexture = layout };
            // Quad lying on the felt: image top toward +z (the croupier), readable from the players' side.
            GameObject q = k.Quad(t, "Layout", new Vector3(-0.35f, RouletteFelt + 0.001f, -0.14f), new Vector2(1.5f, 0.56f), 0f, m);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Color ink = new Color(0.95f, 0.92f, 0.8f);
            for (int n = 1; n <= 36; n++) FlatText(k, t, n.ToString(), CellOf(n, rules.DoubleZero) + Vector3.up * 0.001f, 0.022f, ink);
            FlatText(k, t, "0", CellOf(0, rules.DoubleZero) + Vector3.up * 0.001f, 0.026f, ink);
            if (rules.DoubleZero) FlatText(k, t, "00", CellOf(Roulette.DoubleZeroPocket, true) + Vector3.up * 0.001f, 0.026f, ink);
            string[] dozens = { "1st 12", "2nd 12", "3rd 12" };
            for (int d = 1; d <= 3; d++)
            {
                FlatText(k, t, dozens[d - 1], OutsideSpot(RouletteBetKind.Dozen, d) + Vector3.up * 0.001f, 0.022f, ink);
                FlatText(k, t, "2:1", OutsideSpot(RouletteBetKind.Column, d) + Vector3.up * 0.001f, 0.018f, ink);
            }
            string[] even = { "1-18", "EVEN", "RED", "BLACK", "ODD", "19-36" };
            RouletteBetKind[] kinds = { RouletteBetKind.Low, RouletteBetKind.Even, RouletteBetKind.Red, RouletteBetKind.Black, RouletteBetKind.Odd, RouletteBetKind.High };
            for (int i = 0; i < 6; i++) FlatText(k, t, even[i], OutsideSpot(kinds[i], 0) + Vector3.up * 0.001f, 0.018f, ink);

            // The wheel's bowl (the turning rotor and ball are live, see RouletteTableView).
            k.Cylinder(t, "Bowl", WheelCentre + new Vector3(0f, 0.04f, 0f), 0.86f, 0.1f, wood);
            k.Cylinder(t, "Bowl track", WheelCentre + new Vector3(0f, 0.092f, 0f), 0.8f, 0.012f, k.P.Lit(new Color(0.18f, 0.09f, 0.04f), 0.8f));
            Transform placard = Kit.Group(t, "Limit placard", new Vector3(-1.2f, RouletteFelt, 0.45f));
            k.Box(placard, "Stand", new Vector3(0f, 0.06f, 0f), new Vector3(0.2f, 0.12f, 0.01f), k.P.Lit(new Color(0.05f, 0.05f, 0.06f), 0.6f), collider: false);
            Fit(k.Text(placard, $"{CasinoMoney.Whole(rules.TableMin)} MIN", new Vector3(0f, 0.08f, -0.007f), 0f, 0.022f, new Color(1f, 0.85f, 0.45f)), 0.18f);
            Fit(k.Text(placard, $"{rules.Name.ToUpperInvariant()} · {name.ToUpperInvariant()}", new Vector3(0f, 0.04f, -0.007f), 0f, 0.012f, new Color(0.9f, 0.9f, 0.9f)), 0.18f);
            k.Box(t, "Gold edge", new Vector3(-0.35f, RouletteFelt + 0.0005f, 0.145f), new Vector3(1.52f, 0.002f, 0.006f), gold, collider: false);
        }

        public static TextMesh FlatText(Kit k, Transform parent, string text, Vector3 at, float height, Color color)
        {
            TextMesh t = k.Text(parent, text, at, 0f, height, color);
            t.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return t;
        }

        /// <summary>The printed layout, 200 px a metre over the 1.5 × 0.56 m quad (see <see cref="RouletteTable"/>).</summary>
        private static Texture2D LayoutTexture(bool doubleZero)
        {
            const int w = 300, h = 112;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Roulette layout", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            Color felt = new Color(0.06f, 0.33f, 0.2f), red = new Color(0.66f, 0.06f, 0.07f), black = new Color(0.06f, 0.06f, 0.07f), line = new Color(0.9f, 0.78f, 0.45f);
            for (int i = 0; i < px.Length; i++) px[i] = felt;
            // The quad spans x −1.1…0.4 (local) and z −0.42…0.14; pixel (0, 0) is its (−x, −z) corner.
            void Fill(float x0, float z0, float x1, float z1, Color c)
            {
                int ax = Mathf.RoundToInt((x0 + 1.1f) * 200f), az = Mathf.RoundToInt((z0 + 0.42f) * 200f);
                int bx = Mathf.RoundToInt((x1 + 1.1f) * 200f), bz = Mathf.RoundToInt((z1 + 0.42f) * 200f);
                for (int y = Mathf.Max(0, az); y < Mathf.Min(h, bz); y++)
                    for (int x = Mathf.Max(0, ax); x < Mathf.Min(w, bx); x++) px[y * w + x] = c;
            }
            void Box(float x0, float z0, float x1, float z1)
            {
                Fill(x0, z0, x1, z0 + 0.004f, line);
                Fill(x0, z1 - 0.004f, x1, z1, line);
                Fill(x0, z0, x0 + 0.004f, z1, line);
                Fill(x1 - 0.004f, z0, x1, z1, line);
            }
            for (int n = 1; n <= 36; n++)
            {
                Vector3 c = CellOf(n, doubleZero);
                Fill(c.x - 0.045f, c.z - 0.045f, c.x + 0.045f, c.z + 0.045f, Roulette.IsRed(n) ? red : black);
                Box(c.x - 0.05f, c.z - 0.05f, c.x + 0.05f, c.z + 0.05f);
            }
            Vector3 zc = CellOf(0, doubleZero);
            if (doubleZero)
            {
                Box(zc.x - 0.05f, -0.05f, zc.x + 0.05f, 0.1f);
                Box(zc.x - 0.05f, -0.2f, zc.x + 0.05f, -0.05f);
            }
            else Box(zc.x - 0.05f, -0.2f, zc.x + 0.05f, 0.1f);
            for (int d = 1; d <= 3; d++)
            {
                Vector3 c = OutsideSpot(RouletteBetKind.Dozen, d);
                Box(c.x - 0.2f, c.z - 0.05f, c.x + 0.2f, c.z + 0.05f);
                Vector3 col = OutsideSpot(RouletteBetKind.Column, d);
                Box(col.x - 0.05f, col.z - 0.05f, col.x + 0.05f, col.z + 0.05f);
            }
            RouletteBetKind[] kinds = { RouletteBetKind.Low, RouletteBetKind.Even, RouletteBetKind.Red, RouletteBetKind.Black, RouletteBetKind.Odd, RouletteBetKind.High };
            foreach (RouletteBetKind kind in kinds)
            {
                Vector3 c = OutsideSpot(kind, 0);
                if (kind == RouletteBetKind.Red) Fill(c.x - 0.06f, c.z - 0.035f, c.x + 0.06f, c.z + 0.035f, red);
                if (kind == RouletteBetKind.Black) Fill(c.x - 0.06f, c.z - 0.035f, c.x + 0.06f, c.z + 0.035f, black);
                Box(c.x - 0.1f, c.z - 0.05f, c.x + 0.1f, c.z + 0.05f);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>A flat disc with radial UVs (the texture's centre at the disc's centre), facing up.</summary>
        public static Mesh Disc(float radius, int segments = 48)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                // Angle clockwise from +z seen from above, the way the wheel texture and the numbers are laid out.
                float a = i * Mathf.PI * 2f / segments;
                verts.Add(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius);
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Sin(a), 0.5f + 0.5f * Mathf.Cos(a)));
                if (i > 0) tris.AddRange(new[] { 0, i, i + 1 });
            }
            var mesh = new Mesh { name = "Disc" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- poker

        /// <summary>Oval poker table: semi-axes along x and z (local), the dealer at +z.</summary>
        public const float PokerA = 1.25f, PokerB = 0.68f, PokerFelt = 0.78f;

        /// <summary>Seat angles (degrees from +x toward +z), clockwise round the table from the player's seat at the near side.</summary>
        public static readonly float[] PokerSeatAngles = { 270f, 218f, 166f, 124f, 56f, 322f };

        public static Vector3 PokerOnEllipse(float degrees, float scale, float y)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * PokerA * scale, y, Mathf.Sin(a) * PokerB * scale);
        }

        public static void PokerTable(Kit k, Transform t, string name, string stakes)
        {
            const int segments = 40;
            Material wood = k.P.Lit(new Color(0.3f, 0.15f, 0.07f), 0.55f);
            Material leather = k.P.Lit(new Color(0.1f, 0.06f, 0.05f), 0.4f);
            Material cloth = k.P.Lit(new Color(0.12f, 0.24f, 0.42f), 0.02f);
            List<Vector3> Ring(float scale, float y)
            {
                var pts = new List<Vector3> { new Vector3(0f, y, 0f) };
                for (int i = 0; i <= segments; i++) pts.Add(PokerOnEllipse(i * 360f / segments, scale, y)); // counter-clockwise from above
                return pts;
            }
            var apron = new MeshBuilder();
            apron.Slab(Ring(1.12f, PokerFelt - 0.012f), 0.1f);
            apron.Build(t, "Table apron", wood, false);
            var felt = new MeshBuilder();
            felt.Slab(Ring(1f, PokerFelt), 0.012f);
            felt.Build(t, "Felt", cloth, false);
            var rail = new MeshBuilder();
            const float top = PokerFelt + 0.05f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 360f / segments, a1 = (i + 1) * 360f / segments;
                Vector3 in0 = PokerOnEllipse(a0, 1f, top), in1 = PokerOnEllipse(a1, 1f, top);
                Vector3 out0 = PokerOnEllipse(a0, 1.12f, top), out1 = PokerOnEllipse(a1, 1.12f, top);
                rail.Quad(out0, out1, in1, in0);
                rail.Wall(out0 + Vector3.down * 0.07f, out1 + Vector3.down * 0.07f, 0.07f);
                rail.Wall(in1 + Vector3.down * 0.05f, in0 + Vector3.down * 0.05f, 0.05f);
            }
            rail.Build(t, "Padded rail", leather, false);
            k.Cylinder(t, "Pedestal", new Vector3(0f, 0.36f, 0f), 0.6f, 0.7f, wood);
            var solid = new GameObject("Table collider");
            solid.transform.SetParent(t, false);
            var box = solid.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, PokerFelt / 2f, 0f);
            box.size = new Vector3(PokerA * 2.1f, PokerFelt + 0.06f, PokerB * 2.1f);
            // The dealer's chip rack and the room's name printed on the felt.
            k.Box(t, "Chip rack", new Vector3(0f, PokerFelt + 0.02f, PokerB * 0.8f), new Vector3(0.5f, 0.04f, 0.1f), k.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.6f), collider: false);
            Material print = k.P.Lit(new Color(0.75f, 0.8f, 0.9f), 0.05f);
            FeltText(k, t, "THE MERIDIAN POKER ROOM", new Vector3(0f, PokerFelt + 0.0015f, 0.18f), 0.03f, print);
            FeltText(k, t, stakes, new Vector3(0f, PokerFelt + 0.0015f, 0.1f), 0.022f, print);
            foreach (float angle in PokerSeatAngles)
            {
                Vector3 at = PokerOnEllipse(angle, 1.55f, 0f);
                float yaw = Mathf.Atan2(-at.x, -at.z) * Mathf.Rad2Deg; // facing the table centre
                if (k.Real(t, "dining_chair_02", at, yaw + 180f) == null)
                    k.Box(t, "Chair", at + Vector3.up * 0.23f, new Vector3(0.45f, 0.46f, 0.45f), leather);
            }
        }

        // ---------------------------------------------------------------- carpet

        /// <summary>
        /// A classic casino carpet tile: deep burgundy, a gold lattice, teal and gold medallions, busy enough to hide
        /// wear (and to keep eyes up on the games). 128 px, point-sampled so the pattern stays crisp.
        /// </summary>
        public static Texture2D Carpet()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Meridian carpet", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var px = new Color[n * n];
            Color ground = new Color(0.32f, 0.05f, 0.08f), lattice = new Color(0.78f, 0.58f, 0.22f), teal = new Color(0.05f, 0.34f, 0.36f), dark = new Color(0.16f, 0.02f, 0.05f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Diamond lattice: lines where |dx| + |dy| from the tile centre is a multiple of the quarter tile.
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float du = Mathf.Abs(u - 0.5f), dv = Mathf.Abs(v - 0.5f);
                    float diamond = du + dv;
                    Color c = ground;
                    if (Mathf.Abs(diamond - 0.5f) < 0.018f || Mathf.Abs(diamond - 0.25f) < 0.01f) c = lattice;
                    // Medallions: a teal disc with a gold dot in the middle and at the corners.
                    float centre = Mathf.Sqrt(du * du + dv * dv);
                    if (centre < 0.11f) c = teal;
                    if (centre < 0.045f) c = lattice;
                    float corner = Mathf.Sqrt((0.5f - du) * (0.5f - du) + (0.5f - dv) * (0.5f - dv));
                    if (corner < 0.07f) c = teal;
                    if (corner < 0.028f) c = lattice;
                    // Small scattered flecks in the ground, so it isn't flat.
                    if (c == ground && ((x * 7 + y * 13) % 29 == 0)) c = dark;
                    px[y * n + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
