using UnityEngine;
using System.Linq;
public static class PaintedMaterialStudy
{
    public static string Main()
    {
        var home=Object.FindObjectsByType<SeaSick.World.Island>().First(x=>x.IsHome);
        var tree=home.GetComponentsInChildren<SeaSick.World.IslandAsset>().First(x=>x.assetId=="Broad");
        var camera=new GameObject("Material study camera").AddComponent<Camera>();
        camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=43;
        try
        {
            camera.transform.position=tree.transform.position+new Vector3(-15,10,-21);
            camera.transform.LookAt(tree.transform.position+Vector3.up*5);
            Shot(camera,"docs/art-direction/painted-material-tree.png");
            camera.transform.position=new Vector3(-103,32,29);camera.transform.LookAt(new Vector3(-48,19,104));
            Shot(camera,"docs/art-direction/painted-material-cliff.png");
        }
        finally { Object.Destroy(camera.gameObject); }
        return "Painted material study captured; tree "+tree.transform.position;
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
