using System.Collections.Generic;
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

        [SerializeField] int islandCount = 16;
        // Tighter than it was: a denser map means more arrivals and decisions
        // per session instead of long stretches of holding a course.
        [SerializeField] float innerDistance = 120f;
        [SerializeField] float outerDistance = 520f;
        [Header("Reefs")]
        [SerializeField] int reefCount = 26;
        [SerializeField] Vector2 reefRadiusRange = new Vector2(5f, 11f);
        [SerializeField] float reefMinDistance = 90f;
        [SerializeField] float reefMaxDistance = 540f;
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

        Material sandMat, grassMat, rockMat;

        void Awake()
        {
            if (seed != 0) Random.InitState(seed);

            // Clear any authored islands except the home island.
            foreach (var existing in FindObjectsByType<Island>(FindObjectsSortMode.None))
                if (!existing.IsHome) Destroy(existing.gameObject);
            var stale = GameObject.Find("Island_Windward");
            if (stale != null) Destroy(stale);

            sandMat = MakeMat(new Color(0.87f, 0.78f, 0.56f));
            grassMat = MakeMat(new Color(0.34f, 0.55f, 0.28f));
            rockMat = MakeMat(new Color(0.47f, 0.45f, 0.46f));

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

            BuildReefs();
        }

        void BuildReefs()
        {
            var rockMat = MakeMat(new Color(0.26f, 0.27f, 0.30f));
            var foamMat = MakeMat(new Color(0.92f, 0.95f, 0.97f));

            for (int i = 0; i < reefCount; i++)
            {
                float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = Random.Range(reefMinDistance, reefMaxDistance);
                var pos = new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);

                // Never plant a reef on top of an island.
                bool clash = false;
                foreach (var isle in Island.All)
                {
                    Vector3 d = isle.transform.position - pos;
                    d.y = 0f;
                    if (d.magnitude < isle.Radius + 45f) { clash = true; break; }
                }
                if (clash) continue;

                float radius = Random.Range(reefRadiusRange.x, reefRadiusRange.y);
                var root = new GameObject($"Reef_{i}");
                root.transform.position = pos;

                int rocks = Random.Range(2, 5);
                for (int r = 0; r < rocks; r++)
                {
                    var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(rock.GetComponent<Collider>());
                    rock.transform.SetParent(root.transform, false);
                    float rs = radius * Random.Range(0.45f, 0.85f);
                    rock.transform.localScale = new Vector3(rs, rs * Random.Range(0.8f, 1.5f), rs);
                    float ra = Random.Range(0f, Mathf.PI * 2f);
                    float rd = Random.Range(0f, radius * 0.55f);
                    // Mostly submerged: just the teeth show above the surface.
                    rock.transform.localPosition = new Vector3(
                        Mathf.Sin(ra) * rd, Random.Range(-0.4f, 0.9f), Mathf.Cos(ra) * rd);
                    rock.transform.localRotation = Quaternion.Euler(
                        Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
                    rock.GetComponent<MeshRenderer>().sharedMaterial = rockMat;
                }

                // Foam ring: the visual warning that there's rock under there.
                var foam = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(foam.GetComponent<Collider>());
                foam.transform.SetParent(root.transform, false);
                foam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                foam.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                foam.transform.localScale = Vector3.one * radius * 3.2f;
                var fm = new Material(foamMat);
                fm.SetFloat("_Surface", 1f);
                fm.SetOverrideTag("RenderType", "Transparent");
                fm.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fm.SetInt("_ZWrite", 0);
                fm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                fm.renderQueue = 3020;
                fm.SetColor("_BaseColor", new Color(0.95f, 0.98f, 1f, 0.42f));
                foam.GetComponent<MeshRenderer>().sharedMaterial = fm;

                root.AddComponent<Reef>().Configure(radius);
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

            // Small islands are bare sand; bigger ones grow a dirt cap, and the
            // largest push up rock. Mixed in so the archipelago isn't uniform.
            var islandKind = shelterOnly
                ? IslandMeshBuilder.IslandKind.SandOnly
                : radius > radiusRange.y * 0.62f && Random.value < 0.75f
                    ? IslandMeshBuilder.IslandKind.Mountainous
                    : IslandMeshBuilder.IslandKind.SandAndDirt;

            var profile = IslandMeshBuilder.BuildProfile(radius, islandKind, Random.Range(0, 99999));
            var mesh = IslandMeshBuilder.Build(profile, out float maxHeight);

            var body = new GameObject("Land");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { sandMat, grassMat, rockMat };

            var island = root.AddComponent<Island>();
            island.Configure(shelterOnly ? "—" : kind.name, richness, radius, false, false);
            island.SetProfile(profile);

            if (!shelterOnly)
            {
                var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beacon.name = "Beacon";
                Destroy(beacon.GetComponent<Collider>());
                beacon.transform.SetParent(root.transform, false);
                float beaconH = Mathf.Lerp(20f, 44f, radius / radiusRange.y);
                beacon.transform.localScale = new Vector3(1.5f, beaconH, 1.5f);
                beacon.transform.localPosition = new Vector3(0f, maxHeight + beaconH * 0.75f, 0f);
                var bm = MakeMat(kind.beaconColor);
                bm.EnableKeyword("_EMISSION");
                bm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                bm.SetColor("_EmissionColor", kind.beaconColor * 2.2f);
                beacon.GetComponent<MeshRenderer>().sharedMaterial = bm;

                island.RegisterProps(BuildProps(root.transform, island, kind.name, radius));
            }
        }

        /// Each resource gets its own silhouette so you can read an island's
        /// worth from the water. Props are deactivated as the crew strip it.
        List<GameObject> BuildProps(Transform parent, Island island, string kind, float radius)
        {
            var list = new List<GameObject>();
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 0.42f), 4, 26);
            for (int i = 0; i < count; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(radius * 0.15f, island.RadiusAt(ang) * 0.8f);
                Vector3 p = island.SurfacePoint(ang, dist);
                var prop = kind switch
                {
                    "Timber" => MakeTree(),
                    "Stone" => MakeBoulder(),
                    "Ore" => MakeOreRock(),
                    _ => MakeSpiceBush(),
                };
                prop.transform.SetParent(parent, true);
                prop.transform.position = p;
                prop.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                float s = Random.Range(0.8f, 1.35f) * Mathf.Lerp(0.8f, 1.5f, radius / radiusRange.y);
                prop.transform.localScale *= s;
                list.Add(prop);
            }
            return list;
        }

        GameObject MakeTree()
        {
            var root = new GameObject("Tree");
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(trunk.GetComponent<Collider>());
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localScale = new Vector3(0.55f, 2.6f, 0.55f);
            trunk.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = Mat("trunk", new Color(0.36f, 0.25f, 0.15f));

            for (int i = 0; i < 2; i++)
            {
                var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(canopy.GetComponent<Collider>());
                canopy.transform.SetParent(root.transform, false);
                float t = i * 0.5f;
                canopy.transform.localScale = Vector3.one * Mathf.Lerp(5.2f, 3.4f, t);
                canopy.transform.localPosition = new Vector3(0f, Mathf.Lerp(6.2f, 8.4f, t), 0f);
                canopy.GetComponent<MeshRenderer>().sharedMaterial =
                    Mat("leaf", new Color(0.18f, 0.44f, 0.20f));
            }
            return root;
        }

        GameObject MakeBoulder()
        {
            var root = new GameObject("Boulder");
            for (int i = 0; i < 2; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(rock.GetComponent<Collider>());
                rock.transform.SetParent(root.transform, false);
                rock.transform.localScale = new Vector3(
                    Random.Range(2.6f, 4.4f), Random.Range(2.2f, 3.6f), Random.Range(2.6f, 4.4f));
                rock.transform.localPosition = new Vector3(Random.Range(-1.4f, 1.4f), 1.3f + i * 1.4f, Random.Range(-1.4f, 1.4f));
                rock.transform.localRotation = Quaternion.Euler(Random.Range(-14f, 14f), Random.Range(0f, 360f), Random.Range(-14f, 14f));
                rock.GetComponent<MeshRenderer>().sharedMaterial =
                    Mat("stone", new Color(0.62f, 0.63f, 0.66f));
            }
            return root;
        }

        GameObject MakeOreRock()
        {
            var root = new GameObject("OreRock");
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(rock.GetComponent<Collider>());
            rock.transform.SetParent(root.transform, false);
            rock.transform.localScale = new Vector3(3.6f, 3.2f, 3.6f);
            rock.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            rock.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
            rock.GetComponent<MeshRenderer>().sharedMaterial = Mat("darkrock", new Color(0.30f, 0.29f, 0.33f));

            // Glinting seam so ore reads as valuable from a distance.
            var vein = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(vein.GetComponent<Collider>());
            vein.transform.SetParent(root.transform, false);
            vein.transform.localScale = new Vector3(2.1f, 1.1f, 2.1f);
            vein.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            var vm = Mat("orevein", new Color(1f, 0.82f, 0.28f));
            vm.EnableKeyword("_EMISSION");
            vm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            vm.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.2f) * 1.6f);
            vein.GetComponent<MeshRenderer>().sharedMaterial = vm;
            return root;
        }

        GameObject MakeSpiceBush()
        {
            var root = new GameObject("SpiceBush");
            var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bush.GetComponent<Collider>());
            bush.transform.SetParent(root.transform, false);
            bush.transform.localScale = new Vector3(3.6f, 2.4f, 3.6f);
            bush.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            bush.GetComponent<MeshRenderer>().sharedMaterial = Mat("spiceleaf", new Color(0.30f, 0.50f, 0.28f));

            for (int i = 0; i < 3; i++)
            {
                var flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(flower.GetComponent<Collider>());
                flower.transform.SetParent(root.transform, false);
                flower.transform.localScale = Vector3.one * 1.15f;
                float a = i * 2.1f;
                flower.transform.localPosition = new Vector3(Mathf.Sin(a) * 1.3f, 2.3f, Mathf.Cos(a) * 1.3f);
                flower.GetComponent<MeshRenderer>().sharedMaterial =
                    Mat("spiceflower", new Color(0.92f, 0.34f, 0.62f));
            }
            return root;
        }

        readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

        Material Mat(string key, Color c)
        {
            if (matCache.TryGetValue(key, out var m) && m != null) return m;
            m = MakeMat(c);
            matCache[key] = m;
            return m;
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
