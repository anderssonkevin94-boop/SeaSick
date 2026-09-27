using UnityEngine;

namespace SeaSick.World
{
    /// **A standing length of the player's road (2026-09-27).** The truth
    /// is the ledger row (`BuiltRoad`); the drawing is the camp's one road
    /// mesh (`CampRoads`). This object is only what a TAP finds: a flat
    /// trigger box along the segment, so the road opens its sheet
    /// (`UI/Sheets/RoadSheet`, "Tear down") like any other building.
    public class RoadSegment : MonoBehaviour
    {
        public Outpost Camp { get; private set; }
        public BuiltRoad Row { get; private set; }

        public Vector3 A => Row != null ? Row.A : transform.position;
        public Vector3 B => Row != null ? Row.B : transform.position;
        public float Length => Row != null ? Vector3.Distance(Row.A, Row.B) : 0f;

        public void Configure(Outpost camp, BuiltRoad row)
        {
            Camp = camp;
            Row = row;
            Vector3 a = row.A, b = row.B;
            a.y = camp.GroundAt(a);
            b.y = camp.GroundAt(b);
            Vector3 run = b - a; run.y = 0f;
            float len = Mathf.Max(0.5f, run.magnitude);
            Vector3 mid = 0.5f * (a + b);
            transform.SetPositionAndRotation(mid,
                run.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(run.normalized, Vector3.up) : Quaternion.identity);
            var box = gameObject.GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            // A trigger: taps find it (the picker collides with triggers),
            // nothing else's raycasts do. Tall enough to poke out of a slope.
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.2f + 0.5f * Mathf.Abs(b.y - a.y), 0f);
            box.size = new Vector3(RoadStrip.Width, 0.8f + Mathf.Abs(b.y - a.y), len);
        }

        /// Take it up for good: the ledger row goes, the road comes off the
        /// map and the drawing. Nothing is refunded, like a wall.
        public void TearDown()
        {
            if (Camp != null) Camp.ForgetRoad(this);
            Destroy(gameObject);
        }
    }
}
