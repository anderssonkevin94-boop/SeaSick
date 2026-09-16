using UnityEngine;
using System.Linq;
public static class CliffCharacterStudy
{
    public static string Main()
    {
        var ground=GameObject.Find("Home Plateau — Connected Ground").transform;
        var camera=new GameObject("Material study camera").AddComponent<Camera>();
        camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=43;
        try
        {
            camera.transform.position=ground.TransformPoint(new Vector3(-103,31,-14));
            camera.transform.LookAt(ground.TransformPoint(new Vector3(-63,13,38)));
            Shot(camera,"docs/art-direction/cliff-character-close.png");
            camera.transform.position=ground.TransformPoint(new Vector3(-118,12,-55));
            camera.transform.LookAt(ground.TransformPoint(new Vector3(-60,14,39)));
            Shot(camera,"docs/art-direction/cliff-character-ship.png");
        }
        finally { Object.Destroy(camera.gameObject); }
        return "Cliff character close-up and ship-height views captured.";
    }
    static void Shot(Camera camera,string path)
    {
        var rt=new RenderTexture(1600,1000,24);var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        var active=RenderTexture.active;
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());}
        finally {camera.targetTexture=null;RenderTexture.active=active;rt.Release();Object.Destroy(rt);Object.Destroy(tex);}
    }
}
