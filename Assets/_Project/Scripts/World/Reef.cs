using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// A half-submerged hazard between the islands. Small enough to thread at
    /// speed, sharp enough to punish a lazy line — this is what makes open
    /// water demand attention instead of a held course.
    public class Reef : MonoBehaviour
    {
        public static readonly List<Reef> All = new List<Reef>();

        [SerializeField] float radius = 8f;
        public float Radius => radius;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Configure(float r) { radius = r; }

        public static Reef Nearest(Vector3 pos)
        {
            Reef best = null;
            float bestSq = float.MaxValue;
            foreach (var r in All)
            {
                if (r == null) continue;
                Vector3 d = r.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = r; }
            }
            return best;
        }
    }
}
