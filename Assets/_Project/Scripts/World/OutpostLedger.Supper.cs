using System.Collections.Generic;
using UnityEngine;
using SeaSick.World.Economy;

namespace SeaSick.World
{
    /// **Supper at the fire (2026-10-02).** Kevin: "they all eat one time
    /// during the day, supper time together by the fire before they go to
    /// sleep". No meal trips: at the supper bell
    /// (`Life.CampLifeTuning.EveningStartHour`, 21:00) every hand on the
    /// island who is not down is served straight out of the books -- the
    /// store and the station racks, the same free units `BestMeal` reads.
    ///
    /// **In rounds, so a shortage spreads evenly**: round one gives every
    /// hand his best available dish; later rounds go only to hands still
    /// short of the ration's target (a full day, half of one on half
    /// rations), up to `MaxServings` (four raw quarters make a day). The
    /// first hand served turns with the day, so the same man is not always
    /// the one left with the biscuit. Rations none = nobody eats; dishes
    /// marked "save" are never served (`BestMeal`).
    ///
    /// D2: the bell is read off each quantum's own instant (`Step`'s
    /// `atSeconds`), so watched play and a 12 h catch-up (`AwayProgress`)
    /// serve exactly one supper per sky day through this same code. The
    /// body (`CampWorker.Routine`) only mimes it at the ring afterwards.
    public partial class OutpostLedger
    {
        /// The sky day whose supper was last served; -1 = never (a fresh
        /// camp, a save from before suppers). Saved, so a reload or a dev
        /// scrub back never serves the same evening twice.
        public int supperDay = -1;
        /// Every hand at the last supper got a whole day's fill: the fire
        /// sings tonight (`CampWorker.TickEvening`) and each had
        /// `EconomyTuning.FedTogetherMood`.
        public bool fedTogether;

        /// Most servings one hand gets at one supper.
        public const int MaxServings = 4;

        [System.NonSerialized] List<OutpostHand> supperEaters;

        /// **Test seam, not a game path**: serve `day`'s supper now, for a
        /// self test whose `StepForTest`/short `Tick` never crosses 21:00.
        internal void ServeSupperForTest(int day) => ServeSupper(day);

        /// **Did this quantum ring the supper bell** for a day not yet
        /// served? The bell is `EveningStartHour` on the sky calendar; it
        /// rang if its instant lies in this step's span, or (a load or a dev
        /// jump that landed mid-evening) the evening it opened is still on.
        /// NaN (the self tests' `StepForTest`) never rings.
        bool SupperBellAt(float workDays, double atSeconds, out int day)
        {
            day = -1;
            if (double.IsNaN(atSeconds)) return false;
            double bellAt = Life.CampLifeTuning.EveningStartHour / 24.0;
            double now = TimeOfDay.DaysAt(atSeconds);
            int bell = (int)System.Math.Floor(now - bellAt);
            if (bell < 0 || bell <= supperDay) return false;
            double before = TimeOfDay.DaysAt(atSeconds - workDays * (double)TimeOfDay.WorkDaySeconds);
            bool rang = before < bell + bellAt;
            double evening = (Life.CampLifeTuning.SleepHour - Life.CampLifeTuning.EveningStartHour) / 24.0;
            bool stillEvening = now - (bell + bellAt) < evening;
            if (!rang && !stillEvening) return false;
            day = bell;
            return true;
        }

        /// **Serve `day`'s supper.** See the class doc for the rounds. Sets
        /// each eater's `supperHunger` (what the mood reads until the next
        /// bell), `lastMeal` (the best dish he had tonight, whose bonus he
        /// keeps) and the presentation fields the body and the status read.
        void ServeSupper(int day)
        {
            supperDay = day;
            fedTogether = false;
            if (hands == null) return;
            if (supperEaters == null) supperEaters = new List<OutpostHand>();
            var eaters = supperEaters;
            eaters.Clear();
            foreach (var h in hands)
                if (h != null && !h.downed && !h.dragged) eaters.Add(h);
            int n = eaters.Count;
            if (n == 0) return;

            float mult = EatMultiplier;
            float target = EatPerHandPerDay * mult;
            var best = new string[n];
            var bestScore = new float[n];
            for (int i = 0; i < n; i++)
            {
                bestScore[i] = -1f;
                eaters[i].supperOn = day;
                eaters[i].supperServings = 0;
            }

            int first = day % n;
            bool outOfFood = target <= 0f;
            for (int round = 0; round < MaxServings && !outOfFood; round++)
            {
                bool served = false;
                for (int k = 0; k < n && !outOfFood; k++)
                {
                    int i = (first + k) % n;
                    var h = eaters[i];
                    if (h.full >= target - 1e-4f) continue;
                    string meal = BestMeal();
                    if (meal == null || Take(meal, 1) <= 0) { outOfFood = true; break; }
                    float fill = FoodBook.Fill(meal);
                    h.full = Mathf.Min(MaxFull, h.full + fill);
                    h.supperServings++;
                    foodEaten += fill;
                    away.eaten += fill;
                    float score = MealScore(meal);
                    if (score > bestScore[i]) { bestScore[i] = score; best[i] = meal; }
                    served = true;
                }
                if (!served) break;
            }

            // Fed together = everybody reached a WHOLE day (so never on half
            // rations), Kevin's "supper time together".
            bool together = mult >= 1f;
            for (int i = 0; i < n; i++)
            {
                var h = eaters[i];
                if (best[i] != null) h.lastMeal = best[i];
                h.supperHunger = target <= 0f ? 1f
                    : (1f - mult) + mult * Mathf.Clamp01(1f - h.full / target);
                if (h.full < EatPerHandPerDay - 1e-4f) together = false;
                // Short of the ration by a quarter or more (or no rations):
                // the life log remembers a hungry night.
                if (target <= 0f || h.supperHunger - (1f - mult) >= 0.25f)
                    Life.Lives.Log(h.name, Life.LifeEvents.Hungry, CampLabel);
            }
            if (together)
            {
                fedTogether = true;
                float bump = EconomyTuning.FedTogetherMood;
                for (int i = 0; i < n; i++) eaters[i].mood = Mathf.Clamp01(eaters[i].mood + bump);
            }
        }
    }
}
