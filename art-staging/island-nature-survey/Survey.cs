using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using SeaSick.World;
using SeaSick.Terrain;

public static class IslandNatureSurvey
{
    const string Output = "art-staging/island-nature-survey";
    public static string Prepare()
    {
        if (!Application.isPlaying || !SeaSick.Save.SaveGame.Suppressed || !SeaSick.Save.SaveGame.LastRestoreOk || SeaSick.Save.SaveGame.Restoring)
            throw new Exception("Requires completed restore with autosave protection.");
        Time.timeScale = 0;
        var island = Island.All.OrderBy(i => Vector3.SqrMagnitude(i.transform.position - new Vector3(660,0,81))).First();
        var camera = new GameObject("Nature Survey Camera").AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = island.MaxRadius * 1.12f;
        camera.farClipPlane = 5000;
        camera.transform.position = new Vector3(660,700,81);
        camera.transform.rotation = Quaternion.Euler(90,0,0);
        UnityEngine.Object.FindFirstObjectByType<TerrainStreamer>().target = camera.transform;
        var ocean = UnityEngine.Object.FindFirstObjectByType<SeaSick.Ocean.OceanClipmap>();
        if (ocean) ocean.FollowOverride = camera.transform;
        var sky = UnityEngine.Object.FindFirstObjectByType<SkyDirector>();
        if (sky) {
            var so = new SerializedObject(sky);
            so.FindProperty("pinTime").floatValue = .38f;
            so.FindProperty("forceStorm").floatValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            sky.ForceApply();
        }
        Directory.CreateDirectory(Output);
        var report = new System.Text.StringBuilder();
        report.AppendLine("Phone snapshot survey. Save unchanged. No nature assets imported.");
        report.AppendLine(island.name + " centre=" + island.transform.position + " maxRadius=" + island.MaxRadius);
        report.AppendLine("Terrain samples x,z,height:");
        for (int z=-120; z<=280; z+=20)
            for (int x=440; x<=880; x+=20)
                report.AppendLine(x+","+z+","+Island.TerrainHeight(x,z));
        File.WriteAllText(Output+"/terrain-survey.txt",report.ToString());
        return report.ToString().Split('\n')[1];
    }
    public static string Capture(string name)
    {
        var camera = GameObject.Find("Nature Survey Camera").GetComponent<Camera>();
        var rt = RenderTexture.GetTemporary(1600,1400,24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try {
            camera.aspect = 1600f/1400;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            image = new Texture2D(1600,1400,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1600,1400),0,0); image.Apply();
            File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
        } finally {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if(image) UnityEngine.Object.DestroyImmediate(image);
        }
        return Path.GetFullPath(Output+"/"+name+".png");
    }
    public static string Settlement()
    {
        var camera = GameObject.Find("Nature Survey Camera").GetComponent<Camera>();
        camera.orthographicSize = 80;
        camera.transform.position = new Vector3(650,155,290);
        camera.transform.LookAt(new Vector3(650,8,140));
        return "Settlement view framed from northern shore";
    }
}
