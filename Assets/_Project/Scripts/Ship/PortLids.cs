using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship
{
    /// The gun-port lids: shut by default, swung open where a gun is run out.
    ///
    /// **This is the thing that lets the hull carry every port her decks allow
    /// while still showing the battery she actually has.** For one afternoon
    /// the generator solved that by cutting fewer holes — ten in the side of a
    /// forty-six metre first-rate — and she read as an unfinished hull. Holes
    /// are structural and belong to her length and her decks; the number of
    /// guns is a bay decision that changes every time the player buys one. A
    /// lid is exactly the joint between the two, and it is what a real ship
    /// used for the same purpose.
    ///
    /// Every lid is baked SHUT in the mesh, so a ship with no `PortLids` on it
    /// looks like a ship at rest rather than one cleared for action. That is
    /// the right default for the failure case.
    [DisallowMultipleComponent]
    public class PortLids : MonoBehaviour
    {
        /// How far a lid swings. 84° is out and barely rising, which is what
        /// the generator used to bake into every lid on the ship.
        [SerializeField] float openDeg = 84f;
        /// Degrees a second. Ports go up together and it takes a moment —
        /// that moment is most of why the effect is worth having.
        [SerializeField] float swingRate = 150f;

        class Lid
        {
            public Transform pivot;
            public float sign;        // which way this pivot opens
            public float want, have;  // degrees
        }

        readonly List<Lid> lids = new List<Lid>();
        /// Where each lid's PORT is, in ship space: x is which side (±1), y is
        /// the sill and z the station. Kept beside `lids`, index for index.
        readonly List<Vector3> seats = new List<Vector3>();

        /// Rebuild for a freshly swapped hull, then open the lids that have a
        /// gun behind them. `starboardLocal` is the same list the battery is
        /// fitted from, in ship-local space; port is mirrored, as it is there.
        public void Fit(Transform hullVisual, IList<Vector3> starboardLocal)
        {
            // Give back any hinge this component made earlier. Fitting twice
            // on the same visual would otherwise nest a hinge inside a hinge,
            // and the second one would swing about the first.
            foreach (var l in lids)
            {
                if (l.pivot == null) continue;
                for (int i = l.pivot.childCount - 1; i >= 0; i--)
                    l.pivot.GetChild(i).SetParent(l.pivot.parent, true);
                Destroy(l.pivot.gameObject);
            }
            lids.Clear();
            seats.Clear();
            if (hullVisual == null) return;

            foreach (var t in hullVisual.GetComponentsInChildren<Transform>())
            {
                if (t == hullVisual || !t.name.Contains("PortLid")) continue;
                var mr = t.GetComponent<Renderer>();
                var mf = t.GetComponent<MeshFilter>();
                if (mr == null || mf == null || mf.sharedMesh == null) continue;

                // The lid's box in SHIP space. Deliberately NOT
                // `Renderer.bounds`, which is axis-aligned in WORLD space: a
                // ship steering north-east measures a lid across her beam.
                Bounds b = mf.sharedMesh.bounds;
                Vector3 lo = Vector3.one * float.MaxValue;
                Vector3 hi = -lo;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3(
                        (c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1,
                        (c & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(
                        t.TransformPoint(corner));
                    lo = Vector3.Min(lo, p);
                    hi = Vector3.Max(hi, p);
                }

                // Hinged along the HEAD of the port — the top of the lid, on
                // the face against the planking — running fore and aft.
                float side = Mathf.Sign((lo.x + hi.x) * 0.5f);
                var hinge = new Vector3(side > 0 ? lo.x : hi.x, hi.y,
                                        (lo.z + hi.z) * 0.5f);

                var pivot = new GameObject(t.name + "_Hinge");
                pivot.transform.SetParent(hullVisual, false);
                pivot.transform.position = transform.TransformPoint(hinge);
                pivot.transform.rotation = transform.rotation;
                t.SetParent(pivot.transform, true);

                // **Which way is out is MEASURED, not reasoned about.** The
                // asset is yawed -90° on export and the lid's own axes come
                // through the FBX importer, so working the handedness out on
                // paper is how you ship a fleet whose ports open inboard.
                // Swing it both ways and keep the one that lifts the lid's
                // far edge away from the centreline.
                var seat = pivot.transform.localRotation;
                float best = 0f, bestScore = float.NegativeInfinity;
                foreach (float s in new[] { 1f, -1f })
                {
                    pivot.transform.localRotation =
                        seat * Quaternion.Euler(0f, 0f, s * openDeg);
                    var p = transform.InverseTransformPoint(
                        t.TransformPoint(b.center));
                    float score = Mathf.Abs(p.x) + p.y;   // out, and rising
                    if (score > bestScore) { bestScore = score; best = s; }
                }
                pivot.transform.localRotation = seat;

                lids.Add(new Lid { pivot = pivot.transform, sign = best,
                                   want = 0f, have = 0f });
                // Matched to a gun by the SILL, not the hinge: the gun decks
                // are 2.3 m apart and a gun stands about 0.23 m under its own
                // sill, so no row can be mistaken for its neighbour.
                seats.Add(new Vector3(side, lo.y, (lo.z + hi.z) * 0.5f));
            }
            Open(starboardLocal);
            Snap();
        }

        /// Put every lid where it belongs with no swing.
        ///
        /// What `Fit` ends with: a ship that has just been built, swapped or
        /// loaded should already BE as she is, not caught halfway through
        /// running out her guns. Only a later `Open` animates.
        public void Snap()
        {
            foreach (var l in lids)
            {
                l.have = l.want;
                if (l.pivot != null)
                    l.pivot.localRotation = Quaternion.Euler(0f, 0f, l.sign * l.have);
            }
        }

        /// Run out the guns: open every lid with one behind it, shut the rest.
        public void Open(IList<Vector3> starboardLocal)
        {
            for (int i = 0; i < lids.Count; i++) lids[i].want = 0f;
            if (starboardLocal == null) return;

            // **One gun, one port.** The gun stands at its BAY and the port is
            // cut at a station the loft has raked, so on a hull with 4.8 m of
            // rake an upper-deck port can sit most of a bay forward of the gun
            // that belongs to it. Nearest-lid on its own then let two guns
            // agree on the same lid and leave another shut — 70 of 72 open on
            // the three-decker. Taking each lid out of the running once it is
            // claimed fixes it without needing the rake to be modelled here.
            var taken = new HashSet<int>();
            foreach (var g in starboardLocal)
            {
                // One gun is a gun EACH SIDE — that is what a battery cell is
                // — so it opens a lid to starboard and its mirror to port.
                for (int s = -1; s <= 1; s += 2)
                {
                    int pick = -1;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < lids.Count && i < seats.Count; i++)
                    {
                        if (taken.Contains(i)) continue;
                        if (Mathf.Sign(seats[i].x) != s) continue;
                        float d = new Vector2(seats[i].z - g.z,
                                              seats[i].y - g.y).magnitude;
                        if (d < bestD) { bestD = d; pick = i; }
                    }
                    // A gun with no port left within reach of it is a gun the
                    // yard should not have fitted; leave every lid shut rather
                    // than open one on the wrong deck.
                    if (pick >= 0 && bestD < 4f)
                    {
                        lids[pick].want = openDeg;
                        taken.Add(pick);
                    }
                }
            }
        }

        public int LidCount => lids.Count;
        public int OpenCount
        {
            get
            {
                int n = 0;
                foreach (var l in lids) if (l.want > 0.5f) n++;
                return n;
            }
        }

        void Update()
        {
            float step = swingRate * Time.deltaTime;
            foreach (var l in lids)
            {
                if (l.pivot == null || Mathf.Approximately(l.have, l.want))
                    continue;
                l.have = Mathf.MoveTowards(l.have, l.want, step);
                l.pivot.localRotation = Quaternion.Euler(0f, 0f, l.sign * l.have);
            }
        }
    }
}
