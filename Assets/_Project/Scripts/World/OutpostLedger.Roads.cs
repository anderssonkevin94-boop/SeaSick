using System.Collections.Generic;

namespace SeaSick.World
{
    /// **Worn roads, remembered (2026-09-26, `CampRoads`).** Sparse: only
    /// cells with some wear. `roadCell` is the WORLD 2 m lattice cell
    /// (`CampRoads.Pack`), so the save does not depend on where the camp's
    /// grid happened to be centred. `roadWear` is metres walked through the
    /// cell (decayed); NEGATIVE means the cell is currently drawn as road,
    /// which is the hysteresis state -- without it a cell sitting between
    /// the on and off thresholds would come back as grass after a load.
    public partial class OutpostLedger
    {
        public List<int> roadCell = new List<int>();
        public List<float> roadWear = new List<float>();
    }
}
