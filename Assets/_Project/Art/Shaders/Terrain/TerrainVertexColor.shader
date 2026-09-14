// URP lit terrain driven by vertex colour, with PROCEDURAL surface detail.
//
// The mesher bakes sand/grass/rock/snow per vertex from height and slope.
// That was the whole surface for a long time, and it is why the islands read
// as "weird low poly" even at 2 m vertex spacing: with no albedo, no normal
// map and no texture of any kind, the polygon shading IS the highest-frequency
// information in the image, so the eye has nothing to measure the land
// against and every slope becomes a smooth painted ramp.
//
// The detail here is generated, not sampled -- no texture assets, nothing to
// import, no UV seams on a streamed heightfield, and it costs a handful of
// hashes. Three things ride on it:
//   albedo break-up, so a hillside is not one flat green;
//   a perturbed normal, so light catches surface roughness the mesh does not
//     have and cannot afford to have at 2 m vertices;
//   vertical striation on steep faces, because rock erodes downhill and
//     stretching the noise along gravity is most of what reads as "cliff".
// All of it fades out with distance, or it aliases into shimmer on the
// horizon and undoes the silhouette work.
Shader "SeaSick/Terrain Vertex Color"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _DetailScale ("Detail scale (m)", Float) = 3.5
        _DetailStrength ("Albedo break-up", Range(0,0.6)) = 0.22
        _NormalStrength ("Surface roughness", Range(0,1.5)) = 0.55
        _StriationStrength ("Rock striation", Range(0,3)) = 1.4
        _DetailFade ("Detail fade distance (m)", Float) = 260
        _GraphicLight ("Graphic — soft sculpted lighting", Range(0,1)) = 0
        _ShadowTint ("Graphic — shadow colour", Color) = (0.56,0.67,0.88,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma multi_compile_instancing
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _DetailScale;
                float _DetailStrength;
                float _NormalStrength;
                float _StriationStrength;
                float _DetailFade;
                float _GraphicLight;
                float4 _ShadowTint;
            CBUFFER_END

            // --- procedural value noise -------------------------------------
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i + float3(0, 0, 0));
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            // Two octaves is enough: this is surface texture, not landform.
            float surfaceNoise(float3 p)
            {
                return vnoise(p) * 0.65 + vnoise(p * 2.7) * 0.35;
            }

            // Steep ground gets the noise STRETCHED along Y, which is what
            // makes a face read as bedded rock running downhill rather than
            // as gravel sprayed on a ramp.
            float3 detailCoords(float3 wp, float steep)
            {
                float3 q = wp / max(_DetailScale, 0.01);
                q.y *= lerp(1.0, 0.18, steep);
                return q;
            }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                // The heightfield's finite-difference normals bridge mesh LODs.
                // Derivative triangle normals exposed every narrow grid strip.
                float3 albedo = i.color.rgb * _Tint.rgb;

                // Fade the whole detail layer out with distance. Without this
                // it turns into per-pixel noise on the horizon -- shimmer that
                // reads as a rendering fault and eats the silhouette.
                float dist = distance(i.positionWS, GetCameraPositionWS());
                float fade = saturate(1.0 - dist / max(_DetailFade, 1.0));
                fade *= fade;

                if (fade > 0.001 && (_DetailStrength > 0.001 || _NormalStrength > 0.001))
                {
                    // How much of this surface is ROCK, from the vertex
                    // alpha the mesher writes (rock that broke out, or a
                    // sheer face) -- not from the normal, which striated and
                    // mottled every steep grass flank like a cliff.
                    float steep = i.color.a;
                    float3 q = detailCoords(i.positionWS, steep);

                    // Gradient by finite difference, in the same stretched
                    // space, so the perturbation agrees with what the albedo
                    // break-up is doing instead of fighting it.
                    float e = 0.35;
                    float c = surfaceNoise(q);
                    float dx = surfaceNoise(q + float3(e, 0, 0)) - c;
                    float dz = surfaceNoise(q + float3(0, 0, e)) - c;
                    float dy = surfaceNoise(q + float3(0, e, 0)) - c;

                    float3 g = float3(dx, dy, dz) / e;
                    // Only the part across the surface tilts it.
                    g -= n * dot(g, n);
                    n = normalize(n - g * _NormalStrength * fade
                                        * lerp(1.0, 1.0 + _StriationStrength, steep));

                    // Albedo break-up, stronger on rock than on sand: a beach
                    // is genuinely uniform and mottling it looks like dirt.
                    float rocky = lerp(0.45, 1.0, steep);
                    albedo *= 1.0 + (c - 0.5) * 2.0 * _DetailStrength * fade * rocky;
                }

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                float ndl = saturate(dot(n, light.direction));
                float3 diffuse = light.color * light.shadowAttenuation * ndl;
                float3 ambient = SampleSH(n);
                float3 col = albedo * (diffuse + ambient);
                // Vertex colours already carry canopy/rock form. Give their
                // darkest faces a coloured fill rather than crushing twice.
                // Two broad cel bands with a small antialiased transition.
                float sun = ndl * light.shadowAttenuation;
                float bandWidth = max(.025, fwidth(sun) * 1.5);
                float sculpted = smoothstep(.22 - bandWidth, .22 + bandWidth, sun);
                float3 fill = max(ambient * .6, 0.22 * _ShadowTint.rgb);
                float3 graphic = albedo * (fill + light.color
                    * lerp(_ShadowTint.rgb * 0.38, float3(1.02,0.97,0.85), sculpted));
                col = lerp(col, graphic, _GraphicLight);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma multi_compile_instancing
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vertShadow(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 fragShadow() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            float4 vertDepth(A v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
