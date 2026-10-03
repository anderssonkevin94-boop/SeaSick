using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Shows the camp store in its authored container slots (2026-10-03).**
    ///
    /// Kevin approved the storage-slot containers preview
    /// (`art-staging/storage-slots-preview/SPEC.md`): every resource a
    /// building holds lives in a slot, a slot holds one bundle of ONE
    /// resource and shows fill steps, capacity = visible slots x bundle. No
    /// ground piles (the `CampPiles` ring and hut-side stacks are retired).
    ///
    /// **The naming contract the FBX follows** (art in parallel; the two
    /// building roots are `StorageHutL1` and `FireCache`):
    /// <list type="bullet">
    /// <item>An ANCHOR per slot: `Stock_<Family>_NN`, NN from 01, in slot
    /// order. Family words: Timber, Stone, Boards, Sack, Hang, Dish, Gear
    /// (the preview's containers.py), or the long forms LogCradle,
    /// StoneCrib, BoardBearers, Sacks, HangBeam, DishShelf, GearRack
    /// (`StorageSlots.TryFamilyFromWord`). A Blender `.001` suffix is
    /// ignored.</item>
    /// <item>Under each anchor, the fill steps: `Fill_1..Fill_N`, CUMULATIVE
    /// -- exactly one step is shown (Fill_3 is the whole slot at three
    /// steps of fill, not the third item). Where a resource looks different
    /// within a family (Fish/Meat/Hide on the hang beam, the crop top on a
    /// sack, Brick courses in the crib) a per-resource set
    /// `Fill_<Res>_1..N` (`Fill_Fish_3`, `Res` = the ledger id) is
    /// preferred; the generic set is the fallback. N may differ per set
    /// (a log cradle 5, sacks 3: slumped / half / full).</item>
    /// <item>An anchor with no `Fill_` children is NOT a slot to this view
    /// -- that is what keeps it off Astra's old storage kit, whose
    /// `Stock_Timber_NN` / `Stock_Boards_NN` meshes `StoreStockView` still
    /// toggles until the new hut replaces it.</item>
    /// <item>The fire cache must be parented under the CAMPFIRE building's
    /// root, the hut's anchors under the store hut's: the view finds its
    /// site from the `Building` it sits on.</item>
    /// </list>
    ///
    /// **Which slot shows what** is `StorageSlots.Allocate` over the WHOLE
    /// camp's slots of a family -- store huts and storehouses first, in raise
    /// order, then the fire cache (the hut fills first, the fire keeps the
    /// overflow) -- so a view only shows its own window of that list. It is
    /// derived from the ledger every refresh, never saved, so it cannot
    /// disagree with the books; an over-capacity old save shows every slot
    /// full and the rest is simply not drawn.
    ///
    /// **Cheap.** Anchors are found once (`Start`); a model with none
    /// disables the component and it never ticks or logs. Otherwise it
    /// refreshes on a slow timer, recomputes into preallocated arrays, and
    /// only touches a GameObject whose shown step changed: no per-frame
    /// garbage.
    public class StorageSlotView : MonoBehaviour
    {
        /// Seconds between refreshes. Goods move at walking pace.
        const float RefreshSeconds = 0.3f;

        /// One authored slot.
        sealed class Slot
        {
            public int number;                  // NN off the name
            public readonly List<GameObject> generic = new List<GameObject>();   // Fill_k at [k-1]
            public Dictionary<string, List<GameObject>> perRes;                  // Fill_<Res>_k
            public string shownRes;
            public int shownStep = -1;
        }

        readonly List<Slot>[] slots = new List<Slot>[StorageSlots.FamilyCount];
        bool any;

        Outpost outpost;
        Building building;
        float clock;
        int resolveTries;

        // Allocation scratch, grown only when a family gains slots.
        string[] allocRes = new string[0];
        int[] allocUnits = new int[0];
        System.Func<string, int> countFn;

        void Start()
        {
            Discover();
            if (!any) { enabled = false; return; }
            countFn = r => outpost != null && outpost.Ledger != null ? outpost.Ledger.OnStorePile(r) : 0;
            clock = RefreshSeconds;   // first refresh on the first Update
        }

        void Update()
        {
            clock += Time.deltaTime;
            if (clock < RefreshSeconds) return;
            clock = 0f;
            if (outpost == null || building == null)
            {
                // Resolved lazily and retried slowly: a building raised or
                // loaded this frame may not be parented under its outpost yet.
                if (resolveTries > 200) return;
                resolveTries++;
                outpost = GetComponentInParent<Outpost>();
                building = GetComponentInParent<Building>();
                if (outpost == null || building == null) return;
            }
            var l = outpost.Ledger;
            if (l == null || l.storeSlots == null) return;
            Refresh(l);
        }

        void Refresh(OutpostLedger l)
        {
            for (int f = 0; f < StorageSlots.FamilyCount; f++)
            {
                var mine = slots[f];
                if (mine == null || mine.Count == 0) continue;
                var fam = (StoreFamily)f;
                int total = l.SlotsOf(fam);
                int offset = OffsetOf(fam, out int ownSlots);
                if (total > allocRes.Length) { allocRes = new string[total]; allocUnits = new int[total]; }
                StorageSlots.Allocate(fam, total, countFn, allocRes, allocUnits);
                for (int i = 0; i < mine.Count; i++)
                {
                    int g = offset + i;
                    bool live = i < ownSlots && g < total;
                    string res = live ? allocRes[g] : null;
                    int units = live ? allocUnits[g] : 0;
                    Show(mine[i], res, units);
                }
            }
        }

        /// Where this building's slots of `fam` start in the camp-wide list,
        /// and how many it has by the table (`StorageSlots.SlotsOf`). Store
        /// huts and storehouses first in `Outpost.Built` order, then the fire
        /// cache -- the same sites `Outpost.StoreSlotsNow` sums.
        int OffsetOf(StoreFamily fam, out int own)
        {
            own = 0;
            int before = 0;
            var built = outpost.Built;
            // The stores first (store huts, storehouses), in raise order.
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null || b.Id == BuildPlans.Campfire.id || !StorageSlots.IsStoreSite(b.Id)) continue;
                int n = SlotsOfSite(b, fam);
                if (b == building) { own = n; return before; }
                before += n;
            }
            if (building.Id != BuildPlans.Campfire.id) return 0;
            // Then the fire cache(s): the overflow once a store stands.
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null || b.Id != BuildPlans.Campfire.id) continue;
                int n = SlotsOfSite(b, fam);
                if (b == building) { own = n; return before; }
                before += n;
            }
            return before;
        }

        /// One site's slots of `fam`, into a reused array (no garbage).
        int SlotsOfSite(Building b, StoreFamily fam)
        {
            System.Array.Clear(scratch, 0, scratch.Length);
            StorageSlots.AddSite(scratch, b.Id, outpost.LevelOfBuilding(b));
            return scratch[(int)fam];
        }

        readonly int[] scratch = new int[StorageSlots.FamilyCount];

        /// Show one slot: `units` of `res` (0 = empty). Touches the scene
        /// only when the shown resource or step changes.
        static void Show(Slot s, string res, int units)
        {
            List<GameObject> set = null;
            if (units > 0 && res != null)
            {
                if (s.perRes == null || !s.perRes.TryGetValue(res, out set) || set.Count == 0)
                    set = s.generic;
            }
            int steps = set != null ? set.Count : 0;
            int step = 0;
            if (steps > 0 && units > 0)
            {
                int bundle = Mathf.Max(1, StorageSlots.BundleOf(res));
                // Ceil: one unit shows the first step, a full bundle the last.
                step = Mathf.Clamp((units * steps + bundle - 1) / bundle, 1, steps);
            }
            string shownRes = step > 0 ? res : null;
            if (step == s.shownStep && shownRes == s.shownRes) return;
            s.shownStep = step;
            s.shownRes = shownRes;
            SetAll(s.generic, set == s.generic ? step : 0);
            if (s.perRes != null)
                foreach (var kv in s.perRes) SetAll(kv.Value, set == kv.Value ? step : 0);
        }

        /// Exactly `Fill_step` on (cumulative steps), the rest off.
        static void SetAll(List<GameObject> set, int step)
        {
            for (int k = 0; k < set.Count; k++)
            {
                var go = set[k];
                if (go == null) continue;
                bool on = k == step - 1;
                if (go.activeSelf != on) go.SetActive(on);
            }
        }

        // --- discovery --------------------------------------------------------

        void Discover()
        {
            any = false;
            for (int f = 0; f < slots.Length; f++) slots[f] = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!TryAnchor(BuildingFactory.Stem(t.name), out var fam, out int number)) continue;
                var s = new Slot { number = number };
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                {
                    if (c == t) continue;
                    AddFill(s, BuildingFactory.Stem(c.name), c.gameObject);
                }
                if (s.generic.Count == 0 && s.perRes == null) continue;   // not a slot (old kit)
                Compact(s.generic);
                if (s.perRes != null) foreach (var kv in s.perRes) Compact(kv.Value);
                // Everything starts hidden; the first refresh shows the truth.
                SetAll(s.generic, 0);
                if (s.perRes != null) foreach (var kv in s.perRes) SetAll(kv.Value, 0);
                s.shownStep = 0;
                (slots[(int)fam] ??= new List<Slot>()).Add(s);
                any = true;
            }
            for (int f = 0; f < slots.Length; f++)
                slots[f]?.Sort((a, b) => a.number.CompareTo(b.number));
        }

        /// `Stock_<Family>_NN` -> family and NN.
        static bool TryAnchor(string stem, out StoreFamily fam, out int number)
        {
            fam = StoreFamily.Count;
            number = 0;
            if (!stem.StartsWith("Stock_", System.StringComparison.Ordinal)) return false;
            int us = stem.LastIndexOf('_');
            if (us <= 6 || !int.TryParse(stem.Substring(us + 1), out number)) return false;
            return StorageSlots.TryFamilyFromWord(stem.Substring(6, us - 6), out fam);
        }

        /// `Fill_k` (generic) or `Fill_<Res>_k` (per resource), 1-based.
        static void AddFill(Slot s, string stem, GameObject go)
        {
            if (!stem.StartsWith("Fill_", System.StringComparison.Ordinal)) return;
            string rest = stem.Substring(5);
            int us = rest.LastIndexOf('_');
            string res = us > 0 ? rest.Substring(0, us) : null;
            if (!int.TryParse(us > 0 ? rest.Substring(us + 1) : rest, out int k) || k < 1 || k > 64) return;
            List<GameObject> into;
            if (res == null) into = s.generic;
            else
            {
                s.perRes ??= new Dictionary<string, List<GameObject>>();
                if (!s.perRes.TryGetValue(res, out into)) s.perRes[res] = into = new List<GameObject>();
            }
            while (into.Count < k) into.Add(null);
            into[k - 1] = go;
        }

        /// A missing step (Fill_1, Fill_3, no Fill_2) is tolerated: drop the
        /// holes so N is the number of steps actually authored.
        static void Compact(List<GameObject> set) => set.RemoveAll(g => g == null);
    }
}
