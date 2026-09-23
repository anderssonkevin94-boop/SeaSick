// 2026-09-23: the siting ghost (BuildingFactory.GhostMat) was drawn in
// "Universal Render Pipeline/Lit" with _ZWrite 0 and whatever ZTest that
// shader defaults to. Neither URP Lit nor URP Unlit in this project's
// package (com.unity.render-pipelines.universal 17.4.0) expose ZTest as a
// settable material property -- their ForwardLit/Unlit passes hard-code
// `ZTest LEqual` (or omit the token, which means the same default) with no
// `[_ZTest]` token to override from C#, so `mat.SetInt("_ZTest", ...)` is a
// silent no-op on them. Checked directly in Library/PackageCache's Lit.shader
// and Unlit.shader.
//
// A blueprint standing behind a tree or in the grass needs to draw ON TOP,
// full stop -- that is the whole point of a ghost the player is aiming. This
// is the smallest shader that can say so: flat-tinted, unlit (a ghost has no
// business being lit anyway; it is chalk, not a real object), one colour
// property so `BuildingFactory.Tint` keeps writing `_BaseColor` exactly as
// it did against Lit. `ZTest Always` is the entire point of the file; `ZWrite
// Off` keeps it from fouling the real depth buffer for anything drawn after
// it, same as before.
Shader "SeaSick/BlueprintGhost"
{
    Properties
    {
        _BaseColor("Colour", Color) = (0.62, 0.78, 0.92, 0.5)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" }

        Pass
        {
            Name "GhostOverlay"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            // The one line this whole file exists for: draw over anything
            // already in the depth buffer, trees and grass included.
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
