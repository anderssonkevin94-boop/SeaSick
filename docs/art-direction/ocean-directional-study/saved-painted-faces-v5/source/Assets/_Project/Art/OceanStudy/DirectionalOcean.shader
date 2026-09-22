Shader "SeaSick/Studies/DirectionalOcean"
{
 Properties {
 _Deep("Trough blue",Color)=(.035,.26,.54,1)
 _Mid("Wave blue",Color)=(.045,.38,.66,1)
 _Light("Lit face blue",Color)=(.09,.49,.74,1)
 _FoamAmount("Foam visibility",Range(0,1))=0
 _Foam("Foam",Color)=(.86,.95,.96,1)
 _CrestVariation("Crest variation",Range(0,1))=.75
 _FoamPersistence("Foam lifetime (seconds)",Range(.3,2.5))=1.25
 _StudyTime("Study time",Float)=0
 _Sharpness("Crest sharpness",Range(0,0.4))=.27
 _Amplitude("Wave height multiplier",Range(.2,2))=1
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass {
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 half4 _Deep,_Mid,_Light,_Foam;
 float _StudyTime,_Sharpness,_Amplitude,_CrestVariation,_FoamPersistence,_FoamAmount;
 CBUFFER_END
 struct A {float4 positionOS:POSITION;};
 struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float2 baseXZ:TEXCOORD1;float fog:TEXCOORD2;};
 float Hash(float p){return frac(sin(p*127.1)*43758.5453);}
 float Noise(float p){float f=frac(p);return lerp(Hash(floor(p)),Hash(floor(p)+1),f*f*(3-2*f));}
 // Rounded triangular ridge: nearly planar flanks, finite curvature at
 // joins. Analytic derivative avoids faceted mesh normals or clipping seams.
 float Ridge(float p){return 1-2*acos(.985*cos(p))/3.14159265;}
 float RidgeSlope(float p){float c=cos(p);return -2*.985*sin(p)/(3.14159265*sqrt(max(1-.970225*c*c,.0001)));}
 // Shared displacement and derivative: colored faces and foam describe the
 // very same peaked waves that change the mesh silhouette.
 void Wave(float2 p,float2 d,float wavelength,float amp,float offset,inout float h,inout float2 gradient,inout float foam){
  d=normalize(d);float2 crossDir=float2(-d.y,d.x);float across=dot(p,crossDir);
  float k=6.2831853/wavelength;
  float omega=sqrt(9.81*k);
  float along=dot(p,d);
  // Two travelling envelopes shorten the crests into groups. Their spatial
  // derivatives are included, so the painted normal matches the geometry.
  float group=along-omega/k*.47*_StudyTime;
  float u=across*.105+group*.13+offset;
  float v=across*.217-group*.07+offset*2.3;
  float packet=1-_CrestVariation*(.38-.22*sin(u)-.16*sin(v));
  float2 packetGradient=_CrestVariation*(.22*cos(u)*(.105*crossDir+.13*d)
                      +.16*cos(v)*(.217*crossDir-.07*d));
  float bend=.75*Ridge(across*.15+offset)+.25*Ridge(across*.31+along*.045+offset*3);
  float phase=k*along+bend-omega*_StudyTime+offset;
  float2 phaseGradient=k*d+.1125*RidgeSlope(across*.15+offset)*crossDir
      +.25*RidgeSlope(across*.31+along*.045+offset*3)*(.31*crossDir+.045*d);
  float s=sin(phase),c=cos(phase);
  float q=phase-1.57079633;
  float profile=Ridge(q)+.24*sin(q);
  float profileSlope=RidgeSlope(q)+.24*cos(q);
  h+=amp*packet*profile;
  gradient+=amp*(packet*profileSlope*phaseGradient+profile*packetGradient);

  // Time since this crest crossed the surface point. The half-cycle wrap is
  // outside the visible foam lifetime, avoiding a live-pattern discontinuity.
  float age=atan2(c,s)/omega;
  float birthGroup=group+omega/k*.47*age;
  float birthPacket=1-_CrestVariation*(.38-.22*sin(across*.105+birthGroup*.13+offset)
                     -.16*sin(across*.217-birthGroup*.07+offset*2.3));
  float energy=amp*k*birthPacket*(1+2*_Sharpness)*_Amplitude;
  float breaking=smoothstep(.13,.195,energy);
  float segment=across*.48+offset*17;
  float alongCrest=lerp(Hash(floor(segment)),Hash(floor(segment)+1),frac(segment));
  float form=smoothstep(-.12,.035,age);
  float life=min(_FoamPersistence,2.6/omega);
  float fade=1-smoothstep(life*.18,life,age);
  // The crest leaves scraps in place. Erosion opens holes as they age rather
  // than dragging a solid white strip along with the travelling wave.
  float row=floor(along*.9),f=frac(along*.9);f=f*f*(3-2*f);
  float fragments=lerp(Noise(across*1.5+row*19+offset),Noise(across*1.5+(row+1)*19+offset),f);
  float erosion=smoothstep(.24+saturate(age/_FoamPersistence)*.42,
                          .37+saturate(age/_FoamPersistence)*.42,fragments);
  float crestBreak=smoothstep(.40,.64,alongCrest);
  float fresh=1-smoothstep(.005,.075,age);
  float remnant=erosion*fade*.055;
  foam=max(foam,breaking*form*crestBreak*max(fresh,remnant));

 }
 void Surface(float2 p,out float h,out float2 gradient,out float foam){
  h=0;gradient=0;foam=0;
  Wave(p,float2(.20,1),32,1.15,0,h,gradient,foam);
  Wave(p,float2(-.18,1),14.5,.35,2.1,h,gradient,foam);
  Wave(p,float2(.45,1),5.4,.085,4.3,h,gradient,foam);
  Wave(p,float2(-.55,1),2.6,.012,1.7,h,gradient,foam);
  h*=_Amplitude;gradient*=_Amplitude;
 }
 V Vert(A i){V o;float3 p=TransformObjectToWorld(i.positionOS.xyz);float h,f;float2 g;Surface(p.xz,h,g,f);o.baseXZ=p.xz;p.y+=h;o.world=p;o.positionCS=TransformWorldToHClip(p);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 half4 Frag(V i):SV_Target {
  float h,foam;float2 g;Surface(i.baseXZ,h,g,foam);
  float3 n=normalize(float3(-g.x,1,-g.y));
  // One dominant swell carries the smaller ripples. Color follows the
  // actual light and surface orientation continuously, not threshold bands.
  Light sun=GetMainLight();
  float3 view=normalize(_WorldSpaceCameraPos-i.world);
  float dist=distance(_WorldSpaceCameraPos,i.world);
  float facing=saturate(dot(n,sun.direction));
  // Discrete pigment regions follow the displaced surface's slope and
  // elevation. Intersecting waves break these boundaries into tapered faces.
  // Screen-space derivatives soften only the pixel edge, not the whole face.
  float faceSignal=facing+.065*h;
  float aa=max(fwidth(faceSignal)*.85,.003);
  float middle=smoothstep(.61-aa,.61+aa,faceSignal);
  float lit=smoothstep(.77-aa,.77+aa,faceSignal);
  float tip=smoothstep(.88-aa,.88+aa,faceSignal);
  half3 color=lerp(_Deep.rgb*.74,_Mid.rgb*.80,middle);
  color=lerp(color,_Light.rgb*.86,lit);
  color=lerp(color,_Light.rgb*1.07,tip*.55);
  color*=.84+.23*smoothstep(.38,.96,facing)+.06*smoothstep(-1,1.1,h);
  color*=lerp(half3(1,1,1),sun.color,.18);
  // Keep only a little grazing reflection; wide specular haze concealed
  // the blue face boundaries in the preceding study.
  float fresnel=pow(1-saturate(dot(n,view)),5);
  color=lerp(color,half3(.12,.33,.52),fresnel*.09);
  // Distant white lines lose coverage before becoming flickering pixels.
  foam*=1-smoothstep(90,220,dist);
  // Study wake: fragmented Kelvin shoulders and scattered churn, composited
  // on the displaced surface, with no billboard or separate hovering plane.
  float aft=-i.baseXZ.y-4.5;
  float spread=1.5+max(aft,0)*.18;
  float edge=abs(i.baseXZ.x)-spread-.35*sin(aft*.65);
  float arm=exp(-edge*edge/max(.12+aft*.012,.05));
  arm*=smoothstep(.25,.50,Noise(aft*.70+sign(i.baseXZ.x)*12));
  float2 q=i.baseXZ*float2(1.4,.65)+float2(0,_StudyTime*.3);
  float row=floor(q.y),f=frac(q.y);f=f*f*(3-2*f);
  float churnNoise=lerp(Noise(q.x+row*13),Noise(q.x+(row+1)*13),f);
  float churn=smoothstep(.47,.53,churnNoise)*(1-smoothstep(.35,.90,abs(i.baseXZ.x)/spread));
  float wake=max(arm,churn*.9)*smoothstep(0,1.2,aft)*(1-smoothstep(16,48,aft));
  foam=max(foam,wake);
  float foamAA=max(fwidth(foam)*.8,.015);
  foam=max(smoothstep(.32-foamAA,.44+foamAA,foam),foam*.38);
  color=lerp(color,_Foam.rgb,saturate(foam)*_FoamAmount);
  return half4(MixFog(color,i.fog),1);
 }
 ENDHLSL
 }
 }
}
