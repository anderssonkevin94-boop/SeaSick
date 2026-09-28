using UnityEngine;
using SeaSick.Crew;
using SeaSick.World.Life;

namespace SeaSick.Ship.Overboard
{
    /// **Jolly boat** (phase 8 shipyard module, docs/PLAN-DEATH-RESCUE.md
    /// "Shipyard modules": "big ships only... launches from the side, rows
    /// out on the real waves... reaches the swimmer, returns, and calls the
    /// same OnHauled -> Rescue"). Spawned by `JollyBoatDispatch`, never
    /// directly.
    ///
    /// Three legs: row OUT to the target, TOW it back alongside (reusing
    /// `IOverboardTarget.BeingHauled`/`HaulAnchor`, the exact mechanism a
    /// rail haul already uses to pull a swimmer or a floating crate), then
    /// hand off through the SAME `OnHauled` every other pickup calls
    /// (`Swimmer.Rescue` / `FloatingCargo.Recover`) once alongside. Rides
    /// the real wave height every frame off `OceanSampler`, same one-sample
    /// budget note as `Swimmer.Update`.
    ///
    /// **Astra**: primitive placeholders only (a flattened cube hull, a
    /// capsule rower) — replace with a real jolly-boat model, a rower
    /// animation, and an oar-stroke visual whenever there's time for it.
    public class JollyBoat : MonoBehaviour
    {
        enum Phase { Out, Towing, Back }

        Phase phase;
        IOverboardTarget target;
        CrewAgent hand;
        Transform hull;
        Vector3 homeLocalPos;

        const float ArriveRadius = 1.5f;

        /// **`JollyBoatDispatch` only.** `hand` must already be off the
        /// roster (`CrewAgent.BeginJollyBoatDuty` already returned true).
        public static JollyBoat Launch(Transform hull, CrewAgent hand, IOverboardTarget target)
        {
            if (hull == null || hand == null || target == null) return null;
            var go = new GameObject("JollyBoat");
            var boat = go.AddComponent<JollyBoat>();
            boat.hull = hull;
            boat.hand = hand;
            boat.target = target;
            // Roughly amidships, starboard side -- launched "from the side"
            // per the build brief; a real davit position is Astra's to set
            // once she has a hull to hang it off.
            boat.homeLocalPos = new Vector3(2.4f, 0f, 0f);
            go.transform.position = hull.TransformPoint(boat.homeLocalPos);
            boat.BuildVisual();
            boat.phase = Phase.Out;
            return boat;
        }

        void BuildVisual()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Hull";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(1.1f, 0.35f, 2.6f);
            body.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            var bc = body.GetComponent<Collider>();
            if (bc != null) Destroy(bc);

            var rower = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rower.name = "Rower";
            rower.transform.SetParent(transform, false);
            rower.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
            rower.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            var rc = rower.GetComponent<Collider>();
            if (rc != null) Destroy(rc);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float speed = OverboardTuning.JollyBoatRowSpeed;

            switch (phase)
            {
                case Phase.Out:
                    if (target == null || target.Resolved) { phase = Phase.Back; return; }
                    RowToward(target.WorldPosition, speed, dt);
                    if (FlatDistance(transform.position, target.WorldPosition) <= ArriveRadius)
                        phase = Phase.Towing;
                    break;

                case Phase.Towing:
                    if (target == null || target.Resolved) { phase = Phase.Back; return; }
                    target.BeingHauled = true;
                    target.HaulAnchor = transform.position;
                    HomeLeg(speed, dt, out bool arrivedTowing);
                    if (arrivedTowing)
                    {
                        string rescuer = hand != null ? hand.DisplayName : "the jolly boat";
                        target.BeingHauled = false;
                        target.OnHauled(rescuer);
                        target = null;
                        phase = Phase.Back;
                    }
                    break;

                case Phase.Back:
                    HomeLeg(speed, dt, out bool arrivedBack);
                    if (arrivedBack) Finish();
                    break;
            }
        }

        void HomeLeg(float speed, float dt, out bool arrived)
        {
            Vector3 dest = hull != null ? hull.TransformPoint(homeLocalPos) : transform.position;
            RowToward(dest, speed, dt);
            arrived = FlatDistance(transform.position, dest) <= ArriveRadius;
        }

        void RowToward(Vector3 worldDest, float speed, float dt)
        {
            Vector3 pos = transform.position;
            Vector3 flatDest = new Vector3(worldDest.x, pos.y, worldDest.z);
            Vector3 next = Vector3.MoveTowards(pos, flatDest, speed * dt);
            if (Ocean.OceanSampler.Ready) next.y = Ocean.OceanSampler.SampleImmediate(next).height + 0.1f;
            Vector3 face = flatDest - pos;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            transform.position = next;
        }

        static float FlatDistance(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        void Finish()
        {
            if (hand != null) hand.EndJollyBoatDuty();
            JollyBoatDispatch.Cleared(this);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (target != null) target.BeingHauled = false;
        }
    }
}
