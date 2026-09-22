using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Where the open sheet is, on a screen of any shape.**
    ///
    /// One UI Toolkit document carries the whole sheet HUD: the card, its
    /// tail, the place label and the ashore rail. It is created at runtime and
    /// survives a scene load, so no scene has to be wired for it and nothing
    /// in `Sea.unity` has to be re-serialised when the HUD changes.
    ///
    /// ## Two shapes, one layout pass
    ///
    /// The HUD has no "mode". `HudLayout.Wide` asks the WINDOW what shape it
    /// is, exactly as the IMGUI HUD does, and this re-lays out on the next
    /// frame when the answer changes. On a desk the card is a 390-520 px panel
    /// standing beside the thing it is about, with a hairline tail back to it
    /// — the object is the subject and the card is a note pinned next to it.
    /// On a phone held upright there is nowhere beside anything, so it docks
    /// to the bottom edge at full width and scrolls, and the tail is dropped
    /// rather than drawn across the whole screen.
    ///
    /// ## Why the placement runs in LateUpdate
    ///
    /// `AnchorWorld` is projected through `Camera.main`, and the chase camera
    /// moves in `LateUpdate`. Placing the card in `Update` puts it one frame
    /// behind the object it points at, which on a camera that swings is a tail
    /// that visibly lags its own anchor.
    public class SheetHost : MonoBehaviour
    {
        public static SheetHost Instance { get; private set; }

        UIDocument doc;
        VisualElement root;

        // The sheet layer and its parts.
        VisualElement layer;
        VisualElement shadow;
        VisualElement card;
        VisualElement body;
        ScrollView scroll;
        VisualElement tail;
        VisualElement tailDot;

        PlaceLabel place;
        AshoreRail rail;
        ChartInstrument chart;
        SelectionRing ring;

        ISheet built;
        float nextRefresh;
        float nextScan;

        // --- creation ---

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // **The scene is asked, not just the static.** Domain reload is
            // off in this project, so `Instance` survives a play session and
            // comes back as a destroyed object — which reads as null, so this
            // happily built a SECOND host on top of the one already there.
            // The duplicate then took the early-exit in `Awake`, never built
            // its tree and never claimed `Instance`, and the HUD came up as a
            // UIDocument with an empty root: no error, no panel, nothing.
            if (Instance != null) return;
            var existing = FindFirstObjectByType<SheetHost>(FindObjectsInactive.Include);
            if (existing != null) { Instance = existing; return; }

            var go = new GameObject("SheetHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<SheetHost>();
            go.AddComponent<WorldPicker>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            EnsureBuilt();
        }

        /// **Building is lazy and idempotent, not an `Awake` step.**
        ///
        /// A host whose `Awake` was skipped or which lost the race to another
        /// copy used to sit there for the whole session as a live component
        /// with an empty panel. Doing the work here, and calling it from
        /// `LateUpdate` as well, means the HUD cannot end up half-created:
        /// whatever object wins, the first frame it ticks it is complete.
        bool chromeBuilt;

        void EnsureBuilt()
        {
            if (chromeBuilt) return;

            var settings = Resources.Load<PanelSettings>("UI/SheetPanel");
            if (settings == null)
            {
                Debug.LogWarning("[Sheets] Resources/UI/SheetPanel.asset is missing — the sheet HUD will not draw.");
                enabled = false;
                return;
            }

            doc = GetComponent<UIDocument>();
            if (doc == null) doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            // The document is created empty and filled here rather than from a
            // UXML tree: every element in it is data-driven, and a UXML file
            // would be a second place a class name could drift from
            // `SheetTheme`.
            root = doc.rootVisualElement;
            root.AddToClassList("sheet-root");
            root.pickingMode = PickingMode.Ignore;

            var sheetStyle = Resources.Load<StyleSheet>("UI/Sheets");
            if (sheetStyle != null) root.styleSheets.Add(sheetStyle);
            else Debug.LogWarning("[Sheets] Resources/UI/Sheets.uss is missing — the sheet HUD will be unstyled.");

            BuildChrome();

            place = new PlaceLabel(root);
            rail = new AshoreRail(root);
            // The chart is NOT gated on `SuppressLegacy`: it is the instrument
            // at sea as much as at anchor, which is the whole reason it can
            // replace the minimap and the compass tape rather than sit beside
            // them.
            chart = new ChartInstrument(root);
            if (ring == null) ring = gameObject.AddComponent<SelectionRing>();
            chromeBuilt = true;
        }

        void OnEnable() { Sheets.Changed += OnSheetChanged; }
        void OnDisable() { Sheets.Changed -= OnSheetChanged; }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// The panel a tap has to be tested against before it is allowed to
        /// reach the world. `WorldPicker` asks for this rather than keeping
        /// its own reference, so there is one answer to "is the finger on the
        /// HUD".
        public IPanel Panel => root != null ? root.panel : null;

        void BuildChrome()
        {
            layer = new VisualElement();
            layer.style.position = Position.Absolute;
            layer.style.left = 0; layer.style.top = 0;
            layer.style.right = 0; layer.style.bottom = 0;
            layer.pickingMode = PickingMode.Ignore;
            root.Add(layer);

            tail = new VisualElement();
            tail.AddToClassList(SheetTheme.Tail);
            tail.pickingMode = PickingMode.Ignore;
            tail.style.display = DisplayStyle.None;
            layer.Add(tail);

            tailDot = new VisualElement();
            tailDot.AddToClassList("sheet-tail-dot");
            tailDot.pickingMode = PickingMode.Ignore;
            tailDot.style.display = DisplayStyle.None;
            layer.Add(tailDot);

            shadow = new VisualElement();
            shadow.AddToClassList("sheet-shadow");
            shadow.style.position = Position.Absolute;
            shadow.pickingMode = PickingMode.Ignore;
            shadow.style.display = DisplayStyle.None;
            layer.Add(shadow);

            card = new VisualElement();
            card.AddToClassList(SheetTheme.Card);
            card.style.display = DisplayStyle.None;
            layer.Add(card);

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sheet-scroll");
            card.Add(scroll);

            body = new VisualElement();
            body.AddToClassList(SheetTheme.Body);
            scroll.Add(body);

            // The 1px inner rule, last so it sits over the body, and inert so
            // it never eats a press meant for a button under it.
            var inner = new VisualElement();
            inner.AddToClassList("sheet-inner");
            inner.pickingMode = PickingMode.Ignore;
            card.Add(inner);
        }

        // --- the open sheet ---

        void OnSheetChanged()
        {
            built = null;
            body.Clear();
            var s = Sheets.Current;
            if (s == null)
            {
                card.style.display = DisplayStyle.None;
                shadow.style.display = DisplayStyle.None;
                tail.style.display = DisplayStyle.None;
                tailDot.style.display = DisplayStyle.None;
                return;
            }

            var content = s.Build();
            if (content != null) body.Add(content);
            built = s;
            nextRefresh = Time.unscaledTime + 0.25f;

            card.style.display = DisplayStyle.Flex;
            shadow.style.display = DisplayStyle.Flex;
            scroll.scrollOffset = Vector2.zero;
        }

        void LateUpdate()
        {
            if (Instance == null) Instance = this;
            EnsureBuilt();
            if (root == null) return;

            bool on = Sheets.SuppressLegacy;
            place.Tick(on, root);
            rail.Tick(on, root);
            chart.Tick(root);
            // Claimed here rather than inside the instrument, so the flag is
            // true for exactly as long as something is actually drawing.
            Sheets.ChartActive = true;

            // A pickable with no collider can never be tapped, and a campfire
            // is a bare mesh with a Light on it. Rather than demanding every
            // world factory remember, the host sweeps for them — cheaply, and
            // only while the sheet HUD is the HUD.
            if (on && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 2f;
                Pickable.EnsureAll();
            }

            var s = Sheets.Current;
            if (s == null) return;

            if (!s.StillValid) { Sheets.Close(); return; }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.25f;
                s.Refresh();
            }

            Place(s);
        }

        // --- placement ---

        void Place(ISheet s)
        {
            float W = root.resolvedStyle.width;
            float H = root.resolvedStyle.height;
            if (W <= 1f || H <= 1f) return;

            // The safe area arrives in screen pixels; the panel is scaled.
            float scale = W / Mathf.Max(1f, Screen.width);
            var safe = Screen.safeArea;
            float mL = safe.xMin * scale + 12f;
            float mR = (Screen.width - safe.xMax) * scale + 12f;
            float mT = (Screen.height - safe.yMax) * scale + 12f;
            float mB = safe.yMin * scale + 12f;

            if (HudLayout.Wide) PlaceRight(W, H, mR, mT, mB);
            else PlaceDocked(W, H, mL, mR, mB);
        }

        /// **The card is a column on the right, not a label on the object.**
        ///
        /// It used to be placed beside the thing it was about, with a tail
        /// back to it. Kevin, playing it: *"the menu system on the island
        /// should always be pinned on the right side of the screen. It moves
        /// around when I zoom in and out and move around."* That is the whole
        /// argument — a card anchored in the WORLD is a card that slides out
        /// from under the finger every time the camera breathes, and the
        /// camera on this island is never still. Which object the card is
        /// about is now said by the ring on the ground, which costs the player
        /// nothing to look at and does not move the buttons.
        ///
        /// So the only thing still read off the world is `AnchorWorld`, and
        /// only by `SelectionRing`. The tail is gone with the placement that
        /// needed it.
        void PlaceRight(float W, float H, float mR, float mT, float mB)
        {
            card.RemoveFromClassList(SheetTheme.Docked);

            float w = Mathf.Clamp(W * 0.32f, 400f, 440f);
            card.style.width = w;
            card.style.left = StyleKeyword.Auto;
            card.style.bottom = StyleKeyword.Auto;
            card.style.right = Mathf.Max(16f, mR + 4f);

            // The top is under the ashore rail, which shares this corner and
            // is the one thing allowed to sit above the card -- it is four
            // discs, it is read at a glance, and burying it under a sheet
            // would be hiding the crew behind the crew's own sheet.
            float top = mT;
            float railBottom = rail != null ? rail.BottomPanelY : 0f;
            if (railBottom > 0f) top = Mathf.Max(top, railBottom + 10f);

            // The floor is the legacy prompt stack, not the screen edge.
            // "Cast off (space)" is drawn bottom-centre by `AnchorController`
            // and is the one control the sheet HUD deliberately leaves to
            // IMGUI, so a card allowed to run to the bottom margin covers the
            // way out.
            float floor = H - mB;
            float clusters = HudLayout.BottomClustersTop;
            if (clusters > 0f)
                floor = Mathf.Min(floor, clusters * (H / Mathf.Max(1f, Screen.height)) - 10f);

            card.style.top = top;
            card.style.maxHeight = Mathf.Max(160f, floor - top);

            float h = card.resolvedStyle.height;
            Shadow(W - card.style.right.value.value - w, top, w, h);

            tail.style.display = DisplayStyle.None;
            tailDot.style.display = DisplayStyle.None;
        }

        void PlaceDocked(float W, float H, float mL, float mR, float mB)
        {
            card.AddToClassList(SheetTheme.Docked);
            card.style.left = mL - 12f;
            card.style.right = mR - 12f;
            card.style.width = StyleKeyword.Auto;
            card.style.top = StyleKeyword.Auto;
            card.style.bottom = Mathf.Max(0f, mB - 12f);
            card.style.maxHeight = H * 0.55f;

            float h = card.resolvedStyle.height;
            Shadow(card.resolvedStyle.left, H - (mB - 12f) - h, card.resolvedStyle.width, h);

            tail.style.display = DisplayStyle.None;
            tailDot.style.display = DisplayStyle.None;
        }

        void Shadow(float x, float y, float w, float h)
        {
            if (w <= 1f || h <= 1f) { shadow.style.display = DisplayStyle.None; return; }
            shadow.style.display = DisplayStyle.Flex;
            shadow.style.left = x + 2f;
            shadow.style.top = y + 6f;
            shadow.style.width = w;
            shadow.style.height = h;
        }

        /// Screen pixels (origin bottom-left) to panel coordinates (origin
        /// top-left, and scaled by the panel's own match rule). The y flip is
        /// the caller's job — `ScreenToPanel` does not do it — and forgetting
        /// it puts every card upside down on the screen, which reads as a
        /// placement bug rather than a units one.
        Vector2 ToPanel(Vector2 screen)
        {
            var p = root.panel;
            if (p == null) return screen;
            return RuntimePanelUtils.ScreenToPanel(p, new Vector2(screen.x, Screen.height - screen.y));
        }
    }
}
