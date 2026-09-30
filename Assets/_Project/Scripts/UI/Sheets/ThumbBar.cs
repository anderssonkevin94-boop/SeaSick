using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The thumb bar, 2026-09-30 (island UI restructure, phase 1A).**
    ///
    /// Three layers on the island: the top bar is status (read it,
    /// `MidnightLandHud`), the world is for tapping things, and this bar at
    /// the bottom is the actions. Controls live in the bottom 40 % of a phone
    /// held upright, under one thumb.
    ///
    /// Two modes, one strip of buttons:
    /// <list type="bullet">
    /// <item>**Normal** -- [Camp] [Build] [Ship], Build the primary. Shown at
    ///   an island camp (`MidnightLandHud.Active`) while no sheet is open and
    ///   nothing is being placed. A sheet HIDES the bar rather than sitting
    ///   above it: the sheet is the action surface while it is open, and the
    ///   bar would only steal a docked sheet's bottom 90 pt.</item>
    /// <item>**Placement** -- [Cancel] [Turn] [confirm], confirm the primary
    ///   and rightmost, plus an instruction card at the top of the screen
    ///   (under the status bar and alert strip). Driven by the siting code
    ///   through `ShowPlacement` and friends; shown whether or not a camp
    ///   exists, because the first campfire is placed before there is one.</item>
    /// </list>
    ///
    /// The static half is the API and the published rects; `View` is the
    /// UI Toolkit tree `SheetHost` owns and ticks. Built once, re-texted only
    /// when a setter changed something (`stamp`), so a frame allocates
    /// nothing.
    public static class ThumbBar
    {
        /// Bar height, side margin and the desk-width cap, in the land
        /// panel's units (the 430x932 reference, so ~points on the phone).
        public const float Height = 74f;
        public const float Side = 12f;
        public const float MaxWide = 520f;
        /// Gap between the safe area's bottom edge and the bar.
        public const float BottomGap = 6f;
        /// Gap kept clear above the bar by things that stack on it.
        public const float Gap = 8f;

        public static bool PlacementActive { get; private set; }

        /// The bar's rect in screen pixels, GUI space (origin top-left) --
        /// the same space as `MidnightLandHud.ResourcesRect`. Zero while
        /// the bar is hidden.
        public static Rect Rect { get; private set; }
        /// The placement instruction card, same space. Zero unless placing.
        public static Rect CardRect { get; private set; }
        /// True while the bar is drawn (either mode).
        public static bool Visible { get; private set; }
        /// Panel units from the bottom edge of the panel that the bar takes,
        /// with `Gap` above it: where something that must sit ABOVE the bar
        /// puts its `bottom`. Zero while hidden.
        public static float ReservePanel { get; private set; }

        /// Does a GUI-space point land on the bar, the placement card, or
        /// the Next card above the bar (`NextCard`, phase 2)?
        public static bool Blocks(Vector2 guiPoint) =>
            Rect.Contains(guiPoint) || CardRect.Contains(guiPoint) || NextCard.Blocks(guiPoint);

        /// Something stacked on the bar (the Next card) raises the reserve
        /// so the food notice sits above it too. Called
        /// after the bar's own tick, the same frame.
        internal static void RaiseReserve(float panelUnits)
        {
            if (panelUnits > ReservePanel) ReservePanel = panelUnits;
        }

        /// **The bar's lane**, in panel units: the left edge and width of
        /// its buttons (the 12-unit margins already taken) and its bottom.
        /// Shared with `NextCard`, which sits in the same lane above it --
        /// or in the bar's own slot when there is no camp and so no bar.
        internal static void Lane(VisualElement root, out float left, out float width, out float bottom)
        {
            float scale = SheetHost.PanelScale;
            float W = root.resolvedStyle.width;
            if (float.IsNaN(W) || W < 1f) W = Screen.width * scale;
            var safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
            float sl = safe.xMin * scale, sr = (Screen.width - safe.xMax) * scale;
            float sb = safe.yMin * scale;
            if (HudLayout.Wide)
            {
                width = Mathf.Min(MaxWide, W - sl - sr - Side * 2f);
                left = (W - width) * .5f;
            }
            else
            {
                left = sl + Side;
                width = W - sl - sr - Side * 2f;
            }
            bottom = sb + BottomGap;
        }

        static string title = "", hint = "", status = "", confirmLabel = "Build here";
        static bool statusOk = true, canConfirm = true;
        static Action onCancel, onTurn, onConfirm;
        static int stamp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            PlacementActive = Visible = false;
            Rect = CardRect = Rect.zero;
            ReservePanel = 0f;
            onCancel = onTurn = onConfirm = null;
            title = hint = status = ""; confirmLabel = "Build here";
            statusOk = canConfirm = true;
            stamp++;
        }

        /// Replaces Camp·Build·Ship with [Cancel] [Turn] [confirmLabel]
        /// (Turn hidden when `onTurn` is null; confirm is the primary,
        /// rightmost) and shows the instruction card at the top.
        ///
        /// **`onCancel == null` is the one forced placement (2026-09-30):**
        /// the tombstone after a death (`GravePlacementFlow`) has no way
        /// out, so the Cancel button is hidden and the confirm button takes
        /// the whole bar. Every other caller passes a Cancel and is
        /// unchanged -- the GDD's "always a Cancel" has this one exception.
        public static void ShowPlacement(string title, string hint, Action onCancel, Action onTurn,
                                         Action onConfirm, string confirmLabel = "Build here")
        {
            ThumbBar.title = title ?? "";
            ThumbBar.hint = hint ?? "";
            ThumbBar.onCancel = onCancel;
            ThumbBar.onTurn = onTurn;
            ThumbBar.onConfirm = onConfirm;
            ThumbBar.confirmLabel = string.IsNullOrEmpty(confirmLabel) ? "Build here" : confirmLabel;
            status = ""; statusOk = true; canConfirm = true;
            PlacementActive = true;
            stamp++;
        }

        /// Status line under the hint: ok = moss, not ok = ember.
        /// `canConfirm` enables or disables the confirm button.
        public static void SetPlacementStatus(string status, bool ok, bool canConfirm)
        {
            status ??= "";
            if (status == ThumbBar.status && ok == statusOk && canConfirm == ThumbBar.canConfirm) return;
            ThumbBar.status = status; statusOk = ok; ThumbBar.canConfirm = canConfirm;
            stamp++;
        }

        public static void SetPlacementHint(string hint)
        {
            hint ??= "";
            if (hint == ThumbBar.hint) return;
            ThumbBar.hint = hint; stamp++;
        }

        public static void SetConfirmLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) label = "Build here";
            if (label == confirmLabel) return;
            confirmLabel = label; stamp++;
        }

        /// Back to normal mode.
        public static void HidePlacement()
        {
            if (!PlacementActive) return;
            PlacementActive = false;
            onCancel = onTurn = onConfirm = null;
            CardRect = Rect.zero;
            stamp++;
        }

        // --- presses ---

        /// **Camp opens the Camp sheet (2026-09-30, phase 4)**, the one camp
        /// hub that replaced the ledger drawer.
        static void PressCamp()
        {
            var camp = MidnightLandHud.Camp;
            if (camp != null && camp.Ledger != null) Sheets.Open(new CampSheet(camp));
        }

        static void PressBuild()
        {
            var camp = MidnightLandHud.Camp;
            if (camp != null) Sheets.Open(new BuildSheet(camp));
        }

        static void PressShip()
        {
            var sheet = SheetBootstrap.ShipFor();
            if (sheet != null) Sheets.Open(sheet);
        }

        static void PressCancel() => onCancel?.Invoke();
        static void PressTurn() => onTurn?.Invoke();
        static void PressConfirm() { if (canConfirm) onConfirm?.Invoke(); }

        // --- the tree ---

        internal sealed class View
        {
            readonly VisualElement bar, card;
            readonly Button camp, build, ship, cancel, turn, confirm;
            readonly Label confirmText, titleText, hintText, statusText;
            int seen = -1;
            bool shownPlacing;
            bool shown = true;

            public View(VisualElement root)
            {
                bar = new VisualElement();
                bar.AddToClassList("thumb-bar");
                // Position, not Ignore: the gaps between the buttons are the
                // bar too, so `WorldPicker`'s `panel.Pick` and `UIBlocker`'s
                // rect agree about what a finger there is on.
                bar.pickingMode = PickingMode.Position;
                root.Add(bar);

                camp = Btn(Glyph.Kind.Camp, "Camp", PressCamp, false, out _, "The camp: what needs you, the fire, the buildings, people, stores");
                build = Btn(Glyph.Kind.Build, "Build", PressBuild, true, out _, "Build: put up a new building");
                ship = Btn(Glyph.Kind.Ship, "Ship", PressShip, false, out _, "The ship: hold, crew, chart");
                cancel = Btn(Glyph.Kind.Cancel, "Cancel", PressCancel, false, out _, "Stop placing");
                turn = Btn(Glyph.Kind.Turn, "Turn", PressTurn, false, out _, "Turn it");
                confirm = Btn(Glyph.Kind.Confirm, "Build here", PressConfirm, true, out confirmText, "Place it here");

                card = new VisualElement();
                card.AddToClassList("thumb-card");
                card.pickingMode = PickingMode.Position;
                titleText = new Label { pickingMode = PickingMode.Ignore };
                titleText.AddToClassList("thumb-card-title");
                hintText = new Label { pickingMode = PickingMode.Ignore };
                hintText.AddToClassList("thumb-card-hint");
                statusText = new Label { pickingMode = PickingMode.Ignore };
                statusText.AddToClassList("thumb-card-status");
                card.Add(titleText); card.Add(hintText); card.Add(statusText);
                root.Add(card);

                Hide();
            }

            Button Btn(Glyph.Kind kind, string text, Action click, bool primary, out Label label, string tip)
            {
                var b = new Button(click);
                b.AddToClassList("thumb-btn");
                if (primary) b.AddToClassList("thumb-btn--primary");
                b.tooltip = tip;
                var g = new Glyph(kind) { Tint = primary ? Dark : MidnightLandHud.Pearl };
                b.Add(g);
                label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("thumb-label");
                b.Add(label);
                bar.Add(b);
                return b;
            }

            public void Hide()
            {
                if (shown)
                {
                    shown = false;
                    bar.style.display = DisplayStyle.None;
                    card.style.display = DisplayStyle.None;
                }
                Visible = false;
                Rect = CardRect = Rect.zero;
                ReservePanel = 0f;
            }

            /// Once a frame from `SheetHost.LateUpdate`, BEFORE the land HUD
            /// ticks, so the food notice reads this
            /// frame's reserve.
            public void Tick(VisualElement root)
            {
                bool placing = PlacementActive;
                bool normal = !placing && MidnightLandHud.Active && Sheets.Current == null
                              && !SeaSick.UI.CampSiting.Placing;
                if (!placing && !normal) { Hide(); return; }

                if (!shown)
                {
                    shown = true;
                    bar.style.display = DisplayStyle.Flex;
                    seen = -1;
                }
                if (placing != shownPlacing || seen != stamp) Apply(placing);

                float scale = SheetHost.PanelScale;
                var safe = Screen.safeArea;
                if (safe.width < 1f || safe.height < 1f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
                float st = (Screen.height - safe.yMax) * scale;
                Lane(root, out float left, out float width, out float bottom);
                // The buttons carry 4 units of side margin each, so the row
                // is widened by that much and the outer buttons still meet
                // the 12-unit margin.
                bar.style.left = left - 4f;
                bar.style.width = width + 8f;
                bar.style.bottom = bottom;
                bar.style.height = Height;

                float inv = 1f / Mathf.Max(1e-4f, scale);
                Rect = new Rect(left * inv, Screen.height - (bottom + Height) * inv, width * inv, Height * inv);
                Visible = true;
                ReservePanel = bottom + Height + Gap;

                if (!placing) { CardRect = Rect.zero; return; }

                // The card: under the status bar, the alert strip and the
                // rest of the top chrome when the land HUD is up (all in ResourcesRect), or
                // under the safe top when there is no camp yet.
                float top = st + 8f;
                if (MidnightLandHud.Active && MidnightLandHud.ResourcesRect.height > 0f)
                    top = MidnightLandHud.ResourcesRect.yMax * scale + 8f;
                card.style.left = left;
                card.style.width = width;
                card.style.top = top;
                var wb = card.worldBound;
                CardRect = float.IsNaN(wb.height) || wb.height < 1f
                    ? new Rect(left * inv, top * inv, width * inv, 96f * inv)
                    : new Rect(wb.x * inv, wb.y * inv, wb.width * inv, wb.height * inv);
            }

            void Apply(bool placing)
            {
                seen = stamp;
                shownPlacing = placing;
                var on = DisplayStyle.Flex; var off = DisplayStyle.None;
                camp.style.display = build.style.display = ship.style.display = placing ? off : on;
                confirm.style.display = placing ? on : off;
                // No Cancel when the caller gave none (the forced grave).
                cancel.style.display = placing && onCancel != null ? on : off;
                turn.style.display = placing && onTurn != null ? on : off;
                card.style.display = placing ? on : off;
                if (!placing) return;
                camp.RemoveFromClassList("thumb-btn--on");
                confirmText.text = confirmLabel;
                titleText.text = title;
                hintText.text = hint;
                hintText.style.display = hint.Length > 0 ? on : off;
                statusText.text = status;
                statusText.style.display = status.Length > 0 ? on : off;
                statusText.EnableInClassList("thumb-card-status--ok", statusOk);
                statusText.EnableInClassList("thumb-card-status--bad", !statusOk);
                confirm.SetEnabled(canConfirm);
            }
        }

        static readonly Color Dark = new Color32(11, 23, 32, 255);

        /// **The bar's pictograms**, stroked like `HudGlyph` / `LandIcon`
        /// (no font glyphs, no emoji -- Kevin): a tent, a hammer, a ship,
        /// the placement trio ✕ ↻ ✓, and the Next card's chevron. A 24-unit grid.
        internal sealed class Glyph : VisualElement
        {
            public enum Kind { Camp, Build, Ship, Cancel, Turn, Confirm, Chevron }

            readonly Kind kind;
            Color tint = MidnightLandHud.Pearl;

            public Glyph(Kind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("thumb-glyph");
                generateVisualContent += Draw;
            }

            public Color Tint
            {
                get => tint;
                set { if (value == tint) return; tint = value; MarkDirtyRepaint(); }
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                if (s <= 0f) return;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                void Line(float x, float y, float a, float b)
                { p.BeginPath(); p.MoveTo(V(x, y)); p.LineTo(V(a, b)); p.Stroke(); }
                p.strokeColor = tint;
                p.fillColor = tint;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                p.lineWidth = 2.2f * s;
                switch (kind)
                {
                    case Kind.Camp:
                        // A tent with its door flap open, on a ground line.
                        p.BeginPath(); p.MoveTo(V(3, 20)); p.LineTo(V(12, 4)); p.LineTo(V(21, 20)); p.Stroke();
                        Line(2, 20, 22, 20);
                        p.BeginPath(); p.MoveTo(V(8.5f, 20)); p.LineTo(V(12, 13)); p.LineTo(V(15.5f, 20)); p.Stroke();
                        break;
                    case Kind.Build:
                        // A hammer: a thick head across a handle.
                        Line(5, 20, 14, 11);
                        p.lineWidth = 4.6f * s;
                        Line(10.5f, 6.5f, 17.5f, 13.5f);
                        break;
                    case Kind.Ship:
                        // Hull, mast, sail.
                        p.BeginPath(); p.MoveTo(V(3, 15)); p.LineTo(V(21, 15)); p.LineTo(V(17.5f, 20.5f));
                        p.LineTo(V(6.5f, 20.5f)); p.ClosePath(); p.Stroke();
                        Line(11, 15, 11, 3);
                        p.BeginPath(); p.MoveTo(V(11, 4)); p.LineTo(V(19, 12)); p.LineTo(V(11, 12)); p.ClosePath(); p.Fill();
                        break;
                    case Kind.Cancel:
                        p.lineWidth = 2.6f * s;
                        Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                        break;
                    case Kind.Turn:
                    {
                        // A clockwise arc, 120° round to 30°, and its head.
                        p.BeginPath(); p.Arc(V(12, 12), 7f * s, 120f, 360f); p.Stroke();
                        p.BeginPath(); p.Arc(V(12, 12), 7f * s, 0f, 30f); p.Stroke();
                        float a = 30f * Mathf.Deg2Rad;
                        var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        var t = new Vector2(-n.y, n.x);          // clockwise, y down
                        var end = new Vector2(12f, 12f) + n * 7f;
                        var tip = end + t * 3.6f;
                        var w1 = end + n * 3.2f - t * .6f;
                        var w2 = end - n * 3.2f - t * .6f;
                        p.BeginPath(); p.MoveTo(tip * s); p.LineTo(w1 * s); p.LineTo(w2 * s); p.ClosePath(); p.Fill();
                        break;
                    }
                    case Kind.Confirm:
                        p.lineWidth = 2.8f * s;
                        p.BeginPath(); p.MoveTo(V(5, 12.5f)); p.LineTo(V(10, 17.5f)); p.LineTo(V(19, 7)); p.Stroke();
                        break;
                    case Kind.Chevron:
                        // The Next card's "go": a heavy right chevron.
                        p.lineWidth = 3.2f * s;
                        p.BeginPath(); p.MoveTo(V(9, 5)); p.LineTo(V(16, 12)); p.LineTo(V(9, 19)); p.Stroke();
                        break;
                }
            }
        }
    }
}
