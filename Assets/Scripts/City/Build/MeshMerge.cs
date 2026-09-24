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
                // Only a mesh and its renderer: anything solid, animated or scripted stays as it is.
                if (go.GetComponents<Component>().Length != 3 || Scripted(go.transform, root)) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                if (m.determinant < 0f) continue; // mirrored: combining would turn its faces inside out
                Material[] mats = mr.sharedMaterials;
                Vector3 at = mr.bounds.center;
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
                var mesh = new Mesh { name = "Merged " + material.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true, false);
                var go = new GameObject(mesh.name) { layer = layer };
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = shadows;
            }
            // Gone now: the object if it was only this, or just its mesh if things hang off it.
            foreach (GameObject go in merged)
            {
                if (go.transform.childCount == 0) Object.DestroyImmediate(go);
                else
                {
                    Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(go.GetComponent<MeshFilter>());
                }
            }
            return merged.Count;
        }

        /// <summary>A script anywhere between the object and the merge root (it may move or restyle its children).</summary>
        private static bool Scripted(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
                if (p.GetComponent<MonoBehaviour>() != null) return true;
            return false;
        }
    }
}
