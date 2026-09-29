Shader "SeaSick/Coaster Paint"
{
 Properties { _Tint("Tint",Color)=(1,1,1,1) }
 SubShader
 {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
  Pass
  {
   Name "Forward" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _Tint;
   CBUFFER_END
   // SkyDirector globals keep the art fill tied to weather and time of day.
   float _SS_Night;
   float _SS_Storminess;
   struct A {float4 p:POSITION;float3 n:NORMAL;float4 c:COLOR;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float4 c:COLOR;float fog:TEXCOORD2;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.c=a.c;o.fog=ComputeFogFactor(o.p.z);return o;}
   half4 frag(V i):SV_Target
   {
    float3 n=normalize(i.n);Light main=GetMainLight(TransformWorldToShadowCoord(i.world));
    // Broad sky/bounce fill preserves the authored wood and navy in shade.
    // It fades at night; lanterns still supply the local warm light.
    float day=(1-saturate(_SS_Night))*lerp(1,.55,saturate(_SS_Storminess));
    float hemi=saturate(n.y*.5+.5);
    float3 bounce=lerp(float3(.21,.18,.145),float3(.42,.46,.52),hemi)*day;
    float3 ambient=max(SampleSH(n),float3(.018,.024,.034))+bounce;
    float diffuse=saturate((dot(n,main.direction)+.25)/1.25);
    float visibility=lerp(.22,1,main.shadowAttenuation);
    float3 lighting=ambient+main.color*diffuse*visibility;
    // Cool moonlit fill reveals rails and hull edges while lamps remain the warm key.
    ambient += float3(.025,.045,.08)*saturate(_SS_Night)*(.35+.65*hemi);
    lighting=ambient+main.color*diffuse*visibility;
    // A restrained, broad dielectric highlight reveals bevels without varnish.
    float3 viewDir=GetWorldSpaceNormalizeViewDir(i.world);
    float3 halfDir=SafeNormalize(main.direction+viewDir);
    float3 specular=main.color*pow(saturate(dot(n,halfDir)),12)*.065*diffuse*visibility;
    #if defined(_ADDITIONAL_LIGHTS)
    InputData inputData=(InputData)0;inputData.positionWS=i.world;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
    uint count=GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
      Light l=GetAdditionalLight(lightIndex,i.world);lighting+=l.color*saturate(dot(n,l.direction))*l.distanceAttenuation*l.shadowAttenuation;
    LIGHT_LOOP_END
    #endif
    float hi=max(i.c.r,max(i.c.g,i.c.b)), lo=min(i.c.r,min(i.c.g,i.c.b));
    float iron=(1-smoothstep(.012,.04,hi-lo))*(1-smoothstep(.09,.18,hi));
    float3 charcoal=dot(i.c.rgb,float3(.22,.70,.08))*float3(.72,.78,.84);
    float3 paint=lerp(i.c.rgb,charcoal,iron);
    return half4(MixFog(paint*_Tint.rgb*lighting+specular*lerp(1,.7,iron),i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
  UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
}
