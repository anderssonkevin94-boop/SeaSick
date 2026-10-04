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

            // **No rail tab while closed (2026-09-27).** The old "⚙ ▶" tab
            // drew as blank boxes on the phone; Settings is reached from the
            // pause menu now (the ledger drawer's gear ashore, the rail's
            // Menu at sea). Open, the tab is its labelled way back out.
            if (!open) return;
            var tab = HudLayout.Place(HudLayout.Slot.RailSettings,
                                      HudLayout.RailWidth, HudLayout.RailButtonHeight);
            UIBlocker.Block(tab);
            if (GUI.Button(tab, "Close", UITheme.Button))
            {
                open = false;
                DevTools.CloseAll();
                return;
            }

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
        void DrawList(Rect view, int u)
        {
            // **Scrolls** (2026-10-03): the Camera group made the list taller
            // than the drawer on a phone. Content height is last frame's; the
            // rows are laid out in the scroll view's own space, so they must
            // not claim `UIBlocker` rects (the drawer panel already claims the
            // whole thing) -- `BlockRow` is silent while `inScroll`.
            float sbw = u * 1.0f;
            var content = new Rect(0f, 0f, view.width - sbw, Mathf.Max(listHeight, view.height));

            // A thumb drags the list itself (IMGUI's scroll view only scrolls
            // by its bar or a wheel). Past a few pixels the drag is a scroll,
            // not a press: the row under the thumb lets go, so lifting off it
            // does not flip a toggle.
            var e = Event.current;
            if (e.type == EventType.MouseDown && view.Contains(e.mousePosition)) dragTravel = 0f;
            if (e.type == EventType.MouseDrag && view.Contains(e.mousePosition))
            {
                dragTravel += Mathf.Abs(e.delta.y);
                if (dragTravel > u * 0.6f)
                {
                    scroll.y = Mathf.Clamp(scroll.y - e.delta.y, 0f, Mathf.Max(0f, content.height - view.height));
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
            }
#if UNITY_EDITOR
            if (e.type == EventType.Repaint) { DevRows.Clear(); DevView = view; DevScrollAtDraw = scroll; DevRowsFrame = Time.frameCount; }
#endif
            scroll = GUI.BeginScrollView(view, scroll, content, false, false,
                                         GUIStyle.none, GUI.skin.verticalScrollbar);
            inScroll = true;
            float endY = DrawListBody(new Rect(0f, 0f, content.width, view.height), u);
            inScroll = false;
            GUI.EndScrollView();
            listHeight = endY + u * 0.5f;
        }

        /// Draws the list rows; returns the y just below the last one.
        float DrawListBody(Rect body, int u)
        {
            float y = body.y;
            float rowH = u * 2.2f;

            GUI.Label(new Rect(body.x, y, body.width, u * 1.8f), "SETTINGS", UITheme.Strong);
            y += u * 2.6f;

            GUI.Label(new Rect(body.x, y, body.width, u * 1.4f), "HUD", UITheme.Small);
            y += u * 1.8f;

            y = Toggle(body, y, rowH, RowLandTheme, Sheets.MidnightLandHud.Enabled,
                       v => Sheets.MidnightLandHud.Enabled = v);
            y = Toggle(body, y, rowH, RowPerf, HudVisibility.Perf,
                       v => HudVisibility.Perf = v);
            // The FEEL button (Dev/FeelLab): Kevin's tuning lab, one tap away
            // here rather than on every screen.
            y = Toggle(body, y, rowH, RowTuningLab, HudVisibility.TuningLab,
                       v => HudVisibility.TuningLab = v);

            y = DrawCameraGroup(body, y, rowH, u);

            // The playtest save. One button, one file; what it wrote is on
            // the console, and the next launch offers CONTINUE.
            y += u * 0.5f;
            var saveRow = new Rect(body.x, y, body.width, rowH);
            BlockRow(saveRow);
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
#if UNITY_EDITOR
            DevRow("tuning header", new Rect(body.x, y, body.width, u * 1.4f), "TUNING — editor and dev builds only", UITheme.Small, false);
#endif
            y += u * 1.8f;

            // The kraken, first in the dev list because the drawer does not
            // scroll (Kevin 2026-10-03: "i want these options to be in
            // settings" -- the floating LIFE panel never showed on his phone).
            // Summoned while the menu is up, it rises once the game resumes.
            float half = (body.width - u * 0.4f) * 0.5f;
            var summon = new Rect(body.x, y, half, rowH);
            var dismiss = new Rect(body.x + half + u * 0.4f, y, half, rowH);
            BlockRow(summon);
            BlockRow(dismiss);
            bool krakenUp = SeaSick.Combat.Kraken.Active != null;
            if (GUI.Button(summon, "Summon kraken", krakenUp ? UITheme.ButtonPressed : UITheme.Button))
                krakenNote = SeaSick.Combat.Kraken.DevSummon();
            if (GUI.Button(dismiss, "Dismiss kraken", UITheme.Button))
                krakenNote = SeaSick.Combat.Kraken.DevDismiss();
#if UNITY_EDITOR
            DevRow("summon kraken", summon, "Summon kraken", UITheme.Button, false);
            DevRow("dismiss kraken", dismiss, "Dismiss kraken", UITheme.Button, false);
#endif
            y += rowH + u * 0.3f;
            // The wild spawn's whole path (shadow, toast, chevron, rise) on
            // demand, and the deep-water test for where she is now.
            var wild = new Rect(body.x, y, body.width, rowH);
            BlockRow(wild);
            if (GUI.Button(wild, "Wild spawn now", SeaSick.Combat.KrakenDirector.Warning
                    ? UITheme.ButtonPressed : UITheme.Button))
                krakenNote = SeaSick.Combat.KrakenDirector.DevWarnNow();
#if UNITY_EDITOR
            DevRow("wild spawn now", wild, "Wild spawn now", UITheme.Button, false);
#endif
            y += rowH + u * 0.15f;
            if (Time.unscaledTime >= krakenStatusAt)
            {
                krakenStatusAt = Time.unscaledTime + 0.5f;
                krakenStatus = SeaSick.Combat.KrakenDirector.DevStatus();
            }
            // Wrapped, never clipped (Kevin: no cut-off text, ever).
            if (krakenWrap == null) krakenWrap = new GUIStyle(UITheme.Small) { wordWrap = true, clipping = TextClipping.Overflow };
            string kline = (string.IsNullOrEmpty(krakenNote) ? "" : krakenNote + "\n") + krakenStatus;
            var kw = body.width - u * 0.3f;
            float kh = krakenWrap.CalcHeight(new GUIContent(kline), kw);
            GUI.Label(new Rect(body.x + u * 0.3f, y, kw, kh), kline, krakenWrap);
#if UNITY_EDITOR
            DevRow("kraken status", new Rect(body.x + u * 0.3f, y, kw, kh), kline, krakenWrap, true);
#endif
            y += kh + u * 0.4f;

            // The rest of the old floating LIFE panel (Kevin: "move the other
            // dev options to settings too"), as a page with its own scroll.
            var life = SeaSick.Dev.LifeDevPanel.Instance;
            if (life != null)
            {
                var lifeRow = new Rect(body.x, y, body.width, rowH);
                BlockRow(lifeRow);
                if (GUI.Button(lifeRow, life.ToolName + "  ▶", UITheme.Button)) DevTools.Open = life;
#if UNITY_EDITOR
                DevRow("LIFE page", lifeRow, life.ToolName + "  ▶", UITheme.Button, false);
#endif
                y += rowH + u * 0.15f;
                GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                          "hands, raids, weather, overboard, castaways", UITheme.Small);
#if UNITY_EDITOR
                DevRow("LIFE note", new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                       "hands, raids, weather, overboard, castaways", UITheme.Small, false);
#endif
                y += u * 1.6f;
            }

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
                return y + u * 1.8f;
            }

            for (int i = 0; i < tools.Count; i++)
            {
                var t = tools[i];
                if (t == null) continue;
                var r = new Rect(body.x, y, body.width, rowH);
                BlockRow(r);
                if (GUI.Button(r, t.ToolName, UITheme.Button)) DevTools.Open = t;
                y += rowH + u * 0.15f;
                GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.3f),
                          t.ToolBlurb, UITheme.Small);
                y += u * 1.6f;
            }
#endif
            return y;
        }

        /// **Camera** (DREDGE controls step 2): the player's sea-camera
        /// choices (`SeaCameraPrefs`), for everyone, outside the dev-only
        /// TUNING block. Rows are at least ~44 pt tall (`touchH`), so a thumb
        /// hits them; sub-lines wrap, never clip.
        float DrawCameraGroup(Rect body, float y, float rowH, int u)
        {
            float touchH = Mathf.Max(rowH, Mathf.Min(Screen.dpi > 0f ? Screen.dpi * 0.27f : 0f, u * 4.4f));
            y += u * 0.5f;
            GUI.Label(new Rect(body.x, y, body.width, u * 1.4f), "CAMERA", UITheme.Small);
            y += u * 1.8f;

            y = Toggle(body, y, touchH, RowCamFollow, SeaSick.CameraRig.SeaCameraPrefs.Follow,
                       v => SeaSick.CameraRig.SeaCameraPrefs.Follow = v);
            y = Note(body, y, u, "Off: the view holds still while she turns");

            // Look speed: three chips (the drawer has no slider for a list row).
            GUI.Label(new Rect(body.x + u * 0.3f, y, body.width, u * 1.4f), "Look speed", UITheme.Body);
            y += u * 1.8f;
            float sens = SeaSick.CameraRig.SeaCameraPrefs.Sensitivity;
            int cur = sens <= 0.8f ? 0 : (sens >= 1.3f ? 2 : 1);
            float gap = u * 0.4f;
            float cw = (body.width - gap * 2f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(body.x + i * (cw + gap), y, cw, touchH);
                BlockRow(r);
                if (GUI.Button(r, LookSpeedNames[i], i == cur ? UITheme.ButtonPressed : UITheme.Button))
                    SeaSick.CameraRig.SeaCameraPrefs.Sensitivity = LookSpeedValues[i];
            }
            y += touchH + u * 0.5f;

            y = Toggle(body, y, touchH, RowInvX, SeaSick.CameraRig.SeaCameraPrefs.InvertX,
                       v => SeaSick.CameraRig.SeaCameraPrefs.InvertX = v);
            y = Toggle(body, y, touchH, RowInvY, SeaSick.CameraRig.SeaCameraPrefs.InvertY,
                       v => SeaSick.CameraRig.SeaCameraPrefs.InvertY = v);
            return y + u * 0.3f;
        }

        /// A wrapped sub-line under a row. Never clipped.
        static float Note(Rect body, float y, int u, string text)
        {
            if (noteWrap == null || noteWrapUnit != u)
            {
                noteWrapUnit = u;
                noteWrap = new GUIStyle(UITheme.Small) { wordWrap = true, clipping = TextClipping.Overflow };
            }
            float w = body.width - u * 0.3f;
            float h = noteWrap.CalcHeight(new GUIContent(text), w);
            GUI.Label(new Rect(body.x + u * 0.3f, y, w, h), text, noteWrap);
            return y + h + u * 0.5f;
        }

        static GUIStyle noteWrap;
        static int noteWrapUnit;

        /// `UIBlocker.Block` for list rows -- except inside the scroll view,
        /// where rects are local and the panel already claims the whole drawer.
        static void BlockRow(Rect r) { if (!inScroll) UIBlocker.Block(r); }

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

        Vector2 scroll;
        float listHeight;

#if UNITY_EDITOR
        /// Dev seam (`Dev/SweepCheck` "settings"): the TUNING rows as last
        /// repainted, in the scroll view's content space, with the room their
        /// text needs (`CalcSize` width, or the wrapped `CalcHeight`).
        internal struct DevRowInfo
        {
            public string name, text;
            public Rect local;
            public float needW, needH;
            public bool wrap;
        }
        internal static readonly System.Collections.Generic.List<DevRowInfo> DevRows =
            new System.Collections.Generic.List<DevRowInfo>();
        internal static int DevRowsFrame = -1;
        /// The list's scroll view, GUI space, and the scroll it was drawn at.
        internal static Rect DevView;
        internal static Vector2 DevScrollAtDraw;
        internal static Vector2 DevScroll
        {
            get => Instance != null ? Instance.scroll : Vector2.zero;
            set { if (Instance != null) Instance.scroll = value; }
        }
        internal static void DevClose()
        {
            if (Instance != null) Instance.open = false;
        }

        static void DevRow(string name, Rect r, string text, GUIStyle style, bool wrap)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || style == null) return;
            var c = new GUIContent(text);
            DevRows.Add(new DevRowInfo
            {
                name = name, text = text, local = r, wrap = wrap,
                needW = style.CalcSize(c).x,
                needH = wrap ? style.CalcHeight(c, r.width) : style.CalcSize(c).y,
            });
        }
#endif
        float dragTravel;
        static bool inScroll;
        static readonly string[] LookSpeedNames = { "Slow", "Normal", "Fast" };
        static readonly float[] LookSpeedValues = { 0.6f, 1f, 1.6f };
        static readonly string[] RowCamFollow = { "◎  camera follows the boat", "◉  camera follows the boat" };
        static readonly string[] RowInvX = { "◎  invert look left/right", "◉  invert look left/right" };
        static readonly string[] RowInvY = { "◎  invert look up/down", "◉  invert look up/down" };
        string saveNote = "";
        string krakenNote = "";
        string krakenStatus = "";
        float krakenStatusAt;
        static GUIStyle krakenWrap;

        /// Each row's two faces, { off, on }, built at load.
        ///
        /// `(value ? "◉  " : "◎  ") + label` is a fresh string every time it
        /// runs, and it ran four times per IMGUI EVENT — Layout, Repaint and
        /// one more per mouse move — to say four fixed things. A row has
        /// exactly two readings; there is no reason to build either twice.
        static readonly string[] RowPerf = { "◎  performance readout", "◉  performance readout" };
        static readonly string[] RowTuningLab = { "◎  show tuning lab (FEEL)", "◉  show tuning lab (FEEL)" };
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
            BlockRow(r);
            var style = value ? UITheme.ButtonPressed : UITheme.Button;
            if (GUI.Button(r, faces[value ? 1 : 0], style)) set(!value);
            return y + rowH + HudLayout.Unit * 0.35f;
        }
    }
}
