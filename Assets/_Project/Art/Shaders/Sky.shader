// Procedural sky dome.
//
// Built rather than imported for two reasons. The sea has to match the sky —
// a static cubemap would leave the water the same blue under a black storm —
// and the whole picture has to swing from a clear shelf to a western storm on
// one number, which a pair of cross-faded cubemaps does badly and expensively.
//
// Everything here is driven from SkyDirector. The gradient, the cloud deck and
// the sun are all separate so that TimeOfDay01 (milestone 3) can move them
// independently later without touching this shader's structure.
Shader "SeaSick/Sky"
{
    Properties
    {
        _ZenithColor  ("Zenith",          Color) = (0.17, 0.38, 0.66, 1)
        _HorizonColor ("Horizon",         Color) = (0.68, 0.80, 0.88, 1)
        _GroundColor  ("Below Horizon",   Color) = (0.22, 0.30, 0.34, 1)
        _HorizonSharp ("Horizon Falloff", Range(0.2, 6)) = 1.6

        _SunColor  ("Sun",       Color) = (1, 0.96, 0.86, 1)
        _SunGlow   ("Sun Glow",  Range(1, 600)) = 140
        _SunHaze   ("Sun Haze",  Range(1, 60))  = 8

        _CloudLit  ("Cloud Lit",    Color) = (0.94, 0.95, 0.97, 1)
        _CloudDark ("Cloud Shadow", Color) = (0.17, 0.18, 0.21, 1)
        _Overcast  ("Overcast",     Range(0, 1)) = 0.08
        _Scud      ("Low Scud",     Range(0, 1)) = 0
        _CloudScale ("Cloud Scale", Float) = 0.030
        _CloudSpeed ("Cloud Speed", Float) = 0.9

        _Exposure ("Exposure", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float  _HorizonSharp;
                float4 _SunColor;
                float  _SunGlow;
                float  _SunHaze;
                float4 _CloudLit;
                float4 _CloudDark;
                float  _Overcast;
                float  _Scud;
                float  _CloudScale;
                float  _CloudSpeed;
                float  _Exposure;
            CBUFFER_END

            // Pushed by SkyDirector each frame. xyz = direction TOWARD the sun.
            float4 _SS_SunDir;
            // xy = wind direction on the water plane; the cloud deck runs with it.
            float4 _SS_SkyWind;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float SkyHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float SkyNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = SkyHash(i);
                float b = SkyHash(i + float2(1, 0));
                float c = SkyHash(i + float2(0, 1));
                float d = SkyHash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float SkyFbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int o = 0; o < 4; o++)
                {
                    v += a * SkyNoise(p);
                    p *= 2.07;
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // --- Gradient -------------------------------------------------
                float up = saturate(dir.y);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb,
                                  pow(up, 1.0 / max(0.2, _HorizonSharp)));
                col = lerp(_GroundColor.rgb, col, smoothstep(-0.05, 0.02, dir.y));

                // --- Sun ------------------------------------------------------
                // Two terms: a wide haze that lifts the whole quarter of sky the
                // sun is in, and a tight disc. Both are put out by cloud.
                float sd = saturate(dot(dir, _SS_SunDir.xyz));
                float clear = 1.0 - _Overcast;
                float haze = pow(sd, _SunHaze) * 0.35 * clear;
                float disc = pow(sd, _SunGlow) * clear * clear;
                col += _SunColor.rgb * (haze + disc * 1.6);

                // --- Cloud deck -----------------------------------------------
                // The view direction projected onto a plane overhead. Near the
                // horizon this stretches toward infinity, which is exactly what
                // a cloud deck does — it compresses into a bank on the skyline.
                float2 wind = _SS_SkyWind.xy;
                float2 plane = dir.xz / max(dir.y, 0.06);
                float2 cuv = plane * _CloudScale + wind * (_Time.y * _CloudSpeed * 0.01);

                float n = SkyFbm(cuv);
                // Overcast lowers the threshold, so cloud spreads from scattered
                // tops to a lid over the whole sky.
                float cover = lerp(0.68, 0.24, _Overcast);
                float density = saturate((n - cover) / max(0.02, 1.0 - cover));
                density = density * density * (3.0 - 2.0 * density);

                // Thick cloud is dark underneath; its edges catch the light.
                float3 cloudCol = lerp(_CloudLit.rgb, _CloudDark.rgb,
                                       saturate(density * lerp(0.65, 1.35, _Overcast)));
                cloudCol += _SunColor.rgb * pow(sd, 24.0) * (1.0 - density) * 0.5 * clear;

                // Fade the deck out at the skyline so the sea meets the sky in
                // fog, not in an aliasing mess of stretched noise.
                float lift = smoothstep(0.0, 0.13, dir.y);
                col = lerp(col, cloudCol, saturate(density * lift));

                // --- Low scud --------------------------------------------------
                // Ragged cloud tearing past underneath the deck, three times the
                // speed. This is what reads as wind rather than weather.
                if (_Scud > 0.001)
                {
                    float2 suv = plane * (_CloudScale * 2.4)
                               + wind * (_Time.y * _CloudSpeed * 0.038);
                    float sn = SkyFbm(suv);
                    float sdens = saturate((sn - 0.42) / 0.58);
                    sdens *= sdens;
                    col = lerp(col, _CloudDark.rgb * 0.82,
                               saturate(sdens * _Scud * lift * 0.85));
                }

                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
