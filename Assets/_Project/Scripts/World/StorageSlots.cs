using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The container families a store keeps its goods in (2026-10-03).**
    ///
    /// Kevin approved the storage-slot containers preview
    /// (`art-staging/storage-slots-preview/SPEC.md`): every resource a
    /// building holds lives in an authored container SLOT, a slot holds one
    /// bundle of ONE resource, and capacity = visible slots x bundle size.
    /// The order here is the index into every per-family array
    /// (`StorageSlots.HutL1`, `OutpostLedger.storeSlots`, ...); `Count` is
    /// the length, never a family.
    public enum StoreFamily
    {
        Logs = 0,    // log cradle
        Stone = 1,   // stone crib (bays)
        Boards = 2,  // board bearers
        Sacks = 3,   // sacks
        Hang = 4,    // hang beam (wooden pegs)
        Dishes = 5,  // dish shelf
        Gear = 6,    // gear rack
        Count = 7,
    }

    /// **The one table of where a camp's store SHOWS things, 2026-10-03.**
    ///
    /// **Visual only (Kevin, 2026-10-03: "remove storage limits. infinite
    /// stacking is allowed").** The slots no longer cap anything: they fill
    /// at their bundle sizes and, once every slot of a family is full, stay
    /// shown full while the true count keeps rising (the sheets show the
    /// real number). The one thing that still reads them as a limit is an
    /// IDLE hand's store top-up, which stops once the family's visible slots
    /// are full (Kevin's option b, `OutpostLedger.SlotRoomFor`).
    ///
    /// Plain data, read by the ledger's visual arithmetic
    /// (`OutpostLedger.SlotCapacityOf` / `Fill01`), by `Outpost` when it adds up
    /// what stands (`Outpost.StoreSlotsNow`), and by `StorageSlotView` when it
    /// shows which slot holds what. Nothing here is saved: the slot layout is
    /// what is BUILT, pushed into the ledger before every tick exactly as the
    /// old `ceilingPer` was, and which slot holds which resource is DERIVED
    /// from the ledger's counts (`Allocate`), so the two can never disagree.
    ///
    /// **Replaced the uniform ceiling.** Until 2026-10-03 a camp kept
    /// `ceilingPer` of EACH resource (fire 10 + store hut 20 + L2 10), and
    /// `CampPiles` stacked whatever the hut had no rack for on the grass
    /// beside it. Kevin, 2026-10-02: *"this issue of resources just stacking
    /// somewhere in the proximity of the structures is not okay"* -- then
    /// "capacity = visible slots, no stockyard"; and on 2026-10-03 he lifted
    /// the cap altogether. No ground piles still holds.
    public static class StorageSlots
    {
        public const int FamilyCount = (int)StoreFamily.Count;

        // --- the sites (slots per family, in `StoreFamily` order) -----------

        /// **Storage hut, level 1** (SPEC "Storage hut L1 layout contract"):
        /// log cradle 4, stone crib 3 bays, board bearers 3 stacks, sacks 6,
        /// hang beam 6 pegs, dish shelf 4 trays, gear rack 4.
        static readonly int[] HutL1 = { 4, 3, 3, 6, 6, 4, 4 };

        /// **The fire cache** (SPEC "Fire cache"): a pegged groundsheet and a
        /// tripod beside the campfire -- logs 2, stone 1, boards 1, sacks 2,
        /// tripod hooks 2, dish tray 1, gear 1. It is what a camp keeps before
        /// a store hut exists, and it STILL COUNTS afterwards (SPEC); the
        /// store hut fills first (`Allocate` puts the fire's slots last), so
        /// goods live in the hut once one stands -- Kevin 2026-09-23, "once
        /// storage is built everything moves into it".
        static readonly int[] FireCache = { 2, 1, 1, 2, 2, 1, 1 };

        // **No level-2 slots (Kevin DECIDED 2026-10-03):** with infinite
        // stacking a store hut's level 2 is about its runners (4 instead of
        // 2), not room; the earlier "+1 slot a family" was dropped the same
        // day. A level never changes a site's slots (nor does level 3, the
        // runner level the Storehouse merged into, 2026-10-04).
        // (`Techs.StoreBonus` is unread for storage.)

        /// **Add the slots one standing building gives** to `into` (length
        /// `FamilyCount`). Anything that is not a store adds nothing.
        public static void AddSite(int[] into, string planId, int level)
        {
            if (into == null || string.IsNullOrEmpty(planId)) return;
            int[] baseRow = null;
            if (planId == BuildPlans.Storage.id) baseRow = HutL1;
            else if (planId == BuildPlans.Campfire.id) baseRow = FireCache;
            if (baseRow == null) return;
            for (int f = 0; f < FamilyCount; f++) into[f] += baseRow[f];
        }

        /// The fire cache's row on its own: what a camp somebody is actively
        /// building keeps before its fire is raised (`Outpost.StoreSlotsNow`,
        /// the same exception `KeepsOfEach` made for landed rations).
        public static void AddFireCache(int[] into)
        {
            if (into == null) return;
            for (int f = 0; f < FamilyCount; f++) into[f] += FireCache[f];
        }

        /// The slots one building gives, in a fresh array (views, sheets).
        public static int[] SlotsOf(string planId, int level)
        {
            var a = new int[FamilyCount];
            AddSite(a, planId, level);
            return a;
        }

        /// True for a plan that holds store slots.
        public static bool IsStoreSite(string planId) =>
            planId == BuildPlans.Storage.id || planId == BuildPlans.Campfire.id;

        // --- resource -> family + bundle -------------------------------------

        /// **Every storable resource, in family and then in STABLE slot
        /// order.** `Allocate` hands slots out in this order, so a family's
        /// slots fill the same way at every camp and on every load.
        ///
        /// `Res.Game` is deliberately absent: it is the HERD on the island,
        /// counted in the ground, never put in a store (its meat and hide
        /// are). Any id NOT in this table falls back to the gear rack at
        /// `UnknownBundle` with one warning (`FamilyOf`) -- add it here.
        static readonly (string res, StoreFamily fam, int bundle)[] Table =
        {
            // Log cradle: 5 logs a slot (a lashed bundle when 5).
            (Res.Timber,       StoreFamily.Logs,   5),
            // Stone crib: 10 a bay; brick as neat courses.
            (Res.Stone,        StoreFamily.Stone,  10),
            (Res.Ore,          StoreFamily.Stone,  10),
            (Res.Brick,        StoreFamily.Stone,  10),
            // Board bearers: 10 a stack; fine boards paler and thinner.
            (Res.Boards,       StoreFamily.Boards, 10),
            (Res.FineBoards,   StoreFamily.Boards, 10),
            // Sacks: **12 a sack (Kevin DECIDED 2026-10-03, not the SPEC's 8)**
            // -- hut 6 + fire 2 = 96 food across all crops. Fill steps stay
            // slumped / half / full. `Res.Food` is forage (berries, roots).
            (Res.Potato,       StoreFamily.Sacks,  SackBundle),
            (Res.Carrot,       StoreFamily.Sacks,  SackBundle),
            (Res.Onion,        StoreFamily.Sacks,  SackBundle),
            (Res.Wheat,        StoreFamily.Sacks,  SackBundle),
            (Res.Apple,        StoreFamily.Sacks,  SackBundle),
            (Res.Flour,        StoreFamily.Sacks,  SackBundle),
            (Res.Spice,        StoreFamily.Sacks,  SackBundle),
            (Res.Food,         StoreFamily.Sacks,  SackBundle),
            // Hang beam: 4 a peg (Kevin 2026-10-03: pegs stay 4).
            (Res.Fish,         StoreFamily.Hang,   4),
            (Res.Meat,         StoreFamily.Hang,   4),
            (Res.Hide,         StoreFamily.Hang,   4),
            // Dish shelf: **12 a tray, ship's biscuit too** (Kevin 2026-10-03;
            // the SPEC's 4 bowls is superseded).
            (Res.BakedPotato,  StoreFamily.Dishes, DishBundle),
            (Res.GrilledFish,  StoreFamily.Dishes, DishBundle),
            (Res.GrilledMeat,  StoreFamily.Dishes, DishBundle),
            (Res.RoastCarrots, StoreFamily.Dishes, DishBundle),
            (Res.Bread,        StoreFamily.Dishes, DishBundle),
            (Res.VegStew,      StoreFamily.Dishes, DishBundle),
            (Res.FishPie,      StoreFamily.Dishes, DishBundle),
            (Res.HuntersStew,  StoreFamily.Dishes, DishBundle),
            (Res.Meals,        StoreFamily.Dishes, DishBundle),
            // Gear rack: per-item counts (SPEC). Iron is ingots on the rack.
            (Res.Arrows,       StoreFamily.Gear,   12),
            (Res.Spear,        StoreFamily.Gear,   4),
            (Res.IronSpear,    StoreFamily.Gear,   4),
            (Res.Bow,          StoreFamily.Gear,   4),
            (Res.Tools,        StoreFamily.Gear,   4),
            (Res.SawBlade,     StoreFamily.Gear,   2),
            (Res.Iron,         StoreFamily.Gear,   6),
        };

        /// Units in one sack (Kevin DECIDED 2026-10-03).
        public const int SackBundle = 12;

        /// Units on one dish tray, ship's biscuit included (Kevin DECIDED
        /// 2026-10-03).
        public const int DishBundle = 12;

        /// Bundle for an id the table does not know (gear rack fallback).
        const int UnknownBundle = 4;

        static Dictionary<string, int> index;
        static readonly HashSet<string> warnedUnknown = new HashSet<string>();
        /// Per family: the table rows of that family, in slot order.
        static int[][] membersOf;

        static void Build()
        {
            if (index != null) return;
            index = new Dictionary<string, int>(Table.Length);
            var lists = new List<int>[FamilyCount];
            for (int f = 0; f < FamilyCount; f++) lists[f] = new List<int>();
            for (int i = 0; i < Table.Length; i++)
            {
                index[Table[i].res] = i;
                lists[(int)Table[i].fam].Add(i);
            }
            membersOf = new int[FamilyCount][];
            for (int f = 0; f < FamilyCount; f++) membersOf[f] = lists[f].ToArray();
        }

        /// Which family keeps `res`. An unknown id goes on the gear rack,
        /// warned once -- never refused outright, which would silently eat
        /// a new resource's every delivery.
        public static StoreFamily FamilyOf(string res)
        {
            Build();
            if (!string.IsNullOrEmpty(res) && index.TryGetValue(res, out int i)) return Table[i].fam;
            if (!string.IsNullOrEmpty(res) && warnedUnknown.Add(res))
                Debug.LogWarning($"[StorageSlots] '{res}' has no container family; kept on the gear rack. Add it to StorageSlots.Table.");
            return StoreFamily.Gear;
        }

        /// Units of `res` one slot holds.
        public static int BundleOf(string res)
        {
            Build();
            return !string.IsNullOrEmpty(res) && index.TryGetValue(res, out int i) ? Table[i].bundle : UnknownBundle;
        }

        /// Slots `n` units of `res` take: whole bundles, a part bundle is a
        /// whole slot (a slot holds ONE resource).
        public static int SlotsFor(string res, int n) =>
            n <= 0 ? 0 : (n + BundleOf(res) - 1) / BundleOf(res);

        /// The resources of family `f`, in slot order (allocation-free view).
        public static int MemberCount(StoreFamily f) { Build(); return membersOf[(int)f].Length; }
        public static string Member(StoreFamily f, int k) { Build(); return Table[membersOf[(int)f][k]].res; }

        /// Every resource the table knows, in table order.
        public static int ResourceCount => Table.Length;
        public static string ResourceAt(int i) => Table[i].res;

        // --- naming contract for the art -------------------------------------

        /// **The canonical family word in an anchor's name**,
        /// `Stock_<Family>_NN` -- the words the preview's containers.py
        /// already writes (`Stock_Timber_01`, `Stock_Sack_03`, ...).
        public static string AnchorWord(StoreFamily f)
        {
            switch (f)
            {
                case StoreFamily.Logs: return "Timber";
                case StoreFamily.Stone: return "Stone";
                case StoreFamily.Boards: return "Boards";
                case StoreFamily.Sacks: return "Sack";
                case StoreFamily.Hang: return "Hang";
                case StoreFamily.Dishes: return "Dish";
                case StoreFamily.Gear: return "Gear";
            }
            return "?";
        }

        /// **Family words the view also accepts** (2026-10-03): the art is
        /// being prepared in parallel and the brief has used both the
        /// preview's short words and the container names ("HangBeam",
        /// "StoneCrib", "Sacks"). Either reads; null = no family.
        public static bool TryFamilyFromWord(string word, out StoreFamily f)
        {
            switch (word)
            {
                case "Timber": case "Logs": case "LogCradle": f = StoreFamily.Logs; return true;
                case "Stone": case "StoneCrib": f = StoreFamily.Stone; return true;
                case "Boards": case "BoardBearers": f = StoreFamily.Boards; return true;
                case "Sack": case "Sacks": f = StoreFamily.Sacks; return true;
                case "Hang": case "HangBeam": f = StoreFamily.Hang; return true;
                case "Dish": case "Dishes": case "DishShelf": f = StoreFamily.Dishes; return true;
                case "Gear": case "GearRack": f = StoreFamily.Gear; return true;
            }
            f = StoreFamily.Count;
            return false;
        }

        // --- which slot holds what -------------------------------------------

        /// **Hand out a family's slots to its resources, deterministically
        /// from the counts.** Resources go in table order, each taking
        /// `SlotsFor(count)` slots; slot `k` of the result holds `res[k]`
        /// with `units[k]` units (a full bundle, or the remainder in the
        /// resource's LAST slot). `count(res)` is what to place (the view
        /// passes `OnStorePile`). Returns slots used (<= `slots`).
        ///
        /// **Past the slots (infinite stacking, Kevin 2026-10-03):** the store
        /// keeps any count; here the slots simply all show full and what
        /// does not fit is not drawn. Nothing is refused or deleted.
        ///
        /// A slot's resource can change when an EARLIER resource of the
        /// family grows a slot (the sacks shuffle along by one). Accepted for
        /// now: it is what keeps this stateless, so it can never disagree
        /// with the books.
        public static int Allocate(StoreFamily f, int slots, System.Func<string, int> count,
            string[] res, int[] units)
        {
            Build();
            int used = 0;
            var members = membersOf[(int)f];
            for (int m = 0; m < members.Length && used < slots; m++)
            {
                var row = Table[members[m]];
                int n = count(row.res);
                while (n > 0 && used < slots)
                {
                    int put = Mathf.Min(n, row.bundle);
                    if (res != null && used < res.Length) res[used] = row.res;
                    if (units != null && used < units.Length) units[used] = put;
                    n -= put;
                    used++;
                }
            }
            for (int k = used; k < slots; k++)
            {
                if (res != null && k < res.Length) res[k] = null;
                if (units != null && k < units.Length) units[k] = 0;
            }
            return used;
        }
    }
}
