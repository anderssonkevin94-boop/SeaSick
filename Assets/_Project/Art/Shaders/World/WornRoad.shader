// Worn dirt roads (2026-09-26): the one draw per camp that `CampRoads`
// builds from where the villagers actually walk.
//
// The mesh is ONE sheet draped on the height field -- a lattice at 0.5 m,
// every vertex written once -- so no two triangles of it ever lie on top of
// each other and nothing is blended twice, at a junction or anywhere else.
// uv0.x is the vertex's distance (m) to the nearest SMOOTHED road
// centre-line (negative offsets widen the fire yard, positive ones taper
// dead ends), uv0.y the wear strength. Coverage is worked out PER PIXEL from
// the interpolated distance plus a little world-space edge noise, so an
// edge is a smooth curve at any zoom -- the 0.5 m lattice never shows.
// Palette and cross-section are Astra's roads-astra-lvl1-v1 (compacted light
// crown -> darker shoulder, soft feathered edge, 2.16 m across).
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
        _CentreColour ("Crown colour", Color) = (0.52,0.40,0.25,1)
        _EdgeColour ("Shoulder colour", Color) = (0.40,0.30,0.17,1)
        _HalfWidth ("Half width incl. feather (m)", Float) = 1.05
        _Feather ("Edge feather (m)", Float) = 0.32
        _Wobble ("Edge noise (m)", Float) = 0.34
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _CentreColour;
                float4 _EdgeColour;
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
                // Coverage from the distance to the smoothed centre-line,
                // with irregular shoulders (two octaves of edge noise).
                float2 xz = i.positionWS.xz;
                float wob = (VNoise(xz * 0.55) - 0.5) + (VNoise(xz * 1.6 + 31.7) - 0.5) * 0.45;
                float dn = i.road.x + wob * _Wobble;
                float cover = 1.0 - smoothstep(_HalfWidth - _Feather, _HalfWidth, dn);
                float alpha = cover * saturate(i.road.y);
                clip(alpha - 0.004);

                float3 n = normalize(i.normalWS);
                if (n.y < 0) n = -n;
                float shoulder = smoothstep(0.0, 1.0, saturate((dn / max(_HalfWidth, 0.01) - 0.25) / 0.65));
                float mottle = 0.92 + 0.16 * VNoise(xz * 0.23 + float2(17.1, -3.7));
                float3 albedo = lerp(_CentreColour.rgb, _EdgeColour.rgb, shoulder) * mottle * _Tint.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                float ndl = saturate(dot(n, light.direction));
                float3 ambient = SampleSH(n);
                float3 col = albedo * (light.color * light.shadowAttenuation * ndl + ambient);

                // The terrain's "graphic" two-band response, same numbers.
                float sun = ndl * light.shadowAttenuation;
                float bandWidth = max(.012, fwidth(sun) * 1.2);
                float sculpted = smoothstep(.34 - bandWidth, .34 + bandWidth, sun);
                float3 fill = max(0, SampleSH(float3(0,1,0))) * .58 * _ShadowTint.rgb;
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
                float3 authored = albedo * (max(0,SampleSH(float3(0,1,0))) * float3(.36,.42,.54) + light.color * aTint);
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
