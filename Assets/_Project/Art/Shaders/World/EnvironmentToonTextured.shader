// "SeaSick/Environment Toon" with one colour map (2026-09-28, the level 1
// wall, art-staging/wall-textured-v3). Everything but the albedo is a
// copy of EnvironmentToon.shader -- keep the two lighting blocks in step.
// albedo = colour map (UV0, Repeat, used as authored: values outside 0-1
// are the tiling) * vertex colour * _BaseColor, as the Blender source
// multiplies them. With the default white map it is Environment Toon.
Shader "SeaSick/Environment Toon Textured"
{
    Properties
    {
        _BaseMap ("Colour map", 2D) = "white" {}
        _BaseColor ("Base tint", Color) = (1,1,1,1)
        _Ambient ("Ambient floor", Range(0,1)) = 0.0
        // 1 = the vertex colours are sRGB (Blender's FBX colours) and are
        // linearised before use. Off by default: the textured kits were tuned
        // with the raw value. The level 2 wall is vertex colour only, and raw it
        // drew far paler than its Blender source (Kevin 2026-10-04: "so light it
        // almost looks like a blueprint").
        [Toggle] _VertexSRGB ("Vertex colour is sRGB", Float) = 0
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
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RichLight.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Ambient;
                float _VertexSRGB;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float  fog        : TEXCOORD2;
                float2 uv         : TEXCOORD3;
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
                o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog        = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                // The project renders in Linear; a vertex colour is not converted
                // on import, so an sRGB one is linearised here when the material
                // says so (`_VertexSRGB`).
                float3 vc = i.color.rgb;
                if (_VertexSRGB > 0.5)
                    vc = vc <= 0.04045 ? vc / 12.92 : pow((vc + 0.055) / 1.055, 2.4);
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb
                              * vc * _BaseColor.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                float ndl = saturate(dot(n, light.direction));
                // Rich light (RichLight.hlsl): the ship's ambient and its 22 %
                // shadow floor; the three cel bands stay, edges a touch softer.
                // rich = 0 is the old look exactly.
                float rich = SS_Rich();
                float edge = max(lerp(.012,.03,rich),fwidth(ndl)*1.2);
                float lowBand = lerp(.16,.22,rich);
                float band = lowBand + (.60-lowBand)*smoothstep(.32-edge,.32+edge,ndl)
                                  + .40*smoothstep(.72-edge,.72+edge,ndl);
                float3 diffuse = light.color * band * lerp(lowBand,1,smoothstep(.4,.6,light.shadowAttenuation));
                // _Ambient stays additive on both sides: flames and pier glows use it as emission.
                float3 ambient = lerp(max(0,SampleSH(float3(0,1,0))) * float3(.48,.55,.68), SS_ShipAmbient(n), rich) + _Ambient;
                float3 col = albedo * (diffuse + ambient);
                // Point lights: the campfire and the lamps. URP's own falloff
                // is inverse-square, which lights a fire's stone ring and
                // nothing past it; a camp has to read from the water, so
                // this is a stylised linear fade over ~22 m with wrapped
                // Lambert (the slope behind the fire still glows). Works in
                // both the Forward and the Forward+ (cluster) paths.
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light pl = GetAdditionalLight(lightIndex, i.positionWS);
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
                        // Retain Unity's real range/spot fade while keeping the
                        // stylised broad glow instead of inverse-square dimming.
                        float rangeFade = saturate(pl.distanceAttenuation * distanceSqr);
                        float fall = saturate(1.0 - sqrt(distanceSqr) * 0.045);
                        fall *= fall;
                        float wrap = saturate(dot(n, pl.direction) * 0.6 + 0.4);
                        col += albedo * pl.color * fall * rangeFade * wrap;
                    LIGHT_LOOP_END
                }
                #endif
                col = MixFog(col, i.fog);
                return half4(col, 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vertShadow(A v) : SV_POSITION
            {
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            float4 vertDepth(A v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
