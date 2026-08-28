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
        _SpecPowerNear ("Spec Power Near", Float) = 420
        _SpecPowerFar ("Spec Power Far", Float) = 48
        _SpecStrength ("Spec Strength", Range(0, 2)) = 0.75
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RegionField.hlsl"

            TEXTURE2D_ARRAY(_Ocean_Displacement);
            SAMPLER(sampler_Ocean_Displacement);
            TEXTURE2D_ARRAY(_Ocean_Derivatives);
            SAMPLER(sampler_Ocean_Derivatives);
            TEXTURE2D_ARRAY(_Ocean_Turbulence);
            SAMPLER(sampler_Ocean_Turbulence);
            float4 _Ocean_PatchSizes;
            float4 _Ocean_FadeParams;
            float4 _Ocean_CascadeWeights;   // per-ring MaterialPropertyBlock
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
            // Dev only: which shading layers to SUPPRESS (subsurface, sky
            // reflection, sun glitter, foam). Phrased as "off" and not "on"
            // deliberately -- an unset global reads as ZERO, so the shipped
            // look is the one you get when nothing binds this, and a probe
            // that forgets to reset it can only ever fail loudly rather than
            // silently ship a shader with its reflections switched off.
            float4 _SS_LayerOff;

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
            half _SpecPowerNear, _SpecPowerFar, _SpecStrength;
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
            };

            // The envelope multiplies INSIDE the cascade sum now, because it is
            // per cascade: shallow water flattens the long swell while leaving
            // the short chop alone, which is what a real shoreline does.
            float3 SampleDisplacement(float2 worldXZ, float fade, float3 envC, out float dispLen)
            {
                float3 d = 0;
                dispLen = 0;
                [unroll]
                for (int c = 0; c < 3; c++)
                {
                    float w = _Ocean_CascadeWeights[c] * fade * envC[c];
                    if (w <= 0.001) continue;
                    float2 uv = worldXZ / _Ocean_PatchSizes[c];
                    float4 s = SAMPLE_TEXTURE2D_ARRAY_LOD(_Ocean_Displacement,
                        sampler_Ocean_Displacement, uv, c, 0);
                    d += w * s.xyz;
                    dispLen += w * length(s.xz);
                }
                return d;
            }

            float4 SampleDerivs(float2 worldXZ, float fade, float3 envC)
            {
                float4 dv = 0;
                [unroll]
                for (int c = 0; c < 3; c++)
                {
                    float w = _Ocean_CascadeWeights[c] * fade * envC[c];
                    if (w <= 0.001) continue;
                    float2 uv = worldXZ / _Ocean_PatchSizes[c];
                    dv += w * SAMPLE_TEXTURE2D_ARRAY_LOD(_Ocean_Derivatives,
                        sampler_Ocean_Derivatives, uv, c, 0);
                }
                return dv;
            }

            // Two octaves of value noise, world-anchored: tears the raw
            // Jacobian foam so it reads as spume, not maths.
            float FoamHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
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
                float dispLen;
                float3 disp = SampleDisplacement(ws.xz, fade, envC, dispLen);
                disp.y += SampleSim(ws.xz).r; // wakes & splash rings
                ws += disp;
                o.positionWS = ws;
                o.heightY = disp.y;
                o.positionHCS = TransformWorldToHClip(ws);
                o.envC = envC;
                // dispLen already carries the envelope, per cascade.
                o.data = float4(envC.x, fade, dispLen,
                    ComputeFogFactor(o.positionHCS.z));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_HullClipSize.w > 0.5)
                {
                    float3 hp = mul(_HullClipWorldToLocal,
                                    float4(input.positionWS, 1.0)).xyz - _HullClipCentre.xyz;
                    float plan = (hp.x * hp.x) / (_HullClipSize.x * _HullClipSize.x)
                               + (hp.z * hp.z) / (_HullClipSize.z * _HullClipSize.z);
                    // Inside the plan ellipse AND within the deck-to-rail band.
                    clip(max(plan - 1.0, abs(hp.y) - _HullClipSize.y));
                }

                float env = input.data.x;
                float fade = input.data.y;
                float2 xz = input.positionWS.xz;

                // Per-pixel normals from the derivative bands. The horizontal
                // squeeze term keeps crests sharp instead of shaded like domes.
                float4 dv = SampleDerivs(xz, fade, input.envC);
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

                // The signature: sun behind a steep, choppy crest glows jade
                // through the water toward the camera.
                float peakMask = saturate(input.data.z * _PeakMaskScale);
                float steep = saturate(length(slope) * 1.4);
                float towardSun = pow(saturate(dot(Vf, -L) * 0.5 + 0.5), 3.0);
                float sss = towardSun * (0.35 + 0.65 * peakMask) * (0.4 + 0.6 * steep)
                            * _SubsurfaceStrength;
                body += subsurf * sss * sun.color * (1.0 - _SS_LayerOff.x);

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
                float dist = distance(xz, GetCameraPositionWS().xz);
                float rough = saturate(dist / max(_Ocean_FadeParams.y, 1.0));
                float specPow = lerp(_SpecPowerNear, _SpecPowerFar, rough);
                float3 H = normalize(L + Vf);
                float spec = pow(saturate(dot(n, H)), specPow) * _SpecStrength
                             * (1.0 - 0.6 * storm) * (1.0 - _SS_LayerOff.z);

                // Foam: instant Jacobian whitecaps + the persistent buffer,
                // torn by two octaves of world noise.
                // J < threshold means folding. Flat water is J = 1 exactly and
                // must produce zero (cross term arrives with the M9 compute).
                float j = (1.0 + dv.z) * (1.0 + dv.w);
                float breaking = saturate((_FoamJThreshold - j) * 4.0);
                // Single combined-J foam layer, tiled with patch 0.
                float turb = SAMPLE_TEXTURE2D_ARRAY(_Ocean_Turbulence,
                    sampler_Ocean_Turbulence, xz / _Ocean_PatchSizes[0], 0).r
                    * _Ocean_CascadeWeights[0];
                float noise = FoamNoise(xz * _FoamNoiseScale)
                            * FoamNoise(xz * _FoamNoiseScale * 3.7 + 17.0);
                float foamAmt = saturate((breaking * (0.35 + 0.65 * storm) + turb)
                                * (0.4 + 1.5 * noise)) * env;
                foamAmt = saturate(foamAmt + sim.g * (0.5 + 0.8 * noise))
                        * (1.0 - _SS_LayerOff.w);

                half3 col = lerp(body, sky, fresnel * (1.0 - foamAmt) * (1.0 - _SS_LayerOff.y));
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
                col = lerp(col, foamCol, foamAmt);

                col = MixFog(col, input.data.w);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
