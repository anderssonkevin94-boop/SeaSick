using UnityEngine;

namespace SeaSick.World
{
    /// **Kevin's storage-visibility rule, applied to the campfire
    /// (2026-09-23):** Astra's kit (`art-staging/campfire-astra-lvl1-v2`)
    /// has a cold/embers/lit contract and three removable fuel logs; this
    /// maps both onto real camp state rather than a decorative default.
    ///
    /// **State chosen from what the ledger actually tracks.** There is no
    /// "recently left" timer anywhere in `OutpostLedger` -- only who is
    /// housed right now (`hands.Count`, via `Housed`) and what is in the
    /// store (`StoreCountOf`). So:
    /// - **lit** -- somebody is housed here: the fire is tended.
    /// - **embers** -- nobody is housed, but there is still Timber in the
    ///   store to burn: banked, not out.
    /// - **cold** -- nobody housed and no Timber in store: abandoned.
    ///
    /// `Fuel_Log_01..03` show `min(3, StoreCountOf(Timber))` regardless of
    /// state -- a pile of unlit logs is still a real pile. The game's own
    /// `Firelight` (added by `BuildingFactory.Dress` for `BuildKind.Fire`,
    /// day/night driven, `Campfire.health01`) is untouched by this view; it
    /// is a separate concern this task was told to leave in place, not
    /// re-gate.
    public class CampfireStateView : MonoBehaviour
    {
        readonly System.Collections.Generic.List<GameObject> fuelSlots = new System.Collections.Generic.List<GameObject>();
        GameObject embers;
        bool discovered;

        OutpostLedger ledger;
        bool resolved;
        int resolveAttempts;
        const int MaxResolveAttempts = 20;

        int shownFuel = -1;
        bool shownEmbers;

        void Start()
        {
            DiscoverSlots();
            if (fuelSlots.Count == 0 && embers == null) enabled = false;
        }

        void Update()
        {
            if (!resolved)
            {
                var outpost = GetComponentInParent<Outpost>();
                ledger = outpost != null ? outpost.Ledger : null;
                if (ledger != null) resolved = true;
                else if (++resolveAttempts >= MaxResolveAttempts) { enabled = false; return; }
                else return;
            }
            Apply();
        }

        void Apply()
        {
            int fuel = Mathf.Clamp(ledger.StoreCountOf(Res.Timber), 0, fuelSlots.Count);
            if (fuel != shownFuel)
            {
                for (int i = 0; i < fuelSlots.Count; i++) fuelSlots[i].SetActive(i < fuel);
                shownFuel = fuel;
            }

            bool housed = ledger.Housed > 0;
            bool hasFuel = ledger.StoreCountOf(Res.Timber) > 0;
            // lit or embers both show the ember geometry; only "cold" hides it.
            bool showEmbers = housed || hasFuel;
            if (showEmbers != shownEmbers)
            {
                if (embers != null) embers.SetActive(showEmbers);
                shownEmbers = showEmbers;
            }
        }

        void DiscoverSlots()
        {
            if (discovered) return;
            discovered = true;
            var found = new System.Collections.Generic.List<(int n, Transform t)>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string stem = BuildingFactory.Stem(t.name);
                if (stem == "Embers") { embers = t.gameObject; continue; }
                if (!stem.StartsWith("Fuel_Log_", System.StringComparison.Ordinal)) continue;
                int n = TrailingNumber(stem);
                if (n == int.MaxValue) continue;
                found.Add((n, t));
            }
            found.Sort((a, b) => a.n.CompareTo(b.n));
            foreach (var f in found) fuelSlots.Add(f.t.gameObject);

            if (embers != null) embers.SetActive(false);
            for (int i = 0; i < fuelSlots.Count; i++) fuelSlots[i].SetActive(false);
        }

        static int TrailingNumber(string stem)
        {
            int i = stem.Length;
            while (i > 0 && char.IsDigit(stem[i - 1])) i--;
            if (i == stem.Length) return int.MaxValue;
            return int.TryParse(stem.Substring(i), out int n) ? n : int.MaxValue;
        }

        /// **Drive the view without a live ledger** -- edit-mode / preview.
        /// `state`: 0 = cold, 1 = embers, 2 = lit (embers mesh on for 1 and 2).
        public static CampfireStateView Preview(GameObject building, int fuel, int state)
        {
            var view = building.GetComponent<CampfireStateView>();
            if (view == null) view = building.AddComponent<CampfireStateView>();
            view.DiscoverSlots();
            view.resolved = true;
            view.ledger = null;
            view.enabled = false;
            int f = Mathf.Clamp(fuel, 0, view.fuelSlots.Count);
            for (int i = 0; i < view.fuelSlots.Count; i++) view.fuelSlots[i].SetActive(i < f);
            view.shownFuel = f;
            bool showEmbers = state >= 1;
            if (view.embers != null) view.embers.SetActive(showEmbers);
            view.shownEmbers = showEmbers;
            return view;
        }
    }
}
