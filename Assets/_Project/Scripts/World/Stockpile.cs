using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// The pile of everything landed at one place, sitting on the ground.
    /// The whole point of the voyage loop is watching this grow — a number in
    /// a menu doesn't do that.
    ///
    /// One per island, not one per world: a camp's pile is the same object as
    /// home's beach, and it is what a returning player has come back to look
    /// at. See `Outpost` — home is outpost zero.
    public class Stockpile : MonoBehaviour
    {
        static readonly List<Stockpile> all = new List<Stockpile>();

        /// Home's pile. **RETIRED as a store 2026-10-04**: home docking no
        /// longer banks or draws crates here (it unloads into the home camp's
        /// store like any camp; no ground piles). The game only `Clear`s it
        /// (`VoyageManager.RestoreStores` / `RepairLegacyBank`); kept for the
        /// populator and the older probes. Anything asking about the island
        /// the ship is AT must use `Of`.
        /// Derived from the home berth (2026-09-29): the pile on
        /// `Island.Home`, null before the player has made a home.
        /// Made on first ask, so a load (which rebuilds the island without
        /// the runtime component) gets home's pile back with no save field.
        public static Stockpile Instance => Island.Home != null ? EnsureOn(Island.Home) : null;

        [SerializeField] int perRow = 4;
        [SerializeField] float spacing = 1.7f;
        [SerializeField] float layerHeight = 0.9f;
        [Tooltip("Visual ceiling only -- how many objects one pile will ever build. What a place can KEEP is Outpost.StoreCapacity, and the ledger is VoyageManager's.")]
        [SerializeField] int maxPerResource = 160;

        readonly Dictionary<string, List<GameObject>> piles = new Dictionary<string, List<GameObject>>();
        readonly Dictionary<string, Transform> roots = new Dictionary<string, Transform>();
        Island island;
        int pileIndex;

        /// The island this pile is on. Resolved on demand rather than in
        /// `Start`, because `Of` can be asked before Unity has run it --
        /// script order is not guaranteed, and the populator adds the island
        /// and the pile in the same frame.
        public Island Island => island != null ? island : (island = GetComponent<Island>());

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        void OnDisable()
        {
            all.Remove(this);
        }

        /// The pile on this island, or null if nothing has been landed there.
        public static Stockpile Of(Island isle)
        {
            if (isle == null) return null;
            foreach (var s in all) if (s != null && s.Island == isle) return s;
            return null;
        }

        /// The pile on this island, made if it is not there yet -- a camp gets
        /// one the moment something is set down on it.
        public static Stockpile EnsureOn(Island isle)
        {
            if (isle == null) return null;
            return Of(isle) ?? isle.gameObject.AddComponent<Stockpile>();
        }

        Transform RootFor(string resource)
        {
            if (roots.TryGetValue(resource, out var t) && t != null) return t;

            var go = new GameObject($"Pile_{resource}");
            go.transform.SetParent(transform, false);

            // Space the piles around the shore so they don't grow into a
            // single heap and you can read them apart.
            float ang = pileIndex * 0.9f + 0.4f;
            pileIndex++;
            Vector3 local = Vector3.zero;
            var isle = Island;   // through the property: `Start` no longer caches it
            if (isle != null)
            {
                Vector3 world = isle.SurfacePoint(ang, isle.RadiusAt(ang) * 0.62f);
                local = transform.InverseTransformPoint(world);
            }
            go.transform.localPosition = local;
            roots[resource] = go.transform;
            return go.transform;
        }

        /// One unit set down on the beach.
        public void Deposit(string resource)
        {
            if (!piles.TryGetValue(resource, out var list))
            {
                list = new List<GameObject>();
                piles[resource] = list;
            }
            if (list.Count >= maxPerResource) return;

            var root = RootFor(resource);
            var item = CargoVisual.Build(resource, root);
            item.transform.localPosition =
                CargoVisual.StackSlot(list.Count, perRow, spacing, layerHeight);
            item.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            list.Add(item);
        }

        /// Taken off the beach again -- spent on a building. The pile has to
        /// come down with it, or the stores read as spent in the panel and
        /// untouched on the ground two metres away.
        public int Withdraw(string resource, int amount)
        {
            if (!piles.TryGetValue(resource, out var list)) return 0;
            int took = 0;
            while (took < amount && list.Count > 0)
            {
                int last = list.Count - 1;
                if (list[last] != null) Destroy(list[last]);
                list.RemoveAt(last);
                took++;
            }
            return took;
        }

        public int CountOf(string resource) =>
            piles.TryGetValue(resource, out var list) ? list.Count : 0;

        /// Every pile down to nothing. A load rebuilds them from the saved
        /// stores; a probe wipes them between the save and the load.
        public void Clear()
        {
            foreach (var kv in piles)
                foreach (var go in kv.Value) if (go != null) Destroy(go);
            piles.Clear();
        }
    }
}
