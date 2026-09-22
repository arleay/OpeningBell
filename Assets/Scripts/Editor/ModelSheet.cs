using System.Collections.Generic;
using System.IO;
using OpeningBell.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpeningBell.EditorTools
{
    /// <summary>
    /// Renders kit models into a labelled grid (TestResults/models-*.png) so orientation and proportions can be
    /// checked before placing them. Each model gets two views, from +z (front-right) and from -z (back-left), with a
    /// red marker on its +x side and a blue one on its +z side. Batch:
    /// <c>Unity -batchmode -quit -executeMethod OpeningBell.EditorTools.ModelSheet.Batch [-models a,b,c] [-scale 0.2] [-sheet name.png]</c>
    /// (no <c>-models</c>: the whole furniture list). A companion .txt lists each model's size and its upward-facing
    /// surfaces (shelves, seats, tops) at that scale, for placing things on them.
    /// </summary>
    public static class ModelSheet
    {
        private static readonly string[] FurnitureModels =
        {
            "bedSingle", "bedDouble", "cabinetBed", "cabinetBedDrawer", "cabinetBedDrawerTable", "pillow", "pillowLong",
            "chair", "chairCushion", "chairDesk", "chairModernCushion", "chairModernFrameCushion", "chairRounded", "stoolBar",
            "desk", "deskCorner", "table", "tableCross", "sideTable", "sideTableDrawers", "tableCoffee", "tableCoffeeSquare",
            "computerScreen", "computerKeyboard", "computerMouse", "laptop", "speakerSmall", "radio", "televisionModern",
            "kitchenCabinet", "kitchenCabinetDrawer", "kitchenSink", "kitchenStove", "kitchenStoveElectric", "kitchenCabinetUpper",
            "kitchenCabinetUpperDouble", "kitchenCabinetUpperLow", "kitchenFridge", "kitchenFridgeSmall", "kitchenFridgeBuiltIn",
            "kitchenMicrowave", "kitchenCoffeeMachine", "kitchenBlender", "toaster", "hoodModern",
            "loungeSofa", "loungeChair", "loungeDesignSofa", "loungeSofaOttoman", "cabinetTelevision", "bookcaseOpen",
            "bookcaseOpenLow", "bookcaseClosedWide", "books", "lampRoundFloor", "lampRoundTable", "lampSquareCeiling",
            "lampSquareFloor", "lampSquareTable", "lampWall", "ceilingFan", "pottedPlant", "plantSmall1", "plantSmall2",
            "plantSmall3", "rugRectangle", "rugRound", "rugRounded", "rugDoormat", "coatRackStanding", "trashcan",
            "cardboardBoxClosed", "cardboardBoxOpen",
        };

        [MenuItem("Opening Bell/Model Sheet (Furniture)")]
        public static void Furniture() => Render("models-furniture.png", FurnitureModels, 0.2f);

        public static void Batch()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                int i = System.Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            string models = Arg("-models", null);
            Render(Arg("-sheet", "models-furniture.png"), models != null ? models.Split(',') : FurnitureModels,
                float.Parse(Arg("-scale", "0.2"), System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void Render(string file, IReadOnlyList<string> names, float scale)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load after the scene switch: a Single-mode switch unloads unreferenced in-memory assets.
            var art = AssetDatabase.LoadAssetAtPath<CityArt>(CityArtBuilder.ArtPath);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.32f, 0.34f, 0.37f);
            camera.fieldOfView = 30f;
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(camera.transform, false);
            label.transform.localPosition = new Vector3(0f, -0.262f, 1f);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.fontSize = 48;
            label.characterSize = 0.0042f;
            label.anchor = TextAnchor.LowerCenter;
            label.color = Color.white;

            const int cell = 280, cols = 8; // two cells (views) per model
            int perRow = cols / 2, rows = (names.Count + perRow - 1) / perRow;
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
            var target = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            Vector3[] views = { new Vector3(0.45f, 0.5f, 1f).normalized, new Vector3(-0.45f, 0.5f, -1f).normalized };
            var notes = new System.Text.StringBuilder();

            for (int i = 0; i < names.Count; i++)
            {
                GameObject prefab = art.Model(names[i]);
                if (prefab == null)
                {
                    Debug.LogWarning($"ModelSheet: no model {names[i]}");
                    continue;
                }
                GameObject go = Object.Instantiate(prefab);
                go.transform.localScale = Vector3.one * scale;
                Bounds b = Bounds(go);
                notes.AppendLine($"{names[i]} size {b.size.x:0.000} x {b.size.y:0.000} x {b.size.z:0.000}, min {b.min.x:0.000},{b.min.y:0.000},{b.min.z:0.000}");
                Surfaces(go, notes);
                float marker = Mathf.Max(0.04f, b.extents.magnitude * 0.08f);
                GameObject px = Marker(new Vector3(b.max.x + marker, b.min.y + marker / 2f, b.center.z), marker, Color.red);
                GameObject pz = Marker(new Vector3(b.center.x, b.min.y + marker / 2f, b.max.z + marker), marker, Color.blue);

                float distance = b.extents.magnitude * 1.2f / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                for (int v = 0; v < views.Length; v++)
                {
                    camera.transform.position = b.center + views[v] * distance;
                    camera.transform.LookAt(b.center);
                    camera.nearClipPlane = distance * 0.05f;
                    camera.farClipPlane = distance * 4f;
                    label.text = v == 0 ? $"{names[i]} {b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00}" : "(from -z)";
                    camera.Render();

                    RenderTexture.active = target;
                    sheet.ReadPixels(new Rect(0, 0, cell, cell), (i % perRow * 2 + v) * cell, (rows - 1 - i / perRow) * cell);
                    RenderTexture.active = null;
                }
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(px);
                Object.DestroyImmediate(pz);
            }

            sheet.Apply();
            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, file), sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(dir, Path.ChangeExtension(file, ".txt")), notes.ToString());
            camera.targetTexture = null;
            target.Release();
            Debug.Log($"ModelSheet: wrote {file} ({names.Count} models)");
        }

        /// <summary>Upward-facing faces grouped by height (1 cm), largest first: where things can rest on the model.</summary>
        private static void Surfaces(GameObject go, System.Text.StringBuilder notes)
        {
            var levels = new SortedDictionary<int, (float Area, Bounds Extent)>();
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = mf.transform.TransformPoint(v[t[i]]), b = mf.transform.TransformPoint(v[t[i + 1]]), c = mf.transform.TransformPoint(v[t[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    float area = n.magnitude / 2f;
                    if (area < 1e-6f || n.normalized.y < 0.95f) continue;
                    int key = Mathf.RoundToInt((a.y + b.y + c.y) / 3f * 100f);
                    var extent = new Bounds(a, Vector3.zero);
                    extent.Encapsulate(b);
                    extent.Encapsulate(c);
                    if (levels.TryGetValue(key, out var level))
                    {
                        level.Extent.Encapsulate(extent);
                        levels[key] = (level.Area + area, level.Extent);
                    }
                    else levels[key] = (area, extent);
                }
            }
            foreach (var (key, level) in levels)
                if (level.Area > 0.002f)
                    notes.AppendLine($"  surface y {key / 100f:0.00}: area {level.Area:0.000}, x {level.Extent.min.x:0.00}..{level.Extent.max.x:0.00}, z {level.Extent.min.z:0.00}..{level.Extent.max.z:0.00}");
        }

        private static Bounds Bounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        private static GameObject Marker(Vector3 at, float size, Color color)
        {
            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m.transform.position = at;
            m.transform.localScale = Vector3.one * size;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
            m.GetComponent<MeshRenderer>().sharedMaterial = material;
            return m;
        }
    }
}
