using UnityEngine;
using SeaSick.Combat;

namespace SeaSick.World
{
    /// <summary>
    /// **Bows on land (2026-09-30, docs/GDD.md "Bows").** Kevin: the
    /// fletcher makes bows from fine boards + hide, and bows are for defence
    /// and hunting (and the ship: `Ship.ShipArchers`). **Arrows are the
    /// ammunition**: every shot is one `OutpostLedger.SpendArrow`, and with
    /// none left the bow does not shoot and the hand's row says so
    /// (`OutpostHand.bowDry`).
    ///
    /// Three archers here, all live-raid or watched-hunt bodies only (the
    /// books never need them: a raid only fights while watched, and a hunt's
    /// arrow is booked at the kill by `OutpostLedger.HuntKill` either way):
    /// - **the tower lookout** (`TickTowerArcher`, from `TickTower`): a
    ///   posted lookout on his deck with the camp's bow shoots the nearest
    ///   raider within `RaidFightTuning.LookoutBowRange` -- the first to
    ///   shoot, from the landing on. His arrows never draw the raider to him
    ///   (he is up a tower); a kill wears the pile's bow.
    /// - **the defender with a bow** (`TickArcher`, from `TickDefend`): a
    ///   hand armed at the alarm with a store bow (`raidSpear == Res.Bow`) or
    ///   a bow hunter caught out. Shoots any raider within
    ///   `RaidFightTuning.BowRange` -- over the wall too -- and otherwise
    ///   holds the gate/front line like a spear hand, stepping up only far
    ///   enough to bring a raider inside the wall into range. Spear hands
    ///   still close to melee.
    /// - **the bow hunter** (`LooseHuntArrow`, from `TickHunting`): the
    ///   arrow the kill already spent, flown so the player sees it.
    /// </summary>
    public partial class CampWorker
    {
        float archerClock;
        float towerShotClock;
        bool archerReleased, towerReleased;
        RaidWalker archerMark, towerMark;
        bool HasArrows(OutpostHand h) => h.equipment!=null && h.equipment.mainHand==Res.Bow
            ? h.equipment.arrows>0 : camp.Ledger.ArrowsHeld;
        bool SpendShot(OutpostHand h) {
            if(h.equipment!=null && h.equipment.mainHand==Res.Bow) {
                if(h.equipment.arrows<=0) return false; h.equipment.arrows--; return true;
            }
            return camp.Ledger.SpendArrow();
        }
        int huntArrowSerial = -1;

        /// Height of the bow hand above the feet, body metres, for where an
        /// arrow leaves from.
        const float BowHandHeight = 1.35f;
        /// A raider's chest above his feet: where an arrow is aimed.
        const float MarkHeight = 1.1f;
        /// How far round the mark a miss lands, metres.
        const float MissScatter = 1.6f;
        /// A defending archer walks in until the raider is this share of his
        /// range away, so one step does not take him back out of it.
        const float ArcherCloseIn = 0.8f;

        /// True = handled this frame (always, while he is defending).
        bool TickArcher(OutpostHand r, RaidParty party, float dt)
        {
            var ledger = camp.Ledger;
            var props = HunterProps.On(gameObject);
            bool dry = !HasArrows(r);
            r.bowDry = dry;
            if(dry && r.equipment?.mainHand==Res.Bow && ledger.ArrowsHeld) {
                var store=CampPiles.StoreBuildingOf(camp);
                var goal=store!=null ? WorkSpot(camp,store) : camp.CampCentre;
                phase=Phase.Going; acting?.Set(VillagerActing.Mode.None);
                if(!Near(goal,1.5f)) Walk(goal,dt);
                else ledger.ReloadQuiver(r);
                return true;
            }

            RaidWalker mark = dry ? null : RaiderWithin(party, transform.position, RaidFightTuning.BowRange);
            if (mark != null)
            {
                Vector3 at = mark.transform.position;
                // Keep a short shooting distance, but only along walkable routes.
                if(Vector3.Distance(at,transform.position)<4f) {
                    Vector3 away=transform.position+(transform.position-at).normalized*3f;
                    if(CampPath.Reachable(camp,away)) { archerClock=0f; archerReleased=false; phase=Phase.Going; acting?.Set(VillagerActing.Mode.None); Walk(away,dt); return true; }
                }
                if(archerMark!=mark) { archerMark=mark; archerClock=0f; archerReleased=false; }
                phase = Phase.Working;
                Face(at - transform.position, dt);
                archerClock += dt;
                float progress=archerClock/RaidFightTuning.BowShotSeconds;
                acting?.CombatPose(VillagerActing.Mode.BowAttack,progress);
                props.Drive(Res.Bow, HunterProps.Pose.Thrust, at + Vector3.up * MarkHeight);
                if(!archerReleased && progress>=.65f) { archerReleased=true; Shoot(r,mark,transform.position+Vector3.up*BowHandHeight*BodyHeightScale,r); }
                if(progress>=1f) { archerClock=0f; archerReleased=false; }
                return true;
            }
            archerClock = 0f; archerReleased=false; archerMark=null;

            // Nobody in range: step toward a raider who is already INSIDE
            // (never out through the wall), else hold the gather point.
            RaidWalker inside = dry ? null : NearestRaider(party);
            if (inside != null)
            {
                Vector3 at = inside.transform.position;
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                props.Drive(Res.Bow, HunterProps.Pose.Upright);
                if (!Near(at, RaidFightTuning.BowRange * ArcherCloseIn)) Walk(at, dt);
                return true;
            }

            Vector3 gather = GatherPoint(camp, party);
            props.Drive(Res.Bow, HunterProps.Pose.Upright);
            acting?.Set(VillagerActing.Mode.None);
            if (!Near(gather, 1.2f) && !Walk(gather, dt)) { phase = Phase.Going; return true; }
            phase = Phase.Resting;
            var toward = RaidersCentroid(party);
            Face((toward ?? camp.CampCentre) - transform.position, dt);
            return true;
        }

        /// **The lookout shoots first.** Called from `TickTower` while he
        /// stands on his deck. True = he is shooting this frame (the look-out
        /// sweep is skipped); false = no raid, no bow, no arrows or nobody in
        /// range, and he watches as always.
        bool TickTowerArcher(OutpostHand r, Vector3 deck, float dt)
        {
            var party = RaidParty.Active;
            var ledger = camp != null ? camp.Ledger : null;
            if (party == null || party.Camp != camp || ledger == null || !RaidAlarm.IsActive(camp) || !(r.equipment?.mainHand == Res.Bow || ledger.BowHeld))
            {
                r.bowDry = false;
                return false;
            }
            var props = HunterProps.On(gameObject);
            bool dry = !HasArrows(r);
            r.bowDry = dry;
            RaidWalker mark = dry ? null : RaiderWithin(party, deck, RaidFightTuning.LookoutBowRange);
            if (mark == null)
            {
                towerMark=null; towerShotClock=0f; towerReleased=false;
                props.Drive(Res.Bow, HunterProps.Pose.Upright);
                return false;
            }
            Vector3 at = mark.transform.position;
            Face(at - transform.position, dt);
            props.Drive(Res.Bow, HunterProps.Pose.Thrust, at + Vector3.up * MarkHeight);
            if(towerMark!=mark) { towerMark=mark; towerShotClock=0f; towerReleased=false; }
            towerShotClock += dt;
            acting?.CombatPose(VillagerActing.Mode.BowAttack,towerShotClock/RaidFightTuning.BowShotSeconds);
            if (!towerReleased && towerShotClock >= RaidFightTuning.BowShotSeconds*.65f)
            {
                towerReleased = true;
                // No attacker: a raider stung from a tower cannot climb it
                // after him (`RaidWalker.TakeHit` with null re-picks among
                // the hands actually defending on the ground).
                Shoot(r, mark, deck + Vector3.up * BowHandHeight * BodyHeightScale, null);
            }
            if(towerShotClock>=RaidFightTuning.BowShotSeconds) { towerShotClock=0f; towerReleased=false; }
            return true;
        }

        /// **One shot**: one arrow off the pile, the hit rolled now, the
        /// damage landed when the arrow does. `attacker` is who the raider
        /// turns on (null from a tower); a tower kill wears the pile's bow,
        /// a store bow's wear is `RaidAlarm.WearOnKill`'s.
        void Shoot(OutpostHand r, RaidWalker mark, Vector3 from, OutpostHand attacker)
        {
            var ledger = camp.Ledger;
            if (!SpendShot(r)) { r.bowDry = true; return; }
            float distance=Vector3.Distance(from,mark.transform.position);
            float range=attacker==null ? RaidFightTuning.LookoutBowRange : RaidFightTuning.BowRange;
            float chance=Mathf.Clamp(RaidFightTuning.BowHitChance+.18f-.35f*Mathf.Clamp01(distance/range)
                -(mark.Moving ? .10f : 0f) - Mathf.Clamp01(1f-r.mood)*.12f,.15f,.95f);
            bool hit = Random.value < chance;
            Vector3 aim = mark.transform.position + mark.Velocity*(distance/RaidFightTuning.ArrowSpeed) + Vector3.up * MarkHeight;
            if (!hit)
            {
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(0.6f, MissScatter);
                aim = mark.transform.position + new Vector3(off.x, 0f, off.y);
                aim.y = camp.GroundAt(aim);
            }
            var target = mark;
            var owner = camp;
            bool fromTower = attacker == null;
            ArrowFlight.Loose(from, aim, () =>
            {
                if (!hit || target == null || target.Dead || owner==null) return;
                // Fixed ballistic aim: a target that changes direction can dodge.
                if(Vector3.Distance(aim,target.transform.position+Vector3.up*MarkHeight)>1.25f) return;
                if(!VillageDefense.ClearSight(owner,from,aim)) return;
                target.TakeArrow(RaidFightTuning.BowDamage, attacker);
                ArrowFlight.Puff(target.transform.position + Vector3.up * MarkHeight, new Color(0.55f, 0.12f, 0.10f));
                if (fromTower && r.equipment?.mainHand != Res.Bow && target.Dead && owner != null) owner.Ledger?.WearPileBow();
            });
        }

        /// **The hunt's arrow, flown once per trip** (the kill already spent
        /// it in the books). Called from `TickHunting` the frame the beast
        /// is his.
        void LooseHuntArrow(OutpostHand r, Animal beast)
        {
            if (beast == null || !r.huntArmed || huntArrowSerial == r.haulSerial) return;
            huntArrowSerial = r.haulSerial;
            Vector3 from = transform.position + Vector3.up * BowHandHeight * BodyHeightScale;
            Vector3 at = beast.transform.position + Vector3.up * FlankHeight;
            ArrowFlight.Loose(from, at, null, beast.transform);
        }

        /// The nearest live raider (not dead, fleeing or recalled) within
        /// `range` metres of `from`, walls or no walls -- an arrow goes over.
        static RaidWalker RaiderWithin(RaidParty party, Vector3 from, float range)
        {
            if (party == null) return null;
            var list = party.Walkers;
            RaidWalker best = null;
            float bestD = range * range;
            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                if (w == null || w.Dead) continue;
                if (w.phase == RaidWalker.Phase.Fleeing || w.phase == RaidWalker.Phase.Recalled) continue;
                Vector3 d = w.transform.position - from;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestD && VillageDefense.ClearSight(party.Camp,from+Vector3.up*1.3f,w.transform.position+Vector3.up)) { bestD = sq; best = w; }
            }
            return best;
        }

        /// This body's height against the 1.7 m it was modelled at.
        float BodyHeightScale
        {
            get
            {
                float s = transform.lossyScale.y;
                return s > 1e-4f ? s : 1f;
            }
        }
    }
}
