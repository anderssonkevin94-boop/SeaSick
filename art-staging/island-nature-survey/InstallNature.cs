using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SeaSick.Terrain;

public static class InstallIslandNature
{
    public static string Install()
    {
        if(Application.isPlaying) throw new Exception("Install outside Play mode");
        const string root="Assets/_Project/Art/AstraPlaytest/NatureIsland2/";
        var material=AssetDatabase.LoadAssetAtPath<Material>(root+"Nature.mat");
        if(material==null)
        {
            material=new Material(Shader.Find("SeaSick/Terrain Vertex Color")) { name="Island 2 Nature" };
            AssetDatabase.CreateAsset(material,root+"Nature.mat");
        }
        material.enableInstancing=true;
        material.SetFloat("_DetailStrength",0); material.SetFloat("_NormalStrength",0);
        material.SetFloat("_StriationStrength",0); material.SetFloat("_PaintedSurface",0);
        material.SetFloat("_CrispTerrain",0); material.SetFloat("_GraphicLight",.35f);
        material.SetFloat("_AuthoredFormLighting",0);
        EditorUtility.SetDirty(material);
        var go=GameObject.Find("Island 2 Nature");
        if(go==null) go=new GameObject("Island 2 Nature");
        var profile=go.GetComponent<IslandNatureProfile>();
        if(profile==null) profile=go.AddComponent<IslandNatureProfile>();
        var so=new SerializedObject(profile);
        so.FindProperty("meshLibrary").objectReferenceValue=AssetDatabase.LoadAssetAtPath<TextAsset>(root+"kit.json");
        so.FindProperty("natureMaterial").objectReferenceValue=material;
        so.ApplyModifiedPropertiesWithoutUndo();
        if(so.FindProperty("meshLibrary").objectReferenceValue==null) throw new Exception("Nature mesh library missing");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(go.scene);EditorSceneManager.SaveScene(go.scene);
        Selection.activeGameObject=go;
        return "Island 2 Nature profile saved in Sea scene. Disable its GameObject before Play to restore the original treatment.";
    }
}
