using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Who is working and who is not (top bar, 2026-09-29).** Kevin: *"a
    /// n/n showing working people compared to total people. pressing on it
    /// should show you who is unassigned, and pressing on the people will
    /// take you to where they currently are."*
    ///
    /// Headline "3 of 5 working" (`CampReadouts.Working` / `Total`). Then
    /// every hand NOT counted as working, in the section that says why:
    /// UNASSIGNED (the player's reserve, and anyone with no job or nothing
    /// to do on it), HELD UP (on a job but stalled -- no order, store full,
    /// walled off -- with the ledger's own reason), DOWN (with the timer and
    /// the cause), BUSY (pouting, rescuing, a raid). The working hands close
    /// the page, one compact row each with the job. A tap on anyone pans the
    /// camera to where he is now and opens his sheet
    /// (`CampReadouts.GoToHand`), where the orders are.
    public sealed class WorkersSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public WorkersSheet(Outpost camp) { outpost = camp; }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- frame --------------------------------------------------------------

        public string Title => "Workers";
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;
        public bool StillValid => outpost != null && outpost.Ledger != null;
        public Color Accent => MidnightLandHud.Ice;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public bool WantsTallSheet => true;

        Label subtitle;

        public VisualElement BuildHeader() =>
            CampPages.Header("Workers", out subtitle, () => CampPages.OpenLedger(PeopleSheet.OpenLedger, outpost));

        public VisualElement BuildActions() =>
            SheetKit.Actions(SheetKit.Btn("All people", () =>
            {
                if (outpost != null) Sheets.Open(new PeopleSheet(outpost));
            }));

        // --- body ----------------------------------------------------------------

        static readonly CampReadouts.HandKind[] Order =
        {
            CampReadouts.HandKind.Unassigned,
            CampReadouts.HandKind.HeldUp,
            CampReadouts.HandKind.Down,
            CampReadouts.HandKind.Busy,
            CampReadouts.HandKind.Working,
        };

        static string Heading(CampReadouts.HandKind k) => k switch
        {
            CampReadouts.HandKind.Unassigned => "UNASSIGNED",
            CampReadouts.HandKind.HeldUp => "HELD UP",
            CampReadouts.HandKind.Down => "DOWN",
            CampReadouts.HandKind.Busy => "BUSY",
            _ => "WORKING",
        };

        static string Tag(CampReadouts.HandKind k, OutpostHand h) => k switch
        {
            CampReadouts.HandKind.Unassigned => OutpostLedger.Reserve(h) ? "reserve" : "no job",
            CampReadouts.HandKind.HeldUp => "held up",
            CampReadouts.HandKind.Down => "down",
            CampReadouts.HandKind.Busy => "busy",
            _ => "",
        };

        static string Tone(CampReadouts.HandKind k) => k switch
        {
            CampReadouts.HandKind.Unassigned => "warm",
            CampReadouts.HandKind.HeldUp => "bad",
            CampReadouts.HandKind.Down => "bad",
            CampReadouts.HandKind.Busy => "warm",
            _ => "ok",
        };

        VisualElement rootEl;
        Label big, small, allGood;
        Label[] heads;
        ReadoutUi.People[] lists;
        readonly List<(OutpostHand h, string why)>[] buckets =
        {
            new List<(OutpostHand h, string why)>(), new List<(OutpostHand h, string why)>(),
            new List<(OutpostHand h, string why)>(), new List<(OutpostHand h, string why)>(),
            new List<(OutpostHand h, string why)>(),
        };

        public VisualElement Build()
        {
            rootEl = ReadoutUi.Root(out var col);
            col.Add(ReadoutUi.Headline(out big, out small));
            allGood = CampPages.Classed(new Label(), "cp-note");
            col.Add(allGood);

            heads = new Label[Order.Length];
            lists = new ReadoutUi.People[Order.Length];
            for (int s = 0; s < Order.Length; s++)
            {
                heads[s] = ReadoutUi.Eyebrow(col);
                lists[s] = new ReadoutUi.People(col, name => CampReadouts.GoToHand(outpost, name));
            }
            Refresh();
            return rootEl;
        }

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null) return;

            foreach (var b in buckets) b.Clear();
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                var k = CampReadouts.KindOf(l, h, out string why);
                buckets[System.Array.IndexOf(Order, k)].Add((h, why));
            }

            int total = CampReadouts.Total(l);
            int working = CampReadouts.Working(l);
            ReadoutUi.SetText(big, $"{working} of {total}");
            ReadoutUi.SetText(small, total == 0 ? "nobody lives here yet" : "working");
            int idle = buckets[0].Count;
            if (subtitle != null) ReadoutUi.SetText(subtitle, idle > 0 ? $"{idle} unassigned" : $"{total} hands");
            ReadoutUi.SetText(allGood, "Everyone is at work.");
            ReadoutUi.Show(allGood, total > 0 && working == total);

            for (int s = 0; s < Order.Length; s++)
            {
                var k = Order[s];
                var list = lists[s];
                var bucket = buckets[s];
                ReadoutUi.SetText(heads[s], $"{Heading(k)} · {bucket.Count}");
                ReadoutUi.Show(heads[s], bucket.Count > 0);
                list.Begin();
                foreach (var (h, why) in bucket)
                {
                    string sub = why;
                    // The place, for a hand with no job: where to find him.
                    if (k == CampReadouts.HandKind.Unassigned && h.Doing != "idle")
                        sub = why + " · " + h.Doing;
                    list.Add(h.name, Tag(k, h), Tone(k), sub,
                        k == CampReadouts.HandKind.HeldUp || k == CampReadouts.HandKind.Down);
                }
                list.End();
            }
        }
    }
}
