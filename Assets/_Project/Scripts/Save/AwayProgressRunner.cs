using UnityEngine;

namespace SeaSick.Save
{
    /// **Coroutine host + card timing for `AwayProgress`.** Survives scene
    /// reloads. Opens the "while you were away" card once a load's catch-up
    /// is done and nothing covers the screen (restore over, loading screen
    /// faded, no Home/Pause menu).
    public class AwayProgressRunner : MonoBehaviour
    {
        static AwayProgressRunner instance;

        public static AwayProgressRunner Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("AwayProgressRunner");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AwayProgressRunner>();
            return instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() => Get();

        void Update()
        {
            if (!AwayProgress.CardPending || AwayProgress.Running) return;
            if (SaveGame.Restoring || !GameBoot.Decided) return;
            var ls = UI.Menus.LoadingScreen.Instance;
            if (ls != null && !ls.Finished) return;
            if (UI.Menus.GameMenus.Current != UI.Menus.GameMenus.Mode.None) return;
            AwayProgress.CardPending = false;
            if (AwayProgress.Last != null) UI.Sheets.Sheets.Open(new UI.Sheets.AwaySheet());
        }
    }
}
