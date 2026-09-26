using UnityEngine;

namespace SeaSick.World
{
    /// **The hunt, mimed to the books' own clock (2026-09-26).**
    ///
    /// Kevin, phone playtest: *"my hunter goes and hunts an animal, some
    /// issues there. make a placeholder spear. make sure that the animal he
    /// kills dies. and that he carries the animal back to camp."*
    ///
    /// What was wrong with the 2026-09-22 mime: he walked up to a goat,
    /// clubbed it for two seconds and carried "meat" home -- every trip,
    /// whatever the ledger did. The ledger kills half an animal a day (one
    /// beast per ~6 min of play), and `Outpost.SyncHunting` drops a beast
    /// only when `Game.standing` crosses a whole number, so the goat he
    /// clubbed stood there grazing while he walked home with nothing the
    /// books had paid. Worse, he did it with a full larder, when the books
    /// were not killing at all (`Stalled` was never asked).
    ///
    /// **2026-09-27: the hunt is a ledger TRIP** (`OutpostLedger.Hunting`),
    /// and the body follows its phases, never driving them:
    /// - **No trip = home.** The books start a trip only with a spear, a
    ///   beast nobody else is on and room for meat or hide; until then he
    ///   stands at home (the sheets say why: `StallReason`).
    /// - **Stalk** his claimed beast at `StalkDistance` while the trip's
    ///   stalk runs.
    /// - **Strike** in the last `StrikeSeconds` before the books' kill
    ///   (`OutpostLedger.HuntDaysToKill` on the step grid): close to arm's
    ///   length and jab. The kill is the books' whole animal; `SyncHunting`
    ///   drops the claimed beast in that step, so the goat that falls is the
    ///   one he is spearing.
    /// - **Carry**: he shoulders the animal's own body (`HunterProps.Shoulder`)
    ///   and walks it to the store; early, he stands there holding it; he
    ///   stoops as the deposit comes due and the carcass goes in the step
    ///   the books put 4 Food and 1 Hide in the store -- not before.
    public partial class CampWorker
    {
        /// Metres he keeps off a beast while the books work through it.
        const float StalkDistance = 4.5f;
        /// Slack past `StalkDistance` before he re-closes on a grazing beast.
        const float StalkSlack = 2f;
        /// Real seconds before the books' kill that he closes in to strike.
        const float StrikeSeconds = 6f;
        /// Seconds of stoop at the store before the carcass is gone.
        const float SetDownSeconds = 0.8f;
        /// Roughly the middle of a goat's flank above its feet, metres.
        const float FlankHeight = 0.35f;

        HunterProps hunterProps;
        float setDownLeft = -1f;
        /// The ledger trip (`haulSerial`) whose carcass is on his shoulders.
        int carcassTrip = -1;

        /// The books still have this carcass in his arms.
        static bool BooksCarrying(OutpostHand r, int trip) =>
            r.HuntTrip && r.huntKilled && r.haulSerial == trip;

        void TickHunting(OutpostHand r, float dt)
        {
            var ledger = camp != null ? camp.Ledger : null;
            string spear = ledger != null ? ledger.SpearInHand() : null;
            if (hunterProps == null) hunterProps = HunterProps.On(gameObject);
            var props = hunterProps;

            // --- carrying the carcass home: nothing else matters ----------
            if (phase == Phase.Coming)
            {
                props.Drive(spear, HunterProps.Pose.Upright);
                if (!props.HasCarcass) { Drop(); phase = Phase.Resting; wait = 0f; setDownLeft = -1f; return; }
                bool booksHave = BooksCarrying(r, carcassTrip);
                dropAt = Dropoff(r, Res.Food);
                if (setDownLeft < 0f)
                {
                    acting?.Set(VillagerActing.Mode.None);
                    if (!Walk(dropAt, dt)) return;
                    // At the store. Stoop as the deposit comes due (or at
                    // once if the books already put it in).
                    float due = booksHave ? SecondsToDeposit(r) : 0f;
                    if (due > SetDownSeconds) return;
                    setDownLeft = SetDownSeconds;
                }
                acting?.Set(VillagerActing.Mode.Bend);
                setDownLeft -= dt;
                // The carcass goes in the step the books deposit it.
                if (setDownLeft > 0f || booksHave) return;
                props.PutDown();
                setDownLeft = -1f;
                carcassTrip = -1;
                Drop();
                phase = Phase.Resting;
                wait = 0f;
                return;
            }
            setDownLeft = -1f;

            // A carcass destroyed under him (it waited too long) is gone.
            if (quarry == null) quarry = null;
            bool fetching = quarry != null && quarry.Dead;
            bool stalking = r.HuntTrip && (!r.huntKilled || (quarry != null && !quarry.Dead));

            // --- no hunt in the books: home ------------------------------
            if (!fetching && !stalking)
            {
                Unclaim();
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
                phase = Phase.Resting;
                if (Walk(home, dt)) FaceRest(dt, 0f);
                return;
            }

            if (quarry == null && !ClaimQuarry())
            {
                // Every beast left has a man on it (or none is loaded here):
                // wait at home; the books go on without a picture.
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
                phase = Phase.Resting;
                if (Walk(home, dt)) FaceRest(dt, 0f);
                return;
            }

            Vector3 at = quarry.transform.position;
            if (fetching)
            {
                // The books took it. Walk to where it fell, watch it go
                // over, lift it.
                if (!Near(at, HuntReach))
                {
                    phase = Phase.Going;
                    props.Drive(spear, HunterProps.Pose.Upright);
                    acting?.Set(VillagerActing.Mode.None);
                    Walk(StandOffFrom(at, HuntReach * 0.6f), dt);
                    return;
                }
                Face(at - transform.position, dt);
                if (!quarry.Down)
                {
                    props.Drive(spear, HunterProps.Pose.Thrust, at + Vector3.up * FlankHeight * 0.5f);
                    acting?.Set(VillagerActing.Mode.Bend);
                    return;
                }
                props.Drive(spear, HunterProps.Pose.Upright);
                props.Shoulder(quarry);
                quarry = null;               // not Unclaim: it is a carcass now
                carcassTrip = r.HuntTrip && r.huntKilled ? r.haulSerial : -1;
                carrying = Res.Food;
                dropAt = Dropoff(r, Res.Food);
                acting?.Set(VillagerActing.Mode.None);
                phase = Phase.Coming;
                return;
            }

            // Stalking: close in for the strike in the last seconds before
            // the books' kill (or at once if the books already killed).
            float toKill = r.huntKilled ? 0f
                : SecondsUntilSpent(OutpostLedger.HuntDaysToKill(r), OutpostLedger.QuantumDays * TripFactor(r));
            bool strike = toKill <= StrikeSeconds;
            float keep = strike ? HuntReach : StalkDistance;
            float slack = strike ? 0f : StalkSlack;
            Vector3 dv = at - transform.position; dv.y = 0f;
            if (dv.magnitude > keep + slack + 0.35f)
            {
                phase = Phase.Going;
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
                Walk(StandOffFrom(at, keep), dt);
                return;
            }

            phase = Phase.Working;
            Face(at - transform.position, dt);
            if (strike)
            {
                props.Drive(spear, HunterProps.Pose.Thrust, at + Vector3.up * FlankHeight);
                acting?.Set(VillagerActing.Mode.Bend);
            }
            else
            {
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
            }
        }

        /// A point `off` metres from a beast, on the side he is standing on.
        Vector3 StandOffFrom(Vector3 beast, float off)
        {
            Vector3 away = transform.position - beast;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            Vector3 p = beast + away.normalized * off;
            p.y = camp != null ? camp.GroundAt(p) : p.y;
            return p;
        }
    }
}
