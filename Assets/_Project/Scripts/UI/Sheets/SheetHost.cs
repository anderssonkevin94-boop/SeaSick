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
        VisualElement head;
        VisualElement tabs;
        VisualElement body;
        VisualElement actions;
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
            card.AddToClassList(SheetTheme.Frame);
            card.style.display = DisplayStyle.None;
            layer.Add(card);

            // **The four bands of the standard frame, in order.** The header
            // and the tab strip are ABOVE the scroll view and the action row
            // is below it, so the only thing that ever moves when the body is
            // long is the body: the title stays readable and the verbs stay
            // under the thumb. A sheet that does not implement `ISheetFramed`
            // leaves head/tabs/actions empty and puts its whole tree in the
            // body, exactly as before.
            head = new VisualElement();
            head.AddToClassList(SheetTheme.FrameHead);
            head.style.display = DisplayStyle.None;
            card.Add(head);

            tabs = new VisualElement();
            tabs.style.display = DisplayStyle.None;
            card.Add(tabs);

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sheet-scroll");
            card.Add(scroll);

            body = new VisualElement();
            body.AddToClassList(SheetTheme.Body);
            scroll.Add(body);

            actions = new VisualElement();
            actions.style.display = DisplayStyle.None;
            card.Add(actions);

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
            framed = null;
            body.Clear();
            head.Clear();
            tabs.Clear();
            actions.Clear();
            head.style.display = DisplayStyle.None;
            tabs.style.display = DisplayStyle.None;
            actions.style.display = DisplayStyle.None;

            var s = Sheets.Current;
            if (s == null)
            {
                card.style.display = DisplayStyle.None;
                shadow.style.display = DisplayStyle.None;
                tail.style.display = DisplayStyle.None;
                tailDot.style.display = DisplayStyle.None;
                return;
            }

            framed = s as ISheetFramed;
            if (framed != null)
            {
                // **The remembered tab, unless the sheet asked for one.** A
                // sheet opened by the watchtower comes in with `Tab` already
                // set and the session's memory does not get to override it —
                // the player tapped the tower to ask about the watch.
                int want = framed.Tab;
                if (want < 0) want = Sheets.RecallTab(s.GetType());
                var labels = framed.TabLabels;
                int count = labels != null ? labels.Length : 0;
                if (count > 0) want = Mathf.Clamp(want, 0, count - 1); else want = 0;
                framed.SetTab(want);
                Sheets.RememberTab(s.GetType(), want);

                var h = framed.BuildHeader();
                if (h != null) { head.Add(h); head.style.display = DisplayStyle.Flex; }

                if (count > 1)
                {
                    tabs.Add(SheetKit.Tabs(labels, want, PickTab, framed.Accent));
                    tabs.style.display = DisplayStyle.Flex;
                }
                FillTab(s);
            }
            else
            {
                var content = s.Build();
                if (content != null) body.Add(content);
            }

            built = s;
            nextRefresh = Time.unscaledTime + 0.25f;

            card.style.display = DisplayStyle.Flex;
            shadow.style.display = DisplayStyle.Flex;
            scroll.scrollOffset = Vector2.zero;
        }

        ISheetFramed framed;

        /// A tab press: the sheet is told, the choice is remembered for the
        /// session, and only the BODY and the action row are rebuilt. The
        /// header and the strip itself stay put, so the card does not blink
        /// and the finger does not come down on a button that has moved.
        void PickTab(int index)
        {
            var s = Sheets.Current;
            if (framed == null || s == null) return;
            var labels = framed.TabLabels;
            if (labels == null || index < 0 || index >= labels.Length) return;
            if (index == framed.Tab) return;

            framed.SetTab(index);
            Sheets.RememberTab(s.GetType(), index);
            if (tabs.childCount > 0) SheetKit.SetTabs(tabs[0], index, framed.TabLabels);
            FillTab(s);
            scroll.scrollOffset = Vector2.zero;
        }

        /// Body and action row for whatever tab is live now.
        void FillTab(ISheet s)
        {
            body.Clear();
            actions.Clear();
            var content = s.Build();
            if (content != null) body.Add(content);

            var row = framed.BuildActions();
            if (row != null)
            {
                actions.Add(row);
                actions.style.display = DisplayStyle.Flex;
            }
            else actions.style.display = DisplayStyle.None;
        }

        /// Re-label the strip without rebuilding it — "hands · 3" becomes
        /// "hands · 4" when a recruit arrives, and the tab under the finger
        /// is the same element it was.
        void RelabelTabs()
        {
            if (framed == null || tabs.childCount == 0) return;
            SheetKit.SetTabs(tabs[0], framed.Tab, framed.TabLabels);
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
            if (s == null) { FrameOpen = false; return; }

            if (!s.StillValid) { Sheets.Close(); FrameOpen = false; return; }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.25f;
                s.Refresh();
                RelabelTabs();
            }

            Place(s);
        }

        // --- placement ---

        /// **The frame, in screen pixels, GUI space (origin top-left).**
        ///
        /// The one number the IMGUI HUD needs about the sheet. `HudLayout`
        /// keeps every panel it issues out of this rect, which is why the
        /// minimap column, the anchor prompt and the legacy bottom bar no
        /// longer have to know that a sheet exists — they only have to know
        /// where it is. Zero-sized and `FrameOpen == false` while nothing is
        /// open.
        public static Rect FrameRect { get; private set; }
        public static bool FrameOpen { get; private set; }

        /// The fraction of the short axis the frame takes: the bottom third
        /// upright, the right third on a desk. One number, because the shape
        /// of the frame is the whole point of "one sheet, tabs" — a card that
        /// sizes itself to its content is a card that moves its own buttons.
        public const float Third = 1f / 3f;

        /// The inner margin between the frame and the edges of its region.
        public const float Margin = 12f;

        /// **One rect, computed in screen pixels, then converted once.**
        ///
        /// It used to be two placement methods that each did their own
        /// arithmetic in panel units, and the card's height came from its
        /// CONTENT — which is exactly what Kevin rejected on the phone: a
        /// sheet that is a different size every time you open it. The frame
        /// is now a function of the safe area and nothing else, so opening
        /// the build tab and opening the hands tab put the card in the same
        /// place to the pixel.
        void Place(ISheet s)
        {
            float W = root.resolvedStyle.width;
            float H = root.resolvedStyle.height;
            if (W <= 1f || H <= 1f) return;

            var safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f)
                safe = new Rect(0f, 0f, Screen.width, Screen.height);

            // Screen space, origin BOTTOM-left, as `Screen.safeArea` is.
            float x, yBottom, w, h;
            if (HudLayout.Wide)
            {
                float band = safe.width * Third;
                w = band - Margin * 2f;
                h = safe.height - Margin * 2f;
                x = safe.xMax - band + Margin;
                yBottom = safe.yMin + Margin;
                card.RemoveFromClassList(SheetTheme.Docked);
            }
            else
            {
                float band = safe.height * Third;
                w = safe.width - Margin * 2f;
                h = band - Margin * 2f;
                x = safe.xMin + Margin;
                yBottom = safe.yMin + Margin;
                card.AddToClassList(SheetTheme.Docked);
            }

            // GUI space (origin top-left), for `HudLayout`.
            FrameRect = new Rect(x, Screen.height - (yBottom + h), w, h);
            FrameOpen = true;
            // Told once a frame, to the IMGUI HUD's own space. It used to be
            // the other way round — the card read `BottomClustersTop` and got
            // out of the prompt stack's way — and that made the sheet's size
            // depend on the HUD's, which is the loop a fixed frame exists to
            // cut. The sheet is the fixed thing now; the HUD moves.
            HudLayout.ClaimSheet(FrameRect);

            // Panel space. The panel is scaled by its match rule, so every
            // screen pixel above becomes `scale` panel units — the y flip is
            // already done, because the card is positioned from the top.
            float scale = W / Mathf.Max(1f, Screen.width);
            card.style.left = x * scale;
            card.style.right = StyleKeyword.Auto;
            card.style.top = FrameRect.y * scale;
            card.style.bottom = StyleKeyword.Auto;
            card.style.width = w * scale;
            card.style.height = h * scale;
            card.style.maxHeight = StyleKeyword.None;

            Shadow(x * scale, FrameRect.y * scale, w * scale, h * scale);

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
