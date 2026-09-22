using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The drawing on the ground: what it still wants, and the three things
    /// you can do about it.**
    ///
    /// Kevin, 2026-09-21: *"the blueprint should be pressable where it states
    /// how many resources it still needs and the option to cancel the build /
    /// move it."* That is `CampSheet.BlueprintPanel` (CampSheet.cs:546-608);
    /// this is the same three facts and the same two calls, unfolded beside
    /// the stakes instead of along the bottom of the screen, plus the one
    /// thing the old panel could not do -- put somebody on it.
    ///
    /// **It closes itself the frame the building goes up.** `BuildSite.Retire`
    /// destroys the drawing when the row is raised, and `StillValid` is that
    /// object plus the row: a sheet about a promise that has been kept has
    /// nothing left to say.
    public class SiteSheet : ISheetFramed
    {
        // --- the frame (2026-09-22, "one sheet, tabs") ----------------------
        //
        // **One section, so no tab strip.** A drawing on the ground is one
        // fact -- how far along it is -- and three verbs. A strip with a
        // single tab on it would be a label dressed as a control, so the
        // frame simply leaves it out.
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Timber;

        public VisualElement BuildHeader() =>
            SheetKit.Header("going up", Title, SheetTheme.Timber, "⚒",
                () => Sheets.Close());

        /// The three verbs, pinned: staff it, call it off, put it somewhere
        /// else. Same calls as before -- they have simply stopped being the
        /// last thing you scroll to.
        public VisualElement BuildActions()
        {
            addHand = SheetKit.Btn("Add a hand", AddHand, true);
            return SheetKit.Actions(
                addHand,
                SheetKit.Btn("Cancel", CancelBuild, false, true),
                SheetKit.Btn("Move", MoveBuild, false, true));
        }

        readonly Outpost outpost;
        readonly BuildSite site;
        readonly string planId;
        readonly string label;

        public SiteSheet(Outpost o, BuildSite s)
        {
            outpost = o;
            site = s;
            planId = s != null ? s.PlanId : null;
            var l = o != null ? o.Ledger : null;
            var p = l != null ? l.pending : null;
            label = BuildPlans.Named(p != null ? p.planId : planId).label;
        }

        public string Title => string.IsNullOrEmpty(label) ? "blueprint" : label;

        public Vector3 AnchorWorld => site != null
            ? site.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        /// The drawing is still standing and the row behind it is still open.
        public bool StillValid
        {
            get
            {
                if (outpost == null || site == null) return false;
                var l = outpost.Ledger;
                return l != null && l.pending != null && l.pending.planId == planId;
            }
        }

        PendingBuild Pending
        {
            get
            {
                var l = outpost != null ? outpost.Ledger : null;
                return l != null ? l.pending : null;
            }
        }

        // --- the pieces kept between refreshes ---------------------------------

        Label big;            // "60 %"
        Label who;            // "Bo is on it · about 2 days left"
        VisualElement ring;   // the bar standing in for a progress ring
        VisualElement chips;
        Button addHand;

        long chipsKey = long.MinValue;
        int ringKey = -99;

        public VisualElement Build()
        {
            big = null; who = null; ring = null; chips = null;
            chipsKey = long.MinValue;
            ringKey = -99;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            // **A ring is a bar you have not drawn yet.** The percentage is
            // the number the player reads; the bar under it is what makes it
            // a shape rather than a figure, and a real ring can replace both
            // without this file changing.
            big = SheetKit.Text("0%", true, false, 28f);
            ring = SheetBits.Holder();
            root.Add(SheetKit.Row(big, ring));

            who = SheetKit.Text("", false, true, 12f);
            root.Add(who);

            chips = SheetBits.Holder();
            root.Add(chips);

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var l = outpost != null ? outpost.Ledger : null;
            var p = Pending;
            if (l == null || p == null) return;
            outpost.CatchUp();

            int pct = Mathf.RoundToInt(p.Fill01 * 100f);
            if (pct != ringKey)
            {
                ringKey = pct;
                if (big != null) big.text = pct + "%";
                SheetBits.Swap(ring, SheetKit.Bar(p.Fill01, SheetTheme.Timber, 10f));
            }

            int builders = l.HandsOn(OutpostOrder.Build);
            string first = null;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Build) { first = h.name; break; }

            // The line under the percentage is the CLOCK; the note below is
            // who and what. Splitting them is what lets the note read as one
            // sentence -- "Bo is on it. Needs 5 timber, 3 stone." -- instead
            // of a row of half-facts joined by dots.
            string clock = DaysLeftLine(l, p, builders);
            // With nobody on it the clock has nothing to say that the note
            // below does not say better, so it says nothing.
            if (who != null) who.text = clock == Nobody ? "" : Cap(clock);

            string crew = builders == 0 ? "Nobody is on it"
                : builders == 1 ? $"{first} is on it"
                : $"{first} and {builders - 1} more are on it";

            int timberLeft = Mathf.Max(0, p.needed - p.done);
            int stoneLeft = Mathf.Max(0, p.stoneNeeded - p.stoneDone);
            // Brick is `BuildPlan.brickCost` reaching the site -- zero on
            // every plan today (BuildPlan.cs:80), so `p.brickNeeded` is zero
            // and this whole branch is dormant until an upgrade actually
            // prices itself in bricks.
            int brickLeft = Mathf.Max(0, p.brickNeeded - p.brickDone);
            long key = ((((long)p.done * 31 + p.stoneDone) * 31 + p.needed * 7 + p.stoneNeeded)
                       * 31 + builders) * 31 + (p.brickDone * 31 + p.brickNeeded);
            if (key != chipsKey && chips != null)
            {
                chipsKey = key;
                chips.Clear();
                // **The delivered chips only appear once something has been
                // delivered.** "0 of 5 timber · 0 of 3 stone" over "Needs 5
                // timber and 3 stone" is the same sentence twice, and on a
                // site nobody has carried a log to yet the first of them is
                // pure noise.
                if (p.done > 0 || p.stoneDone > 0 || p.brickDone > 0)
                {
                    var row = new List<VisualElement>
                    {
                        SheetKit.Text($"{p.done} of {p.needed} timber", false, false, 12f),
                        p.stoneNeeded > 0
                            ? SheetKit.Text($"{p.stoneDone} of {p.stoneNeeded} stone", false, false, 12f)
                            : SheetKit.Text("", false, true, 12f),
                    };
                    if (p.brickNeeded > 0)
                        row.Add(SheetKit.Text($"{p.brickDone} of {p.brickNeeded} bricks", false, false, 12f));
                    chips.Add(SheetKit.Row(row.ToArray()));
                }
                string need = timberLeft == 0 && stoneLeft == 0 && brickLeft == 0
                    ? "Stocked, everything it wants is here."
                    : NeedSentence(timberLeft, stoneLeft, brickLeft);
                chips.Add(SheetKit.Note(crew + ". " + need));
            }

            if (addHand != null)
                addHand.SetEnabled(SheetBits.FirstIdle(l) != null);
        }

        /// **"about 2 days left", out of the ledger's own day rates.**
        ///
        /// A guess by construction -- the hands have to walk, the stock can
        /// run out, and a starving camp works at `WorkFactor` -- so it says
        /// "about". `TimberPerHandPerDay` / `StonePerHandPerDay` are the same
        /// constants `OutpostLedger.Step` builds with, so the estimate and the
        /// thing it estimates cannot drift apart.
        /// The one answer callers have to be able to recognise: it is the
        /// only one that is about the CREW rather than about the clock, and
        /// both sheets say that part in their own words instead.
        public const string Nobody = "nobody is building it";

        public static string DaysLeftLine(OutpostLedger l, PendingBuild p, int builders)
        {
            if (p == null) return "not started";
            int timberLeft = Mathf.Max(0, p.needed - p.done);
            int stoneLeft = Mathf.Max(0, p.stoneNeeded - p.stoneDone);
            // Brick has no per-hand pace below -- `OutpostLedger.PayBrick`
            // pays it straight out of the pile, never off a hand's back
            // (BuildPlan.cs:86) -- so it only holds this clock at "ready to
            // raise" until the stores cover it; it never slows the estimate.
            int brickLeft = Mathf.Max(0, p.brickNeeded - p.brickDone);
            if (timberLeft == 0 && stoneLeft == 0 && brickLeft == 0) return "ready to raise";
            if (builders <= 0) return Nobody;

            float days = 0f;
            if (timberLeft > 0 && OutpostLedger.TimberPerHandPerDay > 0f)
                days += timberLeft / (builders * OutpostLedger.TimberPerHandPerDay);
            if (stoneLeft > 0 && OutpostLedger.StonePerHandPerDay > 0f)
                days += stoneLeft / (builders * OutpostLedger.StonePerHandPerDay);

            if (days < 0.75f) return "about half a day left";
            if (days < 1.5f) return "about a day left";
            return $"about {days:0.#} days left";
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// "Needs 5 timber, 3 stone and 2 bricks." -- one clause per short
        /// pile that is still owed, joined the way a person would say them.
        /// Bricks come last: they are the one thing here nobody can walk out
        /// and gather (BuildPlan.cs:86), so the list ends on the part that is
        /// waiting on the fire's own stores rather than on a hand's back.
        static string NeedSentence(int timberLeft, int stoneLeft, int brickLeft)
        {
            var parts = new List<string>(3);
            if (timberLeft > 0) parts.Add($"{timberLeft} timber");
            if (stoneLeft > 0) parts.Add($"{stoneLeft} stone");
            if (brickLeft > 0) parts.Add($"{brickLeft} bricks");
            if (parts.Count == 0) return "Stocked, everything it wants is here.";
            if (parts.Count == 1) return $"Needs {parts[0]}.";
            if (parts.Count == 2) return $"Needs {parts[0]}, {parts[1]}.";
            return $"Needs {parts[0]}, {parts[1]} and {parts[2]}.";
        }

        // --- the three verbs ------------------------------------------------------

        /// The per-hand build order -- `Outpost.OrderBuild` (Outpost.cs:1680),
        /// which is what the Hand does when it drops somebody on a drawing.
        /// The crew list's build verb sites a NEW building; this one staffs
        /// the drawing that is already here.
        void AddHand()
        {
            var l = outpost != null ? outpost.Ledger : null;
            var free = SheetBits.FirstIdle(l);
            if (free == null) return;
            outpost.OrderBuild(free);
            Refresh();
        }

        /// `Outpost.CancelPending` -- the wood and stone already carried here
        /// go back on the pile (CampSheet.cs:594).
        void CancelBuild()
        {
            if (outpost == null) return;
            outpost.CancelPending();
            Sheets.Close();
        }

        /// `CampSiting.Begin(..., movePending: true)` -- the drawing stays
        /// where it is until the new spot is tapped, so escaping the move
        /// leaves the camp exactly as it was (CampSheet.cs:602).
        void MoveBuild()
        {
            var p = Pending;
            if (outpost == null || p == null) return;
            var plan = BuildPlans.Named(p.planId).WithLength(p.length);
            CampSiting.Begin(outpost, plan, SheetBits.ShipTransform, movePending: true);
            Sheets.Close();
        }
    }
}
