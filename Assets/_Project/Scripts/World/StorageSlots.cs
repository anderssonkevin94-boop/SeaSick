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

    /// **The one table of where a camp's store can put things, 2026-10-03.**
    ///
    /// Plain data, read by the ledger's capacity arithmetic
    /// (`OutpostLedger.CapacityOf` / `RoomFor`), by `Outpost` when it adds up
    /// what stands (`Outpost.StoreSlotsNow`), and by `StorageSlotView` when it
    /// shows which slot holds what. Nothing here is saved: the slot layout is
    /// what is BUILT, pushed into the ledger before every tick exactly as the
    /// old `ceilingPer` was, and which slot holds which resource is DERIVED
    /// from the ledger's counts (`Allocate`), so the two can never disagree.
    ///
    /// **Replaces the uniform ceiling.** Until 2026-10-03 a camp kept
    /// `ceilingPer` of EACH resource (fire 10 + store hut 20 + L2 10), and
    /// `CampPiles` stacked whatever the hut had no rack for on the grass
    /// beside it. Kevin, 2026-10-02: *"this issue of resources just stacking
    /// somewhere in the proximity of the structures is not okay"* -- then
    /// "capacity = visible slots, no stockyard". Full = full.
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

        /// **Storehouse (the big one)**: no art, no SPEC layout yet. It kept
        /// 40 of each (twice the hut's 20), so it is given twice the hut's
        /// slots until its own containers are designed. PROVISIONAL -- for
        /// Kevin to confirm (2026-10-03).
        static readonly int[] Storehouse = { 8, 6, 6, 12, 12, 8, 8 };

        /// **Store hut level 2 = extra slots, in CODE only (Kevin DECIDED
        /// 2026-10-03):** keep the current L2 model, no new L2 art; the level
        /// adds this many slots to EVERY family. Replaces the old flat
        /// `storeBonus` of +10 of each (`Techs` Storage L2 row, now unread).
        public const int HutLevel2ExtraSlotsPerFamily = 1;

        /// The storehouse's level 2 (was +20 of each, twice the hut's +10):
        /// twice the hut's extra. PROVISIONAL, same as `Storehouse`.
        public const int StorehouseLevel2ExtraSlotsPerFamily = 2;

        /// **Add the slots one standing building gives** to `into` (length
        /// `FamilyCount`). Anything that is not a store adds nothing.
        public static void AddSite(int[] into, string planId, int level)
        {
            if (into == null || string.IsNullOrEmpty(planId)) return;
            int[] baseRow = null;
            int perLevel = 0;
            if (planId == BuildPlans.Storage.id) { baseRow = HutL1; perLevel = HutLevel2ExtraSlotsPerFamily; }
            else if (planId == BuildPlans.Storehouse.id) { baseRow = Storehouse; perLevel = StorehouseLevel2ExtraSlotsPerFamily; }
            else if (planId == BuildPlans.Campfire.id) baseRow = FireCache;
            if (baseRow == null) return;
            int extra = perLevel * Mathf.Max(0, level - 1);
            for (int f = 0; f < FamilyCount; f++) into[f] += baseRow[f] + extra;
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
            planId == BuildPlans.Storage.id || planId == BuildPlans.Storehouse.id
            || planId == BuildPlans.Campfire.id;

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
            // Dish shelf: 4 bowls a tray; ship's biscuit (`Meals`) too.
            (Res.BakedPotato,  StoreFamily.Dishes, 4),
            (Res.GrilledFish,  StoreFamily.Dishes, 4),
            (Res.GrilledMeat,  StoreFamily.Dishes, 4),
            (Res.RoastCarrots, StoreFamily.Dishes, 4),
            (Res.Bread,        StoreFamily.Dishes, 4),
            (Res.VegStew,      StoreFamily.Dishes, 4),
            (Res.FishPie,      StoreFamily.Dishes, 4),
            (Res.HuntersStew,  StoreFamily.Dishes, 4),
            (Res.Meals,        StoreFamily.Dishes, 4),
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
        /// **Over capacity (Kevin DECIDED 2026-10-03):** a save written under
        /// the old per-resource ceiling may hold more than its slots fit.
        /// NOTHING is deleted: the ledger keeps every unit; here the slots
        /// simply all show full and what does not fit is not drawn, and
        /// `OutpostLedger.RoomFor` accepts nothing more of the family until
        /// it falls back under capacity.
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
