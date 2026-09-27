using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The pinned goal, one slim line under the alert strip (2026-09-27).**
    /// Kevin: *"let's try it."* *"Goal: Sawmill level 2 · 2/3 ready ›"* --
    /// a tap opens the camp overview, which shows the pinned goal's chain.
    /// Only while a goal is pinned (`GoalPin`); the fire's standing goal
    /// stays in the overview and costs no screen. Takes the strip's slot
    /// when the strip is empty, sits under it when not, and steps aside
    /// while a sheet is open. Built once, re-texted (a rebuilt button loses
    /// the tap it is in the middle of).
    public sealed class GoalBar
    {
        public const float Height = 28f;
        const float Gap = 4f;

        /// True while the bar is up (the top chrome rect grows to cover it).
        public static bool Showing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Showing = false; }

        readonly Button bar;
        readonly VisualElement fill;
        readonly Label text;
        string shown;
        float nextCompute;
        int pinVersion = -1;
        Outpost lastCamp;

        public GoalBar(VisualElement root)
        {
            bar = new Button(Tap);
            bar.AddToClassList("ledger-goal");
            bar.style.display = DisplayStyle.None;
            var track = new VisualElement();
            track.AddToClassList("ledger-goal-track");
            track.pickingMode = PickingMode.Ignore;
            fill = new VisualElement();
            fill.AddToClassList("ledger-goal-fill");
            track.Add(fill);
            bar.Add(track);
            text = new Label();
            text.AddToClassList("ledger-goal-text");
            text.pickingMode = PickingMode.Ignore;
            bar.Add(text);
            root.Add(bar);
        }

        void Tap()
        {
            var camp = MidnightLandHud.Camp;
            if (camp != null) Sheets.Open(new CampOverviewSheet(camp));
        }

        public void Hide()
        {
            bar.style.display = DisplayStyle.None;
            Showing = false;
        }

        /// `top` is where the alert strip sits; the bar goes under it when
        /// the strip has a chip up. Returns the height it adds to the top
        /// chrome (0 when hidden).
        public float Place(Outpost camp, float left, float right, float top)
        {
            var pin = camp != null && camp.Ledger != null ? camp.Ledger.ActiveGoal : null;
            if (pin == null || SheetHost.FrameOpen)
            {
                if (Showing) Hide();
                return 0f;
            }
            float now = Time.unscaledTime;
            if (camp != lastCamp || now >= nextCompute || pinVersion != GoalPin.Version)
            {
                lastCamp = camp;
                nextCompute = now + 1f;
                pinVersion = GoalPin.Version;
                var c = GoalChain.ForCamp(camp.Ledger);
                string t = $"Goal: {c.title} · " + (c.CanComplete ? "ready ›" : $"{c.ready}/{c.total} ready ›");
                if (t != shown) { shown = t; text.text = t; }
                float f = c.total > 0 ? (float)c.ready / c.total : 1f;
                fill.style.width = Length.Percent(Mathf.Clamp01(f) * 100f);
                bar.EnableInClassList("ledger-goal--ready", c.CanComplete);
            }
            bool under = AlertStrip.Showing;
            bar.style.display = DisplayStyle.Flex;
            bar.style.left = left;
            bar.style.right = right;
            bar.style.top = under ? top + AlertStrip.Height + Gap : top;
            Showing = true;
            return under ? Gap + Height : Height;
        }
    }
}
