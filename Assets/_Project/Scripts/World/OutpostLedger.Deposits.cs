using UnityEngine;

namespace SeaSick.World
{
    /// **Stone deposits (2026-09-24).** Kevin, phone, after Astra's island
    /// makeover: *"the stone now exists on the island. so include them as a
    /// resource that can be gathered and stored."*
    ///
    /// The rocks on the ground (`StoneDeposit`) are a picture of the camp's
    /// Stone stock, the same way the trees are a picture of `timberTaken`.
    /// The ledger learns exactly one thing from them, once: how big the seam
    /// is (`SizeStoneToDeposits`). Everything after that is the ledger's
    /// ordinary arithmetic -- D2 untouched -- and `GatherSync` draws it.
    ///
    /// Plus the two "not yet" counts the pictures need: units a trip has
    /// booked out of the ground but not cut yet (the rock stays until the
    /// swing is done), and units booked out of the store but not lifted yet
    /// (the stack stays until the man it waits for picks it up).
    public partial class OutpostLedger
    {
        /// Bumped when the one-off seam sizing changes. Saved; an old save
        /// reads 0 and is sized once on its next `CatchUp`.
        ///
        /// **2 (2026-09-27): the island's loose scenery rocks are stone**
        /// (`SceneryStone`). Every save sized at 1 is sized once more, now
        /// with those rocks in the sum -- so a seam worked below them (Kevin's
        /// Island_2: 0 standing) comes back as exactly the rocks he can see,
        /// all of them standing, and an old save loads with nothing gathered.
        /// Still only ever raised.
        public const int StoneDepositsVersion = 2;

        /// Which `StoneDepositsVersion` this camp's Stone stock was last
        /// sized to. See `SizeStoneToDeposits`.
        public int stoneDepositsV;

        /// **Size the Stone seam to the rocks standing on the island, once.**
        ///
        /// `units` is what the island's deposits hold between them (the sum
        /// of `StoneDeposit.Units`). If the seam has LESS standing than that
        /// -- a fresh camp seeded from hectares, or an old save worked down
        /// to almost nothing (Kevin's Island_2 camp: 0.9) -- it becomes
        /// exactly the rocks: `standing = standingMax = units`, every deposit
        /// full. A seam already holding at least that much is left alone (the
        /// rocks are spread over it, `GatherSync`). Stone is only ever
        /// raised, never lowered; the cap may come down to the rocks only
        /// when the stone itself goes up to meet it.
        ///
        /// Marks `stoneDepositsV` and never runs again for this version.
        /// Returns true when it changed the stock. Does nothing (and does
        /// not mark) with no Stone stock or no deposits yet -- the island may
        /// not have been built when this is first asked.
        public bool SizeStoneToDeposits(float units)
        {
            if (stoneDepositsV >= StoneDepositsVersion) return false;
            var s = Stock(Res.Stone);
            if (s == null || units < 1f) return false;
            stoneDepositsV = StoneDepositsVersion;
            if (s.standing >= units) return false;
            s.standing = units;
            s.standingMax = units;
            return true;
        }

        /// Units of `res` a trip has booked out of the FIELD (a gatherer's
        /// armful, a builder quarrying for his site) that the body has not
        /// finished cutting yet (`progress01 < workEnd01`): still part of the
        /// rock on the ground as far as anyone looking is concerned.
        public int UncutFromField(string res)
        {
            if (hands == null || string.IsNullOrEmpty(res)) return 0;
            int n = 0;
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null || !h.Hauling || h.haulFrom != HaulPlace.Field || h.haulRes != res) continue;
                var v = HaulOf(h);
                if (v.progress01 < v.workEnd01) n += Mathf.Max(0, v.count);
            }
            return n;
        }

        /// **What the store's pile physically holds of `res`**: the store's
        /// count plus the units a trip has already booked OUT of it
        /// (`HaulPlace.Store`) that the body has not picked up yet.
        ///
        /// Kevin, phone 2026-09-24: *"they gather it ... and take it to the
        /// hut but nothing is placed."* A builder's armful comes out of the
        /// store when his trip is BOOKED -- the same ledger step a gatherer's
        /// stone lands in whenever a blueprint is waiting for stone -- so the
        /// stack was drawn for no frames at all. Read by `CampPiles` and
        /// `StoreStockView`.
        public int OnStorePile(string res)
        {
            if (string.IsNullOrEmpty(res)) return 0;
            int n = StoreCountOf(res);
            if (hands == null) return n;
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null || !h.Hauling || h.haulFrom != HaulPlace.Store || h.haulRes != res) continue;
                var v = HaulOf(h);
                if (v.progress01 < v.workEnd01) n += Mathf.Max(0, v.count);
            }
            return n;
        }
    }
}
