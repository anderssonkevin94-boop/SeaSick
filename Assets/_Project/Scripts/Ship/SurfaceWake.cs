using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using SeaSick.Ocean;
namespace SeaSick.Ship
{
    /// A bounded history of the ship's actual path. Foam stays in world space,
    /// spreads with age, and samples the sea instead of facing the camera.
    public sealed class SurfaceWake : MonoBehaviour
    {
        const int Rows=96, Columns=9;
        struct Stamp { public Vector3 point,right; public float born,distance,width,speed,speed01,turn01; }
        readonly Stamp[] stamps=new Stamp[Rows];
        readonly Vector3[] vertices=new Vector3[Rows*Columns];
        readonly Vector2[] uv=new Vector2[Rows*Columns];
        readonly Color[] colors=new Color[Rows*Columns];
        NativeArray<float3> queries; NativeArray<OceanSample> samples;
        int count; float distance; float length=24,beam=8;
        Mesh mesh; Material material; GameObject surface; ShipMotor motor; Rigidbody body;
        public void Configure(float hullLength,float hullBeam){length=hullLength;beam=hullBeam;count=0;}
        void Awake()
        {
            queries=new NativeArray<float3>(Rows*Columns,Allocator.Persistent);
            samples=new NativeArray<OceanSample>(Rows*Columns,Allocator.Persistent);
            motor=GetComponent<ShipMotor>();body=GetComponent<Rigidbody>();
            surface=new GameObject("Surface foam wake");
            mesh=new Mesh {name="Surface wake history"};mesh.MarkDynamic();
            surface.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=surface.AddComponent<MeshRenderer>();
            material=new Material(Resources.Load<Shader>("Shaders/SurfaceWake"));
            renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            var triangles=new int[(Rows-1)*(Columns-1)*6];int t=0;
            for(int r=0;r<Rows-1;r++)for(int c=0;c<Columns-1;c++){
                int a=r*Columns+c,b=a+Columns;
                triangles[t++]=a;triangles[t++]=b;triangles[t++]=a+1;
                triangles[t++]=a+1;triangles[t++]=b;triangles[t++]=b+1;
            }
            mesh.vertices=vertices;mesh.triangles=triangles;
        }
        void LateUpdate()
        {
            if(!OceanSampler.Ready)return;
            float speed=Mathf.Abs(motor.CurrentSpeed);
            Vector3 stern=transform.position-transform.forward*(length*.40f);stern.y=0;
            float gap=count>0?Vector3.Distance(stern,stamps[0].point):0;
            if(count>0&&gap>Mathf.Max(40,length*2)){count=0;gap=0;} // teleports/refits never join trails
            if(speed>.6f&&(count==0||gap>.85f)){
                for(int i=Mathf.Min(count,Rows-1);i>0;i--)stamps[i]=stamps[i-1];
                distance+=gap;
                var right=transform.right;right.y=0;right.Normalize();
                // Speed and turn are baked into the stamp as she lays it, so
                // the trail REMEMBERS them: the stretch run at full ahead stays
                // broad and white behind her after she eases, and the arc of
                // a hard turn is a wider, churned band where the stern skidded.
                float s01=JuiceTuning.Speed01(motor);
                float turn01=JuiceTuning.Turn01(motor,JuiceTuning.YawRateDeg(body))*Mathf.Sqrt(s01);
                stamps[0]=new Stamp{point=stern,right=right,born=Time.time,distance=distance,
                    width=beam*.33f*Mathf.Lerp(.85f,1.2f,s01)*(1+.35f*turn01),speed=speed,speed01=s01,turn01=turn01};
                count=Mathf.Min(count+1,Rows);
            }
            while(count>0&&Time.time-stamps[count-1].born>9)count--;
            // `wakeScale`, live: under 1 fades the wake, over 1 spreads it
            // faster, broadens it and makes it more solid. Zero hides the mesh
            // (one draw call saved) but the history keeps being laid, so
            // turning it back up shows the trail she has actually sailed.
            float wk=Mathf.Max(0f,JuiceTuning.wakeScale);
            surface.SetActive(count>1&&wk>.001f);if(count<2||wk<=.001f)return;
            float fade=Mathf.Min(wk,1f),over=Mathf.Max(0f,wk-1f);
            float widthK=1+.3f*over,spreadK=1+over,solidK=1+.5f*over;
            for(int r=0;r<Rows;r++){
                var s=stamps[Mathf.Min(r,count-1)];float age=Time.time-s.born;
                float width=(s.width+age*Mathf.Min(s.speed*.055f,.65f)*spreadK)*widthK;
                float alpha=Mathf.Clamp01(age*5+.3f)*Mathf.Exp(-age*.7f)*Mathf.Clamp01((7-age)/2)*Mathf.Clamp01(s.speed/3)
                    *Mathf.Lerp(.6f,1f,s.speed01)*(1+.3f*s.turn01);
                alpha=Mathf.Clamp01(alpha*fade*solidK);
                for(int c=0;c<Columns;c++){
                    float x=c/(float)(Columns-1)*2-1;
                    var p=s.point+s.right*(x*width);

                    int i=r*Columns+c;vertices[i]=p;queries[i]=p;uv[i]=new Vector2(x,s.distance);
                    colors[i]=new Color(1,1,1,r<count?alpha:0);
                }
            }
            OceanSampler.SampleBatch(queries.GetSubArray(0,count*Columns),samples.GetSubArray(0,count*Columns),default).Complete();
            for(int i=0;i<Rows*Columns;i++)vertices[i].y=samples[Mathf.Min(i,count*Columns-1)].height+.16f;
            mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.RecalculateBounds();
        }
        void OnDisable(){if(surface)surface.SetActive(false);count=0;}
        void OnDestroy(){if(queries.IsCreated)queries.Dispose();if(samples.IsCreated)samples.Dispose();if(surface)Destroy(surface);if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
