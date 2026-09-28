using UnityEngine;

namespace SeaSick.World
{
    /// **A hunt is a WALK (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md).**
    ///
    /// Kevin: *"it should be them walking up to an animal, jabbing it with
    /// its spear, picking it up and walking back. There should be no
    /// arbitrary timer."* A hunter's trip is an ordinary walked trip on the
    /// hand's haul fields (`haulRes == Res.Game`, one carcass, `Field ->
    /// Store`):
    ///
    /// 1. **walk to the animal** -- claimed (`GameUnclaimed`); the body walks
    ///    to the real beast, the invisible walker to where the herd was last
    ///    measured (`SourceMetres(Game)`);
    /// 2. **the jab** -- `JabSeconds` of pose at the beast, then the pickup
    ///    event IS the kill (`HuntKill`): one animal off `Game.standing`, the
    ///    spear worn `SpearWear`, an arrow loosed if he went out armed;
    /// 3. **the carry** -- the carcass on his shoulders, walked home;
    /// 4. **the drop-off** -- `Res.MeatPerAnimal` Food and `Techs.HuntDrops`
    ///    (1 Hide) as WHOLE units, as far as each fits, on arrival.
    ///
    /// No stalk timer, no kill-rate schedule: the limits are a beast to
    /// claim, a spear, and room for meat or hide.
    public partial class OutpostLedger
    {
        /// True when neither the meat nor any drop of a carcass has room --
        /// the one "store full" that stops a hunter. Whole units, net of
        /// loads walking to the store, like every other trip's room.
        bool HuntStoreFull()
        {
            if (RoomFor(Res.Meat) > 0) return false;
            foreach (var drop in Economy.Techs.HuntDrops)
                if (drop.n > 0 && RoomFor(drop.res) > 0) return false;
            return true;
        }

        /// **Game-days of one hunt trip -- a DISPLAY estimate only**: walk
        /// out, the jab, walk back. Nothing books through it.
        public float HuntTripDays(bool armed)
        {
            float walk = SourceMetres(Res.Game) / WalkMetresPerSecond;
            return SecondsToDays(2f * walk + JabSeconds);
        }

        /// Animals a day one hunter could bring home (display estimate).
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
            h.workLeft = JabSeconds;
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
            Life.Lives.Log(h.name, Life.LifeEvents.HuntingKill, CampLabel);

            // **Hunting accidents** (death/rescue phase 2, 2026-09-27):
            // rolled only at the jab, and only while a BODY is walking this
            // trip -- `h.driven` is true exactly while the camp is watched
            // and `CampWorker` is miming him, false in catch-up/away steps,
            // so this never fires headless. Iron halves the stone chance.
            // The carcass is already his (picked up above): if he goes
            // down carrying it, `Down` drops it on the ground where he
            // stands, same as anything else.
            if (h.driven)
            {
                float chance = Life.LifeTuning.HuntAccidentChance * (spear == Res.IronSpear ? 0.5f : 1f);
                // Flagged, not acted on here -- see `huntAccidentPending`'s
                // doc: `PickUp`/`FinishPickup` are still writing this trip's
                // fields at this point.
                if (Random.value < chance) h.huntAccidentPending = true;
            }
            return true;
        }

        /// **All clear (death/rescue phase 12).** A spear a defender is
        /// walking home from the store lands back in the fluid "whole+part"
        /// pool at `1 - wear` of a whole unit -- worn spears come home
        /// lighter, run the OTHER way from how `DrawHeld` wears a stack
        /// down, since the store keeps no per-unit wear to hand a specific
        /// physical spear's fraction back to. Room-ignoring on purpose,
        /// same as `SpillPart`'s own reasoning: a spear that existed and is
        /// walking home is not lost to a ceiling it never asked to fit
        /// under ("nothing vanishes").
        public void ReturnWornSpear(string spear, float wear)
        {
            if (string.IsNullOrEmpty(spear)) return;
            float remain = Mathf.Clamp01(1f - wear);
            if (remain <= 0f) return;   // shouldn't reach here -- WearOnKill breaks it first
            var st = Store(spear, true);
            st.part += remain;
            int whole = Mathf.FloorToInt(st.part + 1e-5f);
            if (whole > 0) { st.whole += whole; st.part -= whole; }
        }

        /// **The carcass lands at the store**: meat and hide as whole units,
        /// each as far as its room goes (net of loads walking there); the
        /// rest is lost. A trip that never killed just ends.
        void DepositCarcass(OutpostHand h)
        {
            if (h.huntKilled)
            {
                int meat = Mathf.Min(Mathf.RoundToInt(Res.MeatPerAnimal), RoomFor(Res.Meat));
                if (meat > 0) { Store(Res.Meat, true).whole += meat; away.Add(Res.Meat, meat); NoteDelivered(Res.Meat, meat); }
                foreach (var drop in Economy.Techs.HuntDrops)
                {
                    int n = Mathf.Min(drop.n, RoomFor(drop.res));
                    if (n <= 0) continue;
                    Store(drop.res, true).whole += n;
                    away.Add(drop.res, n);
                    NoteDelivered(drop.res, n);
                }
                h.huntDeposits++;
            }
            ClearHaul(h);
        }

        /// **A hunter's quantum, by walks.** Stationary work (the jab) is
        /// paid at `WorkFactorOn(Food)` x `PriorityMultiplier(Food)`; the
        /// walking is what it is. With the store full for both halves of a
        /// carcass the rest of the quantum helps (builds or hauls), exactly
        /// as `GatherDay`.
        void HuntDay(OutpostHand h, float days, bool helpBuild)
        {
            float scale = WorkFactorOn(h, Res.Food) * PriorityMultiplier(Res.Food);
            float budget = days * scale;
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                // Any load in his arms (a carcass, or one from helping) first.
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget, scale)) return; continue; }
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
    }
}
