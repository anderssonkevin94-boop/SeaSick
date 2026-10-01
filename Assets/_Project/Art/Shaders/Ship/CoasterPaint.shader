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
    // Lanterns, torches, campfire. Both renderers are Forward+, where URP never
    // enables _ADDITIONAL_LIGHTS, so the loop must also compile under
    // USE_CLUSTER_LIGHT_LOOP (it was compiled out: lanterns lit nothing).
    // Falloff = the island contract (EnvironmentToon / TerrainVertexColor),
    // duplicated in Crew/CrewPaint.shader -- keep the three constants in step.
    // Daylight is unaffected: these lights are off by day.
    #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
    {
    const float LampFadePerMetre=0.045; // linear fade, 0 at ~22 m
    const float LampWrapScale=0.6;      // wrapped Lambert n.l*.6+.4
    const float LampWrapFloor=0.4;
    InputData inputData=(InputData)0;inputData.positionWS=i.world;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
    uint count=GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
      Light l=GetAdditionalLight(lightIndex,i.world);
      #if USE_CLUSTER_LIGHT_LOOP
        int pidx=lightIndex;
      #else
        int pidx=GetPerObjectLightIndex(lightIndex);
      #endif
      #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float3 lp=_AdditionalLightsBuffer[pidx].position.xyz;
      #else
        float3 lp=_AdditionalLightsPosition[pidx].xyz;
      #endif
      float3 toLamp=lp-i.world;
      float distanceSqr=max(dot(toLamp,toLamp),HALF_MIN);
      float rangeFade=saturate(l.distanceAttenuation*distanceSqr); // URP range/spot fade, inverse-square cancelled
      float fall=saturate(1.0-sqrt(distanceSqr)*LampFadePerMetre);fall*=fall;
      float wrap=saturate(dot(n,l.direction)*LampWrapScale+LampWrapFloor);
      lighting+=l.color*fall*rangeFade*wrap*l.shadowAttenuation;
    LIGHT_LOOP_END
    }
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
