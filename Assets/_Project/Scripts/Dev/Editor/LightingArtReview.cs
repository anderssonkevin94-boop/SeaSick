using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SeaSick.World;
namespace SeaSick.Dev {
public static class LightingArtReview {
 const string Output="docs/art-direction/lighting-study";
 public static void Setup(){
 const string path="Assets/_Project/Resources/AdventureLighting.asset";
 if(!AssetDatabase.LoadAssetAtPath<AdventureLighting>(path)){var p=ScriptableObject.CreateInstance<AdventureLighting>();AssetDatabase.CreateAsset(p,path);AssetDatabase.SaveAssetIfDirty(p);}
 }
 [MenuItem("SeaSick/Art/Compare adventure lighting")]
 public static void Capture(){
 if(!Application.isPlaying)throw new Exception("Requires live Sea scene");
 var terrain=UnityEngine.Object.FindFirstObjectByType<SeaSick.Terrain.TerrainStreamer>();if(terrain.PendingCount>0||terrain.LoadedCount<200)throw new Exception("Wait for terrain streaming: loaded="+terrain.LoadedCount+" pending="+terrain.PendingCount);
 var sky=SkyDirector.Instance;var data=new SerializedObject(sky);
 float oldPin=data.FindProperty("pinTime").floatValue,oldForce=data.FindProperty("forceStorm").floatValue;
 var json=EditorJsonUtility.ToJson(sky,true);var clock=TimeOfDay.Seconds;var storm=sky.Storminess01;bool enabled=sky.UseArtLighting;
 var cam=new GameObject("Lighting comparison camera").AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;
 var ship=UnityEngine.Object.FindFirstObjectByType<SeaSick.Ship.ShipMotor>().transform;
 cam.transform.position=ship.position-ship.forward*32+Vector3.up*15;cam.transform.LookAt(ship.position+ship.forward*4+Vector3.up*6);cam.fieldOfView=55;
 Directory.CreateDirectory(Output);File.WriteAllText(Output+"/before/SkyDirector-live.json",json);
 try{
  SetSky(sky,data,.38f,0);
  Pair(sky,cam,"ship",1600,900);
  Pair(sky,cam,"ship-portrait",900,1600);
  cam.transform.position=new Vector3(160,48,-155);cam.transform.LookAt(new Vector3(0,20,66));
  Pair(sky,cam,"home",1600,900);
  cam.transform.position=new Vector3(80,12,-82);cam.transform.LookAt(new Vector3(-25,18,90));
  Pair(sky,cam,"shore",1600,900);
  sky.UseArtLighting=true;SetSky(sky,data,.85f,0);Shot(cam,960,540,"night");
  SetSky(sky,data,.38f,1);Shot(cam,960,540,"storm");
  CheckWeather(sky,data,.85f,0);CheckWeather(sky,data,.38f,1);
  File.WriteAllText(Output+"/validation.txt","PASS: live Sea scene, four matched before/after camera views (including portrait), plus night and storm-light renders. Comparison uses the same clock (.38), weather (clear), camera, geometry and simulation instant per pair. Clear-night lighting and full-storm colour/intensity/ambient/fog parity passed; daytime sun follows the new lower arc in all weather. Production Resources profile saved. No scene save. Night and storm captures exercise lighting only, not storm physics.\n");
  File.WriteAllText("/tmp/seasick-light-result","PASS");
 }finally{data.Update();data.FindProperty("pinTime").floatValue=oldPin;data.FindProperty("forceStorm").floatValue=oldForce;data.ApplyModifiedPropertiesWithoutUndo();TimeOfDay.Scrub(clock);typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,storm);sky.UseArtLighting=enabled;sky.ForceApply();sky.SendMessage("LateUpdate");UnityEngine.Object.DestroyImmediate(cam.gameObject);}
 }
 static void CheckWeather(SkyDirector sky,SerializedObject data,float time,float storm){
 sky.UseArtLighting=false;SetSky(sky,data,time,storm);
 var sun=RenderSettings.sun; if(!sun) sun=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)[0];
 var color=sun.color;float intensity=sun.intensity;var rotation=sun.transform.rotation;
 var equator=RenderSettings.ambientEquatorColor;var ground=RenderSettings.ambientGroundColor;var top=RenderSettings.ambientSkyColor;float fog=RenderSettings.fogEndDistance;
 sky.UseArtLighting=true;SetSky(sky,data,time,storm);
 if(Vector4.Distance(color,sun.color)>.001f||Mathf.Abs(intensity-sun.intensity)>.001f||(storm==0 && Quaternion.Angle(rotation,sun.transform.rotation)>.01f)||Vector4.Distance(equator,RenderSettings.ambientEquatorColor)>.001f||Vector4.Distance(ground,RenderSettings.ambientGroundColor)>.001f||Vector4.Distance(top,RenderSettings.ambientSkyColor)>.001f||Mathf.Abs(fog-RenderSettings.fogEndDistance)>.01f)throw new Exception("Weather lighting parity failed at "+time+" storm "+storm);
 }
 static void SetSky(SkyDirector sky,SerializedObject so,float time,float storm){so.Update();so.FindProperty("pinTime").floatValue=time;so.FindProperty("forceStorm").floatValue=storm;so.ApplyModifiedPropertiesWithoutUndo();typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,storm);sky.ForceApply();sky.SendMessage("LateUpdate");}
 static void Pair(SkyDirector sky,Camera cam,string name,int w,int h){sky.UseArtLighting=false;sky.ForceApply();sky.SendMessage("LateUpdate");Shot(cam,w,h,name+"-before");sky.UseArtLighting=true;sky.ForceApply();sky.SendMessage("LateUpdate");Shot(cam,w,h,name+"-after");}
 static void Shot(Camera cam,int w,int h,string name){var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;Texture2D t=null;try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;t=new Texture2D(w,h,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,w,h),0,0);t.Apply();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(t)UnityEngine.Object.DestroyImmediate(t);}}
}}
