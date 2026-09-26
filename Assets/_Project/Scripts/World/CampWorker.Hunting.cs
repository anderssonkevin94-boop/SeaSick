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
    /// Now, still never driving the books:
    /// - **Stalled = standing at home.** No spear, no room for meat or
    ///   hide, no game left: he does not go out (the sheets say why:
    ///   `StallReason`). A beast he already killed is still fetched.
    /// - **Stalk** his claimed beast at `StalkDistance`, spear upright,
    ///   while the books work through it (`OutpostLedger.HuntProgress01`).
    /// - **Strike** once the books are `StrikeAt` of the way through the
    ///   animal: close to arm's length and jab (`Bend` + `HunterProps`
    ///   thrust). The kill itself is still `SyncHunting`'s, on the claimed
    ///   beast, so the goat that drops is the one he is spearing.
    /// - **Carry**: the carcass waits for him (`Animal.AwaitingHunter`),
    ///   he shoulders the animal's own body (`HunterProps.Shoulder`), walks
    ///   it to the store the Food goes to, stoops, and it is gone -- the
    ///   meat and hide were booked at the kill.
    public partial class CampWorker
    {
        /// Metres he keeps off a beast while the books work through it.
        const float StalkDistance = 4.5f;
        /// Slack past `StalkDistance` before he re-closes on a grazing beast.
        const float StalkSlack = 2f;
        /// Fraction of the current animal the books must have taken before
        /// he closes in to strike. The ledger takes ~1 % of an animal per
        /// 3.6 s quantum, so 0.94 is the last ~20 s of the stalk.
        const float StrikeAt = 0.94f;
        /// Seconds of stoop at the store before the carcass is gone.
        const float SetDownSeconds = 0.8f;
        /// Roughly the middle of a goat's flank above its feet, metres.
        const float FlankHeight = 0.35f;

        HunterProps hunterProps;
        float setDownLeft = -1f;

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
                if (!props.HasCarcass) { Drop(); phase = Phase.Resting; wait = RestSeconds; setDownLeft = -1f; return; }
                if (setDownLeft >= 0f)
                {
                    acting?.Set(VillagerActing.Mode.Bend);
                    setDownLeft -= dt;
                    if (setDownLeft > 0f) return;
                    props.PutDown();
                    setDownLeft = -1f;
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;
                }
                acting?.Set(VillagerActing.Mode.None);
                // Re-aimed every step: a hut raised mid-carry takes it.
                dropAt = Dropoff(r, Res.Food);
                if (!Walk(dropAt, dt)) return;
                setDownLeft = SetDownSeconds;
                return;
            }
            setDownLeft = -1f;

            // A carcass destroyed under him (it waited too long) is gone.
            if (quarry == null) quarry = null;
            bool fetching = quarry != null && quarry.Dead;

            // --- stalled: the books are not hunting, so neither is he -----
            if (!fetching && ledger != null && ledger.Stalled(r))
            {
                Unclaim();
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
                phase = Phase.Resting;
                if (Walk(home, dt)) FaceRest(dt, 0f);
                return;
            }

            switch (phase)
            {
                case Phase.Resting:
                {
                    props.Drive(spear, HunterProps.Pose.Upright);
                    acting?.Set(VillagerActing.Mode.None);
                    bool there = Walk(home, dt);
                    wait -= dt;
                    if (wait > 0f) { if (there) FaceRest(dt, 0f); return; }
                    if (!ClaimQuarry())
                    {
                        // Every beast left has a man on it (or none is
                        // loaded here): wait at home and ask again.
                        wait = RestSeconds * 3f;
                        return;
                    }
                    phase = Phase.Going;
                    return;
                }

                case Phase.Going:
                case Phase.Working:
                {
                    if (quarry == null) { phase = Phase.Resting; wait = RestSeconds; return; }
                    Vector3 at = quarry.transform.position;

                    if (fetching)
                    {
                        // The books took it. Walk to where it fell, watch it
                        // go over, lift it.
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
                        carrying = Res.Food;
                        dropAt = Dropoff(r, Res.Food);
                        acting?.Set(VillagerActing.Mode.None);
                        phase = Phase.Coming;
                        return;
                    }

                    bool strike = ledger != null && ledger.HuntProgress01() >= StrikeAt;
                    float keep = strike ? HuntReach : StalkDistance;
                    float slack = strike ? 0f : StalkSlack;
                    Vector3 d = at - transform.position; d.y = 0f;
                    if (d.magnitude > keep + slack + 0.35f)
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
                    return;
                }

                default:
                    // Held/Landing/Flying belong to the Hand's throw, which
                    // runs ahead of this; nothing to mime here.
                    return;
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
