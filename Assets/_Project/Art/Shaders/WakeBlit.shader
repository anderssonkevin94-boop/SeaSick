// Maintains the wake / interaction buffer.
//
// Pass 0 scrolls the previous frame's buffer to keep it anchored in world
// space as the ship moves, and fades it so old marks dissipate.
// Pass 1 stamps a new contribution (hull footprint, splash) additively.
//
// R = surface displacement, G = foam.
Shader "SeaSick/WakeBlit"
{
    Properties
    {
        _MainTex ("Previous", 2D) = "black" {}
        _Offset  ("UV Offset", Vector) = (0,0,0,0)
        _Decay   ("Decay", Float) = 0.995
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // --- Scroll + fade ---
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Offset;
            float _Decay;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv + _Offset.xy;
                // Anything scrolled in from outside the buffer is open water.
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 0;
                half4 prev = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                // Foam lingers a little longer than the displacement, the way
                // a wake's white water outlasts the disturbance itself.
                prev.r *= _Decay;
                prev.g *= lerp(_Decay, 1.0, 0.35);
                return prev;
            }
            ENDHLSL
        }

        // --- Additive stamp ---
        Pass
        {
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _StampColor;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }

            half4 frag(V i) : SV_Target
            {
                half4 brush = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return brush.a * _StampColor;
            }
            ENDHLSL
        }
    }
}
