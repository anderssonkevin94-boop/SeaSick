using SeaSick.Save;
using SeaSick.Terrain;
using SeaSick.Voyage;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeaSick.UI.Menus
{
    /// **The loading screen.** Kevin, 2026-09-26: *"when the game loads up
    /// I'd like to see a bar filling up so I know how much longer I have to
    /// wait for the ship to launch."*
    ///
    /// One full-screen `UIDocument`, sorted above everything else this game
    /// draws (`GameMenus` at 900, the shipyard modal at 1000) so it hides
    /// whatever is behind it -- the Home screen coming up on a cold launch,
    /// or the world itself already running on a Continue/Load/New reload --
    /// until real progress says the wait is over.
    ///
    /// **Progress is measured, not guessed, where the game can measure it**:
    /// the sliced world build (`TerrainWorldPopulator.Progress01`, the
    /// dominant cost -- see commit ebdaa50) drives most of the bar; the save
    /// restore (`SaveGame.Restoring`, only present on Continue/Load) drives
    /// a second stage that fills at a steady rate while it is true, because
    /// that call has no finer-grained progress to read and this file does
    /// not own `SaveGame.cs` to add one; a last, short stage covers the
    /// frame the ship needs to settle into her restored (or spawned) pose.
    /// The overall number is a weighted sum of the three and is clamped to
    /// never step backwards, so a stage that finishes ahead of its own
    /// weight's share never visibly un-fills the bar.
    ///
    /// **Installs itself the way `GameBoot`/`GameMenus` do** -- on every
    /// `SceneManager.sceneLoaded`, not just the first -- and does NOT
    /// survive the reload itself: a fresh one comes up with the reloaded
    /// scene, already showing, already at zero. There is nothing to reset by
    /// hand because there is nothing static to reset; the old instance goes
    /// away with the old scene exactly like `GameMenus`.
    [DefaultExecutionOrder(-400)]
    public class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Same gate as `GameBoot`: a lab/probe scene with no
            // `VoyageManager` never had a boot overlay and does not get a
            // loading screen either.
            if (FindAnyObjectByType<VoyageManager>() == null) return;
            EnsureInstance();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReinstallOnReload()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedReinstall;
            SceneManager.sceneLoaded += OnSceneLoadedReinstall;
        }

        static void OnSceneLoadedReinstall(Scene scene, LoadSceneMode mode) => Install();

        static LoadingScreen EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<LoadingScreen>();
            if (existing != null) return existing;
            return new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
        }

        // ------------------------------------------------------------------
        // Stage weights. The world build dominates real cost (13-16 s ->
        // ~0.85 s sliced, see ebdaa50); restore and ship-settle are the two
        // short tails after it.
        // ------------------------------------------------------------------
        const float WorldWeight = 0.72f;
        const float RestoreWeight = 0.20f;
        const float ShipWeight = 0.08f;

        /// The restore stage has no real progress to read (`SaveGame`
        /// exposes only `Restoring`, a bool) so it fills at a steady rate
        /// for as long as that flag holds, capped short of 100% so it never
        /// visibly stalls dead on a slow restore -- the last sliver waits
        /// for `Restoring` to actually clear.
        const float RestoreFillPerSecond = 0.6f;
        const float RestoreFillCap = 0.92f;

        /// How long the ship gets, once the world and any restore are both
        /// done, to visibly settle into her pose before the bar calls the
        /// stage finished.
        const float ShipSettleSeconds = 0.25f;

        /// How long the finished bar holds at 100% before it fades.
        const float HoldAtFullSeconds = 0.15f;
        const float FadeSeconds = 0.35f;

        PanelSettings settings;
        UIDocument document;
        VisualElement fill;
        Label stageLabel;
        Label pctLabel;

        TerrainWorldPopulator populator;
        float restoreProgress;
        bool everRestoring;
        float shipTimer;
        float overallProgress;
        float fullSince = -1f;
        bool fadingOut;
        bool done;
        /// Faded out: nothing covers the game any more (the time-away card waits for this).
        public bool Finished => done;

        void Awake()
        {
            Instance = this;
            BuildUI();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (settings != null) Destroy(settings);
        }

        void BuildUI()
        {
            var template = Resources.Load<PanelSettings>("UI/SheetPanel");
            settings = template != null ? Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            // Above the shipyard modal (1000) and GameMenus (900) both --
            // this hides whichever of them is under it until it fades.
            settings.sortingOrder = 2000;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;

            var root = document.rootVisualElement;
            var menuCss = Resources.Load<StyleSheet>("UI/Menus");
            if (menuCss != null) root.styleSheets.Add(menuCss);

            root.AddToClassList("loading-root");

            var title = new Label("SEASICK");
            title.AddToClassList("loading-title");
            root.Add(title);

            var track = new VisualElement();
            track.AddToClassList("loading-track");
            fill = new VisualElement();
            fill.AddToClassList("loading-fill");
            fill.style.width = Length.Percent(0f);
            track.Add(fill);
            root.Add(track);

            var row = new VisualElement();
            row.AddToClassList("loading-row");
            stageLabel = new Label("Raising islands…");
            stageLabel.AddToClassList("loading-stage");
            pctLabel = new Label("0%");
            pctLabel.AddToClassList("loading-pct");
            row.Add(stageLabel);
            row.Add(pctLabel);
            root.Add(row);
        }

        void Update()
        {
            if (document == null) return;

            // Same safe-area/orientation recipe as `GameMenus.Update` and
            // `ShipyardModal`, so the card reads right in both 1080x2340
            // portrait and 1920x1080 desktop.
            bool wide = Screen.width > Screen.height * 1.15f;
            settings.referenceResolution = wide ? new Vector2Int(844, 390) : new Vector2Int(390, 844);
            var root = document.rootVisualElement;
            float scale = settings.referenceResolution.x / (float)Mathf.Max(1, Screen.width);
            var safe = Screen.safeArea;
            root.style.paddingLeft = safe.xMin * scale;
            root.style.paddingRight = (Screen.width - safe.xMax) * scale;
            root.style.paddingTop = (Screen.height - safe.yMax) * scale;
            root.style.paddingBottom = safe.yMin * scale;

            if (done) return;

            if (populator == null) populator = FindFirstObjectByType<TerrainWorldPopulator>();
            // A build that threw is never going to reach `Done` -- do not
            // sit on a black screen forever over it. `SaveGame`/`GameBoot`
            // already logged why and fall back to a live (if empty) world.
            float worldP = populator != null && populator.Failed ? 1f
                : populator != null ? Mathf.Clamp01(TerrainWorldPopulator.Progress01) : 0f;

            if (SaveGame.Restoring)
            {
                everRestoring = true;
                restoreProgress = Mathf.Min(RestoreFillCap, restoreProgress + RestoreFillPerSecond * Time.unscaledDeltaTime);
            }
            else if (everRestoring)
            {
                restoreProgress = 1f;
            }
            else
            {
                // Nothing to restore this reload (a fresh voyage): this
                // stage has nothing to wait for once the world is up.
                restoreProgress = worldP >= 1f ? 1f : 0f;
            }

            bool priorStagesDone = worldP >= 1f && restoreProgress >= 1f;
            float shipP;
            if (priorStagesDone)
            {
                shipTimer += Time.unscaledDeltaTime;
                shipP = Mathf.Clamp01(shipTimer / ShipSettleSeconds);
            }
            else
            {
                shipTimer = 0f;
                shipP = 0f;
            }

            float computed = WorldWeight * worldP + RestoreWeight * restoreProgress + ShipWeight * shipP;
            // Monotonic: never let a stage's own noise (or this frame's
            // rounding) step the bar backwards.
            if (computed > overallProgress) overallProgress = computed;

            string label = worldP < 1f ? "Raising islands…"
                : AwayProgress.Running ? $"Your camps kept working… {Mathf.RoundToInt(AwayProgress.Progress01 * 100f)}%"
                : restoreProgress < 1f ? "Loading your voyage…"
                : "Launching the ship…";
            stageLabel.text = label;
            fill.style.width = Length.Percent(overallProgress * 100f);
            pctLabel.text = Mathf.RoundToInt(overallProgress * 100f) + "%";

            if (overallProgress >= 0.999f)
            {
                if (fullSince < 0f) fullSince = Time.unscaledTime;
                if (!fadingOut && Time.unscaledTime - fullSince >= HoldAtFullSeconds) BeginFade();
            }

            if (fadingOut) TickFade();
        }

        void BeginFade()
        {
            fadingOut = true;
            fadeStart = Time.unscaledTime;
        }

        float fadeStart;

        void TickFade()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - fadeStart) / FadeSeconds);
            document.rootVisualElement.style.opacity = 1f - t;
            if (t >= 1f)
            {
                done = true;
                document.rootVisualElement.style.display = DisplayStyle.None;
            }
        }
    }
}
