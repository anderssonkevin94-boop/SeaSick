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

        _MoonColor ("Moon",         Color) = (0.92, 0.94, 1.0, 1)
        _MoonSize  ("Moon Radius",  Range(0.01, 0.12)) = 0.045
        _MoonHalo  ("Moon Halo",    Range(0, 1)) = 0.35
        _StarBright  ("Star Brightness", Range(0, 3)) = 1.0
        _StarDensity ("Star Density",    Range(0.9, 0.999)) = 0.948

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
                float4 _MoonColor;
                float  _MoonSize;
                float  _MoonHalo;
                float  _StarBright;
                float  _StarDensity;
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
            // Direction TOWARD the moon. Pushed independently of _SS_SunDir and
            // of whichever body the directional light happens to be playing —
            // if the disc positions came off the light, the sun would follow
            // the moon all night.
            float4 _SS_MoonDir;
            // 0 = new, 1 = full. Only feeds the halo; the terminator below is
            // geometry, so the phase draws itself from the two directions.
            float  _SS_MoonPhase;
            // 0 = broad daylight, 1 = full night. Phrased this way so that an
            // UNSET global renders exactly what this shader rendered before
            // night existed.
            float  _SS_Night;
            // xyz = the celestial pole, w = hour angle. The stars turn about
            // this, so they hold station against each other while the whole
            // sky wheels — which is the thing that reads as a night passing.
            float4 _SS_StarRot;
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

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // Rodrigues. Cheaper than passing a matrix and keeps the shader
            // ignorant of latitude — SkyDirector owns where the pole is.
            float3 RotateAxis(float3 v, float3 axis, float ang)
            {
                float c = cos(ang), s = sin(ang);
                return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
            }

            // A fixed field of stars on the celestial sphere. The direction is
            // rotated back by the hour angle first, so the pattern is the same
            // every night and only its orientation moves.
            float Stars(float3 dir)
            {
                float3 sd = RotateAxis(dir, normalize(_SS_StarRot.xyz), -_SS_StarRot.w);

                float3 g = sd * 90.0;
                float3 cell = floor(g);
                float3 f = frac(g) - 0.5;
                float3 h = Hash33(cell);

                // Most cells are empty; the ones that are not put their star
                // somewhere inside rather than dead centre, or the field reads
                // as a grid.
                float present = step(_StarDensity, h.x);
                float d = length(f - (h - 0.5) * 0.72);
                // The falloff has to be measured against a PIXEL, not chosen
                // for looks. A cell here is 1/90 rad across, which at this
                // camera is under four pixels; the first version faded over
                // 1/9 of a cell -- a star about four TENTHS of a pixel wide,
                // so almost every one fell between sample points and the
                // measured sky came back with no pixel above 25/255 and not a
                // star in it. Fading over ~0.3 of a cell puts the core near a
                // pixel across; the cube keeps it a point rather than a blob.
                float pip = saturate(1.0 - d * 3.2);
                pip = pip * pip * pip;
                // h.y squared biases the field toward faint stars, which is
                // right -- but 0.30 put most of them under the threshold where
                // a star is distinguishable from the sky at all (measured: 25
                // pixels above 30/255 across a whole night sky).
                float mag = lerp(0.42, 1.0, h.y * h.y);

                // Scintillation, at a different rate per star.
                float twinkle = 0.72 + 0.28 * sin(_Time.y * 2.4 + h.z * 61.0);
                return present * mag * pip * twinkle;
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
                //
                // Gated on the sun's own elevation as well: without that, a sun
                // three degrees under the horizon still throws its haze across
                // the sky it is behind, and the night never gets dark on the
                // eastern side.
                float sd = saturate(dot(dir, _SS_SunDir.xyz));
                float clear = 1.0 - _Overcast;
                float sunUp = saturate((_SS_SunDir.y + 0.06) / 0.10);
                float haze = pow(sd, _SunHaze) * 0.35 * clear * sunUp;
                float disc = pow(sd, _SunGlow) * clear * clear * sunUp;
                col += _SunColor.rgb * (haze + disc * 1.6);

                // --- Moon -----------------------------------------------------
                // A real disc with a real terminator rather than a bright dot:
                // at five degrees across the phase genuinely reads, and it is
                // the one thing that tells you which night of a voyage you are
                // on without a UI saying so.
                float md = dot(dir, _SS_MoonDir.xyz);
                float moonUp = saturate((_SS_MoonDir.y + 0.04) / 0.10);
                float moonlit = _SS_Night * moonUp * clear;

                // Offset from the moon's centre, in units of its radius. Inside
                // the disc this is the surface point; the near-side normal adds
                // the component pointing back at the viewer.
                float3 tang = dir - _SS_MoonDir.xyz * md;
                float3 u = tang / max(1e-4, _MoonSize);
                float r = length(u);
                float inDisc = step(0.0, md) * (1.0 - smoothstep(0.90, 1.02, r));
                // Named mn, not n: the cloud deck below already owns `n` for
                // its noise sample, and a redefinition here reports as a
                // MAGENTA sky with a clean C# compile.
                float3 mn = u - _SS_MoonDir.xyz * sqrt(saturate(1.0 - r * r));
                float lit = smoothstep(-0.06, 0.12, dot(normalize(mn), _SS_SunDir.xyz));

                float halo = pow(saturate(md), 900.0) * _MoonHalo
                           * lerp(0.25, 1.0, _SS_MoonPhase);
                col += _MoonColor.rgb * moonlit * (inDisc * lit * 1.5 + halo);

                // --- Stars ----------------------------------------------------
                // Behind the cloud deck, so an overcast night has none.
                col += _MoonColor.rgb * Stars(dir)
                     * _StarBright * _SS_Night * clear
                     * smoothstep(-0.02, 0.10, dir.y);

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
