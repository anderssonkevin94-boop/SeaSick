using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// A place to anchor. Every island is shelter; some also carry resources.
    /// Keeps a static registry so the swell warning and navigation can ask
    /// "where is the nearest land?" without searching the scene.
    public class Island : MonoBehaviour
    {
        public static readonly List<Island> All = new List<Island>();

        [SerializeField] string resourceName = "Timber";
        [SerializeField] float remaining;
        [SerializeField] float radius = 40f;
        [SerializeField] bool isHome;

        public string ResourceName => resourceName;
        public float Remaining => remaining;
        public float Radius => radius;
        public bool IsHome => isHome;
        public bool HasResources => remaining > 0.5f;
        /// How close the ship must be before the anchor option appears.
        public float AnchorRadius => radius + 30f;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Configure(string resource, float amount, float islandRadius, bool home)
        {
            resourceName = resource;
            remaining = amount;
            radius = islandRadius;
            isHome = home;
        }

        public float Extract(float amount)
        {
            float take = Mathf.Min(amount, remaining);
            remaining -= take;
            return take;
        }

        /// A standing spot on the beach for crew member `index` of `total`.
        public Vector3 ShorePoint(int index, int total)
        {
            float ang = (total <= 1 ? 0f : (index / (float)total) * Mathf.PI * 2f) + 0.7f;
            float d = radius * 0.5f;
            // Matches the sand dome built by ArchipelagoGenerator.
            float y = -0.12f * radius + 0.2f * radius * Mathf.Sqrt(Mathf.Max(0f, 1f - 0.25f));
            return transform.position + new Vector3(Mathf.Sin(ang) * d, y, Mathf.Cos(ang) * d);
        }

        public static Island Nearest(Vector3 pos, bool requireResources = false)
        {
            Island best = null;
            float bestSq = float.MaxValue;
            foreach (var isle in All)
            {
                if (isle == null) continue;
                if (requireResources && !isle.HasResources) continue;
                Vector3 d = isle.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = isle; }
            }
            return best;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
