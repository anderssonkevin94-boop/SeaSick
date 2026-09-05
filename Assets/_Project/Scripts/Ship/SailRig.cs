using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Every sail on the ship, each turning about its own mast.
    ///
    /// `build_sails` in the Blender generator bakes the mast's x into the
    /// sail's VERTICES and leaves the object's origin at the hull origin. That
    /// is fine for a static sail and wrong the moment anything rotates it:
    /// `ShipMotor` was slerping the sail's own transform, so it swung about the
    /// centre of the ship and the after sail sailed off the quarter. It also
    /// only ever found the FIRST object called "Sail", so a two-master trimmed
    /// one sail and left the other rigid.
    ///
    /// Rather than re-export twenty hulls to move an origin, this gives each
    /// sail a pivot on the centreline at its own station and reparents it. The
    /// sail does not move; what changes is what it turns about.
    public class SailRig : MonoBehaviour
    {
        readonly List<Transform> pivots = new List<Transform>();
        // Half the height of each sail, measured when it was fitted. Growing a
        // sail about the pivot at its own centre would sink its foot through
        // the deck, so the pivot is lifted by the growth it causes.
        readonly List<float> halfHeights = new List<float>();
        readonly List<Vector3> seats = new List<Vector3>();
        ShipMotor motor;
        float area = 1f;

        void Awake() { motor = GetComponent<ShipMotor>(); }

        /// Rebuild the rig for a freshly swapped hull.
        public void Fit(Transform hullVisual)
        {
            pivots.Clear();
            halfHeights.Clear();
            seats.Clear();
            if (hullVisual == null) return;

            var sails = new List<Transform>();
            foreach (var t in hullVisual.GetComponentsInChildren<Transform>())
                if (t != hullVisual && t.name.Contains("Sail")) sails.Add(t);

            foreach (var sail in sails)
            {
                var mr = sail.GetComponent<Renderer>();
                if (mr == null) continue;

                // The mast stands on the centreline at the sail's own station.
                // Bounds are world-space, so bring the centre into ship-local
                // and keep only the fore-and-aft component: a sail is spread
                // ACROSS the beam, so its centre in x is the centreline anyway,
                // and forcing it removes any belly asymmetry from the answer.
                Vector3 local = transform.InverseTransformPoint(mr.bounds.center);
                var pivot = new GameObject(sail.name + "_Pivot");
                pivot.transform.SetParent(hullVisual, false);
                pivot.transform.localPosition = Vector3.zero;
                pivot.transform.localRotation = Quaternion.identity;
                // Position in SHIP space, then let the parenting sort itself
                // out — the hull visual may carry its own offset one day.
                pivot.transform.position = transform.TransformPoint(
                    new Vector3(0f, local.y, local.z));
                pivot.transform.rotation = transform.rotation;

                sail.SetParent(pivot.transform, true);   // keep the sail where it is
                pivots.Add(pivot.transform);
                halfHeights.Add(mr.bounds.extents.y);
                seats.Add(pivot.transform.localPosition);
            }
            Apply();
        }

        public int SailCount => pivots.Count;

        /// How much canvas she is carrying, as a multiple of the drawn sail.
        ///
        /// A "bigger suit of sails" that changes only a speed multiplier is a
        /// number in a panel. This is the same purchase, drawn: the sail grows
        /// about its FOOT, because a sail is bent to a yard and sheeted to the
        /// deck — it gets taller and wider, it does not sink into the ship.
        public void SetArea(float scale)
        {
            area = Mathf.Clamp(scale, 0.5f, 2f);
            Apply();
        }

        public float Area => area;

        void Apply()
        {
            for (int i = 0; i < pivots.Count; i++)
            {
                if (pivots[i] == null) continue;
                pivots[i].localScale = Vector3.one * area;
                // The pivot sits at the sail's centre, so scaling about it
                // drops the foot by (s-1) x half the sail's height. Lift the
                // pivot by exactly that and the foot stays where it was bent.
                var seat = seats[i];
                seat.y += (area - 1f) * halfHeights[i];
                pivots[i].localPosition = seat;
            }
        }

        void LateUpdate()
        {
            if (motor == null || !motor.UnderSail || pivots.Count == 0) return;
            var want = Quaternion.Euler(0f, motor.SailTrimDeg, 0f);
            float blend = Mathf.Clamp01(motor.SailTrimBlend);
            for (int i = 0; i < pivots.Count; i++)
            {
                if (pivots[i] == null) continue;
                // A little less angle on each successive mast going forward:
                // the headsails lie flatter than the main, which is what stops
                // a three-master reading as one flat sheet swinging in unison.
                float scale = 1f - i * 0.10f;
                pivots[i].localRotation = Quaternion.Slerp(
                    pivots[i].localRotation,
                    Quaternion.Euler(0f, motor.SailTrimDeg * scale, 0f), blend);
            }
        }
    }
}
