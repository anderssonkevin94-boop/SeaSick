using UnityEngine;

namespace SeaSick.UI
{
    /// The settings drawer: one tab at the top of the left rail, and behind it
    /// everything that used to be loose on the screen.
    ///
    /// Two sections, because they answer two different questions:
    ///
    ///   HUD      what the player wants to look at. The first real in-game
    ///            options; the rest land here beside them.
    ///   TUNING   the knobs that decide what the game LOOKS like — foam,
    ///            water clarity, islands, camera seats. These were seven
    ///            separate overlays, each with its own corner and its own
    ///            Inspector checkbox, and at the shipping portrait aspect
    ///            four of them drew on top of instruments the player needs.
    ///            They are now a list, one open at a time, drawn inside this
    ///            panel's own rect.
    ///
    /// The tuning section is compiled out of a player build. It is not hidden
    /// behind a flag — the whole section, and the cost of finding the tools,
    /// is `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. A shipping build has a
    /// settings drawer with options in it and nothing else.
    ///
    /// **Drawn with plain `GUI`, not `GUILayout`, on purpose.** `ShipyardPanel`
    /// measured 55 KB of garbage a frame from its layout tree, and a 12 ms GC
    /// pause reads as the sea going choppy. Tool bodies may use `GUILayout` —
    /// they already do, and they are only open while somebody is tuning.
    public class SettingsPanel : MonoBehaviour
    {
        [Tooltip("Open on start. Off is right for anything but working on the drawer itself.")]
        [SerializeField] bool open = false;

        /// So a keyboard shortcut can open the drawer onto its own tool.
        /// There is one drawer; if there are two, the last one enabled wins
        /// and the other still works from its own tab.
        public static SettingsPanel Instance { get; private set; }
        static readonly string[] RowLandTheme = { "Island UI: Classic", "Island UI: Midnight" };

        void OnEnable() => Instance = this;

        /// **It installs itself.** Every tuner in the project now draws inside
        /// this drawer and nowhere else, so a scene without one is a scene
        /// where the tuners cannot be reached — including the three lab scenes
        /// (`OceanLab`, `HullLab`, `TerrainLab`) that were never going to be
        /// remembered. It is HUD chrome with no serialized state worth
        /// authoring, and this project's own record says a scene reference is
        /// a thing that goes stale: `SkyDirector.Instance` was null for two
        /// days because of where its object sat in a hierarchy.
        ///
        /// A drawer placed in a scene by hand still wins — this only runs when
        /// there is not one already.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<SettingsPanel>() != null) return;
            new GameObject("SettingsDrawer").AddComponent<SettingsPanel>();
        }

        /// The attribute fires once per session; a Continue/Load/New reload
        /// (`GameMenus.ReloadForBoot`) destroys the drawer with the scene and
        /// left Home/Pause's SETTINGS button opening nothing. Same fix as
        /// `GameBoot.HookReinstallOnReload` (2026-09-26).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReinstallOnReload()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                                  UnityEngine.SceneManagement.LoadSceneMode m) => Install();

        /// Open the drawer onto its list, no tool selected — what the
        /// Home/Pause menus' SETTINGS button does. They have no tool to hand
        /// it (there may be none in a shipping build) and no way to draw a
        /// callback into an IMGUI drawer, so `GameMenus` polls `IsOpen` and
        /// comes back once this closes on its own.
        public static void Open()
        {
            if (Instance != null) Instance.open = true;
        }

        /// Whether the drawer is currently drawn open. `GameMenus` polls this
        /// to know when to bring its own screen back after handing off to
        /// SETTINGS — the drawer has no close callback of its own.
        public static bool IsOpen => Instance != null && Instance.open;

        /// Open the drawer straight onto one tool — what F1 and G do. A
        /// shortcut that set the tool without opening the drawer would switch
        /// a tuner on with nowhere for it to draw.
        public static void Show(IDevTool tool)
        {
            if (Instance != null) Instance.open = true;
            DevTools.Open = tool;
        }

        /// The shortcut form: open onto this tool, or shut the drawer if it is
        /// already the one showing.
        public static void Toggle(IDevTool tool)
        {
            if (DevTools.Open == tool)
            {
                DevTools.Open = null;
                if (Instance != null) Instance.open = false;
            }
            else Show(tool);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            // A drawer that goes away with a tuner still running leaves the
            // tuner with no way to be switched off. See the SailCamTuner
            // lesson: an idle tool must not be left touching shared state.
            DevTools.CloseAll();
        }

        void OnGUI()
        {
            // The shipyard is a full-screen UI Toolkit modal that OnGUI
            // knows nothing about (2026-09-25 review: the gear chip sat at
            // the left edge over the modal). `GameMenus` gets the same
            // suppression (2026-09-26 review: the gear chip sat over Home)
            // -- EXCEPT when it is `GameMenus` itself that opened this drawer
            // (`OpenSettingsFromMenu`/`SettingsHostedByMenu`): `Current`
            // stays Home/Pause/etc. the whole time that drawer is up, so
            // gating on it unconditionally closed the drawer the instant it
            // opened.
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || (SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None
                    && !SeaSick.UI.Menus.GameMenus.SettingsHostedByMenu))
            { if (open) { open = false; DevTools.CloseAll(); } return; }
            int u = HudLayout.Unit;
            float pad = HudLayout.Pad;

            var tab = HudLayout.Place(HudLayout.Slot.RailSettings,
                                      HudLayout.RailWidth, HudLayout.RailButtonHeight);
            UIBlocker.Block(tab);
            if (GUI.Button(tab, open ? "◀ ⚙" : "⚙ ▶", UITheme.Button))
            {
                open = !open;
                if (!open) DevTools.CloseAll();
            }
            if (!open) return;

            // Wide enough for a tuner's sliders when one is open, narrow
            // enough to leave the sea visible when it is just a list.
            bool tooling = DevTools.Open != null;
            var safe = HudLayout.Safe;
            float w = Mathf.Min(safe.width - pad * 2f, u * (tooling ? 30f : 22f));
            // Below the WHOLE rail, so the Yard and Home tabs stay reachable
            // with the drawer open. Under its own tab is where the Yard panel
            // used to open, and it sat on the tab beneath it.
            float top = HudLayout.RailPanelTop;
            // Stops above the helm and the broadside buttons rather than at
            // the bottom of the screen: the drawer is not allowed to cover the
            // controls that get the ship out of trouble.
            float h = Mathf.Max(u * 10f, HudLayout.BottomClustersTop - top - HudLayout.Gap);

            var panel = new Rect(safe.x + pad, top, w, h);
            // Push anything centred (the prompts, a toast) clear of this panel
            // for as long as it is open.
            HudLayout.ClaimLeftPanel(panel);
            UITheme.Rect(panel, UITheme.PanelSolid);
            UITheme.Rect(new Rect(panel.x, panel.y, panel.width, 2f), UITheme.Sea);
            UIBlocker.Block(panel);

            var body = new Rect(panel.x + pad, panel.y + pad,
                                panel.width - pad * 2f, panel.height - pad * 2f);

            if (DevTools.Open != null) DrawOpenTool(body, u);
            else DrawList(body, u);
        }

        /// The list: display options, then the tools.
        void DrawList(Rect body, int u)
        {
            float y = body.y;
            float rowH = u * 2.2f;

            GUI.Label(new Rect(body.x, y, body.width, u * 1.8f), "SETTINGS", UITheme.Strong);
            y += u * 2.6f;

            GUI.Label(new Rect(body.x, y, body.width, u * 1.4f), "HUD", UITheme.Small);
            y += u * 1.8f;

            y = Toggle(body, y, rowH, RowMinimap, HudVisibility.Minimap,
                       v => HudVisibility.Minimap = v);
            y = Toggle(body, y, rowH, RowLandTheme, Sheets.MidnightLandHud.Enabled,
                       v => Sheets.MidnightLandHud.Enabled = v);
            y = Toggle(body, y, rowH, RowCompass, HudVisibility.Compass,
                       v => HudVisibility.Compass = v);
            y = Toggle(body, y, rowH, RowCrew, HudVisibility.Crew,
                       v => HudVisibility.Crew = v);
            y = Toggle(body, y, rowH, RowPerf, HudVisibility.Perf,
                       v => HudVisibility.Perf = v);

            // The playtest save. One button, one file; what it wrote is on
            // the console, and the next launch offers CONTINUE.
            y += u * 0.5f;
            var saveRow = new Rect(body.x, y, body.width, rowH);
            UIBlocker.Block(saveRow);
            if (GUI.Button(saveRow, "SAVE", UITheme.Button))
                saveNote = Save.SaveGame.Save("the settings drawer")
                    ? "saved  " + System.DateTime.Now.ToString("HH:mm:ss")
                    : "could not save -- see the console";
            y += rowH + u * 0.15f;
            GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                      string.IsNullOrEmpty(saveNote)
                          ? (Save.SaveGame.Exists ? "a save exists; CONTINUE is offered on launch"
                                                  : "no save yet")
                          : saveNote,
                      UITheme.Small);
            y += u * 1.6f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            y += u * 1.0f;
            UITheme.Rect(new Rect(body.x, y, body.width, 1f), UITheme.Track);
            y += u * 0.9f;
            GUI.Label(new Rect(body.x, y, body.width, u * 1.4f),
                      "TUNING — editor and dev builds only", UITheme.Small);
            y += u * 1.8f;

            // Straight in the list rather than behind a tuner, because the
            // thing it fixes only exists in a full-sized sea a long way from
            // home, and reaching it means sailing there -- the A/B has to be
            // one thumb away when you arrive. See OceanRenderer.AccumulatePhase.
            y = Toggle(body, y, rowH, RowWavePhase,
                       SeaSick.Ocean.OceanRenderer.AccumulatePhase,
                       v => SeaSick.Ocean.OceanRenderer.AccumulatePhase = v);
            GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                      "off = the old sea, which re-phases on every rebuild",
                      UITheme.Small);
            y += u * 1.6f;

            y = Toggle(body, y, rowH, RowShoreward,
                       SeaSick.Ocean.RegionField.ShorewardEnabled,
                       v => SeaSick.Ocean.RegionField.ShorewardEnabled = v);
            GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                      "off = swell runs past islands with a notch in it",
                      UITheme.Small);
            y += u * 1.6f;

            y = Toggle(body, y, rowH, RowStorm,
                       SeaSick.Ocean.RegionField.WanderingStorm,
                       v => SeaSick.Ocean.RegionField.WanderingStorm = v);
            GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                      "off = the storm sits at a fixed distance west, forever",
                      UITheme.Small);
            y += u * 1.6f;

            var tools = DevTools.All;
            if (tools.Count == 0)
            {
                GUI.Label(new Rect(body.x, y, body.width, u * 1.4f),
                          "no tuners in this scene", UITheme.Small);
                return;
            }

            for (int i = 0; i < tools.Count; i++)
            {
                var t = tools[i];
                if (t == null) continue;
                var r = new Rect(body.x, y, body.width, rowH);
                if (y + rowH > body.yMax) break;   // the drawer does not scroll off its own panel
                UIBlocker.Block(r);
                if (GUI.Button(r, t.ToolName, UITheme.Button)) DevTools.Open = t;
                y += rowH + u * 0.15f;
                GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                          t.ToolBlurb, UITheme.Small);
                y += u * 1.6f;
            }
#endif
        }

        /// One tool, with the way back out at the top where a thumb expects it.
        void DrawOpenTool(Rect body, int u)
        {
            var t = DevTools.Open;
            float head = u * 2.2f;
            var back = new Rect(body.x, body.y, u * 5.0f, head);
            UIBlocker.Block(back);
            if (GUI.Button(back, "◀ back", UITheme.Button)) DevTools.Open = null;

            GUI.Label(new Rect(back.xMax + u * 0.6f, body.y, body.width - back.width - u * 0.6f, head),
                      t.ToolName, UITheme.Body);

            var toolBody = new Rect(body.x, back.yMax + HudLayout.Gap,
                                    body.width, body.yMax - back.yMax - HudLayout.Gap);
            UIBlocker.Block(toolBody);
            t.DrawTool(toolBody);
        }

        string saveNote = "";

        /// Each row's two faces, { off, on }, built at load.
        ///
        /// `(value ? "◉  " : "◎  ") + label` is a fresh string every time it
        /// runs, and it ran four times per IMGUI EVENT — Layout, Repaint and
        /// one more per mouse move — to say four fixed things. A row has
        /// exactly two readings; there is no reason to build either twice.
        static readonly string[] RowMinimap = { "◎  minimap & wind", "◉  minimap & wind" };
        static readonly string[] RowCompass = { "◎  compass tape", "◉  compass tape" };
        static readonly string[] RowCrew = { "◎  crew", "◉  crew" };
        static readonly string[] RowPerf = { "◎  performance readout", "◉  performance readout" };
        static readonly string[] RowWavePhase = { "◎  smooth wave phase", "◉  smooth wave phase" };
        static readonly string[] RowShoreward = { "◎  shoreward band", "◉  shoreward band" };
        static readonly string[] RowStorm = { "◎  wandering storm", "◉  wandering storm" };

        /// A row that reads as on or off without a checkbox glyph: the pressed
        /// style IS the state, the same way the oars and ease-her buttons at
        /// the helm already work.
        static float Toggle(Rect body, float y, float rowH, string[] faces,
                            bool value, System.Action<bool> set)
        {
            var r = new Rect(body.x, y, body.width, rowH);
            UIBlocker.Block(r);
            var style = value ? UITheme.ButtonPressed : UITheme.Button;
            if (GUI.Button(r, faces[value ? 1 : 0], style)) set(!value);
            return y + rowH + HudLayout.Unit * 0.35f;
        }
    }
}
