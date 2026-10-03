using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// <summary>
    /// **The store hut's "Runners" card (2026-10-02).** Runners are hands
    /// posted at the Storage building who push wheelbarrows: they move goods
    /// to workshops and building sites so nobody else has to. The ledger
    /// owns the job (`OutpostLedger.IsRunner`, `RunnerCount`,
    /// `RunnerSlots`); this is the slot UI, shaped like the station
    /// `WorkerCard` but with one row per slot of THIS copy
    /// (`StationCapacity`: 2 at level 1, 4 at level 2).
    ///
    /// **The Storehouse uses it too (2026-10-03, Kevin: runner
    /// progression):** 2 / 4 / 6 slots at levels 1 / 2 / 3, and a perk line
    /// under the blurb says what the island's best Storehouse gives EVERY
    /// runner ("Every runner here: barrow +1 armful · jog 10% faster"); on a
    /// store hut the line shows only when a Storehouse stands.
    ///
    /// A filled row is the person (tap: their sheet, the same one a tap in
    /// the world opens), their status word ("Runner, waiting", "Running 6
    /// boards to Sawmill"), **Swap** (the best free hand takes the slot, the
    /// runner steps off) and **Free** (stand him down). An open row is
    /// **Assign** (the best free hand). The rows are a pool built once and
    /// re-texted on the 0.25 s refresh -- nothing a finger can land on is
    /// rebuilt on that timer -- and each button reads who is in its slot at
    /// the moment it is pressed.
    /// </summary>
    internal sealed class RunnerCard
    {
        public readonly VisualElement Root;

        /// Six is the level 3 Storehouse's capacity (four the level 2 store
        /// hut's); a row past the copy's capacity is simply hidden.
        const int MaxSlots = 6;

        readonly Outpost outpost;
        readonly string planId;
        readonly System.Func<int> ordinalOf;
        readonly System.Action changed;
        readonly Label countLabel, note, perks;
        readonly Row[] rows = new Row[MaxSlots];
        readonly System.Collections.Generic.List<OutpostHand> here = new System.Collections.Generic.List<OutpostHand>();
        string refusal;

        sealed class Row
        {
            public VisualElement root;
            public Button person, main, off;
            public Label initial, name, sub;
            public OutpostHand hand;
        }

        public RunnerCard(Outpost o, string planId, System.Func<int> ordinalOf, System.Action changed)
        {
            outpost = o;
            this.planId = planId;
            this.ordinalOf = ordinalOf;
            this.changed = changed;

            Root = StationPage.Card();
            Root.style.flexDirection = FlexDirection.Column;
            Root.style.alignItems = Align.Stretch;

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.justifyContent = Justify.SpaceBetween;
            var title = StationPage.Text("Runners", "st-worker-name");
            countLabel = StationPage.Text("", "st-worker-name");
            countLabel.style.color = StationPage.Amber;
            head.Add(title);
            head.Add(countLabel);
            Root.Add(head);

            var blurb = StationPage.Text(
                "Runners push wheelbarrows: they move goods to workshops and building sites so nobody else has to.",
                "st-worker-sub");
            blurb.style.whiteSpace = WhiteSpace.Normal;
            blurb.style.marginBottom = 6f;
            Root.Add(blurb);

            // The Storehouse's camp-wide runner perk (2026-10-03): wraps,
            // never cut (phone portrait).
            perks = StationPage.Text("", "st-worker-sub");
            perks.style.whiteSpace = WhiteSpace.Normal;
            perks.style.color = StationPage.Amber;
            perks.style.marginBottom = 6f;
            perks.style.display = DisplayStyle.None;
            Root.Add(perks);

            note = StationPage.Text("", "st-worker-sub");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.color = SheetTheme.Ember;
            note.style.display = DisplayStyle.None;
            Root.Add(note);

            for (int i = 0; i < MaxSlots; i++)
            {
                var r = new Row();
                r.root = new VisualElement();
                r.root.style.flexDirection = FlexDirection.Row;
                r.root.style.alignItems = Align.Center;
                r.root.style.marginTop = 6f;
                int slot = i;

                r.person = new Button(() => OpenPerson(slot)) { text = "" };
                r.person.AddToClassList("st-worker-person");
                var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
                avatar.AddToClassList("st-avatar");
                r.initial = StationPage.Text("?", "st-avatar-text");
                avatar.Add(r.initial);
                r.person.Add(avatar);
                var words = new VisualElement { pickingMode = PickingMode.Ignore };
                words.AddToClassList("st-worker-words");
                r.name = StationPage.Text("", "st-worker-name");
                r.sub = StationPage.Text("", "st-worker-sub");
                words.Add(r.name);
                words.Add(r.sub);
                r.person.Add(words);
                r.root.Add(r.person);

                r.main = new Button(() => Main(slot)) { text = "Assign" };
                r.main.AddToClassList("st-btn");
                Tighten(r.main);
                r.root.Add(r.main);
                r.off = new Button(() => Off(slot)) { text = "Free" };
                r.off.AddToClassList("st-btn");
                Tighten(r.off);
                r.root.Add(r.off);

                rows[i] = r;
                Root.Add(r.root);
            }
        }

        /// Two buttons share the row with the avatar and the words in a
        /// 430-unit portrait panel: narrower padding and a 16 px label keep
        /// the 56-unit height (the thumb target) and give the words room.
        static void Tighten(Button b)
        {
            b.style.paddingLeft = 12f;
            b.style.paddingRight = 12f;
            b.style.marginLeft = 6f;
            b.style.fontSize = 16f;
            b.style.minWidth = 64f;
        }

        public void Update(OutpostLedger l)
        {
            if (l == null) return;
            int ordinal = ordinalOf != null ? ordinalOf() : -1;
            int cap = ordinal >= 0 ? Mathf.Clamp(l.StationCapacity(planId, ordinal), 0, MaxSlots) : 0;

            here.Clear();
            if (ordinal >= 0)
                foreach (var h in l.hands)
                    if (OutpostLedger.IsRunner(h) && l.OrdinalOfHand(h) == ordinal) here.Add(h);

            countLabel.text = $"{l.RunnerCount()}/{l.RunnerSlots()}";

            int shLevel = l.StorehouseLevel();
            string perk = OutpostLedger.PerkWords(shLevel);
            string pt = string.IsNullOrEmpty(perk) ? ""
                : planId == BuildPlans.Storehouse.id ? "Every runner on this island: " + perk
                : "Storehouse perk, every runner: " + perk;
            if (perks.text != pt) perks.text = pt;
            perks.style.display = pt.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            bool free = l.FreeHandFor(planId) != null;
            bool freeSlot = here.Count < cap;

            for (int i = 0; i < MaxSlots; i++)
            {
                var r = rows[i];
                bool show = i < cap;
                r.root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) { r.hand = null; continue; }
                var h = i < here.Count ? here[i] : null;
                r.hand = h;
                if (h != null)
                {
                    string n = string.IsNullOrEmpty(h.name) ? "?" : h.name;
                    r.initial.text = n.Substring(0, 1).ToUpperInvariant();
                    r.name.text = n;
                    string word = l.StatusWord(h);
                    r.sub.text = string.IsNullOrEmpty(word) ? "Runner" : word;
                    r.person.SetEnabled(true);
                    // Replace: only while another free hand exists.
                    r.main.text = "Swap";
                    r.main.style.display = free ? DisplayStyle.Flex : DisplayStyle.None;
                    r.main.SetEnabled(true);
                    r.off.style.display = DisplayStyle.Flex;
                }
                else
                {
                    r.initial.text = "?";
                    r.name.text = "Open slot";
                    r.sub.text = free ? "needs a runner" : "no free hand to assign";
                    r.person.SetEnabled(false);
                    r.main.text = "Assign";
                    r.main.style.display = DisplayStyle.Flex;
                    r.main.SetEnabled(free);
                    r.off.style.display = DisplayStyle.None;
                }
            }

            bool hasNote = !string.IsNullOrEmpty(refusal) && freeSlot;
            note.text = hasNote ? refusal : "";
            note.style.display = hasNote ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OpenPerson(int slot)
        {
            var h = rows[slot].hand;
            if (outpost == null || h == null || string.IsNullOrEmpty(h.name)) return;
            Sheets.Open(new HandSheet(outpost, h.name));
        }

        void Main(int slot)
        {
            var l = outpost != null ? outpost.Ledger : null;
            int ordinal = ordinalOf != null ? ordinalOf() : -1;
            if (l == null || ordinal < 0) return;
            var previous = rows[slot].hand;
            var free = l.FreeHandFor(planId);
            if (free == null) return;
            // The runner steps off first so his slot is free; a refused
            // assignment puts him straight back, so it changes nobody.
            if (previous != null) outpost.OrderIdle(previous, reserve: false);
            bool ok = outpost.Assign(free, planId, ordinal);
            if (!ok && previous != null) outpost.Assign(previous, planId, ordinal);
            refusal = ok ? null : (outpost.AssignRefusal ?? "That order didn't take.");
            changed?.Invoke();
        }

        void Off(int slot)
        {
            var h = rows[slot].hand;
            if (outpost == null || h == null) return;
            outpost.OrderIdle(h, reserve: false);
            refusal = null;
            changed?.Invoke();
        }
    }
}
