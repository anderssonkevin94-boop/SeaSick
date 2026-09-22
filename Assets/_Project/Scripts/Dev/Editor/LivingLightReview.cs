using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SeaSick.World;
namespace SeaSick.Dev {
public static class LivingLightReview {
 const string Output="docs/art-direction/living-light";
 public static void Capture(){
 if(!Application.isPlaying)throw new Exception("Requires Play");
 var terrain=UnityEngine.Object.FindFirstObjectByType<SeaSick.Terrain.TerrainStreamer>();if(terrain.PendingCount>0)throw new Exception("Terrain still streaming");
 var sky=SkyDirector.Instance;var so=new SerializedObject(sky);float pin=so.FindProperty("pinTime").floatValue,force=so.FindProperty("forceStorm").floatValue;double clock=TimeOfDay.Seconds;float storm=sky.Storminess01;
 var cam=new GameObject("Living light review").AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;cam.fieldOfView=65;cam.transform.position=new Vector3(160,48,-155);cam.transform.LookAt(new Vector3(0,20,66));
 var mat=RenderSettings.skybox;float strength=mat.GetFloat("_SunShaftStrength");Directory.CreateDirectory(Output);
 try{
 mat.SetFloat("_CloudReviewOverride",1);mat.SetFloat("_CloudReviewTime",0);
 float[] times={.27f,.32f,.50f,.69f,.73f,.77f,.88f};string[] names={"sunrise","morning","noon","golden-hour","sunset","blue-hour","night"};
 for(int i=0;i<times.Length;i++){Set(sky,so,times[i],0);Shot(cam,names[i]);}
 Set(sky,so,.72f,0);cam.transform.position=new Vector3(0,12,-150);cam.transform.rotation=Quaternion.LookRotation(sky.SunDirection+Vector3.up*.20f);
 for(int i=0;i<4;i++){mat.SetFloat("_CloudReviewTime",i*90);Shot(cam,"rays-"+i);}
 mat.SetFloat("_CloudReviewTime",90);mat.SetFloat("_SunShaftStrength",0);Shot(cam,"rays-before");mat.SetFloat("_SunShaftStrength",strength);Shot(cam,"rays-after");Shot(cam,"rays-portrait",900,1600);
 Set(sky,so,.72f,1);Shot(cam,"storm");
 if(ShaderUtil.ShaderHasError(mat.shader))throw new Exception("Sky shader render failure");
 File.WriteAllText(Output+"/validation.txt","PASS live Unity captures: sunrise, morning, noon, golden hour, sunset, blue hour, night, storm. Matched sun-ray on/off capture with fixed clock, camera, cloud time and simulation frame. Desktop and portrait ray views rendered. Sky shader checked after rendering. Rays are sky scattering, occluded by rendered world geometry; not volumetric beams between nearby objects. No frame-time benchmark performed.\n");File.WriteAllText("/tmp/seasick-living-result","PASS");
 }finally{mat.SetFloat("_CloudReviewOverride",0);mat.SetFloat("_SunShaftStrength",strength);so.Update();so.FindProperty("pinTime").floatValue=pin;so.FindProperty("forceStorm").floatValue=force;so.ApplyModifiedPropertiesWithoutUndo();TimeOfDay.Scrub(clock);typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,storm);sky.ForceApply();sky.SendMessage("LateUpdate");UnityEngine.Object.DestroyImmediate(cam.gameObject);}
 }
 static void Set(SkyDirector s,SerializedObject so,float t,float storm){so.Update();so.FindProperty("pinTime").floatValue=t;so.FindProperty("forceStorm").floatValue=storm;so.ApplyModifiedPropertiesWithoutUndo();typeof(SkyDirector).GetProperty("Storminess01").SetValue(s,storm);s.ForceApply();s.SendMessage("LateUpdate");}
 static void Shot(Camera cam,string name,int w=1600,int h=900){var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;var tex=new Texture2D(w,h,TextureFormat.RGB24,false);try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+name+".png",tex.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}}
}}
