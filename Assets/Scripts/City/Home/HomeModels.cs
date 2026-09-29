using System.Collections.Generic;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// How catalog items look: a furniture model (Kenney, or its Poly Haven / Sketchfab upgrade) fitted to the item's size and tinted by its colour, or a few
    /// boxes for the things the kit doesn't have (monitors, arms, art, the tower PC), or a shipping box while boxed.
    /// Local frame: origin at the bottom centre, the front facing -z.
    /// </summary>
    public static class HomeModels
    {
        private static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>
        {
            ["Default"] = Color.white, ["Charcoal"] = new Color(0.38f, 0.38f, 0.4f), ["Oatmeal"] = new Color(0.95f, 0.9f, 0.8f),
            ["Navy"] = new Color(0.4f, 0.48f, 0.75f), ["Forest"] = new Color(0.45f, 0.62f, 0.45f), ["Oak"] = new Color(1f, 0.93f, 0.8f),
            ["Walnut"] = new Color(0.6f, 0.45f, 0.35f), ["White"] = new Color(1f, 1f, 1f), ["Black"] = new Color(0.3f, 0.3f, 0.32f),
            ["Silver"] = new Color(0.9f, 0.9f, 0.92f),
        };

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static Color ColourOf(HomeItem item, int variant) =>
            Colours.TryGetValue(item.Variants[Mathf.Clamp(variant, 0, item.Variants.Length - 1)], out Color c) ? c : Color.white;

        /// <summary>Monitor screen quad, by name, so the display can find it.</summary>
        public const string ScreenName = "Screen";

        public static GameObject Build(CityContext c, Transform parent, HomeItem item, int variant, bool boxed)
        {
            var root = new GameObject(item.Name);
            root.transform.SetParent(parent, false);
            Kit k = c.Kit;
            Transform r = root.transform;
            if (boxed)
            {
                // A shipping box a little bigger than what's in it.
                Vector3 size = new Vector3(item.Width + 0.08f, Mathf.Max(0.12f, item.Height + 0.08f), item.Depth + 0.08f);
                k.Box(r, "Box", new Vector3(0f, size.y / 2f, 0f), size, c.P.Lit(new Color(0.66f, 0.52f, 0.36f), 0.05f), collider: false);
                k.Box(r, "Tape", new Vector3(0f, size.y + 0.002f, 0f), new Vector3(0.06f, 0.004f, size.z), c.P.Lit(new Color(0.8f, 0.75f, 0.6f), 0.4f), collider: false);
                k.Text(r, (item.Brand ?? "CIRCUIT STOP").ToUpperInvariant(), new Vector3(0f, size.y * 0.55f, -size.z / 2f - 0.005f), 0f,
                    Mathf.Clamp(size.x * 0.06f, 0.02f, 0.08f), new Color(0.15f, 0.12f, 0.1f));
                return root;
            }
            if (item.IsBox)
            {
                // Kenney's cardboard box, flaps up (open) or folded shut; a plain carton where the kit is missing.
                string model = variant == 1 ? "cardboardBoxClosed" : "cardboardBoxOpen";
                if (k.Fit(r, model, Vector3.zero, new Vector3(item.Width, item.Height, item.Depth), 0f, stretch: true) == null)
                    k.Box(r, "Carton", new Vector3(0f, item.Height / 2f, 0f), new Vector3(item.Width, item.Height, item.Depth), c.P.Lit(new Color(0.66f, 0.52f, 0.36f), 0.05f), collider: false);
                return root;
            }
            if (item.IsKit) KitSetup(c, r, item);
            else if (item.IsMonitor) Monitor(c, r, item, variant);
            else if (item.IsLaptop) Laptop(c, r, item, variant);
            else if (item.IsArm) Arm(c, r, item, variant);
            else if (item.Id == "pc_tower")
            {
                // The workspace pack's tower, scaled to the catalog height; a dark case with a light strip without it.
                if (k.Fit(r, "office_ws_pc", Vector3.zero, new Vector3(0f, item.Height, 0f)) != null) return root;
                k.Box(r, "Case", new Vector3(0f, item.Height / 2f, 0f), new Vector3(item.Width, item.Height, item.Depth), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f), collider: false);
                k.Box(r, "Light strip", new Vector3(0f, item.Height * 0.55f, -item.Depth / 2f - 0.003f), new Vector3(0.02f, item.Height * 0.7f, 0.004f), c.P.Glow(new Color(0.3f, 0.7f, 1f)), collider: false);
            }
            else if (item.Id == "coffee_station") CoffeeStation(c, r, item, variant);
            else if (item.Support == Support.Wall) WallPiece(c, r, item);
            else
            {
                // Furniture is fitted to its catalog box. Desk tech (keyboards, mice, laptops, speakers) keeps its own
                // proportions, scaled to fit the footprint: stretched to the box, a keyboard came out four times as
                // thick as it is and the open laptop was squashed flat onto its keys.
                bool tech = item.Store == HomeStore.Tech;
                GameObject go = k.Fit(r, item.Model, Vector3.zero, new Vector3(item.Width, tech ? 0f : item.Height, item.Depth), 0f, stretch: !tech);
                if (go == null) k.Box(r, "Stand-in", new Vector3(0f, item.Height / 2f, 0f), new Vector3(item.Width, item.Height, item.Depth), c.P.Lit(new Color(0.6f, 0.55f, 0.5f)), collider: false);
                else Tint(go, ColourOf(item, variant));
            }
            return root;
        }

        /// <summary>Tints a kit model's own texture by the chosen colour (white leaves it as it is).</summary>
        public static void Tint(GameObject go, Color colour)
        {
            if (colour == Color.white) return;
            var block = new MaterialPropertyBlock();
            foreach (Renderer rend in go.GetComponentsInChildren<Renderer>())
            {
                rend.GetPropertyBlock(block);
                block.SetColor(BaseColor, colour);
                rend.SetPropertyBlock(block);
            }
        }

        /// <summary>A stand, a thin panel and the screen facing -z (landscape; portrait turns the panel).</summary>
        private static void Monitor(CityContext c, Transform r, HomeItem item, int variant)
        {
            Kit k = c.Kit;
            Material body = c.P.Lit(ColourOf(item, variant) * 0.3f, 0.4f);
            float w = item.Width, h = item.Height;
            k.Box(r, "Base", new Vector3(0f, 0.01f, 0f), new Vector3(w * 0.35f, 0.02f, item.Depth), body, collider: false);
            k.Box(r, "Neck", new Vector3(0f, h * 0.3f, item.Depth * 0.2f), new Vector3(0.05f, h * 0.55f, 0.03f), body, collider: false);
            Transform panel = Kit.Group(r, "Panel", new Vector3(0f, h * 0.6f, 0f));
            float pw = w, ph = h * 0.75f;
            k.Box(panel, "Bezel", Vector3.zero, new Vector3(pw, ph, 0.03f), body, collider: false);
            GameObject screen = k.Box(panel, ScreenName, new Vector3(0f, 0f, -0.017f), new Vector3(pw - 0.03f, ph - 0.03f, 0.002f),
                c.P.Unlit(new Color(0.02f, 0.02f, 0.03f)), collider: false);
            screen.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// A laptop open on the desk: the base with a keyboard deck and trackpad, the lid on a hinge at the back tilted
        /// 15° away, its screen facing -z like a monitor's (a "Panel" holding the <see cref="ScreenName"/> quad, which
        /// MonitorScreen draws on).
        /// </summary>
        private static void Laptop(CityContext c, Transform r, HomeItem item, int variant)
        {
            Kit k = c.Kit;
            Material shell = c.P.Lit(ColourOf(item, variant) * 0.45f, 0.55f);
            Material keys = c.P.Lit(new Color(0.07f, 0.07f, 0.08f), 0.2f);
            float w = item.Width, d = item.Depth, t = 0.016f;
            k.Box(r, "Base", new Vector3(0f, t / 2f, 0f), new Vector3(w, t, d), shell, collider: false);
            k.Box(r, "Keys", new Vector3(0f, t + 0.001f, 0.02f), new Vector3(w - 0.04f, 0.002f, d * 0.45f), keys, collider: false);
            k.Box(r, "Trackpad", new Vector3(0f, t + 0.001f, -d * 0.3f), new Vector3(w * 0.3f, 0.002f, d * 0.22f), keys, collider: false);
            float lh = d - 0.01f; // the lid folds down over the base
            Transform hinge = Kit.Group(r, "Hinge", new Vector3(0f, t, d / 2f - 0.004f));
            hinge.localRotation = Quaternion.Euler(15f, 0f, 0f);
            Transform panel = Kit.Group(hinge, "Panel", new Vector3(0f, lh / 2f, 0f));
            k.Box(panel, "Lid", new Vector3(0f, 0f, 0.004f), new Vector3(w, lh, 0.008f), shell, collider: false);
            GameObject screen = k.Box(panel, ScreenName, new Vector3(0f, 0.004f, -0.001f), new Vector3(w - 0.03f, lh - 0.035f, 0.002f),
                c.P.Unlit(new Color(0.02f, 0.02f, 0.03f)), collider: false);
            screen.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>One piece of an Office Ready Kit, in the desk's frame: where it stands, and what it hangs from.</summary>
        public readonly struct KitPart
        {
            public readonly HomeItem Item;
            public readonly Vector3 Local;
            /// <summary>0 on the floor (the desk), 1 on the desk, 2 on the arm.</summary>
            public readonly int On;

            public KitPart(HomeItem item, Vector3 local, int on)
            {
                Item = item;
                Local = local;
                On = on;
            }
        }

        /// <summary>
        /// How an Office Ready Kit is set out, in the desk's frame (front -z, where you sit): the arm clamped at the back
        /// middle with the screens on its slots, the tower on the left of the top, the keyboard front and centre with
        /// the mouse to its right, the speaker at the right-hand end.
        /// </summary>
        public static List<KitPart> KitLayout(HomeItem kit)
        {
            string[] ids = HomeItem.KitParts(kit.KitScreens);
            HomeItem desk = HomeCatalog.Find(ids[0]), arm = HomeCatalog.Find(ids[1]);
            float top = desk.Surface, hw = desk.Width / 2f, hd = desk.Depth / 2f;
            var parts = new List<KitPart> { new KitPart(desk, Vector3.zero, 0) };
            Vector3 clamp = new Vector3(0f, top, hd - 0.08f);
            parts.Add(new KitPart(arm, clamp, 1));
            int screen = 0;
            for (int i = 2; i < ids.Length; i++)
            {
                HomeItem it = HomeCatalog.Find(ids[i]);
                if (it.IsMonitor) { parts.Add(new KitPart(it, clamp + ArmSlot(kit.KitScreens, screen++), 2)); continue; }
                Vector3 at = it.Id switch
                {
                    "pc_tower" => new Vector3(-hw + 0.2f, top, 0.05f),
                    "keyboard" => new Vector3(0f, top, -hd + 0.22f),
                    "mouse" => new Vector3(0.36f, top, -hd + 0.22f),
                    _ => new Vector3(hw - 0.15f, top, 0.1f), // the speaker
                };
                parts.Add(new KitPart(it, at, 1));
            }
            return parts;
        }

        /// <summary>The kit as it'll stand once unpacked: every piece's own model in its place (screens on the arm, no stands).</summary>
        private static void KitSetup(CityContext c, Transform r, HomeItem kit)
        {
            foreach (KitPart part in KitLayout(kit))
            {
                GameObject piece = Build(c, r, part.Item, 0, false);
                piece.transform.localPosition = part.Local;
                if (part.On != 2) continue;
                foreach (Transform t in piece.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Base" || t.name == "Neck") t.gameObject.SetActive(false);
            }
        }

        /// <summary>A pole clamped at the back, a bar across with a mount for each screen.</summary>
        private static void Arm(CityContext c, Transform r, HomeItem item, int variant)
        {
            Kit k = c.Kit;
            Material metal = c.P.Lit(ColourOf(item, variant) * 0.5f, 0.6f);
            k.Box(r, "Clamp", new Vector3(0f, 0.03f, 0.05f), new Vector3(0.1f, 0.06f, 0.1f), metal, collider: false);
            k.Box(r, "Pole", new Vector3(0f, 0.3f, 0.05f), new Vector3(0.04f, 0.6f, 0.04f), metal, collider: false);
            float span = ArmSpan(item.Arms);
            k.Box(r, "Bar", new Vector3(0f, 0.45f, 0.03f), new Vector3(span, 0.03f, 0.03f), metal, collider: false);
            if (item.Arms > 3) k.Box(r, "Upper bar", new Vector3(0f, 0.85f, 0.03f), new Vector3(span, 0.03f, 0.03f), metal, collider: false);
        }

        /// <summary>Width of an arm's bar: three screens abreast at most, more stack in a second row.</summary>
        public static float ArmSpan(int arms) => 0.66f * Mathf.Min(arms, 3);

        /// <summary>Where screen <paramref name="slot"/> hangs on an arm (arm-local), bottom centre of the monitor.</summary>
        public static Vector3 ArmSlot(int arms, int slot)
        {
            int perRow = Mathf.Min(arms, 3);
            int row = slot / perRow, col = slot % perRow;
            int inRow = row == 0 ? perRow : arms - perRow;
            return new Vector3((col - (inRow - 1) / 2f) * 0.66f, 0.2f + row * 0.42f, -0.06f);
        }

        /// <summary>
        /// A base cabinet in the chosen wood with a stone top, the kit's coffee machine, a grinder, cups and a tray of mugs:
        /// the office coffee point.
        /// </summary>
        private static void CoffeeStation(CityContext c, Transform r, HomeItem item, int variant)
        {
            Kit k = c.Kit;
            float w = item.Width, d = item.Depth, top = 0.9f;
            GameObject cab = k.Fit(r, "kitchenCabinet", Vector3.zero, new Vector3(w, top, d), 0f, stretch: true);
            if (cab == null) k.Box(r, "Cabinet", new Vector3(0f, top / 2f, 0f), new Vector3(w, top, d), c.P.Lit(ColourOf(item, variant) * 0.55f, 0.3f), collider: false);
            else Tint(cab, ColourOf(item, variant));
            k.Box(r, "Worktop", new Vector3(0f, top + 0.02f, 0f), new Vector3(w + 0.02f, 0.04f, d + 0.02f), c.P.Lit(new Color(0.78f, 0.76f, 0.72f), 0.4f), collider: false);
            float y = top + 0.04f;
            if (k.Fit(r, "kitchenCoffeeMachine", new Vector3(-w * 0.22f, y, 0.02f), new Vector3(0.36f, 0.4f, 0.34f), 0f) == null)
            {
                k.Box(r, "Machine", new Vector3(-w * 0.22f, y + 0.2f, 0.02f), new Vector3(0.36f, 0.4f, 0.34f), c.P.Lit(new Color(0.75f, 0.76f, 0.78f), 0.8f), collider: false);
                k.Box(r, "Drip tray", new Vector3(-w * 0.22f, y + 0.03f, -0.12f), new Vector3(0.28f, 0.03f, 0.1f), c.P.Lit(new Color(0.2f, 0.2f, 0.22f), 0.6f), collider: false);
            }
            Material cup = c.P.Lit(new Color(0.95f, 0.95f, 0.93f), 0.6f);
            k.Box(r, "Grinder", new Vector3(w * 0.06f, y + 0.16f, 0.08f), new Vector3(0.14f, 0.32f, 0.16f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f), collider: false);
            k.Box(r, "Tray", new Vector3(w * 0.3f, y + 0.01f, 0f), new Vector3(0.36f, 0.02f, 0.26f), c.P.Lit(new Color(0.3f, 0.2f, 0.13f), 0.3f), collider: false);
            for (int i = 0; i < 6; i++)
                k.Cylinder(r, "Mug", new Vector3(w * 0.3f - 0.12f + (i % 3) * 0.12f, y + 0.07f, -0.06f + (i / 3) * 0.12f), 0.08f, 0.1f, cup);
        }

        private static void WallPiece(CityContext c, Transform r, HomeItem item)
        {
            Kit k = c.Kit;
            float w = item.Width, h = item.Height;
            switch (item.Id)
            {
                case "wall_clock":
                {
                    GameObject face = k.Cylinder(r, "Face", new Vector3(0f, h / 2f, 0f), w, 0.04f, c.P.Lit(new Color(0.95f, 0.94f, 0.9f)));
                    face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    // Hour ticks, and hands on pivots at the centre that keep the game's time (ClockHands).
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f * Mathf.Deg2Rad;
                        Vector3 at = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * (w * 0.42f) + new Vector3(0f, h / 2f, -0.022f);
                        k.Box(r, "Tick", at, new Vector3(0.008f, i % 3 == 0 ? 0.03f : 0.016f, 0.004f), c.P.Lit(Color.black), collider: false, yaw: 0f)
                            .transform.localRotation = Quaternion.Euler(0f, 0f, -i * 30f);
                    }
                    Transform hour = Kit.Group(r, "Hour hand", new Vector3(0f, h / 2f, -0.025f));
                    Transform minute = Kit.Group(r, "Minute hand", new Vector3(0f, h / 2f, -0.027f));
                    k.Box(hour, "Hand", new Vector3(0f, w * 0.13f, 0f), new Vector3(0.014f, w * 0.3f, 0.004f), c.P.Lit(Color.black), collider: false);
                    k.Box(minute, "Hand", new Vector3(0f, w * 0.19f, 0f), new Vector3(0.01f, w * 0.42f, 0.004f), c.P.Lit(Color.black), collider: false);
                    r.gameObject.AddComponent<ClockHands>().Configure(c, hour, minute);
                    break;
                }
                case "whiteboard":
                    k.Box(r, "Frame", new Vector3(0f, h / 2f, 0f), new Vector3(w, h, 0.03f), c.P.Lit(new Color(0.75f, 0.76f, 0.78f), 0.7f), collider: false);
                    k.Box(r, "Board", new Vector3(0f, h / 2f, -0.017f), new Vector3(w - 0.05f, h - 0.05f, 0.004f), c.P.Lit(new Color(0.97f, 0.97f, 0.96f), 0.9f), collider: false);
                    k.Box(r, "Ledge", new Vector3(0f, 0.02f, -0.04f), new Vector3(w * 0.6f, 0.02f, 0.06f), c.P.Lit(new Color(0.75f, 0.76f, 0.78f), 0.7f), collider: false);
                    k.Box(r, "Chart line", new Vector3(-0.2f, h * 0.55f, -0.021f), new Vector3(w * 0.45f, 0.012f, 0.002f), c.P.Lit(new Color(0.15f, 0.35f, 0.75f)), collider: false, yaw: 0f)
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                    k.Box(r, "Note", new Vector3(w * 0.28f, h * 0.7f, -0.021f), new Vector3(0.3f, 0.012f, 0.002f), c.P.Lit(new Color(0.8f, 0.2f, 0.2f)), collider: false);
                    break;
                case "mirror":
                    k.Box(r, "Frame", new Vector3(0f, h / 2f, 0f), new Vector3(w, h, 0.03f), c.P.Lit(new Color(0.35f, 0.25f, 0.18f)), collider: false);
                    Mirror.Create(c, r, "Glass", new Vector3(0f, h / 2f, -0.017f), Vector3.back, new Vector2(w - 0.08f, h - 0.08f));
                    break;
                default:
                    k.Box(r, "Frame", new Vector3(0f, h / 2f, 0f), new Vector3(w, h, 0.03f), c.P.Lit(new Color(0.1f, 0.1f, 0.1f)), collider: false);
                    k.Box(r, "Print", new Vector3(0f, h / 2f, -0.017f), new Vector3(w - 0.1f, h - 0.1f, 0.004f), c.P.Lit(new Color(0.3f, 0.5f, 0.6f)), collider: false);
                    k.Box(r, "Sun", new Vector3(0.12f, h * 0.6f, -0.02f), new Vector3(0.12f, 0.12f, 0.004f), c.P.Lit(new Color(0.95f, 0.7f, 0.3f)), collider: false);
                    break;
            }
        }
    }
}
