using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Steamer;
namespace SeaSick.Ship
{
    /// Wave-following bow/wake and blade-contact spray. Visual only: never changes forces or ocean settings.
    [DefaultExecutionOrder(100)]
    public sealed class ShipWaterEffects : MonoBehaviour
    {
        const int WakeRows=48,BowRows=14,MaxBlades=32,MaxDrops=24,MaxBursts=12;
        struct Blade {public bool primed,wet;public float gap,drained;public Vector3 previous;}
        struct Drop {public bool live;public Vector3 position,velocity;public float age,size;}
        struct Burst {public bool live;public Vector3 position,forward;public float age,strength;public int variant;}
        struct Stamp {public Vector3 point,right;public float time,speed,distance,halfWidth;public int direction;}
        readonly Blade[] blades=new Blade[MaxBlades*2];readonly Drop[] drops=new Drop[MaxDrops];readonly Burst[] bursts=new Burst[MaxBursts];
        readonly Stamp[] stamps=new Stamp[WakeRows];
        OceanProbeRegistry.Handle[] bladeQueries,bowQueries,wakeQueries,dropQueries,burstQueries;
        ShipWaterGeometry surface,volume;ShipWaterGeometry.Shape glob;readonly ShipWaterGeometry.Shape[] jets=new ShipWaterGeometry.Shape[4];
        ShipWaterSettings settings;HullFormData hull;Transform wheel;Rigidbody body;PaddleDrive drive;
        SurfaceWake legacyWake;Transform legacyEmitters;bool legacyWakeEnabled,legacyEmittersActive,suppressed,ready,queriesReady;
        int bladeCount,count,eventId,dropsCursor,burstsCursor;float direction=1,speed,travel,visualRate,lastAngle;bool anglePrimed;
        Vector3 lastPosition;ShipWaterProbeFeeder feeder;
        public int EntryEvents {get;private set;} public int ExitEvents {get;private set;}
        public int ActiveDrops {get;private set;} public int ActiveBursts {get;private set;}
        public int WakeCount=>count;public int ProbeCount=>bladeCount*2+BowRows*2+WakeRows*2+MaxDrops+MaxBursts;
        public int Triangles=>(surface?.TriangleCount??0)+(volume?.TriangleCount??0);
        public bool Running=>ready&&settings!=null&&settings.effectsEnabled&&settings.intensity>0&&wheel!=null&&hull!=null&&hull.StationCount>1;
        public void Configure(HullFormData data,Transform drawnWheel)
        {
            ReleaseQueries();hull=data;wheel=drawnWheel;body=GetComponent<Rigidbody>();drive=GetComponent<PaddleDrive>();
            settings=Resources.Load<ShipWaterSettings>("ShipWater/Settings");
            if(settings==null||hull==null||wheel==null)return;
            if(surface==null){
                var shader=Resources.Load<Shader>("ShipWater/ShipWater");var mesh=Resources.Load<Mesh>("ShipWater/WaterGlob");
                if(!shader||!mesh){Debug.LogWarning("Ship water assets missing; retaining legacy effects.",this);return;}
                glob=new ShipWaterGeometry.Shape(mesh);
                for(int i=0;i<4;i++){mesh=Resources.Load<Mesh>("ShipWater/Jet"+i);if(!mesh)return;jets[i]=new ShipWaterGeometry.Shape(mesh);}
                surface=new ShipWaterGeometry("Ship surface tracers",shader,false);volume=new ShipWaterGeometry("Paddle carried water and spray",shader,true);
            }
            // F30's drawn rotor uses ten/fifteen blades; hydraulic wheelFloats remains unchanged.
            var view=GetComponentInChildren<Modular.ModularShipView>();
            bool coaster=view&&Modular.CoasterFamily.Is(view.Current?.Find("stern")?.moduleId);
            bladeCount=coaster?Mathf.Clamp(Mathf.Max(10,Mathf.RoundToInt(Mathf.PI*4*data.wheelRadius/1.38f)),1,MaxBlades):Mathf.Clamp(data.wheelFloats,1,MaxBlades);ready=true;ResetHistory();
            feeder=GetComponent<ShipWaterProbeFeeder>();if(!feeder)feeder=gameObject.AddComponent<ShipWaterProbeFeeder>();feeder.Owner=this;
            if(isActiveAndEnabled&&Running)RegisterQueries();
        }
        void OnEnable(){if(ready&&Running)RegisterQueries();}
        static OceanProbeRegistry.Handle[] Register(int n,Vector3 at){var a=new OceanProbeRegistry.Handle[n];for(int i=0;i<n;i++)a[i]=OceanProbeRegistry.Register(at);return a;}
        void RegisterQueries()
        {
            if(bladeQueries!=null)return;
            var p=transform.position;bladeQueries=Register(bladeCount*2,p);bowQueries=Register(BowRows*2,p);wakeQueries=Register(WakeRows*2,p);
            dropQueries=Register(MaxDrops,p);burstQueries=Register(MaxBursts,p);WriteQueries();
        }
        static void Release(ref OceanProbeRegistry.Handle[] a){if(a==null)return;foreach(var h in a)OceanProbeRegistry.Unregister(h);a=null;}
        void ReleaseQueries(){Release(ref bladeQueries);Release(ref bowQueries);Release(ref wakeQueries);Release(ref dropQueries);Release(ref burstQueries);queriesReady=false;}
        void ResetHistory(){count=0;anglePrimed=false;queriesReady=false;lastPosition=transform.position;System.Array.Clear(blades,0,blades.Length);System.Array.Clear(drops,0,drops.Length);System.Array.Clear(bursts,0,bursts.Length);}
        public void ClearTrail(){ResetHistory();}
        Vector3 BladePoint(int index)
        {
            float a=(index/2)*Mathf.PI*2/bladeCount;
            return wheel.position+wheel.rotation*new Vector3((index%2==0?-1:1)*hull.wheelWidth*.43f,Mathf.Cos(a)*hull.wheelRadius,Mathf.Sin(a)*hull.wheelRadius);
        }
        static float Height(OceanProbeRegistry.Handle q){return q.sample.height;}
        static Vector3 Normal(OceanProbeRegistry.Handle q){Vector3 n=q.sample.normal;return n.sqrMagnitude>.1f?n.normalized:Vector3.up;}
        static Vector3 OnWater(OceanProbeRegistry.Handle q,Vector3 p,float lift)
        {
            Vector3 n=Normal(q),delta=p-q.position;
            p.y=q.sample.height-(n.x*delta.x+n.z*delta.z)/Mathf.Max(.3f,n.y)+lift;return p;
        }
        public void WriteQueries()
        {
            if(!Running||bladeQueries==null)return;
            for(int i=0;i<bladeCount*2;i++)bladeQueries[i].position=BladePoint(i);
            for(int i=0;i<BowRows*2;i++){
                var q=bowQueries[i];int row=i/2;int side=i%2==0?-1:1;float u=row/(float)(BowRows-1);
                float lo=hull.stations[0].z,hi=hull.stations[hull.StationCount-1].z;
                float z=direction>0?Mathf.Lerp(hi-.06f,hi-Mathf.Min(hull.beam*.8f,(hi-lo)*.38f),u):Mathf.Lerp(lo+.06f,lo+Mathf.Min(hull.beam*.8f,(hi-lo)*.38f),u);
                float y=q.sampledFrame>0?transform.InverseTransformPoint(new Vector3(q.position.x,Height(q),q.position.z)).y:0;
                float keel,deck;float breadth=HalfBreadth(hull,z,y,out keel,out deck);
                q.position=transform.TransformPoint(new Vector3(side*(breadth+settings.bowOffset),y,z));
            }
            for(int row=0;row<WakeRows;row++)for(int side=0;side<2;side++){
                var q=wakeQueries[row*2+side];if(row>=count){q.position=transform.position;continue;}
                var s=stamps[row];float age=Time.time-s.time;float width=s.halfWidth+age*settings.wakeSpread;
                q.position=s.point+s.right*((side==0?-1:1)*width);
            }
            for(int i=0;i<MaxDrops;i++)dropQueries[i].position=drops[i].live?drops[i].position:wheel.position;
            for(int i=0;i<MaxBursts;i++)burstQueries[i].position=bursts[i].live?bursts[i].position:wheel.position;
        }
        // Hull profile rather than fixed visual coordinates: works after a modular refit.
        public static float HalfBreadth(HullFormData data,float z,float y,out float keel,out float deck)
        {
            keel=deck=0;if(data?.stations==null||data.StationCount==0)return 0;
            int j=0;while(j<data.StationCount-2&&data.stations[j+1].z<z)j++;
            var a=data.stations[j];var b=data.stations[Mathf.Min(j+1,data.StationCount-1)];float t=Mathf.InverseLerp(a.z,b.z,z);
            keel=Mathf.Lerp(a.keelY,b.keelY,t);deck=Mathf.Lerp(a.deckY,b.deckY,t);
            return Mathf.Lerp(StationBreadth(a,y),StationBreadth(b,y),t);
        }
        static float StationBreadth(HullFormStation s,float y)
        {
            if(s.y==null||s.y.Length==0)return 0;
            int i=0;while(i<s.y.Length-2&&s.y[i+1]<y)i++;
            int j=Mathf.Min(i+1,s.y.Length-1);return Mathf.Lerp(s.halfBreadth[i],s.halfBreadth[j],Mathf.InverseLerp(s.y[i],s.y[j],y));
        }
        void SuppressLegacy(bool on)
        {
            if(on){
                if(!legacyWake)legacyWake=GetComponent<SurfaceWake>();
                if(!legacyEmitters)legacyEmitters=transform.Find("FoamEmitters");
                if(!suppressed){legacyWakeEnabled=legacyWake&&legacyWake.enabled;legacyEmittersActive=legacyEmitters&&legacyEmitters.gameObject.activeSelf;suppressed=true;}
                if(legacyWake)legacyWake.enabled=false;if(legacyEmitters)legacyEmitters.gameObject.SetActive(false);
            }else if(suppressed){if(legacyWake)legacyWake.enabled=legacyWakeEnabled;if(legacyEmitters)legacyEmitters.gameObject.SetActive(legacyEmittersActive);suppressed=false;}
        }
        void LateUpdate()
        {
            if(!Running||!OceanSampler.Ready){SuppressLegacy(false);if(surface!=null){surface.root.SetActive(false);volume.root.SetActive(false);}if(!Running)ReleaseQueries();return;}
            if(bladeQueries==null)RegisterQueries();
            if(bladeQueries[0].sampledFrame==0)return;
            SuppressLegacy(true);
            float dt=Time.deltaTime;if(dt<=0)return;
            if(Vector3.Distance(transform.position,lastPosition)>Mathf.Max(20,hull.lwl*2)||dt>.3f)ResetHistory();
            lastPosition=transform.position;
            Vector3 velocity=body?body.linearVelocity:Vector3.zero;
            Vector3 waterVelocity=bladeQueries[0].sample.velocity;
            float way=Vector3.Dot(velocity-waterVelocity,transform.forward);speed=Mathf.Abs(way);
            float nextDirection=Mathf.Abs(way)>.25f?Mathf.Sign(way):direction;
            if(nextDirection!=direction){count=0;direction=nextDirection;}
            float angle=wheel.localEulerAngles.x;
            visualRate=anglePrimed?Mathf.DeltaAngle(lastAngle,angle)*Mathf.Deg2Rad/dt:0;lastAngle=angle;anglePrimed=true;
            if(!queriesReady){for(int i=0;i<bladeCount*2;i++){var p=BladePoint(i);blades[i].previous=p;blades[i].gap=p.y-Height(bladeQueries[i]);blades[i].wet=blades[i].gap<0;blades[i].primed=true;}queriesReady=true;}
            surface.Clear();volume.Clear();
            UpdateBlades(dt);UpdateDrops(dt);UpdateBursts(dt);UpdateWake();DrawBow();DrawChurn();
            volume.Upload(true);surface.Upload(true);
        }
        static float Noise01(int seed){uint x=(uint)seed;x^=x>>16;x*=0x7feb352d;x^=x>>15;x*=0x846ca68b;x^=x>>16;return (x&65535)/65535f;}
        void UpdateBlades(float dt)
        {
            float h=settings.contactHysteresis;float energy=Mathf.Clamp01(Mathf.Abs(visualRate)*hull.wheelRadius/3)*settings.intensity;
            for(int i=0;i<bladeCount*2;i++){
                ref var b=ref blades[i];Vector3 p=BladePoint(i);var q=bladeQueries[i];float gap=p.y-OnWater(q,p,0).y,closing=(gap-b.gap)/dt;
                bool entered=!b.wet&&b.gap> -h&&gap< -h&&closing<-.05f;
                bool exited=b.wet&&gap>h&&closing>.05f;
                if(entered){b.wet=true;if(energy>.05f){EntryEvents++;SpawnBurst(p,i%2==0?-1:1,energy);}}
                if(exited){b.wet=false;b.drained=.6f;if(energy>.04f){ExitEvents++;Vector3 v=(p-b.previous)/dt;SpawnDrop(p,v*.50f+(body?body.GetPointVelocity(p)*.50f:Vector3.zero),energy);}}
                b.drained=Mathf.Max(0,b.drained-dt);
                if(!b.wet&&b.drained>0&&gap>-.03f&&gap<hull.wheelRadius*1.2f){
                    float wet=Mathf.Clamp01(b.drained/.40f);var c=settings.waterColor;
                    volume.ShapeAt(glob,p,wheel.rotation,new Vector3(hull.wheelWidth*.22f*wet,.07f*wet,.11f*wet),c);
                    if(b.drained>.20f){var hang=p-Vector3.up*Mathf.Min(gap*.35f,.22f);volume.ShapeAt(glob,hang,Quaternion.identity,new Vector3(.065f,.13f,.065f)*wet,Color.Lerp(c,settings.foamColor,.25f));}
                }
                b.previous=p;b.gap=gap;
            }
        }
        void SpawnDrop(Vector3 p,Vector3 velocity,float strength)
        {
            int seed=++eventId;
            drops[dropsCursor]=new Drop{live=true,position=p,velocity=velocity+transform.right*((Noise01(seed)-.5f)*1.3f),size=Mathf.Lerp(.075f,.14f,Noise01(seed+21))*Mathf.Lerp(.65f,1,strength)};
            dropsCursor=(dropsCursor+1)%MaxDrops;
        }
        void SpawnBurst(Vector3 contact,int side,float strength)
        {
            // Entry is inside the recess; the visible discharge escapes by that blade end.
            Vector3 local=transform.InverseTransformPoint(contact);
            local.x=hull.wheelAxle.x+side*(hull.wheelWidth*.5f+.06f);
            local.z=Mathf.Min(local.z,hull.wheelAxle.z-.18f*hull.wheelRadius);
            var p=transform.TransformPoint(local);p.y=contact.y;
            int seed=++eventId;
            bursts[burstsCursor]=new Burst{live=true,position=p,forward=(transform.right*side-transform.forward*.50f).normalized,variant=seed%4,strength=strength*Mathf.Lerp(.8f,1.2f,Noise01(seed))};
            burstsCursor=(burstsCursor+1)%MaxBursts;
        }
        void UpdateDrops(float dt)
        {
            ActiveDrops=0;for(int i=0;i<MaxDrops;i++){
                ref var d=ref drops[i];if(!d.live)continue;d.age+=dt;d.velocity+=Physics.gravity*dt;d.position+=d.velocity*dt;
                if(d.age>1.25f||(dropQueries[i].sampledFrame>0&&d.position.y<Height(dropQueries[i])+.02f)){d.live=false;continue;}
                ActiveDrops++;float r=d.size*Mathf.Clamp01((1.25f-d.age)/.20f);
                volume.ShapeAt(glob,d.position,Quaternion.FromToRotation(Vector3.up,d.velocity),new Vector3(r,r*1.6f,r),Color.Lerp(settings.waterColor,settings.foamColor,.35f));
            }
        }
        void UpdateBursts(float dt)
        {
            ActiveBursts=0;for(int i=0;i<MaxBursts;i++){
                ref var b=ref bursts[i];if(!b.live)continue;b.age+=dt;if(b.age>.46f){b.live=false;continue;}
                ActiveBursts++;float pulse=Mathf.Pow(Mathf.Sin(b.age/.46f*Mathf.PI),.7f)*b.strength;
                var p=b.position;if(burstQueries[i].sampledFrame>0)p=OnWater(burstQueries[i],p,-.05f);
                volume.ShapeAt(jets[b.variant],p,Quaternion.LookRotation(b.forward,Vector3.up),new Vector3(1.1f,1.2f,1.1f)*pulse,new Color(1,1,1,1),true);
            }
        }
        void UpdateWake()
        {
            Vector3 origin=direction>0?wheel.position-transform.forward*(hull.wheelRadius*.65f):transform.TransformPoint(new Vector3(0,0,hull.stations[hull.StationCount-1].z));origin.y=0;
            float gap=count>0?Vector3.Distance(origin,stamps[0].point):0;
            if(speed>.5f&&(count==0||(gap>Mathf.Max(.6f,speed*.16f)&&Time.time-stamps[0].time>.13f))){
                for(int i=Mathf.Min(count,WakeRows-1);i>0;i--)stamps[i]=stamps[i-1];travel+=gap;
                var right=transform.right;right.y=0;right.Normalize();stamps[0]=new Stamp{point=origin,right=right,time=Time.time,speed=speed,distance=travel,halfWidth=Mathf.Max(hull.wheelWidth*.58f,hull.beam*.29f),direction=(int)direction};count=Mathf.Min(count+1,WakeRows);
            }
            while(count>0&&Time.time-stamps[count-1].time>settings.wakeLifetime)count--;
            for(int side=0;side<2;side++){
                int previous=-1;for(int r=0;r<count;r++){
                    var q=wakeQueries[r*2+side];if(q.sampledFrame==0){previous=-1;continue;}
                    var s=stamps[r];float age=Time.time-s.time;float alpha=Mathf.Clamp01((settings.wakeLifetime-age)/2)*Mathf.Exp(-age*.15f)*Mathf.Clamp01(s.speed/2)*settings.intensity*Mathf.Max(0,JuiceTuning.wakeScale);
                    float width=(.48f+.045f*age)*(1+.2f*Mathf.Sin(s.distance*1.7f+side));
                    // Use the position actually sampled this step; old trail points never follow the hull.
                    Vector3 center=OnWater(q,q.position,settings.surfaceLift);Vector3 n=Normal(q),across=s.right;
                    float foam=Mathf.SmoothStep(.12f,1,Mathf.InverseLerp(-.65f,.35f,Mathf.Sin(s.distance*.75f+side*2)));
                    // Break up the crest in travelled metres, so gaps stay in the water rather than flickering in time.
                    float breakup=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.35f,.65f,Mathf.Sin(s.distance*1.35f+side*2.1f)+.3f*Mathf.Sin(s.distance*2.7f)));
                    alpha*=breakup*Mathf.Clamp01(age/.25f);
                    int start=AddSection(center,across,n,width,.025f,alpha,foam);
                    if(previous>=0)Join(previous,start);previous=start;
                }
            }
        }
        int AddSection(Vector3 p,Vector3 across,Vector3 normal,float width,float height,float alpha,float foam)
        {
            Color blue=settings.waterColor;blue.a=alpha*.66f;Color crest=Color.Lerp(settings.waterColor,settings.foamColor,foam);crest.a=alpha;
            int start=surface.Vertex(p-across*width*.5f,normal,new Color(blue.r,blue.g,blue.b,0));
            surface.Vertex(p-across*width*.13f+normal*height,normal,blue);
            surface.Vertex(p+across*width*.27f+normal*height*.8f,normal,crest);
            surface.Vertex(p+across*width*.5f,normal,new Color(crest.r,crest.g,crest.b,0));return start;
        }
        void Join(int a,int b){for(int j=0;j<3;j++)surface.Quad(a+j,b+j,b+j+1,a+j+1);}
        void DrawBow()
        {
            float strength=Mathf.Clamp01((speed-.3f)/3)*settings.intensity*Mathf.Max(0,JuiceTuning.sprayScale);
            if(strength<.01f)return;
            for(int side=0;side<2;side++){
                int previous=-1;for(int r=0;r<BowRows;r++){
                    var q=bowQueries[r*2+side];if(q.sampledFrame==0){previous=-1;continue;}
                    Vector3 p=OnWater(q,q.position,settings.surfaceLift),local=transform.InverseTransformPoint(p);float keel,deck;
                    HalfBreadth(hull,local.z,local.y,out keel,out deck);
                    if(local.y<keel-.06f||local.y>deck+.18f){previous=-1;continue;}
                    float u=r/(float)(BowRows-1),taper=Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*Mathf.PI)),.4f);
                    int start=AddSection(p,transform.right*(side==0?-1:1),Normal(q),settings.bowWidth*taper,settings.bowHeight*taper*strength,strength*taper,1);
                    if(previous>=0)Join(previous,start);previous=start;
                }
            }
        }
        void DrawChurn()
        {
            float energy=Mathf.Clamp01(Mathf.Abs(visualRate)*hull.wheelRadius/3)*settings.intensity;if(energy<.03f)return;
            for(int side=0;side<2;side++){
                var q=bladeQueries[side];Vector3 axle=wheel.position;
                if(Height(q)<axle.y-hull.wheelRadius||Height(q)>axle.y+hull.wheelRadius)return;
                Vector3 p=axle-transform.forward*hull.wheelRadius*.55f+transform.right*((side==0?-1:1)*hull.wheelWidth*.48f);p=OnWater(q,p,settings.surfaceLift);
                float s=energy*(.85f+.15f*Mathf.Sin(Time.time*7+side));
                volume.ShapeAt(glob,p,Quaternion.LookRotation(transform.forward),new Vector3(.30f,.035f,.65f)*s,Color.Lerp(settings.waterColor,settings.foamColor,.7f));
            }
        }
        void OnDisable(){ReleaseQueries();SuppressLegacy(false);ResetHistory();if(surface!=null&&surface.root)surface.root.SetActive(false);if(volume!=null&&volume.root)volume.root.SetActive(false);}
        void OnDestroy(){ReleaseQueries();SuppressLegacy(false);surface?.Dispose();volume?.Dispose();if(feeder)feeder.Owner=null;}
    }
}
