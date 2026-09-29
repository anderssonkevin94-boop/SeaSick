using System.IO;
using UnityEngine;
using UnityEditor;
using SeaSick.Ship;
namespace SeaSick.Dev
{
 public static class SailingVisualReview
 {
  const string Folder="art-staging/visual-polish-2026-09-29";
  public static void Stage()
  {
   SeaSick.Save.SaveGame.Suppressed=true;
   var motor=Object.FindFirstObjectByType<ShipMotor>();
   motor.GetComponent<Rigidbody>().position=new Vector3(-120,0,-450);
   motor.GetComponent<Rigidbody>().rotation=Quaternion.Euler(0,25,0);
   motor.GetComponent<Rigidbody>().linearVelocity=Vector3.zero;
   var sky=Object.FindFirstObjectByType<SeaSick.World.SkyDirector>();
   var so=new SerializedObject(sky);so.FindProperty("pinTime").floatValue=.3f;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();
   SeaSick.UI.HudVisibility.TuningLab=false;motor.ThrottleOrder=.5f;
   PortraitGameView.Execute();
  }
  public static void Capture()
  {
   var motor=Object.FindFirstObjectByType<ShipMotor>();var source=Camera.main;
   var go=new GameObject("Angle comparison camera");var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;
   try {
    var p=motor.transform.position;var forward=Vector3.ProjectOnPlane(motor.transform.forward,Vector3.up).normalized;
    var names=new[]{"A-low-sailing","B-balanced","C-high-deck"}; var ahead=new[]{6f,7f,10f};
    var heights=new[]{8f,12f,16f};var backs=new[]{20f,23f,25f};
    for(int j=0;j<3;j++){
     cam.transform.position=p-forward*backs[j]+Vector3.up*heights[j];
     cam.transform.LookAt(p+forward*ahead[j]+Vector3.up*1.5f);cam.fieldOfView=55;
     Render(cam,1080,2340,names[j]);
    }
   }finally{Object.DestroyImmediate(go);}
  }
  public static void CaptureQuarter()
  {
   var motor=Object.FindFirstObjectByType<ShipMotor>();var source=Camera.main;
   var go=new GameObject("Rear quarter comparison");var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;
   try {
    var p=motor.transform.position;var forward=Vector3.ProjectOnPlane(motor.transform.forward,Vector3.up).normalized;
    cam.transform.position=p+(Quaternion.AngleAxis(32,Vector3.up)*-forward)*23+Vector3.up*12;
    cam.transform.LookAt(p+(Quaternion.AngleAxis(32,Vector3.up)*forward)*7+Vector3.up*1.5f);cam.fieldOfView=55;
    Render(cam,1080,2340,"rear-quarter/rear-three-quarter");
   }finally{Object.DestroyImmediate(go);}
  }
  static void Render(Camera cam,int w,int h,string name)
  {
   var rt=RenderTexture.GetTemporary(w,h,24);var previous=RenderTexture.active;Texture2D tex=null;
   try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();Directory.CreateDirectory(Folder);File.WriteAllBytes(Folder+"/"+name+".png",tex.EncodeToPNG());}
   finally{cam.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(tex)Object.DestroyImmediate(tex);}
  }
 }
}
