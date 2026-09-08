using UnityEngine;

namespace SeaSick.Ocean
{
    /// Authoring for a hull's buoyancy probes: local positions along keel,
    /// bilge and rail, each with a submersion ramp radius and a share of the
    /// displaced volume. Gizmos show the layout in the editor.
    public class BuoyancyProbeSet : MonoBehaviour
    {
        [System.Serializable]
        public struct Probe
        {
            public Vector3 localPosition;
            [Tooltip("Vertical ramp over which the probe goes 0 -> fully submerged, metres.")]
            public float radius;
            [Tooltip("Fraction of the body's displaced volume this probe carries.")]
            public float volumeShare;
            [Tooltip("Rail probes also measure green water coming aboard.")]
            public bool isRail;
        }

        [SerializeField] Probe[] probes;

        public Probe[] Probes => probes;
        public int Count => probes?.Length ?? 0;

        public void SetProbes(Probe[] p) => probes = p;

        /// A sensible sloop layout: keel line fore/aft, four bilge corners,
        /// four rail points (reused for green-water measurement), two beam.
        public static Probe[] SloopLayout(float length, float beam, float keelY, float railY)
        {
            // The sloop's original radii, kept exactly so her gates stay valid.
            return HullLayout(length, beam, keelY, railY, 1.1f, 1.2f, 1.0f);
        }

        /// The same layout with the probe radii exposed. Radius is what sets
        /// how fast a probe saturates as it goes under, so it has to scale
        /// with the hull: the sloop's 1.1 m spheres on a shallow-draft boat
        /// make a cork that never settles. Everything else is proportional to
        /// length and beam already.
        public static Probe[] HullLayout(float length, float beam, float keelY, float railY,
            float stemRadius, float bodyRadius, float railRadius)
        {
            float l2 = length * 0.5f, b2 = beam * 0.5f;
            return new[]
            {
                P(0f, keelY, l2 * 0.9f, stemRadius, 0.10f),      // stem
                P(0f, keelY, -l2 * 0.9f, stemRadius, 0.10f),     // sternpost
                P(b2 * 0.7f, keelY + 0.3f, l2 * 0.45f, bodyRadius, 0.12f),
                P(-b2 * 0.7f, keelY + 0.3f, l2 * 0.45f, bodyRadius, 0.12f),
                P(b2 * 0.7f, keelY + 0.3f, -l2 * 0.45f, bodyRadius, 0.12f),
                P(-b2 * 0.7f, keelY + 0.3f, -l2 * 0.45f, bodyRadius, 0.12f),
                R(b2, railY, l2 * 0.35f, railRadius, 0.04f),     // rails: small share,
                R(-b2, railY, l2 * 0.35f, railRadius, 0.04f),    // they mostly measure
                R(b2, railY, -l2 * 0.35f, railRadius, 0.04f),    // green water
                R(-b2, railY, -l2 * 0.35f, railRadius, 0.04f),
                P(b2 * 0.9f, keelY + 0.6f, 0f, bodyRadius, 0.10f),
                P(-b2 * 0.9f, keelY + 0.6f, 0f, bodyRadius, 0.10f),
            };
        }

        /// The same topology, but every vertical offset and radius scaled by
        /// the hull's own DRAFT instead of typed in metres.
        ///
        /// `HullLayout` above hard-codes the bilge at keel+0.3 and the beam
        /// probes at keel+0.6, which are the paddle steamer's numbers. On the
        /// progression fleet those constants stop meaning anything: the log
        /// raft draws 0.29 m, so keel+0.6 puts her beam probes half a metre
        /// ABOVE her own rail, and the three-decker draws 5.4 m, so the same
        /// offsets bunch every probe into the bottom tenth of her hull. The
        /// radius has the same problem in the other direction — it is the band
        /// over which a probe goes dry to fully wet, so a 1.1 m sphere on a
        /// 0.29 m raft is a cork that never settles.
        ///
        /// Draft is the right ruler because it is the only length that says
        /// how deep the underwater body actually is.
        ///
        /// SUPERSEDED for the ladder by `HydrostaticLayout`: draft-scaling
        /// the offsets keeps the rig proportional to the hull, but the
        /// fractions being scaled were still authored, and they are what
        /// her stability is. Kept because the five progression hulls and
        /// the paddle steamer are floated by it and have no manifest
        /// hydrostatics to solve from.
        public static Probe[] FleetLayout(float length, float beam, float draft,
                                          float keelY, float railY)
        {
            float l2 = length * 0.5f, b2 = beam * 0.5f;
            float bilge = keelY + draft * 0.35f;
            float mid = keelY + draft * 0.55f;
            float r = draft * 0.85f, rr = draft * 0.75f;
            return new[]
            {
                P(0f, keelY, l2 * 0.9f, r, 0.10f),      // stem
                P(0f, keelY, -l2 * 0.9f, r, 0.10f),     // sternpost
                P(b2 * 0.7f, bilge, l2 * 0.45f, r, 0.12f),
                P(-b2 * 0.7f, bilge, l2 * 0.45f, r, 0.12f),
                P(b2 * 0.7f, bilge, -l2 * 0.45f, r, 0.12f),
                P(-b2 * 0.7f, bilge, -l2 * 0.45f, r, 0.12f),
                R(b2, railY, l2 * 0.35f, rr, 0.04f),
                R(-b2, railY, l2 * 0.35f, rr, 0.04f),
                R(b2, railY, -l2 * 0.35f, rr, 0.04f),
                R(-b2, railY, -l2 * 0.35f, rr, 0.04f),
                P(b2 * 0.9f, mid, 0f, r, 0.10f),
                P(-b2 * 0.9f, mid, 0f, r, 0.10f),
            };
        }

        /// The probe rig SOLVED from the hull's own hydrostatics, instead of
        /// authored fractions of her half-beam.
        ///
        /// `FleetLayout` below places probes where they LOOK right — the bilge
        /// at 0.7 of half-beam, the beam probes at 0.9 — and that decides her
        /// stability, because the righting moment a probe rig makes is its own
        /// geometry and nothing else. Measured on the brig: the rig was 2.8x
        /// stiffer than her arithmetic. The inclining experiment predicted
        /// 5.80 deg of heel and the water gave 2.05. What the player felt was
        /// not GM, so every stability number the yard printed was a caption on
        /// a different ship.
        ///
        /// Where the error came from, in two halves, both derivable:
        ///
        ///   BM. A probe's lift rises at rho*g*A per metre it goes under,
        ///   where A = totalVolume * share / radius is its share of the
        ///   WATERPLANE. Heel her and the restoring moment is sum(A x^2) --
        ///   the second moment of that waterplane, which is exactly what BM
        ///   measures. The authored rig put its probes at 0.32-0.41 of the
        ///   beam; the hull's own waterplane has an RMS radius of 0.24. Square
        ///   that and it is 1.95x too stiff.
        ///
        ///   KB. Buoyancy acts where the probes are, so the rig's centre of
        ///   buoyancy is its lift-weighted mean height. `probe_lift` raised
        ///   the keel line 0.55 x draft to make her FLOAT right, and the bilge
        ///   and beam offsets then stacked on top of it: measured KB 0.81 x
        ///   draft against the hull's 0.58. A quarter of a draft of stability
        ///   nobody typed and nobody wanted.
        ///
        /// So: place the rig to reproduce her measured volume, waterplane,
        /// second moment and centre of buoyancy, and GM stops being a caption.
        ///
        ///   * HALF her displacement rides the keel line, at TWICE the depth
        ///     of her centre of buoyancy. Its radius is twice its own depth,
        ///     which is the one setting where a probe is exactly submerged and
        ///     exactly at the reserve onset -- so this half is a constant lift
        ///     in still water and contributes no stiffness at all.
        ///   * The other half cuts the surface, sitting ON her drawn
        ///     waterline. The two halves average to her KB because the surface
        ///     half is at zero.
        ///   * That surface half is spread to the RMS radii of her own
        ///     waterplane -- x from her measured `bm_m`, z from her measured
        ///     area -- so heel and trim both restore with her numbers.
        ///
        /// `probe_lift` is not used and is not needed: she floats where she
        /// was drawn because the rig displaces what she displaces, by
        /// construction, rather than because a ratio was solved for once.
        ///
        /// `capacityM3` is the rig's volume at full submersion. Hand it to
        /// `BuoyantBody.ConfigureForHull` as volume/capacity and the float
        /// ratio stops being a constant too.
        public static Probe[] HydrostaticLayout(
            float length, float beam, float draft, float railY,
            float volumeM3, float waterplaneM2, float bmM, float kbAboveKeelM,
            out float capacityM3)
        {
            float T = Mathf.Max(0.05f, draft);
            float V = Mathf.Max(0.01f, volumeM3);
            float Aw = Mathf.Max(0.01f, waterplaneM2);

            // Her centre of buoyancy, below the drawn waterline. Every hull on
            // the ladder measures 0.57-0.66 of her draft, so the keel line
            // below lands well inside her; the clamp is for a hull that never
            // existed rather than one that does.
            float yB = Mathf.Min(-0.01f, kbAboveKeelM - T);
            float yK = Mathf.Max(2f * yB, -0.98f * T);
            float volK = V * (yB / yK);          // exactly half, unless clamped
            float volW = Mathf.Max(0.01f * V, V - volK);

            // The surface band. Its immersed half IS volW, so the band is her
            // mean immersed depth: 0.55 x draft on the skiff, 0.81 on the
            // three-decker -- near the 0.85 the storm terms were tuned at,
            // which is why this is a stability fix and not a seakeeping one.
            float rW = 2f * volW / Aw;
            float rK = 2f * (-yK);

            // Transverse: sum(A x^2) = I = BM x volume, with sum(A) = Aw, so
            // one radius does both. This is the whole fix in one line.
            float xq = Mathf.Sqrt(Mathf.Max(0f, bmM) * V / Aw);

            // Longitudinal wants the same radius about the other axis, and the
            // exporter does not emit it. Take it from the waterplane's own
            // fullness: a half-breadth of (B/2)(1 - |zeta|^n) has coefficient
            // Cw = n/(n+1), and integrating that family gives both radii. The
            // check is that the same family predicts the TRANSVERSE radius --
            // which we measured -- to within 3% on every rung of the ladder,
            // sign drifting with Cw the way a one-parameter shape should.
            float Cw = Mathf.Clamp(Aw / Mathf.Max(0.01f, length * beam), 0.3f, 0.95f);
            float shape = Cw / (1f - Cw);
            float zq = length * Mathf.Sqrt(
                (1f / 3f - 1f / (shape + 3f)) / (4f * Cw));

            // Three stations, so she reads a wave along her length instead of
            // at two points. The ends sit where the old stem and sternpost
            // did; midships carries whatever area is left over, which is most
            // of it -- as it should be on a hull that is fullest amidships.
            float zEnd = 0.40f * length;
            float endShare = Mathf.Clamp(zq * zq / (zEnd * zEnd), 0.02f, 0.49f);
            float aEnd = Aw * endShare * 0.25f;          // each of four
            float aMid = Aw * (1f - endShare) * 0.5f;    // each of two

            float b2 = beam * 0.45f;
            float rRail = T * 0.75f;
            // The hull rig at full submersion, plus the topsides. The rails
            // hold the same 15% of the rig they always did: dry at her marks
            // on every rung, so they change nothing about how she floats, and
            // they are what green water and the burial clamp read.
            float hull = volK + Aw * rW;
            float railEach = hull * (0.15f / 0.85f) * 0.25f;
            capacityM3 = hull + railEach * 4f;

            float kEnd = volK * endShare * 0.5f;         // each of two
            float kMid = volK * (1f - endShare) * 0.5f;  // each of two
            float inv = 1f / Mathf.Max(0.01f, capacityM3);

            return new[]
            {
                // The keel line: constant lift, no stiffness, all of her KB.
                P(0f, yK, zEnd, rK, kEnd * inv),
                P(0f, yK, -zEnd, rK, kEnd * inv),
                P(0f, yK, zEnd * 0.35f, rK, kMid * inv),
                P(0f, yK, -zEnd * 0.35f, rK, kMid * inv),
                // The waterplane: her area, her second moment, her trim.
                P(xq, 0f, zEnd, rW, aEnd * rW * inv),
                P(-xq, 0f, zEnd, rW, aEnd * rW * inv),
                P(xq, 0f, -zEnd, rW, aEnd * rW * inv),
                P(-xq, 0f, -zEnd, rW, aEnd * rW * inv),
                P(xq, 0f, 0f, rW, aMid * rW * inv),
                P(-xq, 0f, 0f, rW, aMid * rW * inv),
                // The rails, at the actual rail: green water and deck-edge
                // reserve, which is the one place a probe belongs outboard.
                R(b2, railY, zEnd * 0.6f, rRail, railEach * inv),
                R(-b2, railY, zEnd * 0.6f, rRail, railEach * inv),
                R(b2, railY, -zEnd * 0.6f, rRail, railEach * inv),
                R(-b2, railY, -zEnd * 0.6f, rRail, railEach * inv),
            };
        }

        /// What a rig ACTUALLY does at rest, read back off the probe array.
        ///
        /// This is the same arithmetic `BuoyantBody` runs every step, taken at
        /// the drawn waterline and with the heel derivative done by hand, so a
        /// layout can be checked against the hull's book without floating it.
        /// A probe only contributes waterplane -- and so stiffness -- while it
        /// is strictly inside its own ramp: saturated or dry, moving it up or
        /// down buys nothing, which is exactly why the rig's stiffness is not
        /// something you can read off the picture.
        public static void RestHydrostatics(Probe[] probes, float totalVolume,
            float draft, out float volume, out float waterplane,
            out float bm, out float kb)
        {
            volume = waterplane = bm = kb = 0f;
            if (probes == null || probes.Length == 0) return;
            float lift = 0f, moment = 0f, second = 0f;
            foreach (var p in probes)
            {
                float cap = totalVolume * p.volumeShare;
                float sub = Mathf.Clamp01(-p.localPosition.y / p.radius + 0.5f);
                lift += cap * sub;
                moment += cap * sub * p.localPosition.y;
                if (sub <= 0f || sub >= 1f) continue;       // clamped: no stiffness
                float area = cap / p.radius;
                waterplane += area;
                second += area * p.localPosition.x * p.localPosition.x;
            }
            volume = lift;
            bm = lift > 1e-6f ? second / lift : 0f;
            kb = lift > 1e-6f ? moment / lift + draft : 0f;
        }

        static Probe P(float x, float y, float z, float r, float share) =>
            new Probe { localPosition = new Vector3(x, y, z), radius = r, volumeShare = share };

        static Probe R(float x, float y, float z, float r, float share) =>
            new Probe { localPosition = new Vector3(x, y, z), radius = r, volumeShare = share, isRail = true };

        void OnDrawGizmosSelected()
        {
            if (probes == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            foreach (var p in probes)
            {
                Vector3 w = transform.TransformPoint(p.localPosition);
                Gizmos.DrawWireSphere(w, Mathf.Max(0.15f, p.volumeShare * 2f));
                Gizmos.DrawLine(w, w + Vector3.down * p.radius);
            }
        }
    }
}
