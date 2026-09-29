using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpeningBell.EditorTools
{
    /// <summary>
    /// Renders the detailed third-party cars (<see cref="ArtImportRules.Cars"/>) in sunlight on a ground plane, one row per
    /// car: as imported, after a respray the way the game paints owned cars, and from the other side (wheels, trim).
    /// Batch: <c>Unity -batchmode -quit -executeMethod OpeningBell.EditorTools.CarSheet.Batch [-cars urus,charger]</c>
    /// writes TestResults/cars.png.
    /// </summary>
    public static class CarSheet
    {
        private static readonly string[] Variants = { "as imported", "resprayed blue (CarFactory.Paint)", "from the other side", "back wheel close up" };

        [MenuItem("Opening Bell/Car Sheet")]
        public static void Menu() => Render(null, ArtImportRules.Cars);

        public static void Batch()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-cars");
            int d = System.Array.IndexOf(args, "-dir");
            Render(i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : null, d >= 0 && d + 1 < args.Length ? args[d + 1] : ArtImportRules.Cars);
        }

        private static void Render(string[] only, string folder)
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (only == null || System.Array.IndexOf(only, name) >= 0) paths.Add(path);
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.52f);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 4f;

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.68f, 0.82f);
            camera.fieldOfView = 30f;
            QualitySettings.shadowDistance = 30f;

            const int w = 560, h = 350;
            var sheet = new Texture2D(w * Variants.Length, h * Mathf.Max(1, paths.Count), TextureFormat.RGBA32, false);
            var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;

            for (int row = 0; row < paths.Count; row++)
            {
                GameObject car = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[row]));
                car.transform.localScale = Vector3.one * City.CarFactory.Scale;
                Bounds b = Bounds(car);
                car.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = Bounds(car);
                // Rear three-quarter from the sunny side, about where the player stands at a kerb.
                camera.transform.position = b.center + new Vector3(4.2f, 0.9f, -4.6f) * (b.extents.magnitude / 2.9f);
                camera.transform.LookAt(b.center);

                // First render of a batch session draws before shaders and textures finish loading: throw one away.
                if (row == 0) camera.Render();
                Vector3 side = camera.transform.position;
                for (int v = 0; v < Variants.Length; v++)
                {
                    if (v == 1) City.CarFactory.Paint(car, new Color(0.12f, 0.3f, 0.75f));
                    if (v == 2)
                    {
                        camera.transform.position = new Vector3(2f * b.center.x - side.x, side.y, 2f * b.center.z - side.z);
                        camera.transform.LookAt(b.center);
                    }
                    if (v == 3)
                    {
                        Transform wheel = Find(car.transform, "wheel-back-right") ?? Find(car.transform, "wheel-back-left");
                        if (wheel != null && wheel.TryGetComponent(out Renderer wr))
                        {
                            Vector3 wc = wr.bounds.center;
                            Vector3 outward = new Vector3(Mathf.Sign(wc.x - b.center.x), 0f, 0f);
                            camera.transform.position = wc + outward * 2.2f + new Vector3(0f, 0.5f, -1.2f);
                            camera.transform.LookAt(wc);
                        }
                    }
                    camera.Render();
                    RenderTexture.active = target;
                    sheet.ReadPixels(new Rect(0, 0, w, h), v * w, (paths.Count - 1 - row) * h);
                    RenderTexture.active = null;
                }
                foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh mesh = mf.sharedMesh;
                    long vb = 0;
                    for (int st = 0; st < mesh.vertexBufferCount; st++) vb += (long)mesh.GetVertexBufferStride(st) * mesh.vertexCount;
                    long ib = 0;
                    for (int sm = 0; sm < mesh.subMeshCount; sm++) ib += (long)mesh.GetSubMesh(sm).indexCount * (mesh.indexFormat == IndexFormat.UInt32 ? 4 : 2);
                    Debug.Log($"CarSheet mesh {Path.GetFileNameWithoutExtension(paths[row])}/{mf.name}: {mesh.vertexCount} verts, vertex buffer {vb / 1048576f:0.0} MB, index {ib / 1048576f:0.0} MB, lods {mesh.lodCount}");
                }
                foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
                    foreach (Material m in r.sharedMaterials)
                        if (m != null && m.HasProperty("_BaseColor"))
                            Debug.Log($"CarSheet colour {Path.GetFileNameWithoutExtension(paths[row])}/{m.name}: {m.GetColor("_BaseColor")} (linear {m.GetColor("_BaseColor").linear})");
                Debug.Log($"CarSheet: row {row} = {Path.GetFileNameWithoutExtension(paths[row])} ({string.Join(" | ", Variants)})");
                Object.DestroyImmediate(car);
            }

            sheet.Apply();
            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "cars.png"), sheet.EncodeToPNG());
            camera.targetTexture = null;
            target.Release();
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }

        private static Bounds Bounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
