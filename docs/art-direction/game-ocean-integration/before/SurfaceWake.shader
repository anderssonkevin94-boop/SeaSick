Shader "SeaSick/SurfaceWake"
{
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" }
 Pass {
 Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 float _SS_Night, _SS_NightBodyDim;
 struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
 struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;float fog:TEXCOORD1;};
 V Vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 float Hash(float2 p){p=frac(p*float2(.1031,.1030));p+=dot(p,p.yx+33.33);return frac((p.x+p.y)*p.x);}
 float Noise(float2 p){float2 q=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(q),Hash(q+float2(1,0)),f.x),lerp(Hash(q+float2(0,1)),Hash(q+1),f.x),f.y);}
 half4 Frag(V i):SV_Target {
   float x=abs(i.uv.x);
   float n=Noise(float2(i.uv.x*8,i.uv.y*.65));
   float fine=Noise(float2(i.uv.x*25,i.uv.y*1.8));
   float arm=1-smoothstep(.035,.12,abs(x-(.72+.15*(n-.5))));
   float churn=(1-smoothstep(.15,.60,x));
   float lace=1-smoothstep(.045,.12,abs(fine-.5));
   float edge=max(arm*(.65+.35*lace),churn*lace*.78)-(.40+n*.38);
   float aa=max(fwidth(edge),.015);
   float alpha=smoothstep(-aa,aa,edge)*i.color.a;
   clip(alpha-.035);
   Light sun=GetMainLight();
   half3 foam=half3(.82,.91,.91)*(sun.color*.40+.60*lerp(1,_SS_NightBodyDim,saturate(_SS_Night)));
   return half4(MixFog(foam,i.fog),alpha);
 }
 ENDHLSL
 }
 }
}
