// Standalone staging asset. Not imported into the Unity project.
Shader "SeaSick/Skybox/Graphic Adventure Day"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Sky panorama (2:1, sRGB)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure multiplier", Range(0,4)) = 1
        _Rotation ("Rotation", Range(0,360)) = 0
        _SeamWidth ("Wrap blend width", Range(0.001,0.08)) = 0.025
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Tint;
            float _Exposure, _Rotation, _SeamWidth;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }
            half3 SampleSky(float2 uv)
            {
                float2 border = abs(_MainTex_TexelSize.xy) * 0.5;
                uv.y = clamp(uv.y, border.y, 1.0-border.y);
                float u = frac(uv.x);
                half3 a = tex2D(_MainTex, float2(clamp(u,border.x,1.0-border.x),uv.y)).rgb;
                half3 b = tex2D(_MainTex, float2(clamp(1.0-u,border.x,1.0-border.x),uv.y)).rgb;
                float weight = 0.5 * (1.0-smoothstep(0.0,_SeamWidth,min(u,1.0-u)));
                return lerp(a,b,weight);
            }
            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float u = atan2(d.x,d.z) / (2.0*UNITY_PI) + 0.5 + _Rotation/360.0;
                float v = asin(clamp(d.y,-1.0,1.0))/UNITY_PI + 0.5;
                half3 sky = SampleSky(float2(u,v));
                float poleV = d.y>0 ? 1.0-abs(_MainTex_TexelSize.y)*0.5 : abs(_MainTex_TexelSize.y)*0.5;
                half3 pole = tex2D(_MainTex,float2(0.5,poleV)).rgb;
                sky = lerp(sky,pole,smoothstep(0.96,1.0,abs(d.y)));
                return half4(sky*_Tint.rgb*_Exposure,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
