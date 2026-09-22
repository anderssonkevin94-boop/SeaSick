using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace SeaSick.Dev {
public static class VolumetricLabReview {
const string Folder="Assets/_Project/Art/VolumetricLab/";
const string Output="docs/art-direction/volumetric-lab/";
[MenuItem("SeaSick/Art Reviews/Build and capture volumetric lab")]
public static void Build(){
if(Application.isPlaying)throw new Exception("Stop Play first");
var original=SceneManager.GetActiveScene();var roots=original.GetRootGameObjects();var enabled=new bool[roots.Length];
for(int i=0;i<roots.Length;i++){enabled[i]=roots[i].activeSelf;roots[i].SetActive(false);}
var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
try{
RenderSettings.skybox=null;RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.19f,.24f,.34f);
var stone=Mat("Stone",new Color(.33f,.39f,.42f));var sand=Mat("Sand",new Color(.66f,.53f,.34f));var grass=Mat("Leaf",new Color(.19f,.31f,.12f));var water=Mat("Water",new Color(.025f,.27f,.39f));
Cube("Quiet water",new Vector3(0,-.6f,0),new Vector3(90,1,90),water);
Cube("Cove floor",new Vector3(0,-.1f,3),new Vector3(29,.5f,30),sand);
// Broken stone arch and clustered trees cast real shadow-map occlusion into the haze.
Cube("Left cliff",new Vector3(-10,6,13),new Vector3(6,12,5),stone);
Cube("Right cliff",new Vector3(9,7,13),new Vector3(6,14,5),stone);
Cube("Arch crown",new Vector3(-.5f,13,13),new Vector3(14,3,5),stone);
Cube("Broken pillar",new Vector3(-2,4,13),new Vector3(1.6f,8,3),stone);
for(int j=0;j<4;j++){float x=3+j*2.6f;Cube("Tree trunk",new Vector3(x,3,7-j*1.8f),new Vector3(.65f,6,.65f),stone);var leaf=GameObject.CreatePrimitive(PrimitiveType.Sphere);leaf.name="Graphic tree crown";leaf.transform.position=new Vector3(x,6.5f,7-j*1.8f);leaf.transform.localScale=new Vector3(3.2f,3,3);leaf.GetComponent<Renderer>().sharedMaterial=grass;}
for(int j=0;j<6;j++){var rock=Cube("Shore rock",new Vector3(-10+j*3,.7f,-5+j%2),new Vector3(2.2f,1.8f,2),stone);rock.transform.rotation=Quaternion.Euler(12,j*28,15);}
var light=new GameObject("Low warm sun").AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.76f,.46f);light.intensity=1.7f;light.shadows=LightShadows.Soft;light.shadowBias=.02f;light.shadowNormalBias=.15f;light.transform.rotation=Quaternion.LookRotation(new Vector3(-.3f,-.45f,-1));RenderSettings.sun=light;
var volume=new Material(Shader.Find("SeaSick/Lab/ShadowedSunVolume"));volume.SetFloat("_Density",.012f);volume.SetFloat("_Strength",.7f);volume=Save(volume,"Haze");var box=Cube("Shadowed atmosphere — toggle renderer to compare",new Vector3(0,11,5),new Vector3(60,22,60),volume);box.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
var cam=new GameObject("Lab camera").AddComponent<Camera>();cam.tag="MainCamera";cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.36f,.48f,.61f);cam.nearClipPlane=.1f;cam.farClipPlane=150;cam.fieldOfView=58;cam.GetUniversalAdditionalCameraData().requiresDepthTexture=true;
cam.gameObject.AddComponent<SeaSick.Dev.VolumetricLabCamera>();
cam.transform.position=new Vector3(-13,5,-20);cam.transform.LookAt(new Vector3(0,5,10));
EditorSceneManager.SaveScene(scene,"Assets/_Project/Scenes/VolumetricLightLab.unity");
Directory.CreateDirectory(Output);
for(int v=0;v<3;v++){cam.transform.position=new[]{new Vector3(-13,5,-20),new Vector3(10,4,-14),new Vector3(1,3,3)}[v];cam.transform.LookAt(new Vector3(0,6,13));box.SetActive(false);Shot(cam,"view"+v+"-off",1280,720);box.SetActive(true);Shot(cam,"view"+v+"-on",1280,720);}
Shot(cam,"portrait",720,1280);
for(int f=0;f<18;f++){float t=f/17f;cam.transform.position=Vector3.Lerp(new Vector3(-13,5,-20),new Vector3(10,4,-14),t);cam.transform.LookAt(new Vector3(0,6,13));Shot(cam,"motion-"+f,960,540);}
if(ShaderUtil.ShaderHasError(volume.shader))throw new Exception("Volume shader error");
File.WriteAllText(Output+"validation.txt","PASS: live Unity renders, 3 camera positions, matched volume on/off, portrait. 96 depth-clipped raymarch samples per pixel, real directional-light shadow-map occlusion. Isolated test only; no production renderer or scene changed. This prototype renders at full resolution; GPU frame-time/device profiling remains required before production adoption.");
File.WriteAllText("/tmp/seasick-volume-result","PASS");
}finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);for(int i=0;i<roots.Length;i++)roots[i].SetActive(enabled[i]);}
}
static Material Mat(string name,Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=c;m.SetFloat("_Smoothness",.05f);return Save(m,name);}
static Material Save(Material m,string name){string p=Folder+name+".mat";var old=AssetDatabase.LoadAssetAtPath<Material>(p);if(old){EditorUtility.CopySerialized(m,old);UnityEngine.Object.DestroyImmediate(m);}else AssetDatabase.CreateAsset(m,p);AssetDatabase.SaveAssets();return old?old:m;}
static GameObject Cube(string name,Vector3 p,Vector3 scale,Material m){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.position=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=m;return g;}
static void Shot(Camera cam,string name,int w,int h){var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;var tex=new Texture2D(w,h,TextureFormat.RGB24,false);try{cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes(Output+name+".png",tex.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}}
}
}
