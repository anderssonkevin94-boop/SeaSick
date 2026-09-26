using UnityEngine;

namespace SeaSick.World
{
    /// **A standing ladder chain up a cliff (2026-09-27).** See
    /// `LadderLayout` for the shape and the numbers, `Outpost.Ladders` for
    /// siting and raising, `CampPath` for the link it adds (hands and
    /// raiders may use it, animals never ask the camp's map), and
    /// `LadderClimb` for the walk.
    ///
    /// The truth is the ledger row (`BuiltLadder`); this is its rendering
    /// plus the shape the walkers read.
    public class Ladder : MonoBehaviour
    {
        public Outpost Camp { get; private set; }
        public BuiltLadder Row { get; private set; }
        public LadderLayout.Shape Shape { get; private set; }

        public Vector3 Foot => Shape != null ? Shape.foot : transform.position;
        public Vector3 Top => Shape != null ? Shape.top : transform.position;
        public float Rise => Shape != null ? Shape.Rise : 0f;
        public int Flights => Shape != null ? Shape.Flights : 0;
        public int Landings => Shape != null ? Shape.Landings : 0;
        public float ClimbSeconds => Shape != null ? Shape.ClimbSeconds : 0f;

        public void Configure(Outpost camp, LadderLayout.Shape shape, BuiltLadder row)
        {
            Camp = camp;
            Shape = shape;
            Row = row;
        }

        /// The chain's mesh is its own (built per chain); free it with it.
        void OnDestroy()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }

        /// Take it down for good: the ledger row goes, the link comes off
        /// the map. Nothing is refunded, the way a wall's tear-down is not.
        public void TearDown()
        {
            if (Camp != null) Camp.ForgetLadder(this);
            Destroy(gameObject);
        }
    }
}
