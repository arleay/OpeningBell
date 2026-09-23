using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>A flat building pad: the terrain is levelled to <see cref="Y"/> under it and blended out around it.</summary>
    public readonly struct Pad
    {
        public readonly Vector2 Center;
        public readonly Vector2 Half;   // half width (local x), half depth (local z)
        public readonly float Yaw;      // degrees
        public readonly float Y;

        public Pad(Vector2 center, Vector2 half, float yaw, float y)
        {
            Center = center;
            Half = half;
            Yaw = yaw;
            Y = y;
        }

        public static Pad FromRect(Rect r, float y) => new Pad(r.center, r.size * 0.5f, 0f, y);
    }

    /// <summary>
    /// The ground (TOWN_SPEC A3): a Unity terrain from <see cref="TownTerrain"/>, cut level under every street
    /// (sunk a little under the road and sidewalk meshes), alley, dirt road and building pad, blended back into the
    /// hills around them, and painted grass, forest floor, gravel, sand and rock. Bridges keep the water under them.
    /// </summary>
    public static class TerrainBuilder
    {
        public const int Resolution = 1025;
        private const float RoadBlend = 14f, PadBlend = 10f;

        public static Terrain Build(Transform parent, StreetMap map, IReadOnlyList<Pad> pads)
        {
            Rect e = TownTerrain.Extent;
            int res = Resolution;
            float dx = e.width / (res - 1), dz = e.height / (res - 1);
            var height = new float[res, res];
            var priority = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                height[z, x] = TownTerrain.Height(e.xMin + x * dx, e.yMin + z * dz);
                priority[z, x] = float.MaxValue;
            }

            // Priority: road cores beat pad cores beat road blends beat pad blends beat nature.
            void Offer(int x, int z, float p, float h)
            {
                if (p >= priority[z, x]) return;
                priority[z, x] = p;
                height[z, x] = h;
            }

            foreach (StreetMap.Segment s in map.Segments)
            {
                float core = s.HalfWidth + s.Sidewalk;
                float reach = core + RoadBlend;
                Bounds2(s.A.P, s.B.P, reach, e, dx, dz, res, out int x0, out int x1, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(e.xMin + x * dx, e.yMin + z * dz);
                    float t = Vector2.Dot(p - s.A.P, s.Dir);
                    if (t < -core || t > s.Length + core) continue;
                    if (s.IsBridge && t > s.BridgeFrom + 2f && t < s.BridgeTo - 2f) continue; // the canal stays under the deck
                    if (CityPlan.InTunnel(p, 0.5f)) continue; // the hill stays over the tunnel
                    float d = Mathf.Abs(Vector2.Dot(p - s.A.P, s.Left));
                    if (t < 0f) d = Mathf.Max(d, -t * 0.5f);
                    else if (t > s.Length) d = Mathf.Max(d, (t - s.Length) * 0.5f);
                    if (d > reach) continue;
                    float g = s.GradeAt(Mathf.Clamp(t, 0f, s.Length));
                    // Under the road meshes: sunk; under the sidewalk's outer part: just below its top.
                    float kerbside = s.Sidewalk > 0f ? g - 0.02f : g + CityPlan.RoadY - 0.03f;
                    if (d < s.HalfWidth + s.Sidewalk - 1.6f) Offer(x, z, -2000f + d, g - 0.3f);
                    else if (d < core + 0.4f) Offer(x, z, -2000f + d, kerbside);
                    else
                    {
                        float k = Smooth((d - core - 0.4f) / RoadBlend);
                        Offer(x, z, d - core, Mathf.Lerp(kerbside, height[z, x], k));
                    }
                }
            }

            // Alleys and dirt roads follow a smoothed line between their end grades.
            foreach (StreetDef drive in map.Driveways)
            {
                float hw = CityPlan.HalfWidth(drive.Class);
                float[] grade = DrivewayGrades(map, drive);
                for (int i = 0; i + 1 < drive.Points.Length; i++)
                {
                    Vector2 a = drive.Points[i], b = drive.Points[i + 1];
                    Vector2 dir = (b - a).normalized, left = new Vector2(-dir.y, dir.x);
                    float length = Vector2.Distance(a, b), reach = hw + 8f;
                    Bounds2(a, b, reach, e, dx, dz, res, out int x0, out int x1, out int z0, out int z1);
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 p = new Vector2(e.xMin + x * dx, e.yMin + z * dz);
                        float t = Vector2.Dot(p - a, dir);
                        if (t < -hw || t > length + hw) continue;
                        float d = Mathf.Abs(Vector2.Dot(p - a, left));
                        if (d > reach) continue;
                        float g = Mathf.Lerp(grade[i], grade[i + 1], Mathf.Clamp01(t / length));
                        if (d < hw + 0.5f) Offer(x, z, -1500f + d, g - 0.04f);
                        else Offer(x, z, 500f + d, Mathf.Lerp(g - 0.04f, height[z, x], Smooth((d - hw - 0.5f) / 8f)));
                    }
                }
            }

            foreach (Pad pad in pads)
            {
                float reach = Mathf.Max(pad.Half.x, pad.Half.y) * 1.5f + PadBlend;
                Bounds2(pad.Center, pad.Center, reach, e, dx, dz, res, out int x0, out int x1, out int z0, out int z1);
                Quaternion inverse = Quaternion.Euler(0f, -pad.Yaw, 0f);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(e.xMin + x * dx, e.yMin + z * dz) - pad.Center;
                    Vector3 local = inverse * new Vector3(p.x, 0f, p.y);
                    float ox = Mathf.Abs(local.x) - pad.Half.x, oz = Mathf.Abs(local.z) - pad.Half.y;
                    float outside = Mathf.Max(ox, oz);
                    if (outside <= 0f) Offer(x, z, -1000f + outside, pad.Y - 0.03f);
                    else if (outside < PadBlend) Offer(x, z, 1000f + outside, Mathf.Lerp(pad.Y - 0.03f, height[z, x], Smooth(outside / PadBlend)));
                }
            }

            // The railway: wherever the hills rise towards the tunnels, cut the ground down under the viaduct.
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = e.xMin + x * dx, wz = e.yMin + z * dz;
                if (wx < CityPlan.RailWest - 2f || wx > CityPlan.RailEast + 2f) continue;
                float d = Mathf.Abs(wz - CityPlan.RailZ);
                if (d > 22f) continue;
                float cap = CityPlan.RailDeck(wx) - 1.6f;
                float allowed = d < 7f ? cap : cap + (d - 7f) * 1.2f; // a cutting with sloped sides
                if (height[z, x] > allowed) height[z, x] = allowed;
            }

            var data = new TerrainData { heightmapResolution = res, alphamapResolution = 512 };
            data.size = new Vector3(e.width, TownTerrain.Span, e.height);
            var normalized = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) normalized[z, x] = Mathf.Clamp01((height[z, x] - TownTerrain.Base) / TownTerrain.Span);
            data.SetHeights(0, 0, normalized);
            Holes(data, height, e, dx, dz);
            Paint(data, map);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(e.xMin, TownTerrain.Base, e.yMin);
            var terrain = go.GetComponent<Terrain>();
            var material = Resources.Load<Material>("Materials/TerrainLit");
            if (material != null) terrain.materialTemplate = material;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 250f;
            terrain.drawInstanced = true;
            go.AddComponent<SurfaceTag>().Roughness = 1f;
            return terrain;
        }

        /// <summary>Opens the hillside where a tunnel tube passes through the ground (the tube's own mesh shows there).</summary>
        private static void Holes(TerrainData data, float[,] height, Rect e, float dx, float dz)
        {
            int res = data.holesResolution;
            var solid = new bool[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                solid[z, x] = true;
                var p = new Vector2(e.xMin + (x + 0.5f) * dx, e.yMin + (z + 0.5f) * dz);
                foreach (CityPlan.TunnelDef t in CityPlan.Tunnels)
                {
                    if (!t.Contains(p)) continue;
                    float ground = Mathf.Max(height[z, x], height[Mathf.Min(z + 1, res), Mathf.Min(x + 1, res)]);
                    if (ground < t.Floor + t.Height + 2f) solid[z, x] = false;
                }
            }
            data.SetHoles(0, 0, solid);
        }

        /// <summary>Grades for a driveway's points: its ends meet the street grade (or the ground), the middle follows the land.</summary>
        public static float[] DrivewayGrades(StreetMap map, StreetDef drive)
        {
            var g = new float[drive.Points.Length];
            for (int i = 0; i < g.Length; i++)
            {
                Vector2 p = drive.Points[i];
                bool end = i == 0 || i == g.Length - 1;
                g[i] = end && map.DistanceToStreet(p) < 1f ? map.StreetGrade(p) : TownTerrain.Natural(p.x, p.y);
            }
            return g;
        }

        private static void Bounds2(Vector2 a, Vector2 b, float pad, Rect e, float dx, float dz, int res, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - pad - e.xMin) / dx), 0, res - 1);
            x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + pad - e.xMin) / dx), 0, res - 1);
            z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - pad - e.yMin) / dz), 0, res - 1);
            z1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y, b.y) + pad - e.yMin) / dz), 0, res - 1);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ---- painting ----

        private enum Layer { Grass, Forest, Gravel, Sand, Rock, Mud }

        private static void Paint(TerrainData data, StreetMap map)
        {
            data.terrainLayers = new[]
            {
                // Damp north-west greens: muted, a little olive, never lawn-bright.
                MakeLayer("grass", new Color(0.29f, 0.36f, 0.21f), new Color(0.22f, 0.29f, 0.17f), 8f),
                MakeLayer("forest", new Color(0.22f, 0.24f, 0.16f), new Color(0.16f, 0.18f, 0.12f), 6f),
                MakeLayer("gravel", new Color(0.45f, 0.43f, 0.4f), new Color(0.36f, 0.35f, 0.33f), 4f),
                MakeLayer("sand", new Color(0.66f, 0.6f, 0.48f), new Color(0.58f, 0.53f, 0.43f), 6f),
                MakeLayer("rock", new Color(0.33f, 0.32f, 0.3f), new Color(0.25f, 0.25f, 0.25f), 10f),
                MakeLayer("mud", new Color(0.31f, 0.27f, 0.22f), new Color(0.25f, 0.22f, 0.18f), 5f),
            };
            int res = data.alphamapResolution;
            var maps = new float[res, res, 6];
            Rect e = TownTerrain.Extent;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res, v = (z + 0.5f) / res;
                float wx = e.xMin + u * e.width, wz = e.yMin + v * e.height;
                float h = data.GetInterpolatedHeight(u, v) + TownTerrain.Base;
                float steep = data.GetSteepness(u, v);
                var w = new float[6];
                float wild = TownTerrain.Wildness(wx, wz);
                float noise = Mathf.PerlinNoise(wx * 0.05f, wz * 0.05f);
                w[(int)Layer.Grass] = 1f - wild;
                w[(int)Layer.Forest] = wild * (0.6f + 0.4f * noise);
                // The industrial south-west is gravel and packed dirt.
                float industrial = Smooth((-wx - 255f) / 20f) * Smooth((-wz - 62f) / 10f) * (1f - wild);
                w[(int)Layer.Gravel] = industrial * 1.4f;
                w[(int)Layer.Grass] *= 1f - industrial;
                // Beaches and river mud near the water; rock on steep slopes and under water.
                if (h < 0.6f && h > -6f) w[(int)Layer.Sand] = 2f * Smooth((0.6f - h) / 1.5f) * (wz < 0f ? 1f : 0f);
                if (Mathf.Abs(wz - TownTerrain.River(wx)) < TownTerrain.RiverHalfWidth + 12f || TownTerrain.InCanal(wx, wz)) w[(int)Layer.Mud] = 2.5f;
                w[(int)Layer.Rock] = Smooth((steep - 36f) / 14f) * 4f;
                float sum = 0f;
                foreach (float f in w) sum += f;
                for (int l = 0; l < 6; l++) maps[z, x, l] = w[l] / Mathf.Max(1e-4f, sum);
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>A tiling noise texture between two colours (no texture assets needed).</summary>
        private static TerrainLayer MakeLayer(string name, Color a, Color b, float tile)
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Tileable: sample noise on a torus.
                float fx = x / (float)size * Mathf.PI * 2f, fy = y / (float)size * Mathf.PI * 2f;
                float n = Mathf.PerlinNoise(Mathf.Cos(fx) * 2f + 10f, Mathf.Sin(fx) * 2f + Mathf.Cos(fy) * 2f + 20f) * 0.6f
                        + Mathf.PerlinNoise(Mathf.Sin(fy) * 6f + 30f, Mathf.Cos(fx) * 6f + 40f) * 0.4f;
                // Fine speckle over the soft blotches; low contrast so the tiling doesn't show.
                float fine = Mathf.Abs(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f) % 1f * 0.25f; // per-pixel, so it tiles
                Color32 px = Color.Lerp(a, b, Mathf.Clamp01(0.5f + (n - 0.5f) * 0.55f + fine - 0.12f));
                // URP's terrain shader takes smoothness from the diffuse alpha when a layer has no mask map: keep it
                // matte, or the ground mirrors the sky (pale by day, glowing blue-white at night).
                px.a = 10;
                pixels[y * size + x] = px;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "terrain-" + name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return new TerrainLayer { name = name, diffuseTexture = texture, tileSize = new Vector2(tile, tile), smoothness = 0.05f };
        }
    }
}
