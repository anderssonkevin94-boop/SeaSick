using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Keeps rigid scenery feet in the rendered terrain, including streamed LOD changes.
    [DefaultExecutionOrder(500)]
    public sealed class NatureGrounding : MonoBehaviour
    {
        public sealed class Anchor
        {
            public int cell, start0, count0, start1, count1;
            public Vector3[] feet;
            public Transform instance;
            public float applied;
            public float referenceY;
        }
        sealed class Surface
        {
            public int revision, n;
            public float spacing;
            public Vector3[] vertices;
        }
        readonly Dictionary<int2, Surface> surfaces = new Dictionary<int2, Surface>();
        readonly List<Anchor> anchors = new List<Anchor>();
        readonly HashSet<int> dirty = new HashSet<int>();
        IReadOnlyList<SceneryWood.Cell> cells;
        TerrainStreamer streamer;
        int lastBuilt=-1;
        float nextCheck;
        public int AnchorCount => anchors.Count;
        public float LargestCorrection { get; private set; }

        public static Vector3[] Feet(IList<Vector3> vertices, int start, int count)
        {
            float bottom=float.MaxValue, top=float.MinValue;
            for(int i=start;i<start+count;i++){bottom=Mathf.Min(bottom,vertices[i].y);top=Mathf.Max(top,vertices[i].y);}
            var feet=new List<Vector3>();
            float band=Mathf.Min(.18f,Mathf.Max(.035f,(top-bottom)*.035f));
            for(int i=start;i<start+count;i++)
                if(vertices[i].y<=bottom+band && !feet.Contains(vertices[i])) feet.Add(vertices[i]);
            return feet.ToArray();
        }
        public void Configure(IReadOnlyList<SceneryWood.Cell> meshCells,List<Anchor> contacts)
        {
            cells=meshCells;anchors.AddRange(contacts);
            streamer=FindFirstObjectByType<TerrainStreamer>();
        }
        public void AddInstance(Transform root)
        {
            var points=new List<Vector3>();
            foreach(var f in root.GetComponentsInChildren<MeshFilter>())
            {
                var r=f.GetComponent<MeshRenderer>();
                if(r==null || r.forceRenderingOff || f.sharedMesh==null) continue;
                foreach(var v in f.sharedMesh.vertices)points.Add(f.transform.TransformPoint(v));
            }
            if(points.Count>0)anchors.Add(new Anchor {instance=root,feet=Feet(points,0,points.Count),referenceY=root.position.y});
        }
        public bool SurfaceHeight(float x,float z,out float y)
        {
            y=0;if(streamer==null || streamer.settings==null)return false;
            float size=streamer.settings.chunkSize;
            var key=new int2(Mathf.FloorToInt(x/size),Mathf.FloorToInt(z/size));
            int revision=streamer.MeshRevisionAt(key);
            if(!surfaces.TryGetValue(key,out var s) || s.revision!=revision)
            {
                var mesh=streamer.MeshAt(key);
                if(mesh==null)return false;
                int lod=streamer.LodAt(key);
                s=new Surface {revision=revision,n=(streamer.settings.chunkResolution-1)/lod+1,
                    spacing=size/(streamer.settings.chunkResolution-1)*lod,vertices=mesh.vertices};
                surfaces[key]=s;
            }
            float u=(x-key.x*size)/s.spacing,v=(z-key.y*size)/s.spacing;
            int ix=Mathf.Clamp(Mathf.FloorToInt(u),0,s.n-2),iz=Mathf.Clamp(Mathf.FloorToInt(v),0,s.n-2);
            float fx=u-ix,fz=v-iz;
            int a=iz*s.n+ix,b=a+1,c=a+s.n,d=c+1;
            y=fx+fz<=1 ? s.vertices[a].y*(1-fx-fz)+s.vertices[b].y*fx+s.vertices[c].y*fz
                : s.vertices[d].y*(fx+fz-1)+s.vertices[c].y*(1-fx)+s.vertices[b].y*(1-fz);
            return true;
        }
        void LateUpdate()
        {
            if(streamer==null)streamer=FindFirstObjectByType<TerrainStreamer>();
            if(streamer==null)return;
            if(lastBuilt==streamer.TotalBuilt && Time.unscaledTime<nextCheck)return;
            lastBuilt=streamer.TotalBuilt;nextCheck=Time.unscaledTime+.25f;
            Refresh();
        }
        public void Refresh()
        {
            if(streamer==null)streamer=FindFirstObjectByType<TerrainStreamer>();
            dirty.Clear();
            foreach(var a in anchors)
            {
                if(a.instance==null && a.count0==0)continue;
                float correction=float.MaxValue;bool ready=true;
                foreach(var foot in a.feet)
                {
                    if(!SurfaceHeight(foot.x,foot.z,out float y)){ready=false;break;}
                    correction=Mathf.Min(correction,y-foot.y-.045f);
                }
                if(!ready || a.feet.Length==0)continue;
                float delta=correction-a.applied;
                if(Mathf.Abs(delta)<.002f)continue;
                if(a.instance!=null)a.instance.position+=Vector3.up*delta;
                else
                {
                    var cell=cells[a.cell];
                    // Cleared/felled ranges stay collapsed. On regrowth the saved
                    // standing mesh returns at its previous offset, then catches up here.
                    if(Collapsed(cell.v0,a.start0,a.count0))continue;
                    Shift(cell.v0,a.start0,a.count0,delta);Shift(cell.v1,a.start1,a.count1,delta);
                    dirty.Add(a.cell);
                }
                a.applied=correction;LargestCorrection=Mathf.Max(LargestCorrection,Mathf.Abs(correction));
            }
            foreach(int i in dirty)
            {
                var c=cells[i];
                if(c.lod0!=null){c.lod0.SetVertices(c.v0);c.lod0.RecalculateBounds();}
                if(c.lod1!=null){c.lod1.SetVertices(c.v1);c.lod1.RecalculateBounds();}
            }
        }
        static bool Collapsed(Vector3[] v,int start,int count)
        {
            if(v==null || count<2)return true;
            for(int i=start+1;i<start+count;i++)if((v[i]-v[start]).sqrMagnitude>1e-8f)return false;
            return true;
        }
        static void Shift(Vector3[] v,int start,int count,float dy)
        {if(v!=null)for(int i=start;i<start+count;i++)v[i].y+=dy;}
        public string Audit()
        {
            Refresh();int checkedFeet=0,missing=0;float gap=float.MinValue;
            foreach(var a in anchors)
            {
                if(a.instance==null && (a.count0==0 || Collapsed(cells[a.cell].v0,a.start0,a.count0)))continue;
                float actual=a.instance!=null ? a.instance.position.y-a.referenceY : cells[a.cell].v0[a.start0].y-a.referenceY;
                foreach(var foot in a.feet)
                {
                if(!SurfaceHeight(foot.x,foot.z,out float y)){missing++;continue;}
                checkedFeet++;gap=Mathf.Max(gap,foot.y+actual-y);
                }
            }
            return $"{anchors.Count} anchors; {checkedFeet} feet checked; {missing} not streamed; maximum foot gap {gap:F4} m; largest correction {LargestCorrection:F3} m";
        }
    }
}
