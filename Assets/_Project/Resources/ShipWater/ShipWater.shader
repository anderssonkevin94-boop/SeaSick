Shader "SeaSick/ShipWater"
{
 Properties { _ZWrite("Depth write",Float)=0 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+8" "RenderType"="Transparent" }
 Pass {
 Cull Off ZWrite [_ZWrite] Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 float _SS_Night, _SS_NightBodyDim;
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;};
 struct V {float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float4 color:COLOR;float fog:TEXCOORD1;};
 V Vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.color=i.color;o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 half4 Frag(V i):SV_Target {
  clip(i.color.a-.008);
  Light sun=GetMainLight();float shade=.56+.44*saturate(dot(normalize(i.normalWS),sun.direction));
  float night=lerp(1,max(.18,_SS_NightBodyDim),saturate(_SS_Night));
  half3 col=i.color.rgb*(sun.color*shade*.48+night*.52);
  return half4(MixFog(col,i.fog),i.color.a);
 }
 ENDHLSL
 }
 }
}
