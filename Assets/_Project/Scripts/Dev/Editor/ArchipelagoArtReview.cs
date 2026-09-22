using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.Terrain;
using SeaSick.World;

namespace SeaSick.Dev
{
    // Review actual streamed islands, with their existing scatter and resources.
    public static class ArchipelagoArtReview
    {
        const string Output = "docs/art-direction/archipelago-style";
        static Camera camera;
        static TerrainStreamer streamer;
        static Transform oldTarget, oldOceanFollow;
        static SeaSick.Ocean.OceanClipmap ocean;
        static Island[] islands;
        static int index;
        static double ready;
        static SkyDirector sky;
        static float oldPin, oldStorm, oldStorminess;
        static double oldClock;
        static StringBuilder report;
        [MenuItem("SeaSick/Art/Review archipelago style")]
        public static void Capture()
        {
            if (!Application.isPlaying || camera) throw new Exception("Requires play mode and no running review.");
            streamer = UnityEngine.Object.FindFirstObjectByType<TerrainStreamer>();
            var candidates = Island.All.Where(i => !i.IsHome).OrderBy(i => i.MaxRadius).ToArray();
            if (candidates.Length < 3) throw new Exception("World has not populated.");
            islands = new[] { candidates[candidates.Length/4], candidates[candidates.Length*3/4], candidates[candidates.Length-1] };
            report = new StringBuilder("Live archipelago review\n");
            report.AppendLine("Non-home islands sharing terrain treatment: " + candidates.Length);
            report.AppendLine("Material: " + streamer.material.name + "; authored lighting=" + streamer.material.GetFloat("_AuthoredFormLighting"));
            camera = new GameObject("Archipelago review camera").AddComponent<Camera>();
            camera.CopyFrom(Camera.main); camera.enabled = false; camera.farClipPlane=6000;
            oldTarget=streamer.target; streamer.target=camera.transform;
            ocean=UnityEngine.Object.FindFirstObjectByType<SeaSick.Ocean.OceanClipmap>();
            if(ocean){oldOceanFollow=ocean.FollowOverride;ocean.FollowOverride=camera.transform;}
            sky=UnityEngine.Object.FindFirstObjectByType<SkyDirector>();
            oldClock=TimeOfDay.Seconds;oldStorminess=sky.Storminess01;
            var so=new SerializedObject(sky);oldPin=so.FindProperty("pinTime").floatValue;oldStorm=so.FindProperty("forceStorm").floatValue;
            so.FindProperty("pinTime").floatValue=.38f;so.FindProperty("forceStorm").floatValue=0;so.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory(Output);ValidateTerrain();index=0;Place();EditorApplication.update+=Tick;
        }
        static void ValidateTerrain()
        {
            var prm=TerrainParams.From(streamer.settings);var unstyled=prm;unstyled.storybookLandforms=0;
            using(var lut=TerrainCurveLut.Bake(streamer.settings.profileCurve,Unity.Collections.Allocator.TempJob))
            {
                var random=new Unity.Mathematics.Random(71423);int shore=0;float shoreError=0;
                for(int n=0;n<6000;n++)
                {
                    var p=random.NextFloat2(new Unity.Mathematics.float2(-3500),new Unity.Mathematics.float2(3500));
                    if(TerrainHeight.HomeIsleWeight(p,prm)>0)continue;
                    float a=TerrainHeight.Height(p,prm,lut),b=TerrainHeight.Height(p,unstyled,lut);
                    if(float.IsNaN(a)||float.IsInfinity(a))throw new Exception("Non-finite terrain");
                    if(b<=prm.seaLevel+4){shore++;shoreError=Mathf.Max(shoreError,Mathf.Abs(a-b));}
                }
                if(shoreError>.0001f)throw new Exception("Shoreline changed: "+shoreError);
                report.AppendLine("6000 deterministic world samples finite; "+shore+" low beach/seabed samples unchanged, max error="+shoreError+" m.");
            }
        }
        static void Place()
        {
            var island=islands[index];var centre=island.transform.position;float r=Mathf.Clamp(island.MaxRadius,60,500);
            camera.transform.position=centre+new Vector3(r*.7f,r*.6f+35,-r*1.8f-90);
            camera.transform.LookAt(centre+Vector3.up*30);ready=EditorApplication.timeSinceStartup+22;
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<ready)return;
            try {
                if(!Application.isPlaying)throw new Exception("Play stopped during review");
                sky.ForceApply();sky.SendMessage("LateUpdate");
                Shot(1600,900,"island-"+(index+1));
                if(index==1)Shot(900,1600,"island-portrait");
                if(index==2)
                {
                    var centre=islands[index].transform.position;var peak=centre;
                    for(float z=-400;z<=400;z+=40)for(float x=-400;x<=400;x+=40)
                    {
                        float height=Island.TerrainHeight(centre.x+x,centre.z+z);
                        if(height>peak.y)peak=new Vector3(centre.x+x,height,centre.z+z);
                    }
                    var position=camera.transform.position;var rotation=camera.transform.rotation;
                    camera.transform.position=peak+new Vector3(120,15,-210);camera.transform.LookAt(peak-Vector3.up*20);
                    if(ocean)ocean.SendMessage("LateUpdate");Shot(1600,900,"cliff-detail");
                    camera.transform.SetPositionAndRotation(position,rotation);
                }
                var island=islands[index];
                using(var lut=TerrainCurveLut.Bake(streamer.settings.profileCurve,Unity.Collections.Allocator.TempJob))
                {
                    var prm=TerrainParams.From(streamer.settings);var coord=streamer.ChunkCoordOf(island.transform.position);var mesh=streamer.MeshAt(coord);
                    if(!mesh)throw new Exception("Island terrain did not finish streaming");
                    var vertices=mesh.vertices;int n=(streamer.settings.chunkResolution-1)/streamer.LodAt(coord)+1;float error=0;
                    for(int v=0;v<n*n;v++)
                    {
                        var p=new Unity.Mathematics.float2(vertices[v].x+coord.x*streamer.settings.chunkSize,vertices[v].z+coord.y*streamer.settings.chunkSize);
                        error=Mathf.Max(error,Mathf.Abs(vertices[v].y-TerrainHeight.Evaluate(p,prm,lut,false).height));
                    }
                    if(error>.01f)throw new Exception("Mesh/height mismatch: "+error);
                    report.AppendLine("Streamed mesh/height samples="+(n*n)+" maximum error="+error+" m");
                }
                report.AppendLine(island.name+" centre="+island.transform.position+" radius="+island.MaxRadius+" resource="+island.ResourceName+" loaded="+streamer.LoadedCount+" pending="+streamer.PendingCount);
                if(ShaderUtil.ShaderHasError(streamer.material.shader))throw new Exception("Terrain shader failed after render");
                if(++index<islands.Length){Place();return;}
                File.WriteAllText(Output+"/validation.txt",report+"PASS: three live islands rendered; desktop and portrait.\n");
                File.WriteAllText("/tmp/seasick-island-result","PASS");Cleanup();
            } catch(Exception e){File.WriteAllText("/tmp/seasick-island-result",e.ToString());Cleanup();}
        }
        static void Shot(int w,int h,string name)
        {
            var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;Texture2D t=null;
            try{camera.targetTexture=rt;camera.aspect=(float)w/h;camera.Render();RenderTexture.active=rt;t=new Texture2D(w,h,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,w,h),0,0);t.Apply();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(t)UnityEngine.Object.DestroyImmediate(t);}
        }
        static void Cleanup()
        {
            EditorApplication.update-=Tick;
            if(streamer)streamer.target=oldTarget;
            if(ocean)ocean.FollowOverride=oldOceanFollow;
            if(sky){TimeOfDay.Scrub(oldClock);typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,oldStorminess);var so=new SerializedObject(sky);so.FindProperty("pinTime").floatValue=oldPin;so.FindProperty("forceStorm").floatValue=oldStorm;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();}
            if(camera)UnityEngine.Object.DestroyImmediate(camera.gameObject);camera=null;
        }
    }
}
