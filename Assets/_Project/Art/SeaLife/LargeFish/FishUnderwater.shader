// The large fish SEEN THROUGH THE WATER when the sea cannot be seen through.
//
// On the phone tier (`OceanQuality_Mobile.refraction = 0`) the ocean is an
// opaque surface that writes depth at Transparent-100, so anything below it --
// the fish -- is simply hidden. Floating the fish above the water to "fix"
// that is explicitly ruled out (ASTRAS_READY_ASSETS "Large swimming fish").
//
// Instead, this is a second material on the fish's one skinned mesh, drawn
// AFTER the ocean (Transparent-99):
//
//   ZTest Greater   only where the fish is BEHIND the depth already in the
//                   buffer, i.e. behind the water surface (or behind whatever
//                   else is in front of it -- see the stencil);
//   Stencil Equal 4 only where the ocean pass itself wrote the pixel (the
//                   ocean sets stencil bit 4 when it wins the depth test), so
//                   a hull, pier or island in front of the water still hides
//                   the fish -- they are drawn before the ocean, the ocean
//                   fails its depth test there and leaves the bit clear;
//   ZWrite Off      nothing after us cares about the fish's depth.
//
// Colour: the fish lit like `SeaSick/Crew Vertex Color`, tinted toward the
// water by metres below the local surface (`_FishSurfaceY`, fed per frame by
// `LargeFish` through a MaterialPropertyBlock), and faded out with the same
// depth: ~1 m down reads as a clear dark-teal silhouette, ~4 m is almost gone.
//
// Where refraction is ON (the PC tier) the ocean already composites the
// opaque fish through `_CameraOpaqueTexture`, so `LargeFish` does not attach
// this material at all -- the fish is never drawn twice.
//
// Kept in player builds because the fish prefab under Resources references a
// material using it (no Shader.Find anywhere).
Shader "SeaSick/Fish Underwater"
{
    Properties
    {
        _WaterTint ("Water tint (deep)", Color) = (0.015, 0.085, 0.11, 1)
        _TintPerMetre ("Tint per metre below surface", Range(0, 2)) = 0.7
        _ClearDepth ("Depth with no fade yet (m)", Range(0, 2)) = 0.5
        _Clarity ("Fade per metre below that", Range(0.05, 3)) = 0.5
        _MaxAlpha ("Strongest opacity", Range(0, 1)) = 0.95
        _FishSurfaceY ("Local surface height (set per frame)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-99" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SeenThroughWater"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Greater
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 4
                ReadMask 4
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterTint;
                float _TintPerMetre;
                float _ClearDepth;
                float _Clarity;
                float _MaxAlpha;
                float _FishSurfaceY;
            CBUFFER_END

            float _SS_Night;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float  fog        : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.color      = v.color;
                o.fog        = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight();
                float ndl = saturate(dot(n, light.direction));
                float3 ambient = SampleSH(n) + lerp(float3(.07,.09,.12), float3(.025,.045,.075), saturate(_SS_Night));
                float3 lit = i.color.rgb * (light.color * ndl + ambient);

                // The water in front of the fish is lit from above, not by the
                // fish's own facing: the tint takes the light the surface gets.
                float3 waterLight = light.color * saturate(light.direction.y) * 0.6 + SampleSH(float3(0, 1, 0));
                float3 water = _WaterTint.rgb * waterLight;

                float depth = max(0.0, _FishSurfaceY - i.positionWS.y);
                float tint = 1.0 - exp(-depth * _TintPerMetre);
                float3 col = lerp(lit, water, tint);
                float alpha = _MaxAlpha * saturate(exp(-max(0.0, depth - _ClearDepth) * _Clarity));

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
