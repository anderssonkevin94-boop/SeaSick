using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace SeaSick.Ship.Editor
{
    public static class ShipWaterInstall
    {
        [Serializable] class Kit {public Part[] parts;}
        [Serializable] class Part {public string name;public float[] vertices,normals,colors;public int[] triangles;}
        [MenuItem("SeaSick/Art/Install ship water effects")]
        public static void Install()
        {
            const string dir="Assets/_Project/Resources/ShipWater/";
            Directory.CreateDirectory(dir);
            var kit=JsonUtility.FromJson<Kit>(File.ReadAllText("Assets/_Project/Art/ShipWater/ship-water-meshes.json"));
            foreach(var part in kit.parts){
                int n=part.vertices.Length/3;var v=new Vector3[n];var normal=new Vector3[n];var colors=new Color[n];
                for(int i=0;i<n;i++){v[i]=new Vector3(part.vertices[i*3],part.vertices[i*3+1],part.vertices[i*3+2]);normal[i]=new Vector3(part.normals[i*3],part.normals[i*3+1],part.normals[i*3+2]);colors[i]=new Color(part.colors[i*4],part.colors[i*4+1],part.colors[i*4+2],part.colors[i*4+3]);}
                string name=part.name=="EXPORT_WaterGlob"?"WaterGlob":part.name=="EXPORT_ChunkyJet"?"Jet0":"Jet"+part.name.Substring(part.name.Length-1);
                var mesh=new Mesh{name=name};mesh.vertices=v;mesh.normals=normal;mesh.colors=colors;mesh.triangles=part.triangles;mesh.RecalculateBounds();
                var old=AssetDatabase.LoadAssetAtPath<Mesh>(dir+name+".asset");
                if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);}else AssetDatabase.CreateAsset(mesh,dir+name+".asset");
            }
            if(!AssetDatabase.LoadAssetAtPath<ShipWaterSettings>(dir+"Settings.asset"))AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ShipWaterSettings>(),dir+"Settings.asset");
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("Ship water resources installed; PaddleDrive.Configure installs effects on spawn and refit.");
        }
        public static string SelfCheck()
        {
            var k=new Steamer.HullFormData{stations=new[]{new Steamer.HullFormStation{z=0,keelY=-1,deckY=1,y=new[]{-1f,0f,1f},halfBreadth=new[]{0f,1f,2f}},new Steamer.HullFormStation{z=2,keelY=-1,deckY=1,y=new[]{-1f,0f,1f},halfBreadth=new[]{0f,2f,3f}}}};
            float keel,deck;float breadth=ShipWaterEffects.HalfBreadth(k,1,.5f,out keel,out deck);
            if(Mathf.Abs(breadth-2)>.0001f)throw new Exception("Hull waterline interpolation failed: "+breadth);
            for(int i=0;i<4;i++){var m=Resources.Load<Mesh>("ShipWater/Jet"+i);if(!m||m.vertexCount==0)throw new Exception("Missing jet "+i);}
            if(!Resources.Load<Mesh>("ShipWater/WaterGlob")||!Resources.Load<ShipWaterSettings>("ShipWater/Settings"))throw new Exception("Missing water assets");
            var shader=Resources.Load<Shader>("ShipWater/ShipWater");if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Ship water shader error");
            return "PASS: hull waterline interpolation, four jet variants, droplet, settings, shader.";
        }
    }
}
