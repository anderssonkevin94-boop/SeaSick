// The crew and villagers, lit like the ship (2026-10-01, Kevin: "the same
// light as you did the ship").
//
// This is SeaSick/Coaster Paint's lighting model, line for line, on the crew's
// albedo contract:
//
//   albedo = vertex colour * _BaseColor
//
//   CREW_Cloth  vertex colour IS the colour, _BaseColor stays white.
//   CREW_Skin   vertex colour is white/greyscale shade, _BaseColor is the hue,
//               and CrewAgent pushes _BaseColor toward green through a
//               MaterialPropertyBlock when the sea makes them sick.
//
// What the ship's model adds over the old SeaSick/Crew Vertex Color (which is
// why villagers under the sawmill tarp at dusk went near-black):
//   * a broad hemisphere sky/bounce fill that fades with SkyDirector's
//     _SS_Night / _SS_Storminess globals, so night stays dark and lanterns
//     stay the key,
//   * a wrapped diffuse ((N.L + .25) / 1.25) instead of a hard N.L cut,
//   * a shadow floor (22 % of the sun survives in shadow) instead of zero,
//   * a cool moonlit fill at night and a restrained broad highlight.
// Keep the constants in step with CoasterPaint.shader; the ship is the
// reference and is not touched from here.
//
// The Coaster's "iron" palette remap is left out on purpose: it turns
// near-neutral dark vertex colours charcoal-blue, which on a person is hair
// and boots, not iron.
Shader "SeaSick/Crew Paint"
{
    Properties
    {
        _BaseColor ("Base tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            // SkyDirector globals: the fill follows time of day and weather.
            float _SS_Night;
            float _SS_Storminess;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float  fog        : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.color      = v.color;
                o.fog        = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));

                // --- CoasterPaint lighting, same constants ---
                float day = (1 - saturate(_SS_Night)) * lerp(1, .55, saturate(_SS_Storminess));
                float hemi = saturate(n.y * .5 + .5);
                float3 bounce = lerp(float3(.21,.18,.145), float3(.42,.46,.52), hemi) * day;
                float3 ambient = max(SampleSH(n), float3(.018,.024,.034)) + bounce;
                ambient += float3(.025,.045,.08) * saturate(_SS_Night) * (.35 + .65 * hemi);
                float diffuse = saturate((dot(n, main.direction) + .25) / 1.25);
                float visibility = lerp(.22, 1, main.shadowAttenuation);
                float3 lighting = ambient + main.color * diffuse * visibility;

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 halfDir = SafeNormalize(main.direction + viewDir);
                float3 specular = main.color * pow(saturate(dot(n, halfDir)), 12) * .065 * diffuse * visibility;

                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    lighting += l.color * saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                LIGHT_LOOP_END
                #endif

                float3 albedo = i.color.rgb * _BaseColor.rgb;
                return half4(MixFog(albedo * lighting + specular, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vertShadow(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 fragShadow() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vertDepth(A v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V vertDN(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o;
            }
            half4 fragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
