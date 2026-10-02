// Worn dirt roads (2026-09-26): the one draw per camp that `CampRoads`
// builds from where the villagers actually walk.
//
// The mesh is ONE sheet draped on the height field -- a lattice at 0.5 m,
// every vertex written once -- so no two triangles of it ever lie on top of
// each other and nothing is blended twice, at a junction or anywhere else.
// uv0.x is the vertex's distance (m) to the nearest SMOOTHED road
// centre-line (negative offsets widen the fire yard, positive ones taper
// dead ends), uv0.y the wear strength. v3 (2026-09-27): the distance comes
// pre-shaped (a slow width wander + a per-vertex facet, CampRoads.Shaped),
// so the linear interpolation over the 0.5 m lattice draws Astra's faceted,
// polygonal shoulders; per-pixel edge noise is off (`_Wobble` 0).
// Palette and cross-section are Astra's roads-astra-lvl1-v1 exactly: worn
// crown -> earth -> shoulder -> 0.2 m feather, 2.16 m across.
//
// **Never z-fights, never floats.** Each vertex is pulled TOWARD THE CAMERA
// along its own view ray. That moves its depth and nothing else: the road
// lands on exactly the same pixels, so from the phone camera 150 m out it
// still beats the terrain's depth (a 1 cm lift would shimmer at that range)
// and it cannot be seen hovering, because it is not higher, only nearer.
//
// Lighting is the terrain's own (main light + shadows + SH, the "graphic"
// band blend, the stylised camp-fire falloff, fog), with `_GraphicLight`,
// `_ShadowTint` and `_AuthoredFormLighting` copied off the island's terrain
// material at build time, so the dirt sits IN the ground rather than on it.
Shader "SeaSick/Worn Road"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _CentreColour ("Worn centre colour", Color) = (0.69,0.60,0.46,1)
        _EarthColour ("Earth colour", Color) = (0.64,0.55,0.40,1)
        _EdgeColour ("Shoulder colour", Color) = (0.59,0.53,0.39,1)
        _HalfWidth ("Half width incl. feather (m)", Float) = 1.08
        _Feather ("Edge feather (m)", Float) = 0.2
        _Wobble ("Per-pixel edge noise (m)", Float) = 0
        _Patch ("Compacted patch tint (+-)", Float) = 0.09
        _GraphicLight ("Graphic light (copied from terrain)", Range(0,1)) = 0.35
        _ShadowTint ("Graphic shadow colour (copied from terrain)", Color) = (0.56,0.67,0.88,1)
        _AuthoredFormLighting ("Authored form lighting (copied from terrain)", Range(0,1)) = 0
        _DepthPull ("Depth pull toward camera (m)", Float) = 0.12
        _DepthPullPerMetre ("Extra pull per metre of distance", Float) = 0.002
    }
    SubShader
    {
        // After every opaque body in the 2000 queue (so terrain depth is
        // down), before the sky and the water. Transparent-shaped: blends,
        // writes no depth, casts nothing.
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RichLight.hlsl" // _SS_Night, _SS_Storminess, _SS_RichLight

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _CentreColour;
                float4 _EarthColour;
                float4 _EdgeColour;
                float _Patch;
                float _HalfWidth;
                float _Feather;
                float _Wobble;
                float _GraphicLight;
                float4 _ShadowTint;
                float _AuthoredFormLighting;
                float _DepthPull;
                float _DepthPullPerMetre;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 road : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 road : TEXCOORD3;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = wp;                      // lighting at the true ground point
                float3 toCam = GetCameraPositionWS() - wp;
                float dist = length(toCam);
                float pull = min(_DepthPull + dist * _DepthPullPerMetre, dist * 0.5);
                float3 drawn = wp + toCam / max(dist, 1e-4) * pull;
                o.positionCS = TransformWorldToHClip(drawn);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.road = v.road;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Hash without sine (Hoskins): stable in float32 on a phone GPU.
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * .1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1,0));
                float c = Hash21(i + float2(0,1)), d = Hash21(i + float2(1,1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // v3 (2026-09-27, Kevin: "they don't look like the asset
                // Astra made"): her cross-section, not a dark stripe. The
                // distance is already faceted -- the C# side bakes a slow
                // width wander and a per-vertex shoulder offset into it, and
                // a 0.5 m lattice interpolates it linearly, so the iso-line
                // IS a polygon like her shoulders. No per-pixel noise by
                // default (`_Wobble` 0): that is what made v2 read smooth.
                float2 xz = i.positionWS.xz;
                float dn = i.road.x;
                if (_Wobble > 0) dn += ((VNoise(xz * 0.55) - 0.5) + (VNoise(xz * 1.6 + 31.7) - 0.5) * 0.45) * _Wobble;
                float core = max(_HalfWidth - _Feather, 0.05);
                float cover = 1.0 - smoothstep(core, _HalfWidth, dn);
                float alpha = cover * saturate(i.road.y);
                clip(alpha - 0.004);

                float3 n = normalize(i.normalWS);
                if (n.y < 0) n = -n;
                // Her seven-vertex profile: worn crown at 0, earth at 0.55,
                // shoulder at 0.88 (of a 0.88 core), feather to 1.08 --
                // linear between, as her vertex colours interpolate.
                float crown = core * 0.625;
                float3 albedo = dn < crown
                    ? lerp(_CentreColour.rgb, _EarthColour.rgb, saturate(dn / crown))
                    : lerp(_EarthColour.rgb, _EdgeColour.rgb, saturate((dn - crown) / max(core - crown, 0.01)));
                // Broad compacted patches (her +-9 % tint, row by row).
                float patch = (VNoise(xz * 0.42 + float2(17.1, -3.7)) - 0.5) * 2.0;
                albedo *= (1.0 + _Patch * patch) * _Tint.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                float ndl = saturate(dot(n, light.direction));
                // Rich light: the terrain's swap, same numbers (see
                // Terrain/TerrainVertexColor.shader), so a road keeps the
                // ground's brightness. rich = 0 is the old look exactly.
                float rich = SS_Rich();
                float3 shipAmbient = SS_ShipAmbient(n);
                float3 ambient = lerp(SampleSH(n), shipAmbient, rich);
                float3 col = albedo * (light.color * lerp(light.shadowAttenuation, lerp(.22,1,light.shadowAttenuation), rich) * ndl + ambient);

                // The terrain's "graphic" two-band response, same numbers.
                float sun = ndl * light.shadowAttenuation;
                float bandWidth = max(.012, fwidth(sun) * 1.2);
                float sculpted = smoothstep(.34 - bandWidth, .34 + bandWidth, sun);
                float3 fill = lerp(max(0, SampleSH(float3(0,1,0))) * .58 * _ShadowTint.rgb, shipAmbient, rich);
                float3 graphic = albedo * (fill + light.color
                    * lerp(_ShadowTint.rgb * 0.38, float3(1.02,1.01,1.0), sculpted));
                col = lerp(col, graphic, _GraphicLight);

                // Authored-form bands, ground weight (no rock on a road).
                float edge = max(.012, fwidth(sun) * 1.2);
                float aLow = smoothstep(.27-edge, .27+edge, sun);
                float aMid = smoothstep(.60-edge, .60+edge, sun);
                float aHigh = smoothstep(.82-edge, .82+edge, sun);
                float3 aTint = lerp(float3(0.30,0.42,0.85), float3(0.66,0.70,0.82), aLow);
                aTint = lerp(aTint, float3(1.04,1.03,1.0), aMid);
                aTint *= lerp(0.92, 1.04, aHigh);
                float3 authored = albedo * (lerp(max(0,SampleSH(float3(0,1,0))) * float3(.36,.42,.54), shipAmbient, rich) + light.color * aTint);
                col = lerp(col, authored, _AuthoredFormLighting * 0.62);

                // Camp-fire and lamps: the terrain's stylised linear falloff.
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
                        float rangeFade = saturate(pl.distanceAttenuation * distanceSqr);
                        float fall = saturate(1.0 - sqrt(distanceSqr) * 0.045);
                        fall *= fall;
                        float wrap = saturate(dot(n, pl.direction) * 0.6 + 0.4);
                        col += albedo * pl.color * fall * rangeFade * wrap;
                    LIGHT_LOOP_END
                }
                #endif
                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
