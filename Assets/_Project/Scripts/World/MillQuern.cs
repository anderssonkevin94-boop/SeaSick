using UnityEngine;

namespace SeaSick.World
{
    /// **The level 1 mill's quern runner turns with the miller's fists
    /// (2026-10-03).** The asset agent's README: `Crew_Mill` is a 1.5 s loop
    /// that turns the quern once, and `Quern_Runner` turns once in the same
    /// 1.5 s (take `Mill1_Grind`, frame 0 = his frame 0). Like
    /// `MillCrankWheels`, the runner is not a clip of its own here: each
    /// frame it reads where in HIS loop the body milling at `Worker_Stand`
    /// is (`VillagerActing.MillPhaseNear`) and stands at that angle, so it
    /// is in step with him whatever his clip's start, fade or rate. Nobody
    /// milling: it stops where it is.
    ///
    /// Set up by `Dev/Editor/GrainMillL1Import` (axis and turns measured off
    /// the FBX's object take) and saved on the `mill_l1` model.
    public class MillQuern : MonoBehaviour
    {
        [SerializeField] private Transform runner;
        [SerializeField] private Quaternion runnerRest = Quaternion.identity;
        [SerializeField] private Vector3 runnerAxis = Vector3.up;
        [SerializeField] private float turns = 1f;

        /// How far from `Worker_Stand` a milling body counts as ours.
        const float Reach = 0.9f;

        Transform stand;
        bool looked;

        /// The phase last shown (0..1), and whether anyone is milling now.
        public float Phase { get; private set; }
        public bool Turning { get; private set; }

        public void Configure(Transform runnerStone, Quaternion atZero, Vector3 localAxis, float turnsPerLoop)
        {
            runner = runnerStone; runnerRest = atZero; runnerAxis = localAxis; turns = turnsPerLoop;
        }

        void Update()
        {
            if (!looked)
            {
                looked = true;
                var b = GetComponentInParent<Building>();
                if (b == null) { enabled = false; return; }   // a ghost or a preview
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (BuildingFactory.Stem(t.name) == "Worker_Stand") { stand = t; break; }
            }
            if (stand == null) return;
            Turning = VillagerActing.MillPhaseNear(stand.position, Reach, out float p);
            if (!Turning) return;
            Show(p);
        }

        /// The runner stone at `phase` of the loop (0 = his frame 0).
        public void Show(float phase)
        {
            Phase = phase;
            if (runner != null) runner.localRotation = runnerRest * Quaternion.AngleAxis(360f * turns * phase, runnerAxis);
        }
    }
}
