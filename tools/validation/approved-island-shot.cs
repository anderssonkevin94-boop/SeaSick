using UnityEngine;
public static class ApprovedIslandShot
{
    public static string Main()
    {
        var main=Camera.main;
        var go=new GameObject("Island review camera"); var camera=go.AddComponent<Camera>();
        camera.CopyFrom(main);camera.enabled=false;
        go.transform.position=new Vector3(-180,210,-205);go.transform.LookAt(new Vector3(0,20,90));
        camera.fieldOfView=50;camera.farClipPlane=3000;
        Render(camera,1920,1080,"docs/art-direction/approved-island-unity-overview.png");
        Render(main,1080,2340,"docs/art-direction/approved-island-unity-portrait.png");
        Render(main,1920,1080,"docs/art-direction/approved-island-unity-gameplay.png");
        Object.Destroy(go);
        return "Rendered overview, desktop and portrait gameplay cameras; no camera settings changed.";
    }
    static void Render(Camera camera,int width,int height,string path)
    {
        var original=camera.targetTexture;var active=RenderTexture.active;
        var rt=new RenderTexture(width,height,24);var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
        try {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();
            System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());
        } finally { camera.targetTexture=original;RenderTexture.active=active;rt.Release();Object.Destroy(rt);Object.Destroy(tex); }
    }
}
