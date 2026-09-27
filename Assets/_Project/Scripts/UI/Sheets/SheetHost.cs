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
        PanelSettings runtimePanel;
        Vector2Int originalResolution;
        VisualElement root;

        // The sheet layer and its parts.
        VisualElement layer;
        VisualElement shadow;
        VisualElement card;
        VisualElement head;
        VisualElement tabs;
        VisualElement body;
        VisualElement actions;
        VisualElement page;
        VisualElement tail;
        VisualElement tailDot;

        PlaceLabel place;
        AshoreRail rail;
        ChartInstrument chart;
        SelectionRing ring;
        MidnightLandHud land;
        SeaLedger sea;
        bool midnight;
        readonly BuildingSheetFocus buildingFocus = new BuildingSheetFocus();

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
            runtimePanel = Instantiate(settings);
            originalResolution = runtimePanel.referenceResolution;
            doc.panelSettings = runtimePanel;
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
            var landStyle = Resources.Load<StyleSheet>("UI/MidnightLand");
            if (landStyle != null) root.styleSheets.Add(landStyle);

            place = new PlaceLabel(root);
            rail = new AshoreRail(root);
            // The chart is NOT gated on `SuppressLegacy`: it is the instrument
            // at sea as much as at anchor, which is the whole reason it can
            // replace the minimap and the compass tape rather than sit beside
            // them.
            chart = new ChartInstrument(root);
            land = new MidnightLandHud(root);
            // The sea twin of the land drawer: added after it, so it draws
            // over everything else in this document too.
            sea = new SeaLedger(root);
            if (ring == null) ring = gameObject.AddComponent<SelectionRing>();
            chromeBuilt = true;
        }

        void OnEnable() { Sheets.Changed += OnSheetChanged; }
        void OnDisable() { Sheets.Changed -= OnSheetChanged; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (runtimePanel != null) Destroy(runtimePanel);
        }

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

            // **No scroll view. The body is a horizontal pager.**
            //
            // Kevin, on the phone, 2026-09-22: *"you need to scroll down to
            // see all the options and that's a huge no no. All the
            // information should be available on screen. If it doesn't fit
            // you can swipe left to right for new windows."* So the body
            // clips, every page is built to fit the band exactly (see
            // `BandHeight`), and the tab strip's entries ARE the pages:
            // swiping the body moves between them.
            body = new VisualElement();
            body.AddToClassList(SheetTheme.Body);
            card.Add(body);
            body.RegisterCallback<PointerDownEvent>(OnBodyDown, TrickleDown.TrickleDown);
            body.RegisterCallback<PointerMoveEvent>(OnBodyMove, TrickleDown.TrickleDown);
            body.RegisterCallback<PointerUpEvent>(OnBodyUp, TrickleDown.TrickleDown);
            body.RegisterCallback<PointerCancelEvent>(OnBodyCancel, TrickleDown.TrickleDown);
            body.RegisterCallback<PointerCaptureOutEvent>(_ => CancelDrag());

            actions = new VisualElement();
            actions.style.display = DisplayStyle.None;
            // Never squeezed: the body band (min-height 0) gives way instead,
            // so a taller action row is never cut off at the card's bottom.
            actions.style.flexShrink = 0f;
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
                    tabs.Add(SheetKit.Strip(labels, want, PickTab, framed.Accent));
                    tabs.style.display = DisplayStyle.Flex;
                    stripCount = count;
                }
                else stripCount = 0;
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

            // **A tall sheet sits over the chart instead of under it.** The
            // chart is drawn in `EnsureBuilt` after `layer`, so it normally
            // wins the z-order -- invisible for the standard band (the chart
            // lives in the untouched two-thirds of the screen), but Stores'
            // full-height band (`ISheetFramed.WantsTallSheet`) reaches into
            // the chart's top-right corner. The chart is deliberately "never
            // hidden" (its own doc comment), so this covers it with the
            // opaque card rather than hiding the instrument itself -- Kevin's
            // own call on the mockup: the chart can be covered while Stores
            // is open.
            if (framed != null && framed.WantsTallSheet) layer.BringToFront();
            else layer.SendToBack();
        }

        ISheetFramed framed;
        int stripCount;

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

            // Tapping a tab travels the same way a swipe does, so the strip
            // and the gesture agree about which direction the pages lie in.
            Slide(index > framed.Tab ? 1 : -1, index, 0f);
        }

        /// Move to `index` without animating -- the commit half of a swipe
        /// and of a tab press both land here.
        void GoTo(int index)
        {
            var s = Sheets.Current;
            if (framed == null || s == null) return;
            framed.SetTab(index);
            Sheets.RememberTab(s.GetType(), index);
            if (tabs.childCount > 0) SheetKit.SetStrip(tabs[0], index, framed.TabLabels);
            FillTab(s);
        }

        /// Body and action row for whatever tab is live now.
        void FillTab(ISheet s)
        {
            body.Clear();
            actions.Clear();
            // Every page is one element, so the pager has exactly one thing
            // to translate. It fills the band and clips -- a page that does
            // not fit is a page the sheet should have split, not a page the
            // frame should scroll.
            page = new VisualElement();
            page.style.flexDirection = FlexDirection.Column;
            page.style.flexGrow = 1f;
            page.style.flexShrink = 1f;
            // min-height 0: an action row that grows a line (the Ship
            // sheet's Shipyard blocker, 2026-09-26) takes its room from the
            // page's bottom edge instead of being pushed off the card.
            page.style.minHeight = 0f;
            page.style.overflow = Overflow.Hidden;
            body.Add(page);
            var content = s.Build();
            // **Never squeezed, 2026-09-23.** UI Toolkit's default
            // `flex-shrink` is 1, so a page taller than the band did not clip
            // -- it squashed every block below its own height and each one
            // spilled onto the next: the camp's fire block drawn over the
            // store gauges, and a recipe's amount chips drawn UNDER the next
            // recipe row, which then took the tap (Kevin: *"I can't press to
            // create planks"*). At its natural height a page that is too tall
            // is cut at the bottom instead, and what is drawn is what the
            // finger lands on.
            if (content != null) { content.style.flexShrink = 0f; page.Add(content); }

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
            var labels = framed.TabLabels;
            int count = labels != null ? labels.Length : 0;
            // **A page COUNT change rebuilds the strip.** Labels move with the
            // camp ("hands 1/2" becomes "hands 1/3" when a recruit arrives),
            // and a strip with the wrong number of entries is a strip that
            // cannot reach the last page.
            if (count != stripCount)
            {
                tabs.Clear();
                stripCount = count;
                if (count > 1)
                {
                    int at = Mathf.Clamp(framed.Tab, 0, count - 1);
                    if (at != framed.Tab) { GoTo(at); return; }
                    tabs.Add(SheetKit.Strip(labels, at, PickTab, framed.Accent));
                    tabs.style.display = DisplayStyle.Flex;
                }
                else tabs.style.display = DisplayStyle.None;
                return;
            }
            SheetKit.SetStrip(tabs[0], framed.Tab, labels);
        }

        // --- the pager ---------------------------------------------------

        /// A press has to travel this far sideways before it stops being a
        /// tap on a row and becomes a page turn. 20 px: far enough that a
        /// thumb pressing a button never crosses it, near enough that a
        /// deliberate swipe is caught before it feels dead.
        public const float SwipeArm = 20f;

        /// Past a quarter of the body's width, the page commits rather than
        /// snapping back -- or past this speed, however short the throw.
        public const float CommitFrac = 0.25f;
        public const float FlickPxPerSecond = 600f;

        int dragPointer = -1;
        bool dragArmed, dragging;
        float dragX0, dragY0, dragDx, dragT0;
        bool animating;
        float animDeadline;

        int PageCount
        {
            get
            {
                var labels = framed != null ? framed.TabLabels : null;
                return labels != null ? labels.Length : 1;
            }
        }

        void OnBodyDown(PointerDownEvent e)
        {
            dragPointer = e.pointerId;
            dragX0 = e.position.x;
            dragY0 = e.position.y;
            dragDx = 0f;
            dragging = false;
            dragT0 = Time.unscaledTime;
            dragArmed = !animating && PageCount > 1;
        }

        void OnBodyMove(PointerMoveEvent e)
        {
            if (!dragArmed || e.pointerId != dragPointer) return;
            float dx = e.position.x - dragX0;
            float dy = e.position.y - dragY0;
            if (!dragging)
            {
                if (Mathf.Abs(dx) < SwipeArm) return;
                // **A vertical drag is not swallowed.** There is nothing left
                // to scroll, but eating it would kill the camera drag that
                // starts under a docked card's edge -- and a gesture that
                // does nothing AND blocks something else is the worst of both.
                if (Mathf.Abs(dy) > Mathf.Abs(dx)) { dragArmed = false; return; }
                dragging = true;
                // Capturing here is what turns the press on a button into a
                // swipe: the button never sees the release, so it never
                // fires, and the page follows the finger instead.
                body.CapturePointer(e.pointerId);
            }
            dragDx = dx;
            Shift(page, dx);
            e.StopPropagation();
        }

        void OnBodyUp(PointerUpEvent e) { EndDrag(e.pointerId); }
        void OnBodyCancel(PointerCancelEvent e) { EndDrag(e.pointerId); }

        void CancelDrag()
        {
            if (!dragging) { dragArmed = false; dragPointer = -1; return; }
            dragging = false; dragArmed = false; dragPointer = -1;
            Animate(page, dragDx, 0f, 0.12f, null);
        }

        void EndDrag(int id)
        {
            if (id != dragPointer) return;
            bool was = dragging;
            dragging = false; dragArmed = false; dragPointer = -1;
            if (body.HasPointerCapture(id)) body.ReleasePointer(id);
            if (!was) return;

            float w = Mathf.Max(1f, body.resolvedStyle.width);
            float dt = Mathf.Max(0.001f, Time.unscaledTime - dragT0);
            float vel = dragDx / dt;
            bool commit = Mathf.Abs(dragDx) > w * CommitFrac
                          || (Mathf.Abs(vel) > FlickPxPerSecond && Mathf.Abs(dragDx) > SwipeArm);
            int dir = dragDx < 0f ? 1 : -1;
            int next = (framed != null ? framed.Tab : 0) + dir;
            if (!commit || next < 0 || next >= PageCount)
            {
                Animate(page, dragDx, 0f, 0.14f, null);
                return;
            }
            Slide(dir, next, dragDx);
        }

        /// The old page leaves the way the finger was going, the new one
        /// comes in behind it. `dir` is +1 for the next page.
        void Slide(int dir, int next, float fromDx)
        {
            if (framed == null) return;
            float w = Mathf.Max(1f, body.resolvedStyle.width);
            animating = true;
            animDeadline = Time.unscaledTime + 1f;
            var outgoing = page;
            Animate(outgoing, fromDx, -dir * w, 0.13f, () =>
            {
                GoTo(next);
                // Placed before the first tick, or the new page flashes at
                // rest for one frame before it slides in.
                Shift(page, dir * w);
                if (page != null) Animate(page, dir * w, 0f, 0.15f, () => animating = false);
                else animating = false;
            });
        }

        static void Shift(VisualElement e, float px)
        {
            if (e == null) return;
            e.style.translate = new Translate(
                new Length(px, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel));
        }

        /// A per-frame lerp rather than `experimental.animation`, because the
        /// element being animated is destroyed by the page swap in the middle
        /// of the sequence and a scheduled item on a dead element simply
        /// stops -- which the deadline in `LateUpdate` then clears.
        void Animate(VisualElement e, float from, float to, float secs, System.Action done)
        {
            if (e == null) { done?.Invoke(); return; }
            float t0 = Time.unscaledTime;
            IVisualElementScheduledItem item = null;
            item = e.schedule.Execute(() =>
            {
                float t = secs <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - t0) / secs);
                float k = 1f - (1f - t) * (1f - t);        // ease out
                Shift(e, Mathf.Lerp(from, to, k));
                if (t < 1f) return;
                if (item != null) item.Pause();
                done?.Invoke();
            }).Every(16);
        }

        // --- the band ------------------------------------------------------

        /// **The chrome of the frame, in panel units.** Every one of these is
        /// the sum of the paddings and heights in `Sheets.uss`, and they are
        /// here rather than measured because a sheet has to know how many
        /// rows fit BEFORE it builds the page that holds them -- a measured
        /// answer is always one frame late, and one frame late is a page that
        /// overflows on the frame it is opened.
        public const float HeadPx = 56f;      // 12 pad + 34 badge + 10 margin
        public const float StripPx = 45f;     // 44 tab + 1 rule
        public const float ActionsPx = 61f;   // 10 pad + 38 button + 12 pad + 1 rule
        public const float BodyPadPx = 26f;   // 12 top + 14 bottom
        public const float BorderPx = 4f;     // 2 px of card border, top and bottom

        /// The panel is scaled by its own match rule, so a screen pixel is
        /// this many panel units. 1 until the panel has laid itself out once.
        public static float PanelScale
        {
            get
            {
                var h = Instance;
                if (h == null || h.root == null) return 1f;
                float w = h.root.resolvedStyle.width;
                return w > 1f && Screen.width > 0 ? w / Screen.width : 1f;
            }
        }

        /// The frame's size in SCREEN pixels, from the safe area alone --
        /// the same arithmetic `Place` does, available before the first
        /// `LateUpdate` so a sheet can be paginated as it is built.
        public static Vector2 FrameSizeScreen()
        {
            var safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f)
                safe = new Rect(0f, 0f, Screen.width, Screen.height);
            var size = HudLayout.Wide
                ? new Vector2(safe.width * Third - Margin * 2f, safe.height - Margin * 2f
                    - (MidnightLandHud.Active ? (MidnightLandHud.NavHeight + MidnightLandHud.TopHeight + 24f) / PanelScale : 0f))
                : new Vector2(safe.width - Margin * 2f, safe.height * (MidnightLandHud.Active ? .46f : Third) - Margin * 2f);
            if (!HudLayout.Wide && Sheets.Current is StationSheet station && station.ProductionLayout)
            {
                float ceiling = safe.height - (MidnightLandHud.TopHeight + MidnightLandHud.NavHeight + 160f) / PanelScale;
                size.y = Mathf.Min(Mathf.Max(size.y, 480f / PanelScale), ceiling);
            }
            // **The villager sheet, V2 Split (2026-09-27).** Its top edge
            // sits `HandSheet.PhoneHeight` of the safe height up from the
            // bottom, so the island camera can frame him in the rest
            // (`IslandCam.HeroScreenSpot` reads `FrameRect` back).
            else if (!HudLayout.Wide && Sheets.Current is HandSheet)
            {
                float lift = MidnightLandHud.Active ? (MidnightLandHud.NavHeight + 10f) / PanelScale : 0f;
                float ceiling = safe.height - (MidnightLandHud.TopHeight + 24f) / PanelScale - Margin;
                size.y = Mathf.Min(safe.height * HandSheet.PhoneHeight - Margin - lift, ceiling);
            }
            // **Tall sheet, 2026-09-26.** `ISheetFramed.WantsTallSheet` is the
            // minimal opt-in: rather than changing every sheet's band, one
            // flag lets a sheet ask for the space between the top resource
            // bar and the bottom nav instead of the bottom third. Phone only
            // -- a desk sheet is already close to full height.
            else if (!HudLayout.Wide && Sheets.Current is ISheetFramed tallFramed && tallFramed.WantsTallSheet)
            {
                // Land HUD: the chrome sits 8 units below the safe top and the
                // frame is lifted 10 units off the bottom (`Place`), so the
                // reserve carries both plus a 6-unit gap -- without them the
                // tall frame's top edge cut the alert strip's chips in half
                // (2026-09-27, 1080x2340 overlay capture).
                float reserve = MidnightLandHud.Active
                    ? (MidnightLandHud.TopHeight + MidnightLandHud.NavHeight + 24f) / PanelScale + Margin
                    : Margin * 2f;
                float ceiling = safe.height - reserve;
                size.y = Mathf.Max(size.y, ceiling);
            }
            return size;
        }

        /// **How tall a page may be, in panel units.**
        ///
        /// The strip and the action row are always subtracted, even on a
        /// sheet that shows neither: a page built to the slack of a
        /// one-section sheet would overflow the moment that sheet grew a
        /// second page, and every sheet in this game grows.
        public static float BandHeight
        {
            get
            {
                float h = FrameSizeScreen().y * PanelScale
                          - (BorderPx + HeadPx + StripPx + ActionsPx + BodyPadPx + (MidnightLandHud.Active ? 10f : 0f));
                return Mathf.Max(80f, h);
            }
        }

        /// How many `rowPx`-tall rows fit on one page once `reservePx` (an
        /// eyebrow, a note) has been taken off the top. Never zero, so a
        /// pagination loop cannot spin.
        public static int RowsThatFit(float rowPx, float reservePx = 0f) =>
            Mathf.Max(1, Mathf.FloorToInt((BandHeight - reservePx) / Mathf.Max(1f, rowPx)));

        /// Does a block of `px` panel units fit one page, with `reservePx`
        /// already spoken for?
        public static bool Fits(float px, float reservePx = 0f) =>
            px + reservePx <= BandHeight;

        Vector2 lastPanelSize;
        void LateUpdate()
        {
            if (Instance == null) Instance = this;
            EnsureBuilt();
            if (root == null) return;

            // The chart instrument (and the rest of this document -- the
            // sheet card, the place label, the ashore rail) is a UI Toolkit
            // tree with no OnGUI event of its own, so none of the IMGUI
            // `ShipyardModal.IsOpen`/`GameMenus.Current` bail-outs the rest
            // of the HUD uses ever touched it (2026-09-26 review: the chart
            // dial and "heavy to the east" line drew straight through the
            // Home/Pause/Save card, and the shipyard modal sits at the same
            // sorting order this document uses so it needed the same gate).
            // Hiding the WHOLE root rather than picking apart which child is
            // "the chart" is deliberate: nothing in this document should be
            // interactive while either owns the screen (both already block
            // world input), and a sheet left open behind one would still be
            // ticking its refresh/pager underneath for no one to see.
            bool suppressed = SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None;
            root.style.display = suppressed ? DisplayStyle.None : DisplayStyle.Flex;
            if (suppressed) return;

            // A page swap destroys the element a slide is running on, and a
            // scheduler on a dead element never reports finishing. Without
            // this, one interrupted swipe would lock the pager for the rest
            // of the session.
            if (animating && Time.unscaledTime > animDeadline) animating = false;

            bool on = Sheets.SuppressLegacy;
            runtimePanel.referenceResolution = MidnightLandHud.Active && !HudLayout.Wide
                ? new Vector2Int(430, 932) : originalResolution;
            land.Tick(root);
            sea.Tick(root);
            var panelSize = new Vector2(root.resolvedStyle.width, root.resolvedStyle.height);
            if (midnight != MidnightLandHud.Active || (panelSize - lastPanelSize).sqrMagnitude > 1f)
            {
                lastPanelSize = panelSize;
                midnight = MidnightLandHud.Active;
                OnSheetChanged();
            }
            place.Tick(on && !midnight, root);
            rail.Tick(on && !midnight, root);
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
            if (s == null) { FrameOpen = false; buildingFocus.Tick(null); return; }

            if (!s.StillValid) { Sheets.Close(); FrameOpen = false; return; }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.25f;
                s.Refresh();
                RelabelTabs();
            }

            Place(s);
            buildingFocus.Tick(s);
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
            if (MidnightLandHud.Active)
            {
                var size = FrameSizeScreen();
                w = size.x; h = size.y;
                yBottom += (MidnightLandHud.NavHeight + 10f) / PanelScale;
            }
            FrameRect = new Rect(x, Screen.height - (yBottom + h), w, h);
            FrameOpen = true;
            // Told once a frame, to the IMGUI HUD's own space. It used to be
            // the other way round — the card read `BottomClustersTop` and got
            // out of the prompt stack's way — and that made the sheet's size
            // depend on the HUD's, which is the loop a fixed frame exists to
            // cut. The sheet is the fixed thing now; the HUD moves.
            var claimed = FrameRect;
            if (MidnightLandHud.Active) claimed.yMax = MidnightLandHud.NavigationRect.yMax;
            HudLayout.ClaimSheet(claimed);

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
