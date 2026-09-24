using UnityEngine;

namespace SeaSick.World
{
    /// **The gate's two leaves: swing open for a hand who is coming
    /// through, shut behind him, shut for the whole raid.**
    ///
    /// D3 (Kevin, 2026-09-23): gates are automatic -- open for hands,
    /// closed to raiders, nothing to toggle. The pathing already says so
    /// (`CampPath.MarkWall`: a gate blocks raiders only); this is what it
    /// looks like. **2026-09-24** (Kevin, phone: *"the gate asset stays
    /// closed so they clip through the closed gate"*): the leaves used to
    /// stand open all day and shut only on a raid; now they open (each leaf
    /// 100° outward, away from the camp) when a villager body is within
    /// `OpenRadius` of the passage and close again when nobody is, in a
    /// swing short enough (`SwingSeconds`) to be fully open before a hand
    /// walking at `CampWorker.Speed` from the radius reaches the leaves.
    /// A raid party ashore keeps them shut whoever is near.
    ///
    /// Lives on the `Gate_L1` piece in the segment's "whole" state, so a
    /// breached gate (the "broken" state, no leaves) never ticks it. A
    /// blueprint ghost has no segment to ask and stays drawn open.
    public class GateLeaves : MonoBehaviour
    {
        [SerializeField] Transform left, right;
        [SerializeField] Quaternion leftClosed, rightClosed;

        /// 0 shut, 1 open.
        float open = 1f;
        float target = 1f;
        float poll;
        WallSegment seg;

        /// Degrees, from the kit (`Gate_L1` README): outward is the front,
        /// the side away from the rails, which `WallVisual` turns away from
        /// the camp. +/- measured in the wrapper prefab: the left leaf opens
        /// outward on +100 about up, the right on -100.
        const float OpenAngle = 100f;
        /// 0.6 s to swing; a hand at 2.6 m/s covers 1.6 m in that time, so
        /// from `OpenRadius` the leaves are wide before he is at the frame.
        const float SwingSeconds = 0.6f;
        const float PollSeconds = 0.15f;
        /// Metres from the passage centre within which a villager body
        /// opens the gate. Wide enough for the swing, narrow enough that a
        /// hand working beside the wall does not hold it open.
        const float OpenRadius = 5f;
        /// Give the segment this long to appear before giving up.
        const float SegmentGraceSeconds = 5f;
        float segWait;
        Transform passage;

        /// Find the hinges and draw the gate open. Called once, by
        /// `WallVisual.Build`, on the piece it has just placed.
        public void Fit()
        {
            left = Find(transform, "Gate_Hinge");
            right = Find(transform, "Gate_Hinge_Right");
            passage = Find(transform, "Gate__Passage");
            if (left != null) leftClosed = left.localRotation;
            if (right != null) rightClosed = right.localRotation;
            if (left == null || right == null)
                Debug.LogWarning($"[Camp] GateLeaves on {name}: hinge(s) not found (left={left != null}, right={right != null}); the gate will not swing");
            // Modelled closed (kit README); drawn closed until somebody comes.
            open = 0f; target = 0f;
            Pose(0f);
        }

        /// `Transform.Find` sees direct children only; the kit keeps the
        /// hinges at the root today, but a re-export that nests them under
        /// the posts must not leave a gate that never swings.
        static Transform Find(Transform root, string name)
        {
            var direct = root.Find(name);
            if (direct != null) return direct;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// The middle of the opening: the kit's `Gate__Passage` marker, else
        /// the module's centre (root X=0 at one end, X=3 at the other).
        Vector3 PassagePoint => passage != null ? passage.position
                                                : transform.TransformPoint(new Vector3(WallVisual.GateSpan * 0.5f, 0f, 0f));

        /// Is a villager body close enough to want the gate open. Walks
        /// `CampWorker.Bodies` (tens at most) once per poll; no allocation.
        bool HandNear()
        {
            var bodies = CampWorker.Bodies;
            if (bodies.Count == 0) return false;
            Vector3 at = PassagePoint;
            float r2 = OpenRadius * OpenRadius;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                Vector3 d = b.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude <= r2) return true;
            }
            return false;
        }

        void Update()
        {
            if (seg == null)
            {
                // `Configure` runs after `WallVisual.Build`, and a ghost has
                // no segment at all: keep asking for a few seconds, then stop.
                seg = GetComponentInParent<WallSegment>();
                if (seg == null)
                {
                    segWait += Time.deltaTime;
                    if (segWait > SegmentGraceSeconds) enabled = false;
                    return;
                }
            }

            poll -= Time.deltaTime;
            if (poll <= 0f)
            {
                poll = PollSeconds;
                target = RaidersAshore(seg.Camp) ? 0f : (HandNear() ? 1f : 0f);
            }
            if (Mathf.Approximately(open, target)) return;
            open = Mathf.MoveTowards(open, target, Time.deltaTime / SwingSeconds);
            Pose(Mathf.SmoothStep(0f, 1f, open));
        }

        static bool RaidersAshore(Outpost camp)
        {
            var party = Combat.RaidParty.Active;
            return camp != null && party != null && party.Camp == camp && party.Ashore > 0;
        }

        void Pose(float t)
        {
            if (left != null) left.localRotation = Quaternion.AngleAxis(OpenAngle * t, Vector3.up) * leftClosed;
            if (right != null) right.localRotation = Quaternion.AngleAxis(-OpenAngle * t, Vector3.up) * rightClosed;
        }
    }
}
