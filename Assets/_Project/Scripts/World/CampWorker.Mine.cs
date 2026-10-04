using UnityEngine;

namespace SeaSick.World
{
    /// **The miner goes into the hill (2026-10-05).** Kevin's spec: he walks
    /// to the mouth, disappears, is underground for the trip's minute, and
    /// comes back out carrying his four stone. The books already have all of
    /// it -- a mine trip is a catch trip from the mouth (`OutpostLedger.Mines`,
    /// `HaulPlace.Shore`) whose `AtPickup` leg is the time underground -- so
    /// the body only has to stop being drawn for exactly that leg.
    ///
    /// Its own flag, NOT `bodyHidden`: that one belongs to the raid alarm and
    /// sleep, and `TickAlarmRole` reveals any `bodyHidden` body that is not
    /// asleep every frame. This one is re-derived from the books every frame
    /// (`TickMineHide`, the top of `Update`), so a load, a watch starting
    /// mid-trip, an alarm or a re-order can never leave him invisible.
    public partial class CampWorker
    {
        bool inMine;

        /// The mine's `Mouth` (model or stand-in) for mine station `station`,
        /// or false when that station is not a mine / not standing.
        bool MineAt(int station, out Vector3 mouth)
        {
            mouth = default;
            var l = camp != null ? camp.Ledger : null;
            if (l == null || !OutpostLedger.Mines(l.StationAt(station))) return false;
            var b = StationBuilding(station);
            if (b != null)
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                    if (BuildingFactory.Stem(t.name) == "Mouth") { mouth = t.position; return true; }
            var row = l.StationRow(station);
            if (row == null) return false;
            OutpostLedger.MouthOf(row, out _, out mouth);
            mouth.y = camp.GroundAt(mouth);
            return true;
        }

        /// Hidden while (and only while) he is underground: on a trip from a
        /// mine's mouth, at the pickup, the stone not yet in his arms. The
        /// alarm's and sleep's own hiding (`bodyHidden`) is left alone.
        void TickMineHide(OutpostHand r)
        {
            bool want = false;
            if (r.Hauling && r.haulFrom == HaulPlace.Shore && !r.haulPicked && r.Leg == TripLeg.AtPickup)
            {
                var l = camp.Ledger;
                want = l != null && OutpostLedger.Mines(l.StationAt(r.haulFromStation));
            }
            if (want == inMine) return;
            inMine = want;
            if (!bodyHidden) SetRenderersEnabled(!want);
        }
    }
}
