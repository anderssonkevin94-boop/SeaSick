using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;
namespace SeaSick.Dev
{
    public static class WaterArtReview
    {
        const string Output="docs/art-direction/painted-ocean-v2";
        const string MaterialPath="Assets/_Project/Materials/GraphicArt/OceanSurface.mat";
        static void Configure(Material m)
        {
            m.SetFloat("_PaintedStrength",1);
            m.SetFloat("_SurfaceDetail",.62f);
            m.SetFloat("_ReflectionStrength",.38f);
            m.SetFloat("_GraphicLight",.85f);
            m.SetFloat("_SpecStrength",.65f);
            m.SetFloat("_FoamBreakup",.30f);
            m.SetFloat("_FoamEdge",.88f);
            m.SetFloat("_FoamRelief",0);m.SetFloat("_FoamSparkle",0);
            m.SetFloat("_SubsurfaceStrength",.40f);
            m.SetColor("_DeepColor",new Color(.025f,.19f,.44f));
            m.SetColor("_ShallowColor",new Color(.045f,.46f,.70f));
            m.SetColor("_ShoalColor",new Color(.065f,.64f,.60f));
            m.SetColor("_FoamColor",new Color(.98f,.965f,.86f));
        }
        [MenuItem("SeaSick/Art/Apply painted ocean")]
        public static void Apply()
        {
            if(Application.isPlaying)throw new Exception("Apply in edit mode.");
            var m=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(!m||ShaderUtil.ShaderHasError(m.shader))throw new Exception("Ocean shader failed.");
            Directory.CreateDirectory(Output);
            Undo.RecordObject(m,"Painted ocean style");Configure(m);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
            File.WriteAllText("/tmp/seasick-water-result","Applied painted ocean material.");
        }
        public static void Run(string command)
        {
            if(command=="apply"){Apply();return;}
            if(!Application.isPlaying)throw new Exception("Review requires Play mode.");
            var ocean=UnityEngine.Object.FindFirstObjectByType<OceanClipmap>();
            if(!ocean||!ocean.Material)throw new Exception("No live ocean.");
            var m=ocean.Material;var saved=new Material(m);var go=new GameObject("Painted water review camera");
            Directory.CreateDirectory(Output);
            try
            {
                var source=Camera.main;var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;
                cam.transform.SetPositionAndRotation(source.transform.position,Quaternion.Euler(8,source.transform.eulerAngles.y,0));
                if(command=="ocean")cam.transform.SetPositionAndRotation(source.transform.position+Vector3.up*12,Quaternion.Euler(18,source.transform.eulerAngles.y+160,0));
                Capture(cam,1920,1080,command+"-before");Configure(m);Capture(cam,1920,1080,command);
                Capture(cam,1080,2340,command+"-portrait");
                float storm=Shader.GetGlobalFloat("_SS_Storminess"),night=Shader.GetGlobalFloat("_SS_Night");
                try{Shader.SetGlobalFloat("_SS_Storminess",1);Capture(cam,960,540,command+"-storm-palette");Shader.SetGlobalFloat("_SS_Storminess",storm);Shader.SetGlobalFloat("_SS_Night",1);Capture(cam,960,540,command+"-night-palette");}
                finally{Shader.SetGlobalFloat("_SS_Night",night);Shader.SetGlobalFloat("_SS_Storminess",storm);}
                if(ShaderUtil.ShaderHasError(m.shader))throw new Exception("Rendered shader has errors.");
                File.WriteAllText(Output+"/"+command+".txt",$"PASS live {m.shader.name}; material {m.name}; Hs={SeaSick.Ocean.SeaStateController.Instance.CurrentHs}; storm={storm}; night={night}; camera={cam.transform.position}; desktop 1920x1080, portrait 1080x2340. Before/after use same simulation frame. Night and storm images check water palette only, not weather simulation.\n");
                File.WriteAllText("/tmp/seasick-water-result","PASS "+command);
            }
            finally{m.CopyPropertiesFromMaterial(saved);UnityEngine.Object.DestroyImmediate(saved);UnityEngine.Object.DestroyImmediate(go);}
        }
        static void Capture(Camera camera,int w,int h,string name)
        {
            var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;
            Texture2D image=null;
            try{camera.targetTexture=rt;camera.aspect=(float)w/h;camera.Render();RenderTexture.active=rt;image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(image)UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
