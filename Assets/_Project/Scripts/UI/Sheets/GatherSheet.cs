using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **One resource seam, its own page (menu-audit #3, 2026-09-27).**
    /// The ☰ Ledger's GATHER row used to open the HandSheet of whoever
    /// happened to be gathering first -- with three hands on Timber that
    /// picked one of them at random and told the player nothing about the
    /// stock. This is the card a tap on that row now opens: what is
    /// standing on the island, what is kept in the store, who is on it, and
    /// one tap to send an idle hand after it -- the same
    /// `Outpost.OrderGather` call the old gather order pressed.
    ///
    /// **It reads and it calls; it never decides.** Every number is the
    /// ledger's; the send button is `Outpost.OrderGather`, exactly what
    /// `HandSheet`'s gather tile presses.
    public class GatherSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly string res;

        public GatherSheet(Outpost outpost, string res)
        {
            this.outpost = outpost;
            this.res = res;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- frame -------------------------------------------------------------

        public string Title => res == Res.Game ? "Hunt" : StationPage.Cap(ResDefs.Label(res));
        public Color Accent => SheetTheme.Moss;
        public bool WantsTallSheet => false;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public bool StillValid => outpost != null && outpost.Ledger != null;

        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;

        StationPage.Header header;

        public VisualElement BuildHeader()
        {
            header = new StationPage.Header(Title, true, () => StationPage.OpenLedgerFor(outpost));
            return header.Root;
        }

        // --- pieces kept between refreshes --------------------------------------

        VisualElement root;
        VisualElement stockIcon;
        Label stockBig, stockSmall;
        VisualElement stockBar;
        VisualElement onHolder;
        Label onEmpty;
        Button sendBtn;
        Label noteLine;

        public VisualElement Build()
        {
            root = StationPage.Root("st-page");
            StationPage.FitToParent(root);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("st-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var col = new VisualElement();
            col.AddToClassList("st-content");
            scroll.Add(col);

            // --- the stock -------------------------------------------------
            var s = new VisualElement(); s.AddToClassList("st-section");
            s.style.marginTop = 0f;
            s.Add(StationPage.Text(res == Res.Game ? "IN THE STORE" : "STANDING · IN THE STORE", "st-eyebrow"));
            var stockCard = StationPage.Card();
            stockCard.style.flexDirection = FlexDirection.Row;
            stockCard.style.alignItems = Align.Center;
            stockIcon = StationPage.Icon(res, "st-tile-icon");
            stockCard.Add(stockIcon);
            var words = new VisualElement(); words.AddToClassList("st-worker-words");
            stockBig = StationPage.Text("", "st-worker-name");
            stockSmall = StationPage.Text("", "st-worker-sub");
            words.Add(stockBig); words.Add(stockSmall);
            stockCard.Add(words);
            s.Add(stockCard);
            stockBar = SheetKit.Bar(0f, SheetBits.Colour(res));
            stockBar.style.marginTop = 6f;
            s.Add(stockBar);
            noteLine = StationPage.Text("", "st-line");
            noteLine.style.marginTop = 6f;
            s.Add(noteLine);
            col.Add(s);

            // --- who's on it -------------------------------------------------
            var onSection = new VisualElement(); onSection.AddToClassList("st-section");
            onSection.Add(StationPage.Text("ON IT", "st-eyebrow"));
            onHolder = new VisualElement();
            onSection.Add(onHolder);
            onEmpty = StationPage.Text("nobody on it", "st-line");
            onSection.Add(onEmpty);
            col.Add(onSection);

            // --- send an idle hand, pinned at the bottom --------------------
            sendBtn = new Button(SendIdle) { text = "Send an idle villager" };
            sendBtn.AddToClassList("st-btn");
            sendBtn.style.minHeight = StationSheet.TouchPx;
            root.Add(SheetKit.Actions(sendBtn));

            Refresh();
            return root;
        }

        // --- refresh -------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null || root == null) return;
            outpost.CatchUp();

            var stock = l.Stock(res);
            float standing = stock != null ? stock.standing : 0f;
            bool huntBlocked = res == Res.Game && l.HunterBlocker() != null;
            bool workedOut = res != Res.Game && standing < 1f;
            bool huntedOut = res == Res.Game && !huntBlocked && stock != null && stock.standing < 1f;

            int kept = l.StoreCountOf(res);
            stockBig.text = kept.ToString();
            stockSmall.text = res == Res.Game ? "food in the store" : $"of {l.ceilingPer} kept";
            SheetKit.SetBar(stockBar, l.Fill01(res), SheetBits.Colour(res));

            if (huntBlocked)
                noteLine.text = "Hunting needs a spear (the forge: a board and a stone) or a bow with arrows (the hunting lodge).";
            else if (workedOut)
                noteLine.text = $"The {ResDefs.Label(res)} here is worked out — nothing left to gather.";
            else if (huntedOut)
                noteLine.text = "No game left here right now.";
            else if (res != Res.Game)
                noteLine.text = $"{standing:0.#} standing on the island";
            else
                noteLine.text = "";
            noteLine.style.display = string.IsNullOrEmpty(noteLine.text) ? DisplayStyle.None : DisplayStyle.Flex;

            FillOn(l);

            bool locked = huntBlocked || workedOut || huntedOut;
            var idle = SheetBits.FirstIdle(l);
            sendBtn.SetEnabled(!locked && idle != null);
            sendBtn.text = idle == null ? "Nobody idle to send" : locked ? "Blocked" : "Send an idle villager";
        }

        void FillOn(OutpostLedger l)
        {
            onHolder.Clear();
            int n = 0;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Gather || h.target != res) continue;
                n++;
                var who = h;
                string sub = l.StallReason(who) ?? who.Doing;
                onHolder.Add(SheetKit.ListRow(
                    SheetKit.Token(SheetBits.Initial(who.name), who.Angry, SheetBits.JobGlyph(who), () => OpenHand(who.name)),
                    SheetKit.Text(who.name, true),
                    SheetKit.Text(sub, false, true, 12f)));
            }
            onEmpty.style.display = n == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OpenHand(string who)
        {
            if (outpost == null || string.IsNullOrEmpty(who)) return;
            Sheets.Open(new HandSheet(outpost, who));
        }

        void SendIdle()
        {
            var l = L;
            if (outpost == null || l == null) return;
            var idle = SheetBits.FirstIdle(l);
            if (idle == null) return;
            outpost.OrderGather(idle, res);
            Refresh();
        }
    }
}
