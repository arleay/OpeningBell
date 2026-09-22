using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Paints the town once, top-down, into a texture for the minimap: roads, sidewalks, lawns and paving from
    /// <see cref="CityPlan.Blocks"/>, then every building footprint from the built colliders (anything solid taller than
    /// a person that starts at the ground), so the map always matches what was generated. North (+z) is up; texture
    /// row 0 is the world's south edge.
    /// </summary>
    public static class MapTexture
    {
        public const float PixelsPerMetre = 2f;

        private static readonly Color32 Road = new Color(0.22f, 0.23f, 0.25f);
        private static readonly Color32 Walk = new Color(0.6f, 0.6f, 0.58f);
        private static readonly Color32 Lawn = new Color(0.36f, 0.53f, 0.3f);
        private static readonly Color32 Paved = new Color(0.47f, 0.47f, 0.46f);
        private static readonly Color32 Building = new Color(0.86f, 0.8f, 0.7f);
        private static readonly Color32 Line = new Color(0.78f, 0.66f, 0.25f);

        public static Texture2D Paint(CityContext c)
        {
            Rect w = CityPlan.World;
            int width = Mathf.CeilToInt(w.width * PixelsPerMetre), height = Mathf.CeilToInt(w.height * PixelsPerMetre);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Road;

            void Fill(Rect r, Color32 color)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt((r.xMin - w.xMin) * PixelsPerMetre), 0, width);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((r.xMax - w.xMin) * PixelsPerMetre), 0, width);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((r.yMin - w.yMin) * PixelsPerMetre), 0, height);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((r.yMax - w.yMin) * PixelsPerMetre), 0, height);
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) pixels[y * width + x] = color;
            }

            // Centre lines, then the blocks over the asphalt.
            foreach (var (a, b, _) in CityPlan.Streets)
            {
                Vector2 pa = CityPlan.Nodes[a].P, pb = CityPlan.Nodes[b].P;
                Fill(Rect.MinMaxRect(Mathf.Min(pa.x, pb.x) - 0.3f, Mathf.Min(pa.y, pb.y) - 0.3f, Mathf.Max(pa.x, pb.x) + 0.3f, Mathf.Max(pa.y, pb.y) + 0.3f), Line);
            }
            float sw = CityPlan.SidewalkWidth;
            foreach (var (_, area, _, lawn) in CityPlan.Blocks)
            {
                Fill(area, Walk);
                Fill(Rect.MinMaxRect(area.xMin + sw, area.yMin + sw, area.xMax - sw, area.yMax - sw), lawn ? Lawn : Paved);
            }

            foreach (Collider col in c.Static.GetComponentsInChildren<Collider>())
            {
                if (col is CapsuleCollider) continue; // tree trunks
                Bounds b = col.bounds;
                if (b.size.y < 2.2f || b.min.y > 1f || b.size.y > 50f) continue; // kerbs and fences; boundary walls
                Fill(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z), Building);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Town map", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        /// <summary>A world position as a pixel on the map, measured from its top-left corner (UI coordinates).</summary>
        public static Vector2 ToPixel(Vector3 world) =>
            new Vector2((world.x - CityPlan.World.xMin) * PixelsPerMetre, (CityPlan.World.yMax - world.z) * PixelsPerMetre);
    }
}
