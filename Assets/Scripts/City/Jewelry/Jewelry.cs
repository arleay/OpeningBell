using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum JewelrySlot { Watch, Bracelet, Necklace, Ring, Earrings, Glasses }

    public enum Metal { YellowGold, WhiteGold, RoseGold, Silver, Steel, Platinum, Titanium, Black }

    /// <summary>What a piece looks like; each kind has its own builder in <see cref="Jewelry"/>.</summary>
    public enum JewelryKind
    {
        DiverWatch, DressWatch, Chronograph, IcedWatch, DigitalWatch,
        CubanChain, RopeChain, SolitairePendant, DropPendant, Pearls,
        TennisBracelet, Bangle, CubanBracelet, Beads,
        Solitaire, Signet, Halo, Band,
        Studs, Hoops,
        Aviators, RoundFrames, SquareFrames, Rimless,
    }

    public sealed class JewelryItem
    {
        public string Id, Name, Blurb;
        public JewelrySlot Slot;
        public JewelryKind Kind;
        public decimal Price;
        public Metal Metal;
        /// <summary>The stone (or dial, lens, bead) colour.</summary>
        public Color Accent;
        /// <summary>Second colour: strap, frame tint.</summary>
        public Color Trim;
    }

    /// <summary>
    /// The jeweller's stock and how each piece is made. Everything is modelled to the body part it's made for, from a
    /// <see cref="Fit"/> measured on the wearer (see <see cref="Adornments"/>), in a local frame per slot:
    /// <list type="bullet">
    /// <item>wrist/finger: +x along the arm toward the hand, +y the back of the wrist (dial side), radius = the limb's;</item>
    /// <item>neck: origin at the base of the neck, +y up the neck, +z forward; the chain drops onto the chest;</item>
    /// <item>face: origin between the eyes on the face's surface, +x the wearer's right, +y up, +z out of the face;</item>
    /// <item>ear: origin at the lobe, +y up, +x outward.</item>
    /// </list>
    /// Sizes are real proportions of the part they sit on (a 40 mm watch on a 28 mm-radius wrist: case = 1.43 r), so
    /// they look right on the big-headed Tiny bodies as well as anything else.
    /// </summary>
    public static class Jewelry
    {
        public static readonly IReadOnlyList<JewelryItem> Catalog = new List<JewelryItem>
        {
            // Watches (left wrist).
            W("watch_diver", "Tidemark 300 Diver", JewelryKind.DiverWatch, 4800m, Metal.Steel, new Color(0.04f, 0.05f, 0.07f), new Color(0.1f, 0.2f, 0.45f), "Steel, 300 m, ceramic dive bezel. Automatic."),
            W("watch_dress", "Aurelian Dress", JewelryKind.DressWatch, 12500m, Metal.YellowGold, new Color(0.93f, 0.9f, 0.82f), new Color(0.3f, 0.17f, 0.08f), "18k gold on brown alligator. Two hands, nothing else."),
            W("watch_chrono", "Meridian Chronograph", JewelryKind.Chronograph, 7900m, Metal.Steel, new Color(0.08f, 0.16f, 0.38f), new Color(0.72f, 0.73f, 0.75f), "Blue sunburst dial, three sub-dials, steel bracelet."),
            W("watch_iced", "Kell Royale Iced", JewelryKind.IcedWatch, 38000m, Metal.YellowGold, new Color(0.9f, 0.93f, 1f), new Color(0.95f, 0.78f, 0.35f), "Full gold, diamond bezel and dial. Subtle it is not."),
            W("watch_digital", "Pulse Digital", JewelryKind.DigitalWatch, 65m, Metal.Black, new Color(0.62f, 0.68f, 0.56f), new Color(0.06f, 0.06f, 0.07f), "Resin, backlight, alarm. Tells the time perfectly."),
            // Necklaces.
            N("chain_cuban", "Cuban Link Chain", JewelryKind.CubanChain, 6200m, Metal.YellowGold, Color.clear, "10 mm 14k gold Miami Cuban, box clasp."),
            N("chain_rope", "Rope Chain", JewelryKind.RopeChain, 450m, Metal.Silver, Color.clear, "Sterling silver, 3 mm."),
            N("pendant_diamond", "Diamond Solitaire Pendant", JewelryKind.SolitairePendant, 9800m, Metal.Platinum, new Color(0.92f, 0.95f, 1f), "One carat on a fine platinum chain."),
            N("pendant_emerald", "Emerald Drop", JewelryKind.DropPendant, 5400m, Metal.YellowGold, new Color(0.05f, 0.62f, 0.3f), "Pear-cut emerald, gold bail and chain."),
            N("pearls", "Pearl Strand", JewelryKind.Pearls, 2900m, Metal.WhiteGold, new Color(0.96f, 0.94f, 0.9f), "Akoya pearls, hand-knotted."),
            // Bracelets (right wrist).
            B("bracelet_tennis", "Diamond Tennis Bracelet", JewelryKind.TennisBracelet, 8500m, Metal.WhiteGold, new Color(0.92f, 0.95f, 1f), "Five carats, one row, four-prong settings."),
            B("bangle_gold", "Gold Bangle", JewelryKind.Bangle, 1900m, Metal.YellowGold, Color.clear, "Solid 18k, polished."),
            B("bracelet_cuban", "Cuban Link Bracelet", JewelryKind.CubanBracelet, 3100m, Metal.YellowGold, Color.clear, "Matches the chain."),
            B("bracelet_beads", "Onyx Bead Bracelet", JewelryKind.Beads, 120m, Metal.Silver, new Color(0.05f, 0.05f, 0.06f), "Polished onyx on a stretch cord."),
            // Rings (right hand).
            R("ring_solitaire", "Solitaire Ring", JewelryKind.Solitaire, 7200m, Metal.Platinum, new Color(0.92f, 0.95f, 1f), "A carat and a half, six prongs."),
            R("ring_signet", "Gold Signet", JewelryKind.Signet, 1400m, Metal.YellowGold, new Color(0.08f, 0.08f, 0.1f), "Heavy 14k with an onyx face."),
            R("ring_ruby", "Ruby Halo Ring", JewelryKind.Halo, 4600m, Metal.RoseGold, new Color(0.8f, 0.04f, 0.12f), "Oval ruby in a ring of diamonds."),
            R("ring_band", "Titanium Band", JewelryKind.Band, 220m, Metal.Titanium, Color.clear, "Brushed, comfort fit."),
            // Earrings.
            E("studs_diamond", "Diamond Studs", JewelryKind.Studs, 3300m, Metal.WhiteGold, new Color(0.92f, 0.95f, 1f), "Half a carat each."),
            E("studs_sapphire", "Sapphire Studs", JewelryKind.Studs, 2100m, Metal.WhiteGold, new Color(0.08f, 0.2f, 0.75f), "Ceylon sapphires, white gold."),
            E("hoops_gold", "Gold Hoops", JewelryKind.Hoops, 780m, Metal.YellowGold, Color.clear, "Medium, 14k."),
            // Glasses.
            G("glasses_aviator", "Gold Aviators", JewelryKind.Aviators, 260m, Metal.YellowGold, new Color(0.18f, 0.3f, 0.2f, 0.72f), new Color(0.9f, 0.75f, 0.4f), "Green G-15 lenses, double bridge."),
            G("glasses_round", "Tortoise Rounds", JewelryKind.RoundFrames, 340m, Metal.Black, new Color(0.85f, 0.9f, 0.95f, 0.18f), new Color(0.35f, 0.18f, 0.08f), "Acetate, clear lenses. Reads as clever."),
            G("glasses_square", "Midnight Squares", JewelryKind.SquareFrames, 180m, Metal.Black, new Color(0.06f, 0.06f, 0.08f, 0.85f), new Color(0.04f, 0.04f, 0.05f), "Black acetate, dark lenses."),
            G("glasses_rimless", "Rimless Readers", JewelryKind.Rimless, 420m, Metal.WhiteGold, new Color(0.85f, 0.9f, 0.95f, 0.15f), new Color(0.8f, 0.8f, 0.82f), "Titanium hinges, anti-glare."),
        };

        private static JewelryItem W(string id, string name, JewelryKind k, decimal p, Metal m, Color dial, Color strap, string blurb) =>
            new JewelryItem { Id = id, Name = name, Kind = k, Price = p, Metal = m, Accent = dial, Trim = strap, Slot = JewelrySlot.Watch, Blurb = blurb };
        private static JewelryItem N(string id, string name, JewelryKind k, decimal p, Metal m, Color stone, string blurb) =>
            new JewelryItem { Id = id, Name = name, Kind = k, Price = p, Metal = m, Accent = stone, Slot = JewelrySlot.Necklace, Blurb = blurb };
        private static JewelryItem B(string id, string name, JewelryKind k, decimal p, Metal m, Color stone, string blurb) =>
            new JewelryItem { Id = id, Name = name, Kind = k, Price = p, Metal = m, Accent = stone, Slot = JewelrySlot.Bracelet, Blurb = blurb };
        private static JewelryItem R(string id, string name, JewelryKind k, decimal p, Metal m, Color stone, string blurb) =>
            new JewelryItem { Id = id, Name = name, Kind = k, Price = p, Metal = m, Accent = stone, Slot = JewelrySlot.Ring, Blurb = blurb };
        private static JewelryItem E(string id, string name, JewelryKind k, decimal p, Metal m, Color stone, string blurb) =>
            new JewelryItem { Id = id, Name = name, Kind = k, Price = p, Metal = m, Accent = stone, Slot = JewelrySlot.Earrings, Blurb = blurb };
        private static JewelryItem G(string id, string name, JewelryKind k, decimal p, Metal m, Color lens, Color frame, string blurb) =>
            new JewelryItem { Id = id, Name = name, Kind = k, Price = p, Metal = m, Accent = lens, Trim = frame, Slot = JewelrySlot.Glasses, Blurb = blurb };

        public static JewelryItem Get(string id)
        {
            foreach (JewelryItem i in Catalog) if (i.Id == id) return i;
            return null;
        }

        public static string SlotName(JewelrySlot s) => s switch
        {
            JewelrySlot.Watch => "watch",
            JewelrySlot.Bracelet => "bracelet",
            JewelrySlot.Necklace => "necklace",
            JewelrySlot.Ring => "ring",
            JewelrySlot.Earrings => "earrings",
            _ => "glasses",
        };

        public static Color MetalColor(Metal m) => m switch
        {
            // Linear-ish reflectance colours of real metals (gold ~ (1.0, 0.77, 0.34)), slightly softened.
            Metal.YellowGold => new Color(1f, 0.78f, 0.36f),
            Metal.WhiteGold => new Color(0.86f, 0.85f, 0.82f),
            Metal.RoseGold => new Color(0.95f, 0.66f, 0.55f),
            Metal.Silver => new Color(0.93f, 0.93f, 0.92f),
            Metal.Steel => new Color(0.72f, 0.73f, 0.75f),
            Metal.Platinum => new Color(0.84f, 0.84f, 0.86f),
            Metal.Titanium => new Color(0.55f, 0.55f, 0.57f),
            _ => new Color(0.06f, 0.06f, 0.07f),
        };

        public static Material MetalOf(Palette p, Metal m) =>
            m == Metal.Black ? p.Lit(MetalColor(m), 0.55f) : p.Metal(MetalColor(m), m == Metal.Titanium || m == Metal.Steel ? 0.8f : 0.9f);

        /// <summary>
        /// Builds <paramref name="item"/> under <paramref name="parent"/> in the slot's local frame, sized to
        /// <paramref name="size"/>: the limb radius for wrist and finger pieces, the neck radius for necklaces
        /// (with <paramref name="drop"/> = how far down the chest the front of the chain falls), and for the face the
        /// eye spacing (glasses) or lobe size (earrings).
        /// </summary>
        public static GameObject Build(CityContext c, Transform parent, JewelryItem item, float size, float drop = 0f, float depth = 0f, List<Vector3> path = null)
        {
            Palette p = c.P;
            Material metal = MetalOf(p, item.Metal);
            Material gem = p.Gem(item.Accent);
            switch (item.Slot)
            {
                case JewelrySlot.Watch: return Watch(c, parent, item, size, metal);
                case JewelrySlot.Bracelet: return Bracelet(p, parent, item, size, metal, gem);
                case JewelrySlot.Ring: return Ring(p, parent, item, size, metal, gem);
                case JewelrySlot.Necklace: return Necklace(p, parent, item, size, drop, depth, metal, gem, path);
                case JewelrySlot.Earrings: return Earring(p, parent, item, size, metal, gem);
                default: return Glasses(p, parent, item, size, depth, metal);
            }
        }

        // ---------------------------------------------------------------- watches

        private static GameObject Watch(CityContext c, Transform parent, JewelryItem item, float r, Material metal)
        {
            Palette p = c.P;
            bool digital = item.Kind == JewelryKind.DigitalWatch;
            // Case radius from the wrist: a 40 mm watch on a 28 mm-radius wrist. Sits on top of the wrist (+y).
            float cr = r * (digital ? 0.78f : item.Kind == JewelryKind.DressWatch ? 0.66f : 0.72f);
            float ch = cr * (digital ? 0.42f : item.Kind == JewelryKind.DressWatch ? 0.24f : 0.34f); // case height
            Vector3 top = Vector3.up * (r * 1.02f);
            Material strapM = item.Kind == JewelryKind.DressWatch || digital ? p.Lit(item.Trim, digital ? 0.35f : 0.45f) : metal;
            var f = new Forge(5); // 0 metal, 1 strap, 2 dial (textured), 3 crystal, 4 diamonds
            // Case: a lathe profile from the caseback up to the bezel, rounded at the shoulders.
            f.Use(0).Lathe(top, Vector3.up, new[]
            {
                new Vector2(0f, 0f), new Vector2(cr * 0.9f, 0f), new Vector2(cr, ch * 0.2f), new Vector2(cr, ch * 0.62f),
                new Vector2(cr * 0.97f, ch * 0.72f),
            }, 40);
            // Bezel: raised ring, knurled on the diver (coin edge), gem-set on the iced one.
            float bezIn = cr * (item.Kind == JewelryKind.DiverWatch || item.Kind == JewelryKind.IcedWatch ? 0.8f : 0.88f);
            f.Lathe(top, Vector3.up, new[]
            {
                new Vector2(cr * 0.97f, ch * 0.72f), new Vector2(cr * 0.98f, ch * 0.86f), new Vector2(cr * 0.93f, ch * 0.98f),
                new Vector2(bezIn, ch), new Vector2(bezIn, ch * 0.9f),
            }, 40);
            if (item.Kind == JewelryKind.DiverWatch)
                f.Use(1).Lathe(top + Vector3.up * (ch * 0.99f), Vector3.up, new[] { new Vector2(cr * 0.95f, 0f), new Vector2(cr * 0.82f, 0f) }, 40); // ceramic insert
            if (item.Kind == JewelryKind.IcedWatch)
            {
                // Pavé bezel: a ring of small brilliants.
                int n = 24;
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    f.Use(4).Gem(top + new Vector3(Mathf.Cos(a) * cr * 0.88f, ch * 1.01f, Mathf.Sin(a) * cr * 0.88f), Vector3.up, cr * 0.2f, 8);
                }
            }
            // Dial frame: 3 o'clock toward the hand (+x), 12 o'clock at +z. The crown sits at 3, the lugs (and the
            // strap) at 12 and 6.
            f.Use(0).Lathe(top + new Vector3(cr, ch * 0.4f, 0f), Vector3.right, new[]
            {
                new Vector2(0f, 0f), new Vector2(cr * 0.16f, 0f), new Vector2(cr * 0.16f, cr * 0.16f), new Vector2(cr * 0.14f, cr * 0.2f), new Vector2(0f, cr * 0.2f),
            }, 14);
            if (item.Kind == JewelryKind.Chronograph)
                foreach (float s in new[] { -1f, 1f })
                    f.Block(top + new Vector3(cr * 0.9f, ch * 0.4f, s * cr * 0.55f), Vector3.right, Vector3.up, new Vector3(cr * 0.22f, cr * 0.12f, cr * 0.12f));
            foreach (float s in new[] { -1f, 1f })
            foreach (float side in new[] { -1f, 1f })
                f.Block(top + new Vector3(side * cr * 0.45f, ch * 0.3f, s * cr * 0.95f), Vector3.right, Vector3.up, new Vector3(cr * 0.18f, ch * 0.45f, cr * 0.4f));
            // Strap/bracelet: round the wrist, as wide as the lugs are apart.
            float strapW = cr * 1.1f;
            var loop = Forge.Circle(Vector3.zero, Vector3.right, r * 1.04f, 48);
            if (strapM == metal)
            {
                // Three-row link bracelet: a centre row of polished links between brushed outer rows.
                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 at = loop[i], outward = at.normalized, along = Vector3.Cross(Vector3.right, outward);
                    if (Vector3.Dot(outward, Vector3.up) > 0.8f) continue; // the case covers the top
                    f.Use(0).Block(at, Vector3.right, outward, new Vector3(strapW * 0.3f, r * 0.14f, r * 0.12f));
                    foreach (float s in new[] { -1f, 1f })
                        f.Block(at + Vector3.right * (s * strapW * 0.33f), Vector3.right, outward, new Vector3(strapW * 0.3f, r * 0.12f, r * 0.13f));
                }
            }
            else
            {
                f.Use(1).Sweep(loop, r * 0.06f, strapW / 2f, true, 12, Vector3.right); // thin radially, strap-wide along the arm
                // Buckle under the wrist.
                f.Use(0).Torus(Vector3.down * (r * 1.1f), Vector3.up, strapW * 0.5f, r * 0.03f, 16, 6, strapW * 0.35f);
            }
            // Dial and crystal.
            float dialR = bezIn * 0.98f;
            f.Use(2).Disc(top + Vector3.up * (ch * 0.9f), Vector3.up, dialR, 40, -1f, Vector3.right);
            f.Use(3).Disc(top + Vector3.up * (ch * 0.98f), Vector3.up, dialR, 40);
            Material dialM = p.Textured("Dial " + item.Id, WatchDial.Face(item), Color.white, digital ? 0.2f : 0.6f);
            Material crystal = p.Glass(new Color(0.85f, 0.9f, 1f, 0.12f));
            GameObject go = f.Build(parent, item.Name, metal, strapM, dialM, crystal, p.Gem(new Color(0.92f, 0.95f, 1f), 0.5f));
            var face = go.AddComponent<WatchFace>();
            face.Configure(c, item, top + Vector3.up * (ch * 0.93f), dialR, p.Lit(item.Kind == JewelryKind.DiverWatch || item.Kind == JewelryKind.Chronograph ? new Color(0.92f, 0.92f, 0.9f) : MetalColor(item.Metal), 0.8f), dialM);
            return go;
        }

        // ---------------------------------------------------------------- bracelets and rings

        private static GameObject Bracelet(Palette p, Transform parent, JewelryItem item, float r, Material metal, Material gem)
        {
            var f = new Forge(2);
            float R = r * 1.08f;
            switch (item.Kind)
            {
                case JewelryKind.TennisBracelet:
                {
                    int n = Mathf.Max(20, Mathf.RoundToInt(R * 2f * Mathf.PI / (r * 0.24f)));
                    var ring = Forge.Circle(Vector3.zero, Vector3.right, R, n);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 outward = ring[i].normalized;
                        f.Use(0).Lathe(ring[i] - outward * (r * 0.03f), outward, new[] { new Vector2(r * 0.1f, 0f), new Vector2(r * 0.11f, r * 0.05f), new Vector2(r * 0.09f, r * 0.07f) }, 12);
                        f.Use(1).Gem(ring[i] + outward * (r * 0.05f), outward, r * 0.17f, 12);
                    }
                    break;
                }
                case JewelryKind.Bangle:
                    f.Use(0).Sweep(Forge.Circle(Vector3.zero, Vector3.right, R * 1.06f, 64), r * 0.07f, r * 0.1f, true, 16, Vector3.right);
                    break;
                case JewelryKind.CubanBracelet:
                    CubanLinks(f, Forge.Circle(Vector3.zero, Vector3.right, R, 64), r * 0.2f, Vector3.right, true);
                    break;
                default: // beads on a cord
                {
                    int n = Mathf.Max(14, Mathf.RoundToInt(R * 2f * Mathf.PI / (r * 0.3f)));
                    var ring = Forge.Circle(Vector3.zero, Vector3.right, R * 1.02f, n);
                    for (int i = 0; i < n; i++)
                        f.Use(i == 0 ? 0 : 1).Lathe(ring[i], Vector3.right, Sphere(r * (i == 0 ? 0.13f : 0.15f), 8), 12);
                    gem = p.Lit(item.Accent, 0.92f);
                    break;
                }
            }
            return f.Build(parent, item.Name, metal, gem);
        }

        private static GameObject Ring(Palette p, Transform parent, JewelryItem item, float r, Material metal, Material gem)
        {
            // A notional finger on the back of the fist: a band of radius r (the finger's), its top on the knuckle line.
            var f = new Forge(3); // 0 metal, 1 centre stone, 2 accent diamonds
            float wire = r * (item.Kind == JewelryKind.Band || item.Kind == JewelryKind.Signet ? 0.28f : 0.18f);
            f.Use(0).Sweep(Forge.Circle(Vector3.zero, Vector3.right, r, 40), wire, r * 0.21f, true, 12, Vector3.right);
            Vector3 top = Vector3.up * (r + wire);
            switch (item.Kind)
            {
                case JewelryKind.Solitaire:
                    // Six prongs holding a brilliant well clear of the band.
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * Mathf.PI * 2f;
                        Vector3 tip = top + new Vector3(Mathf.Cos(a) * r * 0.42f, r * 0.62f, Mathf.Sin(a) * r * 0.42f);
                        f.Use(0).Sweep(new List<Vector3> { top + new Vector3(Mathf.Cos(a) * r * 0.2f, 0f, Mathf.Sin(a) * r * 0.2f), tip }, r * 0.05f, r * 0.05f, false, 6);
                    }
                    f.Use(1).Gem(top + Vector3.up * (r * 0.5f), Vector3.up, r * 1.0f, 16);
                    break;
                case JewelryKind.Signet:
                    f.Use(0).Lathe(top - Vector3.up * (r * 0.1f), Vector3.up, new[] { new Vector2(r * 0.5f, 0f), new Vector2(r * 0.55f, r * 0.2f), new Vector2(r * 0.5f, r * 0.26f), new Vector2(0f, r * 0.26f) }, 24, 1f, 0.8f);
                    f.Use(1).Disc(top + Vector3.up * (r * 0.165f), Vector3.up, r * 0.42f, 24, r * 0.34f);
                    gem = p.Lit(item.Accent, 0.95f);
                    break;
                case JewelryKind.Halo:
                    f.Use(1).Gem(top + Vector3.up * (r * 0.32f), Vector3.up, r * 0.8f, 16);
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i / 14f * Mathf.PI * 2f;
                        f.Use(2).Gem(top + new Vector3(Mathf.Cos(a) * r * 0.58f, r * 0.26f, Mathf.Sin(a) * r * 0.5f), Vector3.up, r * 0.18f, 8);
                    }
                    f.Use(0).Torus(top + Vector3.up * (r * 0.2f), Vector3.up, r * 0.58f, r * 0.07f, 28, 6, r * 0.5f);
                    break;
            }
            return f.Build(parent, item.Name, metal, gem, p.Gem(new Color(0.92f, 0.95f, 1f), 0.5f));
        }

        // ---------------------------------------------------------------- necklaces

        /// <summary>
        /// The path a chain takes: round the back of the neck at <paramref name="r"/>, then down over the collarbones to
        /// hang <paramref name="drop"/> below at the front, resting on a chest <paramref name="depth"/> forward of the neck
        /// axis. Returned as a closed loop, front-most point at index 0.
        /// </summary>
        private static List<Vector3> ChainPath(float r, float drop, float depth, int points)
        {
            var path = new List<Vector3>(points);
            for (int i = 0; i < points; i++)
            {
                float a = i / (float)points * Mathf.PI * 2f; // 0 at the front
                float front = (Mathf.Cos(a) + 1f) / 2f;       // 1 at the front, 0 at the back
                float sag = front * front;                    // falls away steeply only toward the front
                float x = Mathf.Sin(a) * r * (1f + 0.45f * sag);
                float z = Mathf.Cos(a) * r;
                // Out over the chest as it falls: the front sits on the chest surface, not inside it.
                z = Mathf.Lerp(z, Mathf.Max(z, depth * (0.85f + 0.15f * sag)), sag);
                path.Add(new Vector3(x, -drop * sag, z));
            }
            return path;
        }

        /// <param name="path">The chain's closed loop in the holder's frame, front-most point first (worn: fitted to the
        /// body by <see cref="Adornments"/>); null for the idealised drape round a display bust.</param>
        private static GameObject Necklace(Palette p, Transform parent, JewelryItem item, float r, float drop, float depth, Material metal, Material gem, List<Vector3> path)
        {
            var f = new Forge(2);
            float R = r * 1.12f;
            path ??= ChainPath(R, drop, Mathf.Max(depth, R) + r * 0.05f, 96);
            int count = path.Count;
            Vector3 low = path[0];
            switch (item.Kind)
            {
                case JewelryKind.CubanChain:
                    CubanLinks(f, path, r * 0.13f, Vector3.up, true);
                    // Box clasp at the back.
                    f.Use(0).Block(path[count / 2], Vector3.right, Vector3.up, new Vector3(r * 0.22f, r * 0.1f, r * 0.08f));
                    break;
                case JewelryKind.RopeChain:
                    // Two strands twisted round each other.
                    for (int strand = 0; strand < 2; strand++)
                    {
                        var twist = new List<Vector3>(path.Count * 3);
                        for (int i = 0; i < path.Count * 3; i++)
                        {
                            float t = i / 3f;
                            int i0 = Mathf.FloorToInt(t) % path.Count, i1 = (i0 + 1) % path.Count;
                            Vector3 at = Vector3.Lerp(path[i0], path[i1], t - Mathf.Floor(t));
                            Vector3 tangent = (path[i1] - path[i0]).normalized;
                            Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized, up = Vector3.Cross(side, tangent);
                            float a = i * 0.9f + strand * Mathf.PI;
                            twist.Add(at + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * (r * 0.025f));
                        }
                        f.Use(0).Sweep(twist, r * 0.028f, r * 0.028f, true, 6);
                    }
                    break;
                case JewelryKind.Pearls:
                {
                    // Pearls strung round the loop, graduated larger toward the front, a gold clasp at the back.
                    float along = 0f;
                    var marks = new List<(Vector3, float)>();
                    for (int i = 0; i < count; i++) along += Vector3.Distance(path[i], path[(i + 1) % count]);
                    float step = along / 44f;
                    float walked = 0f, next = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 a = path[i], b = path[(i + 1) % count];
                        float len = Vector3.Distance(a, b);
                        while (next <= walked + len)
                        {
                            Vector3 at = Vector3.Lerp(a, b, (next - walked) / Mathf.Max(0.0001f, len));
                            float front = Mathf.Clamp01((at.z / Mathf.Max(0.001f, R) + 1f) / 2f);
                            marks.Add((at, r * (0.075f + 0.035f * front)));
                            next += step;
                        }
                        walked += len;
                    }
                    for (int i = 0; i < marks.Count; i++) f.Use(1).Lathe(marks[i].Item1, Vector3.up, Sphere(marks[i].Item2, 8), 12);
                    f.Use(0).Block(path[count / 2], Vector3.right, Vector3.up, new Vector3(r * 0.12f, r * 0.07f, r * 0.06f));
                    gem = p.Lit(item.Accent, 0.9f);
                    break;
                }
                default: // fine chain + pendant
                {
                    f.Use(0).Sweep(path, r * 0.012f, r * 0.012f, true, 5);
                    // Tiny links catching the light along it: every other point, a ring across the path.
                    for (int i = 0; i < count; i += 2)
                    {
                        Vector3 tangent = (path[(i + 1) % count] - path[i]).normalized;
                        f.Torus(path[i], tangent, r * 0.022f, r * 0.007f, 8, 4);
                    }
                    // Bail and stone, hanging from the lowest point.
                    Vector3 hang = low + Vector3.down * (r * 0.05f);
                    f.Use(0).Torus(low + Vector3.down * (r * 0.02f), Vector3.right, r * 0.04f, r * 0.012f, 12, 5);
                    if (item.Kind == JewelryKind.SolitairePendant)
                    {
                        Vector3 stone = hang + Vector3.down * (r * 0.12f) + Vector3.forward * (r * 0.03f);
                        for (int i = 0; i < 4; i++)
                        {
                            float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                            f.Use(0).Sweep(new List<Vector3> { stone + new Vector3(Mathf.Cos(a) * r * 0.08f, Mathf.Sin(a) * r * 0.08f, -r * 0.03f), stone + new Vector3(Mathf.Cos(a) * r * 0.11f, Mathf.Sin(a) * r * 0.11f, r * 0.02f) }, r * 0.012f, r * 0.012f, false, 5);
                        }
                        f.Use(1).Gem(stone, Vector3.forward, r * 0.22f, 16);
                    }
                    else
                    {
                        // Pear drop: a gem stretched downward in a gold cup.
                        Vector3 stone = hang + Vector3.down * (r * 0.16f) + Vector3.forward * (r * 0.03f);
                        f.Use(0).Torus(stone, Vector3.forward, r * 0.1f, r * 0.018f, 20, 5, r * 0.14f);
                        f.Use(1).Lathe(stone, Vector3.forward, new[] { new Vector2(0f, -r * 0.04f), new Vector2(r * 0.085f, 0f), new Vector2(r * 0.06f, r * 0.03f), new Vector2(0f, r * 0.035f) }, 10, 1f, 1.45f);
                    }
                    break;
                }
            }
            return f.Build(parent, item.Name, metal, gem);
        }

        /// <summary>
        /// Miami Cuban links along a path: thick oval links, each turned 90° from the last and flattened so they lie
        /// flat against the skin (<paramref name="normal"/> is the direction away from the body's centre line).
        /// </summary>
        private static void CubanLinks(Forge f, List<Vector3> path, float width, Vector3 axis, bool closed)
        {
            float length = 0f;
            for (int i = 0; i < path.Count; i++) length += Vector3.Distance(path[i], path[(i + 1) % path.Count]);
            int links = Mathf.Max(12, Mathf.RoundToInt(length / (width * 0.62f)));
            float step = length / links, walked = 0f, next = 0f;
            int k = 0;
            for (int i = 0; i < path.Count && k < links; i++)
            {
                Vector3 a = path[i], b = path[(i + 1) % path.Count];
                float len = Vector3.Distance(a, b);
                while (next <= walked + len && k < links)
                {
                    Vector3 at = Vector3.Lerp(a, b, (next - walked) / Mathf.Max(0.0001f, len));
                    Vector3 tangent = (b - a).normalized;
                    // The chain lies on the body: its flat faces point away from the loop's axis.
                    Vector3 outward = Vector3.ProjectOnPlane(at - Vector3.Project(at, axis), tangent).normalized;
                    if (outward.sqrMagnitude < 0.5f) outward = Vector3.Cross(tangent, axis).normalized;
                    Vector3 side = Vector3.Cross(tangent, outward);
                    // Alternate links twist ±25° about the tangent: the Cuban's interlocked, flattened look.
                    float twist = (k % 2 == 0 ? 25f : -25f) * Mathf.Deg2Rad;
                    Vector3 linkNormal = outward * Mathf.Cos(twist) + side * Mathf.Sin(twist);
                    var ring = Forge.Circle(at, linkNormal, width * 0.62f, 14, width * 0.42f);
                    // Circle's major axis may not lie along the tangent; rebuild explicitly.
                    ring.Clear();
                    Vector3 major = tangent, minor = Vector3.Cross(linkNormal, tangent).normalized;
                    for (int s = 0; s < 14; s++)
                    {
                        float t = s / 14f * Mathf.PI * 2f;
                        ring.Add(at + major * (Mathf.Cos(t) * width * 0.62f) + minor * (Mathf.Sin(t) * width * 0.4f));
                    }
                    f.Use(0).Sweep(ring, width * 0.16f, width * 0.11f, true, 7);
                    next += step;
                    k++;
                }
                walked += len;
            }
        }

        // ---------------------------------------------------------------- earrings

        private static GameObject Earring(Palette p, Transform parent, JewelryItem item, float s, Material metal, Material gem)
        {
            var f = new Forge(2);
            if (item.Kind == JewelryKind.Hoops)
                f.Use(0).Torus(Vector3.down * (s * 0.9f), Vector3.right, s * 0.9f, s * 0.09f, 32, 8);
            else
            {
                // Four-prong basket, stone facing out.
                for (int i = 0; i < 4; i++)
                {
                    float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                    f.Use(0).Sweep(new List<Vector3> { new Vector3(0f, Mathf.Cos(a) * s * 0.22f, Mathf.Sin(a) * s * 0.22f), new Vector3(s * 0.25f, Mathf.Cos(a) * s * 0.3f, Mathf.Sin(a) * s * 0.3f) }, s * 0.04f, s * 0.04f, false, 5);
                }
                f.Use(1).Gem(new Vector3(s * 0.2f, 0f, 0f), Vector3.right, s * 0.6f, 16);
            }
            return f.Build(parent, item.Name, metal, gem);
        }

        // ---------------------------------------------------------------- glasses

        /// <summary>
        /// Glasses for eyes <paramref name="spacing"/> apart (centre to centre); temples run back <paramref name="depth"/>
        /// to the ears. Lens size follows the spacing (real frames: lens width ~0.8 of the pupil distance).
        /// </summary>
        private static GameObject Glasses(Palette p, Transform parent, JewelryItem item, float spacing, float depth, Material metal)
        {
            var f = new Forge(3); // 0 frame, 1 lens, 2 metal hinges and pads
            float half = spacing / 2f;
            float lw = spacing * 0.46f, lh = lw * (item.Kind == JewelryKind.Aviators ? 0.9f : item.Kind == JewelryKind.RoundFrames ? 1f : 0.72f);
            bool acetate = item.Kind == JewelryKind.RoundFrames || item.Kind == JewelryKind.SquareFrames;
            float rim = acetate ? spacing * 0.035f : spacing * 0.012f;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 c = new Vector3(side * half, 0f, 0f);
                List<Vector3> shape = LensShape(item.Kind, c, lw / 2f, lh / 2f, side);
                if (item.Kind != JewelryKind.Rimless) f.Use(0).Sweep(shape, rim, rim * (acetate ? 1.3f : 1f), true, acetate ? 6 : 6);
                // Lens: a fan over the same outline, bowed slightly outward.
                f.Use(1).Fan(c + Vector3.forward * (rim * 0.3f), shape, Vector3.forward);
                // Hinge and temple: from the outer edge back along the side of the head to the ear, then a bend down.
                Vector3 hinge = c + new Vector3(side * lw / 2f, lh * 0.18f, -rim);
                f.Use(item.Kind == JewelryKind.Rimless ? 2 : 0).Block(hinge, Vector3.right, Vector3.up, new Vector3(rim * 3f, rim * 3f, rim * 3f));
                Vector3 ear = hinge + new Vector3(side * spacing * 0.08f, -lh * 0.05f, -depth);
                f.Use(0).Sweep(new List<Vector3> { hinge, Vector3.Lerp(hinge, ear, 0.5f) + new Vector3(side * spacing * 0.06f, 0f, 0f), ear, ear + new Vector3(0f, -lh * 0.4f, -spacing * 0.08f) },
                    acetate ? rim * 0.7f : rim * 0.8f, acetate ? rim * 1.2f : rim * 0.8f, false, 6);
                // Nose pads on metal frames.
                if (!acetate) f.Use(2).Lathe(c + new Vector3(-side * lw * 0.42f, -lh * 0.2f, -rim * 2f), Vector3.right * side, Sphere(rim * 1.8f, 6), 8, 1f, 0.6f);
            }
            // Bridge (a double bridge on aviators).
            f.Use(0).Sweep(Arc(new Vector3(-half + lw * 0.45f, lh * 0.12f, 0f), new Vector3(half - lw * 0.45f, lh * 0.12f, 0f), lh * 0.12f), rim, rim, false, 6);
            if (item.Kind == JewelryKind.Aviators)
                f.Use(0).Sweep(new List<Vector3> { new Vector3(-half + lw * 0.2f, lh * 0.46f, 0f), new Vector3(half - lw * 0.2f, lh * 0.46f, 0f) }, rim, rim, false, 6);
            Material frame = item.Metal == Metal.Black ? p.Lit(item.Trim, 0.85f) : p.Metal(item.Trim, 0.9f);
            Material lens = p.Glass(item.Accent);
            return f.Build(parent, item.Name, frame, lens, MetalOf(p, Metal.WhiteGold));
        }

        private static List<Vector3> LensShape(JewelryKind kind, Vector3 c, float a, float b, float side)
        {
            var pts = new List<Vector3>(32);
            for (int i = 0; i < 32; i++)
            {
                float t = i / 32f * Mathf.PI * 2f;
                float x = Mathf.Cos(t), y = Mathf.Sin(t);
                switch (kind)
                {
                    case JewelryKind.Aviators:
                        // Teardrop: wider at the top outer corner, falling to a deep lower inner curve.
                        y = y < 0f ? y * 1.25f : y * 0.85f;
                        x *= 1f + 0.12f * y * side;
                        break;
                    case JewelryKind.SquareFrames:
                    case JewelryKind.Rimless:
                        // Superellipse: squarer corners.
                        x = Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 0.55f);
                        y = Mathf.Sign(y) * Mathf.Pow(Mathf.Abs(y), 0.55f);
                        break;
                }
                pts.Add(c + new Vector3(x * a, y * b, 0f));
            }
            return pts;
        }

        private static List<Vector3> Arc(Vector3 a, Vector3 b, float rise)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                pts.Add(Vector3.Lerp(a, b, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * rise));
            }
            return pts;
        }

        /// <summary>A sphere's lathe profile.</summary>
        private static Vector2[] Sphere(float r, int rings)
        {
            var pts = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float t = -Mathf.PI / 2f + i / (float)rings * Mathf.PI;
                pts[i] = new Vector2(Mathf.Cos(t) * r, Mathf.Sin(t) * r);
            }
            return pts;
        }
    }
}
