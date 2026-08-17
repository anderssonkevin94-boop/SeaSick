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
        _Smoothness     ("Smoothness",     Range(0, 1)) = 0.85
        _CrestStrength  ("Crest Strength", Range(0, 3)) = 1.1
        _NormalSampleDist ("Normal Sample Distance", Float) = 2.0
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
                float  _Smoothness;
                float  _CrestStrength;
                float  _NormalSampleDist;
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
                return float3(moved.x, d.y + HullHeight(moved), moved.y);
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
                // Steep, fast-rising water reads as a crest to be foamed.
                o.crest = saturate((p.y - restWS.y) * 0.35 + (1.0 - n.y) * 3.0);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));

                Light main = GetMainLight();
                float ndotl = saturate(dot(n, main.direction));

                // Fresnel: grazing angles reflect the sky and read bright,
                // looking straight down you see into the water and it darkens.
                float facing = saturate(dot(n, viewDir));
                float fresnel = pow(1.0 - facing, 3.0);
                float3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, fresnel);

                // Foam picked out on the crests.
                baseCol = lerp(baseCol, _CrestColor.rgb, saturate(i.crest * _CrestStrength));

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
