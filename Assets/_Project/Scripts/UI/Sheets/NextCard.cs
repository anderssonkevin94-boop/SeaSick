using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Next card, 2026-09-30 (island UI restructure, phase 2).**
    ///
    /// One card directly above the thumb bar, in the bar's lane: what to do
    /// next, why, and what it costs -- and the WHOLE card is the button that
    /// starts it. What it says, in order:
    /// <list type="number">
    /// <item>**YOUR GOAL** -- a pinned goal (`GoalPin`, "Set as goal")
    ///   while it is incomplete. Replaces the old slim GoalBar.</item>
    /// <item>**NEXT · STEP n OF m** -- the first open step of the goal
    ///   chain (`GoalChainSteps`), derived from the save every refresh.</item>
    /// <item>**NEEDS YOU** -- once the chain is done, the most urgent
    ///   `CampAlerts` alert (the alert strip's own order), its fix the tap.</item>
    /// <item>Nothing -- the card hides. No filler.</item>
    /// </list>
    ///
    /// Shown with the bar's own visibility (an island camp, no sheet open,
    /// nothing being placed). Before a
    /// camp exists there is no bar and no land HUD: at an anchored island
    /// with a surveyed site and no fire the card stands alone in the bar's
    /// slot as step 1, "Make camp", replacing the anchor prompt's old IMGUI
    /// row (`AnchorController.DrawMakeCamp` stands down while `Visible`).
    ///
    /// Content is evaluated at the land HUD's 0.25 s cadence (and at once
    /// when the camp or the pin changes); the tree is built once and
    /// re-texted only when `NextModel.Key` moves.
    public static class NextCard
    {
        /// Gap between the card and the bar under it, panel units.
        public const float Gap = 12f;
        /// The ice square with the chevron.
        public const float Square = 52f;

        /// The card's rect in screen pixels, GUI space (origin top-left) --
        /// the same space as `ThumbBar.Rect`. Zero while hidden.
        public static Rect Rect { get; private set; }
        public static bool Visible { get; private set; }
        /// True while the card is up at an island with no camp (step 1 in
        /// the bar's slot). `SheetHost` uses the land panel scale then.
        public static bool NoCampShowing { get; private set; }
        /// The `CampAlerts` alert text the card is showing, or null -- the
        /// alert strip leaves that one out.
        public static string AlertText { get; private set; }
        /// True while the card is showing a raid alert (`RaidBanner` stays
        /// quiet on land when the strip OR this card says it).
        public static bool ShowsRaid { get; private set; }

        public static bool Blocks(Vector2 guiPoint) => Rect.Contains(guiPoint);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Rect = Rect.zero;
            Visible = NoCampShowing = ShowsRaid = false;
            AlertText = null;
        }

        /// At an island that has a surveyed site but no fire, stopped
        /// (anchored or ashore) -- where the anchor prompt used to offer
        /// "Make camp".
        static bool NoCampHere(Outpost camp)
        {
            if (camp == null || camp.HasCamp) return false;
            var a = Sheets.Anchor;
            if (a == null) return false;
            return a.CurrentState == SeaSick.Ship.AnchorController.State.Anchored
                || a.CurrentState == SeaSick.Ship.AnchorController.State.Ashore;
        }

        internal sealed class View
        {
            readonly Button card;
            readonly Label label, title, reason;
            readonly VisualElement partsRow;
            readonly VisualElement[] partBox = new VisualElement[NextModel.MaxParts];
            readonly Label[] partText = new Label[NextModel.MaxParts];
            readonly ThumbBar.Glyph[] partCheck = new ThumbBar.Glyph[NextModel.MaxParts];
            readonly NextModel model = new NextModel();
            readonly List<CampAlerts.Alert> alerts = new List<CampAlerts.Alert>(8);

            bool shown = true, hasContent;
            long shownKey = long.MinValue;
            float nextRefresh;
            int pinVersion = -1;
            Outpost lastCamp;
            bool lastNoCamp;

            static readonly Color Amber = new Color32(242, 196, 109, 255);
            static readonly Color Moss = new Color32(156, 210, 122, 255);

            public View(VisualElement root)
            {
                card = new Button(Tap);
                card.AddToClassList("next-card");
                card.tooltip = "What to do next: tap to start it";
                card.pickingMode = PickingMode.Position;

                var body = new VisualElement { pickingMode = PickingMode.Ignore };
                body.AddToClassList("next-body");
                label = Text(body, "next-label");
                title = Text(body, "next-title");
                reason = Text(body, "next-reason");
                partsRow = new VisualElement { pickingMode = PickingMode.Ignore };
                partsRow.AddToClassList("next-parts");
                for (int i = 0; i < NextModel.MaxParts; i++)
                {
                    var box = new VisualElement { pickingMode = PickingMode.Ignore };
                    box.AddToClassList("next-part");
                    var t = new Label { pickingMode = PickingMode.Ignore };
                    t.AddToClassList("next-part-text");
                    var check = new ThumbBar.Glyph(ThumbBar.Glyph.Kind.Confirm) { Tint = Moss };
                    check.AddToClassList("next-check");
                    box.Add(t); box.Add(check);
                    partsRow.Add(box);
                    partBox[i] = box; partText[i] = t; partCheck[i] = check;
                }
                body.Add(partsRow);
                card.Add(body);

                var go = new VisualElement { pickingMode = PickingMode.Ignore };
                go.AddToClassList("next-go");
                var chevron = new ThumbBar.Glyph(ThumbBar.Glyph.Kind.Chevron) { Tint = new Color32(11, 23, 32, 255) };
                chevron.AddToClassList("next-chevron");
                go.Add(chevron);
                card.Add(go);

                root.Add(card);
                Hide();
            }

            static Label Text(VisualElement into, string cls)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList(cls);
                into.Add(l);
                return l;
            }

            void Tap() => model.tap?.Invoke();

            void Hide()
            {
                if (shown)
                {
                    shown = false;
                    card.style.display = DisplayStyle.None;
                }
                Visible = NoCampShowing = ShowsRaid = false;
                AlertText = null;
                Rect = Rect.zero;
            }

            /// Once a frame from `SheetHost.LateUpdate`, after the thumb bar
            /// (its lane and reserve are this frame's) and before the land
            /// HUD (whose IMGUI claim and food notice read this card's).
            public void Tick(VisualElement root)
            {
                var camp = MidnightLandHud.Camp;
                bool blocked = ThumbBar.PlacementActive || Sheets.Current != null || CampSiting.Placing
                               || GatherPartySheet.IsOpen || SeaLedger.IsOpen;
                bool noCamp = false, want;
                if (blocked) want = false;
                else if (MidnightLandHud.Active)
                    want = ThumbBar.Visible && camp != null;
                else
                    // No camp yet: the land HUD is off because there is no
                    // fire or blueprint (`SuppressLegacy`), not because the
                    // classic UI was chosen.
                    want = noCamp = MidnightLandHud.Enabled && !Sheets.SuppressLegacy && NoCampHere(camp);
                if (!want) { Hide(); lastCamp = null; return; }

                float now = Time.unscaledTime;
                if (camp != lastCamp || noCamp != lastNoCamp || pinVersion != GoalPin.Version || now >= nextRefresh)
                {
                    lastCamp = camp;
                    lastNoCamp = noCamp;
                    pinVersion = GoalPin.Version;
                    nextRefresh = now + .25f;
                    hasContent = Compute(camp, noCamp);
                    if (hasContent && model.Key != shownKey) Apply();
                }
                if (!hasContent) { Hide(); return; }

                if (!shown)
                {
                    shown = true;
                    card.style.display = DisplayStyle.Flex;
                }
                Visible = true;
                NoCampShowing = noCamp;
                AlertText = model.alertText;
                ShowsRaid = model.raid;

                ThumbBar.Lane(root, out float left, out float width, out float bottom);
                if (ThumbBar.Visible) bottom += ThumbBar.Height + Gap;
                card.style.left = left;
                card.style.width = width;
                card.style.bottom = bottom;

                float scale = SheetHost.PanelScale;
                float inv = 1f / Mathf.Max(1e-4f, scale);
                float h = card.resolvedStyle.height;
                if (float.IsNaN(h) || h < 1f) h = 120f;
                Rect = new Rect(left * inv, Screen.height - (bottom + h) * inv, width * inv, h * inv);
                ThumbBar.RaiseReserve(bottom + h + ThumbBar.Gap);

                // With no camp the land HUD is off and claims nothing: the
                // card keeps the IMGUI anchor prompt (Cast off, gather
                // party) off itself the way the land HUD does for the bar.
                if (noCamp && !SheetHost.FrameOpen) HudLayout.ClaimSheet(Rect);
            }

            /// Fill `model`. False = nothing to say; the card hides.
            bool Compute(Outpost camp, bool noCamp)
            {
                model.Clear();
                var l = camp != null ? camp.Ledger : null;
                if (!noCamp && l != null && l.ActiveGoal != null)
                {
                    var c = GoalChain.ForCamp(l);
                    if (c.HasGoal)
                    {
                        model.label = "YOUR GOAL";
                        model.title = c.title ?? "";
                        model.reason = c.firstStep != null && !string.IsNullOrEmpty(c.firstStep.title)
                            ? CampReadouts.Cap(c.firstStep.title)
                              + (string.IsNullOrEmpty(c.firstStep.detail) ? "" : " · " + c.firstStep.detail)
                            : c.why ?? "";
                        if (c.CanComplete) model.Part("ready", NextTone.Met);
                        else if (c.total > 0) model.Part($"{c.ready}/{c.total} ready", NextTone.Short);
                        var at = camp;
                        model.tap = () => Sheets.Open(new CampSheet(at));   // its YOUR GOAL section
                        return true;
                    }
                }
                if (GoalChainSteps.Current(camp, model)) return true;
                if (noCamp || camp == null) return false;

                CampAlerts.Collect(camp, alerts);
                if (alerts.Count == 0) return false;
                var a = alerts[0];
                model.label = "NEEDS YOU";
                model.title = a.text ?? "";
                model.alertText = a.text;
                model.raid = a.tone == CampAlerts.Tone.Raid;
                var open = a.open;
                model.tap = () =>
                {
                    var sheet = open != null ? open() : null;
                    if (sheet != null) Sheets.Open(sheet);
                };
                return true;
            }

            void Apply()
            {
                shownKey = model.Key;
                label.text = model.label;
                title.text = model.title;
                reason.text = model.reason;
                reason.style.display = string.IsNullOrEmpty(model.reason) ? DisplayStyle.None : DisplayStyle.Flex;
                card.EnableInClassList("next-card--alert", model.alertText != null);
                card.EnableInClassList("next-card--raid", model.raid);
                partsRow.style.display = model.parts > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                for (int i = 0; i < NextModel.MaxParts; i++)
                {
                    bool on = i < model.parts;
                    partBox[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                    if (!on) continue;
                    var tone = model.partTone[i];
                    // A middle dot between the pieces, carried by the next one.
                    partText[i].text = (i > 0 ? "· " : "") + model.partText[i];
                    partText[i].style.color = tone == NextTone.Short ? Amber
                        : tone == NextTone.Met ? Moss : MidnightLandHud.Ice;
                    partCheck[i].style.display = tone == NextTone.Met ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }
    }
}
