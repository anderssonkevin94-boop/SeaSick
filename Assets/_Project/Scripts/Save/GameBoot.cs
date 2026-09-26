using SeaSick.UI;
using SeaSick.UI.Menus;
using UnityEngine;

namespace SeaSick.Save
{
    /// **The HOME screen, on the first frames -- and where a reload from it
    /// lands.**
    ///
    /// There is no title scene: `Sea.unity` boots straight into sailing and
    /// the world builds itself on frame one. So this freezes the game
    /// (`Time.timeScale = 0`, which also stops `TimeOfDay`, whose owner
    /// advances it by `deltaTime`) and hands the screen to `GameMenus`
    /// (Continue / New voyage / Load / Settings), which draws over whatever
    /// the HUD is doing while the world finishes building behind it.
    ///
    /// **2026-09-26: Continue, New and every Load-list row now all go through
    /// the same door.** `GameMenus` never restores anything itself -- it
    /// asks `SaveSlots.RequestLoad`/`RequestNewGame` and reloads the scene,
    /// and `HandleSlotRequest` below is what reads that choice back on the
    /// way up, before this class even considers showing the Home screen
    /// again. The one exception is the dev/probe surface (`Forced`,
    /// `Interactive = false`, `Skip()`), which still resolves in place
    /// through `Decide`/`RunRestore` exactly as before -- a probe has no
    /// scene to reload into and no player to show a menu to.
    ///
    /// **It installs itself**, the way `SettingsPanel` does, and only into
    /// a scene with a `VoyageManager` in it: the lab scenes boot straight
    /// in as they always did.
    ///
    /// Three ways round it, for anything that is not a person:
    /// - `Interactive = false` (a dev launcher) -- boots straight into New.
    /// - `Forced` / `-new` / `-continue` on the command line -- decides
    ///   without drawing.
    /// - `Skip()` -- what `RunProbe.Call` invokes: dismisses the overlay as
    ///   New WITHOUT deleting the file, and turns autosaves off for the
    ///   session so a probe's forty anchorings never touch the player's own
    ///   save.
    ///
    /// Statics outlive play mode here (domain reload is off), so the ones
    /// that are per-session are reset in `ResetForPlay`; `Interactive` and
    /// `Forced` are deliberately NOT, because a launcher sets them before
    /// play and expects them kept.
    [DefaultExecutionOrder(-300)]
    public class GameBoot : MonoBehaviour
    {
        public enum Choice { None, New, Continue }

        /// Draw the overlay and wait for a person. True in the player and in
        /// the editor; a dev launcher that wants a bare New sets it false.
        public static bool Interactive = true;

        /// Decide without asking. Set by a launcher or by the command line.
        public static Choice Forced = Choice.None;

        /// The player has chosen (or something chose for them). Nothing
        /// autosaves before this is true.
        public static bool Decided { get; private set; }

        /// What was chosen, for anything that wants to say so.
        public static Choice Chosen { get; private set; }

        public static GameBoot Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Decided = false;
            Chosen = Choice.None;
            Instance = null;
            SaveGame.ResetForPlay();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<GameBoot>() != null) return;
            if (FindAnyObjectByType<Voyage.VoyageManager>() == null) return;
            new GameObject("GameBoot").AddComponent<GameBoot>();
        }

        /// A probe is running: get out of its way, keep the player's file.
        public static void Skip()
        {
            SaveGame.Suppressed = true;
            if (Instance != null && Instance.showing) Instance.Decide(Choice.New, false);
            else if (Instance == null && !Decided) { Decided = true; Chosen = Choice.New; }
        }

        bool showing;
        bool haveSave;
        string saveLine = "";
        float frozenScale = 1f;

        void Awake()
        {
            Instance = this;
            ParseCommandLine();

            // A previous scene -- the Home screen, the Load list, or Pause's
            // New/Save & exit -- already decided this and reloaded to get
            // here. That takes precedence over everything below: a probe's
            // `Forced` flag cannot arrive with a pending slot request too,
            // and a person who just picked Load is done choosing.
            if (!Decided && HandleSlotRequest()) return;

            haveSave = SaveGame.Exists;
            if (haveSave)
            {
                var peek = SaveGame.Read(SaveGame.Path);
                haveSave = peek != null;
                saveLine = haveSave ? SaveGame.Summary(peek) : "";
            }

            if (Decided) return;   // a probe got here first
            if (!Interactive || Forced != Choice.None)
            {
                bool cont = Forced == Choice.Continue && haveSave;
                Decide(cont ? Choice.Continue : Choice.New, Forced == Choice.New);
                return;
            }

            showing = true;
            frozenScale = Time.timeScale;
            Time.timeScale = 0f;
            GameMenus.ShowHome();
        }

        /// `SaveSlots.PendingNewGame` / `PendingLoadSlot`, read once on the
        /// way up. Returns true when either fired, in which case `Awake` is
        /// done -- there is nothing left for the boot overlay to decide.
        ///
        /// **This call is the loading-screen hook** the save-slot contract
        /// asked for: a future progress bar wraps `RestorePending` (or polls
        /// a progress value beside it) rather than anything here, since this
        /// is the one place in the whole boot sequence that already knows
        /// which of the two happened.
        bool HandleSlotRequest()
        {
            if (SaveSlotsAdapter.PendingNewGame)
            {
                Chosen = Choice.New;
                Decided = true;
                Debug.Log("GameBoot: NEW VOYAGE (from the home screen)");
                return true;
            }

            string pending = SaveSlotsAdapter.PendingLoadSlot;
            if (string.IsNullOrEmpty(pending)) return false;

            bool ok = SaveSlotsAdapter.RestorePending(out string error);
            Decided = true;
            Chosen = ok ? Choice.Continue : Choice.New;
            if (!ok) Debug.LogWarning("GameBoot: RestorePending('" + pending + "') failed: " + error);
            return true;
        }

        static void ParseCommandLine()
        {
            if (Forced != Choice.None) return;
            string[] args;
            try { args = System.Environment.GetCommandLineArgs(); }
            catch { return; }
            if (args == null) return;
            foreach (var a in args)
            {
                if (a == "-continue" || a == "--continue") Forced = Choice.Continue;
                else if (a == "-new" || a == "--new") Forced = Choice.New;
            }
        }

        /// The dev/probe path only now -- see the class doc. A real player's
        /// Continue/New/Load all go through `GameMenus` and a scene reload
        /// instead, and never reach this method.
        void Decide(Choice choice, bool forgetSave)
        {
            if (showing)
            {
                showing = false;
                Time.timeScale = frozenScale > 0f ? frozenScale : 1f;
                // `Skip()` calls this out from under an already-showing
                // `GameMenus.Home` (a probe launched mid-boot) -- the overlay
                // it drew has to come down too, or it sits there inert with
                // the world now running underneath it.
                GameMenus.ForceHide();
            }
            if (Decided) return;

            if (choice == Choice.Continue && !haveSave) choice = Choice.New;

            if (choice == Choice.Continue)
            {
                var data = SaveGame.Read(SaveGame.Path);
                if (data == null) choice = Choice.New;
                else
                {
                    Chosen = Choice.Continue;
                    Debug.Log("GameBoot: CONTINUE <- " + SaveGame.Path);
                    StartCoroutine(RunRestore(data));
                    return;
                }
            }

            Chosen = Choice.New;
            if (forgetSave && SaveGame.Exists) SaveGame.Delete();
            Decided = true;
            Debug.Log("GameBoot: NEW VOYAGE" + (forgetSave ? " (the save was forgotten)" : ""));
        }

        System.Collections.IEnumerator RunRestore(SaveData data)
        {
            yield return SaveGame.Restore(data, this);
            // Whatever happened, the player is in a game now. A refused
            // save (another world's seed) has already said why on the
            // console and left the file alone.
            Decided = true;
            if (!SaveGame.LastRestoreOk) Chosen = Choice.New;
        }

        void OnApplicationQuit() => SaveGame.Autosave("quit");
        void OnApplicationPause(bool paused) { if (paused) SaveGame.Autosave("paused"); }

        /// The interactive Home screen itself is `GameMenus.ShowHome` (UI
        /// Toolkit, `Scripts/UI/Menus/HomeScreen.cs`) now -- this only covers
        /// what is left once that overlay is up: the "restoring…" line while
        /// `RunRestore` (the dev/probe Continue path) is mid-flight. A
        /// player's Load, which goes through `RestorePending` instead, has
        /// no such moment in THIS scene -- it happens before this component
        /// even exists, on the way up in `HandleSlotRequest`.
        void OnGUI()
        {
            if (showing) return;
            if (SaveGame.Restoring) DrawLoading();
        }

        void DrawLoading()
        {
            int u = HudLayout.Unit;
            var r = HudLayout.Centred(HudLayout.Safe.y + HudLayout.Pad + u * 3f, u * 16f, u * 2f);
            UITheme.Rect(r, UITheme.Panel);
            GUI.Label(r, "loading the save…", UITheme.Body);
        }
    }
}
