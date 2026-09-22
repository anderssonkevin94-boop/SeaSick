Shader "SeaSick/ShadowedSunlight" {
Properties { _Density("Haze density", Range(0,.12))=.012 _Strength("Scattering strength",Range(0,4))=0.7 _Range("Distance",Float)=160 _Samples("Samples",Float)=32 }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-200" "RenderType"="Transparent" }
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
float _Density, _Strength, _Range, _Samples;
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
float end=min(_Range,distance(origin,surface));
// Composite before the depth-writing ocean: its displaced surface masks the volume.
// Existing ocean fog retains control over water colour.
float stepSize=end/_Samples;
float transmission=1;float3 result=0;
Light sun=GetMainLight();
float facing=saturate(dot(rd,sun.direction));float phase=.3+1.8*pow(facing,6);
[loop] for(int n=0;n<(int)_Samples;n++){
float3 p=origin+rd*((n+.5)*stepSize);
float shadow=MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
float extinction=_Density*exp(-max(0,p.y)*.009)*smoothstep(0,12,p.y+2);
// Fade the local volume out smoothly rather than ending at a visible wall.
extinction *= 1-smoothstep(_Range*.65,_Range,(n+.5)*stepSize);float opacity=1-exp(-extinction*stepSize);
result+=transmission*opacity*(float3(.10,.15,.23)+sun.color*shadow*phase*_Strength);
transmission*=1-opacity;
}
return half4(result,1-transmission);
}
ENDHLSL
}}}
