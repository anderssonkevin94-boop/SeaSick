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
    public class SiteSheet : ISheet
    {
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
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            root.Add(SheetKit.Header("going up", Title, SheetTheme.Timber, "⚒",
                () => Sheets.Close()));

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

            root.Add(SheetKit.Rule());

            addHand = SheetKit.Btn("Add a hand", AddHand, true);
            root.Add(SheetKit.Row(
                addHand,
                SheetKit.Btn("Cancel", CancelBuild, false, true),
                SheetKit.Btn("Move", MoveBuild, false, true)));

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
            long key = (((long)p.done * 31 + p.stoneDone) * 31 + p.needed * 7 + p.stoneNeeded)
                       * 31 + builders;
            if (key != chipsKey)
            {
                chipsKey = key;
                chips.Clear();
                // **The delivered chips only appear once something has been
                // delivered.** "0 of 5 timber · 0 of 3 stone" over "Needs 5
                // timber and 3 stone" is the same sentence twice, and on a
                // site nobody has carried a log to yet the first of them is
                // pure noise.
                if (p.done > 0 || p.stoneDone > 0)
                    chips.Add(SheetKit.Row(
                        SheetKit.Text($"{p.done} of {p.needed} timber", false, false, 12f),
                        p.stoneNeeded > 0
                            ? SheetKit.Text($"{p.stoneDone} of {p.stoneNeeded} stone", false, false, 12f)
                            : SheetKit.Text("", false, true, 12f)));
                chips.Add(SheetKit.Note(crew + ". " + (
                    timberLeft == 0 && stoneLeft == 0 ? "Stocked, everything it wants is here."
                    : stoneLeft == 0 ? $"Needs {timberLeft} timber."
                    : timberLeft == 0 ? $"Needs {stoneLeft} stone."
                    : $"Needs {timberLeft} timber, {stoneLeft} stone.")));
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
            if (timberLeft == 0 && stoneLeft == 0) return "ready to raise";
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
