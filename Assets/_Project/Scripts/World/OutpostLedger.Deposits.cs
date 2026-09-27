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

        /// Units of `res` a trip has taken out of the FIELD that are not cut
        /// yet. **Always 0 since 2026-09-27** (docs/DELIVERY-ON-ARRIVAL.md):
        /// the island gives a load up at the pickup, when the cutting ends,
        /// so the stock already IS what stands on the ground. Kept for callers.
        public int UncutFromField(string res) => 0;

        /// **What the store's pile physically holds of `res`.** Since
        /// 2026-09-27 the store changes only at pickup and drop-off events,
        /// so this is simply the store's count (the loads a walker is on his
        /// way to fetch are still on the pile). Read by `CampPiles` and
        /// `StoreStockView`.
        public int OnStorePile(string res) =>
            string.IsNullOrEmpty(res) ? 0 : StoreCountOf(res);
    }
}
