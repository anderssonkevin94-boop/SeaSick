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
// One deliberate difference: point lights use the ISLAND contract (the
// linear ~22 m fade of EnvironmentToon / TerrainVertexColor), not URP's
// inverse-square, and the loop also runs in Forward+ (see the frag). The
// camp's torches and campfire are authored for that contract; villagers live
// on islands. Vertex colours are a LINEAR palette (Blender export, same as
// the Coaster meshes: cream shirt = 0.66 linear = #D4CBB8..#DCD3C0), so no
// sRGB->linear conversion belongs here.
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

                // Torches, the campfire, hut windows and deck lanterns.
                //
                // Two fixes (2026-10-01, Kevin: villagers read dark and unlit
                // beside the torches):
                //  1. Both renderers are Forward+, and URP does NOT enable
                //     _ADDITIONAL_LIGHTS in Forward+ (ForwardLights.cs sets
                //     AdditionalLightsPixel only when !m_UseForwardPlus), so a
                //     loop behind "#if defined(_ADDITIONAL_LIGHTS)" alone was
                //     compiled out and no point light ever reached the crew.
                //  2. Every island light is authored for the island shaders'
                //     contract (EnvironmentToon / TerrainVertexColor: "the
                //     shaders fade linearly over the range"): a squared linear
                //     fade to zero at ~22 m, URP's own range fade kept, and a
                //     wrapped Lambert. URP's inverse-square gave a villager 3 m
                //     from a torch ~1/8 of the light the path under him got.
                //     Same maths here so a villager is lit like the ground he
                //     stands on.
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                {
                    // Keep in step with EnvironmentToon.shader / TerrainVertexColor.shader.
                    const float LampFadePerMetre = 0.045;  // linear fade, 0 at ~22 m
                    const float LampWrapScale    = 0.6;    // wrapped Lambert: n.l * .6 + .4,
                    const float LampWrapFloor    = 0.4;    // so the far side of a face still warms
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS);
                        #if USE_CLUSTER_LIGHT_LOOP
                            int pidx = lightIndex;
                        #else
                            int pidx = GetPerObjectLightIndex(lightIndex);
                        #endif
                        #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                            float3 lp = _AdditionalLightsBuffer[pidx].position.xyz;
                        #else
                            float3 lp = _AdditionalLightsPosition[pidx].xyz;
                        #endif
                        float3 toLamp = lp - i.positionWS;
                        float distanceSqr = max(dot(toLamp, toLamp), HALF_MIN);
                        // Cancel inverse-square, keep URP's range/spot fade.
                        float rangeFade = saturate(l.distanceAttenuation * distanceSqr);
                        float fall = saturate(1.0 - sqrt(distanceSqr) * LampFadePerMetre);
                        fall *= fall;
                        float wrap = saturate(dot(n, l.direction) * LampWrapScale + LampWrapFloor);
                        lighting += l.color * fall * rangeFade * wrap * l.shadowAttenuation;
                    LIGHT_LOOP_END
                }
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
