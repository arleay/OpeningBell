// World-space triplanar version of URP Lit, for the generated city: procedural boxes and spans have no useful UVs
// (a stretched 0-1 per face), so the texture is projected from world X, Y and Z and blended by the surface normal.
// Keeps Lit's material layout (LitInput.hlsl) so the SRP batcher still batches it and Lit's shadow/depth passes
// can be reused as they are. Repurposed Lit properties:
//   _BaseMap_ST.x  tiles per metre (e.g. 0.4 = one 2.5 m tile)
//   _Parallax      texture contrast: 1 = as photographed, lower pulls it toward its average colour (stylised)
// The texture is divided by its own average colour, so _BaseColor is what the surface averages to: callers keep
// choosing colours as before and the photo only adds detail (brick courses, grain, stains) around them.
//   _OcclusionStrength  photo colour kept: 1 = its hue variation (brick vs mortar), 0 = light and shade only (paint)
//   _EmissionColor multiplies the albedo: a textured wall can still carry the "bounce light" glow (Palette.Bounce)
Shader "OpeningBell/Triplanar Lit"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.1
        _Parallax("Texture Contrast", Range(0.0, 1.0)) = 0.8
        _OcclusionStrength("Photo Colour Kept", Range(0.0, 1.0)) = 1.0
        [HDR] _EmissionColor("Glow (x albedo)", Color) = (0,0,0,1)
        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment TriplanarFragment

            // Lit's lighting keywords for this URP version (Forward+ uses the cluster loop).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // The triplanar projection needs the world position in the fragment.
            #ifndef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #endif
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            // Colour sample relative to the texture's mean (its smallest mip), its contrast scaled by _Parallax.
            half3 Albedo(float2 uv, half3 mean)
            {
                half3 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                half3 relative = lerp(Luminance(texel) / max(Luminance(mean), 0.02), texel / max(mean, 0.02), _OcclusionStrength);
                return lerp(1.0, relative, _Parallax);
            }

            void TriplanarFragment(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 n = normalize(input.normalWS);
                float3 p = input.positionWS * _BaseMap_ST.x + _BaseMap_ST.zwz;
                // Sharp blend: boxes are mostly one projection; slopes and cylinders cross-fade over a narrow band.
                half3 w = pow(abs(n), 4.0);
                w /= (w.x + w.y + w.z);
                // Mirror per axis sign so textures read the right way round on every face (and bricks stay upright).
                half3 s = (n >= 0) * 2.0 - 1.0;
                float2 uvX = float2(p.z * s.x, p.y);
                float2 uvY = float2(p.x * s.y, p.z);
                float2 uvZ = float2(-p.x * s.z, p.y);

                half3 mean = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, float2(0.5, 0.5), 12).rgb;
                half3 albedo = Albedo(uvX, mean) * w.x + Albedo(uvY, mean) * w.y + Albedo(uvZ, mean) * w.z;
                // Weathering: the same photo sampled ~12x larger (and stretched vertically on walls, like rain
                // streaks) as a soft +-10% light/dark wash. Breaks up the tiling and makes walls read as aged.
                half invMean = 1.0 / max(Luminance(mean), 0.02);
                half wx = Luminance(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX * float2(0.09, 0.025)).rgb) * invMean;
                half wy = Luminance(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY * 0.07).rgb) * invMean;
                half wz = Luminance(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ * float2(0.09, 0.025)).rgb) * invMean;
                albedo *= clamp(lerp(1.0, wx * w.x + wy * w.y + wz * w.z, 0.35), 0.82, 1.12);

                // Whiteout-blended tangent normals (Ben Golus, "Normal Mapping for a Triplanar Shader").
                half3 tX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
                half3 tY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
                half3 tZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);
                tX.x *= s.x; tY.x *= s.y; tZ.x *= -s.z;
                tX = half3(tX.xy + n.zy, abs(tX.z) * n.x);
                tY = half3(tY.xy + n.xz, abs(tY.z) * n.y);
                tZ = half3(tZ.xy + n.xy, abs(tZ.z) * n.z);
                half3 normalWS = normalize(tX.zyx * w.x + tY.xzy * w.y + tZ.xyz * w.z);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo * _BaseColor.rgb;
                surface.alpha = 1.0;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.emission = surface.albedo * _EmissionColor.rgb;

                InputData inputData;
                InitializeInputData(input, surface.normalTS, inputData);
                inputData.normalWS = normalWS;
                InitializeBakedGIData(input, inputData);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                outColor = color;
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        // Shadows and depth don't need the texture: Lit's own passes, which read the same material layout.
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
