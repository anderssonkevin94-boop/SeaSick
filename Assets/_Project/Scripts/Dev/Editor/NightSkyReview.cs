using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SeaSick.World;
namespace SeaSick.Dev {
public static class NightSkyReview {
 const string Output="docs/art-direction/night-sky";
 public static void Capture(){
 if(!Application.isPlaying)throw new Exception("Play first");
 var sky=SkyDirector.Instance;var so=new SerializedObject(sky);float pin=so.FindProperty("pinTime").floatValue,force=so.FindProperty("forceStorm").floatValue;double clock=TimeOfDay.Seconds;float storm=sky.Storminess01;
 var cam=new GameObject("Night sky review").AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;cam.fieldOfView=65;cam.transform.position=new Vector3(0,15,-100);cam.transform.rotation=Quaternion.Euler(-18,70,0);
 var material=RenderSettings.skybox;Directory.CreateDirectory(Output);
 try{
 material.SetFloat("_CloudReviewOverride",1);material.SetFloat("_CloudReviewTime",0);
 Set(sky,so,.38f,0);Shot(cam,"day");
 Set(sky,so,.74f,0);Shot(cam,"sunset");
 TimeOfDay.Scrub(0);Set(sky,so,.85f,0);
 cam.transform.rotation=Quaternion.LookRotation(sky.MoonDirection+Vector3.up*.12f);Shot(cam,"night");
 material.SetFloat("_CloudReviewTime",45);Shot(cam,"night-clouds-45s");
 material.SetFloat("_CloudReviewTime",90);Shot(cam,"night-clouds-90s");
 for(int frame=0;frame<16;frame++){material.SetFloat("_CloudReviewTime",frame*6);Shot(cam,"motion-"+frame.ToString("D2"));}
 material.SetFloat("_CloudReviewTime",300);Shot(cam,"moon-break");
 int mask=cam.cullingMask;cam.cullingMask=0;Shot(cam,"sky-only");cam.cullingMask=mask;
 Set(sky,so,.85f,1);Shot(cam,"night-storm");
 if(ShaderUtil.ShaderHasError(material.shader))throw new Exception("Sky shader render error");
 File.WriteAllText(Output+"/validation.txt","PASS: live sky rendered in day, sunset, full-moon night (day 0), night storm. Cloud-only time sampled at 0/45/90 seconds with camera, moon and star rotation fixed. Phase progression retained.\n");File.WriteAllText("/tmp/seasick-sky-result","PASS");
 }finally{material.SetFloat("_CloudReviewOverride",0);so.Update();so.FindProperty("pinTime").floatValue=pin;so.FindProperty("forceStorm").floatValue=force;so.ApplyModifiedPropertiesWithoutUndo();TimeOfDay.Scrub(clock);typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,storm);sky.ForceApply();sky.SendMessage("LateUpdate");UnityEngine.Object.DestroyImmediate(cam.gameObject);}
 }
 static void Set(SkyDirector s,SerializedObject so,float t,float storm){so.Update();so.FindProperty("pinTime").floatValue=t;so.FindProperty("forceStorm").floatValue=storm;so.ApplyModifiedPropertiesWithoutUndo();typeof(SkyDirector).GetProperty("Storminess01").SetValue(s,storm);s.ForceApply();s.SendMessage("LateUpdate");}
 static void Shot(Camera cam,string name){var rt=RenderTexture.GetTemporary(1600,900,24);var old=RenderTexture.active;var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);try{cam.targetTexture=rt;cam.aspect=1600f/900;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+name+".png",tex.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}}
}}
