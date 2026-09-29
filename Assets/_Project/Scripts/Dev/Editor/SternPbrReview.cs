using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SeaSick.Ship.Modular;
namespace SeaSick.Dev
{
 public static class SternPbrReview
 {
  [Serializable] class Atlas { public Corner[] corners; }
  [Serializable] class Corner { public float[] p,n,uv; }
  const string Folder="Assets/_Project/Resources/ShipModules/PbrTrial";
  const string Output="art-staging/stern-pbr";
  public static void Import()
  {
   Directory.CreateDirectory(Folder);
   foreach(var name in new[]{"BaseColor","Normal","Surface"}) File.Copy(Output+"/"+name+".png",Folder+"/"+name+".png",true);
   File.Copy(Output+"/included.json",Folder+"/Included.json",true);AssetDatabase.Refresh();
   foreach(var name in new[]{"BaseColor","Normal","Surface"})
   {
    var ti=(TextureImporter)AssetImporter.GetAtPath(Folder+"/"+name+".png");
    ti.textureType=name=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
    ti.sRGBTexture=name=="BaseColor";ti.mipmapEnabled=true;ti.wrapMode=TextureWrapMode.Clamp;
    ti.maxTextureSize=2048;ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.alphaSource=TextureImporterAlphaSource.FromInput;
    var ios=ti.GetPlatformTextureSettings("iPhone");ios.overridden=true;ios.maxTextureSize=2048;ios.format=TextureImporterFormat.ASTC_6x6;ti.SetPlatformTextureSettings(ios);ti.SaveAndReimport();
   }
   var a=JsonUtility.FromJson<Atlas>(File.ReadAllText(Output+"/atlas-flat.json"));
   int count=a.corners.Length;var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];var ts=new int[count];
   for(int i=0;i<count;i++){var c=a.corners[i];v[i]=new Vector3(c.p[0],c.p[1],c.p[2]);n[i]=new Vector3(c.n[0],c.n[1],c.n[2]);uv[i]=new Vector2(c.uv[0],c.uv[1]);ts[i]=i;}
   var mesh=new Mesh{name="Stern PBR atlas",indexFormat=IndexFormat.UInt32};mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=ts;mesh.RecalculateTangents();mesh.RecalculateBounds();
   var oldMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/SternAtlas.asset");if(oldMesh){EditorUtility.CopySerialized(mesh,oldMesh);UnityEngine.Object.DestroyImmediate(mesh);mesh=oldMesh;}else AssetDatabase.CreateAsset(mesh,Folder+"/SternAtlas.asset");
   var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/SternPBR.mat");if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,Folder+"/SternPBR.mat");}
   mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/BaseColor.png"));mat.SetColor("_BaseColor",Color.white);
   mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Normal.png"));mat.SetFloat("_BumpScale",.6f);mat.EnableKeyword("_NORMALMAP");
   var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Surface.png");mat.SetTexture("_MetallicGlossMap",mask);mat.SetTexture("_OcclusionMap",mask);mat.SetFloat("_Smoothness",1);mat.SetFloat("_OcclusionStrength",.7f);mat.EnableKeyword("_METALLICSPECGLOSSMAP");mat.EnableKeyword("_OCCLUSIONMAP");EditorUtility.SetDirty(mat);
   var go=new GameObject("SternAtlas");go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;PrefabUtility.SaveAsPrefabAsset(go,Folder+"/SternAtlas.prefab");UnityEngine.Object.DestroyImmediate(go);AssetDatabase.SaveAssets();
   File.WriteAllText(Output+"/import.txt","Imported "+count/3+" triangles; UVs, tangents, three 2048 atlases; iOS ASTC 6x6.\n");
  }
  public static void Capture()
  {
   var live=UnityEngine.Object.FindFirstObjectByType<SeaSick.Ship.ShipMotor>().GetComponentInChildren<ModularShipView>();
   if(!live)throw new Exception("No live modular ship");
   var cfg=live.Current;bool active=live.gameObject.activeSelf;bool enabled=CoasterPbrTrial.Enabled;
   var before=new GameObject("Before PBR comparison");var after=new GameObject("After PBR comparison");
   var cameraGo=new GameObject("PBR comparison camera");var cam=cameraGo.AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;
   var extra=cam.GetUniversalAdditionalCameraData();extra.renderPostProcessing=Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing;
   extra.volumeLayerMask=Camera.main.GetUniversalAdditionalCameraData().volumeLayerMask;extra.antialiasing=AntialiasingMode.None;
   try {
    foreach(var go in new[]{before,after}){go.transform.SetParent(live.transform.parent,false);go.transform.localPosition=live.transform.localPosition;go.transform.localRotation=live.transform.localRotation;go.transform.localScale=live.transform.localScale;}
    CoasterPbrTrial.Enabled=false;before.AddComponent<ModularShipView>().Build(cfg);
    CoasterPbrTrial.Enabled=true;after.AddComponent<ModularShipView>().Build(cfg);
    live.gameObject.SetActive(false);before.SetActive(false);after.SetActive(false);
    var ship=live.GetComponentInParent<SeaSick.Ship.ShipMotor>().transform;var p=ship.position;var f=Vector3.ProjectOnPlane(ship.forward,Vector3.up).normalized;var right=Vector3.Cross(Vector3.up,f);
    foreach(string view in new[]{"detail","sailing"})
    {
     if(view=="detail") {cam.transform.position=p-f*11+right*12+Vector3.up*8;cam.transform.LookAt(p+f*2+Vector3.up*2.2f);cam.fieldOfView=43;}
     else {var back=Quaternion.AngleAxis(25,Vector3.up)*-f;cam.transform.position=p+back*23+Vector3.up*12;cam.transform.LookAt(p-back*7+Vector3.up*1.5f);cam.fieldOfView=55;}
     foreach(bool usePbr in new[]{false,true})
     {
      before.SetActive(!usePbr);after.SetActive(usePbr);
      foreach(var lamp in UnityEngine.Object.FindObjectsByType<CoasterLantern>(FindObjectsSortMode.None))lamp.SendMessage("Update");
      Shot(cam,view=="detail"?1920:1080,view=="detail"?1080:2340,view+"-"+(usePbr?"after":"before"));
     }
    }
   }finally{live.gameObject.SetActive(active);CoasterPbrTrial.Enabled=enabled;UnityEngine.Object.DestroyImmediate(before);UnityEngine.Object.DestroyImmediate(after);UnityEngine.Object.DestroyImmediate(cameraGo);}
   File.WriteAllText(Output+"/capture.txt","Matched Unity camera, world state, time, post-processing and lighting; only stern material/atlas changes.\n");
  }
  static void Shot(Camera cam,int w,int h,string name)
  {
   var rt=RenderTexture.GetTemporary(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=RenderTexture.active;Texture2D tex=null;
   try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+name+".png",tex.EncodeToPNG());}
   finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(tex)UnityEngine.Object.DestroyImmediate(tex);}
  }
 }
}
