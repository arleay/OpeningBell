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
            if (item.IsMonitor) Monitor(c, r, item, variant);
            else if (item.IsArm) Arm(c, r, item, variant);
            else if (item.Id == "pc_tower")
            {
                // The workspace pack's tower, scaled to the catalog height; a dark case with a light strip without it.
                if (k.Fit(r, "office_ws_pc", Vector3.zero, new Vector3(0f, item.Height, 0f)) != null) return root;
                k.Box(r, "Case", new Vector3(0f, item.Height / 2f, 0f), new Vector3(item.Width, item.Height, item.Depth), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f), collider: false);
                k.Box(r, "Light strip", new Vector3(0f, item.Height * 0.55f, -item.Depth / 2f - 0.003f), new Vector3(0.02f, item.Height * 0.7f, 0.004f), c.P.Glow(new Color(0.3f, 0.7f, 1f)), collider: false);
            }
            else if (item.Support == Support.Wall) WallPiece(c, r, item);
            else
            {
                GameObject go = k.Fit(r, item.Model, Vector3.zero, new Vector3(item.Width, item.Height, item.Depth), 0f, stretch: true);
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

        private static void WallPiece(CityContext c, Transform r, HomeItem item)
        {
            Kit k = c.Kit;
            float w = item.Width, h = item.Height;
            switch (item.Id)
            {
                case "wall_clock":
                    GameObject face = k.Cylinder(r, "Face", new Vector3(0f, h / 2f, 0f), w, 0.04f, c.P.Lit(new Color(0.95f, 0.94f, 0.9f)));
                    face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    k.Box(r, "Hour hand", new Vector3(0f, h / 2f + 0.04f, -0.025f), new Vector3(0.015f, 0.09f, 0.005f), c.P.Lit(Color.black), collider: false);
                    k.Box(r, "Minute hand", new Vector3(0.05f, h / 2f, -0.026f), new Vector3(0.11f, 0.01f, 0.005f), c.P.Lit(Color.black), collider: false);
                    break;
                case "mirror":
                    k.Box(r, "Frame", new Vector3(0f, h / 2f, 0f), new Vector3(w, h, 0.03f), c.P.Lit(new Color(0.35f, 0.25f, 0.18f)), collider: false);
                    k.Box(r, "Glass", new Vector3(0f, h / 2f, -0.017f), new Vector3(w - 0.08f, h - 0.08f, 0.004f), c.P.Lit(new Color(0.75f, 0.8f, 0.82f), 0.95f), collider: false);
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
