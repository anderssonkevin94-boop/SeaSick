Shader "SeaSick/Horizon"
{
    // Distant land, drawn as atmosphere rather than as ground.
    //
    // It deliberately does NOT take Unity's fog. The scene's linear fog ends at
    // 1500 m in clear weather and 430 m in a storm, which is exactly right for
    // the sea — it is what makes the water meet the sky without a seam — and
    // exactly wrong for a mountain, which would be erased at the very distance
    // it is supposed to be legible from. Real distant land behaves this way:
    // haze washes it out but it keeps a little contrast against the sky for a
    // very long way, which is why you can navigate by it.
    //
    // So the haze here is exponential (the physical form: extinction over
    // distance) and CAPPED below 1. The cap is the whole trick — it is what
    // leaves a ridge at 8 km as a faint shape instead of nothing at all.
    Properties
    {
        _LowColour  ("Low land", Color) = (0.26, 0.30, 0.27, 1)
        _HighColour ("High land", Color) = (0.42, 0.44, 0.47, 1)
        _HazeDistance ("Haze e-folding distance, m", Float) = 2200
        _MaxHaze ("Maximum haze (below 1 leaves a silhouette)", Range(0,1)) = 0.94
        _BaseHaze ("Extra haze at sea level (atmosphere is thicker low down)", Range(0,1)) = 0.25
        _BaseHazeHeight ("Height over which that extra haze eases off, m", Float) = 90
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }

        Pass
        {
            Name "Horizon"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 colour     : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  height01   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _LowColour;
                float4 _HighColour;
                float  _HazeDistance;
                float  _MaxHaze;
                float  _BaseHaze;
                float  _BaseHazeHeight;
            CBUFFER_END

            // Pushed every frame by HorizonField, from RenderSettings.fogColor,
            // so dusk, night and storm all arrive without a second palette.
            float4 _HorizonHaze;

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.height01 = v.colour.r;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 land = lerp(_LowColour.rgb, _HighColour.rgb, saturate(i.height01));

                float d = distance(i.positionWS, GetCameraPositionWS());
                float haze = 1.0 - exp(-d / max(_HazeDistance, 1.0));

                // Atmosphere is thicker at the bottom, so a headland's foot
                // dissolves before its ridge does. This is what stops the
                // field reading as a cut-out sitting on the water.
                float low = 1.0 - saturate(i.positionWS.y / max(_BaseHazeHeight, 1.0));
                haze = saturate(haze + low * _BaseHaze * haze);

                haze = min(haze, _MaxHaze);
                return half4(lerp(land, _HorizonHaze.rgb, haze), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
