// A real mirror: shows what a reflected camera (Mirror.cs) rendered into _ReflectionTex, sampled at this pixel's own
// screen position (the reflected camera shares the main camera's projection, so screen UV lines up exactly), under a
// faint tint and a little grime so it reads as glass. Unlit: the reflection already carries the room's lighting.
Shader "OpeningBell/Mirror"
{
    Properties
    {
        _ReflectionTex("Reflection", 2D) = "black" {}
        _BaseColor("Tint", Color) = (0.94,0.96,0.97,1)
        _Fallback("Unlit fallback", Color) = (0.42,0.46,0.5,1)
        _Live("Showing a live reflection", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Mirror"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off // a pane of glass: whichever way the quad was turned, its face shows

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Fallback;
                float _Live;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screen : TEXCOORD0; float2 uv : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.screen = ComputeScreenPos(o.positionCS);
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                half3 reflection = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, uv).rgb;
                // Away from a live camera: a dim, sky-ish grey with a soft vertical gradient.
                half3 idle = _Fallback.rgb * (0.85 + 0.15 * i.uv.y);
                half3 c = lerp(idle, reflection * _BaseColor.rgb, saturate(_Live));
                // Edge darkening: the silvering thins toward the frame.
                float2 e = min(i.uv, 1 - i.uv);
                c *= 0.9 + 0.1 * saturate(min(e.x, e.y) * 20);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
