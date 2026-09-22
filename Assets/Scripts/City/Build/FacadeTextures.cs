using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// TODO(art): procedural facade textures until real building art exists. Each texture is a 4×4 grid of cells,
    /// one cell = one window bay × one floor. The emission map marks which windows are lit at night; lighting a
    /// different ~40% per cell keeps the repeat from reading as a pattern.
    /// </summary>
    public static class FacadeTextures
    {
        public const int Cells = 4;
        private const int Cell = 64;
        private const int Size = Cells * Cell;

        public static void Create(FacadeStyle style, bool storefront, out Texture2D albedo, out Texture2D emission)
        {
            var color = new Color32[Size * Size];
            var glow = new Color32[Size * Size];
            var rng = new System.Random(storefront ? 99 : 17 + (int)style * 31);
            for (int cy = 0; cy < Cells; cy++)
            for (int cx = 0; cx < Cells; cx++)
            {
                bool lit = rng.NextDouble() < (storefront ? 0.6 : 0.4);
                PaintCell(style, storefront, cx * Cell, cy * Cell, lit, color, glow, rng);
            }
            albedo = Make("facade-" + style + (storefront ? "-shop" : ""), color);
            emission = Make("facade-" + style + (storefront ? "-shop" : "") + "-glow", glow);
        }

        private static Texture2D Make(string name, Color32[] pixels)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        private static void PaintCell(FacadeStyle style, bool storefront, int ox, int oy, bool lit, Color32[] c, Color32[] g, System.Random rng)
        {
            Color wall, frame, glass;
            RectInt window;
            if (storefront)
            {
                wall = new Color(0.2f, 0.2f, 0.22f);
                frame = new Color(0.12f, 0.12f, 0.13f);
                glass = new Color(0.16f, 0.2f, 0.23f);
                window = new RectInt(3, 8, 58, 40);
            }
            else
            {
                switch (style)
                {
                    case FacadeStyle.Brick:
                        wall = new Color(0.5f, 0.25f, 0.19f); frame = new Color(0.86f, 0.83f, 0.76f); glass = new Color(0.13f, 0.16f, 0.2f);
                        window = new RectInt(18, 14, 28, 38); break;
                    case FacadeStyle.Stucco:
                        wall = new Color(0.8f, 0.74f, 0.6f); frame = new Color(0.42f, 0.36f, 0.3f); glass = new Color(0.15f, 0.18f, 0.22f);
                        window = new RectInt(20, 16, 24, 34); break;
                    case FacadeStyle.Townhouse:
                        wall = new Color(0.36f, 0.4f, 0.47f); frame = new Color(0.92f, 0.92f, 0.9f); glass = new Color(0.12f, 0.15f, 0.19f);
                        window = new RectInt(17, 10, 30, 44); break;
                    case FacadeStyle.Concrete:
                        wall = new Color(0.62f, 0.61f, 0.58f); frame = new Color(0.3f, 0.31f, 0.32f); glass = new Color(0.17f, 0.22f, 0.27f);
                        window = new RectInt(0, 22, 64, 26); break;
                    default: // Glass curtain wall
                        wall = new Color(0.3f, 0.33f, 0.36f); frame = new Color(0.55f, 0.58f, 0.6f); glass = new Color(0.24f, 0.34f, 0.43f);
                        window = new RectInt(0, 12, 64, 52); break;
                }
            }

            for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                int i = (oy + y) * Size + ox + x;
                Color px = wall;
                float grain = (float)rng.NextDouble() * 0.05f - 0.025f;
                if (!storefront && (style == FacadeStyle.Brick || style == FacadeStyle.Townhouse) && (y % 6 == 0 || (x + (y / 6 % 2) * 6) % 12 == 0))
                    px = Color.Lerp(wall, new Color(0.7f, 0.68f, 0.64f), 0.35f); // mortar
                if (!storefront && style == FacadeStyle.Concrete && (x == 0 || y == 0)) px *= 0.85f; // panel joints
                bool inWindow = window.Contains(new Vector2Int(x, y));
                bool onFrame = inWindow && (x - window.xMin < 2 || window.xMax - 1 - x < 2 || y - window.yMin < 2 || window.yMax - 1 - y < 2);
                bool mullion = inWindow && (style == FacadeStyle.Glass || style == FacadeStyle.Concrete || storefront) && x % 32 == 0;
                if (inWindow) px = onFrame || mullion ? frame : Color.Lerp(glass, glass * 1.35f, (float)y / Cell);
                if (storefront && y >= 52) px = new Color(0.55f, 0.18f, 0.14f); // sign band
                px.r += grain; px.g += grain; px.b += grain;
                c[i] = px;
                g[i] = inWindow && !onFrame && !mullion && lit ? new Color32(255, 214, 150, 255) : new Color32(0, 0, 0, 255);
            }
        }
    }
}
