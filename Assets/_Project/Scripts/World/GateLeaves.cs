using UnityEngine;

namespace SeaSick.World
{
    /// **The gate's two leaves, open for the camp and shut on a raid.**
    ///
    /// D3 (Kevin, 2026-09-23): gates are automatic -- open for hands,
    /// closed to raiders, nothing to toggle. The pathing already says so
    /// (`CampPath.MarkWall`: a gate blocks raiders only); this is what it
    /// looks like. Open (each leaf 100° outward, away from the camp) while
    /// nobody is raiding this camp, swung shut while a raid party is
    /// ashore here, in a short eased swing.
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
        const float SwingSeconds = 0.8f;
        const float PollSeconds = 0.25f;

        /// Find the hinges and draw the gate open. Called once, by
        /// `WallVisual.Build`, on the piece it has just placed.
        public void Fit()
        {
            left = transform.Find("Gate_Hinge");
            right = transform.Find("Gate_Hinge_Right");
            if (left != null) leftClosed = left.localRotation;
            if (right != null) rightClosed = right.localRotation;
            Pose(1f);
        }

        void Update()
        {
            if (seg == null)
            {
                seg = GetComponentInParent<WallSegment>();
                if (seg == null) { enabled = false; return; }
            }

            poll -= Time.deltaTime;
            if (poll <= 0f)
            {
                poll = PollSeconds;
                target = RaidersAshore(seg.Camp) ? 0f : 1f;
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
