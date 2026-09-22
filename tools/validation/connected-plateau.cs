var go=UnityEngine.GameObject.Find("Home Plateau — Connected Ground");
if(go==null)throw new System.Exception("Connected ground missing");
var collider=go.GetComponent<UnityEngine.MeshCollider>();
var mesh=go.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
if(collider.sharedMesh!=mesh)throw new System.Exception("Render/collision mesh mismatch");
var streamer=UnityEngine.Object.FindFirstObjectByType<SeaSick.Terrain.TerrainStreamer>();
var prm=SeaSick.Terrain.TerrainParams.From(streamer.settings);
var verts=mesh.vertices;var indices=mesh.triangles;
float maxError=0;int tested=0;
for(int i=0;i<indices.Length;i+=3){
 var local=(verts[indices[i]]+verts[indices[i+1]]+verts[indices[i+2]])/3f;
 var world=go.transform.TransformPoint(local);
 var ray=new UnityEngine.Ray(world+UnityEngine.Vector3.up*100,UnityEngine.Vector3.down);
 if(!collider.Raycast(ray,out var hit,200))throw new System.Exception("Collider hole on triangle "+i/3);
 if(!SeaSick.Terrain.HomePlateauSurface.TryHeight(new Unity.Mathematics.float2(world.x,world.z),prm,out float height))throw new System.Exception("Height lookup hole");
 maxError=UnityEngine.Mathf.Max(maxError,UnityEngine.Mathf.Abs(hit.point.y-height));tested++;
}
if(maxError>.002f)throw new System.Exception("Ground/collision mismatch: "+maxError);
string report="Connected plateau: "+tested+" triangle centroids checked; maximum collider/height error="+maxError+" m; render and collision share exact mesh.";
System.IO.File.AppendAllText("/Users/kevinandersson/Desktop/SeaSick/docs/art-direction/storybook-validation.txt",report+"\n");return report;
