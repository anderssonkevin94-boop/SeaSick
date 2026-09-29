using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using SeaSick.World;
namespace SeaSick.Dev
{
 public static class WorldColorReview
 {
  public static void InstallContactShading()
  {
   foreach(string name in new[]{"Mobile","PC"})
   {
    string path="Assets/Settings/"+name+"_Renderer.asset";
    var data=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
    var feature=data.rendererFeatures.Find(f=>f!=null&&f.name=="Soft contact shading");
    if(feature==null){feature=ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion>();feature.name="Soft contact shading";AssetDatabase.AddObjectToAsset(feature,data);data.rendererFeatures.Add(feature);}
    var so=new SerializedObject(feature);var settings=so.FindProperty("m_Settings");
    settings.FindPropertyRelative("Downsample").boolValue=true;
    settings.FindPropertyRelative("AfterOpaque").boolValue=true;
    settings.FindPropertyRelative("Source").enumValueIndex=0;
    settings.FindPropertyRelative("NormalSamples").enumValueIndex=1;
    settings.FindPropertyRelative("AOMethod").enumValueIndex=1;
    settings.FindPropertyRelative("Samples").enumValueIndex=2;
    settings.FindPropertyRelative("BlurQuality").enumValueIndex=1;
    settings.FindPropertyRelative("Intensity").floatValue=.35f;
    settings.FindPropertyRelative("Radius").floatValue=.12f;
    settings.FindPropertyRelative("Falloff").floatValue=250f;
    so.ApplyModifiedPropertiesWithoutUndo();feature.SetActive(true);
    EditorUtility.SetDirty(feature);EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();
   }
  }
  public static void Capture(string label)
  {
   var sky=UnityEngine.Object.FindFirstObjectByType<SkyDirector>();
   var so=new SerializedObject(sky);float pin=so.FindProperty("pinTime").floatValue;
   float force=so.FindProperty("forceStorm").floatValue;double clock=TimeOfDay.Seconds;float storm=sky.Storminess01;
   var go=new GameObject("World color review");var cam=go.AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;
   var ship=UnityEngine.Object.FindFirstObjectByType<SeaSick.Ship.ShipMotor>().transform;
   var p=ship.position;var f=Vector3.ProjectOnPlane(ship.forward,Vector3.up).normalized;
   try {
    Directory.CreateDirectory("art-staging/world-color-pass/"+label);
    foreach(var mood in new[]{"noon","sunset","night","storm"})
    {
     so.Update();so.FindProperty("pinTime").floatValue=mood=="sunset"?.73f:mood=="night"?.85f:.5f;
     so.FindProperty("forceStorm").floatValue=mood=="storm"?1:0;so.ApplyModifiedPropertiesWithoutUndo();
     typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,mood=="storm"?1f:0f);sky.ForceApply();sky.SendMessage("LateUpdate");
     foreach(var lamp in UnityEngine.Object.FindObjectsByType<SeaSick.Ship.Modular.CoasterLantern>(FindObjectsSortMode.None))lamp.SendMessage("Update");
     cam.transform.position=p+(Quaternion.AngleAxis(32,Vector3.up)*-f)*23+Vector3.up*12;
     cam.transform.LookAt(p+(Quaternion.AngleAxis(32,Vector3.up)*f)*7+Vector3.up*1.5f);cam.fieldOfView=55;
     Shot(cam,1080,2340,label+"/ship-"+mood);
     cam.transform.position=new Vector3(80,12,-82);cam.transform.LookAt(new Vector3(-25,18,90));
     Shot(cam,1600,900,label+"/shore-"+mood);
    }
   }finally{
    so.Update();so.FindProperty("pinTime").floatValue=pin;so.FindProperty("forceStorm").floatValue=force;so.ApplyModifiedPropertiesWithoutUndo();
    TimeOfDay.Scrub(clock);typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,storm);sky.ForceApply();sky.SendMessage("LateUpdate");
    UnityEngine.Object.DestroyImmediate(go);
   }
  }
  static void Shot(Camera cam,int w,int h,string name)
  {
   var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;Texture2D tex=null;
   try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes("art-staging/world-color-pass/"+name+".png",tex.EncodeToPNG());}
   finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(tex)UnityEngine.Object.DestroyImmediate(tex);}
  }
 }
}
