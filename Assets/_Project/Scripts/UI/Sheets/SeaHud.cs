using System.Collections.Generic;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The sea HUD, 2026-09-30 (island UI restructure, phase 6).** Kevin's
    /// approved mockups "8 · Sea: sailing" and "8c · Anchored off a fresh
    /// island": the IMGUI sea HUD retired into the house look, the same
    /// layers as the island (top = how are we doing, bottom = what can I do).
    /// <list type="bullet">
    /// <item>**Top bar** (the island bar's look, shown at sea and off an
    ///   island with no camp; at her own camp the island bar is up instead):
    ///   hold "12/34" with the bag (amber from 80 %, ember overloaded; tap =
    ///   the Backpack, ship side, or its ship-only page with no camp) · the
    ///   aboard count (tap = the Ship sheet) · the sea-state word (calm ...
    ///   wild, "BREAKERS" / "broaching" in ember; off a fresh island "Island_7"
    ///   instead -- until 2026-10-03 "Island_7 · 20%", the share the
    ///   landing party had explored, gone with the fog of war -- and no
    ///   day) · Day N · ☰ (the pause menu).
    ///   Replaces `HoldChip`, `PauseChip`'s Ledger/Menu rail and the sea
    ///   line of `HelmInput`'s panel.</item>
    /// <item>**One alert chip + "+N"** under it: "MAN OVERBOARD · Bo" (tap =
    ///   steer toward, as the marker's tap does), "Taking water 40% · 2
    ///   bailing" / "Hull 82% · repair" (tap = the Ship sheet), "Overloaded
    ///   · throw 5 over" (tap = `VoyageManager.Jettison`). Replaces
    ///   `StatusHUD`'s panel and `Bilge`'s "over the side" button.</item>
    /// <item>**The action card** above the thumb: whatever `SeaActions` was
    ///   offered this frame, the highest priority, in the Next card's look;
    ///   an offer that is information draws with no chevron and takes no
    ///   tap. Hidden while the combat row is up (`CombatHud`).</item>
    /// <item>**The order strip**, under way (2026-10-02, was the helm row;
    ///   2026-10-03 DREDGE controls: it shows the LIVE drive, not a latched
    ///   order): one slim line at the bottom of the safe area, "slow ahead ·
    ///   6.2 m/s" beside the live-throttle bar (full-ahead tick, the burn
    ///   stretch past it lit amber only while the boost is pushing). Information only: it does not block,
    ///   so a thumb resting there starts the stick. Kevin, after playing the
    ///   old 96-120 px row: "I don't like how far up the joystick is pushed
    ///   given all the UI below it", and "remove the ease and the oars from
    ///   the UI" -- the Oars and Ease toggles are gone (rowing stays on
    ///   desktop R; easing has no control left and stays off). Replaces the
    ///   rest of the old `HelmInput.OnGUI` readout (and the deleted `TouchHelm`'s); the floating
    ///   stick ring itself stays IMGUI.</item>
    /// <item>**The boost button** (2026-10-03): a 48 design-px round bolt at
    ///   the bottom-right, level with the order strip, which gives up its
    ///   right end to it. Tap = `HelmInput.ToggleBoost` (the Haste button:
    ///   off / armed = lit outline / boosting = filled). It is a real
    ///   button: `Blocks` covers `BoostRect`, so a thumb on it never starts
    ///   the stick. `HelmRect` is the whole bottom row (strip + button) so
    ///   the edge markers keep off both.</item>
    /// <item>**First-use hint**: a faint dashed ring and "Drag to sail · let
    ///   go to stop" in the stick zone until the stick has been used once
    ///   (`GestureHints`).</item>
    /// </list>
    ///
    /// **Layout is in DESIGN px** of the 390-wide mockup, one `style.scale`
    /// per root, exactly as `CombatHud` does: upright a design px is
    /// `safe.width / 390` screen px; on a desk it is one panel unit and the
    /// bar, row and card are centred. The panel's reference resolution is
    /// never touched. Built once; the bar and chips re-text on a 0.25 s
    /// tick when a number moves, the card and order strip each frame only when
    /// a word changes. No per-frame allocation.
    ///
    /// **Taps never reach the helm or the world**: `Blocks` (read by
    /// `UIBlocker.SheetBlocked`) covers the bar, the chip and the card while
    /// it is a button; an information card and the order strip leave the
    /// stick free under them. `HelmRect` is still the strip's rect, so
    /// `Overlaps` keeps the IMGUI edge markers off it. The action card and
    /// the combat row (`CombatHud.BottomPx`) stack just above the strip, and
    /// the bottom-centre IMGUI slot (`HudLayout.Slot.Wheel`) is reserved to
    /// the highest of them so the prompts left in IMGUI still stack above
    /// all of it.
    public static class SeaHud
    {
        public const float DesignWidth = 390f;
        public const float Side = 10f;
        public const float BarTop = 10f, BarHeight = 52f;
        public const float AlertTop = 72f, AlertHeight = 44f;
        /// The order strip: its margin off the safe area's bottom edge and
        /// its height, design px (2026-10-02: was a 120/96 px helm row on a
        /// 16 px margin).
        public const float HelmBottom = 6f, HelmHeight = 30f;
        /// The boost button's side (design px; 48 is >= 44 pt on a phone) and
        /// the gap between it and the strip.
        public const float BoostSize = 48f, BoostGap = 8f;
        /// Gap between the order strip (or the thumb bar) and the card, design px.
        public const float CardGap = 14f;
        const float LaneWidth = DesignWidth - Side * 2f;
        const float WideBarWidth = 560f;

        public static bool TopBarShowing { get; private set; }
        public static bool HelmShowing { get; private set; }
        /// GUI space (origin top-left), screen pixels. Zero while hidden.
        public static Rect TopRect { get; private set; }
        public static Rect AlertRect { get; private set; }
        /// The whole bottom row: the order strip AND the boost button.
        public static Rect HelmRect { get; private set; }
        /// The boost button alone (GUI space; zero while hidden).
        public static Rect BoostRect { get; private set; }
        /// Panel units from the panel's top edge at which the chart
        /// instrument sits under the bar (0 = no bar, the chart keeps its
        /// corner). `ChartInstrument` reads it.
        public static float ChartTopPanel { get; private set; }

        static bool cardTaps;
        static int tickFrame = -10;

        /// Does a GUI-space point land on the sea HUD? Stale rects (the
        /// document hidden behind a menu) never block.
        public static bool Blocks(Vector2 guiPoint)
        {
            if (Time.frameCount - tickFrame > 1) return false;
            // Not `HelmRect`: the order strip is information, and the very
            // bottom of the screen is where the thumb starts the stick.
            return TopRect.Contains(guiPoint) || AlertRect.Contains(guiPoint) || BoostRect.Contains(guiPoint)
                   || (cardTaps && SeaActions.Visible && SeaActions.Rect.Contains(guiPoint));
        }

        /// Does a GUI-space rect touch the sea HUD (2026-09-30, for the IMGUI
        /// edge markers: rescue arrows, squall arrow)?
        public static bool Overlaps(Rect guiRect)
        {
            if (Time.frameCount - tickFrame > 1) return false;
            return TopRect.Overlaps(guiRect) || AlertRect.Overlaps(guiRect) || HelmRect.Overlaps(guiRect)
                   || (cardTaps && SeaActions.Visible && SeaActions.Rect.Overlaps(guiRect));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            TopBarShowing = HelmShowing = cardTaps = false;
            TopRect = AlertRect = HelmRect = BoostRect = Rect.zero;
            ChartTopPanel = 0f;
            tickFrame = -10;
        }

        static readonly Color Pearl = new Color32(232, 242, 246, 255);
        static readonly Color Amber = new Color32(242, 196, 109, 255);
        static readonly Color Ember = new Color32(246, 146, 128, 255);
        static readonly Color Ice = new Color32(164, 210, 232, 255);
        static readonly Color Dark = new Color32(11, 23, 32, 255);

        enum AlertKind { None, Overboard, Water, Hull, Overloaded }

        internal sealed class View
        {
            // --- top bar
            readonly VisualElement top;
            readonly Button holdBtn, crewBtn, menuBtn;
            readonly VisualElement seaItem, dayItem;
            readonly Label holdText, crewText, seaText, placeText, dayText;
            readonly SeaGlyph seaGlyph;
            // --- alerts
            readonly VisualElement alertRow;
            readonly Button chip, more;
            // --- card
            readonly Button card;
            readonly Label eyebrow, title, detail;
            readonly VisualElement track, fill, go;
            // --- order strip
            readonly VisualElement helmRow;
            readonly Label orderText, speedText;
            readonly VisualElement barBurn, barFill, barTick;
            // --- boost button
            readonly Button boostBtn;
            readonly SeaGlyph boostGlyph;
            // --- hint
            readonly VisualElement hint;
            // The look hint (DREDGE controls step 2): text only, in the upper
            // camera zone. Never a target (`PickingMode.Ignore`, and `Blocks`
            // does not know it), so a drag that starts under it reaches the camera.
            readonly VisualElement lookHint;

            bool topShown = true, alertShown = true, cardShown = true, helmShown = true, hintShown = true, boostShown = true, lookHintShown = true;
            float nextRefresh;

            // Cached text keys (strings are rebuilt only when these move).
            int holdKey = int.MinValue, crewKey = int.MinValue, dayKey = int.MinValue;
            Island placeIsland;
            int holdTone = -1;
            string seaWord;
            Color seaColour;
            bool placeMode;
            AlertKind shownKind = AlertKind.None;
            int alertKey = int.MinValue, moreKey = int.MinValue;
            Swimmer overboard;
            int jettisonN = 5;

            // Card state.
            string cEyebrow, cTitle, cDetail;
            bool cEnabled = true, cBar = true;
            float cProgress = -2f;

            // Helm state.
            string shownOrder;
            bool shownBurn, shownAstern;
            bool shownArmed, shownBoosting;
            int speedKey = int.MinValue;
            float fillShown = -1f, tickShown = -1f;
            static readonly string[] speedTexts = new string[400];

            // The scale each root was last given (a root hidden when the
            // panel rescaled picks the new one up when it shows).
            float kTop = -1f, kAlert = -1f, kHelm = -1f, kBoost = -1f, kCard = -1f, kHint = -1f, kLookHint = -1f;

            static void SetScale(VisualElement e, ref float last, float k)
            {
                if (Mathf.Approximately(k, last)) return;
                last = k;
                e.style.scale = new Scale(new Vector3(k, k, 1f));
            }

            HelmInput helm;
            ShipMotor helmMotor;

            public View(VisualElement root)
            {
                var style = Resources.Load<StyleSheet>("UI/SeaHud");
                if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);

                // --- top bar
                top = Box("sea-top");
                holdBtn = TopButton(new HudGlyph(HudGlyph.Kind.Backpack), OpenHold, "The ship's hold", out holdText);
                crewBtn = TopButton(new LandIcon("crew"), OpenShip, "Hands aboard: the Ship sheet", out crewText);
                seaItem = Box("sea-top-item");
                seaGlyph = new SeaGlyph(SeaGlyph.Kind.Wave);
                seaGlyph.AddToClassList("sea-top-icon");
                seaItem.Add(seaGlyph);
                seaText = Text(seaItem, "sea-top-text");
                placeText = Text(seaItem, "sea-top-text");
                placeText.AddToClassList("sea-top-text--plain");
                seaItem.AddToClassList("sea-top-place");
                top.Add(seaItem);
                dayItem = Box("sea-top-item");
                // Never the one that gives way (2026-09-30: "BREAKERS" cut
                // it to "Day 1..."); the sea word shrinks first.
                dayItem.AddToClassList("sea-top-day");
                dayText = Text(dayItem, "sea-top-text");
                dayText.AddToClassList("sea-top-text--plain");
                top.Add(dayItem);
                menuBtn = new Button(SeaSick.UI.Menus.GameMenus.TogglePause) { text = "" };
                menuBtn.AddToClassList("sea-top-item");
                menuBtn.AddToClassList("sea-top-menu");
                menuBtn.tooltip = "Menu";
                var burger = new SeaGlyph(SeaGlyph.Kind.Menu);
                burger.AddToClassList("sea-top-icon");
                menuBtn.Add(burger);
                top.Add(menuBtn);
                root.Add(top);

                // --- alert chip + "+N"
                alertRow = Box("sea-alerts");
                chip = new Button(TapAlert) { text = "" };
                chip.AddToClassList("sea-chip");
                alertRow.Add(chip);
                more = new Button(OpenShip) { text = "" };
                more.AddToClassList("sea-chip");
                more.AddToClassList("sea-chip--more");
                more.tooltip = "More: the Ship sheet";
                alertRow.Add(more);
                root.Add(alertRow);

                // --- the action card
                card = new Button(() => SeaActions.Tap()) { text = "" };
                card.AddToClassList("sea-card");
                var body = Box("sea-card-body");
                eyebrow = Text(body, "sea-card-eyebrow");
                title = Text(body, "sea-card-title");
                detail = Text(body, "sea-card-detail");
                track = Box("sea-card-track");
                fill = Box("sea-card-fill");
                track.Add(fill);
                body.Add(track);
                card.Add(body);
                go = Box("sea-card-go");
                var chevron = new ThumbBar.Glyph(ThumbBar.Glyph.Kind.Chevron) { Tint = Dark };
                chevron.AddToClassList("sea-card-chevron");
                go.Add(chevron);
                card.Add(go);
                root.Add(card);

                // --- the order strip: words, then the bar. Every piece
                // ignores picking, so a thumb landing on it reaches the stick.
                helmRow = Box("sea-helm");
                var words = Box("sea-strip-words");
                orderText = Text(words, "sea-order");
                speedText = Text(words, "sea-speed");
                helmRow.Add(words);
                var bar = Box("sea-bar");
                barBurn = Box("sea-bar-burn");
                barFill = Box("sea-bar-fill");
                barTick = Box("sea-bar-tick");
                bar.Add(barBurn);
                bar.Add(barFill);
                bar.Add(barTick);
                helmRow.Add(bar);
                root.Add(helmRow);

                // --- the boost button: a real button (see `BoostRect`), the
                // bolt painted rather than a font glyph.
                boostBtn = new Button(TapBoost) { text = "" };
                boostBtn.AddToClassList("sea-boost");
                boostBtn.tooltip = "Boost";
                boostGlyph = new SeaGlyph(SeaGlyph.Kind.Bolt);
                boostGlyph.AddToClassList("sea-boost-icon");
                boostBtn.Add(boostGlyph);
                root.Add(boostBtn);

                // --- first-use hint (never a target)
                hint = Box("sea-hint");
                var ring = new SeaGlyph(SeaGlyph.Kind.DashedRing);
                ring.AddToClassList("sea-hint-ring");
                hint.Add(ring);
                Text(hint, "sea-hint-text").text = "Drag to sail · let go to stop";
                root.Add(hint);

                lookHint = Box("sea-look-hint");
                Text(lookHint, "sea-hint-text").text = "Drag up here to look around · double-tap to recenter";
                root.Add(lookHint);

                Hide();
            }

            static VisualElement Box(string cls)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList(cls);
                return e;
            }

            static Label Text(VisualElement into, string cls)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList(cls);
                into.Add(l);
                return l;
            }

            void TapBoost()
            {
                var h = Helm();
                if (h != null) h.ToggleBoost();
            }

            Button TopButton(VisualElement icon, System.Action tap, string tip, out Label label)
            {
                var b = new Button(tap) { text = "" };
                b.AddToClassList("sea-top-item");
                b.tooltip = tip;
                icon.AddToClassList("sea-top-icon");
                b.Add(icon);
                label = Text(b, "sea-top-text");
                top.Add(b);
                return b;
            }

            static void Show(VisualElement e, ref bool shown, bool on)
            {
                if (shown == on) return;
                shown = on;
                e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void Hide()
            {
                Show(top, ref topShown, false);
                Show(alertRow, ref alertShown, false);
                Show(card, ref cardShown, false);
                Show(helmRow, ref helmShown, false);
                Show(boostBtn, ref boostShown, false);
                Show(hint, ref hintShown, false);
                Show(lookHint, ref lookHintShown, false);
                TopBarShowing = HelmShowing = cardTaps = false;
                TopRect = AlertRect = HelmRect = BoostRect = Rect.zero;
                ChartTopPanel = 0f;
                SeaActions.Visible = false;
                SeaActions.Rect = Rect.zero;
            }

            // --- taps -----------------------------------------------------------

            static void OpenShip()
            {
                if (SheetBits.Anchor != null) Sheets.Open(new ShipSheet());
            }

            /// The Backpack's ship page at a camp; its ship-only page (no
            /// camp) at sea or off a fresh island.
            static void OpenHold()
            {
                var a = SheetBits.Anchor;
                var camp = a != null && a.CurrentIsland != null ? Outpost.Of(a.CurrentIsland) : null;
                Sheets.Open(new BackpackSheet(camp, true));
            }

            void TapAlert()
            {
                switch (shownKind)
                {
                    case AlertKind.Overboard:
                        if (overboard != null && !overboard.Resolved && Helm() != null)
                            helm.SteerToward(overboard.transform, overboard.CrewName);
                        break;
                    case AlertKind.Overloaded:
                        var v = SheetBits.Voyage;
                        if (v != null) v.Jettison(jettisonN);
                        nextRefresh = 0f;
                        break;
                    case AlertKind.Water:
                    case AlertKind.Hull:
                        OpenShip();
                        break;
                }
            }

            HelmInput Helm()
            {
                var m = SheetBits.Motor;
                if (m == null) { helm = null; helmMotor = null; return null; }
                if (helm == null || helmMotor != m) { helmMotor = m; helm = m.GetComponent<HelmInput>(); }
                return helm;
            }

            // --- the tick ---------------------------------------------------------

            /// Once a frame from `SheetHost.LateUpdate`, after the thumb bar,
            /// the Next card and the combat row (it stacks on their rects)
            /// and before the chart (which reads `ChartTopPanel`).
            public void Tick(VisualElement root)
            {
                tickFrame = Time.frameCount;
                var anchor = SheetBits.Anchor;
                var motor = SheetBits.Motor;
                bool sea = anchor != null && SeaSick.Save.GameBoot.Decided && !MidnightLandHud.Active
                           && !ThumbBar.PlacementActive && !SeaSick.UI.CampSiting.Placing
                           && !SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked && !SeaLedger.IsOpen;
                if (!sea) { Hide(); return; }

                bool lying = anchor.CurrentState == AnchorController.State.Anchored
                             || anchor.CurrentState == AnchorController.State.Ashore;
                bool sheet = Sheets.Current != null;
                bool helmOn = !lying && !sheet && motor != null && !SeaSick.CameraRig.IslandCam.Engaged;

                // --- geometry (design px -> screen px -> panel units)
                var safe = Screen.safeArea;
                if (safe.width < 1f || safe.height < 1f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
                float s = Mathf.Max(1e-4f, SheetHost.PanelScale);
                bool wide = HudLayout.Wide;
                float ppd = wide ? 1f / s : safe.width / DesignWidth;
                float k = ppd * s;
                float guiTop = Screen.height - safe.yMax;
                float laneW = LaneWidth * ppd;
                float laneX = wide ? safe.xMin + (safe.width - laneW) * .5f : safe.xMin + Side * ppd;

                // --- top bar
                float barW = (wide ? WideBarWidth : LaneWidth) * ppd;
                float barX = wide ? safe.xMin + (safe.width - barW) * .5f : laneX;
                var barRect = new Rect(barX, guiTop + BarTop * ppd, barW, BarHeight * ppd);
                bool topOn = !(SheetHost.FrameOpen && SheetHost.FrameRect.Overlaps(barRect));
                Show(top, ref topShown, topOn);
                if (topOn)
                {
                    top.style.left = barX * s;
                    top.style.top = barRect.y * s;
                    top.style.width = wide ? WideBarWidth : LaneWidth;
                    SetScale(top, ref kTop, k);
                    TopRect = barRect;
                }
                else TopRect = Rect.zero;
                TopBarShowing = topOn;
                ChartTopPanel = topOn && !wide ? (guiTop + (AlertTop + 2f) * ppd) * s : 0f;

                float now = Time.unscaledTime;
                bool refresh = now >= nextRefresh;
                if (refresh)
                {
                    nextRefresh = now + .25f;
                    RefreshTop(anchor, motor, lying);
                    RefreshAlerts(motor);
                }

                // --- alert chip: right of the chart upright, under the bar's
                // left edge on a desk.
                bool alertOn = topOn && shownKind != AlertKind.None;
                if (alertOn)
                {
                    var alertProbe = new Rect(barX, guiTop + AlertTop * ppd, barW, AlertHeight * ppd);
                    if (SheetHost.FrameOpen && SheetHost.FrameRect.Overlaps(alertProbe)) alertOn = false;
                }
                Show(alertRow, ref alertShown, alertOn);
                if (alertOn)
                {
                    alertRow.style.left = barX * s;
                    alertRow.style.top = (guiTop + AlertTop * ppd) * s;
                    alertRow.style.width = wide ? WideBarWidth : LaneWidth;
                    alertRow.style.justifyContent = wide ? Justify.FlexStart : Justify.FlexEnd;
                    SetScale(alertRow, ref kAlert, k);
                    AlertRect = Union(ToGui(chip.worldBound, s), moreKey > 0 ? ToGui(more.worldBound, s) : Rect.zero);
                }
                else AlertRect = Rect.zero;

                // --- the order strip
                // The bottom row is the strip (left) and the boost button
                // (right, level with it); the row is as tall as the button.
                float helmH = Mathf.Max(HelmHeight, BoostSize);
                Show(helmRow, ref helmShown, helmOn);
                Show(boostBtn, ref boostShown, helmOn);
                if (helmOn)
                {
                    float bottom = safe.yMin + HelmBottom * ppd;
                    float stripW = LaneWidth - BoostSize - BoostGap;            // design px
                    float stripBottom = bottom + (helmH - HelmHeight) * .5f * ppd;
                    helmRow.style.left = laneX * s;
                    helmRow.style.bottom = stripBottom * s;
                    helmRow.style.width = stripW;
                    helmRow.style.height = HelmHeight;
                    SetScale(helmRow, ref kHelm, k);
                    float boostX = laneX + (LaneWidth - BoostSize) * ppd;
                    boostBtn.style.left = boostX * s;
                    boostBtn.style.bottom = bottom * s;
                    SetScale(boostBtn, ref kBoost, k);
                    BoostRect = new Rect(boostX, Screen.height - bottom - BoostSize * ppd, BoostSize * ppd, BoostSize * ppd);
                    var stripRect = new Rect(laneX, Screen.height - stripBottom - HelmHeight * ppd, stripW * ppd, HelmHeight * ppd);
                    HelmRect = Union(stripRect, BoostRect);
                    TickHelm(motor);
                }
                else HelmRect = BoostRect = Rect.zero;
                HelmShowing = helmOn;
                // The combat row sits just above the strip (mockup 8b).
                CombatHud.BottomPx = HelmBottom + helmH + CardGap;

                // --- the action card
                bool cardOn = !sheet && !CombatHud.Visible && SeaActions.HasOffer;
                Show(card, ref cardShown, cardOn);
                float cardTop = 0f;   // screen px from the bottom, for the IMGUI reserve
                if (cardOn)
                {
                    float fromBottom = helmOn
                        ? safe.yMin + (HelmBottom + helmH + CardGap) * ppd
                        : safe.yMin + HelmBottom * ppd;
                    if (ThumbBar.ReservePanel > 0f) fromBottom = Mathf.Max(fromBottom, ThumbBar.ReservePanel / s);
                    card.style.left = laneX * s;
                    card.style.bottom = fromBottom * s;
                    SetScale(card, ref kCard, k);
                    TickCard();
                    var wb = card.worldBound;
                    float h = wb.height > 1f ? wb.height / s : 80f * ppd;
                    SeaActions.Rect = new Rect(laneX, Screen.height - fromBottom - h, laneW, h);
                    SeaActions.Visible = true;
                    cardTaps = cEnabled;
                    cardTop = fromBottom + h;
                }
                else
                {
                    SeaActions.Visible = false;
                    SeaActions.Rect = Rect.zero;
                    cardTaps = false;
                }

                // --- the combat target chip sits under the chart upright
                // (the chart is where the mockup's chip would be).
                float chipTop = AlertTop;
                var chart = ChartInstrument.ScreenRect;
                // On a desk too (2026-09-30): there the chip hugs the safe
                // left edge (`CombatHud.Place`), right on the chart.
                float chipX = wide ? safe.xMin + CombatHud.Side * ppd : laneX;
                if (chart.height > 0f && chart.x < chipX + 120f * ppd)
                    chipTop = Mathf.Max(chipTop, (chart.yMax - guiTop) / ppd + 30f);
                CombatHud.TopPx = chipTop;

                // --- first-use hint in the free stick zone
                var h0 = Helm();
                bool hintOn = helmOn && h0 != null && !CombatHud.Visible
                              && GestureHints.ShowOnce(GestureHints.Stick, h0.StickInUse);
                // Retires after `GestureHints.ShowSeconds` on screen even if
                // the stick is never used (Kevin, 2026-09-30: every hint
                // "auto-hides after the gesture is done once, or after ~6 s").
                if (hintOn && GestureHints.Shown(GestureHints.Stick, Time.unscaledDeltaTime)) hintOn = false;
                Show(hint, ref hintShown, hintOn);
                if (hintOn)
                {
                    float upper = Screen.height * .5f;                         // the stick zone's top (GUI)
                    float lower = Screen.height - Mathf.Max(cardTop, safe.yMin + (HelmBottom + helmH) * ppd);
                    float cy = (upper + lower) * .5f;
                    float cx = safe.xMin + safe.width * .5f;
                    hint.style.left = (cx - 100f * ppd) * s;
                    hint.style.top = (cy - 60f * ppd) * s;
                    SetScale(hint, ref kHint, k);
                }

                // --- look hint, upper camera zone: only once the stick hint is
                // done (the two never stand together), gone for good once the
                // camera has been dragged (`SeaCameraInput.EverUsed`) or after
                // `ShowSeconds` on screen like every other hint.
                bool lookOn = helmOn && !hintOn && !CombatHud.Visible
                              && GestureHints.IsDone(GestureHints.Stick)
                              && GestureHints.ShowOnce(GestureHints.SeaLook, SeaSick.CameraRig.SeaCameraInput.EverUsed);
                if (lookOn && GestureHints.Shown(GestureHints.SeaLook, Time.unscaledDeltaTime)) lookOn = false;
                Show(lookHint, ref lookHintShown, lookOn);
                if (lookOn)
                {
                    // ~30% down the screen, but never up under the top bar.
                    float topY = Screen.height * .30f;
                    if (TopRect.height > 0f) topY = Mathf.Max(topY, TopRect.yMax + 12f * ppd);
                    lookHint.style.left = (safe.xMin + safe.width * .5f - 110f * ppd) * s;
                    lookHint.style.top = topY * s;
                    SetScale(lookHint, ref kLookHint, k);
                }

                // --- keep what is left of the IMGUI HUD above all of this
                float reserve = Mathf.Max(cardTop, helmOn ? safe.yMin + (HelmBottom + helmH) * ppd : 0f) - safe.yMin;
                // The combat row sits above the order strip: the reserve covers it
                // too (2026-09-30), and it is the one rect `HudOverlapProbe`
                // sees for both -- the row used to declare its own rect inside
                // this one, which the probe (rightly) reads as two placed
                // panels on top of each other.
                if (CombatHud.Visible && CombatHud.Rect.height > 0f)
                    reserve = Mathf.Max(reserve, Screen.height - CombatHud.Rect.yMin - safe.yMin);
                if (reserve > 1f)
                {
                    HudLayout.Place(HudLayout.Slot.Wheel, laneW, Mathf.Max(1f, reserve - HudLayout.Pad));
                    if (!SheetHost.FrameOpen && !NextCard.NoCampShowing)
                        HudLayout.ClaimSheet(Union(HelmRect, SeaActions.Visible ? SeaActions.Rect : Rect.zero));
                }
            }

            static Rect ToGui(Rect panelRect, float s) =>
                panelRect.width > 0f && panelRect.height > 0f
                    ? new Rect(panelRect.x / s, panelRect.y / s, panelRect.width / s, panelRect.height / s)
                    : Rect.zero;

            static Rect Union(Rect a, Rect b)
            {
                if (b.width <= 0f || b.height <= 0f) return a;
                if (a.width <= 0f || a.height <= 0f) return b;
                return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                    Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
            }

            // --- top bar content (0.25 s) -------------------------------------------

            void RefreshTop(AnchorController anchor, ShipMotor motor, bool lying)
            {
                var v = SheetBits.Voyage;
                if (v != null)
                {
                    int held = v.TotalHeld, cap = v.HoldCapacity;
                    int key = held * 4099 + cap;
                    if (key != holdKey) { holdKey = key; holdText.text = held + "/" + cap; }
                    int tone = v.Overloaded ? 2 : cap > 0 && held >= cap * .8f ? 1 : 0;
                    if (tone != holdTone)
                    {
                        holdTone = tone;
                        holdText.style.color = tone == 2 ? Ember : tone == 1 ? Amber : Pearl;
                    }
                }
                else if (holdKey != -1) { holdKey = -1; holdText.text = "--"; }

                var roster = SheetBits.Roster;
                int aboard = 0;
                if (roster != null)
                {
                    var all = roster.All;
                    for (int i = 0; i < all.Length; i++)
                        if (all[i] != null && all[i].IsAboard) aboard++;
                }
                if (aboard != crewKey) { crewKey = aboard; crewText.text = aboard.ToString(); }

                // Off an island with no camp: where she is, in place of the
                // sea and the day (mockup 8c). (Until 2026-10-03 also how much
                // of it the party had opened: the fog of war is gone.)
                var isle = lying ? anchor.CurrentIsland : null;
                bool place = isle != null && !Outpost.IsSettled(isle);
                if (place != placeMode)
                {
                    placeMode = place;
                    seaGlyph.style.display = place ? DisplayStyle.None : DisplayStyle.Flex;
                    seaText.style.display = place ? DisplayStyle.None : DisplayStyle.Flex;
                    placeText.style.display = place ? DisplayStyle.Flex : DisplayStyle.None;
                    dayItem.style.display = place ? DisplayStyle.None : DisplayStyle.Flex;
                    placeIsland = null;
                }
                if (place)
                {
                    if (isle != placeIsland)
                    {
                        placeIsland = isle;
                        placeText.text = isle.name;
                    }
                    return;
                }

                // The sea's own word, as the helm panel said it: breakers
                // outrank a broach, both in ember; a sea past lively, or one
                // she is driving hard into, in amber.
                string word = "calm";
                Color col = Pearl;
                if (motor != null)
                {
                    var br = Helm() != null ? helm.Breakers : null;
                    if (br != null && br.Breaking01 > 0.3f) { word = "BREAKERS"; col = Ember; }
                    else if (motor.Broach01 > 0.35f) { word = "broaching"; col = Ember; }
                    else
                    {
                        word = motor.SeaStateName;
                        bool big = word != "calm" && word != "lively";
                        col = big || motor.HeadSea01 * motor.SeaSeverity01 > 0.4f ? Amber : Pearl;
                    }
                }
                if (!ReferenceEquals(word, seaWord)) { seaWord = word; seaText.text = word; }
                if (col != seaColour) { seaColour = col; seaText.style.color = col; seaGlyph.Tint = col; }

                int day = SeaSick.World.TimeOfDay.Day;
                if (day != dayKey) { dayKey = day; dayText.text = "Day " + day; }
            }

            // --- alerts (0.25 s) ----------------------------------------------------

            void RefreshAlerts(ShipMotor motor)
            {
                AlertKind first = AlertKind.None;
                int count = 0;
                Swimmer mob = null;

                var swimmers = Swimmer.All;
                for (int i = 0; i < swimmers.Count; i++)
                    if (swimmers[i] != null && !swimmers[i].Resolved) { mob = swimmers[i]; break; }
                if (mob != null) { count++; first = AlertKind.Overboard; }

                var bilge = motor != null ? motor.GetComponent<Bilge>() : null;
                bool wet = bilge != null && bilge.Flooding;
                if (wet) { count++; if (first == AlertKind.None) first = AlertKind.Water; }

                var hull = motor != null ? motor.GetComponent<HullIntegrity>() : null;
                int pct = hull != null ? Mathf.RoundToInt(hull.Integrity01 * 100f) : 100;
                // Below 95 % only (2026-09-30): one scrape put an ember "Hull
                // 99% · repair" up for the rest of the voyage. The Ship sheet
                // still shows every point.
                if (pct < 95) { count++; if (first == AlertKind.None) first = AlertKind.Hull; }

                var v = SheetBits.Voyage;
                bool over = v != null && v.Overloaded && v.TotalHeld > 0;
                if (bilge != null) jettisonN = Mathf.Max(1, bilge.JettisonPerTap);
                if (over) { count++; if (first == AlertKind.None) first = AlertKind.Overloaded; }

                overboard = mob;
                int key;
                switch (first)
                {
                    case AlertKind.Overboard: key = mob.GetInstanceID(); break;
                    case AlertKind.Water: key = Mathf.RoundToInt(bilge.Bilge01 * 100f) * 64 + bilge.Bailers; break;
                    case AlertKind.Hull: key = pct; break;
                    case AlertKind.Overloaded: key = jettisonN; break;
                    default: key = 0; break;
                }
                if (first != shownKind || key != alertKey)
                {
                    if (first != shownKind)
                    {
                        chip.EnableInClassList("sea-chip--mob", first == AlertKind.Overboard);
                        chip.EnableInClassList("sea-chip--bad", first == AlertKind.Water || first == AlertKind.Hull);
                        chip.EnableInClassList("sea-chip--warn", first == AlertKind.Overloaded);
                    }
                    shownKind = first;
                    alertKey = key;
                    switch (first)
                    {
                        case AlertKind.Overboard:
                            chip.text = "MAN OVERBOARD · " + mob.CrewName;
                            chip.tooltip = "Steer toward them";
                            break;
                        case AlertKind.Water:
                            int w = Mathf.RoundToInt(bilge.Bilge01 * 100f);
                            chip.text = bilge.Bailers > 0
                                ? "Taking water " + w + "% · " + bilge.Bailers + " bailing"
                                : "Taking water " + w + "%";
                            chip.tooltip = "The Ship sheet";
                            break;
                        case AlertKind.Hull:
                            chip.text = "Hull " + pct + "% · repair";
                            chip.tooltip = "The Ship sheet: mend her at anchor";
                            break;
                        case AlertKind.Overloaded:
                            chip.text = "Overloaded · throw " + jettisonN + " over";
                            chip.tooltip = "Throw cargo over the side";
                            break;
                    }
                }
                int rest = first == AlertKind.None ? 0 : count - 1;
                if (rest != moreKey)
                {
                    moreKey = rest;
                    if (rest > 0) more.text = "+" + rest;
                    more.style.display = rest > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            // --- the card (each frame, re-texted only on change) ---------------------

            void TickCard()
            {
                var o = SeaActions.Current;
                if (o.eyebrow != cEyebrow) { cEyebrow = o.eyebrow; eyebrow.text = o.eyebrow ?? ""; }
                if (o.title != cTitle) { cTitle = o.title; title.text = o.title ?? ""; }
                if (o.detail != cDetail)
                {
                    cDetail = o.detail;
                    detail.text = o.detail ?? "";
                    detail.style.display = string.IsNullOrEmpty(o.detail) ? DisplayStyle.None : DisplayStyle.Flex;
                }
                if (o.enabled != cEnabled)
                {
                    cEnabled = o.enabled;
                    card.EnableInClassList("sea-card--info", !o.enabled);
                    go.style.display = o.enabled ? DisplayStyle.Flex : DisplayStyle.None;
                    // Information takes no tap: a drag there is the stick's.
                    card.pickingMode = o.enabled ? PickingMode.Position : PickingMode.Ignore;
                }
                bool bar = o.progress01 >= 0f;
                if (bar != cBar)
                {
                    cBar = bar;
                    track.style.display = bar ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (bar)
                {
                    float p = Mathf.Round(Mathf.Clamp01(o.progress01) * 200f) / 200f;
                    if (!Mathf.Approximately(p, cProgress))
                    {
                        cProgress = p;
                        fill.style.width = Length.Percent(p * 100f);
                    }
                }
            }

            // --- the order strip (each frame, re-texted only on change) -------------

            void TickHelm(ShipMotor motor)
            {
                var h = Helm();
                // The LIVE drive (no latched order any more): the word comes
                // from the helm, the bar is the achieved throttle.
                string word = h != null ? h.OrderWord : "Stop";
                bool boosting = h != null && h.Boosting;
                bool armed = h != null && h.BoostArmed;
                bool astern = motor.Throttle < -0.05f;
                if (!string.Equals(word, shownOrder) || boosting != shownBurn || astern != shownAstern)
                {
                    shownOrder = word;
                    shownBurn = boosting;
                    shownAstern = astern;
                    orderText.text = word;
                    orderText.EnableInClassList("sea-order--burn", boosting);
                    orderText.EnableInClassList("sea-order--astern", astern);
                    barFill.EnableInClassList("sea-bar-fill--burn", boosting);
                    barFill.EnableInClassList("sea-bar-fill--astern", astern);
                    barBurn.EnableInClassList("sea-bar-burn--lit", boosting);
                }
                if (armed != shownArmed || boosting != shownBoosting)
                {
                    shownArmed = armed;
                    shownBoosting = boosting;
                    boostBtn.EnableInClassList("sea-boost--armed", armed && !boosting);
                    boostBtn.EnableInClassList("sea-boost--on", boosting);
                    boostGlyph.Tint = boosting ? Dark : armed ? Amber : Pearl;
                    boostGlyph.Filled = boosting;
                }

                int sk = Mathf.Clamp(Mathf.RoundToInt(motor.CurrentSpeed * 10f), 0, speedTexts.Length - 1);
                if (sk != speedKey)
                {
                    speedKey = sk;
                    speedTexts[sk] ??= "· " + (sk / 10f).ToString("0.0") + " m/s";
                    speedText.text = speedTexts[sk];
                }

                // Zero at the left, full at the tick, burn to the end: the
                // ACHIEVED throttle (astern fills the same way, in amber).
                float ceiling = Mathf.Max(1.05f, motor.Overdrive);
                float f = Mathf.Clamp01(Mathf.Abs(motor.Throttle) / ceiling);
                f = Mathf.Round(f * 250f) / 250f;
                if (!Mathf.Approximately(f, fillShown)) { fillShown = f; barFill.style.width = Length.Percent(f * 100f); }
                float t = 1f / ceiling;
                if (!Mathf.Approximately(t, tickShown))
                {
                    tickShown = t;
                    barTick.style.left = Length.Percent(t * 100f);
                    barBurn.style.left = Length.Percent(t * 100f);   // the burn stretch, tick to end
                }
            }
        }
    }

    /// **The sea HUD's pictograms (2026-09-30)**: the sea-state wave, the ☰
    /// menu lines and the dashed first-use ring (the oars and the ease
    /// arrow went with their toggles, 2026-10-02).
    /// Painted like `HudGlyph` / `LandIcon` (no font glyphs: "❚❚" and "⌂"
    /// drew as blank boxes on the phone's font).
    public sealed class SeaGlyph : VisualElement
    {
        public enum Kind { Wave, Menu, DashedRing, Bolt }

        readonly Kind kind;
        Color tint;

        public SeaGlyph(Kind kind)
        {
            this.kind = kind;
            tint = kind == Kind.DashedRing ? new Color(232f / 255f, 242f / 255f, 246f / 255f, 0.35f)
                : (Color)new Color32(232, 242, 246, 255);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        bool filled;
        /// The bolt only: solid (boosting) rather than an outline.
        public bool Filled
        {
            get => filled;
            set { if (value == filled) return; filled = value; MarkDirtyRepaint(); }
        }

        public Color Tint
        {
            get => tint;
            set { if (value == tint) return; tint = value; MarkDirtyRepaint(); }
        }

        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f) return;
            p.strokeColor = tint;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            if (kind == Kind.DashedRing)
            {
                float r = Mathf.Min(w, h) * .5f - 1.5f;
                var c = new Vector2(w * .5f, h * .5f);
                p.lineWidth = 2f;
                p.lineCap = LineCap.Butt;
                const int Dashes = 28;
                float step = 360f / Dashes;
                for (int i = 0; i < Dashes; i++)
                {
                    p.BeginPath();
                    p.Arc(c, r, i * step, i * step + step * .55f);
                    p.Stroke();
                }
                return;
            }
            // Everything else is drawn on the mockups' 24-unit grid.
            float s = Mathf.Min(w, h) / 24f;
            Vector2 V(float x, float y) => new Vector2(x * s, y * s);
            p.lineWidth = 2.2f * s;
            switch (kind)
            {
                case Kind.Wave:
                    p.BeginPath(); p.MoveTo(V(2, 14));
                    p.BezierCurveTo(V(5, 11), V(7, 11), V(10, 14));
                    p.BezierCurveTo(V(13, 17), V(15, 17), V(18, 14));
                    p.BezierCurveTo(V(20, 12), V(21, 12), V(22, 12));
                    p.Stroke();
                    p.BeginPath(); p.MoveTo(V(2, 19));
                    p.BezierCurveTo(V(5, 16), V(7, 16), V(10, 19));
                    p.BezierCurveTo(V(13, 22), V(15, 22), V(18, 19));
                    p.Stroke();
                    break;
                case Kind.Bolt:
                    p.lineWidth = 2.4f * s;
                    p.fillColor = tint;
                    p.BeginPath();
                    p.MoveTo(V(13.5f, 2)); p.LineTo(V(5, 13.5f)); p.LineTo(V(11, 13.5f));
                    p.LineTo(V(9.5f, 22)); p.LineTo(V(19, 10)); p.LineTo(V(13, 10));
                    p.ClosePath();
                    if (filled) p.Fill();
                    p.Stroke();
                    break;
                case Kind.Menu:
                    p.lineWidth = 2.4f * s;
                    for (int i = 0; i < 3; i++)
                    {
                        p.BeginPath(); p.MoveTo(V(4, 7 + i * 5)); p.LineTo(V(20, 7 + i * 5)); p.Stroke();
                    }
                    break;
            }
        }
    }
}
