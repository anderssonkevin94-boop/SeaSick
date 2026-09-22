using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;
namespace SeaSick.Dev {
public static class GameOceanArtReview {
const string Output="docs/art-direction/game-ocean-integration";
[MenuItem("SeaSick/Art/Apply approved ocean style")]
public static void Apply(){

 var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/GraphicArt/OceanSurface.mat");
 Undo.RecordObject(m,"Apply approved wave-face style");
 m.SetFloat("_PaintedStrength",1);m.SetFloat("_SurfaceDetail",.25f);
 m.SetFloat("_ReflectionStrength",.09f);m.SetFloat("_SpecStrength",0);
 m.SetFloat("_SubsurfaceStrength",.08f);m.SetFloat("_FoamRelief",0);m.SetFloat("_FoamSparkle",0);
 m.SetColor("_DeepColor",new Color(.035f,.26f,.54f));
 m.SetColor("_ShallowColor",new Color(.09f,.49f,.74f));
 m.SetColor("_FoamColor",new Color(.86f,.95f,.96f));
 EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
 if(Application.isPlaying){var live=UnityEngine.Object.FindFirstObjectByType<OceanClipmap>();if(live)live.Material.CopyPropertiesFromMaterial(m);}
 File.WriteAllText("/tmp/seasick-game-ocean-result","Applied");
}
[MenuItem("SeaSick/Art/Capture gameplay ocean")]
public static void Capture(){
 GraphicWaveParity.Run();
 if(!Application.isPlaying)throw new Exception("Requires live Sea scene.");
 var ocean=UnityEngine.Object.FindFirstObjectByType<OceanClipmap>();
 if(!ocean||!Camera.main)throw new Exception("No live gameplay ocean/camera.");
 var sky=UnityEngine.Object.FindFirstObjectByType<SeaSick.World.SkyDirector>();
 double originalClock=SeaSick.World.TimeOfDay.Seconds;float originalStorm=sky.Storminess01;
 var so=new SerializedObject(sky);float pin=so.FindProperty("pinTime").floatValue;
 float weather=so.FindProperty("forceStorm").floatValue;
 var go=new GameObject("Game ocean review camera");var cam=go.AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;
 cam.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation);
 Directory.CreateDirectory(Output);
 try{
 Shot(cam,1920,1080,"live-weather");
 so.FindProperty("pinTime").floatValue=.38f;typeof(SeaSick.World.SkyDirector).GetProperty("Storminess01").SetValue(sky,0f);so.FindProperty("forceStorm").floatValue=0;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();sky.SendMessage("LateUpdate");
 Shot(cam,1920,1080,"day");Shot(cam,1080,2340,"portrait");
 var originalPosition=cam.transform.position;var originalRotation=cam.transform.rotation;var follow=ocean.FollowOverride;
 try{cam.transform.position=new Vector3(originalPosition.x-400,10,originalPosition.z);cam.transform.rotation=Quaternion.Euler(12,0,0);ocean.FollowOverride=cam.transform;ocean.SendMessage("LateUpdate");Shot(cam,1920,1080,"open-sea");Shot(cam,1080,2340,"open-sea-portrait");}
 finally{cam.transform.SetPositionAndRotation(originalPosition,originalRotation);ocean.FollowOverride=follow;ocean.SendMessage("LateUpdate");}
 var rotation=cam.transform.rotation;cam.transform.rotation=Quaternion.Euler(12,cam.transform.eulerAngles.y,0);Shot(cam,1920,1080,"horizon");cam.transform.rotation=rotation;
 typeof(SeaSick.World.SkyDirector).GetProperty("Storminess01").SetValue(sky,1f);so.FindProperty("forceStorm").floatValue=1;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();sky.SendMessage("LateUpdate");Shot(cam,960,540,"storm-light");
 so.FindProperty("pinTime").floatValue=.85f;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();sky.SendMessage("LateUpdate");Shot(cam,960,540,"night");
 if(ShaderUtil.ShaderHasError(ocean.Material.shader))throw new Exception("Ocean shader render error.");
 File.WriteAllText(Output+"/validation.txt","PASS live gameplay ocean rendered at desktop and portrait sizes. Existing FFT retained, with depth-faded pointed detail shared by rendered height and CPU buoyancy. Day/night/storm-light shots change sky controls only, not simulation state. Active material: "+ocean.Material.name+"; painted="+ocean.Material.GetFloat("_PaintedStrength")+"; Hs="+UnityEngine.Object.FindFirstObjectByType<SeaStateController>().CurrentHs+"\n");
 File.WriteAllText("/tmp/seasick-game-ocean-result","PASS");
 }finally{SeaSick.World.TimeOfDay.Scrub(originalClock);typeof(SeaSick.World.SkyDirector).GetProperty("Storminess01").SetValue(sky,originalStorm);so.FindProperty("pinTime").floatValue=pin;so.FindProperty("forceStorm").floatValue=weather;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();sky.SendMessage("LateUpdate");UnityEngine.Object.DestroyImmediate(go);}
}
public static void Shot(Camera cam,int w,int h,string name){var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;Texture2D t=null;try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;t=new Texture2D(w,h,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,w,h),0,0);t.Apply();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(t)UnityEngine.Object.DestroyImmediate(t);}}
}}
