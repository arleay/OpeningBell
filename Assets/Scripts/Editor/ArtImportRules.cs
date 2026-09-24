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
        /// <summary>Poly Haven CC0 furniture (1K FBX + diffuse and OpenGL normal maps), one folder per model.</summary>
        public const string PolyHaven = "Assets/Art/ThirdParty/PolyHaven/";
        /// <summary>Sketchfab CC-BY packs split into one FBX per prop by Tools/Blender/split_pack.py (credited in-game).</summary>
        public const string Sketchfab = "Assets/Art/ThirdParty/Sketchfab/";

        /// <summary>One-shot clips that staff play on repeat while working.</summary>
        private static readonly string[] AlsoLooped = { "Interact", "PickUp_Table", "Fixing_Kneeling" };

        /// <summary>CC0 ambientCG texture sets for the triplanar city surfaces (colour + normal per set).</summary>
        public const string Surfaces = "Assets/Resources/Surfaces/";

        /// <summary>Bump when a rule changes: Unity re-imports what this postprocessor touched.</summary>
        public override uint GetVersion() => 17;

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
            else if ((assetPath.StartsWith(Kenney) && !assetPath.Contains("/CarKit/")) || assetPath.StartsWith(Props) || assetPath.StartsWith(Nature) || assetPath.StartsWith(PolyHaven) || assetPath.StartsWith(Sketchfab))
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
            if (assetPath.StartsWith(Sketchfab))
            {
                // Props are seen at arm's length at most: 1K is plenty (the street pack ships 2-4K maps).
                var tex = (TextureImporter)assetImporter;
                tex.maxTextureSize = 1024;
                string file = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
                if (file.Contains("normal") || file.EndsWith("_nor") || file.Contains("_nor_")) tex.textureType = TextureImporterType.NormalMap;
                return;
            }
            if (assetPath.StartsWith(PolyHaven))
            {
                if (assetPath.Contains("_nor_gl_")) ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
                return;
            }
            if (assetPath.StartsWith(Surfaces))
            {
                // ambientCG sets: "<Name>_Normal.jpg" is OpenGL-convention (+Y up), which is Unity's own.
                var surface = (TextureImporter)assetImporter;
                if (assetPath.EndsWith("_Normal.jpg")) surface.textureType = TextureImporterType.NormalMap;
                surface.anisoLevel = 4; // roads and sidewalks are seen at grazing angles
                // Terrain layers recolour the grass photo on the CPU (TerrainBuilder.PhotoLayer).
                surface.isReadable = assetPath.EndsWith("/Grass_Color.jpg");
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
                {
                    if (mats[i] == null) continue;
                    if (mats[i].name.IndexOf("glass", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        mats[i] = glass;
                        changed = true;
                    }
                    else if (assetPath.StartsWith(Cars))
                    {
                        // Sketchfab exports often mark whole bodies alpha-blended; the FBX importer then makes paint
                        // and trim see-through. Everything that isn't glass is solid.
                        if (mats[i].renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent || mats[i].IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                            MakeOpaque(mats[i]);
                        // glTF bodies are often single shells marked double-sided, and FBX drops that flag: culling
                        // their back faces left holes you could see the cage through. Detailed cars draw both sides.
                        mats[i].SetFloat("_Cull", 0f);
                        mats[i].doubleSidedGI = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static void MakeOpaque(UnityEngine.Material m)
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = -1;
            if (m.HasProperty("_BaseColor"))
            {
                UnityEngine.Color c = m.GetColor("_BaseColor");
                c.a = 1f;
                m.SetColor("_BaseColor", c);
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
