using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The manifest: what she is carrying, what is going aboard, and who
    /// is sailing.**
    ///
    /// The fourth step of the loop (GDD §6) had one button until 2026-09-20
    /// and three by 2026-09-22, spread across `CampSheet`'s bar, its store
    /// rows and the anchor prompt's deck-cargo toggle. They are all about the
    /// same decision -- how much you dare take -- so they are one sheet,
    /// unfolded beside the hull.
    ///
    /// **Nothing here moves a unit.** `CampLoading` carries, `VoyageManager`
    /// counts, `AnchorController` casts off; this reads them and presses
    /// their buttons.
    public class ShipSheet : ISheet
    {
        public string Title => "Manifest";

        AnchorController Anchor => SheetBits.Anchor;
        VoyageManager Voyage => SheetBits.Voyage;
        ShipHold Hold => SheetBits.Hold;
        Outpost Camp => SheetBits.CampAlongside();

        public Vector3 AnchorWorld
        {
            get
            {
                var a = Anchor;
                return a != null ? a.transform.position : Vector3.zero;
            }
        }

        /// She has to still be lying somewhere. Underway there is no manifest
        /// to argue with -- the cargo is decided and you live with it.
        public bool StillValid
        {
            get
            {
                var a = Anchor;
                return a != null
                    && (a.CurrentState == AnchorController.State.Anchored
                        || a.CurrentState == AnchorController.State.Ashore);
            }
        }

        // --- the pieces kept between refreshes ---------------------------------

        Label eyebrowLine;
        Label holdBig;
        VisualElement holdBar;
        Button loadBtn;
        VisualElement stopAtHolder;
        VisualElement deckHolder;
        VisualElement repairHolder;
        VisualElement aboardCol;
        VisualElement ashoreCol;
        Button everyoneAshore;

        long holdKey = long.MinValue;
        long stopKey = long.MinValue;
        int deckKey = -99;
        int repairKey = -99;
        long crewKey = long.MinValue;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            var a = Anchor;
            root.Add(SheetKit.Header(
                a != null && a.CurrentDock != null ? "at the pier" : "at anchor",
                Title, SheetTheme.Sea, "⚓", () => Sheets.Close()));

            eyebrowLine = SheetKit.Text("", false, true, 12f);
            root.Add(eyebrowLine);

            holdBig = SheetKit.Text("0 / 0", true, false, 28f);
            holdBar = SheetBits.Holder();
            root.Add(SheetKit.Row(holdBig, holdBar));

            loadBtn = SheetKit.Btn("Load", LoadPressed, true);
            root.Add(loadBtn);

            root.Add(SheetKit.Rule());
            root.Add(SheetKit.Eyebrow("stop at"));
            stopAtHolder = SheetBits.Holder();
            root.Add(stopAtHolder);

            root.Add(SheetKit.Rule());
            deckHolder = SheetBits.Holder();
            root.Add(deckHolder);
            repairHolder = SheetBits.Holder();
            root.Add(repairHolder);

            root.Add(SheetKit.Rule());
            aboardCol = SheetBits.Holder();
            ashoreCol = SheetBits.Holder();
            root.Add(SheetKit.Row(
                SheetKit.Col(SheetKit.Eyebrow("aboard"), aboardCol),
                SheetKit.Col(SheetKit.Eyebrow("ashore"), ashoreCol)));

            root.Add(SheetKit.Rule());
            everyoneAshore = SheetKit.Btn("Everyone ashore", EveryoneAshore);
            root.Add(SheetKit.Row(
                everyoneAshore,
                SheetKit.Btn("Cast off", CastOff, true)));

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var v = Voyage;
            var camp = Camp;
            if (camp != null) camp.CatchUp();

            HoldBlock(v, camp);
            StopAtBlock(camp);
            DeckBlock(v);
            RepairBlock();
            CrewBlock(camp);
        }

        // --- the hold ------------------------------------------------------------

        void HoldBlock(VoyageManager v, Outpost camp)
        {
            if (v == null) return;
            int room = CampLoading.RoomAboard(v);
            int limit = v.TakeDeckCargo ? v.MaxHold : v.HoldCapacity;

            long key = ((long)v.TotalHeld * 31 + limit) * 31 + room
                       + (CampLoading.Busy ? 1 : 0) * 7919L
                       + (camp != null && camp.Ledger != null ? camp.Ledger.Total : 0) * 131L;
            if (key == holdKey) return;
            holdKey = key;

            if (holdBig != null) holdBig.text = $"{v.TotalHeld} / {limit}";
            SheetBits.Swap(holdBar, SheetKit.Bar(
                limit > 0 ? Mathf.Clamp01((float)v.TotalHeld / limit) : 0f,
                v.Overloaded ? SheetTheme.Ember : SheetTheme.Sea, 10f));

            if (eyebrowLine != null)
                eyebrowLine.text = CampLoading.Busy
                    ? $"loading — {CampLoading.Moved} aboard, room for {room}"
                    : room > 0
                        ? $"{v.TotalHeld} aboard, room for {room} · best first"
                        : v.TakeDeckCargo
                            ? "she is stuffed — nothing more will fit aboard"
                            : "the hold is at her line — deck cargo takes more";

            if (loadBtn == null) return;
            if (CampLoading.Busy)
            {
                loadBtn.text = "Stop";
                loadBtn.SetEnabled(true);
                return;
            }
            string kind = NextKind(camp, room, out int n);
            loadBtn.text = kind == null
                ? "Nothing to load"
                : $"Load {n} {CampLoading.Lower(kind)}";
            loadBtn.SetEnabled(kind != null && room > 0
                && camp != null && CampLoading.Alongside(camp));
        }

        /// **What the first trip would actually carry**, so the button can
        /// say it. `CampLoading.BestFirst` is the order and `RoomAboard` is
        /// the limit -- the same two facts the coroutine works from, read the
        /// same way, so the label cannot promise what the press will not do.
        static string NextKind(Outpost camp, int room, out int n)
        {
            n = 0;
            var l = camp != null ? camp.Ledger : null;
            if (l == null || room <= 0) return null;
            foreach (var res in CampLoading.BestFirst)
            {
                int have = l.CountOf(res);
                if (have <= 0) continue;
                int cap = CampLoading.StopAt(res);
                // A "stop at" is a floor on what stays ashore, so what the
                // hold may take is everything above it.
                int free = cap < 0 ? have : Mathf.Max(0, have - cap);
                if (free <= 0) continue;
                n = Mathf.Min(free, room);
                return res;
            }
            return null;
        }

        /// The lot, best first -- `CampLoading.Begin` (CampSheet.cs:335). The
        /// stop-at caps below are what keep it from emptying the island.
        void LoadPressed()
        {
            if (CampLoading.Busy) { CampLoading.Cancel(); Refresh(); return; }
            var camp = Camp;
            if (camp == null) return;
            CampLoading.Begin(camp, Voyage, Hold);
            holdKey = long.MinValue;
            Refresh();
        }

        // --- what stays ashore -----------------------------------------------------

        static readonly string[] StopOptions = { "all", "half", "none" };
        readonly List<string> kinds = new List<string>();

        /// **"all / half / none" is about what SAILS, not what stays.** Kevin's
        /// words for the three; `CampLoading.SetStopAt` takes the floor the
        /// carry must leave behind, so "all" is no floor at all (-1), "half"
        /// leaves half of what is piled now, and "none" leaves the lot.
        void StopAtBlock(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            kinds.Clear();
            if (l != null)
                foreach (var s in l.stores)
                    if (s != null && s.whole > 0 && !string.IsNullOrEmpty(s.resource))
                        kinds.Add(s.resource);

            long key = kinds.Count;
            foreach (var k in kinds)
                key = key * 31 + k.GetHashCode() + l.CountOf(k) * 7 + CampLoading.StopAt(k) * 131;
            if (key == stopKey) return;
            stopKey = key;

            stopAtHolder.Clear();
            if (kinds.Count == 0)
            {
                stopAtHolder.Add(SheetKit.Note("The camp is holding nothing"));
                return;
            }
            foreach (var kind in kinds)
            {
                string res = kind;
                int ashore = l.CountOf(res);
                int cap = CampLoading.StopAt(res);
                int sel = cap < 0 ? 0 : cap <= 0 ? 2 : 1;
                stopAtHolder.Add(SheetKit.Row(
                    SheetKit.Text(CampLoading.Lower(res), false, false, 13f),
                    SheetKit.Text(ashore.ToString(), false, true, 13f),
                    SheetKit.Segmented(StopOptions, sel, i =>
                    {
                        int keep = i == 0 ? -1 : i == 1 ? ashore / 2 : 0;
                        CampLoading.SetStopAt(res, keep);
                        stopKey = long.MinValue;
                        holdKey = long.MinValue;
                        Refresh();
                    })));
            }
        }

        // --- deck cargo and the hull -------------------------------------------------

        /// The same flag and the same warning the anchor prompt toggles
        /// (AnchorController.cs:826, text at :823, warning at :830).
        void DeckBlock(VoyageManager v)
        {
            if (v == null) return;
            int key = (v.TakeDeckCargo ? 1 : 0) * 100003 + v.MaxHold * 31 + v.HoldCapacity;
            if (key == deckKey) return;
            deckKey = key;

            var btn = SheetKit.Btn(v.TakeDeckCargo
                ? $"◉  deck cargo — to {v.MaxHold}"
                : $"◎  deck cargo — stop at {v.HoldCapacity}", () =>
            {
                v.TakeDeckCargo = !v.TakeDeckCargo;
                deckKey = -99;
                holdKey = long.MinValue;
                Refresh();
            }, false, !v.TakeDeckCargo);

            deckHolder.Clear();
            deckHolder.Add(btn);
            if (v.TakeDeckCargo)
                deckHolder.Add(SheetKit.Note("she'll swim low and take water"));
        }

        /// **The hull: a button only when there is something to press.**
        ///
        /// It drew a button in every state and greyed the ones that could not
        /// be pressed, so a sound hull at 99 % and an empty hold both read as
        /// "repair hull — uses timber" in ghost-grey: the player cannot tell
        /// a thing that is FINE from a thing that is BROKEN and unaffordable.
        /// A disabled control now means one thing only -- "you could do this,
        /// but not this second" -- and everything else is a sentence.
        void RepairBlock()
        {
            var a = Anchor;
            var hull = a != null ? a.GetComponent<HullIntegrity>() : null;
            if (hull == null) { repairHolder.Clear(); repairKey = -99; return; }

            var v = Voyage;
            bool on = SheetBits.Repairing(a);
            int pct = Mathf.RoundToInt(hull.Integrity01 * 100f);
            int timber = v != null ? v.AmountOf(Res.Timber) : 0;
            int key = ((pct * 4 + (on ? 2 : 0) + (hull.NeedsRepair ? 1 : 0)) * 3)
                      + (timber > 0 ? 1 : 0);
            if (key == repairKey) return;
            repairKey = key;

            repairHolder.Clear();

            // Nothing to mend. `NeedsRepair` is `integrity < 0.995`, so this
            // is "hull sound" at 100 % and at the 99 % that is a scrape.
            if (!on && (!hull.NeedsRepair || pct >= 99))
            {
                repairHolder.Add(SheetKit.Text($"hull sound — {pct}%", false, true, 12f));
                return;
            }
            // Something to mend and nothing to mend it with: a fact, not a
            // dead button, and it names what she is short of.
            if (!on && timber <= 0)
            {
                repairHolder.Add(SheetKit.Text(
                    $"hull {pct}% — no timber aboard to mend her", false, true, 12f));
                return;
            }
            // Live, both ways round -- the same labels the anchor prompt
            // prints (AnchorController.cs:984).
            repairHolder.Add(SheetKit.Btn(on
                ? $"stop repairs — hull {pct}%"
                : $"repair hull ({pct}%) — uses timber", () =>
            {
                SheetBits.ToggleRepair(Anchor);
                repairKey = -99;
                Refresh();
            }, false, !on));
        }

        // --- who sails ----------------------------------------------------------------

        void CrewBlock(Outpost camp)
        {
            var roster = SheetBits.Roster;
            var l = camp != null ? camp.Ledger : null;

            long key = (l != null ? l.hands.Count : 0) * 1000003L;
            if (roster != null)
                foreach (var a in roster.All)
                    if (a != null && a.IsAboard)
                        key = key * 31 + a.DisplayName.GetHashCode();
            if (l != null)
                foreach (var h in l.hands)
                    if (h != null) key = key * 31 + (h.name != null ? h.name.GetHashCode() : 0);
            if (key == crewKey) return;
            crewKey = key;

            // Aboard: the arrow leaves them here (`Outpost.Station`,
            // CampSheet.cs:493).
            aboardCol.Clear();
            int aboard = 0;
            if (roster != null)
                foreach (var a in roster.All)
                {
                    if (a == null || !a.IsAboard) continue;
                    var hand = a;
                    aboard++;
                    var arrow = SheetKit.Btn("→", () => Station(hand), false, true);
                    arrow.SetEnabled(camp != null && (camp.HasCamp || camp.Building));
                    aboardCol.Add(SheetKit.Row(
                        SheetKit.Token(SheetBits.Initial(hand.DisplayName), false, "⚓"),
                        SheetKit.Text(hand.DisplayName, false, false, 13f),
                        arrow));
                }
            if (aboard == 0) aboardCol.Add(SheetKit.Text("nobody aboard", false, true, 12f));

            // Ashore: the arrow takes them back (`Outpost.Recall`,
            // CampSheet.cs:519). **By name, never by enumerator** -- `Recall`
            // takes the row out of `l.hands`, and the list this loop walks is
            // that list.
            ashoreCol.Clear();
            if (l == null || l.hands.Count == 0)
                ashoreCol.Add(SheetKit.Text("nobody ashore", false, true, 12f));
            else
                for (int k = 0; k < l.hands.Count; k++)
                {
                    var h = l.hands[k];
                    if (h == null) continue;
                    string who = h.name;
                    var arrow = SheetKit.Btn("←", () => Recall(who), false, true);
                    arrow.SetEnabled(camp != null && camp.BodyNamed(who) != null);
                    ashoreCol.Add(SheetKit.Row(
                        SheetKit.Token(SheetBits.Initial(who), h.Angry,
                            SheetBits.JobGlyph(h)),
                        SheetKit.Text(who, false, false, 13f),
                        arrow));
                }

            if (everyoneAshore != null)
                everyoneAshore.SetEnabled(aboard > 0 && camp != null
                    && (camp.HasCamp || camp.Building));
        }

        void Station(CrewAgent hand)
        {
            var camp = Camp;
            if (camp == null || hand == null) return;
            if (camp.Station(hand))
            {
                var roster = SheetBits.Roster;
                if (roster != null) roster.Refresh();
            }
            crewKey = long.MinValue;
            Refresh();
        }

        void Recall(string who)
        {
            var camp = Camp;
            var anchor = Anchor;
            if (camp == null || anchor == null) return;
            var body = camp.BodyNamed(who);
            if (body != null && camp.Recall(body, anchor.transform))
            {
                var roster = SheetBits.Roster;
                if (roster != null) roster.Refresh();
            }
            crewKey = long.MinValue;
            Refresh();
        }

        /// Station everybody who is aboard, one `Outpost.Station` at a time --
        /// the same call, pressed for each of them, so a hand it refuses (no
        /// camp yet, already on the books) is refused for the same reason it
        /// would be on its own.
        void EveryoneAshore()
        {
            var camp = Camp;
            var roster = SheetBits.Roster;
            if (camp == null || roster == null) return;
            var all = roster.All;
            bool any = false;
            foreach (var a in all)
            {
                if (a == null || !a.IsAboard) continue;
                if (camp.Station(a)) any = true;
            }
            if (any) roster.Refresh();
            crewKey = long.MinValue;
            Refresh();
        }

        /// `AnchorController.CastOff` (AnchorController.cs:762) -- the public
        /// door onto `WeighAnchor`, which is what the space bar and the
        /// "⚓ Cast off (space)" prompt press (AnchorController.cs:947).
        void CastOff()
        {
            var a = Anchor;
            if (a == null) return;
            a.CastOff();
            Sheets.Close();
        }
    }
}
