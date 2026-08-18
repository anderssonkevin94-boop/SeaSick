using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// The pile of everything you've ever brought home, sitting on the beach.
    /// The whole point of the voyage loop is watching this grow — a number in
    /// a menu doesn't do that.
    public class Stockpile : MonoBehaviour
    {
        public static Stockpile Instance { get; private set; }

        [SerializeField] int perRow = 4;
        [SerializeField] float spacing = 1.7f;
        [SerializeField] float layerHeight = 0.9f;
        [SerializeField] int maxPerResource = 40;

        readonly Dictionary<string, List<GameObject>> piles = new Dictionary<string, List<GameObject>>();
        readonly Dictionary<string, Transform> roots = new Dictionary<string, Transform>();
        Island island;
        int pileIndex;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Start() { island = GetComponent<Island>(); }

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
            if (island != null)
            {
                Vector3 world = island.SurfacePoint(ang, island.RadiusAt(ang) * 0.62f);
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

        public int CountOf(string resource) =>
            piles.TryGetValue(resource, out var list) ? list.Count : 0;
    }
}
