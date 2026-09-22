using Unity.Mathematics;
namespace SeaSick.Ocean {
/// Bounded pointed-wave detail shared by rendering and buoyancy.
/// HLSL twin: Art/Shaders/Ocean/GraphicWaveDetail.hlsl.
public static class GraphicWaveDetail {
static float Ridge(float p)=>1-2*math.acos(.985f*math.cos(p))/math.PI;
static float Slope(float p){float c=math.cos(p);return -2*.985f*math.sin(p)/(math.PI*math.sqrt(math.max(1-.970225f*c*c,.0001f)));}
static float4 Wave(float2 p,float time,float2 d,float length,float amp,float offset){
 d=math.normalize(d);float2 cross=new float2(-d.y,d.x);
 float across=math.dot(p,cross),along=math.dot(p,d),k=2*math.PI/length,omega=math.sqrt(9.81f*k);
 float group=along-omega/k*.47f*time;
 float u=across*.105f+group*.13f+offset,v=across*.217f-group*.07f+offset*2.3f;
 float packet=1-.75f*(.38f-.22f*math.sin(u)-.16f*math.sin(v));
 float2 pg=.75f*(.22f*math.cos(u)*(.105f*cross+.13f*d)+.16f*math.cos(v)*(.217f*cross-.07f*d));
 float phase=k*along+.75f*Ridge(across*.15f+offset)+.25f*Ridge(across*.31f+along*.045f+offset*3)-omega*time+offset;
 float2 phaseG=k*d+.1125f*Slope(across*.15f+offset)*cross+.25f*Slope(across*.31f+along*.045f+offset*3)*(.31f*cross+.045f*d);
 float q=phase-math.PI*.5f,profile=Ridge(q)+.24f*math.sin(q),ps=Slope(q)+.24f*math.cos(q);
 float2 g=amp*(packet*ps*phaseG+profile*pg);
 float packetDt=.75f*(.22f*math.cos(u)*.13f-.16f*math.cos(v)*.07f)*(-omega/k*.47f);
 return new float4(amp*packet*profile,g.x,g.y,amp*(packetDt*profile-packet*ps*omega));
}
/// x height, yz spatial gradient, w vertical velocity.
public static float4 Evaluate(float2 p,float time)=>.70f*(
 Wave(p,time,new float2(.20f,1),32,1.15f,0)+
 Wave(p,time,new float2(-.18f,1),14.5f,.35f,2.1f)+
 Wave(p,time,new float2(.45f,1),5.4f,.085f,4.3f)+
 Wave(p,time,new float2(-.55f,1),2.6f,.012f,1.7f));
}}
