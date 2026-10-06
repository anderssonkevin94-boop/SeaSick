using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class RunSailingHudPlay
{
    public static void Batch()
    {
        System.IO.Directory.CreateDirectory("Logs/sailing-probe-save");
        SeaSick.Save.SaveSlots.DirectoryOverride=System.IO.Path.GetFullPath("Logs/sailing-probe-save");
        PortraitGameView.Execute();
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sea.unity");
        SessionState.SetBool("SailingProbe",true); EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod] static void Register() { EditorApplication.playModeStateChanged+=State; }
    static double started;
    static void State(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("SailingProbe",false))return;
        SessionState.SetBool("SailingProbe",false); SailingHudPlayProbe.Result=null;
        SailingHudPlayProbe.SetPhone=phone=>{if(phone)PortraitGameView.Execute();else DesktopGameView.Execute();};
        SeaSick.Save.GameBoot.Skip(); new GameObject("Sailing probe").AddComponent<SailingHudPlayProbe>();
        started=EditorApplication.timeSinceStartup; EditorApplication.update+=Poll;
    }
    static void Poll()
    {
        var result=SailingHudPlayProbe.Result;
        if(result==null && EditorApplication.timeSinceStartup-started<180)return;
        result=result??"FAIL timeout";System.IO.Directory.CreateDirectory("Logs");System.IO.File.WriteAllText("Logs/sailing-check.txt",result);
        Debug.Log("[SailingCheck] "+result);SeaSick.Save.SaveSlots.DirectoryOverride=null;EditorApplication.update-=Poll;EditorApplication.Exit(result.StartsWith("PASS")?0:1);
    }
}
