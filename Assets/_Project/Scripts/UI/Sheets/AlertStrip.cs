using System;
using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The alert strip, 2026-09-27 (Melvor redesign).** Up to three chips
    /// just under the land resource bar -- a raid, a stalled hand, an empty
    /// food pile, nobody on watch -- each opening the sheet where it is
    /// fixed. Facts come from `CampAlerts`. The three chips are built once
    /// and re-texted on the 0.25 s tick (DEV-TOOLS: a rebuilt element loses
    /// the tap it was in the middle of). Ashore only; at sea the IMGUI raid
    /// banner keeps the job.
    public sealed class AlertStrip
    {
        public const int MaxChips = 3;
        public const float Height = 36f;

        /// True while a chip is showing this camp's raid, so `RaidBanner`
        /// does not say it a second time.
        public static bool ShowsRaid { get; private set; }
        /// True while at least one chip is up (the resource rect grows to
        /// cover the strip only then).
        public static bool Showing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { ShowsRaid = Showing = false; }

        readonly VisualElement row;
        readonly Button[] chips = new Button[MaxChips];
        readonly Func<ISheet>[] go = new Func<ISheet>[MaxChips];
        readonly string[] shown = new string[MaxChips];
        readonly CampAlerts.Tone[] tones = new CampAlerts.Tone[MaxChips];
        readonly List<CampAlerts.Alert> alerts = new List<CampAlerts.Alert>(8);

        public int LastCount { get; private set; }

        public AlertStrip(VisualElement root)
        {
            row = new VisualElement();
            row.AddToClassList("ledger-alerts");
            row.pickingMode = PickingMode.Ignore;
            root.Add(row);
            for (int i = 0; i < MaxChips; i++)
            {
                int index = i;
                var chip = new Button(() => Tap(index));
                chip.AddToClassList("ledger-chip");
                chip.style.display = DisplayStyle.None;
                row.Add(chip);
                chips[i] = chip;
            }
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
        }

        /// Position every frame (cheap), content on `refresh`.
        public void Place(float left, float right, float top)
        {
            row.style.display = DisplayStyle.Flex;
            row.style.left = left;
            row.style.right = right;
            row.style.top = top;
        }

        public void Refresh(Outpost camp)
        {
            CampAlerts.Collect(camp, alerts);
            LastCount = alerts.Count;
            bool raid = false;
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
                if (shown[i] != a.text) { shown[i] = a.text; chip.text = a.text; }
                if (tones[i] != a.tone || chip.style.display == DisplayStyle.None)
                {
                    tones[i] = a.tone;
                    chip.EnableInClassList("ledger-chip--raid", a.tone == CampAlerts.Tone.Raid);
                    chip.EnableInClassList("ledger-chip--bad", a.tone == CampAlerts.Tone.Bad);
                    chip.EnableInClassList("ledger-chip--warn", a.tone == CampAlerts.Tone.Warn);
                }
                chip.style.display = DisplayStyle.Flex;
            }
            ShowsRaid = raid;
            Showing = n > 0;
        }
    }
}
