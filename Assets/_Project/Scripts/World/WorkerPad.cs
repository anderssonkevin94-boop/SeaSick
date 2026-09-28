using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **A raised floor a villager stands ON, not in (2026-09-28).** Astra's
    /// level 1 lumber mill (`art-staging/lumber-mill-c-v1`) puts its sawyer on
    /// a 0.16 m timber pad behind the bench, and everything that places a
    /// body -- `CampWorker.WorkSpot`, `Walk`, a Hand drop -- took its height
    /// from `Outpost.GroundAt`, the analytic terrain, so he stood with his
    /// feet 16 cm inside the boards. Rather than a sawmill special case, the
    /// import (`MillL1Import`) measures the pad's walking top off the mesh and
    /// puts this component on a `Worker_Pad` child at that top's centre; any
    /// body inside its rectangle stands on it (`Foot`). A model with no
    /// `Worker_Pad` has none of these, so every other building is untouched.
    ///
    /// **The rear gate.** With `Worker_Approach` wired in, a walker going ON
    /// to the pad from outside is sent to the approach mark first and a
    /// walker coming OFF it leaves by it (`Gate`): the mill's pad is boxed by
    /// the bench in front and the racks either side (0.4 / 0.5 m gaps that
    /// are clearances, not lanes), and only the rear, between the two roof
    /// posts, is open. Beyond the gate the ordinary walk takes over --
    /// buildings are not obstacles to `CampPath` anywhere in the camp.
    ///
    /// A pad on a ghost / blueprint / preview model (no `Building` above it)
    /// is ignored, so a planned mill never lifts anybody.
    [DisallowMultipleComponent]
    public class WorkerPad : MonoBehaviour
    {
        /// The walking top, x by z metres in this transform's own frame
        /// (the building's yaw; unscaled).
        [SerializeField] private Vector2 size;
        /// `Worker_Stand`: where the gate's lane starts.
        [SerializeField] private Transform stand;
        /// `Worker_Approach`: the gate. Null = a pad with no gate.
        [SerializeField] private Transform approach;

        /// Half-width of the lane from the stand to the approach mark: a
        /// body within it is already on its way in or out through the gate.
        const float LaneHalfWidth = 0.5f;
        /// The same arrival radius `CampWorker.Walk` uses.
        const float Arrive = 0.35f;

        static readonly List<WorkerPad> live = new List<WorkerPad>();

        Building building;

        /// Editor-side setup (`MillL1Import`); saved with the prefab.
        public void Configure(Vector2 walkTop, Transform workerStand, Transform workerApproach)
        {
            size = walkTop;
            stand = workerStand;
            approach = workerApproach;
        }

        public Vector2 Size => size;

        void OnEnable() { if (!live.Contains(this)) live.Add(this); }
        void OnDisable() { live.Remove(this); }

        /// On a raised building, not a ghost. Looked up late: `BuildingFactory`
        /// adds the `Building` after the model is instantiated.
        bool Real
        {
            get
            {
                if (building == null) building = GetComponentInParent<Building>();
                return building != null;
            }
        }

        float Top => transform.position.y;

        bool Contains(Vector3 p, float margin)
        {
            Vector3 l = Quaternion.Inverse(transform.rotation) * (p - transform.position);
            return Mathf.Abs(l.x) <= size.x * 0.5f + margin && Mathf.Abs(l.z) <= size.y * 0.5f + margin;
        }

        bool InLane(Vector3 p)
        {
            if (stand == null || approach == null) return false;
            Vector2 a = new Vector2(stand.position.x, stand.position.z);
            Vector2 b = new Vector2(approach.position.x, approach.position.z);
            Vector2 q = new Vector2(p.x, p.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - q).sqrMagnitude <= LaneHalfWidth * LaneHalfWidth;
        }

        /// **Where a body at `p` stands**, given the terrain height there:
        /// the pad's top when `p` is over a real pad (never below the
        /// terrain, for a pad on a slope), else the terrain. One rectangle
        /// test per standing mill, so it is safe on every walk step.
        public static float Foot(Vector3 p, float ground)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var pad = live[i];
                if (pad == null || !pad.Contains(p, 0f) || !pad.Real) continue;
                return Mathf.Max(ground, pad.Top);
            }
            return ground;
        }

        /// **The rear gate.** True with `via` = the approach mark when the
        /// walk `here` -> `to` gets on to (or off) a gated pad from anywhere
        /// but its lane; false (walk straight at `to`) otherwise.
        public static bool Gate(Vector3 here, Vector3 to, out Vector3 via)
        {
            via = to;
            for (int i = 0; i < live.Count; i++)
            {
                var pad = live[i];
                if (pad == null || pad.approach == null || pad.stand == null || !pad.Real) continue;
                bool toOn = pad.Contains(to, 0.05f);
                bool hereOn = pad.Contains(here, 0f);
                bool lane = pad.InLane(here);
                Vector3 a = pad.approach.position;
                if (toOn && !hereOn && !lane) { via = a; return true; }
                if (!toOn && (hereOn || lane) && FlatDistance(here, a) > Arrive) { via = a; return true; }
            }
            return false;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
