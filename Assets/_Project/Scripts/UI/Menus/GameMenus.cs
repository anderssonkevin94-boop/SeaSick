using System;
using SeaSick.Ship.Modular;
using SeaSick.UI.ModularYard;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

namespace SeaSick.UI.Menus
{
    /// **The Home / Load / Pause host.**
    ///
    /// One `UIDocument`, one `PanelSettings` (the same `UI/SheetPanel`
    /// template the shipyard modal uses, so the two full-screen UI Toolkit
    /// overlays scale identically), and a small state machine over which
    /// screen is live. Everything else is a builder in this namespace that
    /// hands back a `VisualElement`; this file only decides which one is on
    /// screen, blocks the world while one is, and does the scene-reload
    /// plumbing the save-slot contract asks for.
    ///
    /// **It installs itself**, the same way `GameBoot` and `SettingsPanel`
    /// do, and does NOT survive a scene reload -- a fresh one comes up with
    /// the reloaded scene and `GameBoot` decides what it should show.
    [DefaultExecutionOrder(-250)]
    public class GameMenus : MonoBehaviour
    {
        public enum Mode { None, Home, Load, Pause, SaveTarget }

        public static GameMenus Instance { get; private set; }
        public static Mode Current { get; private set; } = Mode.None;

        PanelSettings settings;
        UIDocument document;
        bool saveAndExitPending;
        string pendingExitSlotId;
        float scaleBeforePause = 1f;
        Mode settingsReturnMode = Mode.None;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() => EnsureInstance();

        /// Belt-and-braces alongside `GameBoot.HookReinstallOnReload` /
        /// `PauseChip.HookReinstallOnReload`: `EnsureInstance()` already
        /// self-heals whenever `ShowHome`/`TogglePause`/etc. is called after
        /// a mid-session reload with no `GameMenus` left in the new scene,
        /// so this was never the broken half of the 2026-09-26 reload bug --
        /// but re-running `Install()` on every `sceneLoaded`, not just the
        /// first, costs nothing (idempotent) and means a fresh scene always
        /// has one waiting rather than relying on the next caller to notice.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReinstallOnReload()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedReinstall;
            SceneManager.sceneLoaded += OnSceneLoadedReinstall;
        }

        static void OnSceneLoadedReinstall(Scene scene, LoadSceneMode mode) => Install();

        /// Creates the singleton on demand rather than only through
        /// `Install`. **`GameBoot.Awake` (`DefaultExecutionOrder(-300)`)
        /// calls `ShowHome` before this class's own `Install` is guaranteed
        /// to have run** -- `RuntimeInitializeOnLoadMethod` order between
        /// two different classes is not something `DefaultExecutionOrder`
        /// reliably settles the way it does for `Update`. `AddComponent`
        /// runs `Awake` synchronously, so calling this here is enough to
        /// have a live `Instance` by the time the caller's next line runs.
        static GameMenus EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<GameMenus>();
            if (existing != null) return existing;
            return new GameObject("GameMenus").AddComponent<GameMenus>();
        }

        void Awake()
        {
            Instance = this;
            SaveSlotsAdapter.Saved += OnSaved;
        }

        void OnDestroy()
        {
            // `Current` is a static, so -- unlike `Instance`, freshly wired
            // up by the next `EnsureInstance()` -- nothing else touches it
            // on a reload. Found alongside the reinstall bug, 2026-09-26:
            // `ReloadForBoot` (Continue/New/every Load row) never reset it,
            // so a session that reloaded FROM the Home screen (every real
            // session does) came back up still reading `Current == Home`
            // forever after -- which is also what `PauseChip.OnGUI` gates
            // on, so the pause chip, and the whole way back to a menu,
            // silently never appeared again. This object going away IS the
            // scene going away, so nothing it was showing survives it.
            if (Instance == this) { Instance = null; Current = Mode.None; }
            SaveSlotsAdapter.Saved -= OnSaved;
            // A runtime `PanelSettings` instance is not scene state -- Unity
            // will not collect it on its own just because the scene that
            // made it did, the same reason `ShipyardModal.OnDestroy` does
            // this too.
            if (settings != null) Destroy(settings);
        }

        // ------------------------------------------------------------------
        // Public API -- everything else in Scripts/UI/Menus calls in here
        // rather than touching `document`/`Current` directly.
        // ------------------------------------------------------------------

        public static void ShowHome()
        {
            EnsureInstance();
            Instance.EnsureDocument();
            SetWorldBlocked(true);
            Current = Mode.Home;
            Instance.Render();
        }

        public static void ShowLoad()
        {
            if (Instance == null) return;
            Current = Mode.Load;
            Instance.Render();
        }

        /// The pause chip / Esc. No-op while a modal that owns Esc itself is
        /// up (the shipyard, camp/wall siting) -- `PollEscape` below applies
        /// the same gate to the hardware key that `PauseChip` applies to the
        /// tap.
        public static void TogglePause()
        {
            EnsureInstance();
            if (Current == Mode.Pause || Current == Mode.SaveTarget) { ResumeFromPause(); return; }
            if (Current != Mode.None) return;               // Home/Load already up
            if (ShipyardModal.IsOpen) return;

            Instance.EnsureDocument();
            Instance.scaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            SetWorldBlocked(true);
            Current = Mode.Pause;
            Instance.Render();
        }

        public static void ResumeFromPause()
        {
            if (Instance == null || (Current != Mode.Pause && Current != Mode.SaveTarget)) return;
            Time.timeScale = Instance.scaleBeforePause > 0f ? Instance.scaleBeforePause : 1f;
            SetWorldBlocked(false);
            Current = Mode.None;
            Instance.Clear();
        }

        public static void ShowSaveTarget(bool saveAndExit)
        {
            if (Instance == null) return;
            Instance.saveAndExitPending = saveAndExit;
            Instance.pendingExitSlotId = null;
            Current = Mode.SaveTarget;
            Instance.Render();
        }

        /// `GameBoot.Skip()`'s escape hatch: a probe launched while the Home
        /// screen was up needs it gone NOW, without the reload every real
        /// exit from this class goes through -- there is no scene reload in
        /// a probe run. Leaves `Time.timeScale`/world-block to whoever asked
        /// for the skip, exactly as `Decide` already did before this screen
        /// existed.
        public static void ForceHide()
        {
            if (Instance == null) return;
            SetWorldBlocked(false);   // ShowHome set it; nothing else will clear it now
            Current = Mode.None;
            Instance.Clear();
        }

        public static void BackFromLoad()
        {
            Current = Mode.Home;
            Instance.Render();
        }

        public static void BackFromSaveTarget()
        {
            Instance.saveAndExitPending = false;
            Current = Mode.Pause;
            Instance.Render();
        }

        /// A row was confirmed for loading, from either Home's Continue or
        /// the Load list. Per the save-slot contract, EVERY entry into a
        /// world (Continue/Load/New) goes through `RequestLoad`/
        /// `RequestNewGame` and a scene reload -- `GameBoot.Awake` on the
        /// other side reads the pending choice back.
        public static void LoadSlotAndReload(string slotId)
        {
            SaveSlotsAdapter.RequestLoad(slotId);
            ReloadForBoot();
        }

        public static void NewGameAndReload()
        {
            SaveSlotsAdapter.RequestNewGame();
            ReloadForBoot();
        }

        /// Pause -> Exit (confirmed if it needed to be) or the toast after a
        /// successful Save & exit: back to the home screen, which is exactly
        /// what a reload with nothing pending produces.
        public static void ExitToHome()
        {
            ReloadForBoot();
        }

        static void ReloadForBoot()
        {
            Time.timeScale = 1f;
            SetWorldBlocked(false);
            var active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.buildIndex, LoadSceneMode.Single);
        }

        /// Settings, reached from Home or Pause: hide the card (world stays
        /// frozen/blocked exactly as it was) and open `SettingsPanel`
        /// straight onto its list. Polled in `Update` for the drawer closing
        /// again, which is the only way back -- the drawer has no callback.
        public static void OpenSettingsFromMenu()
        {
            if (Instance == null || Current == Mode.None) return;
            Instance.settingsReturnMode = Current;
            Instance.document.rootVisualElement.style.display = DisplayStyle.None;
            SettingsPanel.Open();
        }

        // ------------------------------------------------------------------
        // Rendering
        // ------------------------------------------------------------------

        void EnsureDocument()
        {
            if (document != null) return;
            var template = Resources.Load<PanelSettings>("UI/SheetPanel");
            settings = template != null ? Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            settings.sortingOrder = 900;   // under the shipyard modal (1000), over the world HUD
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;

            var root = document.rootVisualElement;
            var menuCss = Resources.Load<StyleSheet>("UI/Menus");
            if (menuCss != null) root.styleSheets.Add(menuCss);
            var sheetCss = Resources.Load<StyleSheet>("UI/Sheets");
            if (sheetCss != null) root.styleSheets.Add(sheetCss);
        }

        void Render()
        {
            EnsureDocument();
            var root = document.rootVisualElement;
            root.style.display = DisplayStyle.Flex;
            root.Clear();

            switch (Current)
            {
                case Mode.Home:
                    root.Add(HomeScreen.Build(root, OpenSettingsFromMenu));
                    break;
                case Mode.Load:
                    root.Add(WrapAsRoot(SlotListScreen.Build(SlotListScreen.Mode.Load, root,
                        BackFromLoad, LoadSlotAndReload, null)));
                    break;
                case Mode.Pause:
                    root.Add(PauseMenu.Build(root, ResumeFromPause, OpenSettingsFromMenu, ExitToHome));
                    break;
                case Mode.SaveTarget:
                    root.Add(WrapAsRoot(SlotListScreen.Build(SlotListScreen.Mode.SaveTarget, root,
                        BackFromSaveTarget, null, OnSaveTargetPicked)));
                    break;
                default:
                    root.Clear();
                    root.style.display = DisplayStyle.None;
                    break;
            }
        }

        static VisualElement WrapAsRoot(VisualElement content)
        {
            var root = MenuKit.Root();
            var card = MenuKit.Card(wide: true);
            card.Add(content);
            root.Add(card);
            return root;
        }

        void OnSaveTargetPicked(string slotId, string name)
        {
            bool ok = SaveSlotsAdapter.SaveManual(slotId, name, out string error);
            if (!ok)
            {
                MenuKit.Toast(document.rootVisualElement, string.IsNullOrEmpty(error)
                    ? "could not save" : error, 2.2f);
                return;
            }
            if (saveAndExitPending) pendingExitSlotId = slotId;
            // If the backend saves synchronously (no async Saved event
            // follows), do not wait forever for one: fall back once the
            // frame is idle. `OnSaved` cancels this if the event does fire.
            if (!SaveSlotsAdapter.IsSaving)
                document.rootVisualElement.schedule.Execute(() => FallbackAfterSave(slotId)).ExecuteLater(150);
        }

        void FallbackAfterSave(string slotId)
        {
            if (saveAndExitPending && pendingExitSlotId == null && Current == Mode.SaveTarget)
            {
                // The Saved event never arrived -- assume the synchronous
                // path and finish the flow ourselves.
                pendingExitSlotId = slotId;
                MenuKit.Toast(document.rootVisualElement, "Saved");
                saveAndExitPending = false;
                ExitToHome();
            }
            else if (!saveAndExitPending && Current == Mode.SaveTarget)
            {
                MenuKit.Toast(document.rootVisualElement, "Saved");
                BackFromSaveTarget();
            }
        }

        void OnSaved(SeaSick.Save.SaveSlotInfo info)
        {
            if (info == null || document == null) return;
            if (Current == Mode.SaveTarget || Current == Mode.Pause)
                MenuKit.Toast(document.rootVisualElement, "Saved");
            if (saveAndExitPending && info.id == pendingExitSlotId)
            {
                saveAndExitPending = false;
                pendingExitSlotId = null;
                ExitToHome();
            }
            else if (Current == Mode.SaveTarget && !saveAndExitPending)
            {
                BackFromSaveTarget();
            }
        }

        void Clear()
        {
            if (document == null) return;
            document.rootVisualElement.Clear();
            document.rootVisualElement.style.display = DisplayStyle.None;
        }

        static void SetWorldBlocked(bool blocked) => ShipyardSession.SetWorldInputBlocked(blocked);

        // ------------------------------------------------------------------
        // Per-frame: safe-area/scale (same recipe as `ShipyardModal`), the
        // settings-drawer return, and the hardware Esc key.
        // ------------------------------------------------------------------

        void Update()
        {
            if (document != null && Current != Mode.None)
            {
                bool wide = Screen.width > Screen.height * 1.15f;
                settings.referenceResolution = wide ? new Vector2Int(844, 390) : new Vector2Int(390, 844);
                var root = document.rootVisualElement;
                float scale = settings.referenceResolution.x / (float)Mathf.Max(1, Screen.width);
                var safe = Screen.safeArea;
                root.style.paddingLeft = safe.xMin * scale;
                root.style.paddingRight = (Screen.width - safe.xMax) * scale;
                root.style.paddingTop = (Screen.height - safe.yMax) * scale;
                root.style.paddingBottom = safe.yMin * scale;
            }

            if (settingsReturnMode != Mode.None && !SettingsPanel.IsOpen)
            {
                Current = settingsReturnMode;
                settingsReturnMode = Mode.None;
                Render();
            }

            PollEscape();
        }

        void PollEscape()
        {
            var keys = Keyboard.current;
            if (keys == null || !keys.escapeKey.wasPressedThisFrame) return;
            // Anything already using Esc for its own cancel gets it first --
            // a wall/camp placement in progress, or the shipyard modal (which
            // has its own close button and is not meant to be pausable
            // through).
            if (ShipyardModal.IsOpen) return;
            if (SeaSick.UI.CampSiting.Placing || SeaSick.UI.WallSiting.Active) return;
            if (!SeaSick.Save.GameBoot.Decided) return;   // still on the boot overlay
            TogglePause();
        }
    }
}
