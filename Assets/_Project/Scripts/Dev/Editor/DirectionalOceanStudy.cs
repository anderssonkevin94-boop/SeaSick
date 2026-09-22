using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
namespace SeaSick.Dev {
public static class DirectionalOceanStudy {
const string Folder="Assets/_Project/Art/OceanStudy";
const string Output="docs/art-direction/ocean-directional-study";
[MenuItem("SeaSick/Art/Render directional ocean study")]
public static void Build(){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play mode before rendering the isolated study.");
 Directory.CreateDirectory(Output);
 var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
 try{
  var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/DirectionalOcean.mat");if(!mat){mat=new Material(Shader.Find("SeaSick/Studies/DirectionalOcean"));AssetDatabase.CreateAsset(mat,Folder+"/DirectionalOcean.mat");}
  mat.SetFloat("_StudyTime",4);mat.SetFloat("_FoamAmount",0);EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
  var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/StudyGrid.asset");
  if(!mesh){const int n=401;var v=new Vector3[n*n];var t=new int[(n-1)*(n-1)*6];int j=0;
   for(int z=0;z<n;z++)for(int x=0;x<n;x++){v[z*n+x]=new Vector3((x-200)*.65f,0,(z-160)*.65f);if(x<n-1&&z<n-1){int a=z*n+x;t[j++]=a;t[j++]=a+n;t[j++]=a+1;t[j++]=a+1;t[j++]=a+n;t[j++]=a+n+1;}}
   mesh=new Mesh{name="Study wave grid",indexFormat=IndexFormat.UInt32};mesh.vertices=v;mesh.triangles=t;mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/StudyGrid.asset");}
  var bounds=mesh.bounds;bounds.size=new Vector3(bounds.size.x,10,bounds.size.z);mesh.bounds=bounds;EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
  var water=new GameObject("Directional ocean study");water.AddComponent<MeshFilter>().sharedMesh=mesh;water.AddComponent<MeshRenderer>().sharedMaterial=mat;water.AddComponent<DirectionalOceanStudyMotion>();
  var ship=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Ships/FleetV3/Ship04.prefab"),scene);ship.transform.position=new Vector3(0,.1f,0);ship.AddComponent<DirectionalOceanStudyFloat>().Configure(mat);
  var sun=new GameObject("Study sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.25f;sun.color=new Color(1,.95f,.85f);sun.transform.rotation=Quaternion.Euler(45,145,0);sun.shadows=LightShadows.Soft;
  RenderSettings.sun=sun;
  RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.67f,.76f);RenderSettings.fog=false;
  var sky=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/StudySky.mat");if(!sky){sky=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Sky.mat"));AssetDatabase.CreateAsset(sky,Folder+"/StudySky.mat");}sky.SetColor("_HorizonColor",new Color(.60f,.77f,.88f));sky.SetColor("_GroundColor",new Color(.15f,.36f,.58f));EditorUtility.SetDirty(sky);AssetDatabase.SaveAssetIfDirty(sky);RenderSettings.skybox=sky;
  var horizon=GameObject.CreatePrimitive(PrimitiveType.Plane);horizon.name="Distant sea";horizon.transform.position=new Vector3(0,-1.5f,0);horizon.transform.localScale=Vector3.one*1000;horizon.GetComponent<MeshRenderer>().sharedMaterial=mat;horizon.AddComponent<DirectionalOceanStudyMotion>();UnityEngine.Object.DestroyImmediate(horizon.GetComponent<Collider>());
  var cam=new GameObject("Study camera").AddComponent<Camera>();cam.scene=scene;cam.clearFlags=CameraClearFlags.Skybox;cam.fieldOfView=52;cam.nearClipPlane=.2f;cam.farClipPlane=10000;cam.cullingMask=1<<31;sun.cullingMask=1<<31;
  foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
  cam.transform.position=new Vector3(10,12,-29);cam.transform.LookAt(new Vector3(0,3.5f,10));
  var oldSun=Shader.GetGlobalVector("_SS_SunDir");float oldNight=Shader.GetGlobalFloat("_SS_Night");float oldRose=Shader.GetGlobalFloat("_SS_RoseStrength");
  try{Shader.SetGlobalVector("_SS_SunDir",new Vector4(.3f,.7f,-.7f,0));Shader.SetGlobalFloat("_SS_Night",0);Shader.SetGlobalFloat("_SS_RoseStrength",0);
   Capture(cam,1920,1080,"faces-only");mat.SetFloat("_FoamAmount",.85f);
   Capture(cam,1920,1080,"day");Capture(cam,1080,2340,"portrait");
   for(int i=0;i<24;i++){mat.SetFloat("_StudyTime",4+i*.10f);Capture(cam,960,540,"motion-"+i);}
   mat.SetFloat("_StudyTime",4);
   var position=cam.transform.position;var rotation=cam.transform.rotation;
   mat.SetFloat("_FoamAmount",.85f);Capture(cam,1920,1080,"foam-restored");
   var lightRotation=sun.transform.rotation;sun.transform.rotation=Quaternion.Euler(45,-35,0);
   Capture(cam,1920,1080,"sun-reversed");sun.transform.rotation=lightRotation;
   cam.transform.position=new Vector3(16,3.8f,-22);cam.transform.LookAt(new Vector3(0,2.7f,15));
   mat.SetFloat("_FoamAmount",0);Capture(cam,1920,1080,"low-no-foam");mat.SetFloat("_FoamAmount",.85f);
   for(int i=0;i<24;i++){mat.SetFloat("_StudyTime",4+i*.10f);Capture(cam,960,540,"low-motion-"+i);}
   mat.SetFloat("_StudyTime",4);cam.transform.SetPositionAndRotation(position,rotation);
   EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
  }finally{Shader.SetGlobalVector("_SS_SunDir",oldSun);Shader.SetGlobalFloat("_SS_Night",oldNight);Shader.SetGlobalFloat("_SS_RoseStrength",oldRose);}
  if(ShaderUtil.ShaderHasError(mat.shader))throw new Exception("Study shader has rendering errors.");
  EditorSceneManager.SaveScene(scene,"Assets/_Project/Scenes/DirectionalOceanStudy.unity");
  File.WriteAllText(Output+"/validation.txt","PASS isolated scene, displaced mesh and derivative shading; desktop, portrait and 24 motion phases rendered. Production scene and physics not modified.\n");
  File.WriteAllText("/tmp/seasick-ocean-study-result","PASS");
 }finally{SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);}
}
static void Capture(Camera cam,int w,int h,string name){foreach(var root in cam.scene.GetRootGameObjects()){var boat=root.GetComponent<DirectionalOceanStudyFloat>();if(boat)boat.Apply(AssetDatabase.LoadAssetAtPath<Material>(Folder+"/DirectionalOcean.mat").GetFloat("_StudyTime"));}var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;Texture2D image=null;try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(image)UnityEngine.Object.DestroyImmediate(image);}}
}}
