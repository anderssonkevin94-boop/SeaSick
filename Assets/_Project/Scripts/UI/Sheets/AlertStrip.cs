using System;
using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The alert strip, 2026-09-27 (Melvor redesign).** Just under the
    /// land resource bar -- a raid, a stalled hand, an empty food pile,
    /// nobody on watch -- opening the sheet where it is fixed. Facts come
    /// from `CampAlerts`. Ashore only; at sea the IMGUI raid banner keeps
    /// the job.
    ///
    /// **One chip and a count since 2026-09-30** (island UI phase 2): the
    /// most urgent alert, then "+N" for the rest, which opens the Camp
    /// sheet where they all live (NEEDS YOU, each with its fix button). An alert the Next card above the thumb
    /// bar is already showing (`NextCard.AlertText`) is left out here, so
    /// nothing is said twice. Chips are built once and re-texted on the
    /// 0.25 s tick (DEV-TOOLS: a rebuilt element loses the tap it was in
    /// the middle of).
    ///
    /// **"N problems" since 2026-10-03 (villager review group 4).** The
    /// "+N" chip became the camp's Problems chip: with two or more alerts
    /// it reads "3 problems" (the count is ALL of them, the one beside it
    /// included) and opens `ProblemsSheet` -- a short list that hugs its
    /// rows, each tap going straight to the station or hand concerned --
    /// instead of the tall Camp sheet. Hidden when nothing is stuck. The
    /// strip's real height is measured (`ShownHeight`): a wrapped chip or
    /// a chip pushed to a second line still keeps world taps off it.
    public sealed class AlertStrip
    {
        public const int MaxChips = 1;
        public const float Height = 36f;

        /// True while a chip -- or the Next card -- is showing this camp's
        /// raid, so `RaidBanner` does not say it a second time.
        public static bool ShowsRaid { get; private set; }
        /// True while at least one chip is up (the resource rect grows to
        /// cover the strip only then).
        public static bool Showing { get; private set; }

        /// **The strip's laid-out height, panel units (2026-10-03)**:
        /// `Height` until the row has been laid out, then what it really
        /// takes -- a wrapped alert chip, or the Problems chip wrapped onto
        /// a second line, is taller than one row.
        public static float ShownHeight { get; private set; } = Height;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { ShowsRaid = Showing = false; ShownHeight = Height; }

        readonly VisualElement row;
        readonly Button[] chips = new Button[MaxChips];
        readonly Button more;
        // The drawn pills inside the 44 pt hit buttons (Ledger.uss, `.ledger-chip`).
        readonly Label[] chipBodies = new Label[MaxChips];
        readonly Label moreBody;
        readonly Func<ISheet>[] go = new Func<ISheet>[MaxChips];
        readonly string[] shown = new string[MaxChips];
        readonly CampAlerts.Tone[] tones = new CampAlerts.Tone[MaxChips];
        readonly List<CampAlerts.Alert> alerts = new List<CampAlerts.Alert>(8);
        int moreShown = -1;

        public int LastCount { get; private set; }

        public AlertStrip(VisualElement root)
        {
            // The chips' rules live in Ledger.uss (the ledger drawer that used
            // to add it to the root is gone since phase 4).
            var style = Resources.Load<StyleSheet>("UI/Ledger");
            if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);
            row = new VisualElement();
            row.AddToClassList("ledger-alerts");
            row.pickingMode = PickingMode.Ignore;
            root.Add(row);
            for (int i = 0; i < MaxChips; i++)
            {
                int index = i;
                var chip = new Button(() => Tap(index));
                chip.AddToClassList("ledger-chip");
                chipBodies[i] = Body(chip);
                chip.style.display = DisplayStyle.None;
                row.Add(chip);
                chips[i] = chip;
            }
            // "N problems": every alert, in the Problems list.
            more = new Button(OpenProblems);
            more.AddToClassList("ledger-chip");
            more.AddToClassList("ledger-chip--more");
            moreBody = Body(more);
            more.tooltip = "Everything that is stuck: open Problems";
            more.style.display = DisplayStyle.None;
            row.Add(more);
        }

        /// The chip's drawn pill. The button is the 44 pt touch target and
        /// carries no text of its own (Ledger.uss, `.ledger-chip`).
        static Label Body(Button chip)
        {
            var body = new Label();
            body.AddToClassList("ledger-chip-body");
            body.pickingMode = PickingMode.Ignore;
            chip.Add(body);
            return body;
        }

        /// "N problems": the Problems list, every alert with its fix.
        static void OpenProblems()
        {
            var camp = MidnightLandHud.Camp;
            if (camp != null && camp.Ledger != null) Sheets.Open(new ProblemsSheet(camp));
        }

        void Tap(int index)
        {
            var make = go[index];
            var sheet = make != null ? make() : null;
            if (sheet != null) Sheets.Open(sheet);
        }

        public void Hide()
        {
            row.style.display = DisplayStyle.None;
            ShowsRaid = Showing = false;
            ShownHeight = Height;
        }

        /// Position every frame (cheap), content on `refresh`.
        public void Place(float left, float right, float top)
        {
            row.style.display = DisplayStyle.Flex;
            row.style.left = left;
            row.style.right = right;
            row.style.top = top;
            float h = row.resolvedStyle.height;
            ShownHeight = float.IsNaN(h) || h < Height ? Height : h;
        }

        public void Refresh(Outpost camp)
        {
            CampAlerts.Collect(camp, alerts);
            LastCount = alerts.Count;
            // The Next card's alert is said there; drop it from the strip.
            string onCard = NextCard.Visible ? NextCard.AlertText : null;
            if (onCard != null)
                for (int i = 0; i < alerts.Count; i++)
                    if (alerts[i].text == onCard) { alerts.RemoveAt(i); break; }
            bool raid = NextCard.ShowsRaid;
            int n = Mathf.Min(MaxChips, alerts.Count);
            for (int i = 0; i < MaxChips; i++)
            {
                var chip = chips[i];
                if (i >= n)
                {
                    go[i] = null;
                    if (chip.style.display != DisplayStyle.None) chip.style.display = DisplayStyle.None;
                    continue;
                }
                var a = alerts[i];
                go[i] = a.open;
                if (a.tone == CampAlerts.Tone.Raid) raid = true;
                if (shown[i] != a.text) { shown[i] = a.text; chipBodies[i].text = a.text; }
                if (tones[i] != a.tone || chip.style.display == DisplayStyle.None)
                {
                    tones[i] = a.tone;
                    chip.EnableInClassList("ledger-chip--raid", a.tone == CampAlerts.Tone.Raid);
                    chip.EnableInClassList("ledger-chip--bad", a.tone == CampAlerts.Tone.Bad);
                    chip.EnableInClassList("ledger-chip--warn", a.tone == CampAlerts.Tone.Warn);
                }
                chip.style.display = DisplayStyle.Flex;
            }
            // The Problems chip counts every alert (the Next card's and the
            // chip beside it too: the list shows them all), from two up --
            // one alone is the chip itself, one tap from its fix.
            int total = LastCount >= 2 ? LastCount : 0;
            if (total != moreShown)
            {
                moreShown = total;
                if (total > 0) moreBody.text = total + " problems";
                more.style.display = total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ShowsRaid = raid;
            Showing = n > 0 || total > 0;
        }
    }
}
