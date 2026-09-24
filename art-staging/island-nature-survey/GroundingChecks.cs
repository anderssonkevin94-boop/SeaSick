using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using SeaSick.Terrain;
using SeaSick.World;
using SeaSick.Save;

public static class GroundingChecks
{
    const string Path="art-staging/island-nature-survey/redesign/grounding-regression.txt";
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Start()
    {
        if(!Application.isPlaying || !SaveGame.Suppressed)throw new Exception("Protected Play mode required");
        File.WriteAllText(Path,"RUNNING\n");GameBoot.Instance.StartCoroutine(Run());return "Grounding and terrain LOD regression running";
    }
    static IEnumerator Run()
    {
        var streamer=UnityEngine.Object.FindFirstObjectByType<TerrainStreamer>();
        var original=streamer.settings;var temporary=UnityEngine.Object.Instantiate(original);
        var wood=UnityEngine.Object.FindObjectsByType<SceneryWood>().First(w=>w.GetComponent<NatureGrounding>()!=null);
        int before=wood.FelledInMesh();
        try
        {
            streamer.settings=temporary;
            foreach(int step in new[]{2,4,1})
            {
                temporary.lod0Radius=step==1 ? original.lod0Radius : -1;
                temporary.lod1Radius=step==4 ? -1 : step==2 ? 999 : original.lod1Radius;
                streamer.MarkDirty();yield return null;yield return null;
                float deadline=Time.realtimeSinceStartup+90;
                while(streamer.PendingCount>0 && Time.realtimeSinceStartup<deadline)yield return null;
                if(streamer.PendingCount>0)throw new Exception("Terrain did not settle");
                if(step==4){wood.Restand(0);if(wood.FelledInMesh()!=before)throw new Exception("Regrowth failed across terrain LOD transition");}
                Check(streamer,step);
                if(step==2){wood.FellForLedger(0);if(wood.FelledInMesh()!=before+1)throw new Exception("Felling failed");}
            }
            File.AppendAllText(Path,"PASS: felling/regrowth across terrain LOD changes.\nPASS: all checks completed.\n");
        }
        finally
        {
            wood.Restand(0);streamer.settings=original;streamer.MarkDirty();UnityEngine.Object.Destroy(temporary);
        }
    }
    static void Check(TerrainStreamer streamer,int step)
    {
        int feet=0,missing=0;float maxGap=float.MinValue;
        var colliderGo=new GameObject("Temporary ground validation collider");var collider=colliderGo.AddComponent<MeshCollider>();
        var checkedChunks=new HashSet<Unity.Mathematics.int2>();float maxSampleError=0;int rays=0;
        try
        {
            foreach(var g in UnityEngine.Object.FindObjectsByType<NatureGrounding>())
            {
                g.Refresh();var wood=g.GetComponent<SceneryWood>();
                var anchors=(List<NatureGrounding.Anchor>)typeof(NatureGrounding).GetField("anchors",Private).GetValue(g);
                foreach(var a in anchors)
                {
                    Vector3[] actual=null;
                    if(a.instance==null)
                    {
                        actual=wood.Cells[a.cell].lod0.vertices;
                        if(Enumerable.Range(a.start0,a.count0).All(i=>(actual[i]-actual[a.start0]).sqrMagnitude<1e-8f))continue;
                    }
                    float dy=a.instance!=null ? a.instance.position.y-a.referenceY : actual[a.start0].y-a.referenceY;
                    foreach(var p in a.feet)
                    {
                        if(!g.SurfaceHeight(p.x,p.z,out float y)){missing++;continue;}
                        feet++;maxGap=Mathf.Max(maxGap,p.y+dy-y);
                        var key=streamer.ChunkCoordOf(p);
                        if(checkedChunks.Add(key))
                        {
                            collider.sharedMesh=streamer.MeshAt(key);
                            colliderGo.transform.position=new Vector3(key.x*streamer.settings.chunkSize,0,key.y*streamer.settings.chunkSize);
                            Physics.SyncTransforms();
                            if(!collider.Raycast(new Ray(new Vector3(p.x,y+500,p.z),Vector3.down),out var hit,1000))throw new Exception("Terrain collider ray missed");
                            maxSampleError=Mathf.Max(maxSampleError,Mathf.Abs(hit.point.y-y));rays++;
                        }
                    }
                }
            }
            if(missing>0 || maxGap>.005f || maxSampleError>.003f)throw new Exception($"Ground check failed: missing {missing}, gap {maxGap}, ray error {maxSampleError}");
            File.AppendAllText(Path,$"PASS: terrain step {step}: {feet} actual mesh contacts, largest gap {maxGap:F4} m; {rays} independent collider rays, max height error {maxSampleError:F6} m.\n");
        }
        finally{UnityEngine.Object.DestroyImmediate(colliderGo);}
    }
}
