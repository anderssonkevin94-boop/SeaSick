using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Kevin's storage-visibility rule, applied to the camp Storage hut
    /// (2026-09-23):** the building always shows the true amount the camp's
    /// STORE holds -- not a station's bay/rack (that is `StationStockView`'s
    /// job), the store proper (`OutpostLedger.StoreCountOf`), the thing a
    /// hauler is filling and a build site is spending down.
    ///
    /// **Astra's storage kit (`art-staging/storage-astra-lvl1-v1`) has fixed
    /// display racks, not a capacity model**: `Stock_Timber_01..05`,
    /// `Stock_Boards_01..08`, `Stock_Food_01..03`, and two generic
    /// `Stock_Cargo_01..02` crates the README explicitly says have no
    /// resource binding ("do not invent ore, stone, tools, arrows, bricks or
    /// meal counts from the generic crates"). So this view maps the three
    /// named racks onto `StoreCountOf` for the matching resource, CAPPED at
    /// the rack's own slot count -- Kevin: cap the display when the numbers
    /// get extreme, rather than pretend a 5-slot rack reads 40 logs -- and
    /// leaves the cargo crates permanently hidden, unmapped, exactly as
    /// flagged.
    public class StoreStockView : MonoBehaviour
    {
        readonly List<GameObject> timberSlots = new List<GameObject>();
        readonly List<GameObject> boardsSlots = new List<GameObject>();
        readonly List<GameObject> foodSlots = new List<GameObject>();
        readonly List<GameObject> cargoSlots = new List<GameObject>();
        bool discovered;

        OutpostLedger ledger;
        bool resolved;
        int resolveAttempts;
        int retryWait;
        const int RetryFrames = 30;
        const int MaxResolveAttempts = 20;

        int shownTimber = -1, shownBoards = -1, shownFood = -1;

        void Start()
        {
            DiscoverSlots();
            if (timberSlots.Count == 0 && boardsSlots.Count == 0 && foodSlots.Count == 0)
                enabled = false;
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
                var outpost = GetComponentInParent<Outpost>();
                ledger = outpost != null ? outpost.Ledger : null;
                if (ledger != null) resolved = true;
                else { if (resolveAttempts < MaxResolveAttempts) resolveAttempts++; return; }
            }
            Apply();
        }

        void Apply()
        {
            int t = Capped(ledger.StoreCountOf(Res.Timber), timberSlots.Count);
            if (t != shownTimber) { SetShown(timberSlots, t); shownTimber = t; }

            int b = Capped(ledger.StoreCountOf(Res.Boards), boardsSlots.Count);
            if (b != shownBoards) { SetShown(boardsSlots, b); shownBoards = b; }

            int f = Capped(ledger.StoreCountOf(Res.Food), foodSlots.Count);
            if (f != shownFood) { SetShown(foodSlots, f); shownFood = f; }

            // Cargo stays hidden: the kit's own README says its resource
            // binding is unassigned, so showing any count on it would be
            // inventing a stock that is not there.
        }

        static int Capped(int count, int slots) => Mathf.Clamp(count, 0, slots);

        static void SetShown(List<GameObject> slots, int n)
        {
            for (int i = 0; i < slots.Count; i++)
                slots[i].SetActive(i < n);
        }

        void DiscoverSlots()
        {
            if (discovered) return;
            discovered = true;
            CollectByPrefix("Stock_Timber_", timberSlots);
            CollectByPrefix("Stock_Boards_", boardsSlots);
            CollectByPrefix("Stock_Food_", foodSlots);
            CollectByPrefix("Stock_Cargo_", cargoSlots);
            SetShown(timberSlots, 0);
            SetShown(boardsSlots, 0);
            SetShown(foodSlots, 0);
            SetShown(cargoSlots, 0);   // never turned back on
        }

        void CollectByPrefix(string prefix, List<GameObject> into)
        {
            var found = new List<(int n, Transform t)>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string stem = BuildingFactory.Stem(t.name);
                if (!stem.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                found.Add((TrailingNumber(stem), t));
            }
            found.Sort((a, c) => a.n != c.n ? a.n.CompareTo(c.n) : string.CompareOrdinal(a.t.name, c.t.name));
            foreach (var f in found) into.Add(f.t.gameObject);
        }

        static int TrailingNumber(string stem)
        {
            int i = stem.Length;
            while (i > 0 && char.IsDigit(stem[i - 1])) i--;
            if (i == stem.Length) return int.MaxValue;
            return int.TryParse(stem.Substring(i), out int n) ? n : int.MaxValue;
        }

        /// **Drive the view without a live ledger** -- edit-mode / preview
        /// verification, the same shape as `StationStockView.Preview`.
        public static StoreStockView Preview(GameObject building, int timber, int boards, int food)
        {
            var view = building.GetComponent<StoreStockView>();
            if (view == null) view = building.AddComponent<StoreStockView>();
            view.DiscoverSlots();
            view.resolved = true;
            view.ledger = null;
            view.enabled = false;
            SetShown(view.timberSlots, Mathf.Clamp(timber, 0, view.timberSlots.Count));
            view.shownTimber = timber;
            SetShown(view.boardsSlots, Mathf.Clamp(boards, 0, view.boardsSlots.Count));
            view.shownBoards = boards;
            SetShown(view.foodSlots, Mathf.Clamp(food, 0, view.foodSlots.Count));
            view.shownFood = food;
            return view;
        }
    }
}
