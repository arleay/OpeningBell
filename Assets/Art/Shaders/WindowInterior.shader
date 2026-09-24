// Window glass with a fake room behind it ("interior mapping", after Joost van Dongen 2008): the view ray is
// traced into a box the size of the bay and storey behind each pane, so outside-only facades read as occupied
// buildings (walls, floor, ceiling, a bit of furniture) with parallax, for the cost of one quad.
// Each pane's room comes in through its UVs (ModularFacade stamps them, MeshBuilder.StampUV):
//   uv.x = floor(storey floor height * 10) + storey height / 10
//   uv.y = floor(bay centre along the face * 10) + bay width / 10
// where "along the face" is world position . cross(up, outward normal).
// The glass itself is URP Lit (dark, glossy: it picks up the sky); the room is added under the reflection,
// daylit by the main light and, for panes whose _EmissionColor is set at night (Palette.ApplyNight), lamp-lit
// in about four rooms out of five.
Shader "OpeningBell/Window Interior"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Glass Tint", Color) = (0.16,0.2,0.24,1)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.88
        _Parallax("Room Depth (m)", Range(1.0, 8.0)) = 4.0
        [HDR] _EmissionColor("Room Lights (night)", Color) = (0,0,0,1)
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
            #pragma fragment WindowFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #ifndef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #endif
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            float4 Hash4(float3 p)
            {
                float4 q = frac(float4(p.xyzx) * float4(0.1031, 0.1030, 0.0973, 0.1099));
                q += dot(q, q.wzxy + 33.33);
                return frac((q.xxyz + q.yzzw) * q.zywx);
            }

            // Colour of the room surface the ray hits (face: 0 side wall, 1 floor, 2 ceiling, 3 back wall), lit.
            half3 Room(float3 hit, int face, float3 size, float4 h, float4 h2, half day, half3 lamp)
            {
                half3 walls[4] = { half3(0.78, 0.74, 0.66), half3(0.62, 0.68, 0.72), half3(0.8, 0.78, 0.74), half3(0.72, 0.62, 0.52) };
                half3 wall = walls[(int)(h.x * 3.99)];
                half3 floorCol = h.y < 0.5 ? half3(0.42, 0.28, 0.18) : half3(0.36, 0.36, 0.38); // boards or carpet
                half3 albedo = face == 1 ? floorCol : face == 2 ? half3(0.85, 0.85, 0.83) : wall;
                if (face == 0) albedo *= 0.82; // side walls a touch darker than the back
                if (face == 3)
                {
                    // Furniture against the back wall: a low dark block (sofa, sideboard, desk) somewhere along it,
                    // and a picture or shelf above it now and then.
                    float x = hit.x - (h2.x - 0.5) * size.x * 0.5;
                    if (abs(x) < 0.5 + h2.y * 0.6 && hit.y < 0.75 + h2.z * 0.3) albedo = lerp(half3(0.2, 0.18, 0.17), half3(0.35, 0.22, 0.16), h2.w);
                    else if (h2.w > 0.45 && abs(x) < 0.35 && abs(hit.y - 1.55) < 0.25) albedo = half3(0.3, 0.38, 0.45);
                }
                // A ceiling lamp in the middle of the room: brighter near it, dimmer deep and low.
                float3 toLamp = float3(hit.x, size.y - 0.1, size.z * 0.5) - hit;
                half falloff = saturate(1.6 / (1.0 + dot(toLamp, toLamp) * 0.35));
                half3 light = day * (0.5 + 0.35 * saturate(1.0 - hit.z / size.z)) + lamp * (0.35 + falloff); // daylight falls off away from the window
                return albedo * light;
            }

            void WindowFragment(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 n = normalize(input.normalWS);
                float3 p = input.positionWS;
                float3 v = normalize(p - GetCameraPositionWS());

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb * 0.25;
                surface.alpha = 1.0;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                InputData inputData;
                InitializeInputData(input, surface.normalTS, inputData);
                InitializeBakedGIData(input, inputData);
                half4 color = UniversalFragmentPBR(inputData, surface);

                // Only the vertical, outward faces show a room (the pane's edges and top stay plain glass).
                if (abs(n.y) < 0.5 && dot(v, n) < 0.0)
                {
                    float3 t = normalize(cross(float3(0, 1, 0), n));
                    float2 uv = input.uv;
                    float floorY = floor(uv.x) * 0.1, storey = frac(uv.x) * 10.0;
                    float centre = floor(uv.y) * 0.1, width = frac(uv.y) * 10.0;
                    float3 size = float3(max(width, 1.0), max(storey, 2.2), _Parallax);
                    // Ray in the room's frame: x along the face from the bay centre, y up from the floor, z into the building.
                    float3 o = float3(dot(p, t) - centre, p.y - floorY, 0.0);
                    o.xy = clamp(o.xy, float2(-size.x * 0.5 + 0.01, 0.01), float2(size.x * 0.5 - 0.01, size.y - 0.01));
                    float3 d = float3(dot(v, t), v.y, -dot(v, n));
                    float3 bound = float3(d.x > 0 ? size.x * 0.5 : -size.x * 0.5, d.y > 0 ? size.y : 0.0, size.z);
                    float3 tAxis = (bound - o) / (abs(d) > 1e-4 ? d : 1e-4);
                    float tHit = min(tAxis.x, min(tAxis.y, tAxis.z));
                    float3 hit = o + d * tHit;
                    int face = tHit == tAxis.z ? 3 : tHit == tAxis.x ? 0 : (d.y > 0 ? 2 : 1);

                    float4 h = Hash4(float3(centre, floorY, floor(dot(p, n) * 2.0 + 0.5)));
                    float4 h2 = Hash4(float3(floorY * 1.7, centre * 0.37, 11.0));
                    Light sun = GetMainLight();
                    half day = saturate(sun.direction.y * 3.0) * saturate(Luminance(sun.color));
                    // Lamps on in ~4 rooms in 5 of the lit panes, warm or cool bulbs.
                    half3 lamp = _EmissionColor.rgb * (h.z < 0.8 ? 1.0 : 0.0) * (h.w < 0.7 ? half3(1.25, 1.1, 0.95) : half3(1.0, 1.05, 1.15)) * 1.6;
                    half3 room = Room(hit, face, size, h, h2, day, lamp);
                    // Blinds half-down in some rooms: stripes across the top of the pane, in front of the room.
                    if (h.w > 0.78 && o.y > size.y * (0.55 + h.x * 0.2))
                        room = lerp(room, half3(0.7, 0.68, 0.62) * (day * 0.35 + lamp * 0.8), step(0.35, frac(o.y * 14.0)));
                    // Grazing views see the reflection (Fresnel); looking straight in sees the room.
                    half fresnel = pow(1.0 - saturate(dot(-v, n)), 4.0);
                    color.rgb += room * (1.0 - fresnel) * 0.9;
                }

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                outColor = color;
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
