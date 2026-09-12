// The real water surface. Vertex: clipmap displacement (cascades x per-ring
// weights x distance fade x regional envelope). Fragment: per-pixel normals
// from the derivative textures, scattering-driven colour (deep vs subsurface
// picked by view/sun/peak mask - the backlit jade glow on steep crests is the
// whole signature), sphere-softened sun specular that gets rougher with
// distance, fresnel sky reflection, and Jacobian whitecaps torn by value
// noise. Storm palette blends in via the SkyDirector's _SS_Storminess.
Shader "SeaSick/Ocean"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.028, 0.14, 0.21, 1)
        _ShallowColor ("Shallow", Color) = (0.09, 0.42, 0.45, 1)
        _SubsurfaceColor ("Subsurface", Color) = (0.10, 0.65, 0.45, 1)
        _StormDeep ("Storm Deep", Color) = (0.058, 0.086, 0.098, 1)
        _StormShallow ("Storm Shallow", Color) = (0.10, 0.14, 0.15, 1)
        _StormSubsurface ("Storm Subsurface", Color) = (0.16, 0.30, 0.24, 1)
        _FoamColor ("Foam", Color) = (0.92, 0.96, 0.97, 1)
        _SubsurfaceStrength ("Subsurface Strength", Range(0, 3)) = 1.4
        _PeakMaskScale ("Peak Mask Scale", Range(0, 2)) = 0.55
        _FoamJThreshold ("Foam Jacobian Threshold", Range(0, 1)) = 0.72
        _FoamNoiseScale ("Foam Noise Scale", Float) = 0.14
        // How the fold READS. `j` is the surface Jacobian: 1 is flat water and
        // below `_FoamJThreshold` the surface is folding over itself. Foam is
        // the visible evidence of that fold, so its onset should be as abrupt
        // as the fold is. `_FoamSnap` is the width of the onset in Jacobian
        // units -- the old hard-coded `* 4.0` was a fixed 0.25-wide ramp, which
        // spreads what ought to be an edge across a quarter of the whole range
        // and reads as haze lying on the water instead of a crest breaking.
        _FoamSnap ("Foam — snap (J width of the onset)", Range(0.02, 0.6)) = 0.12
        _FoamCrestGain ("Foam — fresh crest gain", Range(0, 3)) = 1.0
        _FoamRelief ("Foam — relief (bump on the whitecap)", Range(0, 3)) = 1.1
        _FoamSparkle ("Foam — wet sparkle", Range(0, 2)) = 0.7
        // WHAT THE FOAM IS ALLOWED TO SEE, as opposed to what the mesh can
        // DRAW. Measured (CrestProbe, 2026-09-09): the instant fold term
        // produced foam on 0.09 % of the open sea — i.e. none — while the
        // persistent buffer produced 100 % of everything on screen. The cause
        // is that the shader's Jacobian is weighted by the CLIPMAP weights, and
        // the folding lives in cascade 2, whose weight is near zero past a few
        // tens of metres. So the water folds and the shader is not looking.
        //
        // `FoamAccumulate.compute` has never had that problem: it sums all
        // three bands UNWEIGHTED, which is exactly why the buffer was carrying
        // the whole load. This lifts the short bands back into the fragment's
        // Jacobian the same way, and fades the lift out with distance because
        // a 32 m patch sampled at LOD 0 two kilometres away is sparkle noise,
        // not foam — past that the buffer carries it, which is its job.
        _FoamBandLift ("Foam — short-band lift", Range(0, 1)) = 0.7
        _FoamLiftFar ("Foam — lift fades out by (m)", Float) = 420
        // The buffer's own contrast. It is a blurred, decaying field, so left
        // linear it lays a uniform milk over the whole sea — measured at 0.35
        // mean coverage with 100 % of water pixels above 0.1, which is a sheet
        // of paint and the reason a big sea reads soft. The floor cuts the
        // base; the gain keeps the real trails.
        _FoamTrailFloor ("Foam — trail floor", Range(0, 0.5)) = 0.20
        _FoamTrailGain ("Foam — trail contrast", Range(0.2, 4)) = 1.8
        _SurfStrength ("Surf Strength", Range(0, 2)) = 0.95
        _SurfBreakFrac ("Surf Break Onset (x breakFraction)", Range(0.2, 1)) = 0.78
        _SurfSwashDepth ("Surf Swash Depth (m)", Range(0, 12)) = 3
        _SpecPowerNear ("Spec Power Near", Float) = 420
        _SpecPowerFar ("Spec Power Far", Float) = 48
        _SpecStrength ("Spec Strength", Range(0, 2)) = 0.75
        // Kevin's, driven by hand with `WaterClarityTuner` 2026-09-06 and
        // printed with its P key -- NOT picked from arithmetic. Where he
        // landed, against the defaults I had proposed: the water is about
        // four times CLEARER (green extinction 0.15 per metre against 0.60),
        // the shoal tint is half as strong because with water this clear the
        // real see-through does the work a painted tint was standing in for,
        // and the shoal fades out at 12.6 m, which is the seabed.
        //
        // _MurkDepth and _MurkExtinction are redundant with each other --
        // only 3 * extinction / depth reaches the shader -- and they are left
        // as he set them so the sliders come back up where he left them.
        _MurkDepth ("Murk — metres you can see down", Range(0.5, 40)) = 35.61
        _MurkExtinction ("Murk — per-channel absorption rate", Color) = (4.55, 1.81, 1.57, 1)
        _RefractStrength ("Refraction (m at the surface)", Range(0, 2)) = 0.57
        _ShoalColor ("Shoal — colour of water over a bottom", Color) = (0.19, 0.60, 0.58, 1)
        _ShoalDepth ("Shoal — depth it fades out by (m)", Range(1, 60)) = 12.6
        _ShoalStrength ("Shoal strength", Range(0, 1)) = 0.29
        // How far past the hull-clip ellipse the water stays OPAQUE, as a
        // multiple of the ellipse's own radius. See the shield in Frag.
        _HullShield ("Hull shield (x clip radius)", Range(1, 3)) = 1.6
    }
    SubShader
    {
        // Transparent queue, but NOT alpha blended: the water still writes
        // depth and still returns alpha 1. It has to render after the opaques
        // purely so `_CameraOpaqueTexture` exists to look through -- the
        // compositing is done by hand in the fragment, with an extinction
        // curve, which is both prettier than a blend mode and impossible to
        // sort wrong. Transparent-100 keeps it ahead of the spray, the
        // tracers and the impact decals, all of which sit at 3000.
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            // The VERTEX stage samples Texture2DArrays (SampleDisplacement,
            // three cascades) -- vertex texture fetch, which is Shader Model
            // 3.5 and up. Nothing declared a target before, so the compiler
            // assumed the 2.5 default and the whole displacement path was
            // legal only by accident of the platforms we happened to build.
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            // Seeing THROUGH the water costs two _CameraDepthTexture fetches,
            // one _CameraOpaqueTexture fetch and three exps per water pixel,
            // and it only works where URP actually binds those textures. The
            // mobile URP asset has m_RequireDepthTexture: 0 and
            // m_RequireOpaqueTexture: 0, so on the phone neither is bound: the
            // sea came out opaque on reverse-Z platforms and BLACK on GLES3 --
            // while still paying for every one of those fetches. Behind a
            // keyword the phone does not compile the block at all.
            //
            // multi_compile and NOT shader_feature, deliberately. There is one
            // shared OceanSurface.mat, handed to every ring as sharedMaterial
            // from a scene field. shader_feature strips variants against the
            // keyword state SAVED ON THE MATERIAL ASSET, so whichever of the
            // two the asset was not saved with would simply not exist in the
            // player, and the tier that wanted it would draw the error shader.
            // multi_compile keeps both and lets OceanClipmap choose at runtime
            // from OceanQuality.Active.refraction.
            #pragma multi_compile_local_fragment _ _REFRACTION
            // Everything the SHIPPED sea must not pay for.
            //
            // _SEASICK_DEBUG gates the six _SS_* dev uniforms and the foam
            // channel chain. They were in the shipped fragment: six global
            // loads and an eleven-way ternary on every water pixel, so that a
            // probe run for an hour in 2026 could read the foam field. Behind
            // the keyword the shipped variant does not declare them, does not
            // load them, and does not branch on them; with them at their
            // neutral values (all zero, which is the rule those uniforms were
            // written to) the two paths are the same picture.
            //
            // _HULL_CLIP gates the clip() for the hull cutout. A clip() in the
            // fragment makes the WHOLE shader late-Z on a tile GPU — the
            // hardware can no longer reject a pixel before shading it, because
            // the shader is allowed to change whether it writes depth — and it
            // does that whether or not _HullClipSize.w is set, since the cost
            // is a property of the compiled program and not of the branch. The
            // ocean is most of the screen, so that is the most expensive
            // instruction in the file. HullWaterClip owns the keyword: on when
            // it pushes a live volume, off when it clears it.
            //
            // GLOBAL multi_compile on both, deliberately — NOT _local. There
            // is one shared OceanSurface.mat and no per-material state to
            // drive this from; the switches are Shader.EnableKeyword /
            // DisableKeyword from script, and a _local keyword is invisible to
            // those. Same reasoning as the _REFRACTION note above for why
            // multi_compile and not shader_feature: nothing may be stripped
            // against whatever state the material asset happened to be saved
            // with, or the variant we want at runtime would not exist.
            #pragma multi_compile _ _SEASICK_DEBUG
            #pragma multi_compile _ _HULL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_REFRACTION)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #endif
            #include "RegionField.hlsl"

            TEXTURE2D_ARRAY(_Ocean_Displacement);
            SAMPLER(sampler_Ocean_Displacement);
            TEXTURE2D_ARRAY(_Ocean_Derivatives);
            SAMPLER(sampler_Ocean_Derivatives);
            TEXTURE2D_ARRAY(_Ocean_Turbulence);
            SAMPLER(sampler_Ocean_Turbulence);
            float4 _Ocean_PatchSizes;
            float4 _Ocean_FadeParams;
            // The cascade fade schedule, pushed once by OceanClipmap.Build.
            // xyz = the shortest wavelength each cascade holds, and the
            // longest; _Ocean_CascadeFade.x = resolvable metres of wavelength
            // per metre of distance, .y = the floor set by the innermost cells.
            // Unset these read as zero, which gives every cascade full weight
            // everywhere -- a boiling horizon, loud and obvious, rather than a
            // sea that silently flattens.
            float4 _Ocean_FadeLamMin;
            float4 _Ocean_FadeLamMax;
            float4 _Ocean_CascadeFade;
            TEXTURE2D(_Ocean_SimTex);       // ripple sim: R offset, G foam
            SAMPLER(sampler_Ocean_SimTex);
            float4 _Ocean_SimRect;          // anchor.xy, extent, texel
            float4 _SS_SkyHorizon;
            float _SS_Storminess;
            // 0 = broad daylight, 1 = full night. Pushed by SkyDirector.
            // Both night terms below are phrased so that an UNSET global (0)
            // reproduces exactly what this shader rendered before night
            // existed — the _SS_LayerOff rule.
            float _SS_Night;
            // How far the body colour is dimmed, and how far the sky mix
            // leans on the authored horizon, at full night. Globals rather
            // than material properties on purpose: night belongs to the world,
            // SkyDirector is its single owner, and a material would have
            // snapshotted these defaults the moment the properties were
            // created and never seen them again. They travel with _SS_Night
            // from the same writer, so either all three arrive or none do —
            // and none means _SS_Night is 0, which is broad daylight.
            float _SS_NightBodyDim;
            float _SS_NightSkyMix;
            // ---- dev uniforms, behind _SEASICK_DEBUG ------------------------
            // All six of these exist to make a probe able to read one of the
            // shader's own inputs off the framebuffer. They are worth having
            // and they are not worth SHIPPING: six constant-buffer loads and
            // an eleven-way ternary chain on every water pixel, permanently,
            // for a measurement taken on a Tuesday.
            //
            // The "unset reads as zero = shipped look" rule each one was
            // written to is what makes this safe, and it is now doubly
            // enforced: with the keyword off the uniforms are not declared at
            // all and every read below resolves to the literal neutral value
            // through the SS_* macros, so the compiler folds the branches out
            // entirely. The shipped variant is the exact picture you get today
            // with every _SS_* at zero.
            #if defined(_SEASICK_DEBUG)
            // Dev only: which shading layers to SUPPRESS (subsurface, sky
            // reflection, sun glitter, foam). Phrased as "off" and not "on"
            // deliberately -- an unset global reads as ZERO, so the shipped
            // look is the one you get when nothing binds this, and a probe
            // that forgets to reset it can only ever fail loudly rather than
            // silently ship a shader with its reflections switched off.
            float4 _SS_LayerOff;
            // Dev only: 1 outputs foamAmt itself as luminance instead of the
            // shaded water, so a probe can read the foam field directly off
            // the framebuffer rather than guessing at it from a photograph of
            // the sea. Same rule as _SS_LayerOff -- an unset global reads as
            // zero, which is the shipped look, so forgetting to reset it can
            // only fail loudly.
            float _SS_FoamOnly;
            // Dev only: 1 puts the foam back the way it was before the fold
            // pass -- Jacobian without its cross term, the fixed 0.25-wide
            // onset, no crest gain, no relief. It exists so the before and the
            // after come off the SAME FRAME of the same sea rather than from
            // two builds an hour apart, which is the only way a foam
            // comparison means anything: the sea moves. Unset reads as zero =
            // the shipped look.
            float _SS_FoamOldJ;
            // Dev only: writes ONE of the foam's inputs to the screen instead
            // of the water, so a term that measures as doing nothing can be
            // split into WHICH of its inputs is wrong. Same discipline that
            // found the DivergenceProbe fault: one number that could be any of
            // three explains nothing.
            //   1 j  2 breaking  3 env  4 wJ.z  5 turb  6 fresh  7 residual
            //   8 fade  9 wC.x  10 wC.z  11 dist/1000
            float _SS_FoamChannel;
            // Dev only: 1 suppresses the surf term below, so SurfProbe can
            // shoot the shore with and without it in ONE run at one wave
            // phase, instead of the before and the after being two builds an
            // hour apart with a different sea in each. Unset reads as zero,
            // which is the shipped look.
            float _SS_SurfOff;
            // Dev only: 1 makes the water opaque again, so a look sheet can
            // shoot the same shore with and without the bottom showing
            // through at one wave phase. Unset reads as zero = shipped look.
            float _SS_RefractOff;

            #define SS_LAYER_OFF     _SS_LayerOff
            #define SS_FOAM_ONLY     _SS_FoamOnly
            #define SS_FOAM_OLD_J    _SS_FoamOldJ
            #define SS_FOAM_CHANNEL  _SS_FoamChannel
            #define SS_SURF_OFF      _SS_SurfOff
            #define SS_REFRACT_OFF   _SS_RefractOff
            #else
            // The neutral values, as COMPILE-TIME CONSTANTS. Every use site
            // below reads a macro, so the non-debug variant folds
            // `1.0 - SS_LAYER_OFF.x` to 1.0, `SS_FOAM_ONLY > 0.5` to false,
            // and the channel chain out of existence, with no uniform
            // declared and nothing loaded.
            //
            // The float4 is a named `static const` rather than an inline
            // `float4(0,0,0,0)` literal for one dull reason: the use sites
            // swizzle it, and a swizzle applied to a constructor expression is
            // the sort of thing a cross-compiler somewhere down the chain gets
            // wrong. A named constant swizzles like any other variable, folds
            // just as hard, and cannot be got wrong.
            static const float4 _SS_LayerOffNeutral = float4(0.0, 0.0, 0.0, 0.0);
            #define SS_LAYER_OFF     _SS_LayerOffNeutral
            #define SS_FOAM_ONLY     0.0
            #define SS_FOAM_OLD_J    0.0
            #define SS_FOAM_CHANNEL  0.0
            #define SS_SURF_OFF      0.0
            #define SS_REFRACT_OFF   0.0
            #endif // _SEASICK_DEBUG

            // How much of each cascade the mesh under this vertex can carry.
            // KEEP IDENTICAL to OceanClipmap.WeightsAt -- see the CascadeFade
            // note there for why the schedule is what it is.
            float3 CascadeWeightsAt(float dist)
            {
                float lamRes = max(_Ocean_CascadeFade.y, dist * _Ocean_CascadeFade.x);
                float3 t = saturate((lamRes - _Ocean_FadeLamMin.xyz)
                    / max(_Ocean_FadeLamMax.xyz - _Ocean_FadeLamMin.xyz, 1e-4));
                return 1.0 - t * t * (3.0 - 2.0 * t);
            }

            float2 SampleSim(float2 xz)
            {
                if (_Ocean_SimRect.z <= 0.0) return float2(0, 0);
                float2 uv = (xz - _Ocean_SimRect.xy) / _Ocean_SimRect.z;
                if (any(uv < 0.0) || any(uv > 1.0)) return float2(0, 0);
                return SAMPLE_TEXTURE2D_LOD(_Ocean_SimTex, sampler_Ocean_SimTex, uv, 0).rg;
            }

            // ---- hull water clip -------------------------------------------
            // In mountainous seas the surface rises above the deck and renders
            // straight through it, so you see the sea inside the boat. Rather
            // than a stencil pass (which needs an extra draw and gets the
            // near-field wrong — ocean BETWEEN the camera and the boat lands in
            // the same screen pixels), the ocean simply refuses to exist inside
            // the hull's inboard volume. One matrix and two vectors, an ellipse
            // in plan because a hull is not a box, four instructions a pixel,
            // no draw call and no render-order rules to get wrong.
            float4x4 _HullClipWorldToLocal;
            float4 _HullClipCentre;    // xyz centre in ship space, w unused
            float4 _HullClipSize;      // x,z semi-axes, y half-height, w on/off

            CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor, _ShallowColor, _SubsurfaceColor;
            half4 _StormDeep, _StormShallow, _StormSubsurface;
            half4 _FoamColor;
            half _SubsurfaceStrength, _PeakMaskScale;
            half _FoamJThreshold, _FoamNoiseScale;
            half _FoamSnap, _FoamCrestGain, _FoamRelief, _FoamSparkle;
            half _FoamBandLift, _FoamLiftFar, _FoamTrailFloor, _FoamTrailGain;
            half _SurfStrength, _SurfBreakFrac, _SurfSwashDepth;
            half _SpecPowerNear, _SpecPowerFar, _SpecStrength;
            half _MurkDepth, _RefractStrength, _ShoalDepth, _ShoalStrength;
            half _HullShield;
            half4 _MurkExtinction, _ShoalColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 data : TEXCOORD1;   // x = env(cascade 0), y = fade, z = |dispXZ|, w = fog
                float heightY : TEXCOORD2;
                // Interpolated rather than recomputed in the fragment: the
                // envelope needs a shore-texture fetch, and paying that per
                // pixel to get a value that varies over hundreds of metres
                // would be silly.
                float3 envC : TEXCOORD3;   // per-cascade envelope
                // Interpolated rather than recomputed per pixel so the normals
                // and the foam ride exactly the weights the geometry was built
                // with. The curve is smooth in distance, so interpolating it
                // across a cell is exact to well under a percent.
                float3 wC : TEXCOORD4;     // per-cascade clipmap weight
            };

            // The envelope multiplies INSIDE the cascade sum now, because it is
            // per cascade: shallow water flattens the long swell while leaving
            // the short chop alone, which is what a real shoreline does.
            float3 SampleDisplacement(float2 worldXZ, float fade, float3 envC,
                float3 wC, out float dispLen)
            {
                float3 d = 0;
                dispLen = 0;
                [unroll]
                for (int c = 0; c < 3; c++)
                {
                    float w = wC[c] * fade * envC[c];
                    if (w <= 0.001) continue;
                    float2 uv = worldXZ / _Ocean_PatchSizes[c];
                    float4 s = SAMPLE_TEXTURE2D_ARRAY_LOD(_Ocean_Displacement,
                        sampler_Ocean_Displacement, uv, c, 0);
                    d += w * s.xyz;
                    dispLen += w * length(s.xz);
                }
                return d;
            }

            // ONE pass over the three cascades feeding TWO weighted sums of
            // the same _Ocean_Derivatives texels.
            //
            // These used to be two functions, `SampleDerivs` and `SampleFold`,
            // called a hundred lines apart in the fragment -- and they looped
            // over the same three cascades sampling _Ocean_Derivatives at the
            // same UVs at the same LOD. Only the weights differed: the shading
            // normals ride the clipmap weights (`wD = wC * fade * envC`), the
            // foam's Jacobian rides the lifted ones (`wF = wJ * envC`) because
            // the foam must be allowed to see bands the mesh cannot draw --
            // see `_FoamBandLift`. Same texel, fetched twice, per water pixel.
            //
            // Merged, each texel is fetched once and both sums are accumulated
            // from it. Each accumulator keeps its OWN `w <= 0.001` early-out
            // and its own per-cascade summation order, so both results are
            // bit-identical to the two loops this replaces -- this is a
            // refactor and not a retune, and nothing about the water changes.
            //
            // `fold` is the three terms of the displacement Jacobian: dxx and
            // dzz from the derivative texture, and the CROSS term Dxz from
            // Displacement.w. That second fetch stays exactly where it was --
            // only the fold wants it, so only the fold pays for it.
            void SampleDerivsAndFold(float2 worldXZ, float3 wD, float3 wF,
                out float4 dv, out float3 fold)
            {
                dv = 0;
                fold = 0;   // x = dxx, y = dzz, z = dxz
                [unroll]
                for (int c = 0; c < 3; c++)
                {
                    bool useD = wD[c] > 0.001;
                    bool useF = wF[c] > 0.001;
                    if (!useD && !useF) continue;
                    float2 uv = worldXZ / _Ocean_PatchSizes[c];
                    float4 d = SAMPLE_TEXTURE2D_ARRAY_LOD(_Ocean_Derivatives,
                        sampler_Ocean_Derivatives, uv, c, 0);
                    if (useD) dv += wD[c] * d;
                    if (useF)
                    {
                        float cross = SAMPLE_TEXTURE2D_ARRAY_LOD(_Ocean_Displacement,
                            sampler_Ocean_Displacement, uv, c, 0).w;
                        fold += wF[c] * float3(d.z, d.w, cross);
                    }
                }
            }

            // Two octaves of value noise, world-anchored: tears the raw
            // Jacobian foam so it reads as spume, not maths.
            //
            // The hash used to be `frac(sin(dot(p, k)) * 43758.5453)`, the
            // canonical shadertoy one-liner, and it is the single most
            // expensive thing this shader did. `sin` is not an ALU op on a
            // GPU: it goes to the transcendental unit, which on most parts
            // issues at a quarter rate. FoamNoise is four hashes, and FoamNoise
            // was called SIX times a water pixel (two for the tear, four for
            // the relief gradient) — twenty-four transcendentals per pixel of
            // most of the screen, to produce a number whose only job is to be
            // random.
            //
            // It is also a bad hash. `sin` at large arguments is where float
            // precision dies, and the lattice coordinates here are world
            // metres times a small scale: sail far enough from the origin and
            // the same texel starts hashing to neighbouring values, so the
            // noise smears into banding. Mobile compilers make it worse — many
            // lower `sin` to a fast approximation, so the phone and the PC do
            // not even agree on what the foam looks like.
            //
            // This is an integer bit-mix instead (xxhash/pcg-shaped: multiply
            // by a large odd constant, fold the high bits down with a shift-
            // xor, repeat). Same interface, same job, and the distribution is
            // better than the thing it replaces rather than merely as good:
            // every output bit depends on every input bit, so it is flat and
            // decorrelated across the whole lattice at any distance from the
            // origin. Cost is a handful of integer ops and no transcendentals.
            //
            // Detail that matters: the mantissa. `float(h) * (1/2^32)` would
            // round h upward at the top of the range and can return exactly
            // 1.0, which puts the value noise outside [0,1) and shows up as
            // the odd blown-out texel. Taking the top 24 bits (`h >> 8`) gives
            // an integer that a float holds EXACTLY, so the result is in
            // [0, 1) by construction — the same half-open range `frac` gave.
            //
            // Requires integer ops in the fragment shader, which is why the
            // `#pragma target 3.5` above is load-bearing and not decorative.
            float FoamHash(float2 p)
            {
                // The callers already hand this integral lattice coordinates;
                // the floor is here so the function is correct on its own
                // terms for anything else that ever calls it. Negative
                // coordinates wrap through two's complement, which is fine —
                // the hash only needs the bit pattern to be distinct.
                int2 ip = int2(floor(p));
                uint2 q = asuint(ip);
                // The 0x9E37... seed is not decoration: without it the lattice
                // point at the world origin mixes 0, every step of a shift-xor-
                // multiply leaves 0 alone, and that one texel would hash to a
                // hard 0.0 for ever. Every multiplier here is ODD, which is
                // what makes each step a bijection on 32 bits -- an even one
                // throws away a bit per multiply and the noise loses range.
                uint h = q.x * 0x27220A95u + q.y * 0x85EBCA6Bu + 0x9E3779B9u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 13;
                h *= 0x297A2D39u;
                h ^= h >> 16;
                return float(h >> 8) * (1.0 / 16777216.0);   // 2^-24, in [0,1)
            }
            float FoamNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = FoamHash(i);
                float b = FoamHash(i + float2(1, 0));
                float c = FoamHash(i + float2(0, 1));
                float d = FoamHash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                float dist = distance(ws.xz, GetCameraPositionWS().xz);
                float fade = 1.0 - smoothstep(_Ocean_FadeParams.x, _Ocean_FadeParams.y, dist);
                float3 envC = RegionEnvelopeCascades(ws.xz);
                float3 wC = CascadeWeightsAt(dist);
                float dispLen;
                float3 disp = SampleDisplacement(ws.xz, fade, envC, wC, dispLen);
                disp.y += SampleSim(ws.xz).r; // wakes & splash rings
                ws += disp;
                o.positionWS = ws;
                o.heightY = disp.y;
                o.positionHCS = TransformWorldToHClip(ws);
                o.envC = envC;
                o.wC = wC;
                // dispLen already carries the envelope, per cascade.
                o.data = float4(envC.x, fade, dispLen,
                    ComputeFogFactor(o.positionHCS.z));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // How much this pixel must NOT look through the water, 1 at
                // the hull and 0 past the shield margin.
                //
                // **The see-through water dissolves against the player's own
                // hull, and only against it.** The transmittance below is
                // exp(-column), where the column is the water between this
                // surface and whatever opaque thing is behind it. Behind open
                // ocean that thing is the sky, so the column is enormous, the
                // transmittance is zero, and the sea is opaque -- which is why
                // the clarity pass looked right everywhere except here. At the
                // waterline the hull is CENTIMETRES behind the surface, the
                // column goes to zero, exp(0) is 1, and the water turns fully
                // transparent exactly where it meets the boat: a dark hull
                // showing through a ring of dissolved sea, reading as a second
                // ocean laid over the first.
                //
                // It cannot be tuned out. No value of _MurkDepth or
                // _MurkExtinction changes exp(-0) = 1, so the fix has to be
                // geometric. HullWaterClip already hands us the hull's volume
                // in ship space for the clip below; the same `plan` term, read
                // a little wider, is exactly the "am I up against the boat"
                // test we need, and it costs one smoothstep.
                float hullShield = 0.0;
                if (_HullClipSize.w > 0.5)
                {
                    float3 hp = mul(_HullClipWorldToLocal,
                                    float4(input.positionWS, 1.0)).xyz - _HullClipCentre.xyz;
                    float plan = (hp.x * hp.x) / (_HullClipSize.x * _HullClipSize.x)
                               + (hp.z * hp.z) / (_HullClipSize.z * _HullClipSize.z);
                    // Inside the plan ellipse AND within the deck-to-rail band.
                    //
                    // Behind _HULL_CLIP because a clip() is not free when it
                    // does nothing. Its cost is not the instruction: it is that
                    // a fragment shader containing one can DISCARD, so the
                    // hardware may no longer decide a pixel's depth before
                    // shading it. On a tile GPU that turns off early-Z for the
                    // whole shader, and the ocean is most of the screen. That
                    // penalty is a property of the compiled program, so
                    // `_HullClipSize.w` being zero does not avoid a penny of
                    // it -- the branch is checked at runtime, early-Z is
                    // decided at compile time. HullWaterClip turns the keyword
                    // on when it pushes a live volume and off when it clears
                    // one, so the sea is only late-Z while there is actually a
                    // hull to cut out of it.
                    //
                    // `hullShield` below is deliberately OUTSIDE the keyword:
                    // it is a smoothstep, not a discard, it costs nothing in
                    // early-Z terms, and the refraction block needs it whenever
                    // there is a hull -- keyword or no keyword.
                    #if defined(_HULL_CLIP)
                    clip(max(plan - 1.0, abs(hp.y) - _HullClipSize.y));
                    #endif
                    // `plan` is a squared normalised radius -- 1 on the
                    // ellipse -- so the margin squares too. Deliberately NOT
                    // gated on hp.y the way the clip is: the clip band is
                    // deck-to-rail, and the surface that needs shielding is
                    // the waterline, which is below it.
                    float edge = _HullShield * _HullShield;
                    hullShield = 1.0 - smoothstep(1.0, edge, plan);
                }

                float env = input.data.x;
                float fade = input.data.y;
                float2 xz = input.positionWS.xz;
                float dist = distance(xz, GetCameraPositionWS().xz);

                // The foam's Jacobian weights, hoisted up from the foam block
                // below. They are needed HERE only because the fold and the
                // shading derivatives read the same three _Ocean_Derivatives
                // texels and are now fetched together -- see
                // SampleDerivsAndFold. Nothing in them depends on anything
                // computed between here and their old home; they are a
                // function of the interpolated weights and the distance.
                //
                // Lift the SHORT bands into the foam's Jacobian, fading the
                // lift out with distance. Cascade 0 is already at full weight
                // everywhere and needs none; cascade 2 needs all of it, and is
                // the band that actually folds.
                float old = saturate(SS_FOAM_OLD_J);
                float liftK = _FoamBandLift * (1.0 - smoothstep(_FoamLiftFar * 0.25,
                                                                _FoamLiftFar, dist));
                float3 wJ = old > 0.5 ? input.wC * fade
                          : saturate(input.wC + liftK * float3(0.0, 0.5, 1.0)) * fade;

                // Per-pixel normals from the derivative bands. The horizontal
                // squeeze term keeps crests sharp instead of shaded like domes.
                // `fold` comes out of the same fetches and is carried down to
                // the foam block unchanged.
                float4 dv;
                float3 fold;
                SampleDerivsAndFold(xz, input.wC * fade * input.envC,
                                    wJ * input.envC, dv, fold);
                float2 slope = dv.xy / max(float2(1.0, 1.0) + dv.zw, 0.15);
                // Ripple sim contributes slope by finite difference + foam.
                float2 sim = SampleSim(xz);
                float simTexel = max(_Ocean_SimRect.w, 0.01);
                float2 simSlope = float2(
                    SampleSim(xz + float2(simTexel, 0)).r - sim.r,
                    SampleSim(xz + float2(0, simTexel)).r - sim.r) / simTexel;
                slope += simSlope;
                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));

                Light sun = GetMainLight();
                float3 L = sun.direction;
                float3 V = normalize(GetCameraPositionWS() - input.positionWS);
                float3 Vf = V;
                if (dot(n, V) < 0) n = float3(-n.x, n.y, -n.z); // underside-ish guard

                float storm = saturate(_SS_Storminess);
                half3 deep = lerp(_DeepColor.rgb, _StormDeep.rgb, storm);
                half3 shallow = lerp(_ShallowColor.rgb, _StormShallow.rgb, storm);
                half3 subsurf = lerp(_SubsurfaceColor.rgb, _StormSubsurface.rgb, storm);

                // Body colour: deep in troughs, lifted on crests. The scale
                // is the SEA'S OWN, not an absolute count of metres. At a
                // fixed 0.18/m this pegged at the shallow colour 3.6 m above
                // mean water, which was reasonable on the 9 m sea it was
                // authored against and turns a 62 m storm into two flat bands
                // of colour: measured on the shipped storm, 45% of the surface
                // sat at the shallow colour and 36% at the deep one, leaving
                // 19% inside the gradient at all. Dividing by the LOCAL Hs
                // (the declared open-sea height, scaled by the same envelope
                // the displacement uses, so sheltered water keeps its own
                // range) puts about +-2 sigma across the ramp at every sea
                // state. Same rule the spindrift threshold had to learn: a
                // threshold into the sea is a fraction of the sea's own
                // spread, never a number of metres.
                // One shore fetch, used three times below: the water's own
                // colour over a bottom, how far you can see through it, and
                // the surf. It was already being paid for down in the surf
                // block; it is only hoisted.
                float3 swd = ShoreWetDepth(xz);

                float localHs = max(env * _Ocean_DepthLimit.y, 0.5);
                float heightLift = saturate(0.38 + input.heightY / localHs);
                half3 body = lerp(deep, shallow, heightLift);

                // The body colour carries no diffuse and no normal term — it
                // is authored, not lit — so unlike the specular and the
                // subsurface (which ride sun.color and dim by themselves once
                // the moon takes over the key light) nothing about it knows
                // the sun has set. Without this the sea keeps its daylight
                // turquoise under a black sky.
                body *= lerp(1.0, _SS_NightBodyDim, saturate(_SS_Night));

                // ---- shoal and murk ------------------------------------
                // Two different things, and they have to be separate because
                // they fail at opposite ends.
                //
                //   SHOAL is the colour water takes over a bottom: light that
                //   reached the sand and came back up. It is driven by the
                //   VERTICAL depth, so it reads the same from anywhere -- and
                //   that is the whole point, because it is what tells you
                //   where the shallow water is from the deck of a ship, at a
                //   grazing angle where you can see through nothing at all.
                //   Outside the shore grid `swd.z` is a 1e9 sentinel, so the
                //   open sea is untouched by construction and not by a
                //   threshold somebody has to keep right.
                //
                //   MURK is actually seeing the bottom, and it is driven by
                //   the length of the water column along the VIEW RAY, which
                //   is the physical thing: straight down through 2 m you see
                //   sand, along the same 2 m of water at a grazing angle you
                //   see none of it. Beer-Lambert per channel, so red dies
                //   first and the last thing visible is a blue-green ghost --
                //   which is why deep water hides its floor without a fade
                //   having to be authored.
                float shoal = 1.0 - saturate(swd.z / max(_ShoalDepth, 0.5));
                body = lerp(body, _ShoalColor.rgb,
                            shoal * shoal * _ShoalStrength * swd.y);

                // Everything from here to the `body = lerp(...)` below is the
                // see-through half of the water, and it is the only thing in
                // this shader that needs the scene depth and colour textures.
                // Without _REFRACTION the water keeps `body` exactly as it was
                // computed above -- deep/shallow ramp, night dim and shoal
                // tint -- and is simply opaque, which is what the sea looked
                // like before the clarity pass and is a correct sea, not a
                // degraded one. Nothing below this block reads `trans`,
                // `column`, `refr`, `sceneEye`, `surfEye` or `suv`, so there
                // is nothing to define in the other branch; `hullShield` above
                // is still computed (its `clip()` is the hull cutout and has
                // to happen either way) and is simply unused here.
                #if defined(_REFRACTION)
                float2 suv = GetNormalizedScreenSpaceUV(input.positionHCS);
                // The fragment's SV_POSITION carries the NDC depth in .z and
                // 1/w in .w -- not the eye depth. Take it through the same
                // LinearEyeDepth the scene sample goes through, so the two are
                // the same quantity and reverse-Z is handled in one place.
                float surfEye = LinearEyeDepth(input.positionHCS.z, _ZBufferParams);
                // Refraction falls off with distance for the reason the
                // glitter roughens with it: a fixed offset in metres is a
                // growing offset in PIXELS as the surface tilts away, and the
                // far field ends up swimming.
                float2 refr = n.xz * (_RefractStrength / max(surfEye, 1.0));
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv + refr), _ZBufferParams);
                // A bent ray must not reach something in FRONT of the water:
                // that is how a hull's bow ends up smeared into the sea
                // beside it. If the refracted sample is nearer than the
                // surface, take the straight one.
                if (sceneEye < surfEye)
                {
                    refr = 0;
                    sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                }
                float column = max(0.0, sceneEye - surfEye);
                // Nothing behind the water is the sky, which is infinitely
                // far, so the column is huge and the transmittance is zero --
                // the open sea needs no special case.
                float3 trans = exp(-column * (3.0 / max(_MurkDepth, 0.25))
                                   * _MurkExtinction.rgb);
                trans *= 1.0 - saturate(SS_REFRACT_OFF);
                // Meet the hull, do not dissolve into her.
                trans *= 1.0 - hullShield;
                body = lerp(body, SampleSceneColor(suv + refr), trans);
                #endif // _REFRACTION

                // The signature: sun behind a steep, choppy crest glows jade
                // through the water toward the camera.
                float peakMask = saturate(input.data.z * _PeakMaskScale);
                float steep = saturate(length(slope) * 1.4);
                float towardSun = pow(saturate(dot(Vf, -L) * 0.5 + 0.5), 3.0);
                float sss = towardSun * (0.35 + 0.65 * peakMask) * (0.4 + 0.6 * steep)
                            * _SubsurfaceStrength;
                body += subsurf * sss * sun.color * (1.0 - SS_LAYER_OFF.x);

                // Fresnel sky reflection: cheap probe + authored horizon mix.
                float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, Vf)), 5.0);
                float3 r = reflect(-Vf, n);
                half3 sky = GlossyEnvironmentReflection(r, input.positionWS,
                    0.15 + 0.35 * storm, 1.0);
                // The reflection probe is deliberately never refreshed —
                // SkyDirector skips DynamicGI.UpdateEnvironment because it
                // costs milliseconds on a phone — so at night the probe is
                // still a daylight sky and would keep the water lit from
                // above. _SS_SkyHorizon is pushed every frame and IS correct
                // after dark, so lean on it almost entirely once the sun is
                // down and let the stale probe fade out.
                sky = lerp(sky, _SS_SkyHorizon.rgb,
                           lerp(0.55, _SS_NightSkyMix, saturate(_SS_Night)));

                // Sun glitter, softened with distance so the far field never
                // sparkles (a variance -> roughness stand-in).
                float rough = saturate(dist / max(_Ocean_FadeParams.y, 1.0));
                float specPow = lerp(_SpecPowerNear, _SpecPowerFar, rough);
                float3 H = normalize(L + Vf);
                float spec = pow(saturate(dot(n, H)), specPow) * _SpecStrength
                             * (1.0 - 0.6 * storm) * (1.0 - SS_LAYER_OFF.z);

                // Foam: instant Jacobian whitecaps + the persistent buffer,
                // torn by two octaves of world noise.
                // J < threshold means folding. Flat water is J = 1 exactly and
                // must produce zero (cross term arrives with the M9 compute).
                // J = (1 + Dxx)(1 + Dzz) - Dxz^2. The cross term is not
                // optional and it was missing here: dropping Dxz^2 can only
                // make J LARGER, so every fold was under-reported and the
                // SHEARED ones were missed outright -- and the sheared folds
                // are the pyramidal peaks where the two swell trains cross,
                // which is precisely the water that should be breaking first.
                // FoamAccumulate.compute has carried the full form since it
                // was written; this is the instant layer catching up with the
                // persistent one, and the two now agree on what "folding"
                // means.
                // `old`, `liftK`, `wJ` and `fold` are computed up with the
                // shading derivatives -- same three texels, one fetch each.
                float j = (1.0 + fold.x) * (1.0 + fold.y) - fold.z * fold.z * (1.0 - old);
                float snap = lerp(_FoamSnap, 0.25, old);
                float breaking = saturate((_FoamJThreshold - j) / max(snap, 0.02));
                // Single combined-J foam layer, tiled with patch 1 -- the grid
                // FoamAccumulate.compute now accumulates on. The two MUST name
                // the same patch: the buffer is a plain N x N field with no
                // world anchor of its own, so this division is the only thing
                // that says what one of its texels means in metres. Get them
                // out of step and the foam is not merely the wrong size, it is
                // in the wrong PLACE -- a trail laid down where the water broke
                // would be drawn somewhere else entirely. See that kernel's
                // header for why patch 0 was the wrong grid to accumulate on.
                float turb = SAMPLE_TEXTURE2D_ARRAY(_Ocean_Turbulence,
                    sampler_Ocean_Turbulence, xz / _Ocean_PatchSizes[1], 0).r
                    * input.wC.x;
                // The buffer is a blurred, decaying field: left linear it lays
                // a uniform milk over the whole sea rather than marking where
                // the water broke. Floor it and lift its contrast so a trail
                // reads as a trail.
                float trail = old > 0.5 ? turb
                            : saturate((turb - _FoamTrailFloor) * _FoamTrailGain);

                // ---- surf ------------------------------------------------
                // Hoisted above the tearing noise, and only for that reason:
                // the surf is torn by the same `noise` as the rest of the foam,
                // so the test that decides whether the noise is worth
                // computing at all has to know whether there is any surf here.
                // Everything it reads -- swd, localHs, heightLift -- was
                // already computed further up; nothing has moved but the lines.
                //
                // The shoreline should be the foamiest water in the world and
                // was measurably the least: SurfProbe read foam falling 23x on
                // the way in, 0.106 in 32-64 m of water down to 0.005 in the
                // last metre. The cause is the `* env` on the fresh and
                // residual terms below.
                // Near a beach `env` IS the depth cap -- breakFraction * depth
                // / Hs, which in 8 m of water under a 62 m sea is 0.07 -- so
                // the term that correctly lies the sea DOWN also takes the
                // whitewater away with it. The cap is right; multiplying the
                // foam by it is the sign error.
                //
                // The fix is a term ADDED off the same depth lookup rather
                // than a suppression, and it is deliberately fragment-side:
                // NewtonIterations is 7 against a 0.4 ms budget with nothing
                // left, so anything touching wave SHAPE costs a step there is
                // no room for. Foam is free. This is one texture fetch and a
                // dozen ALU per water pixel, and the sampler never sees it.
                //
                // Two parts, both physical:
                //
                //   BREAKERS -- a wave breaks when its height approaches the
                //   water under it. `localHs` is already the local wave height
                //   and the depth is one fetch, so the ratio costs nothing --
                //   and it is SELF-CALIBRATING, which is the whole reason to
                //   drive it this way. Wherever the depth cap binds,
                //   env * Hs == breakFraction * depth exactly (cascade 0's
                //   BottomCoupling is 1), so the ratio pins to breakFraction
                //   and the term saturates. The break line therefore lands
                //   where the physics puts it at every sea state, and the surf
                //   zone widens by itself as the sea grows -- with nothing to
                //   re-tune when it does. Same rule as the colour ramp and the
                //   spindrift threshold: a threshold into the sea is a
                //   fraction of the sea's own scale, never a count of metres.
                //
                //   SWASH -- inshore of the breakers the water is white
                //   whatever the sea is doing: run-up, backwash, the last of
                //   the bore. This one IS a count of metres, and correctly so,
                //   because it is set by the beach and not by the sea.
                //
                // swd.y is the wet mask (0 over land), so no whitewater ever
                // appears on dry ground. Outside the shore grid the depth is a
                // 1e9 sentinel: relH goes to zero and swash goes to zero, so
                // the open sea is untouched by construction rather than by a
                // threshold that has to be got right.
                float bf = max(_Ocean_DepthLimit.x, 0.05);
                float relH = localHs / max(swd.z, 0.25);
                float breakers = smoothstep(bf * _SurfBreakFrac, bf, relH);
                float swash = 1.0 - smoothstep(_SurfSwashDepth * 0.2, _SurfSwashDepth, swd.z);
                // Whitewater rides the crest and the bore behind it, not the
                // trough. Leaning GENTLY on height (0.30 + 0.70 * heightLift)
                // was measurably right and visually wrong: it whitened the
                // whole surf zone evenly, so from above it read as a slab of
                // paint laid along the beach rather than as water breaking.
                // Surf is LINES -- one per crest -- so the breaker term wants
                // a hard threshold on the wave's own normalised height rather
                // than a gentle ramp. The SWASH does not: right at the beach
                // the water is white in the trough too, which is what run-up
                // and backwash are, so it is deliberately left out of this and
                // kept at full strength.
                //
                // Torn by the same noise as the rest of the foam, because an
                // untorn breaker line reads as a painted stripe.
                float crest = smoothstep(0.30, 0.72, heightLift);
                float surfMask = max(breakers * (0.20 + 0.80 * crest), swash);

                // ---- the tearing noise, and the one test that skips it -----
                // `noise` is two octaves of value noise, i.e. EIGHT hashes, and
                // it was computed unconditionally on every water pixel in the
                // world. It has exactly one job: to tear foam. On open water
                // with no whitecap, no trail, no wake and no surf under it,
                // every one of those hashes is multiplied by nothing.
                //
                // It cannot simply be moved inside a `foamAmt > 0.02` branch,
                // because `noise` is an INPUT to foamAmt -- it multiplies the
                // fresh term, the residual term, the wake and the surf. So the
                // test is on a CEILING instead: every place the noise appears
                // it appears as a positive factor bounded above (0.4 + 1.5n and
                // 0.5 + 0.8n and 0.45 + 0.9n, all maximised at n = 1), so
                // substituting those maxima gives a value that foamAmt provably
                // cannot exceed. If even that ceiling is below the 0.02 the
                // relief branch already treats as no foam, there is no foam
                // here for any value of the noise, and the hashes are skipped.
                //
                // The bound is one-sided on purpose: it can only ever say "no
                // foam" when there really is none. Where it says "maybe", the
                // noise is computed and every number below is exactly what it
                // was before -- same terms, same order, same rounding.
                float ceilFresh = saturate(breaking * (0.35 + 0.65 * storm) * 1.9)
                                * env * lerp(_FoamCrestGain, 1.0, old);
                float ceilResid = saturate(trail * 1.9) * env;
                float foamCeil = saturate(ceilFresh + ceilResid)
                               + max(sim.g, 0.0) * 1.3
                               + surfMask * 1.35 * swd.y * _SurfStrength;
                float noise = 0.0;
                if (foamCeil > 0.02)
                {
                    noise = FoamNoise(xz * _FoamNoiseScale)
                          * FoamNoise(xz * _FoamNoiseScale * 3.7 + 17.0);
                }

                // TWO foams, kept apart on purpose. FRESH is water that is
                // folding right now: bright, torn, and where the light catches.
                // RESIDUAL is the trail the turbulence buffer carries after it,
                // duller and older. Added into one number before they are used
                // -- which is what this did -- they average into flat paint at
                // one brightness; kept apart, a breaking crest reads white
                // against its own wake, which is the whole shape of the thing.
                float fresh = saturate(breaking * (0.35 + 0.65 * storm)
                                       * (0.4 + 1.5 * noise)) * env;
                float residual = saturate(trail * (0.4 + 1.5 * noise)) * env;
                float foamAmt = saturate(fresh * lerp(_FoamCrestGain, 1.0, old) + residual);
                foamAmt = saturate(foamAmt + sim.g * (0.5 + 0.8 * noise));
                float surf = surfMask
                           * (0.45 + 0.90 * noise) * swd.y * _SurfStrength
                           * (1.0 - saturate(SS_SURF_OFF));
                foamAmt = saturate(foamAmt + surf) * (1.0 - SS_LAYER_OFF.w);

                half3 col = lerp(body, sky, fresnel * (1.0 - foamAmt) * (1.0 - SS_LAYER_OFF.y));
                col += spec * sun.color;
                // The 0.45 term rides sun.color and so dims itself once the
                // moon takes over the key light, but the 0.55 is flat ambient
                // and knows nothing about the sun having set -- the same fault
                // the body colour had. Left alone it leaves every whitecap
                // burning at 55% white on a 4%-grey night sky, which reads as
                // snow rather than water.
                float3 foamCol = _FoamColor.rgb
                    * (0.55 * lerp(1.0, _SS_NightBodyDim, saturate(_SS_Night))
                       + 0.45 * sun.color);
                // Foam is not paint. A whitecap is a rough, structured surface,
                // and without a bump on it the brightest water in the frame is
                // also the flattest -- which is exactly what makes a big sea
                // read soft. Two extra noise taps at a fixed WORLD offset give
                // a stable gradient (never a screen-space derivative: that
                // scales with how much world a pixel covers and turns the far
                // field into sparkle noise), and the branch means clear water
                // pays nothing for it.
                float foamSpec = 0.0;
                if (foamAmt > 0.02 && old < 0.5)
                {
                    float fs = _FoamNoiseScale * 3.7;
                    float e = 0.5 / max(fs, 0.01);
                    float nx = FoamNoise((xz + float2(e, 0)) * fs + 17.0)
                             - FoamNoise((xz - float2(e, 0)) * fs + 17.0);
                    float nz = FoamNoise((xz + float2(0, e)) * fs + 17.0)
                             - FoamNoise((xz - float2(0, e)) * fs + 17.0);
                    float3 foamN = normalize(
                        n + float3(-nx, 0, -nz) * (_FoamRelief * foamAmt));
                    // Wet foam is rougher than open water, so it takes a much
                    // wider highlight than the sea's own glitter -- it should
                    // glow along a whole crest, not pick out one facet.
                    foamSpec = pow(saturate(dot(foamN, H)), 28.0)
                             * _FoamSparkle * foamAmt * (1.0 - SS_LAYER_OFF.z);
                }

                col = lerp(col, foamCol, foamAmt);
                col += foamSpec * sun.color;

                #if defined(_SEASICK_DEBUG)
                // Before the fog, unlike _SS_FoamOnly: these are inputs, not
                // the shaded result, and nothing about them should be dimmed
                // by the weather.
                //
                // The whole chain is compiled out of the shipped variant. It is
                // eleven comparisons and ten selects to answer a question only
                // a probe ever asks, and the answer is discarded on every pixel
                // of every frame the game actually renders.
                if (SS_FOAM_CHANNEL > 0.5)
                {
                    float ch = SS_FOAM_CHANNEL;
                    float v = ch < 1.5 ? j
                            : ch < 2.5 ? breaking
                            : ch < 3.5 ? input.envC.x
                            : ch < 4.5 ? wJ.z
                            : ch < 5.5 ? turb
                            : ch < 6.5 ? fresh
                            : ch < 7.5 ? residual
                            : ch < 8.5 ? fade
                            : ch < 9.5 ? input.wC.x
                            : ch < 10.5 ? input.wC.z : dist * 0.001;
                    return half4(v.xxx, 1);
                }
                #endif // _SEASICK_DEBUG

                col = MixFog(col, input.data.w);
                #if defined(_SEASICK_DEBUG)
                // After the fog, deliberately: the probe wants the foam the
                // shader computed, not the foam the weather let you see.
                if (SS_FOAM_ONLY > 0.5) col = foamAmt.xxx;
                #endif
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
