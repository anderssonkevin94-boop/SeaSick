using UnityEngine;

namespace SeaSick.Ocean2
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
        }

        [SerializeField] Probe[] probes;

        public Probe[] Probes => probes;
        public int Count => probes?.Length ?? 0;

        public void SetProbes(Probe[] p) => probes = p;

        /// A sensible sloop layout: keel line fore/aft, four bilge corners,
        /// four rail points (reused for green-water measurement), two beam.
        public static Probe[] SloopLayout(float length, float beam, float keelY, float railY)
        {
            float l2 = length * 0.5f, b2 = beam * 0.5f;
            return new[]
            {
                P(0f, keelY, l2 * 0.9f, 1.1f, 0.10f),      // stem
                P(0f, keelY, -l2 * 0.9f, 1.1f, 0.10f),     // sternpost
                P(b2 * 0.7f, keelY + 0.3f, l2 * 0.45f, 1.2f, 0.12f),
                P(-b2 * 0.7f, keelY + 0.3f, l2 * 0.45f, 1.2f, 0.12f),
                P(b2 * 0.7f, keelY + 0.3f, -l2 * 0.45f, 1.2f, 0.12f),
                P(-b2 * 0.7f, keelY + 0.3f, -l2 * 0.45f, 1.2f, 0.12f),
                P(b2, railY, l2 * 0.35f, 1.0f, 0.04f),     // rails: small share,
                P(-b2, railY, l2 * 0.35f, 1.0f, 0.04f),    // they mostly measure
                P(b2, railY, -l2 * 0.35f, 1.0f, 0.04f),    // green water
                P(-b2, railY, -l2 * 0.35f, 1.0f, 0.04f),
                P(b2 * 0.9f, keelY + 0.6f, 0f, 1.2f, 0.10f),
                P(-b2 * 0.9f, keelY + 0.6f, 0f, 1.2f, 0.10f),
            };
        }

        static Probe P(float x, float y, float z, float r, float share) =>
            new Probe { localPosition = new Vector3(x, y, z), radius = r, volumeShare = share };

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
