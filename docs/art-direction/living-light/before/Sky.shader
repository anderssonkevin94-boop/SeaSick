// Weather-driven sky with an optional graphic-adventure clear-day panorama.
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

        [NoScaleOffset] _AdventurePanorama ("Painted cloud source", 2D) = "white" {}
        _AdventureStrength ("Painted cloud layer", Range(0,1)) = 0
        _AdventureRotation ("Graphic adventure rotation", Range(0,360)) = 0
        _AdventureSeamWidth ("Panorama wrap blend", Range(0.001,0.08)) = 0.025
        _CloudDrift ("Painted cloud drift (degrees per second)", Float) = 0.22
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
                float4 _AdventurePanorama_TexelSize;
                float _AdventureStrength, _AdventureRotation, _AdventureSeamWidth, _CloudDrift;
                float _CloudReviewTime, _CloudReviewOverride;
            CBUFFER_END

            TEXTURE2D(_AdventurePanorama);
            SAMPLER(sampler_AdventurePanorama);

            float3 AdventureSky(float3 dir)
            {
                float2 border = abs(_AdventurePanorama_TexelSize.xy) * 0.5;
                float u = frac(atan2(dir.x,dir.z) / 6.28318530718 + 0.5 + _AdventureRotation/360.0);
                float v = clamp(asin(clamp(dir.y,-1.0,1.0))/3.14159265359 + 0.5,border.y,1.0-border.y);
                // Correct the longitude derivative at the wrap. Otherwise a
                // single seam pixel selects the coarsest mip and paints a stripe.
                float2 gx = ddx(float2(u,v)), gy = ddy(float2(u,v));
                gx.x -= round(gx.x); gy.x -= round(gy.x);
                float3 a = SAMPLE_TEXTURE2D_GRAD(_AdventurePanorama,sampler_AdventurePanorama,float2(clamp(u,border.x,1.0-border.x),v),gx,gy).rgb;
                float3 b = SAMPLE_TEXTURE2D_GRAD(_AdventurePanorama,sampler_AdventurePanorama,float2(clamp(1.0-u,border.x,1.0-border.x),v),gx*float2(-1,1),gy*float2(-1,1)).rgb;
                float weight = 0.5*(1.0-smoothstep(0.0,max(0.001,_AdventureSeamWidth),min(u,1.0-u)));
                float3 sky = lerp(a,b,weight);
                float poleV = dir.y>0 ? 1.0-border.y : border.y;
                float3 pole = SAMPLE_TEXTURE2D(_AdventurePanorama,sampler_AdventurePanorama,float2(0.5,poleV)).rgb;
                return lerp(sky,pole,smoothstep(0.96,1.0,abs(dir.y)));
            }

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
            // Storminess along eight compass bearings a few km out — A is
            // N/NE/E/SE, B is S/SW/W/NW — plus the two ends the skyline
            // blends between. _SS_RoseStrength is 0 when nothing is pushing a
            // rose, and the horizon then stays the single uniform colour this
            // shader has always drawn.
            float4 _SS_SkyRoseA;
            float4 _SS_SkyRoseB;
            float  _SS_RoseStrength;
            float4 _SS_HorizonClear;
            float4 _SS_HorizonStorm;
            // How bad it is HERE. The rose is a contrast cue, so it needs both
            // ends: "clear that way" only means something measured against
            // what you are sitting in.
            float  _SS_Storminess;
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

            // The weather along the bearing a pixel is looking down.
            //
            // Linear between the two nearest of the eight samples: eight is
            // enough because a storm edge is hundreds of metres of gradient,
            // not a line, and any sharper would read as facets on the skyline.
            float RoseAt(float3 dir)
            {
                // Bearing 0 = north (+Z), clockwise toward east (+X) — the
                // same convention SkyDirector fills the array in.
                float az = atan2(dir.x, dir.z);              // -PI..PI
                float f = az * (8.0 / 6.2831853) + 8.0;      // wrap negatives
                float i0 = floor(f);
                float frac0 = f - i0;

                // Gathered with dot products rather than by indexing a local
                // array: dynamic indexing is not dependable at target 3.0, and
                // this is eight multiply-adds on a shader that is already
                // running four octaves of fbm.
                float ia = fmod(i0, 8.0);
                float ib = fmod(i0 + 1.0, 8.0);

                float4 sel = float4(0, 1, 2, 3);
                float4 wa0 = step(abs(sel - ia), 0.5), wb0 = step(abs(sel + 4.0 - ia), 0.5);
                float4 wa1 = step(abs(sel - ib), 0.5), wb1 = step(abs(sel + 4.0 - ib), 0.5);

                float va = dot(_SS_SkyRoseA, wa0) + dot(_SS_SkyRoseB, wb0);
                float vb = dot(_SS_SkyRoseA, wa1) + dot(_SS_SkyRoseB, wb1);
                return lerp(va, vb, frac0);
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

                // A two-dimensional star chart avoids clipped specks where
                // a 3D noise cell intersects the celestial sphere.
                float2 chart = float2(atan2(sd.x,sd.z)/6.2831853+.5,
                    asin(clamp(sd.y,-1,1))/3.14159265+.5)*float2(180,90);
                float2 cell = floor(chart);
                float3 h = Hash33(float3(cell,17));
                float2 offset = frac(chart)-(.25+h.yz*.50);
                float d = length(offset);
                float radius = lerp(.045,.11,h.y*h.y);
                float pip = 1-smoothstep(radius,radius+clamp(fwidth(d),.018,.06),d);
                float twinkle = .88+.12*sin(_Time.y*.7+h.z*61);
                return step(_StarDensity,h.x)*pip*lerp(.5,1.3,h.y)*twinkle
                    * (1-smoothstep(.95,1,abs(sd.y)));
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

                // The skyline is coloured by what is out THAT way, not by what
                // is overhead here. This is the only cue that tells you which
                // direction leads out of a storm — without it the sky is one
                // colour all round and every heading looks identical.
                //
                // Only the band near the horizon takes the directional colour:
                // the zenith is the weather you are actually under, and it has
                // no bearing to belong to.
                float roseS = 0.0;      // storminess along this bearing
                float roseClear = 0.0;  // how much of a CLEARING this way, masked to the lower sky
                float3 horizonCol = _HorizonColor.rgb;
                if (_SS_RoseStrength > 0.001)
                {
                    roseS = saturate(RoseAt(dir));
                    float3 there = lerp(_SS_HorizonClear.rgb, _SS_HorizonStorm.rgb, roseS);
                    float band = 1.0 - smoothstep(0.0, 0.42, up);
                    horizonCol = lerp(horizonCol, there, band * _SS_RoseStrength);

                    // Weighted toward the lower sky but far wider than the
                    // gradient band: in a storm the cloud deck owns everything
                    // above about 8 degrees, so a cue confined to the skyline
                    // is painted over before it is ever seen. Measured on the
                    // first attempt: the clear bearing came back 6.7% brighter
                    // than the stormy one, and a fully stormy bearing was the
                    // brightest tile of the four.
                    // How much BETTER it is that way than here — not how
                    // clear it is in absolute terms. Phrased as a difference
                    // because the absolute form thinned the cloud deck over
                    // the whole sky in fair weather, where every bearing is
                    // clear and there is nothing to point at: a cue that fires
                    // everywhere points nowhere, and it would have quietly
                    // rewritten the fair-weather sky the feature was never
                    // meant to touch.
                    roseClear = saturate(_SS_Storminess - roseS) * _SS_RoseStrength
                              * (1.0 - smoothstep(0.0, 0.55, up));
                }

                float3 col = lerp(horizonCol, _ZenithColor.rgb,
                                  pow(up, 1.0 / max(0.2, _HorizonSharp)));
                col = lerp(_GroundColor.rgb, col, smoothstep(-0.05, 0.02, dir.y));

                // The painting supplies cloud colour and silhouette only. Its blue
                // background is replaced by the dynamic day/night gradient.
                float cloudTime = lerp(_Time.y, _CloudReviewTime, _CloudReviewOverride);
                float cloudAngle = cloudTime * _CloudDrift * 0.01745329252;
                float3 cloudDir = RotateAxis(dir,float3(0,1,0),cloudAngle);
                float3 paintedCloud = AdventureSky(cloudDir);
                float cloudShape = smoothstep(.005,.06, paintedCloud.r-paintedCloud.b*.25);
                cloudShape *= smoothstep(.045,.20,dir.y);
                float paintedCoverage = _AdventureStrength * (1-smoothstep(.25,.85,_Overcast));
                // Broad blue-violet night sky, without a baked sun or moon.
                float nightBand = exp(-pow((dir.y-.28)*3.1,2));
                col = lerp(col,col*float3(.72,.82,1.22),_SS_Night*(1-_Overcast)*smoothstep(.02,.20,dir.y));
                col += float3(.006,.010,.027)*nightBand*_SS_Night*(1-_Overcast);

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

                // Independent painted cloud layer occludes stars and moon.
                float pigment = saturate(dot(paintedCloud,float3(.25,.55,.20)));
                float3 nightCloud = lerp(float3(.012,.023,.062),float3(.065,.095,.19),smoothstep(.32,.72,pigment));
                float moonEdge = pow(saturate(dot(dir,_SS_MoonDir.xyz)),18)*moonUp;
                nightCloud += float3(.06,.08,.12)*moonEdge*pigment;
                float3 dayCloud = paintedCloud * lerp(float3(.77,.80,.92),float3(1,1,1),saturate(_SS_SunDir.y*4));
                col = lerp(col,lerp(dayCloud,nightCloud,_SS_Night),cloudShape*paintedCoverage);

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
                // A clearing on this bearing thins the lid and lights what is
                // left of it. This is the cue you actually steer by — the lid
                // breaking up over there — and it is the only part of the sky
                // with enough of the frame to be legible from the deck when
                // the sea is running 40 m.
                density *= lerp(1.0, 0.22, roseClear);
                density *= 1.0-paintedCoverage;

                float3 cloudCol = lerp(_CloudLit.rgb, _CloudDark.rgb,
                                       saturate(density * lerp(0.65, 1.35, _Overcast)));
                cloudCol += _SunColor.rgb * pow(sd, 24.0) * (1.0 - density) * 0.5 * clear;
                cloudCol = lerp(cloudCol, _SS_HorizonClear.rgb, roseClear * 0.75);
                cloudCol *= lerp(1.0,.13,_SS_Night);

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
                    // Scud tears out over the clearing too, or the ragged
                    // low cloud simply redraws the lid you just thinned.
                    col = lerp(col, _CloudDark.rgb * 0.82 * lerp(1.0,.13,_SS_Night),
                               saturate(sdens * _Scud * lift * 0.85) * lerp(1.0, 0.25, roseClear));
                }

                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
