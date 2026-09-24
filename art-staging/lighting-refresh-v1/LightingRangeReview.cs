using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class LightingRangeReview
{
    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.StartsWith("/private/tmp/seasick-lighting-review/"))
            throw new Exception("Disposable lighting review project only.");
        string output = Environment.GetEnvironmentVariable("LIGHTING_REVIEW_OUTPUT");
        Directory.CreateDirectory(output);
        QualitySettings.SetQualityLevel(0, true);
        ((UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)QualitySettings.renderPipeline).renderScale=1;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientProbe = new SphericalHarmonicsL2();
        var camera = new GameObject("Review camera").AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 14;
        camera.transform.position = new Vector3(0, 30, 0);
        camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.allowHDR = false; camera.allowMSAA = false;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = Vector3.one * 3;
        var mesh = UnityEngine.Object.Instantiate(floor.GetComponent<MeshFilter>().sharedMesh);
        mesh.colors = Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray();
        floor.GetComponent<MeshFilter>().sharedMesh = mesh;
        var lamp = new GameObject("Range 10m lamp").AddComponent<Light>();
        lamp.type = LightType.Point; lamp.range = 10; lamp.intensity = 1.6f;
        lamp.color = new Color(1,.6f,.25f); lamp.transform.position = new Vector3(0,2,0);
        var report = new StringBuilder();
        foreach (string name in new[] { "SeaSick/Terrain Vertex Color", "SeaSick/Environment Toon" })
        {
            var shader = Shader.Find(name);
            if (shader == null || !shader.isSupported) throw new Exception("Unsupported shader: " + name);
            var material = new Material(shader);
            if (material.HasProperty("_DetailStrength")) material.SetFloat("_DetailStrength", 0);
            if (material.HasProperty("_NormalStrength")) material.SetFloat("_NormalStrength", 0);
            floor.GetComponent<Renderer>().sharedMaterial = material;
            var rt = new RenderTexture(512,512,24); rt.Create(); camera.targetTexture = rt;
            camera.Render();
            var tex = new Texture2D(512,512,TextureFormat.RGB24,false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0,0,512,512),0,0); tex.Apply();
            string id = name.Contains("Terrain") ? "terrain" : "environment";
            File.WriteAllBytes(Path.Combine(output,id+"-night-falloff.png"),tex.EncodeToPNG());
            float peak = tex.GetPixel(256,256).maxColorComponent;
            float edgeJump = 0;
            // The physical light sphere reaches the ground at sqrt(10^2-2^2).
            int boundary = Mathf.RoundToInt(256 + Mathf.Sqrt(96) / 28 * 512);
            for (int x=boundary-5;x<=boundary+5;x++)
                edgeJump = Mathf.Max(edgeJump, Mathf.Abs(tex.GetPixel(x,256).r-tex.GetPixel(x-1,256).r));
            if (peak < .1f) throw new Exception("Point light did not render: " + name);
            if (edgeJump > .035f) throw new Exception("Abrupt range boundary: " + name + " jump="+edgeJump);
            var errors = ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
            if (errors.Length>0) throw new Exception(string.Join("\n",errors.Select(m=>m.message)));
            report.AppendLine("PASS " + name + " peak="+peak+" max boundary step="+edgeJump);
            camera.targetTexture=null; RenderTexture.active=null;
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(material);
        }
        // Adjacent terrain chunks must agree even with more than four lamps.
        var seamMaterial = new Material(Shader.Find("SeaSick/Terrain Vertex Color"));
        seamMaterial.SetFloat("_DetailStrength",0); seamMaterial.SetFloat("_NormalStrength",0);
        floor.GetComponent<Renderer>().sharedMaterial=seamMaterial;
        floor.transform.localScale=new Vector3(1.5f,1,3); floor.transform.position=new Vector3(-7.5f,0,0);
        var other=UnityEngine.Object.Instantiate(floor); other.transform.position=new Vector3(7.5f,0,0);
        lamp.enabled=false;
        for(int j=0;j<6;j++)
        {
            var l=new GameObject("Overlap lamp "+j).AddComponent<Light>();
            l.type=LightType.Point; l.range=10; l.intensity=.22f;
            l.color=new Color(1,.6f,.25f); l.transform.position=new Vector3(j%2==0?-4:4,2,-5+j*2);
        }
        var seamRT=new RenderTexture(512,512,24);seamRT.Create();camera.targetTexture=seamRT;camera.Render();
        var seamTex=new Texture2D(512,512,TextureFormat.RGB24,false);
        RenderTexture.active=seamRT;seamTex.ReadPixels(new Rect(0,0,512,512),0,0);seamTex.Apply();
        float seamStep=0;
        for(int y=180;y<330;y++) seamStep=Mathf.Max(seamStep,Mathf.Abs(seamTex.GetPixel(255,y).r-seamTex.GetPixel(256,y).r));
        File.WriteAllBytes(Path.Combine(output,"six-lamp-chunk-seam.png"),seamTex.EncodeToPNG());
        var standard=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        floor.GetComponent<Renderer>().sharedMaterial=standard;other.GetComponent<Renderer>().sharedMaterial=standard;
        foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if(l.enabled)l.intensity*=20;
        camera.Render();RenderTexture.active=seamRT;seamTex.ReadPixels(new Rect(0,0,512,512),0,0);seamTex.Apply();
        File.WriteAllBytes(Path.Combine(output,"standard-lit-control.png"),seamTex.EncodeToPNG());
        if(seamStep>.035f) throw new Exception("Six-lamp terrain chunk seam: "+seamStep);
        report.AppendLine("PASS mobile six-lamp adjacent chunks max seam step="+seamStep);
        File.WriteAllText(Path.Combine(output,"review.txt"),report.ToString());
        Debug.Log(report.ToString());
        EditorApplication.Exit(0);
    }
}
