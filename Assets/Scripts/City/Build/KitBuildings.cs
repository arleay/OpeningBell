using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Dresses a <see cref="Shell"/> footprint with Kenney City Kit (Commercial) buildings: a row of them along the
    /// street front, more rows behind on deep lots, each picked to roughly match the shell's height and stretched
    /// a little so the row fills the frontage exactly. One scale for the whole kit keeps storeys and windows the
    /// same size on every street; the facade style picks the palette (colour variant, tint).
    /// </summary>
    public static class KitBuildings
    {
        /// <summary>
        /// Metres per kit unit. The kit's road tile is one unit for a lane pair, so ~7.4 m makes 3.7 m lanes and
        /// 3.1–3.3 m storeys.
        /// </summary>
        public const float Scale = 7.4f;

        /// <summary>The kit models' street side faces local +z.</summary>
        private const float ModelYaw = 180f;

        private static readonly string[] Low =
        {
            "building-a", "building-b", "building-c", "building-d", "building-e", "building-f", "building-g",
            "building-h", "building-i", "building-j", "building-k",
        };

        private static readonly string[] Mid = { "building-l", "building-m", "building-n", "building-skyscraper-a" };

        private static readonly string[] Towers =
        {
            "building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c", "building-skyscraper-d", "building-skyscraper-e",
        };

        private static readonly Dictionary<GameObject, Bounds> Sizes = new Dictionary<GameObject, Bounds>();

        /// <summary>Builds the row(s); false (nothing built) when the kit isn't in the art library.</summary>
        public static bool Build(CityContext c, Transform parent, Shell s, int seed, out float tallest)
        {
            tallest = 0f;
            CityArt art = c.Kit.Art;
            if (art == null || art.Model("building-a") == null) return false;
            Material material = Material(c, art, s.Style, seed);
            var rng = new System.Random(seed);

            Rect f = s.Footprint;
            Vector2 outward = ShellBuilder.FrontDirection(f, out Vector2 frontCentre);
            Vector2 along = new Vector2(outward.y, -outward.x);
            float frontage = Mathf.Abs(Vector2.Dot(along, f.size));
            float depth = Mathf.Abs(Vector2.Dot(outward, f.size));
            float yaw = Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg + ModelYaw;
            string[] pool = s.Height >= 26f ? Towers : s.Height >= 15f ? Mid : Low;

            // Kit buildings are 7–13 m deep: deep lots get several rows rather than one long stretched block.
            int rows = Mathf.Max(1, Mathf.CeilToInt(depth / 12f));
            float rowDepth = depth / rows;
            for (int r = 0; r < rows; r++)
            {
                // Back rows run a little taller or shorter than the front: a skyline, not a wall.
                float target = s.Height * (r == 0 ? 1f : 0.8f + 0.5f * (float)rng.NextDouble());
                var picks = new List<(GameObject Prefab, Bounds Size)>();
                float used = 0f;
                while (used < frontage - 2.5f)
                {
                    GameObject prefab = Pick(art, pool, target, rng, out Bounds size);
                    picks.Add((prefab, size));
                    used += size.size.x * Scale;
                }
                // Overshot: drop the last one if squeezing the others less than stretching them would.
                float last = picks[picks.Count - 1].Size.size.x * Scale;
                if (picks.Count > 1 && frontage / (used - last) < used / frontage)
                {
                    picks.RemoveAt(picks.Count - 1);
                    used -= last;
                }
                float stretch = frontage / used;
                float at = -frontage / 2f;
                foreach (var (prefab, size) in picks)
                {
                    float w = size.size.x * Scale * stretch;
                    float height = Mathf.Clamp(target / (size.size.y * Scale), 0.8f, 1.25f) * Scale * size.size.y;
                    tallest = Mathf.Max(tallest, height);
                    Vector2 centre = frontCentre + along * (at + w / 2f) - outward * ((r + 0.5f) * rowDepth);
                    at += w;
                    var scale = new Vector3(w / size.size.x, height / size.size.y, rowDepth / size.size.z);
                    GameObject go = Object.Instantiate(prefab, parent, false);
                    go.name = prefab.name;
                    Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                    // Kit pivots sit near, not exactly at, the footprint centre.
                    Vector3 offset = rotation * Vector3.Scale(new Vector3(size.center.x, 0f, size.center.z), scale);
                    go.transform.localPosition = new Vector3(centre.x, 0f, centre.y) - offset;
                    go.transform.localRotation = rotation;
                    go.transform.localScale = scale;
                    foreach (Renderer rend in go.GetComponentsInChildren<Renderer>()) rend.sharedMaterial = material;
                }
            }
            return true;
        }

        /// <summary>A model near <paramref name="height"/> metres tall (at kit scale), random among the close ones.</summary>
        private static GameObject Pick(CityArt art, string[] pool, float height, System.Random rng, out Bounds size)
        {
            var close = new List<GameObject>();
            GameObject best = null;
            float bestError = float.MaxValue;
            foreach (string name in pool)
            {
                GameObject prefab = art.Model(name);
                if (prefab == null) continue;
                float h = Measure(prefab).size.y * Scale;
                float error = Mathf.Abs(Mathf.Log(h / height));
                if (error < 0.28f) close.Add(prefab);
                if (error < bestError)
                {
                    bestError = error;
                    best = prefab;
                }
            }
            GameObject pick = close.Count > 0 ? close[rng.Next(close.Count)] : best;
            size = Measure(pick);
            return pick;
        }

        /// <summary>Mesh bounds of a prefab in its own space (no instance needed).</summary>
        public static Bounds Measure(GameObject prefab)
        {
            if (Sizes.TryGetValue(prefab, out Bounds b)) return b;
            bool first = true;
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds local = mf.sharedMesh.bounds;
                Matrix4x4 m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (first)
                    {
                        b = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else b.Encapsulate(p);
                }
            }
            return Sizes[prefab] = b;
        }

        /// <summary>Palette per facade style: brick reads warm (the kit's salmon variant), stucco cream, the rest the kit's own greys.</summary>
        private static Material Material(CityContext c, CityArt art, FacadeStyle style, int seed)
        {
            string variant;
            Color tint = Color.white;
            switch (style)
            {
                case FacadeStyle.Brick:
                    variant = "variation-a";
                    tint = seed % 2 == 0 ? Color.white : new Color(0.9f, 0.82f, 0.78f);
                    break;
                case FacadeStyle.Stucco:
                    variant = "colormap";
                    tint = new Color(1f, 0.94f, 0.82f);
                    break;
                case FacadeStyle.Townhouse:
                    variant = "variation-b";
                    break;
                default:
                    variant = seed % 3 == 0 ? "variation-b" : "colormap";
                    break;
            }
            Texture2D palette = art.Palette("CityCommercial/" + variant) ?? art.Palette("CityCommercial/colormap");
            Texture2D windows = art.Palette("CityCommercial/" + variant + "-glow");
            return c.P.KitPalette(palette, windows, tint);
        }
    }
}
