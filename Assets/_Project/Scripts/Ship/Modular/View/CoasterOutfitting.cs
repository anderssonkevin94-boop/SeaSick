using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Conditional joints and fittings, built in the same metre frame as the exported kit.
    public static class CoasterOutfitting
    {
        static Material paint, glass;
        public static Material Paint
        {
            get { if(paint==null) paint=Resources.Load<Material>("ShipModules/FCoasterPaint") ?? new Material(Shader.Find("SeaSick/Coaster Paint")){name="Coaster warm carved timber"}; return paint; }
        }
        public static Material Glass
        {
            get { if(glass==null) {glass=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="Coaster lantern glow"};glass.SetColor("_BaseColor",new Color(1.8f,.85f,.35f,1));}return glass; }
        }
        static readonly Color Wood=new Color(.64f,.35f,.12f), Navy=new Color(.025f,.07f,.095f), Iron=new Color(.045f,.055f,.065f);
        public static void Build(ModularShipView view,AssemblyResult asm)
        {
            if(!CoasterFamily.Is(asm.Find("stern")?.moduleId))return;
            var hulls=asm.placed.FindAll(p=>ModuleKind.IsHull(p.kind));
            for(int i=0;i<hulls.Count;i++)
            {
                var p=hulls[i];Transform host=null;
                foreach(Transform t in view.transform)if(t.name.StartsWith(p.instanceKey+" (")){host=t;break;}
                if(host==null)continue;
                bool raised=CoasterFamily.Raised(p.moduleId), stern=p.kind==ModuleKind.Stern,bow=p.kind==ModuleKind.Bow;
                float length=stern?4.9f:bow?3.55f:3f,top=stern?(raised?3.68f:1.48f):3.08f;
                foreach(var r in host.GetComponentsInChildren<MeshRenderer>())r.sharedMaterial=r.name.Contains("_Glass")?Glass:Paint;
                var fittings=new GameObject("Connections");fittings.transform.SetParent(host,false);
                if(stern)
                {
                    bool nextRaised=i+1<hulls.Count&&CoasterFamily.Raised(hulls[i+1].moduleId);
                    if(raised&&nextRaised)
                    {
                        Ramp(fittings.transform,3.60f,4.90f,3.68f,3.08f,1.77f,-1.29f);
                        Ramp(fittings.transform,3.60f,4.90f,3.68f,3.08f,1.77f,1.29f);
                        Ladder(fittings.transform,4.90f,3.08f,-1,.88f);
                        foreach(int side in new[]{-1,1})
                        {
                            Panel(fittings.transform,side,3.6f,4.90f,3.68f,3.08f);
                            Beam(fittings.transform,new Vector3(side*2.32f,4.23f,3.6f),new Vector3(side*2.32f,3.63f,4.90f),.39f,.245f,Wood);
                        }
                    }
                    else End(fittings.transform,3.60f,top,1,true);
                    // Access to the lower gun room rises only 60 cm, independent of helm height.
                    if(raised)
                    {
                        Ramp(fittings.transform,3.62f,4.62f,1.48f,.88f,1.0f,.95f);
                        // Close the former machinery cutout ahead of the wheel well.
                        Box(fittings.transform,"Gunroom Floor",new Vector3(0,1.425f,2.785f),new Vector3(4.35f,.11f,1.67f),Wood);
                    }
                }
                else if(raised)
                {
                    bool aft=i>0&&CoasterFamily.Raised(hulls[i-1].moduleId);
                    bool fwd=!bow&&i+1<hulls.Count&&CoasterFamily.Raised(hulls[i+1].moduleId);
                    if(!aft) {End(fittings.transform,0,top,-1,true);Sweep(fittings.transform,0,top,-1);}
                    if(!bow&&!fwd) {End(fittings.transform,length,top,1,true);Sweep(fittings.transform,length,top,1);}
                    if(bow) Box(fittings.transform,"Foredeck riser",new Vector3(0,.94f,.90f),new Vector3(4.2f,.12f,.10f),Wood);
                }
                // Port lids are exported closed, about a known hinge. Occupancy comes from the assembly.
                foreach(var r in host.GetComponentsInChildren<MeshRenderer>())
                {
                    if(!r.name.StartsWith("Hinged_Port_Cover"))continue;
                    int side=r.name.EndsWith("Starboard")?-1:1;
                    var slot=asm.slots.Find(s=>s.qualifiedId==p.instanceKey+"/Gun_0_"+side);
                    if(slot==null||string.IsNullOrEmpty(slot.occupiedBy))continue;
                    float gunX=stern?2.85f:bow?1.05f:1.5f;
                    float floor=stern?1.48f:bow?1.055f:.88f;
                    var pivot=new GameObject("Open port hinge").transform;pivot.SetParent(host,false);pivot.localPosition=new Vector3(-side*2.465f,floor+(stern?1.115f:1.065f),gunX);
                    r.transform.SetParent(pivot,true);pivot.localRotation=Quaternion.Euler(0,0,-side*110f);
                }
                // The bow lantern hangs off a beam ahead of the stem, under the harpoon's line (before the light loop: its Glass gets the light).
                if(bow&&!raised)BowLantern(host);
                // Three lanterns maximum on the basic ship; shadowless warm pools are inexpensive on phone.
                // A hidden (replaced) lantern gets no light; a swinging one carries its light with it.
                foreach(var r in host.GetComponentsInChildren<MeshRenderer>())
                    if(r.enabled&&r.name.Contains("Lantern")&&r.name.Contains("_Glass"))
                    {var swing=r.GetComponentInParent<LanternSwing>();var light=new GameObject("Warm lantern light").AddComponent<Light>();light.transform.SetParent(swing!=null?swing.transform:host,false);light.transform.position=r.bounds.center;light.type=LightType.Point;light.color=new Color(1,.55f,.23f);light.intensity=1.6f;light.range=4;light.shadows=LightShadows.None;light.gameObject.AddComponent<CoasterLantern>();}
                if(stern && raised && !(i+1<hulls.Count && CoasterFamily.Raised(hulls[i+1].moduleId))) CoasterPbrTrial.Apply(host);
                CoasterRenderBatch.Build(host);
            }
        }
        /// **Kevin 2026-10-04: the bow lantern on a little block** low on the stem,
        /// hanging just under the harpoon's line of fire across its +/-45 deg
        /// arc (art-staging/harpoon-v1: build.py `lantern`, check.py ->
        /// lantern-verification.json). Hides the hull kit's own bow lantern
        /// (its post stood in the line dead ahead) and hangs
        /// `Resources/Harpoon/BowLantern` in the harpoon mount's frame:
        /// bow-local (0, 1.055, 3.15) m, identity. Not imported yet: the kit
        /// lantern stays, so the ship never loses it. Low bow only: a raised
        /// bow keeps its kit lantern until the harpoon has a raised-bow spot.
        static GameObject bowLantern;static bool bowLanternLoaded;
        static readonly Vector3 BowLanternAt=new Vector3(0,1.055f,3.15f);
        /// The kit lantern's frame on BowLow (kit.json): lowest y, forward-most z.
        /// Anything else is a bow this beam was not fitted to.
        static readonly Vector2 KitBowLanternFoot=new Vector2(2.532f,4.854f);
        static void BowLantern(Transform host)
        {
            if(!bowLanternLoaded){bowLantern=Resources.Load<GameObject>("Harpoon/BowLantern");bowLanternLoaded=true;}
            if(bowLantern==null)return;
            var old=new List<MeshRenderer>();MeshFilter frame=null;
            foreach(var r in host.GetComponentsInChildren<MeshRenderer>())
                if(r.name.StartsWith("Lantern_Bow_")){old.Add(r);if(r.name.StartsWith("Lantern_Bow_Frame"))frame=r.GetComponent<MeshFilter>();}
            if(frame==null||frame.sharedMesh==null)return;
            // Host-local metres, vertex-exact through the mesh bounds' corners.
            var mb=frame.sharedMesh.bounds;float low=float.MaxValue,front=float.MinValue;
            for(int c=0;c<8;c++)
            {
                var p=host.InverseTransformPoint(frame.transform.TransformPoint(mb.center+Vector3.Scale(mb.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1))));
                low=Mathf.Min(low,p.y);front=Mathf.Max(front,p.z);
            }
            if(Mathf.Abs(low-KitBowLanternFoot.x)>.05f||Mathf.Abs(front-KitBowLanternFoot.y)>.05f)return;
            foreach(var r in old)r.enabled=false;
            var go=Object.Instantiate(bowLantern,host,false);go.name="BowLantern";
            go.transform.localPosition=BowLanternAt;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
            // Painted like the lantern it replaces: hull paint, glowing glass (every slot).
            foreach(var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var m=r.name.Contains("_Glass")?Glass:Paint;var a=r.sharedMaterials;
                for(int k=0;k<a.Length;k++)a[k]=m;
                r.sharedMaterials=a;
            }
            foreach(var t in go.GetComponentsInChildren<Transform>())
                if(t.name=="LanternBow_Pivot"){if(t.GetComponent<LanternSwing>()==null)t.gameObject.AddComponent<LanternSwing>();break;}
        }
        static void End(Transform parent,float z,float top,int outward,bool ladder)
        {
            float bottom=.88f;
            // Off-centre, 0.9 m wide doorway: a real entrance next to the ladder.
            if(top-bottom>1.8f)
            {
                float doorX=.95f,half=.45f;
                Box(parent,"Exposed navy wall port",new Vector3((-2.23f+doorX-half)/2,(bottom+top-.12f)/2,z),new Vector3(doorX-half+2.23f,top-.12f-bottom,.12f),Navy);
                Box(parent,"Exposed navy wall starboard",new Vector3((doorX+half+2.23f)/2,(bottom+top-.12f)/2,z),new Vector3(2.23f-doorX-half,top-.12f-bottom,.12f),Navy);
                float lintelBottom=(top>3.5f?1.48f:bottom)+1.85f;
                if(top-.12f>lintelBottom)Box(parent,"Door lintel panel",new Vector3(doorX,(lintelBottom+top-.12f)/2,z),new Vector3(.9f,top-.12f-lintelBottom,.12f),Navy);
                foreach(float x in new[]{-2.23f,doorX-half,doorX+half,2.23f})Box(parent,"End timber",new Vector3(x,(top+bottom)/2,z),new Vector3(.16f,top-bottom,.18f),Wood);
            }
            foreach(int side in new[]{-1,1})
            {Box(parent,"Landing rail",new Vector3(side*1.35f,top+.60f,z),new Vector3(1.85f,.14f,.14f),Wood);foreach(float x in new[]{side*.425f,side*2.25f})Box(parent,"Landing post",new Vector3(x,top+.30f,z),new Vector3(.13f,.60f,.13f),Wood);}
            if(!ladder)return;
            Ladder(parent,z,top,outward,bottom);
        }
        static void Ladder(Transform parent,float z,float top,int outward,float bottom)
        {
            float lz=z+outward*.30f;
            foreach(float x in new[]{-.325f,.325f})Box(parent,"Ladder rail",new Vector3(x,(bottom+top+.4f)/2,lz),new Vector3(.09f,top+.4f-bottom,.10f),Wood);
            int steps=Mathf.CeilToInt((top-bottom)/.24f);
            for(int j=0;j<steps;j++)Box(parent,"Ladder rung",new Vector3(0,bottom+.15f+j*(top-bottom-.15f)/steps,lz),new Vector3(.65f,.07f,.08f),Iron);
            var lower=new GameObject("ClimbBottom").transform;lower.SetParent(parent,false);lower.localPosition=new Vector3(0,bottom,z+outward*.50f);
            var upper=new GameObject("ClimbTop").transform;upper.SetParent(parent,false);upper.localPosition=new Vector3(0,top,z-outward*.25f);
        }
        static void Ramp(Transform parent,float z0,float z1,float y0,float y1,float width=4.35f,float x=0)
        {
            Vector3 a=new Vector3(x,y0,z0),b=new Vector3(x,y1,z1);var go=Box(parent,"Joined deck ramp",(a+b)/2-Vector3.up*.07f,new Vector3(width,.14f,(b-a).magnitude+.04f),Wood);go.transform.localRotation=Quaternion.Euler(-Mathf.Atan2(y1-y0,z1-z0)*Mathf.Rad2Deg,0,0);
        }
        static void Sweep(Transform parent,float z,float top,int direction)
        {
            // Continuous curved cap into a lower neighbour, using broad faceted segments.
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 prev=new Vector3(side*2.32f,top+.55f,z);
                for(int j=1;j<=10;j++)
                {float t=j/10f,s=t*t*(3-2*t);var next=new Vector3(side*2.32f,Mathf.Lerp(top+.55f,1.43f,s),z+direction*.85f*t);Panel(parent,side,prev.z,next.z,prev.y-.55f,next.y-.55f);Beam(parent,prev,next,.30f,.22f,Wood);prev=next;}
            }
        }
        static void Panel(Transform parent,int side,float z0,float z1,float y0,float y1)
        {
            if(z1<z0){(z0,z1)=(z1,z0);(y0,y1)=(y1,y0);}
            // Navy below the deck beam, exposed warm timber above it.
            var v=new List<Vector3>();var c=new List<Color>();var t=new List<int>();
            void Prism(float low0,float low1,float high0,float high1,Color color)
            {
                float x=side*2.30f;Vector3[] p={new Vector3(x-.075f,low0,z0),new Vector3(x+.075f,low0,z0),new Vector3(x+.075f,high0,z0),new Vector3(x-.075f,high0,z0),new Vector3(x-.075f,low1,z1),new Vector3(x+.075f,low1,z1),new Vector3(x+.075f,high1,z1),new Vector3(x-.075f,high1,z1)};
                int[] faces={0,3,2,1,4,5,6,7,0,4,7,3,1,2,6,5,3,7,6,2,0,1,5,4};
                for(int f=0;f<faces.Length;f+=4){int k=v.Count;for(int j=0;j<4;j++){v.Add(p[faces[f+j]]);c.Add(color);}t.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});}
            }
            Prism(.88f,.88f,Mathf.Max(.90f,y0-.12f),Mathf.Max(.90f,y1-.12f),Navy);
            Prism(Mathf.Max(.90f,y0-.12f),Mathf.Max(.90f,y1-.12f),y0+.49f,y1+.49f,Wood);
            var mesh=new Mesh{name="Joined side panel"};mesh.SetVertices(v);mesh.SetColors(c);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject("Joined side panel");go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=Paint;
            go.AddComponent<CoasterOwnedMesh>().mesh=mesh;
        }
        static void Beam(Transform p,Vector3 a,Vector3 b,float w,float h,Color c)
        {var go=Box(p,"Swept timber cap",(a+b)/2,new Vector3(w,h,(b-a).magnitude+.015f),c);go.transform.localRotation=Quaternion.LookRotation(b-a,Vector3.up);}
        static Mesh cube;
        static readonly Dictionary<Color,Mesh> tinted=new Dictionary<Color,Mesh>();
        public static GameObject Box(Transform p,string name,Vector3 at,Vector3 size,Color color)
        {
            var go=new GameObject(name);go.transform.SetParent(p,false);go.transform.localPosition=at;go.transform.localScale=size;
            if(cube==null){var temp=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(temp);}
            // Cached Unity meshes can be destroyed between Play sessions or by an unused-asset unload.
            if(!tinted.TryGetValue(color,out var mesh)||mesh==null){mesh=Object.Instantiate(cube);var c=new Color[mesh.vertexCount];for(int i=0;i<c.Length;i++)c[i]=color;mesh.colors=c;tinted[color]=mesh;}
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=Paint;
            return go;
        }
    }
}
