using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpeningBell.City;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpeningBell.EditorTools
{
    /// <summary>
    /// Rebuilds <see cref="CityArt"/> from the third-party folders and the shared people animator. Batch:
    /// <c>Unity -batchmode -quit -executeMethod OpeningBell.EditorTools.CityArtBuilder.Rebuild</c>. Writes a report of
    /// model sizes to Logs/art-report.txt (layout code sizes things from these numbers).
    /// </summary>
    public static class CityArtBuilder
    {
        public const string ArtPath = "Assets/ScriptableObjects/City/CityArt.asset";
        public const string ControllerPath = "Assets/Art/Animation/People.controller";
        private const string ClipsPath = ArtImportRules.Animations + "UAL1_Standard.fbx";
        private const string RootMotionClipsPath = ArtImportRules.Animations + "UAL1_Standard_RM.fbx";

        private static readonly string[] KenneyKits = { "CityCommercial", "CitySuburban", "CityRoads", "ModularBuildings", "Furniture", "Nature" };

        /// <summary>Animator states (what <see cref="NpcBody"/> cross-fades to) and their clips. Walks follow the Speed parameter.</summary>
        public static readonly (string State, string Clip, bool Paced)[] States =
        {
            ("Idle", "Idle_Loop", false),
            ("Walk", "Walk_Loop", true),
            ("WalkFormal", "Walk_Formal_Loop", true),
            ("Jog", "Jog_Fwd_Loop", true),
            ("Sit", "Sitting_Idle_Loop", false),
            ("SitTalk", "Sitting_Talking_Loop", false),
            ("Talk", "Idle_Talking_Loop", false),
            ("Drive", "Driving_Loop", false),
            ("Interact", "Interact", false),
            ("PickUp", "PickUp_Table", false),
            ("Fix", "Fixing_Kneeling", false),
            ("Crouch", "Crouch_Idle_Loop", false),
        };

        [MenuItem("Opening Bell/Rebuild City Art")]
        public static void Rebuild()
        {
            var report = new StringBuilder();
            Dictionary<string, AnimationClip> clips = Clips(ClipsPath);
            var existing = AssetDatabase.LoadAssetAtPath<CityArt>(ArtPath);
            // The root-motion copy is only needed to measure speeds; without it keep what was measured before.
            float walkSpeed = MeasureSpeeds(report) ?? (existing != null ? existing.WalkClipSpeed : 1.3f);
            RuntimeAnimatorController controller = BuildController(clips, report);

            var models = new List<GameObject>();
            var seen = new HashSet<string>();
            foreach (string kit in KenneyKits)
            {
                string folder = ArtImportRules.Kenney + kit + "/Models";
                report.AppendLine($"== {kit}");
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }).OrderBy(AssetDatabase.GUIDToAssetPath))
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!seen.Add(model.name))
                    {
                        Debug.LogWarning($"CityArt: duplicate model name {model.name} in {kit}; skipped");
                        continue;
                    }
                    models.Add(model);
                    report.AppendLine(Describe(model));
                }
            }

            var people = new List<GameObject>();
            report.AppendLine("== People");
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtImportRules.Characters.TrimEnd('/') }).OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var animator = model.GetComponent<Animator>();
                bool human = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
                report.AppendLine(Describe(model) + (human ? " humanoid" : " NOT HUMANOID"));
                if (human) people.Add(model);
            }

            var (palettes, paletteNames) = Palettes(report);

            var art = AssetDatabase.LoadAssetAtPath<CityArt>(ArtPath);
            if (art == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArtPath));
                art = ScriptableObject.CreateInstance<CityArt>();
                AssetDatabase.CreateAsset(art, ArtPath);
            }
            art.EditorSet(models.ToArray(), people.ToArray(), controller, walkSpeed, palettes, paletteNames);
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/art-report.txt", report.ToString());
            Debug.Log($"CityArt rebuilt: {models.Count} models, {people.Count} people, walk clip {walkSpeed:F2} m/s");
        }

        /// <summary>Each kit palette (colormap and colour variations) plus a generated night-window mask ("-glow").</summary>
        private static (Texture2D[], string[]) Palettes(StringBuilder report)
        {
            var sources = new List<(string Kit, string Path)>();
            foreach (string kit in KenneyKits)
            {
                string folder = ArtImportRules.Kenney + kit + "/Models/Textures";
                if (!Directory.Exists(folder)) continue;
                foreach (string path in Directory.GetFiles(folder, "*.png").Select(p => p.Replace('\\', '/')).Where(p => !p.EndsWith("-glow.png")).OrderBy(p => p))
                {
                    sources.Add((kit, path));
                    WriteGlowMask(path, GlowPath(path), report);
                }
            }
            AssetDatabase.Refresh();
            var textures = new List<Texture2D>();
            var names = new List<string>();
            foreach (var (kit, path) in sources)
            {
                string name = kit + "/" + Path.GetFileNameWithoutExtension(path);
                textures.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                names.Add(name);
                textures.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(GlowPath(path)));
                names.Add(name + "-glow");
            }
            return (textures.ToArray(), names.ToArray());
        }

        private static string GlowPath(string palette) => palette.Substring(0, palette.Length - 4) + "-glow.png";

        /// <summary>
        /// Night windows: white where a palette cell is window glass (a clearly blue colour), black elsewhere, so
        /// emission lights the glass and nothing else.
        /// </summary>
        private static void WriteGlowMask(string source, string target, StringBuilder report)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(source));
            Color[] pixels = texture.GetPixels();
            int lit = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out float h, out float s, out float v);
                bool glass = h > 0.53f && h < 0.66f && s > 0.28f && v > 0.55f;
                if (glass) lit++;
                pixels[i] = glass ? Color.white : Color.black;
            }
            texture.SetPixels(pixels);
            File.WriteAllBytes(target, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            report.AppendLine($"glow mask {Path.GetFileName(target)}: {100f * lit / pixels.Length:F1}% lit");
        }

        private static Dictionary<string, AnimationClip> Clips(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());

        /// <summary>The in-place clips carry no speed, so read it off the root-motion copies.</summary>
        private static float? MeasureSpeeds(StringBuilder report)
        {
            float? walk = null;
            if (!File.Exists(RootMotionClipsPath)) return null;
            Dictionary<string, AnimationClip> rm = Clips(RootMotionClipsPath);
            foreach (string name in new[] { "Walk_Loop", "Walk_Formal_Loop", "Jog_Fwd_Loop", "Sprint_Loop" })
            {
                if (!rm.TryGetValue(name, out AnimationClip clip)) continue;
                float speed = new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude;
                report.AppendLine($"speed {name}: {speed:F3} m/s over {clip.length:F2}s");
                if (name == "Walk_Loop" && speed > 0.3f) walk = speed;
            }
            return walk;
        }

        private static RuntimeAnimatorController BuildController(Dictionary<string, AnimationClip> clips, StringBuilder report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.parameters = controller.parameters.Select(p =>
            {
                if (p.name == "Speed") p.defaultFloat = 1f;
                return p;
            }).ToArray();
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (var (state, clipName, paced) in States)
            {
                if (!clips.TryGetValue(clipName, out AnimationClip clip))
                {
                    report.AppendLine("MISSING clip " + clipName);
                    continue;
                }
                AnimatorState s = machine.AddState(state);
                s.motion = clip;
                s.writeDefaultValues = true;
                if (paced)
                {
                    s.speedParameterActive = true;
                    s.speedParameter = "Speed";
                }
                if (state == "Idle") machine.defaultState = s;
            }
            report.AppendLine($"controller: {machine.states.Length} states; clips available: {string.Join(" ", clips.Keys)}");
            return controller;
        }

        private static string Describe(GameObject model)
        {
            GameObject instance = Object.Instantiate(model);
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return model.name + " (no renderers)";
                Bounds b = renderers[0].bounds;
                foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                int tris = 0;
                foreach (MeshFilter f in instance.GetComponentsInChildren<MeshFilter>()) tris += f.sharedMesh.triangles.Length / 3;
                foreach (SkinnedMeshRenderer s in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) tris += s.sharedMesh.triangles.Length / 3;
                string mats = string.Join(",", renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct());
                return $"{model.name} size {b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} min {b.min.x:F2},{b.min.y:F2},{b.min.z:F2} tris {tris} mats {mats}";
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
