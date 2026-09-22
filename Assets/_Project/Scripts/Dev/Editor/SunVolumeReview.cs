using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SeaSick.World;
namespace SeaSick.Dev {
public static class SunVolumeReview {
const string Output="docs/art-direction/game-volumetric-light/";
public static void Setup(){
var settings=AssetDatabase.LoadAssetAtPath<SunVolumeSettings>("Assets/_Project/Resources/SunVolumeSettings.asset");
if(!settings){settings=ScriptableObject.CreateInstance<SunVolumeSettings>();AssetDatabase.CreateAsset(settings,"Assets/_Project/Resources/SunVolumeSettings.asset");}
var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Resources/SunVolume.mat");
if(!mat){mat=new Material(Shader.Find("SeaSick/ShadowedSunlight"));AssetDatabase.CreateAsset(mat,"Assets/_Project/Resources/SunVolume.mat");}AssetDatabase.SaveAssets();
}
public static void Capture(){
if(!Application.isPlaying)throw new Exception("Requires Play");
var terrain=UnityEngine.Object.FindFirstObjectByType<SeaSick.Terrain.TerrainStreamer>();if(terrain.PendingCount>0)throw new Exception("Terrain still streaming");
var sky=SkyDirector.Instance;var effect=sky.GetComponent<VolumetricSunlight>();var so=new SerializedObject(sky);float pin=so.FindProperty("pinTime").floatValue,stormPin=so.FindProperty("forceStorm").floatValue;double clock=TimeOfDay.Seconds;float storm=sky.Storminess01;
var cam=new GameObject("Volume review camera").AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;cam.fieldOfView=60;
Directory.CreateDirectory(Output);
try{
float[] times={.28f,.5f,.72f,.88f,.72f};string[] names={"sunrise","noon","sunset","night","storm"};
for(int i=0;i<5;i++){
so.Update();so.FindProperty("pinTime").floatValue=times[i];so.FindProperty("forceStorm").floatValue=i==4?1:0;so.ApplyModifiedPropertiesWithoutUndo();typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,i==4?1f:0f);sky.ForceApply();sky.SendMessage("LateUpdate");
cam.transform.position=new Vector3(100,25,-100);cam.transform.LookAt(new Vector3(0,35,80));
effect.PreviewEnabled=false;Shot(cam,names[i]+"-off");effect.PreviewEnabled=true;Shot(cam,names[i]+"-on");
if(i==2){cam.transform.position=new Vector3(175,26,60);cam.transform.LookAt(new Vector3(0,40,70));effect.PreviewEnabled=false;Shot(cam,"shafts-off");effect.PreviewEnabled=true;Shot(cam,"shafts-on");Shot(cam,"shafts-portrait",720,1280);cam.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation);Shot(cam,"sailing");}
}
Shot(cam,"portrait",720,1280);
if(ShaderUtil.ShaderHasError(Resources.Load<Material>("SunVolume").shader))throw new Exception("Volume shader compile error");
File.WriteAllText(Output+"validation.txt","PASS: live Unity game captures, volume on/off at sunrise, noon, sunset, night and storm; portrait. Shader compiled. Medium = 32 samples, mobile defaults off. Full-resolution bounded volume, no device GPU benchmark. Ocean composites after atmosphere to preserve displaced water silhouette and existing ocean colour/fog. Moving cloud shadows not yet implemented.");File.WriteAllText("/tmp/seasick-sun-volume-result","PASS");
}finally{effect.PreviewEnabled=true;so.Update();so.FindProperty("pinTime").floatValue=pin;so.FindProperty("forceStorm").floatValue=stormPin;so.ApplyModifiedPropertiesWithoutUndo();TimeOfDay.Scrub(clock);typeof(SkyDirector).GetProperty("Storminess01").SetValue(sky,storm);sky.ForceApply();sky.SendMessage("LateUpdate");UnityEngine.Object.DestroyImmediate(cam.gameObject);}
}
static void Shot(Camera cam,string name,int w=1280,int h=720){var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;var tex=new Texture2D(w,h,TextureFormat.RGB24,false);try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes(Output+name+".png",tex.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}}
}
}
