using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The drawing on the ground: what it still wants, and the three things
    /// you can do about it.**
    ///
    /// Kevin, 2026-09-21: *"the blueprint should be pressable where it states
    /// how many resources it still needs and the option to cancel the build /
    /// move it."* The old camp bar answered that in a panel; this is the
    /// same three facts and the same two calls, unfolded beside the stakes
    /// instead of along the bottom of the screen, plus the one
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

        /// **The row this sheet is about (2026-09-22).** A camp can queue
        /// several drawings now, so "the pending one" would open the wrong
        /// sheet the moment there were two: the sheet holds the row the
        /// tapped `BuildSite` carries, and every verb on it acts on THAT
        /// row.
        readonly PendingBuild row;

        public SiteSheet(Outpost o, BuildSite s)
        {
            outpost = o;
            site = s;
            planId = s != null ? s.PlanId : null;
            row = s != null ? s.Row : null;
            label = BuildPlans.Named(row != null ? row.planId : planId).label;
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
                if (outpost == null || site == null || row == null) return false;
                var l = outpost.Ledger;
                return l != null && l.sites != null && l.sites.Contains(row);
            }
        }

        PendingBuild Pending => row;

        // --- the pieces kept between refreshes ---------------------------------

        Label big;            // "60 %"
        Label who;            // "Bo is on it · about 2 days left"
        VisualElement ring;   // the bar standing in for a progress ring
        VisualElement chips;
        Button addHand;

        /// **The fix for what this drawing is short of (2026-09-30, island UI
        /// rule 2).** One button under the have/need chips for the material
        /// the CAMP cannot cover -- built once in `Build`, shown/hidden and
        /// re-labelled by `Refresh`, never rebuilt with the chips.
        ShortFix.Slot fixSlot;

        long chipsKey = long.MinValue;
        int ringKey = -99;

        public VisualElement Build()
        {
            big = null; who = null; ring = null; chips = null; fixSlot = null;
            chipsKey = long.MinValue;
            ringKey = -99;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            // **Opened with no row (2026-09-27).** Every real trigger --
            // a tap, `SheetBootstrap`'s `BuildSite` registration -- refuses
            // to build this sheet at all when `s.Row == null`. A caller
            // that constructs `SiteSheet` directly (a script, a probe) can
            // skip that guard; this is the fallback so a null row reads as
            // one honest line instead of a blank "0%" card that closes
            // itself a tick later.
            if (Pending == null)
            {
                root.Add(SheetKit.Text("This blueprint is gone.", false, true, 14f));
                return root;
            }

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

            fixSlot = new ShortFix.Slot(Refresh);
            root.Add(fixSlot.button);

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var l = outpost != null ? outpost.Ledger : null;
            var p = Pending;
            if (l == null || p == null) return;
            outpost.CatchUp();

            // **The percentage is the BUILDING alone** (Kevin's phone
            // playtest, 2026-09-23: *"the percentages also start going up
            // before resource quota is met"*). `Progress01` reads 0 % while
            // the plot is cleared and stocked -- the chips below count the
            // deliveries in whole units -- then 0 to 100 over the hammering.
            int pct = Mathf.RoundToInt(p.Progress01 * 100f);
            if (pct != ringKey)
            {
                ringKey = pct;
                if (big != null) big.text = pct + "%";
                SheetBits.Swap(ring, SheetKit.Bar(p.Progress01, SheetTheme.Timber, 10f));
            }

            int builders = l.HandsOn(OutpostOrder.Build);

            // **The clock is the HAMMER alone, 2026-09-27** (Kevin: "that
            // timer is only active/relevant when resources are in place and
            // someone is working on the building"): "hammering · 42 s left"
            // while it runs, otherwise what it waits for -- "waiting for 3
            // stone", "no builder". No walk-time estimate anywhere.
            string clock = l.SiteLine(p);
            if (who != null) who.text = Cap(clock);

            // **Who is doing what** (Kevin, 2026-09-27: "x is gathering
            // resources for the build, or y is building"), one clause per
            // hand whose task is this site -- `OutpostLedger.WhoIsOn`.
            var crewOn = l.WhoIsOn(p);
            string crew = CrewSentence(crewOn, builders);
            long crewKey = crewOn.Count;
            foreach (var c in crewOn)
                crewKey = crewKey * 31 + (c.hand.name?.GetHashCode() ?? 0) + c.verb.GetHashCode() * 7 + c.load;

            int timberLeft = Mathf.Max(0, p.needed - p.done);
            int stoneLeft = Mathf.Max(0, p.stoneNeeded - p.stoneDone);
            // Brick is `BuildPlan.brickCost` reaching the site -- zero on
            // every plan today (BuildPlan.cs:80), so `p.brickNeeded` is zero
            // and this whole branch is dormant until an upgrade actually
            // prices itself in bricks.
            int brickLeft = Mathf.Max(0, p.brickNeeded - p.brickDone);
            long key = ((((long)p.done * 31 + p.stoneDone) * 31 + p.needed * 7 + p.stoneNeeded)
                       * 31 + builders) * 31 + (p.brickDone * 31 + p.brickNeeded);
            // The building phase moves without any counter moving, so the
            // key has to carry it or the sheet freezes at "stocked".
            key = key * 31 + Mathf.RoundToInt(p.Build01 * 100f);
            key = key * 31 + crewKey;
            // **The CLEAR phase, 2026-09-23.** `ClearLeft` moves as hands
            // fell trees and break rocks well before a single log is
            // stocked -- without it in the key the sheet would freeze on
            // "clearing: 4 trees, 1 rock left" for the whole phase.
            key = key * 31 + p.ClearLeft;
            // Why it is not moving (hungry / waiting its turn), 2026-09-23.
            string stall = l.StallReason(p);
            key = key * 31 + stall.GetHashCode();
            if (key != chipsKey && chips != null)
            {
                chipsKey = key;
                chips.Clear();
                // **The phase, in the site's own words** -- "clearing: 3
                // trees, 1 rock left" comes first (2026-09-23: the CLEAR
                // phase the villagers work through before a single log is
                // laid, `OutpostLedger.PendingBuild.Cleared`), then
                // "stocking 3/6 logs, 2/2 stone" or "building 40%". One
                // string, shared with the camp page (`FireSheet.Note`), so
                // the two cannot say different things about the same
                // drawing.
                chips.Add(SheetKit.Text(Cap(p.Cleared ? p.PhaseLine : ClearingLine(p)),
                    true, false, 13f));

                // **Have/need, one chip per material this site still
                // wants** (2026-09-27 restyle: candidate #5). A blueprint
                // that has not been touched yet still shows "0/5 timber" --
                // that IS the shortfall, not noise -- and a short pile is
                // red the same way a short build-list card is (Kevin's
                // Midnight tile language, `BuildSheet`'s cost slots).
                var mats = new List<VisualElement>(3);
                if (p.needed > 0) mats.Add(MaterialChip(Res.Timber, p.done, p.needed));
                if (p.stoneNeeded > 0) mats.Add(MaterialChip(Res.Stone, p.stoneDone, p.stoneNeeded));
                if (p.brickNeeded > 0) mats.Add(MaterialChip(Res.Brick, p.brickDone, p.brickNeeded));
                if (mats.Count > 0) chips.Add(SheetKit.Row(mats.ToArray()));

                // **Who is on it, as tappable villager tiles (2026-09-27).**
                // A tap opens exactly the hand it is a picture of --
                // `HandSheet(outpost, name)`, the same sheet the world tap
                // on that villager opens.
                if (crewOn.Count > 0)
                {
                    var toks = new VisualElement[crewOn.Count];
                    for (int i = 0; i < crewOn.Count; i++)
                    {
                        var nm = crewOn[i].hand.name;
                        toks[i] = SheetKit.Token(SheetBits.Initial(nm), false, SheetBits.JobGlyph(crewOn[i].hand),
                            () => Sheets.Open(new HandSheet(outpost, nm)));
                    }
                    chips.Add(SheetKit.Row(toks));
                }

                string need = !p.Stocked
                    ? NeedSentence(timberLeft, stoneLeft, brickLeft)
                    : p.Complete
                        ? "Everything is in and it is going up."
                        : "Everything it wants is here; now they raise it.";
                chips.Add(SheetKit.Note(crew + " " + need));
                // **The idle-hand ladder's own warning** (step 5, "find that
                // resource") -- `OutpostLedger.StallReason`, the same line
                // the camp-wide alert chip reads.
                if (stall.Length > 0) chips.Add(SheetKit.Note(stall));
            }

            if (addHand != null)
                addHand.SetEnabled(SheetBits.FirstIdle(l) != null);

            // The material the camp's own pile cannot cover, most missing
            // first (a gatherable wins a tie): what is in the pile the
            // builders will carry over, so only a real shortfall is a problem.
            if (fixSlot != null)
            {
                var most = new ShortFix.Most();
                if (!p.Stocked)
                {
                    most.Add(Res.Timber, timberLeft - l.SpendableOf(Res.Timber));
                    most.Add(Res.Stone, stoneLeft - l.SpendableOf(Res.Stone));
                    most.Add(Res.Brick, brickLeft - l.SpendableOf(Res.Brick));
                }
                fixSlot.Bind(outpost, most.Res);
            }
        }

        /// "Gale is fetching timber (2 in her arms). Tam is building." --
        /// one sentence per hand on this site, names first. With builders
        /// on the order but none on THIS site (another drawing is ahead of
        /// it), says so rather than naming nobody.
        static string CrewSentence(List<SiteHand> on, int builders)
        {
            if (on.Count == 0)
                return builders == 0 ? "Nobody is assigned." : "The builders are on another drawing.";
            var sb = new System.Text.StringBuilder();
            foreach (var c in on)
            {
                sb.Append(c.hand.name).Append(" is ").Append(c.verb);
                if (c.load > 0 && !string.IsNullOrEmpty(c.res))
                    sb.Append(" · ").Append(c.load).Append(" on the way");
                sb.Append(". ");
            }
            return sb.ToString().TrimEnd();
        }

        /// **"clearing: 3 trees, 1 rock left"** -- the CLEAR phase's own
        /// phase line, singular/plural correct, ahead of stocking/building
        /// in `p.PhaseLine`. Only ever asked for while `!p.Cleared`.
        static string ClearingLine(PendingBuild p)
        {
            var parts = new List<string>(2);
            if (p.TreesLeft > 0) parts.Add(p.TreesLeft == 1 ? "1 tree" : $"{p.TreesLeft} trees");
            if (p.RocksLeft > 0) parts.Add(p.RocksLeft == 1 ? "1 rock" : $"{p.RocksLeft} rocks");
            return parts.Count == 0 ? "clearing" : "clearing: " + string.Join(", ", parts) + " left";
        }

        /// One have/need tile: "timber · 3/5", red border while short.
        static VisualElement MaterialChip(string res, int have, int need) =>
            SheetKit.Chip(ResDefs.Label(res), $"{have}/{need}", have >= need ? SheetTheme.Moss : SheetTheme.Ember);

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
        /// go back on the pile.
        void CancelBuild()
        {
            if (outpost == null) return;
            outpost.CancelPending(row);
            Sheets.Close();
        }

        /// `CampSiting.Begin(..., movePending: true)` -- the drawing stays
        /// where it is until the new spot is tapped, so escaping the move
        /// leaves the camp exactly as it was.
        void MoveBuild()
        {
            var p = Pending;
            if (outpost == null || p == null) return;
            var plan = BuildPlans.Named(p.planId).WithLength(p.length);
            CampSiting.Begin(outpost, plan, SheetBits.ShipTransform, p);
            Sheets.Close();
        }
    }
}
