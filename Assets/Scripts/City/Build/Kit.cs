using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>A door or window hole in a wall, in wall-local terms: centre along the wall, width, sill and head heights.</summary>
    public readonly struct Opening
    {
        public readonly float Center, Width, Bottom, Top;

        public Opening(float center, float width, float bottom, float top)
        {
            Center = center;
            Width = width;
            Bottom = bottom;
            Top = top;
        }

        public static Opening Door(float center, float width = 1f, float floor = 0f, float height = 2.15f) =>
            new Opening(center, width, floor, floor + height);
    }

    /// <summary>
    /// Geometry helpers for the generated city: boxes (primitive meshes, optional colliders), walls with openings,
    /// facade volumes with window-grid UVs and text signs. All sizes in metres, positions in the parent's space.
    /// </summary>
    public sealed class Kit
    {
        public readonly Palette P;
        private readonly Mesh _cube, _cylinder, _sphere, _capsule, _quad;

        public Kit(Palette palette)
        {
            P = palette;
            _cube = BuiltinMesh(PrimitiveType.Cube);
            _cylinder = BuiltinMesh(PrimitiveType.Cylinder);
            _sphere = BuiltinMesh(PrimitiveType.Sphere);
            _capsule = BuiltinMesh(PrimitiveType.Capsule);
            _quad = BuiltinMesh(PrimitiveType.Quad);
        }

        private static Mesh BuiltinMesh(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        public static Transform Group(Transform parent, string name, Vector3 position = default, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        private GameObject MeshObject(Transform parent, string name, Mesh mesh, Material m, Vector3 center, Vector3 scale, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        public GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material m, bool collider = true, float yaw = 0f)
        {
            GameObject go = MeshObject(parent, name, _cube, m, center, size, Quaternion.Euler(0f, yaw, 0f));
            if (collider) go.AddComponent<BoxCollider>();
            return go;
        }

        public GameObject Span(Transform parent, string name, Vector3 min, Vector3 max, Material m, bool collider = true) =>
            Box(parent, name, (min + max) * 0.5f, max - min, m, collider);

        public GameObject Cylinder(Transform parent, string name, Vector3 center, float diameter, float height, Material m, bool collider = false)
        {
            GameObject go = MeshObject(parent, name, _cylinder, m, center, new Vector3(diameter, height * 0.5f, diameter), Quaternion.identity);
            if (collider) go.AddComponent<CapsuleCollider>();
            return go;
        }

        public GameObject Sphere(Transform parent, string name, Vector3 center, float diameter, Material m) =>
            MeshObject(parent, name, _sphere, m, center, Vector3.one * diameter, Quaternion.identity);

        public GameObject Capsule(Transform parent, string name, Vector3 center, Vector3 scale, Material m) =>
            MeshObject(parent, name, _capsule, m, center, scale, Quaternion.identity);

        /// <summary>Upright quad whose visible side faces the parent's -z (after <paramref name="yaw"/>): screens, pictures.</summary>
        public GameObject Quad(Transform parent, string name, Vector3 center, Vector2 size, float yaw, Material m) =>
            MeshObject(parent, name, _quad, m, center, new Vector3(size.x, size.y, 1f), Quaternion.Euler(0f, yaw, 0f));

        /// <summary>Flat marking lying on a surface (road lines, crosswalk stripes).</summary>
        public GameObject Decal(Transform parent, string name, Vector3 center, Vector2 size, float yaw, Material m)
        {
            GameObject go = MeshObject(parent, name, _quad, m, center, new Vector3(size.x, size.y, 1f), Quaternion.Euler(90f, yaw, 0f));
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// Wall along x from <paramref name="x0"/> to <paramref name="x1"/> centred on <paramref name="z"/>, with holes.
        /// Returns the pieces so callers can swap materials (e.g. glass infill).
        /// </summary>
        public List<GameObject> WallX(Transform parent, string name, float x0, float x1, float z, float y0, float y1, float thickness, Material m, params Opening[] openings) =>
            Wall(parent, name, x0, x1, y0, y1, openings, (a, b, lo, hi) => Span(parent, name, new Vector3(a, lo, z - thickness / 2f), new Vector3(b, hi, z + thickness / 2f), m));

        public List<GameObject> WallZ(Transform parent, string name, float z0, float z1, float x, float y0, float y1, float thickness, Material m, params Opening[] openings) =>
            Wall(parent, name, z0, z1, y0, y1, openings, (a, b, lo, hi) => Span(parent, name, new Vector3(x - thickness / 2f, lo, a), new Vector3(x + thickness / 2f, hi, b), m));

        private static List<GameObject> Wall(Transform parent, string name, float from, float to, float y0, float y1, Opening[] openings,
            System.Func<float, float, float, float, GameObject> piece)
        {
            var pieces = new List<GameObject>();
            var sorted = new List<Opening>(openings);
            sorted.Sort((a, b) => a.Center.CompareTo(b.Center));
            float cursor = from;
            foreach (Opening o in sorted)
            {
                float a = o.Center - o.Width / 2f, b = o.Center + o.Width / 2f;
                if (a > cursor + 0.01f) pieces.Add(piece(cursor, a, y0, y1));
                if (o.Bottom > y0 + 0.01f) pieces.Add(piece(a, b, y0, o.Bottom));
                if (o.Top < y1 - 0.01f) pieces.Add(piece(a, b, o.Top, y1));
                cursor = b;
            }
            if (to > cursor + 0.01f) pieces.Add(piece(cursor, to, y0, y1));
            return pieces;
        }

        /// <summary>Pane of glass filling an opening (no shadow, no collider unless asked).</summary>
        public GameObject Pane(Transform parent, string name, Vector3 min, Vector3 max, Color tint, bool collider = true)
        {
            GameObject go = Span(parent, name, min, max, P.Glass(tint), collider);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Building volume whose sides tile the facade texture: one cell per ~3 m bay and ~3.2 m floor.</summary>
        public GameObject Facade(Transform parent, string name, Vector3 min, Vector3 max, Material facade, Material roof, bool collider = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = min;
            Vector3 size = max - min;
            go.AddComponent<MeshFilter>().sharedMesh = FacadeMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { facade, roof };
            if (collider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = size / 2f;
                box.size = size;
            }
            return go;
        }

        private static Mesh FacadeMesh(Vector3 size)
        {
            float cells = FacadeTextures.Cells;
            int baysX = Mathf.Max(1, Mathf.RoundToInt(size.x / 3f)), baysZ = Mathf.Max(1, Mathf.RoundToInt(size.z / 3f));
            int floors = Mathf.Max(1, Mathf.RoundToInt(size.y / 3.2f));
            float v = floors / cells;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var sides = new List<int>();
            var top = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float u, List<int> into)
            {
                int i = verts.Count;
                verts.AddRange(new[] { a, b, c, d });
                uvs.AddRange(new[] { new Vector2(0f, 0f), new Vector2(u, 0f), new Vector2(u, v), new Vector2(0f, v) });
                normals.AddRange(new[] { n, n, n, n });
                into.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            float x = size.x, y = size.y, z = size.z;
            Quad(new Vector3(0, 0, 0), new Vector3(x, 0, 0), new Vector3(x, y, 0), new Vector3(0, y, 0), Vector3.back, baysX / cells, sides);
            Quad(new Vector3(x, 0, z), new Vector3(0, 0, z), new Vector3(0, y, z), new Vector3(x, y, z), Vector3.forward, baysX / cells, sides);
            Quad(new Vector3(0, 0, z), new Vector3(0, 0, 0), new Vector3(0, y, 0), new Vector3(0, y, z), Vector3.left, baysZ / cells, sides);
            Quad(new Vector3(x, 0, 0), new Vector3(x, 0, z), new Vector3(x, y, z), new Vector3(x, y, 0), Vector3.right, baysZ / cells, sides);
            Quad(new Vector3(0, y, 0), new Vector3(x, y, 0), new Vector3(x, y, z), new Vector3(0, y, z), Vector3.up, 1f, top);

            var mesh = new Mesh { name = "Facade" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sides, 0);
            mesh.SetTriangles(top, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// Readable text sign. Faces -z in the parent's space (read by someone standing on the -z side) unless rotated
        /// by <paramref name="yaw"/>. <paramref name="height"/> is roughly the capital-letter height in metres.
        /// </summary>
        public TextMesh Text(Transform parent, string text, Vector3 position, float yaw, float height, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text " + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = P.Font;
            mesh.fontSize = 64;
            mesh.characterSize = height * 10f / 64f * 1.45f;
            mesh.anchor = anchor;
            mesh.alignment = TextAlignment.Center;
            mesh.text = text;
            mesh.color = Color.white;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = P.Sign(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mesh;
        }
    }
}
