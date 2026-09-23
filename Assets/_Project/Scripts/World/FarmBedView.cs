using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Kevin's storage-visibility rule, applied to the farm (2026-09-23):**
    /// each of Astra's six beds (`art-staging/farm-astra-lvl1-v1`) shows the
    /// TRUE standing/harvested state of the matching field bed, and the
    /// harvest display shows the camp's actual stored Food -- not an
    /// invented growth clock.
    ///
    /// **Why only Bare/Ripe, not Sprout/Growing.** The field's real state
    /// (`Terrain.SceneryCrops`) is binary per bed: `Outpost.SyncHarvest`
    /// calls `crops.Harvest(i)`/`crops.Regrow(i)` every tick straight off the
    /// ledger's standing-Food count -- there is no tracked intermediate
    /// growth stage to read honestly, and `SceneryCrops.RegrowOverdue` (which
    /// would age a harvest through its `regrowAtDay` timer) is never called
    /// for camp fields, so that timer is not live data either. Showing
    /// `Sprout`/`Growing` would be inventing a stage the game does not know,
    /// which is the thing Kevin's rule forbids; `Bed_NN_Soil` stays visible
    /// always (bare ground under the crop either way) and `Bed_NN_Ripe`
    /// tracks `!harvested`.
    ///
    /// **Which crop bed is which.** `Terrain.SceneryCrops.PlantHere` lays
    /// the field's own small stand-in meshes near the building, independent
    /// of this model's `Bed_NN_Anchor` transforms; there is no stored link
    /// from a bed index to a building. So each of the six Astra anchors is
    /// matched at resolve time to the nearest live crop bed within reach --
    /// stable for the run because neither set of positions moves.
    public class FarmBedView : MonoBehaviour
    {
        struct BedGroup
        {
            public Transform anchor;
            public GameObject soil, sprout, growing, ripe;
        }

        readonly List<BedGroup> bedGroups = new List<BedGroup>();
        readonly List<GameObject> sheafSlots = new List<GameObject>();
        int[] cropIndex;   // per bedGroups entry, index into crops.BedAt(), or -1
        bool discovered;

        Terrain.SceneryCrops crops;
        OutpostLedger ledger;
        bool resolved;
        int resolveAttempts;
        const int MaxResolveAttempts = 40;   // field is planted async of Dress; give it longer than a station

        /// How far a bed anchor may be from a live crop bed to count as its
        /// match. `SceneryCrops.PlantHere` spacing is 1.6 m, so a couple of
        /// rows' worth of slack.
        const float MatchReach = 5f;

        bool[] shownRipe;
        int shownFood = -1;

        void Start()
        {
            DiscoverSlots();
            if (bedGroups.Count == 0 && sheafSlots.Count == 0)
                enabled = false;
        }

        void Update()
        {
            if (!resolved)
            {
                TryResolve();
                if (!resolved)
                {
                    if (++resolveAttempts >= MaxResolveAttempts) enabled = false;
                    return;
                }
            }
            Apply();
        }

        void TryResolve()
        {
            var isle = Island.Nearest(transform.position);
            crops = Terrain.SceneryCrops.On(isle);
            var outpost = GetComponentInParent<Outpost>();
            ledger = outpost != null ? outpost.Ledger : null;
            if (crops == null || crops.BedCount == 0 || ledger == null) return;

            cropIndex = new int[bedGroups.Count];
            for (int i = 0; i < bedGroups.Count; i++)
            {
                int best = -1; float bestSq = MatchReach * MatchReach;
                Vector3 at = bedGroups[i].anchor.position;
                for (int c = 0; c < crops.BedCount; c++)
                {
                    Vector3 d = crops.BedAt(c).at - at; d.y = 0f;
                    float sq = d.sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; best = c; }
                }
                cropIndex[i] = best;
            }
            resolved = true;
        }

        void Apply()
        {
            for (int i = 0; i < bedGroups.Count; i++)
            {
                int ci = cropIndex[i];
                bool ripe = ci >= 0 && !crops.BedAt(ci).harvested;
                if (ripe == shownRipe[i]) continue;
                shownRipe[i] = ripe;
                var g = bedGroups[i];
                if (g.ripe != null) g.ripe.SetActive(ripe);
                // Sprout/Growing intentionally never shown -- see class doc.
            }

            int food = Mathf.Clamp(ledger.StoreCountOf(Res.Food), 0, sheafSlots.Count);
            if (food != shownFood)
            {
                for (int i = 0; i < sheafSlots.Count; i++) sheafSlots[i].SetActive(i < food);
                shownFood = food;
            }
        }

        void DiscoverSlots()
        {
            if (discovered) return;
            discovered = true;

            var byBed = new Dictionary<int, BedGroup>();
            var anchors = new Dictionary<int, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string stem = BuildingFactory.Stem(t.name);
                if (!stem.StartsWith("Bed_", System.StringComparison.Ordinal)) continue;
                var parts = stem.Split('_');
                if (parts.Length != 3 || !int.TryParse(parts[1], out int bed)) continue;
                string kind = parts[2];
                if (kind == "Anchor") { anchors[bed] = t; continue; }
                byBed.TryGetValue(bed, out var g);
                switch (kind)
                {
                    case "Soil": g.soil = t.gameObject; break;
                    case "Sprout": g.sprout = t.gameObject; break;
                    case "Growing": g.growing = t.gameObject; break;
                    case "Ripe": g.ripe = t.gameObject; break;
                }
                byBed[bed] = g;
            }
            var beds = new List<int>(byBed.Keys); beds.Sort();
            foreach (var bed in beds)
            {
                var g = byBed[bed];
                g.anchor = anchors.TryGetValue(bed, out var a) ? a : (g.soil != null ? g.soil.transform : null);
                if (g.anchor == null) continue;
                bedGroups.Add(g);
            }
            shownRipe = new bool[bedGroups.Count];

            var sheaves = new List<(int n, Transform t)>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string stem = BuildingFactory.Stem(t.name);
                if (!stem.StartsWith("Harvest_Sheaf_", System.StringComparison.Ordinal)) continue;
                int n = TrailingNumber(stem);
                if (n == int.MaxValue) continue;
                sheaves.Add((n, t));
            }
            sheaves.Sort((x, y) => x.n.CompareTo(y.n));
            foreach (var s in sheaves) sheafSlots.Add(s.t.gameObject);

            // All hidden until the first real Apply: bare beds and an empty
            // basket until the field/ledger say otherwise.
            foreach (var g in bedGroups)
            {
                if (g.sprout != null) g.sprout.SetActive(false);
                if (g.growing != null) g.growing.SetActive(false);
                if (g.ripe != null) g.ripe.SetActive(false);
            }
            for (int i = 0; i < sheafSlots.Count; i++) sheafSlots[i].SetActive(false);
        }

        static int TrailingNumber(string stem)
        {
            int i = stem.Length;
            while (i > 0 && char.IsDigit(stem[i - 1])) i--;
            if (i == stem.Length) return int.MaxValue;
            return int.TryParse(stem.Substring(i), out int n) ? n : int.MaxValue;
        }

        /// **Drive the view without a live field/ledger** -- edit-mode /
        /// preview verification. `ripeBeds` sets the first N (in discovery
        /// order) beds ripe and the rest bare; `food` sets the harvest
        /// sheaves shown.
        public static FarmBedView Preview(GameObject building, int ripeBeds, int food)
        {
            var view = building.GetComponent<FarmBedView>();
            if (view == null) view = building.AddComponent<FarmBedView>();
            view.DiscoverSlots();
            view.resolved = true;
            view.crops = null; view.ledger = null;
            view.enabled = false;
            for (int i = 0; i < view.bedGroups.Count; i++)
            {
                bool ripe = i < ripeBeds;
                view.shownRipe[i] = ripe;
                if (view.bedGroups[i].ripe != null) view.bedGroups[i].ripe.SetActive(ripe);
            }
            int f = Mathf.Clamp(food, 0, view.sheafSlots.Count);
            for (int i = 0; i < view.sheafSlots.Count; i++) view.sheafSlots[i].SetActive(i < f);
            view.shownFood = f;
            return view;
        }
    }
}
