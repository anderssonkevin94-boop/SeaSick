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
