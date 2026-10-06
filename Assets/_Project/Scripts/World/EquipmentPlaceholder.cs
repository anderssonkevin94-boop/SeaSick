using System.Collections.Generic;
using UnityEngine;
namespace SeaSick.World
{
    /// Replace any placeholder with Resources/Equipment/<item-id>.prefab.
    /// True body metres, +Y up, +Z forward. Sword origin is its grip;
    /// armor origins are attachment centres; paired legs use Left/Right children.
    public static class EquipmentPlaceholder
    {
        static readonly Dictionary<string,Material> materials = new();
        public static GameObject Create(string id,Transform parent)
        {
            var prefab=Resources.Load<GameObject>("Equipment/"+id);
            if(prefab!=null)
            {
                var authored=Object.Instantiate(prefab,parent,false);
                foreach(var col in authored.GetComponentsInChildren<Collider>(true))Object.Destroy(col);
                return authored;
            }
            var root=new GameObject("Equipment_"+id);root.transform.SetParent(parent,false);
            bool iron=id.StartsWith("Iron");
            Material main=Mat(iron?"iron":"leather",iron?new Color(.48f,.57f,.62f):new Color(.40f,.25f,.12f));
            Material dark=Mat("dark",new Color(.16f,.12f,.09f));
            if(id=="IronSword")
            {
                Part(root.transform,"Blade",PrimitiveType.Cube,new Vector3(0,.43f,0),new Vector3(.1f,.78f,.04f),main);
                Part(root.transform,"Guard",PrimitiveType.Cube,Vector3.zero,new Vector3(.3f,.06f,.08f),main);
                Part(root.transform,"Grip",PrimitiveType.Cube,new Vector3(0,-.11f,0),new Vector3(.065f,.2f,.065f),dark);
            }
            else if(VillagerEquipment.IsShield(id))
            {
                var disc=Part(root.transform,"Shield",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.50f,.025f,.50f),main);
                disc.localRotation=Quaternion.Euler(90,0,0);
                Part(root.transform,"Boss",PrimitiveType.Sphere,new Vector3(0,0,.04f),new Vector3(.18f,.18f,.09f),dark);
            }
            else if(id.EndsWith("Helmet"))
            {
                // A low cap, with an open face and a broad rim.
                Part(root.transform,"Cap",PrimitiveType.Sphere,Vector3.zero,new Vector3(.58f,.27f,.55f),main);
                Part(root.transform,"Rim",PrimitiveType.Cube,new Vector3(0,-.06f,0),new Vector3(.61f,.07f,.56f),dark);
            }
            else if(id=="LeatherVest" || id=="IronArmor")
            {
                Part(root.transform,"Torso",PrimitiveType.Cube,Vector3.zero,new Vector3(.48f,.38f,.36f),main);
                Part(root.transform,"Belt",PrimitiveType.Cube,new Vector3(0,-.155f,.01f),new Vector3(.50f,.05f,.38f),dark);
            }
            else if(id.EndsWith("Pants") || id.EndsWith("Boots"))
            {
                bool boots=id.EndsWith("Boots");
                foreach(var side in new[]{"Left","Right"})
                {
                    var leg=new GameObject(side).transform;leg.SetParent(root.transform,false);
                    Part(leg,"Shell",PrimitiveType.Cube,Vector3.zero,boots?new Vector3(.19f,.14f,.30f):new Vector3(.19f,.25f,.26f),main);
                }
            }
            return root;
        }
        static Transform Part(Transform root,string name,PrimitiveType type,Vector3 at,Vector3 scale,Material mat)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root,false);go.transform.localPosition=at;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=mat;return go.transform;
        }
        static Material Mat(string key,Color colour)
        {
            if(materials.TryGetValue(key,out var mat) && mat!=null)return mat;
            var shader=Shader.Find("SeaSick/Environment Toon") ?? Shader.Find("Universal Render Pipeline/Lit");
            mat=new Material(shader);mat.name="Equipment placeholder "+key;mat.SetColor("_BaseColor",colour);
            if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",.12f);
            materials[key]=mat;return mat;
        }
    }
}
