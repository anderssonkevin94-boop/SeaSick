using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Save
{
    /// **NEW VOYAGE or CONTINUE, on the first frames.**
    ///
    /// There is no title scene: `Sea.unity` boots straight into sailing and
    /// the world builds itself on frame one. So this is an overlay, not a
    /// menu -- it freezes the game (`Time.timeScale = 0`, which also stops
    /// `TimeOfDay`, whose owner advances it by `deltaTime`), draws two big
    /// buttons over whatever the HUD is doing, and lets the world finish
    /// building behind it. NEW forgets the save and unfreezes; CONTINUE
    /// unfreezes and runs `SaveGame.Restore` over the booted world. With
    /// no save there is one button, so the flow is the same either way.
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

        void Decide(Choice choice, bool forgetSave)
        {
            if (showing)
            {
                showing = false;
                Time.timeScale = frozenScale > 0f ? frozenScale : 1f;
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

        void OnGUI()
        {
            if (!showing)
            {
                if (SaveGame.Restoring) DrawLoading();
                return;
            }

            // On top of every other panel. The dim is a texture, not a
            // control, so the buttons below it still get the mouse; the
            // click-swallower comes LAST, because IMGUI hands an event to
            // the first control drawn that wants it.
            GUI.depth = -1000;
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            UITheme.Rect(full, UITheme.PanelSolid);
            UIBlocker.Block(full);

            int u = HudLayout.Unit;
            var safe = HudLayout.Safe;
            float pad = HudLayout.Pad;

            // Sized for a thumb on a phone and a mouse on a monitor alike:
            // the width follows the unit, which follows the SHORT edge, so
            // it is the same fraction of the screen in both shapes.
            float w = Mathf.Min(safe.width - pad * 2f, u * 22f);
            float bh = u * 3.2f;
            float titleH = u * 3f;
            float lineH = u * 1.6f;
            int rows = haveSave ? 2 : 1;
            float total = titleH + u
                        + (haveSave ? lineH + u * 0.6f : 0f)
                        + rows * bh + (rows - 1) * HudLayout.Gap
                        + u * 1.4f + lineH;
            float y = safe.y + Mathf.Max(pad, (safe.height - total) * 0.42f);
            float x = safe.x + (safe.width - w) * 0.5f;

            GUI.Label(new Rect(x, y, w, titleH), "SEASICK", UITheme.Title);
            y += titleH + u;

            if (haveSave)
            {
                GUI.Label(new Rect(x, y, w, lineH), saveLine, UITheme.Small);
                y += lineH + u * 0.6f;
                var cont = new Rect(x, y, w, bh);
                if (GUI.Button(cont, "CONTINUE", UITheme.Button)) Decide(Choice.Continue, false);
                y += bh + HudLayout.Gap;
            }

            var fresh = new Rect(x, y, w, bh);
            if (GUI.Button(fresh, "NEW VOYAGE", UITheme.Button))
                Decide(Choice.New, true);
            y += bh + u * 1.4f;

            GUI.Label(new Rect(x, y, w, lineH),
                haveSave ? "a new voyage forgets the save" : "no save yet -- SAVE is in the settings drawer",
                UITheme.Small);

            // Everything the buttons did not take, so nothing underneath
            // (the helm, the rail tabs) is pressed through the overlay.
            GUI.Button(full, GUIContent.none, GUIStyle.none);
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
