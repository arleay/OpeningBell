using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpeningBell.City
{
    /// <summary>
    /// Accumulates triangles for one material and turns them into a single mesh (32-bit indices): the whole town's
    /// road surface, sidewalks or markings each become one draw call instead of thousands of boxes.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _t = new List<int>();

        public int VertexCount => _v.Count;

        /// <summary>A convex (or star-shaped around its first vertex) polygon, counter-clockwise seen from above.</summary>
        public void Polygon(IReadOnlyList<Vector3> points, Vector3 normal)
        {
            if (points.Count < 3) return;
            int b = _v.Count;
            foreach (Vector3 p in points)
            {
                _v.Add(p);
                _n.Add(normal);
                _uv.Add(new Vector2(p.x, p.z) * 0.25f);
            }
            for (int i = 1; i + 1 < points.Count; i++)
            {
                // Counter-clockwise from above → clockwise from below; Unity front faces are clockwise.
                _t.Add(b);
                _t.Add(b + i + 1);
                _t.Add(b + i);
            }
        }

        /// <summary>A fan around a centre point: for polygons that are only star-shaped around the centre.</summary>
        public void Fan(Vector3 centre, IReadOnlyList<Vector3> ring)
        {
            if (ring.Count < 2) return;
            int b = _v.Count;
            _v.Add(centre);
            _n.Add(Vector3.up);
            _uv.Add(new Vector2(centre.x, centre.z) * 0.25f);
            foreach (Vector3 p in ring)
            {
                _v.Add(p);
                _n.Add(Vector3.up);
                _uv.Add(new Vector2(p.x, p.z) * 0.25f);
            }
            for (int i = 0; i < ring.Count; i++)
            {
                _t.Add(b);
                _t.Add(b + 1 + (i + 1) % ring.Count);
                _t.Add(b + 1 + i);
            }
        }

        /// <summary>A four-corner patch (a, b, c, d counter-clockwise from above), normal computed.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 normal = Vector3.Cross(c - a, b - d).normalized;
            if (normal.sqrMagnitude < 0.5f) normal = Vector3.up;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) _n.Add(normal);
            _uv.Add(new Vector2(a.x + a.y, a.z) * 0.25f);
            _uv.Add(new Vector2(b.x + b.y, b.z) * 0.25f);
            _uv.Add(new Vector2(c.x + c.y, c.z) * 0.25f);
            _uv.Add(new Vector2(d.x + d.y, d.z) * 0.25f);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 1);
            _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
        }

        /// <summary>A vertical wall face from a (bottom) to b (bottom), height h, facing right of a→b (seen from above).</summary>
        public void Wall(Vector3 a, Vector3 b, float h)
        {
            Quad(a, b, b + Vector3.up * h, a + Vector3.up * h);
        }

        /// <summary>
        /// A vertical panel from a to b (bottom edge), height h, facing right of a→b, with UVs in metres × scale
        /// along its length and height (so a tiling texture like corrugated metal keeps its size on any wall).
        /// </summary>
        public void Panel(Vector3 a, Vector3 b, float h, float scale = 0.5f)
        {
            Vector3 up = Vector3.up * h;
            Vector3 normal = Vector3.Cross(Vector3.up, b - a).normalized;
            float len = Vector3.Distance(a, b);
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(b + up); _v.Add(a + up);
            for (int k = 0; k < 4; k++) _n.Add(normal);
            _uv.Add(new Vector2(0f, 0f));
            _uv.Add(new Vector2(len * scale, 0f));
            _uv.Add(new Vector2(len * scale, h * scale));
            _uv.Add(new Vector2(0f, h * scale));
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 1);
            _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
        }

        /// <summary>
        /// A slab: the polygon (counter-clockwise from above, star-shaped around its first vertex) as a top, plus
        /// its sides dropped by <paramref name="depth"/> (kerbs, sidewalks, decks).
        /// </summary>
        public void Slab(IReadOnlyList<Vector3> top, float depth)
        {
            Polygon(top, Vector3.up);
            for (int i = 0; i < top.Count; i++)
            {
                Vector3 a = top[i], b = top[(i + 1) % top.Count];
                if ((a - b).sqrMagnitude < 1e-4f) continue;
                // Side faces outward: for a CCW outline, outward is to the right of a→b.
                Wall(a + Vector3.down * depth, b + Vector3.down * depth, depth);
            }
        }

        /// <summary>A closed axis-aligned box (all six faces), for merging many small ones into one mesh.</summary>
        public void Cuboid(Vector3 min, Vector3 max)
        {
            Slab(new[] { new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z) }, max.y - min.y);
            Quad(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z));
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Makes the mesh a static object under <paramref name="parent"/>, optionally solid.</summary>
        public GameObject Build(Transform parent, string name, Material material, bool collider, float roughness = -1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (_v.Count == 0) return go;
            Mesh mesh = ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (roughness >= 0f) go.AddComponent<SurfaceTag>().Roughness = roughness;
            return go;
        }
    }
}
