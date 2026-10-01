using System.Collections.Generic;
using UnityEngine;
namespace SeaSick.Ship.Modular
{
    /// Ship-local surface graph. Hull motion never requires a world NavMesh rebuild.
    public sealed class CoasterNavigation : MonoBehaviour
    {
        const float Step=.30f, Radius=.22f;
        struct Tri {public Vector3 a,b,c;}
        sealed class Node {public Vector3 p;public readonly List<int> edges=new List<int>();}
        sealed class Route {public Vector3 target;public List<int> nodes;public int at;public Vector3 last;}
        readonly List<Node> nodes=new List<Node>();readonly List<Tri> floors=new List<Tri>();
        readonly List<Bounds> obstacles=new List<Bounds>();readonly Dictionary<int,Route> routes=new Dictionary<int,Route>();
        Transform ship;public int NodeCount=>nodes.Count;public int LadderLinks{get;private set;}
        public void Clear(){routes.Clear();nodes.Clear();floors.Clear();obstacles.Clear();}
        /// `reserved`: ship-local boxes the walk graph must stay out of on top
        /// of the guns and walls -- the deck cargo sockets
        /// (`ShipCargoDisplay`, 2026-09-30), reserved whether or not a prop
        /// stands there, so the graph never changes as the hold fills.
        public void Build(Transform vessel,ShipyardPlan plan,Cannon[] guns,IList<Bounds> reserved=null)
        {
            Clear();LadderLinks=0;ship=vessel;var cfg=plan.config;float offset=plan.viewOffset.z;
            if(reserved!=null)obstacles.AddRange(reserved);
            foreach(var gun in guns)
            {
                var p=gun.transform.localPosition;
                obstacles.Add(new Bounds(p+new Vector3(0,.4f,0),new Vector3(1.15f,.8f,.90f)));
            }
            foreach(var mf in GetComponentsInChildren<MeshFilter>())
            {
                string name=mf.name;
                if(name.StartsWith("Exposed navy wall")||name=="Door lintel panel")
                {var r=mf.GetComponent<Renderer>();if(r!=null){var b=mf.sharedMesh.bounds;var matrix=ship.worldToLocalMatrix*mf.transform.localToWorldMatrix;obstacles.Add(new Bounds(matrix.MultiplyPoint3x4(b.center),Vector3.Scale(b.size,mf.transform.lossyScale)));}continue;}
                bool floor=name.Contains("Floor")||name.StartsWith("Main_Deck")||name=="Joined deck ramp";
                if(!floor)continue;
                var m=mf.sharedMesh;if(m==null||!m.isReadable)continue;
                var v=m.vertices;var t=m.triangles;var mx=ship.worldToLocalMatrix*mf.transform.localToWorldMatrix;
                for(int i=0;i<t.Length;i+=3)
                {var a=mx.MultiplyPoint3x4(v[t[i]]);var b=mx.MultiplyPoint3x4(v[t[i+1]]);var c=mx.MultiplyPoint3x4(v[t[i+2]]);if(Vector3.Cross(b-a,c-a).normalized.y>.55f)floors.Add(new Tri{a=a,b=b,c=c});}
            }
            var cells=new Dictionary<Vector2Int,List<int>>();
            int last=Mathf.CeilToInt((9.8f+6*cfg.middleIds.Count+9.4f)*.5f/Step);
            for(int iz=0;iz<=last;iz++)for(int ix=-7;ix<=7;ix++)
            {
                float x=ix*Step,z=offset+iz*Step;var heights=new List<float>();
                foreach(var tri in floors)if(Height(tri,x,z,out float y)&&!heights.Exists(h=>Mathf.Abs(h-y)<.09f))heights.Add(y);
                foreach(float y in heights)
                {
                    if(heights.Exists(h=>h>y+.10f&&h<y+1.55f))continue;
                    // The central wheel housing is not a walking surface.
                    if(CoasterFamily.Raised(cfg.sternId)&&z<offset+1.95f&&y<2.6f&&Mathf.Abs(x)<1.65f)continue;
                    var p=new Vector3(x,y,z);if(Blocked(p))continue;
                    var key=new Vector2Int(ix,iz);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<int>();list.Add(nodes.Count);nodes.Add(new Node{p=p});
                }
            }
            foreach(var pair in cells)foreach(int a in pair.Value)
                foreach(var d in new[]{Vector2Int.right,Vector2Int.up})
                    if(cells.TryGetValue(pair.Key+d,out var next))foreach(int b in next)
                        if(Mathf.Abs(nodes[a].p.y-nodes[b].p.y)<.32f&&!Blocked((nodes[a].p+nodes[b].p)/2))Link(a,b);
            foreach(var t in GetComponentsInChildren<Transform>())if(t.name=="ClimbBottom")
            {
                var top=t.parent.Find("ClimbTop");if(top==null)continue;
                int a=Nearest(ship.InverseTransformPoint(t.position)),b=Nearest(ship.InverseTransformPoint(top.position));
                if(a>=0&&b>=0&&a!=b){Link(a,b);LadderLinks++;}
            }
            // Physical walk surfaces / walls use compound box colliders, never a concave mesh on a moving rigidbody.
            foreach(var mf in GetComponentsInChildren<MeshFilter>())
                if(mf.name=="Joined deck ramp"||mf.name.StartsWith("Exposed navy wall"))
                {var box=mf.GetComponent<BoxCollider>();if(box==null)box=mf.gameObject.AddComponent<BoxCollider>();box.center=mf.sharedMesh.bounds.center;box.size=mf.sharedMesh.bounds.size;}
            // Land is not a ramp for these either (same exclusion as the hull box; HullIntegrity's shore wall grounds her).
            HullIntegrity.ExcludeLand(ship.gameObject);
        }
        /// The guns, walls and reserved boxes the walk graph avoids, ship-local.
        public IReadOnlyList<Bounds> Obstacles=>obstacles;
        /// **Deck height under (x, z), ship-local** (2026-09-30, for the cargo
        /// sockets): the walkable floor nearest `nearY` within `within`
        /// metres, from the same floor triangles the walk graph is built on.
        public bool FloorY(float x,float z,float nearY,float within,out float y)
        {
            y=0;float best=within;bool found=false;
            foreach(var tri in floors)
                if(Height(tri,x,z,out float h)&&Mathf.Abs(h-nearY)<=best){best=Mathf.Abs(h-nearY);y=h;found=true;}
            return found;
        }
        void Link(int a,int b){nodes[a].edges.Add(b);nodes[b].edges.Add(a);}
        bool Blocked(Vector3 p)
        {foreach(var ob in obstacles){var b=ob;b.Expand(new Vector3(Radius*2,0,Radius*2));if(p.y+1.55f>b.min.y&&p.y+.10f<b.max.y&&p.x>b.min.x&&p.x<b.max.x&&p.z>b.min.z&&p.z<b.max.z)return true;}return false;}
        static bool Height(Tri t,float x,float z,out float y)
        {
            y=0;float den=(t.b.z-t.c.z)*(t.a.x-t.c.x)+(t.c.x-t.b.x)*(t.a.z-t.c.z);if(Mathf.Abs(den)<1e-7f)return false;
            float a=((t.b.z-t.c.z)*(x-t.c.x)+(t.c.x-t.b.x)*(z-t.c.z))/den,b=((t.c.z-t.a.z)*(x-t.c.x)+(t.a.x-t.c.x)*(z-t.c.z))/den,c=1-a-b;
            // Decorative plank seams are not holes in the walking deck. Use a
            // world-space 2.5 cm tolerance, not a scale-dependent barycentric one.
            float area=Mathf.Abs(den);
            float Edge(Vector3 u,Vector3 v)=>Mathf.Sqrt((u.x-v.x)*(u.x-v.x)+(u.z-v.z)*(u.z-v.z));
            if(a<0&&-a*area> .025f*Edge(t.b,t.c))return false;
            if(b<0&&-b*area> .025f*Edge(t.c,t.a))return false;
            if(c<0&&-c*area> .025f*Edge(t.a,t.b))return false;
            y=a*t.a.y+b*t.b.y+c*t.c.y;return true;
        }
        int Nearest(Vector3 p)
        {int best=-1;float dist=float.MaxValue;for(int i=0;i<nodes.Count;i++){var d=nodes[i].p-p;float v=d.x*d.x+d.z*d.z+9*d.y*d.y;if(v<dist){dist=v;best=i;}}return best;}
        public Vector3 ClosestWalkable(Vector3 p) { int i=Nearest(p);return i<0?p:nodes[i].p; }
        public Vector3 SpareStation(List<Vector3> occupied,float forwardOf)
        {
            foreach(var node in nodes)
            {
                var p=node.p;if(p.z<forwardOf||p.y>1.6f||Mathf.Abs(p.x)>1.05f)continue;
                if(occupied.Exists(q=>(q-p).sqrMagnitude<.36f))continue;
                occupied.Add(p);return p;
            }
            return ClosestWalkable(new Vector3(0,.88f,forwardOf+.5f));
        }
        public bool HasRoute(Vector3 from,Vector3 to)
        {
            bool Close(Vector3 p) {var d=ClosestWalkable(p)-p;return Mathf.Abs(d.y)<.25f&&d.x*d.x+d.z*d.z<.25f;}
            return Close(from)&&Close(to)&&Find(from,to)!=null;
        }
        List<int> Find(Vector3 from,Vector3 to)
        {
            int start=Nearest(from),end=Nearest(to);if(start<0||end<0)return null;
            var prev=new int[nodes.Count];for(int i=0;i<prev.Length;i++)prev[i]=-1;
            var q=new Queue<int>();q.Enqueue(start);prev[start]=start;
            while(q.Count>0){int a=q.Dequeue();if(a==end)break;foreach(int b in nodes[a].edges)if(prev[b]<0){prev[b]=a;q.Enqueue(b);}}
            if(prev[end]<0)return null;var path=new List<int>();for(int a=end;a!=start;a=prev[a])path.Add(a);path.Reverse();return path;
        }
        /// **Where `Move` would head this frame** (2026-10-01): the route's
        /// next node, or the target -- without moving him or touching the
        /// route, so the walker can turn his body to it first (`Stride`).
        public Vector3 Peek(Transform hand,Vector3 target)
        {
            var here=hand.localPosition;
            if(!routes.TryGetValue(hand.GetInstanceID(),out var r)||(r.target-target).sqrMagnitude>.02f||(r.last-here).sqrMagnitude>.0025f||r.nodes==null)
                return target;
            if(r.at>=r.nodes.Count)return target;
            return nodes[r.nodes[r.at]].p;
        }

        public bool Move(Transform hand,Vector3 target,float distance)
        {
            int id=hand.GetInstanceID();
            // **The cached route is only good for the walk it was made for**
            // (2026-10-01). It used to be keyed on the target alone, so a hand
            // sent to the same spot twice -- after being moved, re-assigned or
            // interrupted -- kept the old node list and `at` index and walked a
            // straight line from wherever he now stood, through props, cargo
            // sockets, rails and the funnel. `last` is where THIS method left
            // him; if he is anywhere else now, someone moved him and the route
            // is rebuilt from his real position. Repeated calls of one walk
            // (same walker, same target, still progressing) still reuse it,
            // so there is no per-frame pathfinding.
            var here=hand.localPosition;
            if(!routes.TryGetValue(id,out var r)||(r.target-target).sqrMagnitude>.02f||(r.last-here).sqrMagnitude>.0025f)
            {r=new Route{target=target,nodes=Find(here,target)};routes[id]=r;}
            r.last=here;
            if(r.nodes==null)return false;
            if(r.at>=r.nodes.Count)
            {
                var end=ClosestWalkable(target);
                if((end-target).sqrMagnitude<.12f&&!Blocked(target))end=target;
                hand.localPosition=Vector3.MoveTowards(hand.localPosition,end,distance);
                r.last=hand.localPosition;
                return (hand.localPosition-end).sqrMagnitude<.001f;
            }
            var next=nodes[r.nodes[r.at]].p;hand.localPosition=Vector3.MoveTowards(hand.localPosition,next,distance);
            if((hand.localPosition-next).sqrMagnitude<.001f)r.at++;
            r.last=hand.localPosition;
            return false;
        }
    }
}
