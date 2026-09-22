using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace SeaSick.Dev
{
    public static class AdventureSkyImport
    {
        const string TexturePath="Assets/_Project/Art/SkyboxB/GraphicAdventureDay.png";
        const string MaterialPath="Assets/_Project/Art/Sky.mat";
        const string Out="docs/art-direction/skybox-b-unity";
        [MenuItem("SeaSick/Art/Import graphic adventure sky")]
        public static void Execute()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Sky import requires edit mode.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
            importer.alphaSource=TextureImporterAlphaSource.None;importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Bilinear;importer.mipmapEnabled=true;importer.maxTextureSize=2048;
            importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var mat=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(!mat || mat.shader.name!="SeaSick/Sky" || ShaderUtil.ShaderHasError(mat.shader))throw new Exception("Existing sky shader missing or failed compilation.");
            mat.SetTexture("_AdventurePanorama",AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            mat.SetFloat("_AdventureStrength",1);mat.SetFloat("_AdventureRotation",0);mat.SetFloat("_AdventureSeamWidth",.025f);
            EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
            Validate();
        }
        [MenuItem("SeaSick/Art/Validate graphic adventure sky")]
        public static void Validate()
        {
            if(Application.isPlaying)throw new Exception("Sky validation requires edit mode.");
            Directory.CreateDirectory(Out);
            var source=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(!source || !source.GetTexture("_AdventurePanorama"))throw new Exception("Sky texture not bound.");
            var savedSky=RenderSettings.skybox;var savedSun=Shader.GetGlobalVector("_SS_SunDir");var savedMoon=Shader.GetGlobalVector("_SS_MoonDir");
            float savedNight=Shader.GetGlobalFloat("_SS_Night"),savedRose=Shader.GetGlobalFloat("_SS_RoseStrength");
            var scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Sky validation camera");SceneManager.MoveGameObjectToScene(go,scene);
            var cam=go.AddComponent<Camera>();cam.scene=scene;cam.clearFlags=CameraClearFlags.Skybox;cam.cullingMask=0;cam.fieldOfView=65;
            cam.transform.rotation=Quaternion.Euler(-12,20,0);var mat=new Material(source);RenderSettings.skybox=mat;
            var report=new System.Text.StringBuilder("Sky integration: shader render and image comparisons\n");
            try
            {
                Shader.SetGlobalFloat("_SS_RoseStrength",0);
                Shader.SetGlobalVector("_SS_MoonDir",new Vector4(0,.6f,.8f,0));
                foreach(string state in new[]{"day","storm","night","dusk"})
                {
                    float elevation=state=="night"?-.8f:state=="dusk"?-.04f:.7f;
                    Shader.SetGlobalVector("_SS_SunDir",new Vector4(.3f,elevation,-.7f,0));Shader.SetGlobalFloat("_SS_Night",state=="night"?1:0);
                    mat.SetFloat("_Overcast",state=="storm"?.95f:.08f);mat.SetFloat("_Scud",state=="storm"?.6f:0);
                    mat.SetColor("_ZenithColor",state=="night"?new Color(.015f,.025f,.05f):state=="storm"?new Color(.12f,.15f,.17f):new Color(.17f,.38f,.66f));
                    mat.SetColor("_HorizonColor",state=="night"?new Color(.03f,.04f,.06f):state=="storm"?new Color(.25f,.28f,.30f):new Color(.68f,.80f,.88f));
                    mat.SetFloat("_AdventureStrength",0);var baseline=Render(cam,960,540);
                    mat.SetFloat("_AdventureStrength",1);var updated=Render(cam,960,540);
                    var a=baseline.GetPixels32();var b=updated.GetPixels32();double diff=0;
                    for(int i=0;i<a.Length;i++)diff+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
                    diff/=a.Length*3;
                    if(state=="day"&&diff<2)throw new Exception("Daytime artwork did not render.");
                    if(state!="day"&&diff>.01)throw new Exception(state+" sky changed unexpectedly: "+diff);
                    File.WriteAllBytes(Out+"/"+state+".png",updated.EncodeToPNG());report.AppendLine($"PASS {state}: old/new mean difference {diff:F6}/255");
                    UnityEngine.Object.DestroyImmediate(baseline);UnityEngine.Object.DestroyImmediate(updated);
                    if(state=="day"){var portrait=Render(cam,1080,2340);File.WriteAllBytes(Out+"/day-portrait.png",portrait.EncodeToPNG());UnityEngine.Object.DestroyImmediate(portrait);}
                }
                if(ShaderUtil.ShaderHasError(source.shader))throw new Exception("Sky shader compilation failed.");
                report.AppendLine("PASS shader compiled; panorama bound; day/portrait rendered; storm/night/dusk preserved.");
                File.WriteAllText(Out+"/validation.txt",report.ToString());Debug.Log("[AdventureSky] PASS");
            }
            finally
            {
                RenderSettings.skybox=savedSky;Shader.SetGlobalVector("_SS_SunDir",savedSun);Shader.SetGlobalVector("_SS_MoonDir",savedMoon);
                Shader.SetGlobalFloat("_SS_Night",savedNight);Shader.SetGlobalFloat("_SS_RoseStrength",savedRose);
                UnityEngine.Object.DestroyImmediate(mat);EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        [MenuItem("SeaSick/Art/Capture live graphic adventure sky")]
        public static void LiveCheck()
        {
            if(!Application.isPlaying)throw new Exception("Live check requires Play mode.");
            var mat=RenderSettings.skybox;
            if(!mat || mat.shader.name!="SeaSick/Sky" || !mat.GetTexture("_AdventurePanorama") || mat.GetFloat("_AdventureStrength")<.99f)
                throw new Exception("SkyDirector did not preserve the authored sky layer.");
            var source=Camera.main;var go=new GameObject("Sky integration review camera");
            try
            {
                var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;
                cam.transform.position=source.transform.position;cam.transform.rotation=Quaternion.Euler(-12,source.transform.eulerAngles.y,0);
                var image=Render(cam,1920,1080);Directory.CreateDirectory(Out);File.WriteAllBytes(Out+"/live.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                File.WriteAllText(Out+"/live-validation.txt",$"PASS live SkyDirector material retains GraphicAdventureDay texture and strength 1. Shader: {mat.shader.name}; overcast: {mat.GetFloat("_Overcast")}; sun direction: {Shader.GetGlobalVector("_SS_SunDir")}; night: {Shader.GetGlobalFloat("_SS_Night")}\n");
            }
            finally{UnityEngine.Object.Destroy(go);}
            Debug.Log("[AdventureSky] live controller PASS");
        }
        static Texture2D Render(Camera camera,int w,int h)
        {
            var rt=new RenderTexture(w,h,24);var old=RenderTexture.active;camera.targetTexture=rt;camera.aspect=(float)w/h;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);return image;
        }
    }
}
