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
        _PaintedSurface ("Painted study: off / ground / plants / stone", Float) = 0
        _PaintStudy ("Limit ground paint to review patch", Range(0,1)) = 0
        _CrispTerrain ("Crisp terrain regions", Range(0,1)) = 0
        _SandLine ("Grass boundary height", Float) = 4.3
        _Tint ("Tint", Color) = (1,1,1,1)
        _DetailScale ("Detail scale (m)", Float) = 3.5
        _DetailStrength ("Albedo break-up", Range(0,0.6)) = 0.22
        _NormalStrength ("Surface roughness", Range(0,1.5)) = 0.55
        _StriationStrength ("Rock striation", Range(0,3)) = 1.4
        _DetailFade ("Detail fade distance (m)", Float) = 260
        _GraphicLight ("Graphic — soft sculpted lighting", Range(0,1)) = 0
        _ShadowTint ("Graphic — shadow colour", Color) = (0.56,0.67,0.88,1)
        _AuthoredFormLighting ("Authored form lighting", Range(0,1)) = 0
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PaintedSurface, _PaintStudy;
                float4 _Tint;
                float _CrispTerrain, _SandLine;
                float _DetailScale;
                float _DetailStrength;
                float _NormalStrength;
                float _StriationStrength;
                float _DetailFade;
                float _GraphicLight;
                float4 _ShadowTint;
                float _AuthoredFormLighting;
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
                float3 positionOS : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
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
                if (_PaintedSurface > .5)
                {
                    // Broad painted colour shapes, with no grit or normal-map
                    // noise. Object coordinates keep marks attached to assets.
                    float3 p = i.positionOS;
                    float cameraDistance = distance(i.positionWS, GetCameraPositionWS());
                    float nearDetail = 1-smoothstep(110,320,cameraDistance);
                    float3 painted = albedo;
                    float plant = step(1.5,_PaintedSurface)*(1-step(2.5,_PaintedSurface));
                    float green = smoothstep(.018,.065,albedo.g-albedo.r);
                    if (plant > .5)
                    {
                        // Overlapping rounded leaf groups; quiet broad pigment
                        // patches persist at range, smaller scallops fade out.
                        float mass = vnoise(p*float3(.65,.80,.65));
                        float scallop = smoothstep(.48,.58,mass+.10*vnoise(p*1.9));
                        float leafTop = smoothstep(-.2,.75,n.y);
                        float3 leaf = albedo * lerp(float3(.84,.90,1.06),float3(1.20,1.13,.82),scallop*nearDetail);
                        leaf = lerp(leaf,leaf*float3(1.08,1.04,.90),leafTop*.35);
                        float strokes = smoothstep(.59,.70,vnoise(p*float3(4,.24,4)));
                        float3 bark = albedo*(1-.15*strokes*nearDetail);
                        painted = lerp(bark,leaf,green);
                    }
                    else
                    {
                        float rock = _PaintedSurface>2.5 ? 1 : saturate(i.color.a);
                        float meadow = green*(1-rock);
                        float large = vnoise(p*float3(.055,.035,.055));
                        float grassPatch = smoothstep(.30,.69,large);
                        float3 grass = albedo*lerp(float3(.79,.89,1.01),float3(1.08,1.04,.88),grassPatch);
                        // Anisotropic pigment follows a rock's broad face; no
                        // painted cracks across bevels or arbitrary black lines.
                        float mineral = vnoise(p*float3(.07,.04,.07));
                        float stonePatch = smoothstep(.22,.78,mineral);
                        float3 stone = albedo*lerp(float3(.965,.975,1.015),float3(1.025,1.012,.98),stonePatch);
                        float sandPatch = vnoise(p*float3(.12,.03,.12));
                        float3 sand = albedo*lerp(float3(.94,.965,1.01),float3(1.035,1.015,.97),sandPatch);
                        painted = lerp(lerp(sand,grass,meadow),stone,rock);
                    }
                    float study = lerp(1,1-smoothstep(24,36,distance(p.xz,float2(-55,32))),_PaintStudy);
                    albedo = lerp(albedo,painted,study);
                }
                if (_CrispTerrain > .5 && i.positionWS.y > 0)
                {
                    // Classify per fragment, never interpolate tan into green
                    // across several metres of a terrain triangle.
                    float border = i.positionWS.y - _SandLine;
                    float aa = max(fwidth(border), .008);
                    float grassMask = smoothstep(-aa, aa, border);
                    float stoneAA = max(fwidth(i.color.a), .01);
                    float rockMask = smoothstep(.48-stoneAA,.48+stoneAA,i.color.a);
                    float3 meadow = float3(.100,.243,.030);
                    float3 sand = float3(.674,.523,.224);
                    float3 rock = float3(.36,.35,.29);
                    albedo = lerp(lerp(sand,meadow,grassMask),rock,rockMask) * _Tint.rgb;
                }

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

                // The Blender plateau has deliberate broad face colours.  This
                // optional response keeps those facets legible: three wide,
                // softly joined bands give the toon shape, while the position
                // inside each band still changes continuously so nearby planes
                // do not collapse to one flat swatch.  Rock alpha strengthens
                // the cool shadow response, while the lower ground weight keeps
                // grass and sand readable when this is enabled on the plateau.
                float authoredSun = saturate(ndl * light.shadowAttenuation);
                float authoredLow = smoothstep(0.16, 0.32, authoredSun);
                float authoredMid = smoothstep(0.48, 0.66, authoredSun);
                float authoredHigh = smoothstep(0.58, 0.78, authoredSun);
                float3 authoredShadow = float3(0.30, 0.42, 0.85);
                float3 authoredMidTint = float3(0.66, 0.70, 0.82);
                float3 authoredSunTint = float3(1.08, 1.03, 0.90);
                float3 authoredTint = authoredShadow;
                authoredTint = lerp(authoredTint, authoredMidTint, authoredLow);
                authoredTint = lerp(authoredTint, authoredSunTint, authoredMid);
                authoredTint *= lerp(0.92, 1.04, authoredHigh);
                // Broad bands are the dominant form cue; this small continuous
                // term preserves plane-to-plane direction variation inside them.
                authoredTint *= lerp(0.94, 1.04, authoredSun);
                float authoredMask = _AuthoredFormLighting * lerp(0.62, 1.0, saturate(i.color.a));
                float3 authored = albedo * (max(ambient * 0.42, float3(0.10,0.10,0.10)) + light.color * authoredTint);
                col = lerp(col, authored, authoredMask);
                // Point lights: the campfire and the lamps. URP's own falloff
                // is inverse-square, which lights a fire's stone ring and
                // nothing past it; a camp has to read from the water, so
                // this is a stylised linear fade over ~22 m with wrapped
                // Lambert (the slope behind the fire still glows). Works in
                // both the Forward and the Forward+ (cluster) paths.
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
