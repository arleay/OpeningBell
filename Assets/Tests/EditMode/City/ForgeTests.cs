using NUnit.Framework;
using OpeningBell.City;
using UnityEngine;

namespace OpeningBell.Tests
{
    /// <summary>Jewellery geometry: every triangle faces the way its normals say, and nothing is degenerate.</summary>
    public class ForgeTests
    {
        [Test]
        public void Gem_FacetsFaceOut_AndAreWellFormed()
        {
            var f = new Forge(1);
            f.Gem(Vector3.zero, Vector3.up, 0.02f, 16);
            Mesh mesh = f.ToMesh("gem");
            Vector3[] v = mesh.vertices, n = mesh.normals;
            int[] t = mesh.triangles;
            int bad = 0, inward = 0;
            var why = new System.Text.StringBuilder();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                Vector3 normal = n[t[i]];
                if (face.sqrMagnitude < 1e-16f || float.IsNaN(normal.x)) { bad++; if (why.Length < 600) why.Append($"[{i / 3} degenerate {face.sqrMagnitude:E1} n {normal}] "); continue; }
                if (Vector3.Dot(face, normal) <= 0f) { bad++; if (why.Length < 600) why.Append($"[{i / 3} against] "); }
                // Out of the stone: away from the axis, up on the crown, down on the pavilion.
                Vector3 centroid = (a + b + c) / 3f;
                if (Vector3.Dot(normal, centroid) <= 0f) inward++;
            }
            Assert.AreEqual(0, bad, "degenerate or wound against its normal: " + why);
            Assert.AreEqual(0, inward, "facing into the stone");
            Assert.AreEqual(16 * 6 * 3, t.Length);
        }
    }
}
