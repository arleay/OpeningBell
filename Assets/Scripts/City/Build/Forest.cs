using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpeningBell.City
{
    /// <summary>
    /// The woods (TOWN_SPEC A9): mostly evergreens, thick in the hills round the town and on the river's banks,
    /// thinning into the town's empty ground (so no bare lawns between neighbourhoods), never on streets, lots,
    /// water or anything built. Thousands of trees, so they're drawn with GPU instancing in 100 m chunks, culled
    /// by the camera and by distance, shadowed only up close. Trunks are real capsule colliders.
    /// </summary>
    public sealed class Forest : MonoBehaviour
    {
        private sealed class Part
        {
            public Mesh Mesh;
            /// <summary>The mesh at each LOD level (see <see cref="Lod"/>), made on first use.</summary>
            public readonly Mesh[] Levels = new Mesh[LodDistances.Length + 1];
            public int Submesh;
            public Material Material;
            public Matrix4x4 Local;
        }

        private sealed class Chunk
        {
            public Bounds Bounds;
            public readonly List<Matrix4x4>[] ByKind;
            public Chunk(int kinds)
            {
                ByKind = new List<Matrix4x4>[kinds];
                for (int i = 0; i < kinds; i++) ByKind[i] = new List<Matrix4x4>();
            }
        }

        private const float ChunkSize = 100f, DrawDistance = 900f, ShadowDistance = 180f;

        /// <summary>
        /// Chunk distances at which trees drop a mesh-LOD level (each level has half the triangles). Full detail within
        /// 80 m; beyond 600 m a sixteenth. The forest was ~50 million triangles a frame drawn at full detail to 900 m.
        /// </summary>
        private static readonly float[] LodDistances = { 80f, 170f, 320f, 600f };

        private static int Lod(float distance)
        {
            int level = 0;
            while (level < LodDistances.Length && distance > LodDistances[level]) level++;
            return level;
        }
        private readonly List<List<Part>> _kinds = new List<List<Part>>();
        private readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Plane[] _planes = new Plane[6];
        private readonly Matrix4x4[] _batch = new Matrix4x4[1023];
        private Camera _camera;

        public int TreeCount { get; private set; }

        private static readonly (string Model, float MinH, float MaxH, float Weight)[] Kinds =
        {
            ("q_pine_a", 14f, 24f, 3f), ("q_pine_b", 12f, 22f, 3f), ("q_pine_c", 10f, 20f, 2.5f),
            ("q_tree_a", 9f, 15f, 0.8f), ("q_tree_b", 9f, 14f, 0.6f), ("q_tree_common", 8f, 13f, 0.6f),
        };

        public static Forest Build(CityContext c, Camera camera)
        {
            if (c.Kit.Art == null || c.Kit.Art.Model(Kinds[0].Model) == null) return null;
            var forest = new GameObject("Forest").AddComponent<Forest>();
            forest.transform.SetParent(c.Dynamic, false);
            forest._camera = camera;
            forest.Plant(c);
            return forest;
        }

        private void Plant(CityContext c)
        {
            var usable = new List<int>();
            for (int i = 0; i < Kinds.Length; i++)
            {
                List<Part> parts = Parts(c, c.Kit.Art.Model(Kinds[i].Model));
                _kinds.Add(parts);
                if (parts.Count > 0) usable.Add(i);
            }
            if (usable.Count == 0) return;
            float totalWeight = 0f;
            foreach (int i in usable) totalWeight += Kinds[i].Weight;

            Physics.SyncTransforms();
            StreetMap map = c.Roads.Map;
            Transform trunks = Kit.Group(transform, "Trunks");
            var rng = new System.Random(5150);
            var hits = new Collider[8];
            Rect e = CityPlan.World;
            const float spacing = 6.5f;
            for (float x = e.xMin + 4f; x < e.xMax - 4f; x += spacing)
            for (float z = e.yMin + 4f; z < e.yMax - 4f; z += spacing)
            {
                float px = x + (float)(rng.NextDouble() - 0.5) * spacing * 0.9f, pz = z + (float)(rng.NextDouble() - 0.5) * spacing * 0.9f;
                double roll = rng.NextDouble();
                float wild = TownTerrain.Wildness(px, pz);
                // Dense in the wild; groves (clumps from noise) on the town's leftover ground.
                float grove = Mathf.Clamp01((Mathf.PerlinNoise(px * 0.018f + 3.1f, pz * 0.018f + 7.7f) - 0.42f) / 0.25f);
                double density = wild > 0.4f ? 0.55 + 0.35 * wild : 0.04 + 0.5 * grove;
                if (roll > density) continue;
                if (TownTerrain.IsWater(px, pz, out _) || TownTerrain.Height(px, pz) < TownTerrain.SeaLevel + 0.6f) continue;
                if (CityPlan.InTunnel(new Vector2(px, pz), 6f)) continue;
                if (px > CityPlan.RailWest - 5f && px < CityPlan.RailEast + 5f && Mathf.Abs(pz - CityPlan.RailZ) < 9f) continue;
                StreetMap.Segment s = map.Nearest(new Vector2(px, pz), out float along, out float lateral);
                if (s != null && Mathf.Abs(lateral) < s.HalfWidth + s.Sidewalk + (wild > 0.4f ? 3f : 7f) && along > -8f && along < s.Length + 8f) continue;
                if (NearDriveway(map, new Vector2(px, pz), 5f)) continue;
                if (OnPad(c, new Vector2(px, pz))) continue;
                float y = c.GroundY(px, pz);
                if (Physics.OverlapSphereNonAlloc(new Vector3(px, y + 2f, pz), 2.6f, hits, ~0, QueryTriggerInteraction.Ignore) > CountTerrain(hits)) continue;

                int kind = Pick(usable, totalWeight, rng.NextDouble());
                float height = Mathf.Lerp(Kinds[kind].MinH, Kinds[kind].MaxH, (float)rng.NextDouble()) * (wild > 0.4f ? 1f : 0.75f);
                var trs = Matrix4x4.TRS(new Vector3(px, y - 0.1f, pz), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * height);
                Add(kind, trs, new Vector3(px, y, pz), height);
                var trunk = new GameObject("Trunk").AddComponent<CapsuleCollider>();
                trunk.transform.SetParent(trunks, false);
                trunk.transform.position = new Vector3(px, y + 1.5f, pz);
                trunk.radius = 0.25f + height * 0.012f;
                trunk.height = 3f;
                TreeCount++;
            }
        }

        private static int CountTerrain(Collider[] hits)
        {
            int n = 0;
            foreach (Collider h in hits)
                if (h is TerrainCollider) n++;
            return n;
        }

        private static bool NearDriveway(StreetMap map, Vector2 p, float margin)
        {
            foreach (StreetDef d in map.Driveways)
            {
                float hw = CityPlan.HalfWidth(d.Class) + margin;
                for (int i = 0; i + 1 < d.Points.Length; i++)
                {
                    Vector2 a = d.Points[i], b = d.Points[i + 1], ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    if ((a + ab * t - p).sqrMagnitude < hw * hw) return true;
                }
            }
            return false;
        }

        private static bool OnPad(CityContext c, Vector2 p)
        {
            foreach (Pad pad in c.Pads)
            {
                Vector2 d = p - pad.Center;
                if (d.sqrMagnitude > (pad.Half.sqrMagnitude + 64f)) continue;
                Vector3 local = Quaternion.Euler(0f, -pad.Yaw, 0f) * new Vector3(d.x, 0f, d.y);
                if (Mathf.Abs(local.x) < pad.Half.x + 3f && Mathf.Abs(local.z) < pad.Half.y + 3f) return true;
            }
            return false;
        }

        private static int Pick(List<int> usable, float total, double roll)
        {
            float r = (float)roll * total;
            foreach (int i in usable)
            {
                r -= Kinds[i].Weight;
                if (r <= 0f) return i;
            }
            return usable[usable.Count - 1];
        }

        /// <summary>Every mesh/submesh/material under the model, with its transform relative to the model's root.</summary>
        private static List<Part> Parts(CityContext c, GameObject prefab)
        {
            var parts = new List<Part>();
            if (prefab == null) return parts;
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            // The kit's rotation convention (Kit.Model keeps the prefab's own root rotation, then yaws it).
            Matrix4x4 rootRotation = Matrix4x4.Rotate(prefab.transform.localRotation);
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || mf.sharedMesh == null) continue;
                Matrix4x4 local = rootRotation * rootInverse * mf.transform.localToWorldMatrix;
                Material[] mats = r.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++)
                {
                    Material m = mats[s];
                    if (m == null) continue;
                    m = Instanced(m);
                    parts.Add(new Part { Mesh = mf.sharedMesh, Submesh = s, Material = m, Local = local });
                }
            }
            return parts;
        }

        private static readonly Dictionary<Material, Material> InstancedCopies = new Dictionary<Material, Material>();

        private static Material Instanced(Material m)
        {
            if (m.enableInstancing) return m;
            if (!InstancedCopies.TryGetValue(m, out Material copy) || copy == null)
                InstancedCopies[m] = copy = new Material(m) { name = m.name + " (instanced)", enableInstancing = true };
            return copy;
        }

        private void Add(int kind, Matrix4x4 trs, Vector3 at, float height)
        {
            var key = new Vector2Int(Mathf.FloorToInt(at.x / ChunkSize), Mathf.FloorToInt(at.z / ChunkSize));
            if (!_chunks.TryGetValue(key, out Chunk chunk))
            {
                _chunks[key] = chunk = new Chunk(Kinds.Length);
                chunk.Bounds = new Bounds(at, Vector3.one);
            }
            chunk.ByKind[kind].Add(trs);
            chunk.Bounds.Encapsulate(new Bounds(at + Vector3.up * height / 2f, new Vector3(height * 0.6f, height, height * 0.6f)));
        }

        private void LateUpdate()
        {
            Camera cam = _camera != null && _camera.isActiveAndEnabled ? _camera : Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            Vector3 eye = cam.transform.position;
            foreach (Chunk chunk in _chunks.Values)
            {
                float distance = Mathf.Sqrt(chunk.Bounds.SqrDistance(eye));
                if (distance > DrawDistance || !GeometryUtility.TestPlanesAABB(_planes, chunk.Bounds)) continue;
                ShadowCastingMode shadows = distance < ShadowDistance ? ShadowCastingMode.On : ShadowCastingMode.Off;
                int lod = Lod(distance);
                for (int kind = 0; kind < _kinds.Count; kind++)
                {
                    List<Matrix4x4> trees = chunk.ByKind[kind];
                    if (trees.Count == 0) continue;
                    foreach (Part part in _kinds[kind])
                    {
                        var rp = new RenderParams(part.Material) { shadowCastingMode = shadows, receiveShadows = true, worldBounds = chunk.Bounds };
                        Mesh mesh = part.Levels[lod] ??= MeshMerge.Level(part.Mesh, lod);
                        for (int start = 0; start < trees.Count; start += _batch.Length)
                        {
                            int n = Mathf.Min(_batch.Length, trees.Count - start);
                            for (int i = 0; i < n; i++) _batch[i] = trees[start + i] * part.Local;
                            Graphics.RenderMeshInstanced(rp, mesh, part.Submesh, _batch, n);
                        }
                    }
                }
            }
        }
    }
}
