using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **One villager, and everything you can tell them.**
    ///
    /// `CampCrewList` is this list down the right-hand side of the screen for
    /// everybody at once: a name, then three verbs under it, then the options
    /// under whichever verb is open (CampCrewList.cs:166-252). A sheet is
    /// about ONE thing, so the three verbs are three blocks and nothing is
    /// folded away -- and it unfolds beside the person it is about, which is
    /// the whole reason the redesign is worth doing.
    ///
    /// **Kept by name, not by reference.** The rows are rebuilt from the
    /// ledger and a cached row is a row that outlives the hand -- the same
    /// reason `CampCrewList` keys its open row on `h.name`.
    public class HandSheet : ISheetFramed
    {
        // --- the frame (2026-09-22, "one sheet, tabs") ----------------------
        //
        // Two sections, and they answer different questions: "orders" is what
        // you can TELL them, "about" is who they are and why they are in the
        // mood they are in. The two verbs that are neither -- stand down, and
        // back aboard -- are the pinned action row, reachable from both.
        public const int TabOrders = 0;
        public const int TabAbout = 1;

        int tab = -1;
        static readonly string[] tabLabels = { "orders", "about" };

        public int Tab => tab;
        public void SetTab(int index) { tab = index; }
        public string[] TabLabels => tabLabels;
        public Color Accent => SheetTheme.Brass;

        readonly Outpost outpost;
        readonly string who;

        public HandSheet(Outpost o, string who)
        {
            outpost = o;
            this.who = who;
        }

        public string Title => who;

        OutpostHand Hand => outpost != null ? outpost.HandNamed(who) : null;

        public bool StillValid => outpost != null && outpost.Ledger != null && Hand != null;

        /// Where he is standing. The parked body is the honest answer; the
        /// fire is the fallback for a hand whose body has not been raised yet
        /// (`OutpostHand.born`, before `Outpost.SpawnVillager` runs).
        public Vector3 AnchorWorld
        {
            get
            {
                if (outpost == null) return Vector3.zero;
                var body = outpost.BodyNamed(who);
                return body != null ? body.transform.position : outpost.CampCentre;
            }
        }

        // --- the pieces kept between refreshes ---------------------------------

        Label doing;
        Label cause;
        VisualElement verbs;
        long verbsKey = long.MinValue;

        public VisualElement BuildHeader() =>
            SheetKit.Header("hand", Title, SheetTheme.Brass,
                SheetBits.JobGlyph(Hand), () => Sheets.Close());

        /// **Standing them down and sending them back aboard are pinned.**
        /// They were the last two rows of a long list of verbs, which on a
        /// phone meant scrolling past every job on the island to stop
        /// somebody doing one.
        public VisualElement BuildActions()
        {
            var h = Hand;
            stand = SheetKit.Btn("stand down", () =>
            {
                outpost.OrderIdle(Hand);
                verbsKey = long.MinValue;
                Refresh();
            });
            stand.SetEnabled(h != null && h.order != OutpostOrder.Idle);

            back = SheetKit.Btn("back aboard", BackAboard);
            back.SetEnabled(SheetBits.Anchor != null && outpost != null
                            && outpost.BodyNamed(who) != null);
            return SheetKit.Actions(stand, back);
        }

        Button stand;
        Button back;

        public VisualElement Build()
        {
            doing = null; cause = null; verbs = null;
            verbsKey = long.MinValue;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            if (tab == TabAbout)
            {
                var h = Hand;
                root.Add(SheetKit.Row(
                    SheetKit.Token(SheetBits.Initial(who), h != null && h.Angry,
                        SheetBits.JobGlyph(h)),
                    SheetKit.Col(
                        doing = SheetKit.Text("", true),
                        cause = SheetKit.Text("", false, true, 12f))));
            }
            else
            {
                verbs = SheetBits.Holder();
                root.Add(verbs);
            }

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var l = outpost != null ? outpost.Ledger : null;
            var h = Hand;
            if (l == null || h == null) return;
            outpost.CatchUp();

            if (doing != null) doing.text = Cap(h.Doing);
            if (cause != null) cause.text = Mood(l, h);
            if (stand != null) stand.SetEnabled(h.order != OutpostOrder.Idle);
            if (back != null)
                back.SetEnabled(SheetBits.Anchor != null && outpost.BodyNamed(who) != null);
            if (verbs == null) return;

            // The verb lists change only when the camp does -- a building
            // raised, a seam worked out, a blueprint sited. Keyed on exactly
            // that, so pressing one of them does not rebuild the list under
            // the finger that is pressing it.
            long key = (l.built.Count * 31L + l.stocks.Count) * 31L
                       + (l.pending != null ? 1 : 0) * 7919L
                       + (int)h.order * 131L
                       + (h.target != null ? h.target.GetHashCode() : 0);
            if (key == verbsKey) return;
            verbsKey = key;
            BuildVerbs(l, h);
        }

        /// The mood word and, where the ledger knows one, what caused it.
        /// Nothing here decides a mood: `OutpostHand.MoodWord`, `hungerDays`
        /// and `raids` are the ledger's own, and this only reads them out.
        static string Mood(OutpostLedger l, OutpostHand h)
        {
            string word = h.MoodWord;
            if (word.Length == 0) return "content";
            if (l.Hungry) return word + " · the food pile is empty";
            if (l.hungerDays > 0.05f)
                return word + $" · {l.hungerDays:0.#} days gone short";
            if (l.raids > 0)
                return word + (l.raids == 1 ? " · the camp was raided"
                                            : $" · raided {l.raids} times");
            return word;
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // --- the three verbs, out of the same three lists ------------------------

        void BuildVerbs(OutpostLedger l, OutpostHand h)
        {
            verbs.Clear();

            // ASSIGN -- `Outpost.Positions()` (Outpost.cs:1716), pressed with
            // `Outpost.Assign` (CampCrewList.cs:177).
            verbs.Add(SheetKit.Eyebrow("put to work"));
            var posts = outpost.Positions();
            if (posts.Count == 0)
                verbs.Add(SheetKit.Note("Nothing here to work at"));
            foreach (var id in posts)
            {
                string planId = id;
                var plan = BuildPlans.Named(planId);
                bool already = h.order == OutpostOrder.Work && h.target == planId;
                var b = SheetKit.Btn($"{plan.position} at the {plan.label}", () =>
                {
                    outpost.Assign(Hand, planId);
                    verbsKey = long.MinValue;
                    Refresh();
                }, false, true);
                b.SetEnabled(!already);
                verbs.Add(b);
            }

            // GATHER -- `Outpost.Gatherable()` (Outpost.cs:1705), pressed with
            // `Outpost.OrderGather` (CampCrewList.cs:198). A worked-out stock
            // is still listed: an empty seam is information, and hiding it
            // would look like the menu was broken.
            verbs.Add(SheetKit.Eyebrow("send out for"));
            foreach (var res in outpost.Gatherable())
            {
                string r = res;
                var stock = l.Stock(r);
                float standing = stock != null ? stock.standing : 0f;
                string tail = standing < 1f
                    ? " — worked out"
                    : $" — {l.CountOf(r)}/{l.ceilingPer} kept";
                bool already = h.order == OutpostOrder.Gather && h.target == r;
                var b = SheetKit.Btn(CampLoading.Lower(r) + tail, () =>
                {
                    outpost.OrderGather(Hand, r);
                    verbsKey = long.MinValue;
                    Refresh();
                }, false, true);
                b.SetEnabled(!already);
                verbs.Add(b);
            }

            // BUILD -- the drawing that is already up takes this hand
            // (`Outpost.OrderBuild`, Outpost.cs:1680); the list below sites a
            // new one, straight into `CampSiting` the way the crew list's
            // build verb does (CampCrewList.cs:236).
            verbs.Add(SheetKit.Eyebrow("build"));
            if (outpost.Building)
            {
                var p = l.pending;
                var plan = BuildPlans.Named(p.planId);
                bool already = h.order == OutpostOrder.Build;
                var b = SheetKit.Btn($"work on the {plan.label}", () =>
                {
                    outpost.OrderBuild(Hand);
                    verbsKey = long.MinValue;
                    Refresh();
                }, false, true);
                b.SetEnabled(!already);
                verbs.Add(b);
            }
            else
            {
                foreach (var plan in outpost.Buildable())
                {
                    var p = plan;
                    verbs.Add(SheetKit.Btn(p.stoneCost > 0
                        ? $"{p.label} — {p.cost} timber {p.stoneCost} stone"
                        : $"{p.label} — {p.cost} timber", () =>
                    {
                        CampSiting.Begin(outpost, p, SheetBits.ShipTransform);
                        Sheets.Close();
                    }, false, true));
                }
            }

        }

        /// `Outpost.Recall(body, ship)` -- the same call the ashore column
        /// makes (CampSheet.cs:519), including the roster recount the ship
        /// needs afterwards (CampSheet.cs:521).
        void BackAboard()
        {
            var anchor = SheetBits.Anchor;
            if (outpost == null || anchor == null) return;
            var body = outpost.BodyNamed(who);
            if (body == null) return;
            if (outpost.Recall(body, anchor.transform))
            {
                var roster = SheetBits.Roster;
                if (roster != null) roster.Refresh();
                Sheets.Close();
            }
        }
    }
}
