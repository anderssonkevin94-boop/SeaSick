// Ocean surface displaced entirely on the GPU.
//
// The CPU used to sum every wave for every vertex, which cost ~8.5 ms a frame.
// The mesh is now a static flat grid that never changes; this shader displaces
// it in the vertex stage from the same wave constants the CPU uses for physics,
// so what you see and what the ship floats on stay in agreement.
//
// Wave packing (matches WaveField.PushToGpu):
//   _Waves[i].xy = direction * wavenumber   (k is its length)
//   _Waves[i].z  = amplitude
//   _Waves[i].w  = phase - omega * time
Shader "SeaSick/Ocean"
{
    Properties
    {
        _DeepColor      ("Deep Colour",    Color) = (0.04, 0.18, 0.32, 1)
        _ShallowColor   ("Shallow Colour", Color) = (0.10, 0.42, 0.58, 1)
        _CrestColor     ("Crest Colour",   Color) = (0.62, 0.83, 0.92, 1)
        _SpecColor      ("Specular",       Color) = (1, 1, 1, 1)
        _ShallowWater   ("Shallow Water",  Color) = (0.32, 0.68, 0.72, 1)
        _Smoothness     ("Smoothness",     Range(0, 1)) = 0.85
        _CrestStrength  ("Crest Strength", Range(0, 3)) = 1.1
        _NormalSampleDist ("Normal Sample Distance", Float) = 2.0
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.14
        _RippleScale    ("Ripple Scale",   Float) = 0.09
        _ShoreFoamBand  ("Shore Foam Band", Float) = 26
        _ShallowBand    ("Shallow Band",    Float) = 90
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define MAX_WAVES 16

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float4 _CrestColor;
                float4 _SpecColor;
                float4 _ShallowWater;
                float  _Smoothness;
                float  _CrestStrength;
                float  _NormalSampleDist;
                float  _RippleStrength;
                float  _RippleScale;
                float  _ShoreFoamBand;
                float  _ShallowBand;
            CBUFFER_END

            // Set globally each frame by WaveField / HullDisplacement.
            float4 _SS_Waves[MAX_WAVES];
            int    _SS_WaveCount;

            // Swell front: xy = direction * k, z = amplitude, w = phase offset.
            float4 _SS_Swell;
            // xy = front centre, zw = travel direction.
            float4 _SS_SwellFront;
            // x = half width, y = active flag.
            float4 _SS_SwellExtra;

            // Wake / interaction buffer: R = displacement, G = foam.
            // xy = world centre, z = world size, w = 1/size.
            TEXTURE2D(_SS_WakeTex); SAMPLER(sampler_SS_WakeTex);
            float4 _SS_WakeRect;

            float2 WakeUV(float2 world)
            {
                return (world - _SS_WakeRect.xy) * _SS_WakeRect.w + 0.5;
            }

            float2 SampleWake(float2 world)
            {
                if (_SS_WakeRect.z <= 0.0) return 0;
                float2 uv = WakeUV(world);
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 0;
                return SAMPLE_TEXTURE2D_LOD(_SS_WakeTex, sampler_SS_WakeTex, uv, 0).rg;
            }

            // Gusts: xy = centre, z = radius. Drawn by the water itself rather
            // than as quads sitting on top of it.
            #define MAX_GUSTS 8
            float4 _SS_Gusts[MAX_GUSTS];
            int    _SS_GustCount;

            float GustFactorAt(float2 p)
            {
                float best = 0.0;
                [loop]
                for (int i = 0; i < _SS_GustCount; i++)
                {
                    float4 g = _SS_Gusts[i];
                    if (g.z <= 0.0) continue;
                    float t = saturate(1.0 - distance(p, g.xy) / g.z);
                    best = max(best, smoothstep(0.0, 1.0, t));
                }
                return best;
            }

            // Shore falloff: xy = island centre, z = inner radius, w = outer.
            #define MAX_ISLANDS 24
            float4 _SS_Islands[MAX_ISLANDS];
            int    _SS_IslandCount;

            // Ship: xy = position, zw = forward (normalised).
            float4 _SS_ShipPos;
            // x = speed01, y = active flag, z = influence radius.
            float4 _SS_ShipParams;
            // Hull shape: x = halfLength, y = halfWidth, z = troughDepth, w = falloff.
            float4 _SS_HullShape;
            // Wake: x = bowOffset, y = bowHeight, z = tan(halfAngle), w = wakeLength.
            float4 _SS_WakeShape;

            // Fine ripples the vertex grid can never resolve. Analytic gradient
            // of a few directional wavelets, so we get a real normal for a
            // handful of instructions and no texture fetch.
            float3 RippleNormal(float2 p, float t)
            {
                float2 grad = 0;
                float2 dirs[4] = {
                    float2( 0.94,  0.34), float2(-0.51,  0.86),
                    float2( 0.28, -0.96), float2(-0.87, -0.49)
                };
                float freqs[4] = { 1.00, 1.63, 2.71, 4.10 };
                float amps[4]  = { 1.00, 0.62, 0.35, 0.18 };
                float speeds[4] = { 1.10, 1.45, 1.90, 2.40 };

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float k = freqs[i] * _RippleScale;
                    float ph = dot(dirs[i], p) * k + t * speeds[i];
                    grad += dirs[i] * (k * amps[i] * cos(ph));
                }
                return normalize(float3(-grad.x * _RippleStrength * 12.0, 1.0,
                                        -grad.y * _RippleStrength * 12.0));
            }

            /// 0 out at sea, 1 right at a shoreline. Reuses the island data
            /// already uploaded for wave attenuation — no extra plumbing.
            float ShoreProximity(float2 p, out float shallow)
            {
                float foam = 0.0;
                shallow = 0.0;
                [loop]
                for (int i = 0; i < _SS_IslandCount; i++)
                {
                    float4 isle = _SS_Islands[i];
                    if (isle.w <= 0.0) continue;
                    // isle.z is the shoreline radius.
                    float d = distance(p, isle.xy) - isle.z;
                    foam = max(foam, saturate(1.0 - abs(d) / _ShoreFoamBand));
                    shallow = max(shallow, saturate(1.0 - max(d, 0.0) / _ShallowBand));
                }
                return foam;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  crest      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };

            // Waves shoal and die at the shoreline. Must match
            // WaveField.ShoreAttenuation exactly or the water the ship floats
            // on and the water you see would disagree near land.
            float ShoreAttenuation(float2 p)
            {
                float atten = 1.0;
                [loop]
                for (int i = 0; i < _SS_IslandCount; i++)
                {
                    float4 isle = _SS_Islands[i];
                    if (isle.w <= 0.0) continue;
                    float dist = distance(p, isle.xy);
                    if (dist >= isle.w) continue;
                    atten = min(atten, smoothstep(0.0, 1.0, (dist - isle.z) / (isle.w - isle.z)));
                }
                return atten;
            }

            // Gerstner sum at a world XZ position. Returns (dx, height, dz).
            float3 WaveDisplacement(float2 p)
            {
                float3 d = 0;
                float shore = ShoreAttenuation(p);
                if (shore <= 0.001) return d;

                [loop]
                for (int i = 0; i < _SS_WaveCount; i++)
                {
                    float4 w = _SS_Waves[i];
                    float k = length(w.xy);
                    if (k < 1e-6) continue;
                    float2 dir = w.xy / k;
                    float ph = dot(w.xy, p) + w.w;
                    float s, c;
                    sincos(ph, s, c);
                    d.xz += dir * (w.z * c);
                    d.y  += w.z * s;
                }

                // Swell front: a moving band of heavy water.
                if (_SS_SwellExtra.y > 0.5)
                {
                    float along = dot(p - _SS_SwellFront.xy, _SS_SwellFront.zw);
                    float t = saturate(1.0 - abs(along) / _SS_SwellExtra.x);
                    float env = smoothstep(0.0, 1.0, t);
                    if (env > 0.001)
                    {
                        float k = length(_SS_Swell.xy);
                        if (k > 1e-6)
                        {
                            float2 dir = _SS_Swell.xy / k;
                            float ph = dot(_SS_Swell.xy, p) + _SS_Swell.w;
                            float s, c;
                            sincos(ph, s, c);
                            float amp = _SS_Swell.z * env;
                            d.xz += dir * (amp * c);
                            d.y  += amp * s;
                        }
                    }
                }
                return d * shore;
            }

            // The ship's own effect on the surface: trough, bow wave and a
            // Kelvin wake at the real half-angle. Visual only — the ship
            // floats on the wave field, not on its own wake.
            float HullHeight(float2 world)
            {
                if (_SS_ShipParams.y < 0.5) return 0.0;

                float2 rel = world - _SS_ShipPos.xy;
                float radius = _SS_ShipParams.z;
                if (dot(rel, rel) > radius * radius) return 0.0;

                float2 fwd = _SS_ShipPos.zw;
                float2 rgt = float2(fwd.y, -fwd.x);
                float along = dot(rel, fwd);
                float across = dot(rel, rgt);
                float absAcross = abs(across);
                float speed01 = _SS_ShipParams.x;

                float halfLen = _SS_HullShape.x;
                float halfWid = _SS_HullShape.y;
                float depth   = _SS_HullShape.z;
                float falloff = _SS_HullShape.w;

                float h = 0.0;

                // Trough the hull sits in.
                float lenT = saturate(1.0 - abs(along) / (halfLen + falloff));
                float widT = saturate(1.0 - absAcross / (halfWid + falloff));
                h -= depth * smoothstep(0, 1, lenT) * smoothstep(0, 1, widT);

                // Bow wave, growing with speed.
                float bowAlong = along - _SS_WakeShape.x;
                float bowT = saturate(1.0 - abs(bowAlong) / 9.0) * saturate(1.0 - absAcross / 7.0);
                h += _SS_WakeShape.y * speed01 * smoothstep(0, 1, bowT);

                // Kelvin wake arms astern at a fixed angle.
                if (along < 0.0)
                {
                    float behind = -along;
                    float wakeLen = _SS_WakeShape.w;
                    if (behind < wakeLen)
                    {
                        float armOffset = behind * _SS_WakeShape.z;
                        float armT = saturate(1.0 - abs(absAcross - armOffset) / 5.5);
                        float fade = saturate(1.0 - behind / wakeLen);
                        h += 0.55 * speed01 * smoothstep(0, 1, armT) * fade * fade;

                        float sternT = saturate(1.0 - behind / 18.0) * saturate(1.0 - absAcross / 6.0);
                        h -= 0.45 * speed01 * smoothstep(0, 1, sternT);
                    }
                }
                return h;
            }

            float3 SurfacePoint(float2 restXZ)
            {
                float3 d = WaveDisplacement(restXZ);
                float2 moved = restXZ + d.xz;
                // The wake buffer dents the surface where things have passed.
                float wakeDisp = SampleWake(moved).r;
                return float3(moved.x, d.y + HullHeight(moved) - wakeDisp, moved.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;

                // The grid is flat and static; everything below is the surface.
                float3 restWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 rest = restWS.xz;

                float3 p = SurfacePoint(rest);

                // Normals by central difference — three wave sums a vertex is
                // nothing on a GPU and avoids any analytic-derivative drift.
                float e = _NormalSampleDist;
                float3 px = SurfacePoint(rest + float2(e, 0));
                float3 pz = SurfacePoint(rest + float2(0, e));
                float3 n = normalize(cross(pz - p, px - p));
                if (n.y < 0) n = -n;

                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = n;
                // Only genuinely steep, lifted water counts as a breaking crest.
                // The waves got steeper, so a loose threshold here foamed the
                // entire sea rather than picking out the tops.
                float lift = saturate((p.y - restWS.y) * 0.22);
                float steep = saturate((1.0 - n.y) * 1.7);
                o.crest = saturate(steep * steep + lift * 0.35);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);

                // Blend the fine ripple normal into the wave normal. Without
                // this the surface is flat-shaded colour and reads as plastic.
                // Gusts ruffle the surface: more ripple detail and a darker,
                // wind-scuffed patch — the classic cat's paw on the water.
                float gust = GustFactorAt(i.positionWS.xz);
                float3 ripple = RippleNormal(i.positionWS.xz, _Time.y);
                float rippleAmount = 1.0 + gust * 2.2;
                n = normalize(float3(n.x + ripple.x * rippleAmount, n.y,
                                     n.z + ripple.z * rippleAmount));

                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));

                Light main = GetMainLight();
                float ndotl = saturate(dot(n, main.direction));

                // Fresnel: grazing angles reflect the sky and read bright,
                // looking straight down you see into the water and it darkens.
                float facing = saturate(dot(n, viewDir));
                float fresnel = pow(1.0 - facing, 3.0);
                float3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, fresnel);

                // Shallows lighten toward the beach, and a foam band breaks
                // along every shoreline.
                float shallow;
                float shoreFoam = ShoreProximity(i.positionWS.xz, shallow);
                baseCol = lerp(baseCol, _ShallowWater.rgb, shallow * shallow * 0.55);

                // Foam picked out on the crests, plus the surf line, plus
                // whatever has churned the water here recently.
                float foam = smoothstep(0.40, 0.95, i.crest * _CrestStrength);
                foam = max(foam, smoothstep(0.55, 1.0, shoreFoam));
                foam = max(foam, smoothstep(0.05, 0.55, SampleWake(i.positionWS.xz).g));
                baseCol = lerp(baseCol, _CrestColor.rgb, saturate(foam));
                baseCol *= 1.0 - gust * 0.28;   // the darker patch of a gust

                float3 h = normalize(main.direction + viewDir);
                float spec = pow(saturate(dot(n, h)), lerp(8.0, 256.0, _Smoothness));

                float3 ambient = SampleSH(n) * 0.55 + 0.35;
                float3 col = baseCol * (ambient + main.color * (0.45 + 0.55 * ndotl))
                           + _SpecColor.rgb * spec * _Smoothness * main.color;

                col = MixFog(col, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
