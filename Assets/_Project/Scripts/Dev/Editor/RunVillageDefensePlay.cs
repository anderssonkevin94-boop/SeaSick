using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class RunVillageDefensePlay
{
    public static void Batch() {
        VillageDefenseCheck.Run();
        EquipmentCraftCheck.Run();
        PortraitGameView.Execute();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool("DefenseProbe",true);
        EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod] static void Register() { EditorApplication.playModeStateChanged+=State; }
    static void State(PlayModeStateChange state) {
        if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("DefenseProbe",false)) return;
        SessionState.SetBool("DefenseProbe",false);
        VillageDefensePlayProbe.Result=null;
        VillageDefensePlayProbe.SetPhone = phone => { if(phone) PortraitGameView.Execute(); else DesktopGameView.Execute(); };
        new GameObject("Defense smoke test").AddComponent<VillageDefensePlayProbe>();
        started=EditorApplication.timeSinceStartup;
        EditorApplication.update+=Poll;
    }
    static double started;
    static void Poll() {
        string result=VillageDefensePlayProbe.Result;
        if(result==null && EditorApplication.timeSinceStartup-started<60) return;
        result=result??"FAIL: editor probe timeout";
        System.IO.Directory.CreateDirectory("Logs"); System.IO.File.WriteAllText("Logs/defense-play-check.txt",result);
        Debug.Log("[DefensePlay] "+result); EditorApplication.update-=Poll;
        EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
    }
}
