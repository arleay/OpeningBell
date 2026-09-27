using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Procedural jewellery geometry, built at the size it's worn (see <see cref="Adornments"/>), so a chain fits the
    /// neck it goes round rather than a stock mesh stretched over it. Three primitives cover everything:
    /// <list type="bullet">
    /// <item>lathe: a profile (radius, height) spun round an axis: watch cases, bezels, bands, beads, discs;</item>
    /// <item>sweep: a circle or ellipse pulled along a path: chain links, rims, temples, hoops, torus rings;</item>
    /// <item>gem: a round brilliant (table, crown, girdle, pavilion) with flat facets so it catches the light.</item>
    /// </list>
    /// Each call appends to one of several sub-meshes (one per material), so a whole piece is one renderer.
    /// </summary>
    public sealed class Forge
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<List<int>> _tris = new List<List<int>>();
        private int _sub;

        public Forge(int materials)
        {
            for (int i = 0; i < materials; i++) _tris.Add(new List<int>());
        }

        /// <summary>Which material the next shapes use.</summary>
        public Forge Use(int material)
        {
            _sub = material;
            return this;
        }

        private List<int> T => _tris[_sub];

        /// <summary>
        /// Spins <paramref name="profile"/> (x = radius, y = height along the axis) round <paramref name="axis"/> through
        /// <paramref name="centre"/>. Smooth-shaded unless <paramref name="facets"/> is small. Open profiles are fine
        /// (a disc is a profile ending on the axis).
        /// </summary>
        public void Lathe(Vector3 centre, Vector3 axis, IReadOnlyList<Vector2> profile, int segments = 32, float scaleX = 1f, float scaleZ = 1f)
        {
            axis.Normalize();
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axis, u);
            // Profile normals: perpendicular to each edge, averaged at shared points.
            var pn = new Vector2[profile.Count];
            for (int i = 0; i < profile.Count; i++)
            {
                Vector2 a = profile[Mathf.Max(0, i - 1)], b = profile[Mathf.Min(profile.Count - 1, i + 1)];
                Vector2 d = (b - a).normalized;
                pn[i] = new Vector2(d.y, -d.x);
            }
            int start = _v.Count;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments * Mathf.PI * 2f;
                Vector3 radial = u * (Mathf.Cos(t) * scaleX) + w * (Mathf.Sin(t) * scaleZ);
                Vector3 radialN = (u * (Mathf.Cos(t) / Mathf.Max(0.001f, scaleX)) + w * (Mathf.Sin(t) / Mathf.Max(0.001f, scaleZ))).normalized;
                for (int i = 0; i < profile.Count; i++)
                {
                    _v.Add(centre + radial * profile[i].x + axis * profile[i].y);
                    _n.Add((radialN * pn[i].x + axis * pn[i].y).normalized);
                    _uv.Add(new Vector2(s / (float)segments, i / (float)Mathf.Max(1, profile.Count - 1)));
                }
            }
            int rows = profile.Count;
            for (int s = 0; s < segments; s++)
            for (int i = 0; i < rows - 1; i++)
            {
                int a = start + s * rows + i, b = a + rows;
                Tri(a, a + 1, b);
                Tri(b, a + 1, b + 1);
            }
        }

        /// <summary>
        /// Pulls an ellipse (radii <paramref name="rx"/> across, <paramref name="ry"/> along the path's up) along
        /// <paramref name="path"/>; closed paths join end to start. <paramref name="up"/> sets which way ry points
        /// (defaults to a stable frame transported along the path).
        /// </summary>
        public void Sweep(IReadOnlyList<Vector3> path, float rx, float ry, bool closed, int sides = 10, Vector3? up = null, bool caps = true)
        {
            int n = path.Count;
            if (n < 2) return;
            int start = _v.Count;
            Vector3 prevNormal = up ?? Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = path[closed ? (i - 1 + n) % n : Mathf.Max(0, i - 1)];
                Vector3 next = path[closed ? (i + 1) % n : Mathf.Min(n - 1, i + 1)];
                Vector3 tangent = (next - prev).normalized;
                Vector3 normal;
                if (up.HasValue) normal = Vector3.ProjectOnPlane(up.Value, tangent).normalized;
                else if (i == 0) normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                else normal = Vector3.ProjectOnPlane(prevNormal, tangent).normalized; // parallel transport: no twisting
                if (normal.sqrMagnitude < 0.5f) normal = Vector3.Cross(tangent, Vector3.forward).normalized;
                prevNormal = normal;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                for (int s = 0; s <= sides; s++)
                {
                    float t = s / (float)sides * Mathf.PI * 2f;
                    Vector3 off = binormal * (Mathf.Cos(t) * rx) + normal * (Mathf.Sin(t) * ry);
                    Vector3 nn = (binormal * (Mathf.Cos(t) / rx) + normal * (Mathf.Sin(t) / ry)).normalized;
                    _v.Add(path[i] + off);
                    _n.Add(nn);
                    _uv.Add(new Vector2(s / (float)sides, i / (float)(n - 1)));
                }
            }
            int ring = sides + 1, segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * ring + s, b = start + ((i + 1) % n) * ring + s;
                Tri(a, b, a + 1);
                Tri(a + 1, b, b + 1);
            }
            if (!closed && caps)
            {
                Cap(path[0], (path[0] - path[1]).normalized, start, ring, false);
                Cap(path[n - 1], (path[n - 1] - path[n - 2]).normalized, start + (n - 1) * ring, ring, true);
            }
        }

        private void Cap(Vector3 centre, Vector3 normal, int ringStart, int ring, bool end)
        {
            int c = _v.Count;
            _v.Add(centre); _n.Add(normal); _uv.Add(new Vector2(0.5f, 0.5f));
            int first = _v.Count;
            for (int s = 0; s < ring; s++) { _v.Add(_v[ringStart + s]); _n.Add(normal); _uv.Add(Vector2.zero); }
            for (int s = 0; s < ring - 1; s++) Tri(c, first + s, first + s + 1);
        }

        /// <summary>A circle of <paramref name="radius"/> round <paramref name="axis"/>, as a path for <see cref="Sweep"/>.</summary>
        public static List<Vector3> Circle(Vector3 centre, Vector3 axis, float radius, int points, float radiusB = -1f, float from = 0f, float to = 360f)
        {
            axis.Normalize();
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axis, u);
            if (radiusB < 0f) radiusB = radius;
            var list = new List<Vector3>(points);
            bool full = Mathf.Approximately(to - from, 360f);
            for (int i = 0; i < points; i++)
            {
                float t = Mathf.Lerp(from, to, i / (float)(full ? points : points - 1)) * Mathf.Deg2Rad;
                list.Add(centre + u * (Mathf.Cos(t) * radius) + w * (Mathf.Sin(t) * radiusB));
            }
            return list;
        }

        /// <summary>A torus (a ring of wire) round <paramref name="axis"/>.</summary>
        public void Torus(Vector3 centre, Vector3 axis, float radius, float wire, int around = 24, int sides = 8, float radiusB = -1f) =>
            Sweep(Circle(centre, axis, radius, around, radiusB), wire, wire, true, sides);

        /// <summary>
        /// A round brilliant, table facing <paramref name="up"/>, girdle <paramref name="diameter"/> across. Proportions
        /// of a well-cut stone: table 57%, crown 15%, pavilion 43% of the diameter. Flat facets (no shared normals).
        /// </summary>
        public void Gem(Vector3 girdle, Vector3 up, float diameter, int facets = 16)
        {
            up.Normalize();
            float r = diameter / 2f;
            Vector3 u = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(up, u);
            Vector3 P(float radius, float angle, float h) => girdle + u * (Mathf.Cos(angle) * radius) + w * (Mathf.Sin(angle) * radius) + up * h;
            Vector3 table = girdle + up * (diameter * 0.15f), culet = girdle - up * (diameter * 0.43f);
            float step = Mathf.PI * 2f / facets;
            for (int i = 0; i < facets; i++)
            {
                float a = i * step, b = a + step, mid = a + step / 2f;
                Vector3 g0 = P(r, a, 0f), g1 = P(r, b, 0f);
                Vector3 t0 = P(r * 0.57f, mid - step / 2f, diameter * 0.15f), t1 = P(r * 0.57f, mid + step / 2f, diameter * 0.15f);
                Vector3 star = P(r * 0.8f, mid, diameter * 0.1f);
                Facet(table, t0, t1, girdle, up);        // table
                Facet(t0, star, t1, girdle, up);         // star
                Facet(t0, g0, star, girdle, up);         // upper girdle
                Facet(star, g0, g1, girdle, up);
                Facet(star, g1, t1, girdle, up);
                Facet(g0, culet, g1, girdle, up);        // pavilion
            }
        }

        /// <summary>
        /// One flat triangle facing out of the stone. "Out" is judged from the stone's axis, not its centre: a crown facet
        /// near the girdle has its centroid almost level with the centre, so outward is its sideways offset from the
        /// axis plus up for the crown (down for the pavilion).
        /// </summary>
        private void Facet(Vector3 a, Vector3 b, Vector3 c, Vector3 girdle, Vector3 up)
        {
            // Normalised by hand: Unity's .normalized returns zero below 1e-5, and a real-size stone's facet cross
            // product is ~1e-6 (that zero normal drew the facets black).
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Vector3 n = cross / Mathf.Max(cross.magnitude, 1e-20f);
            Vector3 centroid = (a + b + c) / 3f;
            float h = Vector3.Dot(centroid - girdle, up);
            Vector3 radial = Vector3.ProjectOnPlane(centroid - girdle, up);
            Vector3 outward = radial + up * (Mathf.Sign(h) * Mathf.Max(radial.magnitude, 1e-5f));
            if (Vector3.Dot(n, outward) < 0f) n = -n;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _uv.Add(Vector2.zero); _uv.Add(Vector2.right); _uv.Add(Vector2.up);
            Tri(i, i + 1, i + 2);
        }

        /// <summary>
        /// Adds a triangle wound to face the way its vertex normals point (Unity draws the side where
        /// Cross(b - a, c - a) points), so the shape builders needn't track handedness.
        /// </summary>
        private void Tri(int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            if (Vector3.Dot(face, _n[a] + _n[b] + _n[c]) < 0f) { T.Add(a); T.Add(c); T.Add(b); }
            else { T.Add(a); T.Add(b); T.Add(c); }
        }

        /// <summary>A small flat-shaded box (watch hands, clasp, lugs, bridges), centred, axes given.</summary>
        public void Block(Vector3 centre, Vector3 right, Vector3 up, Vector3 size)
        {
            right.Normalize();
            up = Vector3.ProjectOnPlane(up, right).normalized;
            Vector3 fwd = Vector3.Cross(right, up);
            Vector3 x = right * size.x / 2f, y = up * size.y / 2f, z = fwd * size.z / 2f;
            Quad(centre + z - x - y, centre + z + x - y, centre + z + x + y, centre + z - x + y, fwd);
            Quad(centre - z + x - y, centre - z - x - y, centre - z - x + y, centre - z + x + y, -fwd);
            Quad(centre + x + z - y, centre + x - z - y, centre + x - z + y, centre + x + z + y, right);
            Quad(centre - x - z - y, centre - x + z - y, centre - x + z + y, centre - x - z + y, -right);
            Quad(centre - x + z + y, centre + x + z + y, centre + x - z + y, centre - x - z + y, up);
            Quad(centre - x - z - y, centre + x - z - y, centre + x + z - y, centre - x + z - y, -up);
        }

        /// <summary>A flat disc facing <paramref name="normal"/> with a 0–1 UV square over it (dials, lenses).</summary>
        public void Disc(Vector3 centre, Vector3 normal, float radius, int segments = 40, float radiusB = -1f, Vector3? right = null)
        {
            normal.Normalize();
            Vector3 u = right.HasValue ? Vector3.ProjectOnPlane(right.Value, normal).normalized : Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(normal, u);
            if (radiusB < 0f) radiusB = radius;
            int c = _v.Count;
            _v.Add(centre); _n.Add(normal); _uv.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments * Mathf.PI * 2f;
                float cx = Mathf.Cos(t), sy = Mathf.Sin(t);
                _v.Add(centre + u * (cx * radius) + w * (sy * radiusB));
                _n.Add(normal);
                _uv.Add(new Vector2(0.5f + cx * 0.5f, 0.5f + sy * 0.5f));
            }
            for (int s = 0; s < segments; s++) Tri(c, c + 1 + s, c + 2 + s);
        }

        /// <summary>A flat, two-sided fan from <paramref name="centre"/> over a closed outline (lenses).</summary>
        public void Fan(Vector3 centre, IReadOnlyList<Vector3> outline, Vector3 normal)
        {
            foreach (Vector3 side in new[] { normal, -normal })
            {
                int c = _v.Count;
                _v.Add(centre); _n.Add(side); _uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < outline.Count; i++) { _v.Add(outline[i]); _n.Add(side); _uv.Add(Vector2.zero); }
                for (int i = 0; i < outline.Count; i++) Tri(c, c + 1 + i, c + 1 + (i + 1) % outline.Count);
            }
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) _n.Add(n);
            _uv.Add(new Vector2(0, 0)); _uv.Add(new Vector2(1, 0)); _uv.Add(new Vector2(1, 1)); _uv.Add(new Vector2(0, 1));
            Tri(i, i + 1, i + 2);
            Tri(i, i + 2, i + 3);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = _tris.Count;
            for (int i = 0; i < _tris.Count; i++) mesh.SetTriangles(_tris[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        public GameObject Build(Transform parent, string name, params Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // too small to matter; saves the shadow pass
            return go;
        }
    }
}
