using UnityEngine;

namespace SeaSick.World
{
    /// Builds a fresh archipelago at runtime: small shelter rocks close in,
    /// bigger and richer islands further out. Distance is the difficulty
    /// curve, and the scatter of small islands is what makes a long voyage
    /// survivable — every one of them is a place to hide from a swell.
    public class ArchipelagoGenerator : MonoBehaviour
    {
        [System.Serializable]
        public struct ResourceKind
        {
            public string name;
            public Color beaconColor;
            public float minRing; // 0..1 — how far out this resource starts appearing
        }

        [SerializeField] int islandCount = 15;
        [SerializeField] float innerDistance = 190f;
        [SerializeField] float outerDistance = 880f;
        [SerializeField] Vector2 radiusRange = new Vector2(13f, 72f);
        [SerializeField] float richnessPerRadius = 0.95f;
        [SerializeField] float shelterOnlyBelowRadius = 21f;
        [SerializeField] int seed = 0; // 0 = random each run

        [SerializeField]
        ResourceKind[] kinds =
        {
            new ResourceKind { name = "Timber", beaconColor = new Color(0.55f, 0.85f, 0.4f),  minRing = 0f },
            new ResourceKind { name = "Stone",  beaconColor = new Color(0.75f, 0.78f, 0.85f), minRing = 0.3f },
            new ResourceKind { name = "Ore",    beaconColor = new Color(1f, 0.85f, 0.35f),    minRing = 0.55f },
            new ResourceKind { name = "Spice",  beaconColor = new Color(0.95f, 0.45f, 0.75f), minRing = 0.78f },
        };

        Material sandMat, grassMat;

        void Awake()
        {
            if (seed != 0) Random.InitState(seed);

            // Clear any authored islands except the home island.
            foreach (var existing in FindObjectsByType<Island>(FindObjectsSortMode.None))
                if (!existing.IsHome) Destroy(existing.gameObject);
            var stale = GameObject.Find("Island_Windward");
            if (stale != null) Destroy(stale);

            sandMat = MakeMat(new Color(0.85f, 0.74f, 0.52f));
            grassMat = MakeMat(new Color(0.36f, 0.62f, 0.32f));

            float goldenAngle = 137.508f;
            for (int i = 0; i < islandCount; i++)
            {
                float ring = (i + 0.5f) / islandCount;
                float dist = Mathf.Lerp(innerDistance, outerDistance, ring) * Random.Range(0.82f, 1.18f);
                float ang = (i * goldenAngle + Random.Range(-14f, 14f)) * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);

                // Bigger islands sit further out — the long voyage has to pay.
                float radius = Mathf.Lerp(radiusRange.x, radiusRange.y, ring) * Random.Range(0.78f, 1.24f);
                radius = Mathf.Clamp(radius, radiusRange.x, radiusRange.y * 1.1f);

                bool shelterOnly = radius < shelterOnlyBelowRadius;
                var kind = PickKind(ring);
                float richness = shelterOnly ? 0f : Mathf.Round(radius * richnessPerRadius * Random.Range(0.8f, 1.2f));

                Build($"Island_{kind.name}_{i}", pos, radius, kind, richness, shelterOnly);
            }
        }

        ResourceKind PickKind(float ring)
        {
            // Choose among the resources unlocked at this distance, biased to
            // the rarest one available so far islands feel distinct.
            int best = 0;
            for (int i = 0; i < kinds.Length; i++)
                if (ring >= kinds[i].minRing) best = i;
            int pick = Random.value < 0.65f ? best : Random.Range(0, best + 1);
            return kinds[pick];
        }

        void Build(string name, Vector3 pos, float radius, ResourceKind kind, float richness, bool shelterOnly)
        {
            var root = new GameObject(name);
            root.transform.position = pos;

            var sand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sand.name = "Sand";
            Destroy(sand.GetComponent<Collider>());
            sand.transform.SetParent(root.transform, false);
            sand.transform.localScale = new Vector3(radius * 2f, radius * 0.4f, radius * 2f);
            sand.transform.localPosition = new Vector3(0f, -radius * 0.12f, 0f);
            sand.GetComponent<MeshRenderer>().sharedMaterial = sandMat;

            if (!shelterOnly)
            {
                var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Hill";
                Destroy(hill.GetComponent<Collider>());
                hill.transform.SetParent(root.transform, false);
                hill.transform.localScale = new Vector3(radius * 1.2f, radius * 0.55f, radius * 1.2f);
                hill.transform.localPosition = new Vector3(0f, radius * 0.05f, 0f);
                hill.GetComponent<MeshRenderer>().sharedMaterial = grassMat;

                var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beacon.name = "Beacon";
                Destroy(beacon.GetComponent<Collider>());
                beacon.transform.SetParent(root.transform, false);
                beacon.transform.localScale = new Vector3(1.5f, Mathf.Lerp(22f, 52f, radius / radiusRange.y), 1.5f);
                beacon.transform.localPosition = new Vector3(0f, beacon.transform.localScale.y * 0.75f, 0f);
                var bm = MakeMat(kind.beaconColor);
                bm.EnableKeyword("_EMISSION");
                bm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                bm.SetColor("_EmissionColor", kind.beaconColor * 2.2f);
                beacon.GetComponent<MeshRenderer>().sharedMaterial = bm;
            }

            var island = root.AddComponent<Island>();
            island.Configure(shelterOnly ? "—" : kind.name, richness, radius, false);
        }

        static Material MakeMat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.12f);
            return m;
        }
    }
}
