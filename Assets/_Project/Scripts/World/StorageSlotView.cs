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
    /// <item>**The KIT path (2026-10-03, the shipping one):** the building
    /// FBX's anchors are EMPTY and the steps come from the shared fill kit
    /// `Resources/Kits/StorageL1/StorageFillKit` (.fbx) -- top-level
    /// children `<Family>__Fill_<k>` / `<Family>__Fill_<Res>_<k>`, Family
    /// the long name (LogCradle, BoardBearers, StoneCrib, Sacks, HangBeam,
    /// DishShelf, GearRack); anchors under the `FireCache` root look up
    /// `Fire<Family>__...` first and fall back to the plain set. A step is
    /// instantiated WITH its children (the food models inside a sack step)
    /// under the anchor on first need, at an identity local transform. The baked
    /// `Fill_` children above are the fallback and win when present. With
    /// no kit imported an empty anchor is not a slot -- which also keeps the
    /// view off Astra's old storage kit (`StoreStockView`'s).</item>
    /// <item>Blender's unique suffixes (`.001`) are stripped from every
    /// name before matching.</item>
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

        /// One authored slot. The step lists are either the anchor's own
        /// baked `Fill_` children (scene objects) or, on the kit path, the
        /// SHARED kit templates for its family (`Kit`), instantiated under
        /// the anchor lazily into `made` the first time a step is needed.
        sealed class Slot
        {
            public int number;                  // NN off the name
            public Transform anchor;
            public List<GameObject> generic = new List<GameObject>();   // Fill_k at [k-1]
            public Dictionary<string, List<GameObject>> perRes;          // Fill_<Res>_k
            public bool kit;
            public Dictionary<GameObject, GameObject> made;              // kit template -> instance
            public GameObject shown;            // the one visible step, or null
            public string shownRes;
            public int shownStep = -1;
        }

        // --- the fill kit (2026-10-03) ----------------------------------------
        //
        // Baking every fill step into the hut FBX cost ~163k tris a hut, of
        // which at most ~28k are ever visible. So the building FBXs carry only
        // the structure and EMPTY `Stock_<Family>_NN` anchors, and one kit
        // FBX (`Resources/Kits/StorageL1/StorageFillKit`) holds ONE copy of
        // each step as a top-level child named `<Family>__Fill_<k>` or
        // `<Family>__Fill_<Res>_<k>` (`Sacks__Fill_Potato_2`,
        // `StoneCrib__Fill_Brick_3`, `GearRack__Fill_Spear_4`), authored in
        // the slot's local space: an instance sits under its anchor at an
        // identity local transform. Loaded once per session, shared by every
        // view; an instance shares the template's mesh and materials.

        const string KitPath = "Kits/StorageL1/StorageFillKit";

        sealed class KitSets
        {
            public readonly List<GameObject> generic = new List<GameObject>();
            public Dictionary<string, List<GameObject>> perRes;
        }

        /// Plain `<Family>__` sets and the fire cache's own `Fire<Family>__`
        /// sets (FireLogCradle, FireBoardBearers, FireStoneCrib, FireGearRack
        /// exist; Sacks, HangBeam, DishShelf have none), by family.
        static KitSets[] kit, kitFire;
        static bool kitTried, kitWarned;

        /// The kit steps for a slot of `f`: under the FireCache root the
        /// `Fire<Family>__` set first, else (and as the fallback) the plain
        /// `<Family>__` set. Null = no kit or no set for the family.
        static KitSets KitFor(StoreFamily f, bool fire)
        {
            LoadKit();
            if (kit == null) return null;
            if (fire && kitFire[(int)f] != null) return kitFire[(int)f];
            return kit[(int)f];
        }

        static void LoadKit()
        {
            if (!kitTried)
            {
                kitTried = true;
                var root = Resources.Load<GameObject>(KitPath);
                if (root != null)
                {
                    kit = new KitSets[StorageSlots.FamilyCount];
                    kitFire = new KitSets[StorageSlots.FamilyCount];
                    var t = root.transform;
                    // Top-level children only: a step's own children (the
                    // food models inside a sack step) come with it when it
                    // is instantiated, they are not steps themselves.
                    for (int i = 0; i < t.childCount; i++)
                    {
                        var c = t.GetChild(i);
                        string stem = StripSuffix(c.name);
                        int sep = stem.IndexOf("__", System.StringComparison.Ordinal);
                        if (sep <= 0) continue;
                        string word = stem.Substring(0, sep);
                        var table = kit;
                        if (!StorageSlots.TryFamilyFromWord(word, out var fam))
                        {
                            if (!word.StartsWith("Fire", System.StringComparison.Ordinal)
                                || !StorageSlots.TryFamilyFromWord(word.Substring(4), out fam)) continue;
                            table = kitFire;
                        }
                        var sets = table[(int)fam] ??= new KitSets();
                        AddFill(sets.generic, ref sets.perRes, stem.Substring(sep + 2), c.gameObject);
                    }
                    foreach (var table in new[] { kit, kitFire })
                        foreach (var k in table)
                        {
                            if (k == null) continue;
                            Compact(k.generic);
                            if (k.perRes != null) foreach (var kv in k.perRes) Compact(kv.Value);
                        }
                }
            }
        }

        /// Blender's unique suffix (`.001`) off a name.
        static readonly System.Text.RegularExpressions.Regex BlenderSuffix =
            new System.Text.RegularExpressions.Regex(@"\.\d+$");
        static string StripSuffix(string name) => BlenderSuffix.Replace(name, "");

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
        /// only when the shown resource or step changes; exactly one step is
        /// ever visible. Per-resource set first, the family's generic set as
        /// the fallback.
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
            GameObject want = step > 0 ? set[step - 1] : null;
            if (want != null && s.kit) want = Instance(s, want);
            if (s.shown == want) return;
            if (s.shown != null && s.shown.activeSelf) s.shown.SetActive(false);
            s.shown = want;
            if (want != null && !want.activeSelf) want.SetActive(true);
        }

        /// The anchor's own copy of a kit step, made the first time it is
        /// needed and kept (hidden) for reuse -- never per frame.
        static GameObject Instance(Slot s, GameObject template)
        {
            s.made ??= new Dictionary<GameObject, GameObject>();
            if (s.made.TryGetValue(template, out var go) && go != null) return go;
            go = Object.Instantiate(template, s.anchor, false);
            go.name = template.name;
            go.SetActive(false);
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            s.made[template] = go;
            return go;
        }

        /// Hide every baked step (the first refresh shows the truth).
        static void HideAll(List<GameObject> set)
        {
            for (int k = 0; k < set.Count; k++)
                if (set[k] != null && set[k].activeSelf) set[k].SetActive(false);
        }

        // --- discovery --------------------------------------------------------

        void Discover()
        {
            any = false;
            for (int f = 0; f < slots.Length; f++) slots[f] = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!TryAnchor(StripSuffix(t.name), out var fam, out int number)) continue;
                var s = new Slot { number = number, anchor = t };
                // Baked path first (the fallback): the anchor's own Fill_ children.
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                {
                    if (c == t) continue;
                    AddFill(s.generic, ref s.perRes, StripSuffix(c.name), c.gameObject);
                }
                Compact(s.generic);
                if (s.perRes != null) foreach (var kv in s.perRes) Compact(kv.Value);
                if (s.generic.Count > 0 || s.perRes != null)
                {
                    HideAll(s.generic);
                    if (s.perRes != null) foreach (var kv in s.perRes) HideAll(kv.Value);
                }
                else
                {
                    // Kit path: an empty anchor takes its family's kit steps.
                    // No kit (not imported yet) = not a slot. An empty
                    // `Stock_` with neither is also how Astra's OLD storage
                    // kit looks to this view, so it stays StoreStockView's.
                    var sets = KitFor(fam, UnderFireCache(t));
                    if (sets == null)
                    {
                        if (kit == null && !kitWarned && t.childCount == 0 && IsNewSite())
                        {
                            kitWarned = true;
                            Debug.LogWarning($"[StorageSlotView] empty slot anchors but no fill kit at Resources/{KitPath}; the store is not drawn.");
                        }
                        continue;
                    }
                    s.kit = true;
                    s.generic = sets.generic;
                    s.perRes = sets.perRes;
                }
                s.shownStep = 0;
                (slots[(int)fam] ??= new List<Slot>()).Add(s);
                any = true;
            }
            for (int f = 0; f < slots.Length; f++)
                slots[f]?.Sort((a, b) => a.number.CompareTo(b.number));
        }

        /// Is this anchor under the `FireCache` root (the fire's own kit
        /// variants, `Fire<Family>__`)?
        static bool UnderFireCache(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (StripSuffix(p.name) == "FireCache") return true;
            return false;
        }

        /// True on the new slot buildings (`StorageHutL1`, `FireCache` roots
        /// in the model), so the one missing-kit warning never fires for the
        /// old Astra storage kit's `Stock_Timber_NN` meshes.
        bool IsNewSite()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = StripSuffix(t.name);
                if (n == "StorageHutL1" || n == "FireCache") return true;
            }
            return false;
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

        /// `Fill_k` (generic) or `Fill_<Res>_k` (per resource), 1-based,
        /// into a step list (`Fill_Brick_3`, never "FillBrick").
        static void AddFill(List<GameObject> generic, ref Dictionary<string, List<GameObject>> perRes,
            string stem, GameObject go)
        {
            if (!stem.StartsWith("Fill_", System.StringComparison.Ordinal)) return;
            string rest = stem.Substring(5);
            int us = rest.LastIndexOf('_');
            string res = us > 0 ? rest.Substring(0, us) : null;
            if (!int.TryParse(us > 0 ? rest.Substring(us + 1) : rest, out int k) || k < 1 || k > 64) return;
            List<GameObject> into;
            if (res == null) into = generic;
            else
            {
                perRes ??= new Dictionary<string, List<GameObject>>();
                if (!perRes.TryGetValue(res, out into)) perRes[res] = into = new List<GameObject>();
            }
            while (into.Count < k) into.Add(null);
            into[k - 1] = go;
        }

        /// A missing step (Fill_1, Fill_3, no Fill_2) is tolerated: drop the
        /// holes so N is the number of steps actually authored.
        static void Compact(List<GameObject> set) => set.RemoveAll(g => g == null);
    }
}
