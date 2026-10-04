using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **What a driven-off kraken leaves on the water** (GDD §6 "Loot": a big
    /// haul of kraken meat for the island, plus one rare trophy, kraken ink).
    ///
    /// No new pickup. The haul is a few `FloatingCargo` crates -- the very
    /// thing a crate lost over the side becomes -- so it rides the real swell,
    /// shows on `RescueHud`'s ring and edge arrow, is climbed aboard by
    /// sailing within the board reach of it (or fetched by the jolly boat,
    /// `JollyBoatDispatch`), and lands in the hold through
    /// `VoyageManager.ReturnCargo`, which never drops a unit even into a full
    /// hold. Left alone it sinks after `OverboardTuning.FloatSeconds`
    /// (120 s), the same clock as any lost cargo, and a save while it floats
    /// pulls it straight back aboard (`FloatingCargo.RecallAllForSave`).
    ///
    /// The meat is the ordinary `Res.Meat` (it feeds the island like any
    /// hunt); the ink is `Res.KrakenInk`, a new raw resource with no use
    /// yet ("use decided later").
    public static class KrakenLoot
    {
        /// Crates are spread on a ring this far round the point, metres: a
        /// little scatter, all within the one spot the ship sails to.
        const float RingMin = 5f, RingMax = 12f;

        /// **Drop the haul at `at`** (flat position; the swell sets the
        /// height). Called by `Kraken` when it is driven off. Safe to call
        /// with no ship in the scene.
        public static void Drop(Vector3 at)
        {
            var motor = Object.FindAnyObjectByType<ShipMotor>();
            Transform hull = motor != null ? motor.transform : null;
            at.y = 0f;

            int crates = Mathf.Max(1, Mathf.RoundToInt(KrakenSpawnTuning.lootMeatCrates));
            int meat = Mathf.Max(1, Mathf.RoundToInt(KrakenSpawnTuning.lootMeatUnits));
            int perCrate = Mathf.Max(1, Mathf.CeilToInt(meat / (float)crates));

            float a0 = Random.Range(0f, Mathf.PI * 2f);
            int left = meat;
            for (int i = 0; i < crates && left > 0; i++)
            {
                int n = Mathf.Min(perCrate, left);
                left -= n;
                // Even spread round the ring, one slot kept for the ink.
                float a = a0 + (i + 1) * (Mathf.PI * 2f / (crates + 1));
                var meatCrate = FloatingCargo.Spawn(Res.Meat, n, hull, At(at, a), KrakenSpawnTuning.lootMeatSpan);
                if (meatCrate != null) meatCrate.HarpoonKind = "loot";
            }

            // The trophy, in the slot the meat left open.
            int ink = Mathf.Max(1, Mathf.RoundToInt(KrakenSpawnTuning.lootInkUnits));
            var inkCrate = FloatingCargo.Spawn(Res.KrakenInk, ink, hull, At(at, a0), KrakenSpawnTuning.lootInkSpan);
            if (inkCrate != null) inkCrate.HarpoonKind = "loot";

            Banner.Show("The kraken is gone. It left a haul on the water.", 5f);
        }

        static Vector3 At(Vector3 centre, float angle)
        {
            float r = Random.Range(RingMin, RingMax);
            return centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
        }
    }
}
