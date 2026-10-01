using UnityEngine;

namespace SeaSick.World
{
    /// **The level 2 sawmill's crank wheel and saw wheel turn with the
    /// sawyer's fists (2026-10-01).** The asset agent's README: `Crew_Crank`
    /// is a 1.2 s loop that turns the crank once; `Mill2_CrankWheel` turns
    /// once and `Saw2_Wheel` five times in the same 1.2 s, frame 0 = his
    /// frame 0. So the wheels are not a clip of their own here: each frame
    /// they read where in HIS loop the body cranking at `Worker_Stand` is
    /// (`VillagerActing.CrankPhaseNear`) and stand at that angle -- in step
    /// with him whatever his clip's start, fade or rate. Nobody cranking:
    /// they stop where they are.
    ///
    /// Set up by `Dev/Editor/MillL2Import` (axes measured off the FBX's
    /// object take) and saved on the `sawmill_l2` model.
    public class MillCrankWheels : MonoBehaviour
    {
        [SerializeField] private Transform crank;
        [SerializeField] private Quaternion crankRest = Quaternion.identity;
        [SerializeField] private Vector3 crankAxis = Vector3.up;
        [SerializeField] private float crankTurns = 1f;
        [SerializeField] private Transform saw;
        [SerializeField] private Quaternion sawRest = Quaternion.identity;
        [SerializeField] private Vector3 sawAxis = Vector3.up;
        [SerializeField] private float sawTurns = 5f;
        [SerializeField] private float loopSeconds = 1.2f;

        /// How far from `Worker_Stand` a cranking body counts as ours.
        const float Reach = 0.9f;

        Transform stand;
        bool looked;

        /// The phase last shown (0..1), and whether anyone is cranking now.
        public float Phase { get; private set; }
        public bool Turning { get; private set; }

        public void Configure(Transform crankWheel, Quaternion crankAtZero, Vector3 crankLocalAxis, float crankTurnsPerLoop,
            Transform sawWheel, Quaternion sawAtZero, Vector3 sawLocalAxis, float sawTurnsPerLoop, float loop)
        {
            crank = crankWheel; crankRest = crankAtZero; crankAxis = crankLocalAxis; crankTurns = crankTurnsPerLoop;
            saw = sawWheel; sawRest = sawAtZero; sawAxis = sawLocalAxis; sawTurns = sawTurnsPerLoop;
            loopSeconds = loop;
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
            Turning = VillagerActing.CrankPhaseNear(stand.position, Reach, out float p);
            if (!Turning) return;
            Show(p);
        }

        /// Both wheels at `phase` of the loop (0 = his frame 0).
        public void Show(float phase)
        {
            Phase = phase;
            if (crank != null) crank.localRotation = crankRest * Quaternion.AngleAxis(360f * crankTurns * phase, crankAxis);
            if (saw != null) saw.localRotation = sawRest * Quaternion.AngleAxis(360f * sawTurns * phase, sawAxis);
        }
    }
}
