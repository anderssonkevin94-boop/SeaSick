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
        SelectionRing ring;

        ISheet built;
        float nextRefresh;
        float nextScan;

        // --- creation ---

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("SheetHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<SheetHost>();
            go.AddComponent<WorldPicker>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            var settings = Resources.Load<PanelSettings>("UI/SheetPanel");
            if (settings == null)
            {
                Debug.LogWarning("[Sheets] Resources/UI/SheetPanel.asset is missing — the sheet HUD will not draw.");
                enabled = false;
                return;
            }

            doc = gameObject.AddComponent<UIDocument>();
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
            ring = gameObject.AddComponent<SelectionRing>();
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
            if (root == null) return;

            bool on = Sheets.SuppressLegacy;
            place.Tick(on, root);
            rail.Tick(on, root);

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

            if (HudLayout.Wide) PlaceBeside(s, W, H, mL, mR, mT, mB);
            else PlaceDocked(W, H, mL, mR, mB);
        }

        void PlaceBeside(ISheet s, float W, float H, float mL, float mR, float mT, float mB)
        {
            card.RemoveFromClassList(SheetTheme.Docked);

            float w = Mathf.Clamp(W * 0.32f, 400f, 440f);
            w = Mathf.Min(w, W - mL - mR);
            card.style.width = w;
            card.style.right = StyleKeyword.Auto;
            card.style.bottom = StyleKeyword.Auto;

            // **The floor is the legacy prompt stack, not the screen edge.**
            // "Cast off (space)" is drawn bottom-centre by `AnchorController`
            // and is the one control the sheet HUD deliberately leaves to
            // IMGUI, so a card allowed to run to the bottom margin covers the
            // way out. `BottomClustersTop` is where those clusters begin, in
            // screen pixels, reported by the panels that drew them.
            float floorPanel = H - mB;
            float clusters = HudLayout.BottomClustersTop;
            if (clusters > 0f)
                floorPanel = Mathf.Min(floorPanel, clusters * (H / Mathf.Max(1f, Screen.height)) - 10f);
            float ceiling = mT;
            card.style.maxHeight = Mathf.Max(160f, floorPanel - ceiling);

            var cam = Camera.main;
            Vector2 p;
            bool infront = false;
            if (cam != null)
            {
                var sp = cam.WorldToScreenPoint(s.AnchorWorld);
                infront = sp.z > 0f;
                p = ToPanel(new Vector2(sp.x, sp.y));
            }
            else p = new Vector2(W * 0.5f, H * 0.5f);

            float h = card.resolvedStyle.height;
            if (h <= 1f) h = 200f;

            // **Beside, on whichever side of the object has more room — and
            // never ON it.** Preferring the right unconditionally put the
            // ship's manifest straight over the ship: she was in the right
            // half of a wide window, the card was 430 px, and it was allowed
            // to clamp back across its own anchor rather than go left. The
            // side is chosen by measuring both gaps first, and whichever wins
            // the card is then held clear of the anchor point.
            const float gap = 40f;
            float roomRight = (W - mR) - p.x;
            float roomLeft = p.x - mL;
            bool right = roomRight >= roomLeft;
            if (!infront) { right = false; }

            float x = right ? p.x + gap : p.x - gap - w;
            x = Mathf.Clamp(x, mL, Mathf.Max(mL, W - w - mR));

            // The clamp above can drag the card back over the anchor when
            // neither side really fits. If it has, push it off the point in
            // whichever direction still has somewhere to go.
            if (infront && x < p.x + gap && x + w > p.x - gap)
            {
                float toRight = Mathf.Min(p.x + gap, W - w - mR);
                float toLeft = Mathf.Max(p.x - gap - w, mL);
                bool canRight = toRight >= p.x + gap - 0.5f;
                bool canLeft = toLeft + w <= p.x - gap + 0.5f;
                if (canRight && (right || !canLeft)) { x = toRight; right = true; }
                else if (canLeft) { x = toLeft; right = false; }
            }

            float y = Mathf.Clamp(p.y - h * 0.5f, ceiling, Mathf.Max(ceiling, floorPanel - h));

            card.style.left = x;
            card.style.top = y;
            Shadow(x, y, w, h);

            // The tail: a hairline from the card's near edge out to the
            // object, with a small dot where it lands. Rotated rather than
            // drawn, because UI Toolkit has no line and a rotated 2 px element
            // is one draw with no mesh of its own.
            if (!infront || p.x < 0f || p.x > W || p.y < 0f || p.y > H)
            {
                tail.style.display = DisplayStyle.None;
                tailDot.style.display = DisplayStyle.None;
                return;
            }
            float ex = right ? x : x + w;
            float ey = Mathf.Clamp(p.y, y + 12f, y + h - 12f);
            var d = new Vector2(p.x - ex, p.y - ey);
            float len = d.magnitude;
            tail.style.display = len > 6f ? DisplayStyle.Flex : DisplayStyle.None;
            tail.style.left = ex;
            tail.style.top = ey;
            tail.style.width = len;
            tail.style.rotate = new StyleRotate(
                new Rotate(new Angle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, AngleUnit.Degree)));

            tailDot.style.display = DisplayStyle.Flex;
            tailDot.style.left = p.x - 4.5f;
            tailDot.style.top = p.y - 4.5f;
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
