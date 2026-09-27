using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Kevin's storage-visibility rule (2026-09-23): every production
    /// building ALWAYS shows the true amount of what it's working with /
    /// has produced.** Toggles a station's authored slot children -- input
    /// bay, bench, output rack, tool -- to match its `StationStock` every
    /// frame, touching `SetActive` only when the shown state actually
    /// changes.
    ///
    /// Attached to every kit building in `BuildingFactory.Dress`. A model
    /// with none of the slot names below (the kit blacksmith/kitchen, the
    /// extruded shed) finds nothing and disables itself in `Start`, so it
    /// costs nothing on those. READ-ONLY: never writes the ledger.
    ///
    /// **Slot convention** (Astra's kit): input bay slots
    /// `Input_<Thing>_NN` under `Input_Container`, output rack slots
    /// `Output_<Thing>_NN` under `Output_Container`, bench state models
    /// `Bench_Loaded` / `Bench_Cutting` / `Bench_Finished` under
    /// `Bench_Anchor` (mutually exclusive, all hidden when
    /// `BenchState.Empty`), and one tool child whose name ends `_Tool`
    /// (visible only while `Working` -- **a choice**: a mallet or saw
    /// sitting out while the bench is idle would read as left behind, not
    /// as "ready"). Discovered by prefix/suffix and sorted by the trailing
    /// number, not hardcoded per building, because the FBX import can add
    /// `.001` suffixes -- matched on the stem, same as `BuildingFactory`'s
    /// bed naming.
    ///
    /// **Bay count is the current/bench recipe's FIRST input**, not a name
    /// match on the slot group: the kit's slot names are cosmetic (the
    /// sawmill's are literally `Input_Log_*` while the resource it actually
    /// holds is `Timber`), so matching by name would silently show zero
    /// forever on that building. The rack, which is never resource-specific
    /// on the model, shows `StationStock.RackTotal` regardless of what is on
    /// it.
    public class StationStockView : MonoBehaviour
    {
        readonly List<GameObject> inputSlots = new List<GameObject>();
        readonly List<GameObject> outputSlots = new List<GameObject>();
        GameObject benchLoaded, benchCutting, benchFinished, tool;
        bool discovered;

        StationStock station;
        bool resolved;
        int resolveAttempts;
        int retryWait;
        const int RetryFrames = 30;
        const int MaxResolveAttempts = 20;   // ~20 frames; a real station resolves on the first one

        int shownInput = -1, shownOutput = -1;
        BenchState shownBench = (BenchState)(-1);

        void Start()
        {
            DiscoverSlots();
            if (inputSlots.Count == 0 && outputSlots.Count == 0
                && benchLoaded == null && benchCutting == null && benchFinished == null && tool == null)
                enabled = false;   // nothing on this model to ever toggle
        }

        void Update()
        {
            if (!resolved)
            {
                // **Never give up, just slow down (2026-09-23).** After
                // `MaxResolveAttempts` fast frames the view retries every
                // `RetryFrames` frames forever, rather than disabling itself
                // for good on a camp whose ledger arrived late (a load, a
                // raise mid-frame): one GetComponentInParent per ~half second.
                if (resolveAttempts >= MaxResolveAttempts && ++retryWait < RetryFrames) return;
                retryWait = 0;
                station = ResolveStation();
                if (station != null) resolved = true;
                else { if (resolveAttempts < MaxResolveAttempts) resolveAttempts++; return; }
            }
            if (station == null) return;
            // A demolish elsewhere in the camp removes a station row and
            // shifts the plan's ordinals down, so the cached row can go dead
            // or stop being ours: re-find it when it is flagged removed, and
            // once a second regardless, which is cheap next to being wrong.
            if (station.removed || ++sinceResolve >= ReresolveFrames)
            {
                sinceResolve = 0;
                var fresh = ResolveStation();
                if (fresh == null) { if (station.removed) { resolved = false; resolveAttempts = 0; } return; }
                if (fresh != station) { station = fresh; shownInput = shownOutput = -1; shownBench = (BenchState)(-1); }
            }
            Apply();
        }

        int sinceResolve;
        const int ReresolveFrames = 60;

        // --- resolving the station ---------------------------------------------

        /// Which `StationStock` this building's ledger row is. Matched by
        /// plan id + ground position against `OutpostLedger.raised`
        /// (written with the very same `Vector3` this building's transform
        /// was placed at, so the x/z compare is exact) rather than by
        /// touching `Outpost`'s private build order -- `raised` and
        /// `StationForRaised` are the public read API for exactly this.
        StationStock ResolveStation()
        {
            var b = GetComponent<Building>();
            if (b == null) return null;   // Building not attached yet this frame (or this is a ghost, and never will be)
            if (!OutpostLedger.IsStation(b.Id)) { enabled = false; return null; }

            var outpost = GetComponentInParent<Outpost>();
            var ledger = outpost != null ? outpost.Ledger : null;
            if (ledger == null || ledger.raised == null) return null;

            Vector3 pos = transform.position;
            for (int i = 0; i < ledger.raised.Count; i++)
            {
                var r = ledger.raised[i];
                if (r == null || r.planId != b.Id) continue;
                if (Mathf.Approximately(r.x, pos.x) && Mathf.Approximately(r.z, pos.z))
                    return ledger.StationForRaised(i);
            }
            return null;
        }

        // --- applying the ledger's truth -----------------------------------------

        void Apply()
        {
            string inRes = PrimaryInputRes(station);
            int inCount = string.IsNullOrEmpty(inRes) ? 0 : station.BayCount(inRes);
            int inN = MapCount(inCount, Mathf.Max(1, station.InputCap), inputSlots.Count);
            if (inN != shownInput) { SetShown(inputSlots, inN); shownInput = inN; }

            int outN = MapCount(station.RackTotal, Mathf.Max(1, station.OutputCap), outputSlots.Count);
            if (outN != shownOutput) { SetShown(outputSlots, outN); shownOutput = outN; }

            if (station.benchState != shownBench) ApplyBench(station.benchState);
        }

        void ApplyBench(BenchState bench)
        {
            shownBench = bench;
            if (benchLoaded != null) benchLoaded.SetActive(bench == BenchState.Loaded);
            if (benchCutting != null) benchCutting.SetActive(bench == BenchState.Working);
            if (benchFinished != null) benchFinished.SetActive(bench == BenchState.Finished);
            // Tool out only while it is actually being swung.
            if (tool != null) tool.SetActive(bench == BenchState.Working);
        }

        /// The recipe currently on the bench (or, with nothing loaded yet,
        /// the one on order) -- its first input line is what the bay is
        /// holding.
        static string PrimaryInputRes(StationStock s)
        {
            var r = s.BenchRecipe ?? s.OrderRecipe;
            if (r != null)
                foreach (var line in r.takes)
                    if (line.n > 0) return line.res;
            return null;
        }

        /// Slots 1..N shown, N mapped from the ledger's unit count onto the
        /// model's slot count: 1:1 when the capacity already matches the
        /// slot count (true for every kit building today), else scaled.
        static int MapCount(int count, int cap, int slots)
        {
            if (slots <= 0) return 0;
            int n = cap == slots ? count : Mathf.CeilToInt(count * (float)slots / cap);
            return Mathf.Clamp(n, 0, slots);
        }

        static void SetShown(List<GameObject> slots, int n)
        {
            for (int i = 0; i < slots.Count; i++)
                slots[i].SetActive(i < n);
        }

        // --- discovery -------------------------------------------------------------

        /// **Alias names, 2026-09-23 (Astra's kitchen, forge and fletcher).**
        /// The kit convention wraps slots in
        /// `Input_Container`/`Output_Container`/`Bench_Anchor` groups, but
        /// several of Astra's kits have no such wrapper -- their numbered
        /// slots and work-state meshes sit flat at the model root, and each
        /// kit names its work states for what they SHOW
        /// (kitchen: `Work_Preparing`/`Work_Cooking`/`Work_Finished`; forge:
        /// `Work_Heating`/`Work_Forging`/`Work_Finished`; fletcher:
        /// `Work_Shaping`/`Work_Fletching`/`Work_Finished`) rather than for
        /// the bench mechanic (`Bench_Loaded`/`Bench_Cutting`/
        /// `Bench_Finished`). Extending discovery with a small alias list
        /// (and a root-level fallback when a container is absent) keeps this
        /// one generic reader working on every kit, per Kevin's rule of
        /// extending the reader rather than special-casing a building.
        static readonly string[] BenchLoadedNames = { "Bench_Loaded", "Work_Preparing", "Work_Heating", "Work_Shaping" };
        static readonly string[] BenchCuttingNames = { "Bench_Cutting", "Work_Cooking", "Work_Forging", "Work_Fletching", "Work_Catch" };
        static readonly string[] BenchFinishedNames = { "Bench_Finished", "Work_Finished" };

        void DiscoverSlots()
        {
            if (discovered) return;
            discovered = true;

            var inputContainer = FindByStem(transform, "Input_Container");
            var outputContainer = FindByStem(transform, "Output_Container");
            var benchAnchor = FindByStem(transform, "Bench_Anchor");

            // Container present: scoped collection, unchanged. Absent (Astra's
            // kitchen): fall back to the whole model, numbered-slot filter in
            // `CollectSlots` keeps `Input_Anchor`/`Input_Crate` out of it.
            CollectSlots(inputContainer != null ? inputContainer : transform, "Input_", inputSlots);
            CollectSlots(outputContainer != null ? outputContainer : transform, "Output_", outputSlots);

            var benchRoot = benchAnchor != null ? benchAnchor : transform;
            benchLoaded = FindFirstByStem(benchRoot, BenchLoadedNames)?.gameObject;
            benchCutting = FindFirstByStem(benchRoot, BenchCuttingNames)?.gameObject;
            benchFinished = FindFirstByStem(benchRoot, BenchFinishedNames)?.gameObject;
            tool = FindToolChild(transform)?.gameObject;

            // All hidden until the first real Apply -- an idle bench should
            // never show its finished/loaded dressing before the ledger has
            // said so.
            if (benchLoaded != null) benchLoaded.SetActive(false);
            if (benchCutting != null) benchCutting.SetActive(false);
            if (benchFinished != null) benchFinished.SetActive(false);
            if (tool != null) tool.SetActive(false);
            SetShown(inputSlots, 0);
            SetShown(outputSlots, 0);
        }

        static Transform FindByStem(Transform root, string stem)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (BuildingFactory.Stem(t.name) == stem) return t;
            return null;
        }

        static void CollectSlots(Transform container, string prefix, List<GameObject> into)
        {
            var found = new List<(int n, Transform t)>();
            foreach (var t in container.GetComponentsInChildren<Transform>(true))
            {
                if (t == container) continue;
                string stem = BuildingFactory.Stem(t.name);
                if (!stem.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                int n = TrailingNumber(stem);
                // A numbered slot only. Scoped to a real `_Container`, every
                // child already qualifies; scanning a whole model root (no
                // container -- Astra's kitchen) also catches unnumbered
                // siblings like `Input_Anchor`/`Input_Crate`, which are not
                // slots and must not count as one.
                if (n == int.MaxValue) continue;
                found.Add((n, t));
            }
            found.Sort((a, b) => a.n != b.n ? a.n.CompareTo(b.n) : string.CompareOrdinal(a.t.name, b.t.name));
            foreach (var f in found) into.Add(f.t.gameObject);
        }

        /// First child (searched depth-first through `root`) whose stem
        /// exactly matches one of `names`, tried in order.
        static Transform FindFirstByStem(Transform root, string[] names)
        {
            foreach (var name in names)
            {
                var t = FindByStem(root, name);
                if (t != null) return t;
            }
            return null;
        }

        static Transform FindToolChild(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (BuildingFactory.Stem(t.name).EndsWith("_Tool", System.StringComparison.Ordinal)) return t;
            }
            return null;
        }

        static int TrailingNumber(string stem)
        {
            int i = stem.Length;
            while (i > 0 && char.IsDigit(stem[i - 1])) i--;
            if (i == stem.Length) return int.MaxValue;   // no trailing digits: sort after the numbered ones
            return int.TryParse(stem.Substring(i), out int n) ? n : int.MaxValue;
        }

        // --- edit-mode / test verification ---------------------------------------

        /// **Drive the view without a live `StationStock`.** For a scene
        /// with no play mode running (edit-mode verification, or a probe):
        /// discovers the model's slots and shows a fixed state directly,
        /// bypassing `Update`'s ledger resolution entirely (`Update` never
        /// runs outside play mode anyway). `input`/`output` are shown-slot
        /// counts, not ledger units -- exact for every kit building today,
        /// since their input/output capacities already equal their slot
        /// counts.
        public static StationStockView Preview(GameObject building, int input, int output, BenchState bench)
        {
            var view = building.GetComponent<StationStockView>();
            if (view == null) view = building.AddComponent<StationStockView>();
            view.DiscoverSlots();
            view.resolved = true;      // never try to resolve a real station over this
            view.station = null;
            view.enabled = false;      // no Update loop in edit mode to disable, but keeps play mode from taking over if this object survives into it
            SetShown(view.inputSlots, Mathf.Clamp(input, 0, view.inputSlots.Count));
            view.shownInput = input;
            SetShown(view.outputSlots, Mathf.Clamp(output, 0, view.outputSlots.Count));
            view.shownOutput = output;
            view.ApplyBench(bench);
            return view;
        }
    }
}
