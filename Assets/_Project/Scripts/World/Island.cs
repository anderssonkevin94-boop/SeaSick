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

        float startingAmount;
        bool hasHill;
        readonly List<GameObject> props = new List<GameObject>();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Configure(string resource, float amount, float islandRadius, bool home,
            bool islandHasHill = true)
        {
            resourceName = resource;
            remaining = amount;
            startingAmount = Mathf.Max(1f, amount);
            radius = islandRadius;
            isHome = home;
            hasHill = islandHasHill;
        }

        /// Resource props (trees, boulders…) so the island shows what it is —
        /// and visibly empties as the crew strip it.
        public void RegisterProps(List<GameObject> resourceProps)
        {
            props.Clear();
            props.AddRange(resourceProps);
        }

        public float Extract(float amount)
        {
            float take = Mathf.Min(amount, remaining);
            remaining -= take;
            UpdateProps();
            return take;
        }

        void UpdateProps()
        {
            if (props.Count == 0) return;
            int shouldShow = Mathf.CeilToInt(props.Count * (remaining / startingAmount));
            for (int i = 0; i < props.Count; i++)
                if (props[i] != null && props[i].activeSelf != (i < shouldShow))
                    props[i].SetActive(i < shouldShow);
        }

        /// A working spot for crew member `index` of `total`, spread across the
        /// shore FACING the ship — so the shore party stays on screen.
        public Vector3 ShorePoint(int index, int total, Vector3 shipPos)
        {
            Vector3 toShip = shipPos - transform.position;
            toShip.y = 0f;
            if (toShip.sqrMagnitude < 0.01f) toShip = Vector3.forward;
            float baseAng = Mathf.Atan2(toShip.x, toShip.z);
            float spread = Mathf.Deg2Rad * 46f;
            float ang = baseAng + (total <= 1 ? 0f : Mathf.Lerp(-spread, spread, index / (float)(total - 1)));
            return SurfacePoint(ang, radius * 0.62f);
        }

        /// A point on the island's visible surface — whichever of the sand dome
        /// or the grass hill is higher there. Both match the shapes built by
        /// ArchipelagoGenerator; without the hill term, props sink inside it.
        public Vector3 SurfacePoint(float angleRad, float distFromCentre)
        {
            float d = Mathf.Min(distFromCentre, radius * 0.97f);
            float t = d / radius;
            float y = -0.12f * radius + 0.2f * radius * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));

            if (hasHill)
            {
                float hillReach = radius * 0.6f;   // hill sphere: 1.2r wide
                if (d < hillReach)
                {
                    float u = d / hillReach;
                    float hillY = radius * 0.05f + radius * 0.275f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                    y = Mathf.Max(y, hillY);
                }
            }
            return transform.position + new Vector3(Mathf.Sin(angleRad) * d, y, Mathf.Cos(angleRad) * d);
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
