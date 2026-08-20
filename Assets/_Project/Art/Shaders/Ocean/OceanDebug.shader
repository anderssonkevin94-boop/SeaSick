// Lab-only surface: sums the cascade displacement in the vertex stage and
// lights with a plain lambert + fresnel so wave shape is readable. The real
// shading lives in Ocean.shader (M8); keep this one dumb on purpose.
Shader "SeaSick/OceanDebug"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.02, 0.09, 0.16, 1)
        _LightColor ("Light", Color) = (0.25, 0.55, 0.6, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_Ocean_Displacement);
            SAMPLER(sampler_Ocean_Displacement);
            TEXTURE2D_ARRAY(_Ocean_Derivatives);
            SAMPLER(sampler_Ocean_Derivatives);
            float4 _Ocean_PatchSizes;
            float4 _Ocean_FadeParams;      // x = fade start, y = fade end
            float4 _Ocean_CascadeWeights;  // per-ring, via MaterialPropertyBlock

            CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _LightColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 derivs : TEXCOORD1; // sx, sz, dxx, dzz summed
            };

            float3 SampleDisplacement(float2 worldXZ, float fade, out float4 derivs)
            {
                float3 d = 0;
                derivs = 0;
                [unroll]
                for (int c = 0; c < 3; c++)
                {
                    float w = _Ocean_CascadeWeights[c] * fade;
                    if (w <= 0.001) continue;
                    float2 uv = worldXZ / _Ocean_PatchSizes[c];
                    d += w * SAMPLE_TEXTURE2D_ARRAY_LOD(_Ocean_Displacement,
                        sampler_Ocean_Displacement, uv, c, 0).xyz;
                    derivs += w * SAMPLE_TEXTURE2D_ARRAY_LOD(_Ocean_Derivatives,
                        sampler_Ocean_Derivatives, uv, c, 0);
                }
                return d;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                // Distance fade keeps the horizon line flat and readable.
                float dist = distance(ws.xz, GetCameraPositionWS().xz);
                float fade = 1.0 - smoothstep(_Ocean_FadeParams.x, _Ocean_FadeParams.y, dist);
                float4 derivs;
                float3 disp = SampleDisplacement(ws.xz, fade, derivs);
                ws += float3(disp.x, disp.y, disp.z);
                o.positionWS = ws;
                o.derivs = derivs;
                o.positionHCS = TransformWorldToHClip(ws);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Slope -> normal, corrected for horizontal squeeze at crests.
                float2 slope = input.derivs.xy /
                    max(float2(1.0, 1.0) + input.derivs.zw, 0.1);
                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));

                Light sun = GetMainLight();
                half ndl = saturate(dot(n, sun.direction));
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 5.0);

                half3 col = lerp(_DeepColor.rgb, _LightColor.rgb, ndl * 0.7 + fresnel * 0.5);
                return half4(col * sun.color, 1);
            }
            ENDHLSL
        }
    }
}
