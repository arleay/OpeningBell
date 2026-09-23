using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Paints the town once, top-down, into a texture for the minimap and the phone's map: land (grass, forest,
    /// the industrial yards), the bay, canal and river, paving, streets with their sidewalks, alleys and dirt roads,
    /// then every building footprint from the built colliders (anything solid taller than a person that starts near
    /// the ground), so the map always matches what was generated. North (+z) is up; row 0 is the south edge.
    /// </summary>
    public static class MapTexture
    {
        public const float PixelsPerMetre = 1f;

        private static readonly Color32 Road = new Color(0.22f, 0.23f, 0.25f);
        private static readonly Color32 Walk = new Color(0.6f, 0.6f, 0.58f);
        private static readonly Color32 Lawn = new Color(0.36f, 0.53f, 0.3f);
        private static readonly Color32 Forest = new Color(0.22f, 0.36f, 0.22f);
        private static readonly Color32 Yard = new Color(0.47f, 0.46f, 0.42f);
        private static readonly Color32 Paved = new Color(0.47f, 0.47f, 0.46f);
        private static readonly Color32 Water = new Color(0.25f, 0.42f, 0.52f);
        private static readonly Color32 Alley = new Color(0.33f, 0.33f, 0.34f);
        private static readonly Color32 Dirt = new Color(0.5f, 0.42f, 0.31f);
        private static readonly Color32 Building = new Color(0.86f, 0.8f, 0.7f);

        public static Texture2D Paint(CityContext c)
        {
            Rect w = CityPlan.World;
            int width = Mathf.CeilToInt(w.width * PixelsPerMetre), height = Mathf.CeilToInt(w.height * PixelsPerMetre);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float wx = w.xMin + (x + 0.5f) / PixelsPerMetre, wz = w.yMin + (y + 0.5f) / PixelsPerMetre;
                bool water = TownTerrain.InCanal(wx, wz) || Mathf.Abs(wz - TownTerrain.River(wx)) < TownTerrain.RiverHalfWidth || wz < TownTerrain.Shore(wx) - 2f;
                float wild = TownTerrain.Wildness(wx, wz);
                bool industrial = wx < -255f && wz < -62f && wild < 0.5f;
                pixels[y * width + x] = water ? Water : industrial ? Yard : Color32.Lerp(Lawn, Forest, wild);
            }

            void Fill(Rect r, Color32 color)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt((r.xMin - w.xMin) * PixelsPerMetre), 0, width);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((r.xMax - w.xMin) * PixelsPerMetre), 0, width);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((r.yMin - w.yMin) * PixelsPerMetre), 0, height);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((r.yMax - w.yMin) * PixelsPerMetre), 0, height);
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) pixels[y * width + x] = color;
            }

            void Line(Vector2 a, Vector2 b, float half, Color32 color)
            {
                Rect box = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - half, Mathf.Min(a.y, b.y) - half, Mathf.Max(a.x, b.x) + half, Mathf.Max(a.y, b.y) + half);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((box.xMin - w.xMin) * PixelsPerMetre), 0, width);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((box.xMax - w.xMin) * PixelsPerMetre), 0, width);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((box.yMin - w.yMin) * PixelsPerMetre), 0, height);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((box.yMax - w.yMin) * PixelsPerMetre), 0, height);
                Vector2 ab = b - a;
                float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var p = new Vector2(w.xMin + (x + 0.5f) / PixelsPerMetre, w.yMin + (y + 0.5f) / PixelsPerMetre);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    if ((a + ab * t - p).sqrMagnitude <= half * half) pixels[y * width + x] = color;
                }
            }

            foreach (Rect r in CityPlan.Paved) Fill(r, Paved);
            StreetMap map = c.Roads.Map;
            foreach (StreetDef d in map.Driveways)
                for (int i = 0; i + 1 < d.Points.Length; i++)
                    Line(d.Points[i], d.Points[i + 1], CityPlan.HalfWidth(d.Class), d.Class == RoadClass.Alley ? Alley : Dirt);
            // Sidewalks first, roads over them (so junctions read as asphalt).
            foreach (StreetMap.Segment s in map.Segments)
                if (s.Sidewalk > 0f) Line(s.A.P, s.B.P, s.HalfWidth + s.Sidewalk, Walk);
            foreach (StreetMap.Segment s in map.Segments) Line(s.A.P, s.B.P, s.HalfWidth, Road);

            foreach (Collider col in c.Static.GetComponentsInChildren<Collider>())
            {
                if (col is CapsuleCollider || col is MeshCollider || col is TerrainCollider) continue; // trunks, ground
                Bounds b = col.bounds;
                float ground = TownTerrain.Natural(b.center.x, b.center.z);
                if (b.size.y < 2.2f || b.min.y > ground + 1.5f || b.size.y > 60f) continue; // kerbs and fences; boundary walls
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
