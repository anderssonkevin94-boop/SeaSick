using UnityEngine;

namespace SeaSick.World
{
    /// **A hunt is a TRIP (2026-09-27).**
    ///
    /// Kevin, phone playtest: *"we need fur/hide as a resource ... the fur
    /// number doesn't seem to be accurate."* Until today the hunt was the one
    /// gather order still booked as a per-day accrual: `Game.standing` fell a
    /// fraction every step and meat and hide went into the stores' `.part`
    /// at the same time, so the Hide count ticked over at arbitrary moments
    /// (mid-stalk, or before the carcass was home). Now a hunter works the
    /// way every other gatherer has since 2026-09-23 (`GatherDay`): one
    /// trip at a time, on the hand's saved haul fields
    /// (`haulRes == Res.Game`, one carcass, `Field -> Store`):
    ///
    /// 1. **walk out + stalk** -- `haulWalkDays` out and a stalk long enough
    ///    that the whole trip keeps the old kill rate (`Res.GatherRate(Game)`
    ///    animals a day, x `BowKillBonus` with an arrow in the quiver);
    /// 2. **the kill** -- the step whose work reaches the end of the stalk:
    ///    ONE whole animal off `Game.standing`, the spear worn `SpearWear`
    ///    once, an arrow loosed if the trip went out armed (`huntKilled`);
    /// 3. **the carry** -- `haulWalkDays` back to the store;
    /// 4. **the deposit** -- `Res.MeatPerAnimal` Food and `Techs.HuntDrops`
    ///    (1 Hide) as WHOLE units, as far as each fits; what does not fit is
    ///    lost (the 2026-09-26 rule: a hunt stops only when neither fits).
    ///
    /// All of it is in `Step`, so a watched camp and an unwatched one land
    /// on the same books at any tick size (D2), and a trip split by a save
    /// resumes from its saved fields.
    public partial class OutpostLedger
    {
        /// True when neither the meat nor any drop of a carcass has room --
        /// the one "store full" that stops a hunter. Whole units, net of
        /// loads walking to the store, like every other trip's room.
        bool HuntStoreFull()
        {
            if (RoomFor(Res.Food) > 0) return false;
            foreach (var drop in Economy.Techs.HuntDrops)
                if (drop.n > 0 && RoomFor(drop.res) > 0) return false;
            return true;
        }

        /// Seconds of stalking for one animal: the old kill rate's whole
        /// animal-time less the two walked legs (never under a handling).
        float HuntStalkSeconds(bool armed, float walkSeconds)
        {
            float perDay = Res.GatherRate(Res.Game) * (armed ? BowKillBonus : 1f);
            float animal = TimeOfDay.DayLength / Mathf.Max(0.0001f, perDay);
            return Mathf.Max(HandleSeconds, animal - 2f * walkSeconds);
        }

        /// **Game-days of one hunt trip** (for readouts): the same
        /// arithmetic `StartHuntTrip` books.
        public float HuntTripDays(bool armed)
        {
            float walk = LegMetres(Res.Game, HaulPlace.Field, -1, HaulPlace.Store, -1, null) / WalkMetresPerSecond;
            return SecondsToDays(2f * walk + HuntStalkSeconds(armed, walk));
        }

        /// Animals a day one full-strength hunter brings home.
        public float HuntTripPerDay(bool armed)
        {
            float d = HuntTripDays(armed);
            return d > 0f ? 1f / d : 0f;
        }

        /// Animals standing that no hunter is already out stalking.
        int GameUnclaimed(OutpostHand except)
        {
            var stock = Stock(Res.Game);
            int n = stock != null ? Mathf.FloorToInt(stock.standing + 1e-4f) : 0;
            if (hands != null)
                foreach (var o in hands)
                    if (o != null && o != except && o.HuntTrip && !o.huntKilled) n--;
            return n;
        }

        /// Out after one animal: a spear in the pile, a beast nobody else is
        /// on, and room for some part of the carcass. False otherwise.
        bool StartHuntTrip(OutpostHand h)
        {
            if (SpearInHand() == null || HuntStoreFull() || GameUnclaimed(h) < 1) return false;
            bool armed = HeldOf(Res.Arrows) >= 1f;
            StartTimedTrip(h, Res.Game, 1, HaulPlace.Field, -1, HaulPlace.Store, -1);
            float walk = h.haulWalkDays * TimeOfDay.DayLength;
            h.haulWorkDays = SecondsToDays(HuntStalkSeconds(armed, walk));
            h.haulDays = Mathf.Max(Eps, 2f * h.haulWalkDays + h.haulWorkDays);
            h.haulLeft = h.haulDays;
            h.huntArmed = armed;
            h.huntKilled = false;
            return true;
        }

        /// **The kill**: one whole animal off the herd, the spear worn once,
        /// an arrow loosed if he went out with the bow. False (the trip is
        /// off) when the herd or the spear went while he stalked.
        bool HuntKill(OutpostHand h)
        {
            var stock = Stock(Res.Game);
            string spear = SpearInHand();
            if (stock == null || stock.standing < 1f - 1e-4f || spear == null) return false;
            stock.standing = Mathf.Max(0f, stock.standing - 1f);
            float wear = Economy.Techs.SpearWear(spear);
            if (wear > 0f) DrawHeld(spear, wear);
            if (h.huntArmed && HeldOf(Res.Arrows) > 0f) DrawHeld(Res.Arrows, Mathf.Min(1f, HeldOf(Res.Arrows)));
            h.huntKilled = true;
            h.huntKills++;
            return true;
        }

        /// **The carcass lands at the store**: meat and hide as whole units,
        /// each as far as its room goes (net of loads walking there); the
        /// rest is lost. A trip that never killed just ends.
        void DepositCarcass(OutpostHand h)
        {
            if (h.huntKilled)
            {
                int meat = Mathf.Min(Mathf.RoundToInt(Res.MeatPerAnimal), RoomFor(Res.Food));
                if (meat > 0) { Store(Res.Food, true).whole += meat; away.Add(Res.Food, meat); }
                foreach (var drop in Economy.Techs.HuntDrops)
                {
                    int n = Mathf.Min(drop.n, RoomFor(drop.res));
                    if (n <= 0) continue;
                    Store(drop.res, true).whole += n;
                    away.Add(drop.res, n);
                }
                h.huntDeposits++;
            }
            ClearHaul(h);
        }

        /// **A hunter's quantum, by trips.** The work clock is scaled as the
        /// old rate was (`WorkFactorOn(Food)` x `PriorityMultiplier(Food)`).
        /// With the store full for both halves of a carcass the rest of the
        /// quantum helps (builds or hauls), exactly as `GatherDay`.
        void HuntDay(OutpostHand h, float days, bool helpBuild)
        {
            float scale = WorkFactorOn(h, Res.Food) * PriorityMultiplier(Res.Food);
            float budget = days * scale;
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling && !h.HuntTrip)
                {
                    // A load from helping (a haul, a site armful) first.
                    if (!AdvanceHaul(h, ref budget)) return;
                    continue;
                }
                if (h.HuntTrip)
                {
                    if (!h.huntKilled)
                    {
                        float toKill = Mathf.Max(0f, h.haulLeft - h.haulWalkDays);
                        float d = Mathf.Min(budget, toKill);
                        h.haulLeft -= d;
                        budget -= d;
                        if (toKill - d > Eps) break;          // still stalking
                        h.haulLeft = h.haulWalkDays;
                        if (!HuntKill(h)) { ClearHaul(h); break; }
                        continue;
                    }
                    float c = Mathf.Min(budget, h.haulLeft);
                    h.haulLeft -= c;
                    budget -= c;
                    if (h.haulLeft > Eps) break;              // still carrying
                    DepositCarcass(h);
                    continue;
                }
                if (!StartHuntTrip(h)) break;
            }
            if (budget <= Eps || h.Hauling || !HuntStoreFull() || scale <= 0f) return;
            float help = budget / scale * WorkFactor(h);
            if (helpBuild)
            {
                if (sites != null) BuilderDay(h, ref help);
                if (help > Eps && !h.Hauling) TransferDay(h, ref help);
            }
            else HaulerDay(h, ref help);
        }

        /// **Game-days of work until this hunter's kill**, 0 once the animal
        /// is down or with no hunt under way. The body strikes on this.
        public static float HuntDaysToKill(OutpostHand h)
        {
            if (h == null || !h.HuntTrip || h.huntKilled) return 0f;
            return Mathf.Max(0f, h.haulLeft - h.haulWalkDays);
        }
    }
}
