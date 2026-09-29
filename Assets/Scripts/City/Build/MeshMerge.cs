using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpeningBell.City
{
    /// <summary>
    /// Folds the plain renderers under a group (shelf goods, yard and street clutter) into one mesh per material,
    /// layer, shadow mode and <c>cell</c>-metre square (TOWN_SPEC A17). Static batching already saves draw calls; this
    /// saves the per-renderer culling a supermarket's nine thousand tins would otherwise cost every frame.
    /// </summary>
    public static class MeshMerge
    {
        /// <summary>Merges what it can under <paramref name="root"/>; returns how many renderers went.</summary>
        /// <summary>Vertices per merged mesh: 250k at up to ~100 bytes each stays well inside the D3D12 upload buffer.</summary>
        public const int MaxVertices = 250_000;

        public static int Merge(Transform root, float cell = 40f)
        {
            var groups = new Dictionary<(Material, int, ShadowCastingMode, int, int), List<CombineInstance>>();
            var merged = new List<GameObject>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {
                GameObject go = mf.gameObject;
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable || !go.TryGetComponent(out MeshRenderer mr) || !mr.enabled) continue;
                // A mesh and its renderer, perhaps a collider and a surface tag (those stay behind on the object, invisible):
                // anything animated or scripted stays as it is.
                if (!Plain(go) || Scripted(go.transform, root)) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                if (m.determinant < 0f) continue; // mirrored: combining would turn its faces inside out
                Material[] mats = mr.sharedMaterials;
                Vector3 at = mr.bounds.center;
                // Dense scans come with mesh LODs; a merged mesh can't switch level, so it takes the lightest level
                // that still has enough triangles for the prop's size (see Budget).
                mesh = Reduced(mesh, mr.bounds.size);
                int cx = Mathf.FloorToInt(at.x / cell), cz = Mathf.FloorToInt(at.z / cell);
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null) continue;
                    var key = (mats[s], go.layer, mr.shadowCastingMode, cx, cz);
                    if (!groups.TryGetValue(key, out List<CombineInstance> list)) groups[key] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
                }
                merged.Add(go);
            }
            if (merged.Count < 2) return 0;

            foreach (var pair in groups)
            {
                var (material, layer, shadows, _, _) = pair.Key;
                // In pieces of at most MaxVertices: D3D12 uploads a mesh through a ~35 MB staging buffer and a bigger one
                // (a tyre shop's oil cans came to 76 MB) takes the game down the first time it's drawn.
                List<CombineInstance> all = pair.Value;
                for (int start = 0; start < all.Count;)
                {
                    int end = start, vertices = 0;
                    while (end < all.Count && (end == start || vertices + all[end].mesh.vertexCount <= MaxVertices)) vertices += all[end++].mesh.vertexCount;
                    var mesh = new Mesh { name = "Merged " + material.name, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(all.GetRange(start, end - start).ToArray(), true, true, false);
                    var go = new GameObject(mesh.name) { layer = layer };
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = material;
                    r.shadowCastingMode = shadows;
                    start = end;
                }
            }
            // Gone now: the object if it was only this, or just its mesh if things hang off it or it's also a collider.
            foreach (GameObject go in merged)
            {
                if (go.transform.childCount == 0 && go.GetComponents<Component>().Length == 3) Object.DestroyImmediate(go);
                else
                {
                    Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(go.GetComponent<MeshFilter>());
                }
            }
            return merged.Count;
        }

        /// <summary>
        /// Folds every plain renderer under <paramref name="root"/> into one renderer on the root, a submesh per material
        /// (a parked car: body, wheels, glass and lights, each with several materials, become one draw list). Returns how
        /// many renderers went; nothing happens when fewer than two would.
        /// </summary>
        public static int MergeIntoOne(Transform root)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var merged = new List<GameObject>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {
                GameObject go = mf.gameObject;
                Mesh mesh = mf.sharedMesh;
                if (go == root.gameObject || mesh == null || !mesh.isReadable || !go.TryGetComponent(out MeshRenderer mr) || !mr.enabled) continue;
                if (!Plain(go) || Scripted(go.transform, root)) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                if (m.determinant < 0f) continue;
                Material[] mats = mr.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null) continue;
                    if (!byMaterial.TryGetValue(mats[s], out List<CombineInstance> list)) byMaterial[mats[s]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
                }
                merged.Add(go);
            }
            if (merged.Count < 2 || root.GetComponent<MeshFilter>() != null) return 0;
            // One mesh per material, then those as the submeshes of the whole.
            var materials = new List<Material>();
            var parts = new List<CombineInstance>();
            foreach (var pair in byMaterial)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(pair.Value.ToArray(), true, true, false);
                parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                materials.Add(pair.Key);
            }
            var whole = new Mesh { name = root.name + " merged", indexFormat = IndexFormat.UInt32 };
            whole.CombineMeshes(parts.ToArray(), false, false, false);
            foreach (CombineInstance p in parts) Object.DestroyImmediate(p.mesh);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = whole;
            root.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            foreach (GameObject go in merged)
            {
                if (go.transform.childCount == 0 && go.GetComponents<Component>().Length == 3) Object.DestroyImmediate(go);
                else
                {
                    Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(go.GetComponent<MeshFilter>());
                }
            }
            return merged.Count;
        }

        /// <summary>
        /// Triangles a merged prop gets for its size: ~3000 per metre of its largest side, never under 600 (a 40 cm
        /// wrench keeps ~1200, a shelf of tins a few hundred each; the scans ship with 10–60 thousand).
        /// </summary>
        public static int Budget(Vector3 size) => Mathf.Max(600, Mathf.RoundToInt(3000f * Mathf.Max(size.x, Mathf.Max(size.y, size.z))));

        private static readonly Dictionary<(Mesh, int), Mesh> ReducedCache = new Dictionary<(Mesh, int), Mesh>();

        /// <summary>
        /// A copy of <paramref name="mesh"/> at its lightest mesh-LOD level whose triangle count still meets the size's
        /// <see cref="Budget"/> (each level halves the one before). The mesh itself when it has no LODs.
        /// Imported LODs keep every level in the submesh's index range; <see cref="Mesh.GetLod"/> gives each level's slice.
        /// </summary>
        public static Mesh Reduced(Mesh mesh, Vector3 size)
        {
            if (mesh.lodCount < 2) return mesh;
            int budget = Budget(size);
            int level = 0;
            while (level + 1 < mesh.lodCount && Triangles(mesh, level + 1) >= budget) level++;
            return Level(mesh, level);
        }

        /// <summary>
        /// Mesh LOD level <paramref name="level"/> of an imported mesh as a mesh of its own (cached; clamped to the levels
        /// there are; the mesh itself when it has no LODs). Imported LODs keep every level in the
        /// submesh's index range, and <see cref="Mesh.GetLod"/> gives each level's slice of it.
        /// </summary>
        public static Mesh Level(Mesh mesh, int level)
        {
            // Level 0 too: a mesh with LODs keeps every level's triangles in each submesh's index range, so merging it
            // whole would draw the prop once per level.
            if (mesh.lodCount < 2) return mesh;
            level = Mathf.Clamp(level, 0, mesh.lodCount - 1);
            if (ReducedCache.TryGetValue((mesh, level), out Mesh cached) && cached != null) return cached;
            // A level's triangles use a fraction of the mesh's vertices (all levels share one vertex buffer): only those
            // are kept. Copying the lot made merged shelves carry every scan's full-detail vertices (76 MB of oil cans).
            var tris = new int[mesh.subMeshCount][];
            var remap = new Dictionary<int, int>();
            var used = new List<int>();
            using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                Mesh.MeshData md = data[0];
                bool wide = md.indexFormat == IndexFormat.UInt32;
                var shorts = wide ? default : md.GetIndexData<ushort>();
                var ints = wide ? md.GetIndexData<int>() : default;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    MeshLodRange range = mesh.GetLod(s, level);
                    int baseVertex = mesh.GetSubMesh(s).baseVertex;
                    tris[s] = new int[range.indexCount];
                    for (int i = 0; i < tris[s].Length; i++)
                    {
                        int v = (wide ? ints[(int)range.indexStart + i] : shorts[(int)range.indexStart + i]) + baseVertex;
                        if (!remap.TryGetValue(v, out int n))
                        {
                            remap[v] = n = used.Count;
                            used.Add(v);
                        }
                        tris[s][i] = n;
                    }
                }
            }
            var reduced = new Mesh { name = mesh.name + " lod" + level, indexFormat = used.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            reduced.SetVertices(Pick(mesh.vertices, used));
            reduced.SetNormals(Pick(mesh.normals, used));
            if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) reduced.SetTangents(Pick(mesh.tangents, used));
            if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color)) reduced.SetColors(Pick(mesh.colors32, used));
            var uv = new List<Vector2>();
            for (int ch = 0; ch < 2; ch++)
            {
                mesh.GetUVs(ch, uv);
                if (uv.Count > 0) reduced.SetUVs(ch, Pick(uv.ToArray(), used));
            }
            reduced.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) reduced.SetTriangles(tris[s], s, false);
            reduced.RecalculateBounds();
            return ReducedCache[(mesh, level)] = reduced;
        }

        private static T[] Pick<T>(T[] source, List<int> used)
        {
            var result = new T[used.Count];
            for (int i = 0; i < used.Count; i++) result[i] = source[used[i]];
            return result;
        }

        private static int Triangles(Mesh mesh, int level)
        {
            int n = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) n += (int)mesh.GetLod(s, level).indexCount / 3;
            return n;
        }

        /// <summary>Only a mesh, its renderer, colliders and passive tags: nothing that could move, animate or restyle it.</summary>
        private static bool Plain(GameObject go)
        {
            foreach (Component c in go.GetComponents<Component>())
                if (!(c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider || c is SurfaceTag)) return false;
            return true;
        }

        /// <summary>A script anywhere between the object and the merge root (it may move or restyle its children).</summary>
        private static bool Scripted(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
                foreach (MonoBehaviour b in p.GetComponents<MonoBehaviour>())
                    if (!(b is SurfaceTag)) return true;
            return false;
        }
    }
}
