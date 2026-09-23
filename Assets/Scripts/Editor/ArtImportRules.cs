using UnityEditor;

namespace OpeningBell.EditorTools
{
    /// <summary>
    /// Import settings for third-party art, applied by folder so re-imports stay consistent. Quaternius people are
    /// humanoid so one animation set (the Universal Animation Library) drives every character; Kenney kits are
    /// static meshes.
    /// </summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        public const string Characters = "Assets/Art/ThirdParty/Quaternius/Characters/";
        public const string Animations = "Assets/Art/ThirdParty/Quaternius/Animations/";
        public const string Kenney = "Assets/Art/ThirdParty/Kenney/";
        /// <summary>Quaternius static props (food): no rig, readable meshes like the Kenney kits.</summary>
        public const string Props = "Assets/Art/ThirdParty/Quaternius/Props/";
        /// <summary>Quaternius trees, bushes and grass (converted from the Stylized Nature MegaKit GLBs, 1 unit tall).</summary>
        public const string Nature = "Assets/Art/ThirdParty/Quaternius/Nature/";

        /// <summary>One-shot clips that staff play on repeat while working.</summary>
        private static readonly string[] AlsoLooped = { "Interact", "PickUp_Table", "Fixing_Kneeling" };

        /// <summary>Bump when a rule changes: Unity re-imports what this postprocessor touched.</summary>
        public override uint GetVersion() => 12;

        private void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;
            if (assetPath.StartsWith(Characters) || assetPath.StartsWith(Animations))
            {
                // Blender exports: bake the axis conversion so roots aren't rotated -90° on X (Quaternius' own setup).
                importer.bakeAxisConversion = true;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = assetPath.StartsWith(Animations);
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            }
            else if ((assetPath.StartsWith(Kenney) && !assetPath.Contains("/CarKit/")) || assetPath.StartsWith(Props) || assetPath.StartsWith(Nature))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                // The city is static-batched at runtime, which needs CPU-readable meshes.
                importer.isReadable = true;
            }
        }

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(Nature))
            {
                var nature = (TextureImporter)assetImporter;
                if (assetPath.EndsWith("_Normal.png")) nature.textureType = TextureImporterType.NormalMap;
                // Leaf cards are alpha-tested: without coverage-preserving mips distant canopies thin out to sticks.
                nature.mipMapsPreserveCoverage = true;
                nature.alphaTestReferenceValue = 0.5f;
                return;
            }
            if (!assetPath.StartsWith(Kenney)) return;
            // Palette textures: flat colour cells. Mipmaps would blend neighbouring cells on distant buildings.
            var importer = (TextureImporter)assetImporter;
            importer.mipmapEnabled = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        }

        public const string CarGlass = "Assets/Art/Materials/CarGlass.mat";
        /// <summary>Detailed third-party cars, one folder each, converted by Tools/Blender/convert_car.py.</summary>
        public const string Cars = "Assets/Art/ThirdParty/Cars/";

        /// <summary>
        /// Third-party car glass comes in opaque (the FBX carries no usable transparency, and settings made on the
        /// importer's own materials don't stick), so windscreens rendered as white panels. Every material named
        /// "glass" on a car model is swapped for the shared tinted, see-through <see cref="CarGlass"/>.
        /// </summary>
        private void OnPostprocessModel(UnityEngine.GameObject root)
        {
            // Cars only: Kenney furniture names its mirror, oven doors and shower screen "glass" too.
            if (!assetPath.StartsWith(Cars) && !assetPath.StartsWith("Assets/Art/ThirdParty/Quaternius/Cars/")) return;
            var glass = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(CarGlass);
            if (glass == null) return;
            foreach (UnityEngine.Renderer r in root.GetComponentsInChildren<UnityEngine.Renderer>())
            {
                UnityEngine.Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.IndexOf("glass", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        mats[i] = glass;
                        changed = true;
                    }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Animations)) return;
            var importer = (ModelImporter)assetImporter;
            bool rootMotion = assetPath.EndsWith("_RM.fbx");
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = clip.name.Substring(clip.name.LastIndexOf('|') + 1); // "Armature|Walk_Loop" → "Walk_Loop"
                clip.loopTime = clip.name.EndsWith("_Loop") || System.Array.IndexOf(AlsoLooped, clip.name) >= 0;
                if (rootMotion) continue; // left as authored: only measured for speeds
                // In place: people are moved by the simulation, so the root stays put and keeps its authored pose.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
