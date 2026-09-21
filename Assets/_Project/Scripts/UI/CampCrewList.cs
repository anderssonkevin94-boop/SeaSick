using System.Collections.Generic;
using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// **The island's own crew, down the right-hand side, and what you can
    /// tell them to do.**
    ///
    /// Kevin, 2026-09-19: *"when they landed at the island the crew currently
    /// assigned to the island's name pop up on a list to the right. when
    /// pressing their name you get options of what you can have them do."*
    /// Three verbs, each opening a list: **assign** to a position, **gather** a
    /// resource, **build** something.
    ///
    /// It is the counterpart to `CampSheet`, not a replacement: the sheet
    /// along the bottom is about MOVEMENT — who is aboard, who is ashore, who
    /// comes back — and this is about WORK. Keeping them apart is what stops
    /// either one becoming a menu of everything.
    ///
    /// Only the hands who LIVE here are listed. A visiting shore party is on
    /// an errand off the ship and takes its orders from the plank, which is
    /// the same distinction the ledger makes: a stationed hand is a row that
    /// produces arithmetically, a visiting hand is a body that delivers.
    public class CampCrewList : MonoBehaviour
    {
        AnchorController anchor;

        /// Who is open, by name, or null. Names rather than a reference
        /// because the rows are rebuilt from the ledger every frame and a
        /// cached row is a row that outlives the hand.
        string open;
        /// Which verb is open under them.
        Verb verb;

        enum Verb { None, Assign, Gather, Build }

        GUIStyle row, sub, note;

        /// Set while the list is on screen, so anything else drawing on the
        /// right can stand aside.
        public static bool Showing { get; private set; }

        void Awake()
        {
            anchor = GetComponentInParent<AnchorController>();
            if (anchor == null) anchor = Object.FindFirstObjectByType<AnchorController>();
        }

        /// The camp this list is about, or null if there is nothing to show.
        Outpost Subject()
        {
            if (anchor == null) return null;
            if (anchor.CurrentState != AnchorController.State.Anchored
                && anchor.CurrentState != AnchorController.State.Ashore) return null;
            var isle = anchor.CurrentIsland;
            if (isle == null || isle.IsHome) return null;
            var camp = Outpost.Of(isle);
            if (camp == null || camp.Ledger == null) return null;
            // A blueprint counts: hands left at one are exactly the people you
            // want to be able to re-order.
            if (!camp.HasCamp && !camp.Building) return null;
            return camp;
        }

        void Update() { Showing = Subject() != null; }

        void OnGUI()
        {
            var camp = Subject();
            Showing = camp != null;
            if (!Showing) return;
            if (CampSiting.Placing) return;      // the thumb is busy putting a building down

            EnsureStyles();
            camp.CatchUp();
            var hands = camp.Ledger.hands;

            var safe = HudLayout.Safe;
            float w = Mathf.Min(safe.width * 0.34f, HudLayout.Unit * 16f);
            float rowH = HudLayout.Unit * 2.2f;
            float pad = HudLayout.Pad;

            // How tall: the names, plus whatever menu is open under the one
            // that is open. Measured before anything is drawn so the panel is
            // never the wrong size for its contents.
            int extras = 0;
            if (open != null)
            {
                extras += 3;                                   // the three verbs
                if (verb != Verb.None) extras += OptionCount(camp, verb);
            }
            // **The fire's own build list is rows too.** Until 2026-09-21
            // this counted only the rows under an OPEN hand, so a build list
            // opened by tapping the campfire (`open == null`) was drawn
            // BELOW the panel it was measured for -- and a panel that does
            // not cover its own buttons is a panel that does not BLOCK them:
            // `UIBlocker` never claimed that strip, `IslandInput.BeginPress`
            // did not latch the press as UI, and the same press/release that
            // clicked "storage -- 5 timber 3 stone" was also a tap on the
            // ground under it. That is half of the blueprint siting itself
            // (the other half is the frame guard in `CampSiting`).
            else if (showBuild) extras += 1 + OptionCount(camp, Verb.Build);
            float panelH = pad * 2f + HudLayout.Unit * 1.6f
                    + hands.Count * (rowH + 2f) + extras * (rowH * 0.86f + 2f);
            panelH = Mathf.Min(panelH, safe.height * 0.72f);

            // **Ask the layout for a place; never pick one.** The first
            // version put itself at a hand-chosen rect under the top-right
            // corner and landed on 240x60 px of the minimap --
            // `HudOverlapProbe` reported it for thirty frames straight, which
            // is exactly what that probe is for. `Place` stacks it under
            // whatever else is live in that column instead.
            var panel = HudLayout.Place(HudLayout.Slot.CampCrew, w, panelH);
            UIBlocker.Block(panel);
            UITheme.Rect(panel, UITheme.PanelSolid);

            var inner = new Rect(panel.x + pad, panel.y + pad,
                                 panel.width - pad * 2f, panel.height - pad * 2f);

            GUI.Label(new Rect(inner.x, inner.y, inner.width, HudLayout.Unit * 1.6f),
                hands.Count == 1 ? "1 HAND ASHORE" : $"{hands.Count} HANDS ASHORE", note);
            float y = inner.y + HudLayout.Unit * 1.6f;

            if (hands.Count == 0)
            {
                GUI.Label(new Rect(inner.x, y, inner.width, rowH),
                    "nobody lives here yet", note);
                return;
            }

            // A copy, because a button pressed in this loop can change an
            // order, and an order change re-arranges the camp.
            var listed = new List<OutpostHand>(hands);
            foreach (var h in listed)
            {
                if (h == null) continue;
                bool isOpen = open == h.name;
                var r = new Rect(inner.x, y, inner.width, rowH);
                if (GUI.Button(r, $"{h.name}   ·   {h.Doing}",
                        isOpen ? UITheme.ButtonPressed : row))
                {
                    open = isOpen ? null : h.name;
                    verb = Verb.None;
                }
                y = r.yMax + 2f;
                if (!isOpen) continue;

                y = Verbs(camp, h, inner.x, y, inner.width, rowH);
            }
            if (showBuild && open == null)
                y = BuildBlock(camp, inner.x, y, inner.width, rowH * 0.86f, HudLayout.Unit);
        }

        /// The three verbs, and whichever list is open under them.
        float Verbs(Outpost camp, OutpostHand h, float x, float y, float w, float rowH)
        {
            float subH = rowH * 0.86f;
            float indent = HudLayout.Unit;

            y = Verb1(ref verb, Verb.Assign, "assign  ▸", x + indent, y, w - indent, subH);
            if (verb == Verb.Assign)
            {
                var posts = camp.Positions();
                if (posts.Count == 0)
                    y = Note("nothing here to work at", x + indent * 2f, y, w - indent * 2f, subH);
                foreach (var planId in posts)
                {
                    var plan = BuildPlans.Named(planId);
                    if (GUI.Button(new Rect(x + indent * 2f, y, w - indent * 2f, subH),
                            $"{plan.position} at the {plan.label}", sub))
                    {
                        camp.Assign(h, planId);
                        open = null; verb = Verb.None;
                    }
                    y += subH + 2f;
                }
            }

            y = Verb1(ref verb, Verb.Gather, "gather  ▸", x + indent, y, w - indent, subH);
            if (verb == Verb.Gather)
            {
                foreach (var res in camp.Gatherable())
                {
                    var stock = camp.Ledger.Stock(res);
                    float left = stock != null ? stock.standing : 0f;
                    string tail = left < 1f ? " — worked out"
                        : $" — {camp.Ledger.CountOf(res)}/{camp.Ledger.ceilingPer} kept";
                    if (GUI.Button(new Rect(x + indent * 2f, y, w - indent * 2f, subH),
                            res.ToLowerInvariant() + tail, sub))
                    {
                        camp.OrderGather(h, res);
                        open = null; verb = Verb.None;
                    }
                    y += subH + 2f;
                }
            }

            y = BuildBlock(camp, x, y, w, subH, indent);
            return y;
        }

        /// The build list: under an open hand's row, or on its own when the
        /// player tapped the fire (Kevin, 2026-09-21: "press on the campfire
        /// to get the options menu for building").
        float BuildBlock(Outpost camp, float x, float y, float w, float subH, float indent)
        {
            y = Verb1(ref verb, Verb.Build, "build  ▸", x + indent, y, w - indent, subH);
            if (verb == Verb.Build)
            {
                if (camp.Building)
                    y = Note("something is already going up", x + indent * 2f, y, w - indent * 2f, subH);
                else
                    foreach (var plan in camp.Buildable())
                    {
                        var pr = new Rect(x + indent * 2f, y, w - indent * 2f, subH);
                        // Claimed one by one as well as by the panel: a row
                        // the panel's own height arithmetic ever misses again
                        // is still a row the world cannot be tapped through.
                        UIBlocker.Block(pr);
                        if (GUI.Button(pr,
                                plan.stoneCost > 0
                                    ? $"{plan.label} — {plan.cost} timber {plan.stoneCost} stone"
                                    : $"{plan.label} — {plan.cost} timber", sub))
                        {
                            // Straight into siting mode: a building is put
                            // WHERE the player says, which is the one thing
                            // this list cannot ask on its own.
                            var motor = Object.FindFirstObjectByType<ShipMotor>();
                            CampSiting.Begin(camp, plan, motor != null ? motor.transform : null);
                            open = null; verb = Verb.None; showBuild = false;
                        }
                        y += subH + 2f;
                    }
            }
            return y;
        }

        /// A tap on the fire opens the build list at camp level, no hand needed.
        bool showBuild;
        public void OpenBuild() { showBuild = true; open = null; verb = Verb.Build; }
        public void CloseBuild() { showBuild = false; if (open == null) verb = Verb.None; }

        float Verb1(ref Verb current, Verb which, string label,
            float x, float y, float w, float h)
        {
            bool on = current == which;
            var vr = new Rect(x, y, w, h);
            UIBlocker.Block(vr);
            if (GUI.Button(vr, label, on ? UITheme.ButtonPressed : sub))
                current = on ? Verb.None : which;
            return y + h + 2f;
        }

        float Note(string text, float x, float y, float w, float h)
        {
            GUI.Label(new Rect(x, y, w, h), text, note);
            return y + h + 2f;
        }

        /// How many rows a verb's list will take, so the panel can be the
        /// right height before anything is drawn.
        int OptionCount(Outpost camp, Verb v) => v switch
        {
            Verb.Assign => Mathf.Max(1, camp.Positions().Count),
            Verb.Gather => Mathf.Max(1, camp.Gatherable().Count),
            Verb.Build => camp.Building ? 1 : Mathf.Max(1, camp.Buildable().Count),
            _ => 0,
        };

        void EnsureStyles()
        {
            if (row != null) return;
            row = new GUIStyle(UITheme.Button) { alignment = TextAnchor.MiddleLeft };
            sub = new GUIStyle(UITheme.Button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(9, UITheme.Button.fontSize - 1),
            };
            note = new GUIStyle(UITheme.Small) { alignment = TextAnchor.MiddleLeft };
        }
    }
}
