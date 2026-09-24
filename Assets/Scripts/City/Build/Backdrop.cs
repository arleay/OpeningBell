using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// What's past the edge of the map, for the view from anywhere high (the penthouse, the mast): the sea out to the
    /// horizon south of the bay, wooded hills that rise straight out of the terrain's edge on the other three sides
    /// (dipping into the sea at the ends as headlands), and a far ring of mountains behind them. Nothing here is
    /// reachable; fog does the haze, so from the street it's only a soft outline over the trees.
    /// </summary>
    public static class Backdrop
    {
        /// <summary>Middle of the town, the hills' and mountains' centre.</summary>
        private static readonly Vector2 Centre = new Vector2(-20f, 60f);
        private const int Steps = 240; // 1.5° apart
        private const float SeaFloor = -12f;

        public static void Build(CityContext c)
        {
            Transform root = Kit.Group(c.Static, "Backdrop");
            Sea(c, root);
            Hills(c, root);
            Mountains(c, root);
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The bay's water carried on out to 7 km, with a dark seabed under it so it doesn't look like glass over the sky.</summary>
        private static void Sea(CityContext c, Transform root)
        {
            Rect e = TownTerrain.Extent;
            const float far = 7000f;
            // The bay quad (StreetBuilder) covers x e.xMin-400..e.xMax+400, z e.yMin-600..-120; this goes round it.
            float bx0 = e.xMin - 400f, bx1 = e.xMax + 400f, bz0 = e.yMin - 600f, bz1 = -120f;
            var water = new MeshBuilder();
            void Quad(MeshBuilder mb, float x0, float z0, float x1, float z1, float y) =>
                mb.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1));
            Quad(water, -far, -far, far, bz0, TownTerrain.SeaLevel);
            Quad(water, -far, bz0, bx0, bz1, TownTerrain.SeaLevel);
            Quad(water, bx1, bz0, far, bz1, TownTerrain.SeaLevel);
            water.Build(root, "Open sea", c.P.Glass(new Color(0.16f, 0.26f, 0.3f, 0.82f)), collider: false);
            var bed = new MeshBuilder();
            Quad(bed, -far, -far, far, e.yMin + 1f, SeaFloor);
            Quad(bed, -far, e.yMin + 1f, e.xMin + 1f, bz1, SeaFloor);
            Quad(bed, e.xMax - 1f, e.yMin + 1f, far, bz1, SeaFloor);
            bed.Build(root, "Seabed", c.P.Lit(new Color(0.1f, 0.16f, 0.17f), 0f), collider: false);
        }

        private static Vector2 Dir(int i)
        {
            float a = i * Mathf.PI * 2f / Steps; // clockwise from north
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        /// <summary>Where a ray from the centre leaves the terrain (just inside it).</summary>
        private static Vector2 Edge(Vector2 d)
        {
            Rect e = TownTerrain.Extent;
            float tx = d.x > 0f ? (e.xMax - Centre.x) / d.x : d.x < 0f ? (e.xMin - Centre.x) / d.x : float.MaxValue;
            float tz = d.y > 0f ? (e.yMax - Centre.y) / d.y : d.y < 0f ? (e.yMin - Centre.y) / d.y : float.MaxValue;
            return Centre + d * (Mathf.Min(tx, tz) - 0.5f);
        }

        /// <summary>
        /// The hills: for each bearing, a profile from the terrain's own edge height up to a crest a few hundred metres
        /// out, then down the far side. Over the bay the edge is seabed, so the profile stays under water; the land share
        /// is blurred round the circle so the hills run down into the sea at the ends instead of stopping in a wall.
        /// </summary>
        private static void Hills(CityContext c, Transform root)
        {
            var land = new float[Steps];
            for (int i = 0; i < Steps; i++)
            {
                Vector2 p = Edge(Dir(i));
                land[i] = Mathf.Clamp01((TownTerrain.Height(p.x, p.y) - TownTerrain.SeaLevel) / 3f);
            }
            var soft = new float[Steps];
            const int blur = 10;
            for (int i = 0; i < Steps; i++)
            {
                float sum = 0f;
                for (int j = -blur; j <= blur; j++) sum += land[(i + j + Steps) % Steps];
                soft[i] = Mathf.SmoothStep(0f, 1f, sum / (blur * 2 + 1));
            }

            float[] share = { 0f, 0.06f, 0.16f, 0.3f, 0.48f, 0.68f, 0.86f, 1f, 1.5f };
            var rings = new Vector3[Steps + 1, share.Length];
            for (int i = 0; i <= Steps; i++)
            {
                int n = i % Steps;
                Vector2 d = Dir(n), p0 = Edge(d);
                float y0 = TownTerrain.Height(p0.x, p0.y);
                float a = n * 360f / Steps;
                // Crest height and distance vary slowly round the circle, with a few taller summits.
                float crest = 70f + 95f * Mathf.PerlinNoise(a * 0.02f, 3.1f) + 60f * Mathf.Pow(Mathf.PerlinNoise(a * 0.06f, 8.7f), 3f);
                float width = 300f + 220f * Mathf.PerlinNoise(a * 0.015f, 5.3f);
                float top = Mathf.Max(Mathf.Lerp(SeaFloor, crest, soft[n]), y0 + 20f * soft[n]);
                for (int k = 0; k < share.Length; k++)
                {
                    float s = share[k];
                    // Up to the crest (smooth), then down the back to two-thirds of it.
                    float y = s <= 1f ? Mathf.Lerp(y0, top, Mathf.SmoothStep(0f, 1f, s)) : Mathf.Lerp(top, top * 0.65f, (s - 1f) * 2f);
                    // A little roughness on the slopes (none at the seam with the terrain).
                    y += s > 0f ? (Mathf.PerlinNoise(a * 0.25f, s * 3f) - 0.5f) * 14f * soft[n] * Mathf.Min(1f, s * 4f) : 0f;
                    Vector2 q = p0 + d * (width * s);
                    rings[i, k] = new Vector3(q.x, y, q.y);
                }
            }
            var hills = new MeshBuilder();
            for (int i = 0; i < Steps; i++)
                for (int k = 0; k < share.Length - 1; k++)
                    hills.Quad(rings[i, k], rings[i + 1, k], rings[i + 1, k + 1], rings[i, k + 1]);
            hills.Build(root, "Hills", c.P.Lit(new Color(0.19f, 0.31f, 0.18f), 0f), collider: false);
        }

        /// <summary>
        /// A jagged ring of mountains 2–2.6 km out, low where it's behind the open sea (only a far coast there), blue with
        /// distance before the fog even starts on it.
        /// </summary>
        private static void Mountains(CityContext c, Transform root)
        {
            var ring = new Vector3[Steps + 1, 4];
            for (int i = 0; i <= Steps; i++)
            {
                int n = i % Steps;
                Vector2 d = Dir(n);
                float a = n * 360f / Steps;
                // South (bearing ~180) looks out to sea: a low far coast; everywhere else, mountains.
                float south = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(Mathf.DeltaAngle(a, 180f)) - 30f) / 50f));
                float r = Mathf.Lerp(2400f, 2150f, Mathf.Abs(d.y)) + 200f * Mathf.PerlinNoise(a * 0.03f, 1.7f);
                // Ridged noise for peaks: sharp tops, broad valleys.
                float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(a * 0.09f, 4.4f) * 2f - 1f);
                float peak = Mathf.Lerp(60f, 230f + 330f * ridge * ridge, south);
                Vector2 foot = Centre + d * (r - 350f), top = Centre + d * r, back = Centre + d * (r + 600f);
                ring[i, 0] = new Vector3(foot.x, SeaFloor, foot.y);
                ring[i, 1] = new Vector3(Vector2.Lerp(foot, top, 0.55f).x, peak * 0.45f, Vector2.Lerp(foot, top, 0.55f).y);
                ring[i, 2] = new Vector3(top.x, peak, top.y);
                ring[i, 3] = new Vector3(back.x, peak * 0.5f, back.y);
            }
            var mountains = new MeshBuilder();
            for (int i = 0; i < Steps; i++)
                for (int k = 0; k < 3; k++)
                    mountains.Quad(ring[i, k], ring[i + 1, k], ring[i + 1, k + 1], ring[i, k + 1]);
            mountains.Build(root, "Mountains", c.P.Lit(new Color(0.33f, 0.4f, 0.46f), 0f), collider: false);
        }
    }
}
