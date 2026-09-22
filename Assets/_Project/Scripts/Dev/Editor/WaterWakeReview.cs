using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.Ocean;
namespace SeaSick.Dev {
public static class WaterWakeReview {
static ShipMotor motor;static Rigidbody body;static HelmInput helm;static SeaSick.World.SkyDirector sky;
static Vector3 oldPos,oldVelocity,oldAngular;static Quaternion oldRot;static bool oldKinematic,motorEnabled,helmEnabled;
static bool nightPhase;static float pin,speed;static double began,last;static GameObject cameraObject;
const string Out="docs/art-direction/painted-ocean-v2";
public static void Start(){
 if(!Application.isPlaying||motor)throw new Exception("Review needs Play mode and no active review.");
 motor=UnityEngine.Object.FindFirstObjectByType<ShipMotor>();body=motor.GetComponent<Rigidbody>();helm=motor.GetComponent<HelmInput>();sky=UnityEngine.Object.FindFirstObjectByType<SeaSick.World.SkyDirector>();
 oldPos=body.position;oldRot=body.rotation;oldVelocity=body.linearVelocity;oldAngular=body.angularVelocity;oldKinematic=body.isKinematic;motorEnabled=motor.enabled;helmEnabled=helm&&helm.enabled;speed=motor.CurrentSpeed;
 var so=new SerializedObject(sky);pin=so.FindProperty("pinTime").floatValue;so.FindProperty("pinTime").floatValue=.30f;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();
 motor.enabled=false;if(helm)helm.enabled=false;body.isKinematic=true;
 body.position=new Vector3(0,0,-100);body.rotation=Quaternion.Euler(0,180,0);
 typeof(ShipMotor).GetProperty("CurrentSpeed").SetValue(motor,8f);
 cameraObject=new GameObject("Wake review camera");var cam=cameraObject.AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.fieldOfView=60;cam.enabled=false;
 nightPhase=false;began=last=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
 File.WriteAllText("/tmp/seasick-water-result","Wake review running; no asset edits until completion.");
}
static void Tick(){try{
 if(!Application.isPlaying||!motor){Finish();return;}
 double now=EditorApplication.timeSinceStartup;float dt=Mathf.Min(.05f,(float)(now-last));last=now;
 var p=body.position+Vector3.back*8*dt;p.y=OceanSampler.Ready?OceanSampler.SampleImmediate(p).height:0;body.position=p;
 if(now-began<10)return;
 Directory.CreateDirectory(Out);var cam=cameraObject.GetComponent<Camera>();
 cam.transform.position=p+new Vector3(18,32,72);cam.transform.LookAt(p+Vector3.forward*12+Vector3.up*8);
 if(!nightPhase){Capture(cam,1920,1080,"wake-day");Capture(cam,1080,2340,"wake-portrait");
 var so=new SerializedObject(sky);so.FindProperty("pinTime").floatValue=.8f;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();nightPhase=true;began=now-9;return;}
 Capture(cam,1920,1080,"wake-night");
 var wake=UnityEngine.Object.FindFirstObjectByType<SurfaceWake>();var mesh=GameObject.Find("Surface foam wake").GetComponent<MeshFilter>().sharedMesh;
 bool error=ShaderUtil.ShaderHasError(Resources.Load<Shader>("Shaders/SurfaceWake"))||ShaderUtil.ShaderHasError(UnityEngine.Object.FindFirstObjectByType<OceanClipmap>().Material.shader);
 File.WriteAllText(Out+"/wake-validation.txt",$"Controlled 8 m/s path over live ocean; mesh vertices {mesh.vertexCount}; shader error {error}. Day, night and portrait rendered. This checks foam appearance, not sailing physics.\n");
 File.WriteAllText("/tmp/seasick-water-result",error?"FAIL shader":"PASS wake review");Finish();
}catch(Exception e){File.WriteAllText("/tmp/seasick-water-result",e.ToString());Finish();}}
static void Finish(){EditorApplication.update-=Tick;if(motor){motor.enabled=motorEnabled;typeof(ShipMotor).GetProperty("CurrentSpeed").SetValue(motor,speed);}if(helm)helm.enabled=helmEnabled;if(body){body.position=oldPos;body.rotation=oldRot;body.isKinematic=oldKinematic;if(!oldKinematic){body.linearVelocity=oldVelocity;body.angularVelocity=oldAngular;}}if(sky){var so=new SerializedObject(sky);so.FindProperty("pinTime").floatValue=pin;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();}if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);motor=null;}
static void Capture(Camera cam,int w,int h,string name){var rt=RenderTexture.GetTemporary(w,h,24);var previous=RenderTexture.active;Texture2D tex=null;try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes(Out+"/"+name+".png",tex.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(tex)UnityEngine.Object.DestroyImmediate(tex);}}
}}
