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
        // --- the frame (2026-09-22, "pages you swipe between") --------------
        //
        // Two sections, and they answer different questions: "orders" is what
        // you can TELL them, "about" is who they are and why they are in the
        // mood they are in. The two verbs that are neither -- stand down, and
        // back aboard -- are the pinned action row, reachable from both.
        //
        // **Orders is as many pages as it takes.** A camp with a sawmill, a
        // quarry and five seams offers more verbs than a phone's bottom third
        // can hold, and the answer is more pages, never a scroll: "orders
        // 1/2", "orders 2/2", "about".
        int tab = -1;

        readonly SheetPager orders = new SheetPager();
        int orderPages = 1;
        string[] labels = { "orders", "about" };
        long planKey = long.MinValue;

        public int Tab { get { Plan(); return tab; } }
        public void SetTab(int index) { tab = index; }
        public string[] TabLabels { get { Plan(); return labels; } }
        public Color Accent => SheetTheme.Brass;

        /// The page index of the "about" section -- the last one, whatever
        /// the orders ran to.
        int AboutPage => orderPages;

        /// **The body the camera should watch.** `Sheets.Open` reads this and
        /// puts `IslandCam` on him, so a hand who walks off to a seam while
        /// his card is open stays in the frame (Kevin, 2026-09-22: "I want
        /// the camera to follow them"). Null before his body is raised, which
        /// simply means nothing to follow yet.
        public Transform FollowTarget
        {
            get
            {
                var body = outpost != null ? outpost.BodyNamed(who) : null;
                return body != null ? body.transform : null;
            }
        }

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
                Dirty();
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
            Plan();

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            if (tab >= AboutPage)
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
                verbs.Add(orders.Build(Mathf.Max(0, tab)));
                root.Add(verbs);
            }

            Refresh();
            return root;
        }

        // --- the page plan --------------------------------------------------
        //
        // Worked out from the band the frame actually has (`SheetHost`), so
        // the same hand is two pages of verbs on a phone and one on a desk
        // without a number being written down twice.

        void Plan()
        {
            var l = outpost != null ? outpost.Ledger : null;
            var h = Hand;
            if (l == null || h == null) return;

            long key = (l.built.Count * 31L + l.stocks.Count) * 31L
                       + (l.pending != null ? 1 : 0) * 7919L
                       + (int)h.order * 131L
                       + (h.target != null ? h.target.GetHashCode() : 0)
                       + Mathf.RoundToInt(SheetHost.BandHeight) * 1000003L;
            if (key == planKey) return;
            planKey = key;

            BuildOrderRows(l, h);
            orders.Lay(SheetHost.BandHeight);
            orderPages = orders.Pages;

            if (labels.Length != orderPages + 1) labels = new string[orderPages + 1];
            for (int i = 0; i < orderPages; i++)
                labels[i] = SheetKit.PageLabel("orders", i, orderPages);
            labels[orderPages] = "about";

            if (tab >= labels.Length) tab = labels.Length - 1;
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
            // raised, a seam worked out, a blueprint sited. `Plan` is keyed
            // on exactly that (plus the band), so pressing one of them does
            // not rebuild the list under the finger that is pressing it.
            long before = planKey;
            Plan();
            if (planKey == before && verbsKey == planKey) return;
            verbsKey = planKey;
            verbs.Clear();
            verbs.Add(orders.Build(Mathf.Clamp(tab, 0, orderPages - 1)));
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

        void BuildOrderRows(OutpostLedger l, OutpostHand h)
        {
            orders.Clear();

            // ASSIGN -- `Outpost.Positions()` (Outpost.cs:1716), pressed with
            // `Outpost.Assign` (CampCrewList.cs:177).
            orders.Add(SheetKit.EyebrowPx, () => SheetKit.Eyebrow("put to work"));
            var posts = outpost.Positions();
            if (posts.Count == 0)
                orders.Add(SheetKit.NotePx, () => SheetKit.Note("Nothing here to work at"));
            foreach (var id in posts)
            {
                string planId = id;
                orders.Add(SheetKit.QuietPx, () =>
                {
                    var hand = Hand;
                    var plan = BuildPlans.Named(planId);
                    bool already = hand != null && hand.order == OutpostOrder.Work
                                   && hand.target == planId;
                    var b = SheetKit.Btn($"{plan.position} at the {plan.label}", () =>
                    {
                        outpost.Assign(Hand, planId);
                        Dirty();
                    }, false, true);
                    b.SetEnabled(!already);
                    b.style.marginBottom = 4f;
                    return b;
                });
            }

            // GATHER -- `Outpost.Gatherable()` (Outpost.cs:1705), pressed with
            // `Outpost.OrderGather` (CampCrewList.cs:198). A worked-out stock
            // is still listed: an empty seam is information, and hiding it
            // would look like the menu was broken.
            orders.Add(SheetKit.EyebrowPx, () => SheetKit.Eyebrow("send out for"));
            foreach (var res in outpost.Gatherable())
            {
                string r = res;
                orders.Add(SheetKit.QuietPx, () =>
                {
                    var led = outpost.Ledger;
                    var hand = Hand;
                    var stock = led != null ? led.Stock(r) : null;
                    float standing = stock != null ? stock.standing : 0f;
                    string tail = standing < 1f
                        ? " — worked out"
                        : $" — {(led != null ? led.CountOf(r) : 0)}/{(led != null ? led.ceilingPer : 0)} kept";
                    bool already = hand != null && hand.order == OutpostOrder.Gather
                                   && hand.target == r;
                    var b = SheetKit.Btn(CampLoading.Lower(r) + tail, () =>
                    {
                        outpost.OrderGather(Hand, r);
                        Dirty();
                    }, false, true);
                    b.SetEnabled(!already);
                    b.style.marginBottom = 4f;
                    return b;
                });
            }

            // BUILD -- the drawing that is already up takes this hand
            // (`Outpost.OrderBuild`, Outpost.cs:1680); the list below sites a
            // new one, straight into `CampSiting` the way the crew list's
            // build verb does (CampCrewList.cs:236).
            orders.Add(SheetKit.EyebrowPx, () => SheetKit.Eyebrow("build"));
            if (outpost.Building)
            {
                orders.Add(SheetKit.QuietPx, () =>
                {
                    var led = outpost.Ledger;
                    var p = led != null ? led.pending : null;
                    if (p == null) return null;
                    var hand = Hand;
                    var plan = BuildPlans.Named(p.planId);
                    bool already = hand != null && hand.order == OutpostOrder.Build;
                    var b = SheetKit.Btn($"work on the {plan.label}", () =>
                    {
                        outpost.OrderBuild(Hand);
                        Dirty();
                    }, false, true);
                    b.SetEnabled(!already);
                    b.style.marginBottom = 4f;
                    return b;
                });
            }
            else
            {
                foreach (var plan in outpost.Buildable())
                {
                    var p = plan;
                    orders.Add(SheetKit.QuietPx, () =>
                    {
                        var b = SheetKit.Btn(p.stoneCost > 0
                            ? $"{p.label} — {p.cost} timber {p.stoneCost} stone"
                            : $"{p.label} — {p.cost} timber", () =>
                        {
                            CampSiting.Begin(outpost, p, SheetBits.ShipTransform);
                            Sheets.Close();
                        }, false, true);
                        b.style.marginBottom = 4f;
                        return b;
                    });
                }
            }
        }

        /// A press changed what the verbs say. Both keys go back so the next
        /// `Refresh` re-cuts the pages as well as re-drawing them -- an order
        /// taken can shorten the list, and a page plan that does not move
        /// with it is a strip pointing at a page that is no longer there.
        void Dirty()
        {
            planKey = long.MinValue;
            verbsKey = long.MinValue;
            Refresh();
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
