using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;
namespace SeaSick.Dev {
public static class GameOceanMotionReview {
static Camera cam;static OceanClipmap ocean;static Transform oldFollow;static int frame;static double next;
static SeaSick.World.SkyDirector sky;static float oldPin,oldStorm,stormValue;static double oldClock;
[MenuItem("SeaSick/Art/Record gameplay ocean motion")]
public static void Start(){
 if(!Application.isPlaying||cam)throw new Exception("Requires Play mode and no active recording.");
 ocean=UnityEngine.Object.FindFirstObjectByType<OceanClipmap>();sky=UnityEngine.Object.FindFirstObjectByType<SeaSick.World.SkyDirector>();
 var so=new SerializedObject(sky);oldPin=so.FindProperty("pinTime").floatValue;oldStorm=so.FindProperty("forceStorm").floatValue;stormValue=sky.Storminess01;oldClock=SeaSick.World.TimeOfDay.Seconds;
 so.FindProperty("pinTime").floatValue=.38f;so.FindProperty("forceStorm").floatValue=0;so.ApplyModifiedPropertiesWithoutUndo();typeof(SeaSick.World.SkyDirector).GetProperty("Storminess01").SetValue(sky,0f);sky.ForceApply();
 cam=new GameObject("Gameplay motion review").AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;var p=Camera.main.transform.position;
 cam.transform.SetPositionAndRotation(new Vector3(p.x-400,10,p.z),Quaternion.Euler(12,0,0));oldFollow=ocean.FollowOverride;ocean.FollowOverride=cam.transform;
 frame=0;next=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Tick;
 File.WriteAllText("/tmp/seasick-game-ocean-result","RECORDING");
}
static void Tick(){if(EditorApplication.timeSinceStartup<next)return;try{
 GameOceanArtReview.Shot(cam,960,540,"motion-"+frame);frame++;next=EditorApplication.timeSinceStartup+.10;
 if(frame==24){File.WriteAllText("/tmp/seasick-game-ocean-result","PASS motion");Finish();}
}catch(Exception e){File.WriteAllText("/tmp/seasick-game-ocean-result",e.ToString());Finish();}}
static void Finish(){EditorApplication.update-=Tick;if(ocean)ocean.FollowOverride=oldFollow;
 if(sky){var so=new SerializedObject(sky);so.FindProperty("pinTime").floatValue=oldPin;so.FindProperty("forceStorm").floatValue=oldStorm;so.ApplyModifiedPropertiesWithoutUndo();SeaSick.World.TimeOfDay.Scrub(oldClock);typeof(SeaSick.World.SkyDirector).GetProperty("Storminess01").SetValue(sky,stormValue);sky.ForceApply();}
 if(cam)UnityEngine.Object.DestroyImmediate(cam.gameObject);cam=null;
}
}}
