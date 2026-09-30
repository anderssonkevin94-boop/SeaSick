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
// Cost: one draw per fogged island in view, no depth write, seven texture
// taps and eight hashes a pixel. No keywords beyond URP's fog.
//
// 2026-09-30, near only: the mesh's vertex colour alpha is a coast fall-off
// (1 on and beside land, 0 ~20 m out to sea), multiplied in last, so the
// cloud's outer edge follows the coastline softly and no straight mesh edge
// shows over the water. `IslandFogView` also fades a whole cover out by
// distance (`_Fade`) and skips its draw beyond ~450 m.
//
// 2026-09-30 polish pass:
// * No white tree silhouettes. The grid is read at the ground behind each
//   pixel, and a TREE behind a pixel is not ground: its cell was fogged
//   while the sea or the clearing beside it was not, so the far coast's
//   palms and every tree at a clearing's edge drew as a white cut-out. The
//   texture's G channel now carries the ground height (0 = sea); a depth
//   hit well above it is a prop, and the grid is read where the ray meets
//   the ground past it -- the same place its neighbours read, so a prop has
//   no outline of its own. Where that lands in the sea, the ray is also
//   read at canopy height (`_Canopy`), so the far coast's trees stay inside
//   the cloud instead of poking out of it.
// * No muddy dusk. The main light's hue is taken at a fifth and its
//   brightness clamped, so a pink-orange sun and a brown ambient give a
//   soft grey cloud with a little warmth, never a pink-brown one.
Shader "SeaSick/Island Fog"
{
    Properties
    {
        _FogTex ("Fog grid (R fog, G ground height / 127.5 m)", 2D) = "black" {}
        _FogRect ("Grid origin xz, 1/size xz", Vector) = (0,0,0.01,0.01)
        _FogTexel ("Texel size", Vector) = (0.01,0.01,0,0)
        _CloudColor ("Cloud", Color) = (0.97,0.975,0.98,1)
        _ShadeColor ("Cloud shade", Color) = (0.74,0.78,0.85,1)
        _Density ("Density (min, max)", Vector) = (0.74,0.95,0,0)
        _Fade ("Whole-island fade", Range(0,1)) = 1
        _NoiseScale ("Billow scale (1/m)", Float) = 0.045
        _Drift ("Drift (m/s)", Float) = 0.6
        _Canopy ("Canopy top, world y (m)", Float) = 20
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
                float _Canopy;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fog : TEXCOORD1;
                float edge : TEXCOORD2;   // coast alpha from the mesh (vertex colour a)
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
                o.edge = v.color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Read the grid where the eye actually lands -- the ground
                // behind this pixel -- not where the cloud sheet is. The sheet
                // floats a canopy up, so at the island camera's 28 deg its
                // own xz is ~30 m nearer the camera than the ground it hides:
                // the landing strip's hole then showed half the island clear
                // (2026-09-30 screenshot pass). Sky behind (the bank's top
                // edge seen from the sea) keeps the sheet's own xz.
                float2 xz = i.positionWS.xz;
                float3 eye = GetCameraPositionWS();
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float raw = SampleSceneDepth(suv);
                #if UNITY_REVERSED_Z
                bool sky = raw <= 0.00001;
                #else
                bool sky = raw >= 0.99999;
                #endif
                float canopyFog = 0;
                float propFog = 1;
                // Sky behind (the bank's top edge, or far sea the depth
                // texture misses): the sheet's own xz, land cells only, so
                // a distant bank has the island's outline and not the
                // mesh's straight quad edges.
                float landOnly = 1;
                if (sky)
                {
                    float gS = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, (xz - _FogRect.xy) * _FogRect.zw).g;
                    landOnly = saturate(gS * 255.0);
                }
                if (!sky)
                {
                    float3 behind = ComputeWorldSpacePosition(suv, raw, UNITY_MATRIX_I_VP);
                    float3 ray = behind - eye;
                    // G = the ground height of the cell behind (0 = sea).
                    float2 gB = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, (behind.xz - _FogRect.xy) * _FogRect.zw).rg;
                    float groundB = gB.g * 127.5;
                    // A prop standing out of the ground (tree, palm, rock,
                    // beast): read past it, where the ray meets its ground,
                    // so it gets its surroundings' cloud and no silhouette.
                    // Never more cloud than its own cell has, though: a
                    // palm on the opened beach stays in front of the bank.
                    if (gB.g > 0.004 && behind.y > groundB + 2.0 && ray.y < -0.01)
                    {
                        propFog = gB.r;
                        float t = (groundB - eye.y) / ray.y;
                        behind = eye + ray * min(t, 4.0);
                    }
                    xz = behind.xz;
                    // Landed in the sea (the far coast, seen past its
                    // trees): the cloud stands canopy-high over that coast,
                    // so read the ray there too. Never nearer than the sheet
                    // itself -- in front of the island that is open water.
                    // Only from above the canopy (the island camera); from
                    // the deck the near cloud already hides the far coast.
                    float2 gE = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, (xz - _FogRect.xy) * _FogRect.zw).rg;
                    if (gE.g <= 0.004 && ray.y < -0.01 && eye.y > _Canopy)
                    {
                        float3 toSheet = i.positionWS - eye;
                        float tc = (min(_Canopy, i.positionWS.y) - eye.y) / ray.y;
                        float ts = length(toSheet) / max(length(ray), 0.001);
                        float3 c = eye + ray * clamp(tc, ts, 1.0);
                        // Land's cloud only: the skirt over open water
                        // (the cover's two-cell margin) stays thin.
                        float2 gc = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, (c.xz - _FogRect.xy) * _FogRect.zw).rg;
                        canopyFog = gc.r * saturate(gc.g * 255.0);
                    }
                }
                float2 uv = (xz - _FogRect.xy) * _FogRect.zw;
                float2 t = _FogTexel.xy * 0.9;
                // Five bilinear taps: the 5 m cells become soft round patches.
                float fog = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv).r * 0.36
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2( t.x, 0)).r * 0.16
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2(-t.x, 0)).r * 0.16
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2(0,  t.y)).r * 0.16
                          + SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, uv + float2(0, -t.y)).r * 0.16;
                fog = max(min(fog, propFog), canopyFog) * landOnly;

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
                // The coast fall-off: the mesh carries 1 on land and near it,
                // 0 a dozen metres out to sea, so the cloud has no plate edge.
                a *= i.edge;

                // Lit like weather: sun-side tops bright, the rest cool grey,
                // night and dusk through the same light the land gets.
                Light light = GetMainLight();
                float3 up = float3(0, 1, 0);
                float sun = saturate(dot(up, light.direction)) * 0.5 + 0.5;
                float3 base = lerp(_ShadeColor.rgb, _CloudColor.rgb, saturate(n * 1.2 - 0.1));
                float3 lit = light.color * (0.35 + 0.35 * sun) + max(0, SampleSH(up)) * 0.8;
                // Never muddy (2026-09-30, the dusk cloud read pink-brown):
                // a fifth of the light's hue, its brightness kept between a
                // soft night grey and full white.
                float lum = dot(lit, float3(0.2126, 0.7152, 0.0722));
                float3 hue = lit / max(lum, 0.0001);
                lit = lerp(float3(1, 1, 1), hue, 0.2) * clamp(lum, 0.42, 1.08);
                float3 col = base * lit;
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
