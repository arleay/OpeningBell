using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// D3D12 uploads a mesh's vertex and index buffers through a staging buffer that tops out near 35 MB: a bigger
    /// buffer ("upload buffer was too small for the requested resource") takes the editor and the game down the first
    /// time the mesh is drawn. Every mesh in the built city has to stay under the budget.
    /// </summary>
    public class MeshBudgetTests : SceneTestBase
    {
        private const long Budget = 32L * 1024 * 1024;

        [UnityTest]
        public IEnumerator EveryMeshInTheCity_FitsTheUploadBuffer()
        {
            yield return LoadMain();
            var sizes = new Dictionary<Mesh, (long Vertices, long Indices, string Where)>();
            foreach (MeshFilter f in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
                if (f.sharedMesh != null) sizes[f.sharedMesh] = Size(f.sharedMesh, Path(f.transform));
            foreach (SkinnedMeshRenderer s in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include))
                if (s.sharedMesh != null) sizes[s.sharedMesh] = Size(s.sharedMesh, Path(s.transform));

            var largest = sizes.OrderByDescending(kv => System.Math.Max(kv.Value.Vertices, kv.Value.Indices)).Take(10).ToList();
            foreach (var kv in largest)
                Debug.Log($"MeshBudget: {kv.Key.name} vertices {kv.Value.Vertices / 1048576f:0.0} MB, indices {kv.Value.Indices / 1048576f:0.0} MB at {kv.Value.Where}");
            var over = sizes.Where(kv => kv.Value.Vertices > Budget || kv.Value.Indices > Budget)
                .Select(kv => $"{kv.Key.name} ({System.Math.Max(kv.Value.Vertices, kv.Value.Indices) / 1048576f:0.0} MB) at {kv.Value.Where}").ToList();
            Assert.IsEmpty(over, "meshes over the D3D12 upload budget:\n" + string.Join("\n", over));
        }

        private static (long, long, string) Size(Mesh mesh, string where)
        {
            long vertices = 0;
            for (int s = 0; s < mesh.vertexBufferCount; s++) vertices = System.Math.Max(vertices, (long)mesh.GetVertexBufferStride(s) * mesh.vertexCount);
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indices += (long)mesh.GetIndexCount(s) * (mesh.indexFormat == IndexFormat.UInt32 ? 4 : 2);
            return (vertices, indices, where);
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (Transform u = t.parent; u != null; u = u.parent) p = u.name + "/" + p;
            return p;
        }
    }
}
