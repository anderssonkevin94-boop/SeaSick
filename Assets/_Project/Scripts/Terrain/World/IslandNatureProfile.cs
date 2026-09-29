using System;
using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Terrain
{
    /// Astra's nature kit and its placement rules (species by height/slope/
    /// grove, groups at trunk and rock bases, shore and cliff accents, the
    /// painted ground). Built for Island_2 and keyed to its centre; since
    /// 2026-09-29 it dresses EVERY island (`allIslands`), because Kevin wants
    /// "that rule / those assets on all islands". Resource placement and
    /// accounting stay owned by the existing scatter.
    [DefaultExecutionOrder(-1100)]
    public sealed class IslandNatureProfile : MonoBehaviour
    {
        [Tooltip("Dress every island with the kit. Off = only the island centred on islandCentre (the original Island_2 treatment).")]
        [SerializeField] bool allIslands = true;
        [SerializeField] Vector2 islandCentre = new Vector2(660, 81);
        [Tooltip("Half-width of the world square the ground index covers, metres from the origin. Must reach past the discovery radius plus the widest island.")]
        [SerializeField] float groundIndexHalfExtent = 4000f;
        [SerializeField] TextAsset meshLibrary;
        [SerializeField] Material natureMaterial;
        public Material Material => natureMaterial;
        public static IslandNatureProfile Active { get; private set; }
        readonly Dictionary<string, SceneryKit.Template> templates = new Dictionary<string, SceneryKit.Template>();
        readonly Dictionary<string, Mesh> resourceMeshes = new Dictionary<string, Mesh>();
        [Serializable] public class Library { public Entry[] entries; }
        [Serializable] public class Entry
        {
            public string name;
            public Vector3[] vertices, normals;
            public Color[] colors;
            public int[] triangles;
        }
        void OnEnable()
        {
            Active = this;
            templates.Clear(); Uses.Clear(); traits.Clear(); current=null;
            if (meshLibrary == null || natureMaterial == null) return;
            var data = JsonUtility.FromJson<Library>(meshLibrary.text);
            foreach (var e in data.entries)
            {
                if (e.vertices.Length != e.normals.Length || e.vertices.Length != e.colors.Length)
                    throw new InvalidOperationException("Invalid nature template " + e.name);
                var tp = new SceneryKit.Template { name=e.name,v=e.vertices,n=e.normals,t=e.triangles,c=new Color32[e.colors.Length] };
                for (int i=0;i<tp.v.Length;i++)
                {
                    tp.c[i]=e.colors[i];
                    tp.height=Mathf.Max(tp.height,tp.v[i].y);
                    tp.radius=Mathf.Max(tp.radius,new Vector2(tp.v[i].x,tp.v[i].z).magnitude);
                }
                ColourD(e.name,tp.c);
                templates.Add(e.name,tp);
            }
        }
        void OnDisable()
        {
            if (Active==this) { Active=null; Shader.SetGlobalFloat("_IslandNatureEnabled",0); }
            foreach(var mesh in resourceMeshes.Values) if(mesh!=null) Destroy(mesh);
            resourceMeshes.Clear();
        }
        /// Dev: template lookups per name since the last world build (≈ placed instances).
        public static readonly Dictionary<string,int> Uses=new Dictionary<string,int>();
        /// Dev A/B: set in play mode before a reload to build the island with the legacy kit.
        public static bool DisabledForCompare;
        public static IslandNatureProfile For(Vector3 centre)
        {
            var p=Active;
            return !DisabledForCompare && p!=null && p.isActiveAndEnabled && p.templates.Count>0
                && (p.allIslands || Vector2.Distance(p.islandCentre,new Vector2(centre.x,centre.z))<1f) ? p : null;
        }

        void LateUpdate() => NatureGroundAtlas.Flush();

        // --- palms: which islands are palm islands ---------------------------
        //
        // Kevin, 2026-09-29: "small island with mostly sand should have palm
        // trees". Not in Astra's notes (hers: palms in irregular coastal
        // groups, never uniformly round every coast, and `Tree` below does
        // that); this is the island-scale half. `Palmy` 0..1 is the larger of
        // "small AND mostly sand" and the old latitude rule (south = tropical,
        // `palmLatitude`), so the warm south stays palm country too.
        sealed class IslandTrait { public Vector2 centre; public float reach, palmy; }
        readonly List<IslandTrait> traits=new List<IslandTrait>();
        IslandTrait current;

        /// Sand line of the kit's own material (`_SandLine`, 4.3), plus the
        /// mesher's sand-to-grass blend: ground below this reads as beach.
        float SandTop => (natureMaterial!=null && natureMaterial.HasProperty("_SandLine") ? natureMaterial.GetFloat("_SandLine") : 4.3f)+1f;

        /// Called by the scenery bake once per island, before any tree is
        /// chosen. `tropical` is the bake's own latitude weight.
        public void BeginIsland(Vector3 centre,float meanR,float tropical,Func<float,float,float> height)
        {
            current=Trait(centre,meanR,height);
            current.palmy=Mathf.Max(current.palmy,tropical);
        }

        /// How strongly this island's low ground and beach go to palms, 0..1.
        public float Palmy => current!=null ? current.palmy : 0f;

        IslandTrait Trait(Vector3 centre,float meanR,Func<float,float,float> height)
        {
            var c=new Vector2(centre.x,centre.z);
            foreach(var t in traits) if((t.centre-c).sqrMagnitude<1f) return t;
            // Fraction of the island that is beach: sampled on a 6 m grid
            // inside its mean radius, against the sand line.
            int land=0,sand=0; float top=SandTop;
            for(float z=-meanR;z<=meanR;z+=6f)
            for(float x=-meanR;x<=meanR;x+=6f)
            {
                if(x*x+z*z>meanR*meanR) continue;
                float h=height(centre.x+x,centre.z+z);
                if(h<=0.3f) continue;
                land++; if(h<top) sand++;
            }
            float sandy=land>0 ? Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,.45f,(float)sand/land)) : 0f;
            float small=1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(55f,95f,meanR));
            var trait=new IslandTrait{centre=c,reach=meanR,palmy=sandy*small};
            traits.Add(trait); return trait;
        }

        /// The trait of whichever island `at` stands on, for callers that
        /// only have a position (resource dressing runs before the bake).
        IslandTrait TraitAt(Vector3 at)
        {
            if(current!=null && (current.centre-new Vector2(at.x,at.z)).magnitude<current.reach*2.2f) return current;
            Island best=null; float bd=float.MaxValue;
            foreach(var isle in Island.All)
            {
                if(isle==null) continue;
                float d=Island.FlatDistance(isle.transform.position,at)-isle.MaxRadius;
                if(d<bd){bd=d;best=isle;}
            }
            if(best==null || Island.TerrainHeight==null) return null;
            return Trait(best.transform.position,Mathf.Max(12f,best.MaxRadius*.75f),Island.TerrainHeight);
        }
        public SceneryKit.Template Get(string name)
        {
            if(!templates.TryGetValue(name,out var t)) return null;
            Uses.TryGetValue(name,out int n); Uses[name]=n+1;
            return t;
        }

        // Colour D "cream sandstone" (Kevin 2026-09-27): one warm sandstone for
        // every stone, warm yellow-green plants, brighter varied canopies. The
        // baked facet value is kept; only hue/saturation move.
        static void ColourD(string name,Color32[] c)
        {
            bool stone=name.StartsWith("Boulder_",StringComparison.Ordinal) || name.StartsWith("Cliff_",StringComparison.Ordinal)
                || name.StartsWith("Shore_WashedStones",StringComparison.Ordinal) || name.StartsWith("Pebble",StringComparison.Ordinal);
            float hue=92,sat=1.3f,val=1.08f;
            if(name.StartsWith("Forest_",StringComparison.Ordinal))
            {
                string sp=name.Substring(7); int cut=sp.IndexOf('_'); if(cut>=0) sp=sp.Substring(0,cut);
                switch(sp)
                {
                    case "Pine": hue=122;sat=1.05f;val=1.0f;break;
                    case "Coastal": hue=112;sat=1.1f;val=1.04f;break;
                    case "Birch": hue=84;sat=1.25f;val=1.2f;break;
                    case "Young": hue=88;sat=1.35f;val=1.22f;break;
                    case "Oak": hue=100;sat=1.2f;val=1.02f;break;
                    case "Broadleaf": hue=92;sat=1.3f;val=1.15f;break;
                    case "Hornbeam": hue=98;sat=1.25f;val=1.12f;break;
                    case "Uneven": hue=95;sat=1.25f;val=1.06f;break;
                    case "Leaning": hue=90;sat=1.25f;val=1.12f;break;
                    case "Slender": hue=94;sat=1.2f;val=1.1f;break;
                    default: hue=88;sat=1.3f;val=1.15f;break; // palms
                }
            }
            else if(name=="Coast_DuneGrass") { hue=66;sat=1.15f;val=1.06f; }
            for(int i=0;i<c.Length;i++)
            {
                Color s=((Color)c[i]).gamma;
                Color.RGBToHSV(s,out float h,out float sa,out float v);
                if(stone)
                {
                    // Lift dark facets so they read warm rather than blue-grey.
                    h=39f/360f; sa=.20f+(1-v)*.10f; v=Mathf.Min(1,v*.85f+.14f);
                }
                else if(s.g>s.r && s.g>=s.b && sa>.18f)
                {
                    h=Mathf.Lerp(h*360,hue,.75f)/360f; sa=Mathf.Clamp01(sa*sat); v=Mathf.Min(1,v*val);
                }
                else continue;
                var o=Color.HSVToRGB(h,sa,v).linear; o.a=((Color)c[i]).a; c[i]=o;
            }
        }

        public void DressResource(GameObject root,string kind,int ordinal)
        {
            if(kind!="Stone" && kind!="Timber") return;
            // Timber is the island's own wood: a palm on a palm island.
            var trait=kind=="Timber" ? TraitAt(root.transform.position) : null;
            bool palm=trait!=null && trait.palmy>.5f;
            string id=kind=="Stone" ? (ordinal%3==0 ? "Boulder_Broad" : ordinal%3==1 ? "Boulder_Long" : "Boulder_Low")
                : palm ? (ordinal%2==0 ? "Forest_PalmStraight" : "Forest_PalmLeaning") : "Forest_Hornbeam";
            var tp=Get(id); if(tp==null) return;
            if(!resourceMeshes.TryGetValue(id,out var mesh))
            {
                mesh=new Mesh {name="Nature_"+id,vertices=tp.v,normals=tp.n,colors32=tp.c,triangles=tp.t};
                mesh.RecalculateBounds();resourceMeshes.Add(id,mesh);
            }
            // Retain root identity and existing interaction components; only replace the visible mesh.
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.enabled=false;
                renderer.forceRenderingOff=true; // Gather/regrow toggles enabled on every renderer.
            }
            var visual=new GameObject("Nature resource visual");visual.transform.SetParent(root.transform,false);
            visual.AddComponent<MeshFilter>().sharedMesh=mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial=natureMaterial;
            var grounder=root.GetComponent<NatureGrounding>() ?? root.AddComponent<NatureGrounding>();
            grounder.AddInstance(root.transform);
        }

        public SceneryKit.Template Resolve(string old)
        {
            bool low=old.EndsWith("_LOD1",StringComparison.Ordinal);
            string id=low ? old.Substring(0,old.Length-5) : old;
            string mapped=null;
            if(id.StartsWith("Scrub_",StringComparison.Ordinal)) mapped="Coast_WindScrub";
            else if(id.StartsWith("Boulder_",StringComparison.Ordinal))
                mapped=id=="Boulder_0" ? "Boulder_Broad" : id=="Boulder_1" ? "Boulder_Low" : "Boulder_Long";
            else if(id.StartsWith("Cliff_",StringComparison.Ordinal)) mapped=id=="Cliff_1" ? "Cliff_BrokenSlab" : "Cliff_LowLedge";
            else switch(id)
            {
                case "Spruce": mapped="Forest_Pine"; break;
                case "Broad": mapped="Forest_Hornbeam"; break;
                case "Palm": mapped="Forest_PalmStraight"; break;
                case "Grass": mapped="Grass_Fan"; break;
                case "Grass_B": mapped="Grass_Sparse"; break;
                case "Grass_Dry": mapped="Coast_DuneGrass"; break;
                case "Fern": mapped="Broadleaf_Rosette"; break;
                case "Fern_B": mapped="Forest_Bracken"; break;
                case "Sticks": mapped="Twig_Fork"; break;
                case "Driftwood": mapped="Shore_ForkedDriftwood"; break;
            }
            if(mapped==null) return SceneryKit.Get(old);
            var tp=Get(mapped+(low ? "_LOD1" : "")) ?? Get(mapped);
            // Legacy rock stamps scale unit-sized forms into large outcrops.
            if(id.StartsWith("Cliff_",StringComparison.Ordinal) || id.StartsWith("Boulder_",StringComparison.Ordinal))
            {
                string key="Unit_"+mapped;
                if(templates.TryGetValue(key,out var unit)) return unit;
                Vector3 min=tp.v[0],max=min;
                foreach(var p in tp.v){ min=Vector3.Min(min,p);max=Vector3.Max(max,p); }
                Vector3 size=max-min;
                unit=new SceneryKit.Template { name=key,v=new Vector3[tp.v.Length],n=new Vector3[tp.n.Length],c=tp.c,t=tp.t,height=1,radius=.8f };
                for(int i=0;i<tp.v.Length;i++)
                {
                    unit.v[i]=new Vector3(tp.v[i].x/size.x,tp.v[i].y/size.y,tp.v[i].z/size.z);
                    unit.n[i]=Vector3.Scale(tp.n[i],size).normalized;
                }
                templates.Add(key,unit); return unit;
            }
            return tp;
        }
        public SceneryKit.Template Tree(Vector3 at,float roll,float cover,float slope,bool low=false)
        {
            float grove=Mathf.PerlinNoise(at.x*.022f+41,at.z*.022f+83);
            string name;
            float coastal=Mathf.PerlinNoise(at.x*.018f+102,at.z*.018f+22);
            // Astra: palms in coastal PATCHES, not every low site (the noise).
            // A palm island widens the band and drops the patch gate; and a
            // tree the scatter stood on the beach itself is always a palm.
            float palmy=Palmy;
            bool onSand=at.y<SandTop;
            if(onSand || (at.y<7f+6f*palmy && slope<.25f+.12f*palmy && coastal>.47f-.6f*palmy))
                name=roll<.35f ? "PalmStraight" : roll<.7f ? "PalmLeaning" : roll<.9f ? "PalmShort" : "PalmTall";
            else if(slope>.5f || (at.y>24 && grove>.57f)) name=roll<.7f ? "Pine" : "Coastal";
            else if(cover<.56f && roll<.64f) name="Young";
            else if(grove<.36f) name=roll<.7f ? "Birch" : "Slender";
            else if(grove>.65f) name=roll<.55f ? "Oak" : "Broadleaf";
            else name=roll<.38f ? "Hornbeam" : roll<.61f ? "Broadleaf" : roll<.8f ? "Uneven" : roll<.93f ? "Slender" : "Leaning";
            return Get("Forest_"+name+(low ? "_LOD1" : ""));
        }

        public struct Accent { public string name; public Vector3 position; public float yaw,scale; }
        static float Patch(float x,float z) => Mathf.PerlinNoise(x*.035f+23,z*.035f+37);
        public IEnumerable<Accent> Accents(Vector3 centre,float radius,Func<float,float,float> height,Func<float,float> radiusAt,List<SceneryWood.Tree> trees)
        {
            // Asymmetric groups share the shelter of a tree. Leave other roots
            // bare so the forest has clearings, not one decorative ring per trunk.
            for(int i=0;i<trees.Count;i++)
            {
                var root=trees[i].baseAt;
                float patch=Patch(root.x,root.z);
                if(patch<.43f || i%3==0)continue;
                float turn=Mathf.Repeat(root.x*.71f+root.z*.37f,6.28318f);
                bool rock=i%4==0;
                int count=rock ? 6 : 4;
                for(int j=0;j<count;j++)
                {
                    float a=turn+j*.61f;
                    float d=1.45f+(j%3)*.7f;
                    float x=root.x+Mathf.Cos(a)*d,z=root.z+Mathf.Sin(a)*d;
                    float h=height(x,z);
                    if(h<4 || Mathf.Abs(h-root.y)>1.2f)continue;
                    string id=j==0 && rock ? "Boulder_Broad" : j==1 && rock ? "Boulder_Low"
                        : j==2 ? "Broadleaf_Rosette" : j%2==0 ? "Grass_Fan" : "Grass_Tall";
                    if(root.y<6 && j>1)id="Coast_DuneGrass";
                    yield return new Accent {name=id,position=new Vector3(x,h,z),yaw=a,scale=j==0 && rock ? .8f+patch*.45f : .85f+patch*.55f};
                }
            }
            for(float z=-radius;z<radius;z+=13)
            for(float x=-radius;x<radius;x+=13)
            {
                float px=centre.x+x+3*Mathf.Sin(z*1.71f),pz=centre.z+z+3*Mathf.Sin(x*2.31f);
                if(new Vector2(px-centre.x,pz-centre.z).magnitude>radiusAt(Mathf.Atan2(px-centre.x,pz-centre.z))*.98f) continue;
                float h=height(px,pz);
                if(h<2.4f) continue;
                float slope=Mathf.Max(Mathf.Abs(height(px+2,pz)-height(px-2,pz)),Mathf.Abs(height(px,pz+2)-height(px,pz-2)))/4;
                float patch=Patch(px,pz);
                float roll=Mathf.Repeat(Mathf.Sin(px*12.9898f+pz*78.233f)*43758.5453f,1);
                string id=null;
                if(h<5.5f && slope<.25f && patch>.52f) id=roll<.12f ? "Shore_ForkedDriftwood" : roll<.4f ? "Shore_WashedStones" : "Coast_DuneGrass";
                else if(h>6 && slope>.18f && slope<.65f && patch>.60f) id=roll<.35f ? "Cliff_BrokenSlab" : "Cliff_TalusCluster";
                else if(h>6 && slope<.3f && patch>.65f) id=roll<.08f ? "Forest_FallenTrunk" : roll<.4f ? "Forest_Bracken" : "Grass_Fan";
                if(id==null)continue;
                yield return new Accent {name=id,position=new Vector3(px,h-.055f,pz),yaw=roll*Mathf.PI*2,scale=1f+patch*.6f};
                // Loose stones and plants soften only one edge of an outcrop.
                for(int j=0;j<3;j++)
                {
                    float a=roll*6.28318f+j*.8f,d=1.3f+j*.45f;
                    float x1=px+Mathf.Cos(a)*d,z1=pz+Mathf.Sin(a)*d,y1=height(x1,z1);
                    if(y1<2.5f || Mathf.Abs(y1-h)>1)continue;
                    string companion=h<5.5f ? (j==0 ? "Shore_WashedStones" : "Coast_DuneGrass")
                        : (j==0 ? "Boulder_Low" : j==1 ? "Grass_Sparse" : "Broadleaf_Rosette");
                    yield return new Accent {name=companion,position=new Vector3(x1,y1,z1),yaw=a,scale=.65f+patch*.55f};
                }
            }
        }

        static Color Palette(string hex) { ColorUtility.TryParseHtmlString(hex,out var c); return c.linear; }
        public void PaintGround(Island island,List<SceneryWood.Tree> trees,Func<float,float,float> height)
        {
            const int N=NatureGroundAtlas.Size;
            float span=island.MaxRadius*2+20;
            var islandCentre=new Vector2(island.transform.position.x,island.transform.position.z);
            var cover=new float[N*N]; var roots=new float[N*N];
            // The stamp window is set in METRES (20 m, where the moss term is
            // ~e^-4), not pixels: a fixed 6-pixel window was 7.8 m on Island_2
            // but under 4 m on a small island, and cutting the fade off there
            // drew a square stain under every trunk.
            int win=Mathf.Clamp(Mathf.CeilToInt(20f/(span/N)),4,40);
            foreach(var tree in trees)
            {
                int tx=Mathf.RoundToInt(((tree.baseAt.x-islandCentre.x)/span+.5f)*(N-1));
                int tz=Mathf.RoundToInt(((tree.baseAt.z-islandCentre.y)/span+.5f)*(N-1));
                for(int dz=-win;dz<=win;dz++) for(int dx=-win;dx<=win;dx++)
                {
                    int x=tx+dx,z=tz+dz; if(x<0 || z<0 || x>=N || z>=N) continue;
                    float d=(dx*dx+dz*dz)*span*span/(N*N);
                    cover[z*N+x]+=Mathf.Exp(-d/90f);
                    roots[z*N+x]=Mathf.Max(roots[z*N+x],Mathf.Exp(-d/22f));
                }
            }
            var pixels=new Color[N*N];
            // Colour D: three warm yellow-green tones (meadow / lush / dry, hue ~66-97 deg),
            // moss under canopy; earth stays warm.
            Color grass=Palette("#7DB04D"),lush=Palette("#5A8F39"),dry=Palette("#ADB562"),moss=Palette("#4E7A37"),earth=Palette("#A48C5E");
            for(int z=0;z<N;z++) for(int x=0;x<N;x++)
            {
                float px=islandCentre.x+(x/(N-1f)-.5f)*span,pz=islandCentre.y+(z/(N-1f)-.5f)*span;
                var delta=new Vector2(px-islandCentre.x,pz-islandCentre.y);
                float inside=island.RadiusAt(Mathf.Atan2(delta.x,delta.y))-delta.magnitude;
                int i=z*N+x; float noise=Patch(px,pz);
                var c=Color.Lerp(grass,dry,Mathf.SmoothStep(0,1,(noise-.3f)*2.5f));
                float lushPatch=Mathf.PerlinNoise(px*.021f+71,pz*.021f+13);
                c=Color.Lerp(c,lush,Mathf.SmoothStep(0,1,(lushPatch-.52f)*4f)*.85f);
                c=Color.Lerp(c,moss,Mathf.Clamp01(cover[i]*.52f)*.9f);
                c=Color.Lerp(c,earth,Mathf.Clamp01((roots[i]*(noise+.45f)-.18f)*1.4f)*.85f);
                c.a=Mathf.Clamp01(inside/8f); pixels[i]=c;
            }
            NatureGroundAtlas.Put(island,pixels,islandCentre,span,island.RadiusAt,groundIndexHalfExtent);
        }
    }
}
