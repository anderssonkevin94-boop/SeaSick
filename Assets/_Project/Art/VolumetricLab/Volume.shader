Shader "SeaSick/Lab/ShadowedSunVolume" {
Properties { _Density("Haze density", Range(0,.12))=.012 _Strength("Scattering strength",Range(0,4))=0.7 }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" "RenderType"="Transparent" }
Pass { Cull Front ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
CBUFFER_START(UnityPerMaterial)
float _Density, _Strength;
CBUFFER_END
struct A {float4 positionOS:POSITION;}; struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;};
V vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
half4 frag(V i):SV_Target {
float2 uv=i.positionCS.xy/_ScaledScreenParams.xy;
float depth=SampleSceneDepth(uv);
#if !UNITY_REVERSED_Z
 depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
#endif
float3 surface=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
float3 origin=_WorldSpaceCameraPos, rd=normalize(i.world-origin);
float3 safe=sign(rd)*max(abs(rd),.00001);
float3 a=(float3(-30,0,-25)-origin)/safe,b=(float3(30,22,35)-origin)/safe;
float3 nearT=min(a,b),farT=max(a,b);
float start=max(0,max(nearT.x,max(nearT.y,nearT.z)));
float end=min(min(farT.x,min(farT.y,farT.z)),distance(origin,surface));
float stepSize=max(0,end-start)/96;
float transmission=1;float3 result=0;
Light sun=GetMainLight();
float facing=saturate(dot(rd,sun.direction));float phase=.3+1.8*pow(facing,6);
[loop] for(int n=0;n<96;n++){
float3 p=origin+rd*(start+(n+.5)*stepSize);
float shadow=MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
float extinction=_Density*exp(-p.y*.065);float opacity=1-exp(-extinction*stepSize);
result+=transmission*opacity*(float3(.10,.15,.23)+sun.color*shadow*phase*_Strength);
transmission*=1-opacity;
}
return half4(result,1-transmission);
}
ENDHLSL
}}}
