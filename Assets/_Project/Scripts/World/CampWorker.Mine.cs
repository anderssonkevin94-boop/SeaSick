using UnityEngine;

namespace SeaSick.World
{
    /// **The miner walks into the hill (2026-10-05).** Kevin's spec: "he
    /// will walk in the mine shaft". The books already have the trip -- a
    /// dig is a catch trip from the mouth (`OutpostLedger.Mines`,
    /// `HaulPlace.Shore`) whose `AtPickup` leg is the time underground, and
    /// they stand him at the mouth stand on the apron. The body then walks
    /// a fixed straight line from there through the doorway to the model's
    /// `Mouth` (no pathfinding: the shaft head is solid to the grid), and is
    /// hidden once there. Coming up, he is shown at `Mouth` and walks the
    /// same line back out before the ordinary carry to `DropSpot`.
    ///
    /// The hiding is its own flag, NOT `bodyHidden`: that one belongs to the
    /// raid alarm and sleep, and `TickAlarmRole` reveals any `bodyHidden`
    /// body that is not asleep. This one is re-derived every frame
    /// (`TickMineHide`, the top of `Update`), so a load, a watch starting
    /// mid-trip or a re-order can never leave him invisible. The raid alarm
    /// leaves an underground miner alone (`OutpostLedger.Underground`).
    public partial class CampWorker
    {
        enum MineWalk { None, In, Under, Out }
        MineWalk mineWalk;
        bool inMine;

        /// Metres from the target at which the scripted walk has arrived.
        const float MineArrive = 0.12f;

        /// The mine's `Mouth` (model or stand-in) for mine station `station`,
        /// and the height of its floor (the building's root), or false when
        /// that station is not a mine / not standing.
        bool MineAt(int station, out Vector3 mouth)
        {
            mouth = default;
            var l = camp != null ? camp.Ledger : null;
            if (l == null || !OutpostLedger.Mines(l.StationAt(station))) return false;
            var b = StationBuilding(station);
            if (b != null)
            {
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                    if (BuildingFactory.Stem(t.name) == "Mouth") { mouth = t.position; return true; }
                mouth = b.transform.TransformPoint(BuildingFactory.MineMarkLocal("Mouth"));
                return true;
            }
            var row = l.StationRow(station);
            if (row == null) return false;
            OutpostLedger.MouthOf(row, out _, out mouth);
            mouth.y = camp.GroundAt(mouth);
            return true;
        }

        static bool MineTrip(HaulView view) => view.from == HaulPlace.Shore;

        /// One straight step toward `to` on the walk clip, the floor held at
        /// `to.y` (the shaft's floor, not the hill's surface above it).
        /// True on arrival.
        bool StraightTo(Vector3 to, float dt)
        {
            Vector3 here = transform.position;
            Vector3 d = to - here;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist <= MineArrive)
            {
                stride.Stop();
                acting?.Commanded(0f);
                return true;
            }
            Vector3 step = stride.Step(transform, d / dist, Cruise, dist, dt);
            acting?.Commanded(stride.Speed);
            here += step;
            here.y = Mathf.Lerp(here.y, to.y, Mathf.Clamp01(step.magnitude / Mathf.Max(dist, 1e-3f)));
            transform.position = here;
            return false;
        }

        /// **At the mouth of a mine (`AtPickup`).** Walk in to `Mouth`, then
        /// stand there unseen; his dig timer runs throughout, exactly as at
        /// any pickup (`BodyWorked`). False for every other pickup.
        bool TickMineIn(OutpostHand r, HaulView view, float dt)
        {
            if (!MineTrip(view) || !MineAt(view.fromStation, out Vector3 mouth))
            {
                mineWalk = MineWalk.None;
                return false;
            }
            phase = Phase.Working;
            r.walkingIn = false;
            if (mineWalk != MineWalk.Under)
            {
                mineWalk = MineWalk.In;
                acting?.Set(VillagerActing.Mode.None);
                if (StraightTo(mouth, dt)) mineWalk = MineWalk.Under;
            }
            camp.Ledger.BodyWorked(r, dt * ClockRate());
            return true;
        }

        /// **Coming up (`ToDrop`, the stone on him).** Shown at `Mouth`
        /// (`TickMineHide` already reveals him where he stood), he walks the
        /// doorway line back out to the mouth stand; then the ordinary carry
        /// takes over. False when there is no way out to walk.
        bool TickMineOut(HaulView view, float dt)
        {
            if (mineWalk == MineWalk.None) return false;
            if (!MineTrip(view) || !MineAt(view.fromStation, out Vector3 mouth))
            {
                mineWalk = MineWalk.None;
                return false;
            }
            if (mineWalk != MineWalk.Out)
            {
                // Picked up while walking in (a very short dig): from where he is.
                if (mineWalk == MineWalk.Under) transform.position = mouth;
                mineWalk = MineWalk.Out;
            }
            Vector3 stand = mimePick;
            stand.y = Mathf.Max(stand.y, mouth.y);
            if (!StraightTo(stand, dt)) return true;
            mineWalk = MineWalk.None;
            return false;
        }

        /// Hidden while (and only while) he is underground: at a mine's
        /// pickup AND through the doorway (`MineWalk.Under`). The alarm's
        /// and sleep's own hiding (`bodyHidden`) is left alone.
        void TickMineHide(OutpostHand r)
        {
            bool want = mineWalk == MineWalk.Under && r.Hauling && r.haulFrom == HaulPlace.Shore
                && !r.haulPicked && r.Leg == TripLeg.AtPickup;
            if (!want && mineWalk == MineWalk.Under && !(r.Hauling && r.haulFrom == HaulPlace.Shore))
                mineWalk = MineWalk.None;          // re-ordered off the dig: back to the world
            if (want == inMine) return;
            inMine = want;
            if (!bodyHidden) SetRenderersEnabled(!want);
        }
    }
}
