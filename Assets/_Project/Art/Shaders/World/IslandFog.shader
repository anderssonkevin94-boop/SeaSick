// The cloud over an unexplored island (Kevin, 2026-09-30: "7 · Landing
// party: Explore (fog)"). One transparent mesh per fogged island, draped a
// canopy above the ground (`IslandFogView`), alpha from that island's fog
// grid as an R8 texture (1 = under cloud, 0 = explored; the view eases each
// cell over ~1 s so opening ground fades rather than pops).
//
// Soft on purpose: five bilinear taps blur the 5 m cells into round-edged
// patches, and a slow two-octave value noise both billows the edge and
// breathes the density, so the cover reads as low cloud and not as a mask.
// The land under it is dimmed and greyed (the cloud is mostly but never
// fully opaque) and a tree under it is a faint shape at most.
//
// Cost: one draw per fogged island in view, no depth write, five texture
// taps and eight hashes a pixel. No keywords beyond URP's fog.
Shader "SeaSick/Island Fog"
{
    Properties
    {
        _FogTex ("Fog grid (R8)", 2D) = "black" {}
        _FogRect ("Grid origin xz, 1/size xz", Vector) = (0,0,0.01,0.01)
        _FogTexel ("Texel size", Vector) = (0.01,0.01,0,0)
        _CloudColor ("Cloud", Color) = (0.97,0.975,0.98,1)
        _ShadeColor ("Cloud shade", Color) = (0.74,0.78,0.85,1)
        _Density ("Density (min, max)", Vector) = (0.74,0.95,0,0)
        _Fade ("Whole-island fade", Range(0,1)) = 1
        _NoiseScale ("Billow scale (1/m)", Float) = 0.045
        _Drift ("Drift (m/s)", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_FogTex);
            SAMPLER(sampler_FogTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FogTex_ST;
                float4 _FogRect;
                float4 _FogTexel;
                float4 _CloudColor;
                float4 _ShadeColor;
                float4 _Density;
                float _Fade;
                float _NoiseScale;
                float _Drift;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash12(i);
                float b = hash12(i + float2(1, 0));
                float c = hash12(i + float2(0, 1));
                float d = hash12(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = (i.positionWS.xz - _FogRect.xy) * _FogRect.zw;
                float2 t = _FogTexel.xy * 0.9;
                // Five bilinear taps: the 5 m cells become soft round patches.
                float fog = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv).r * 0.36
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2( t.x, 0)).r * 0.16
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2(-t.x, 0)).r * 0.16
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2(0,  t.y)).r * 0.16
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2(0, -t.y)).r * 0.16;

                // Slow billows drifting with a light air.
                float2 q = i.positionWS.xz * _NoiseScale + float2(_Time.y, _Time.y * 0.6) * (_Drift * _NoiseScale);
                float n = vnoise(q) * 0.65 + vnoise(q * 2.3 + 17.0) * 0.35;

                // Billowed edge, breathing density inside.
                float edge = smoothstep(0.12, 0.88, fog + (n - 0.5) * 0.35);
                float a = edge * lerp(_Density.x, _Density.y, n);

                // Seen edge-on (from the sea, the skirt at the coast) a cloud
                // is thicker than seen from above.
                float3 face = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                float3 toCam = GetCameraPositionWS() - i.positionWS;
                float dist = length(toCam);
                float facing = abs(dot(face, toCam / max(dist, 0.001)));
                a = 1.0 - pow(saturate(1.0 - a), 1.0 / max(facing, 0.3));
                // Never a white wall in the lens: thin out right at the camera.
                a *= saturate((dist - 3.0) / 14.0);
                a *= _Fade;

                // Lit like weather: sun-side tops bright, the rest cool grey,
                // night and dusk through the same light the land gets.
                Light light = GetMainLight();
                float3 up = float3(0, 1, 0);
                float sun = saturate(dot(up, light.direction)) * 0.5 + 0.5;
                float3 base = lerp(_ShadeColor.rgb, _CloudColor.rgb, saturate(n * 1.2 - 0.1));
                float3 lit = light.color * (0.35 + 0.35 * sun) + max(0, SampleSH(up)) * 0.8;
                float3 col = base * lit;
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
