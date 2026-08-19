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

        // Fewer, bigger, further apart. The ship is 21m long, so a 45m-radius
        // island is already four ship-lengths across and the largest are over
        // twenty — land should dwarf the boat, not sit beside it.
        [SerializeField] int islandCount = 10;
        [SerializeField] float innerDistance = 280f;
        [SerializeField] float outerDistance = 1150f;
        [SerializeField] float minIslandGap = 170f;   // open water between shores
        [Header("Reefs")]
        [SerializeField] int reefCount = 24;
        [SerializeField] Vector2 reefRadiusRange = new Vector2(6f, 13f);
        [SerializeField] float reefMinDistance = 220f;
        [SerializeField] float reefMaxDistance = 1200f;
        // Test targets. Static monsters parked at a known bearing and distance
        // so the guns can be worked on without hunting for something to shoot.
        [Header("Sea monsters (gunnery test targets)")]
        [SerializeField] int monsterCount = 3;
        [SerializeField] float monsterDistance = 260f;
        [SerializeField] float monsterBearingDeg = 0f;   // 0 = due north of home
        [SerializeField] float monsterSpreadDeg = 26f;   // apart enough to engage one at a time

        // Raiders. Only the islands actually worth something get guarded —
        // a bare shelter rock with nothing on it has nothing to defend, and
        // posting a ship there would just be a toll on safe harbour.
        [Header("Raiders")]
        [SerializeField] int raidersPerIsland = 1;
        [SerializeField] int maxRaiders = 8;
        [SerializeField] float raiderMinIslandRadius = 60f;
        [SerializeField] float patrolClearance = 78f;   // water between shore and patrol

        [SerializeField] Vector2 radiusRange = new Vector2(45f, 200f);
        [SerializeField] float richnessPerRadius = 0.95f;
        [SerializeField] float shelterOnlyBelowRadius = 60f;
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
            var placed = new List<(Vector3 pos, float radius)>();
            // The home island is already on the map; nothing may sit on it.
            foreach (var existing in FindObjectsByType<Island>(FindObjectsSortMode.None))
                placed.Add((existing.transform.position, existing.MaxRadius));

            for (int i = 0; i < islandCount; i++)
            {
                float ring = (i + 0.5f) / islandCount;

                // Bigger islands sit further out — the long voyage has to pay.
                float radius = Mathf.Lerp(radiusRange.x, radiusRange.y, ring) * Random.Range(0.78f, 1.24f);
                radius = Mathf.Clamp(radius, radiusRange.x, radiusRange.y * 1.1f);
                float outerReach = radius * 1.35f;   // worst-case outline lobe

                // Islands are big now, so placement has to check for overlap
                // rather than trusting the spiral to keep them apart.
                Vector3 pos = Vector3.zero;
                bool ok = false;
                for (int attempt = 0; attempt < 30 && !ok; attempt++)
                {
                    float dist = Mathf.Lerp(innerDistance, outerDistance, ring)
                                 * Random.Range(0.82f, 1.18f) + attempt * 45f;
                    float ang = (i * goldenAngle + Random.Range(-22f, 22f) + attempt * 11f) * Mathf.Deg2Rad;
                    pos = new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);

                    ok = true;
                    foreach (var (p, r) in placed)
                    {
                        if (Vector3.Distance(p, pos) < r + outerReach + minIslandGap) { ok = false; break; }
                    }
                }
                if (!ok) continue;   // couldn't find room — the sea stays empty here

                placed.Add((pos, outerReach));

                bool shelterOnly = radius < shelterOnlyBelowRadius;
                var kind = PickKind(ring);
                float richness = shelterOnly ? 0f : Mathf.Round(radius * richnessPerRadius * Random.Range(0.8f, 1.2f));

                Build($"Island_{kind.name}_{i}", pos, radius, kind, richness, shelterOnly);
            }

            RebuildHomeIsland();
            BuildMonsters();
            BuildReefs();
            BuildRaiders();
        }

        /// Post raiders on the islands worth taking. They orbit at a fixed
        /// standoff rather than sitting still, so the threat is a patrol you
        /// have to time rather than a wall you have to grind through — and
        /// the direction alternates so two raiders on one island sweep
        /// opposite ways and cover each other's blind side.
        void BuildRaiders()
        {
            if (raidersPerIsland <= 0 || maxRaiders <= 0) return;

            int posted = 0;
            int n = 0;

            foreach (var isle in Island.All)
            {
                if (isle == null || isle.IsHome) continue;
                if (isle.MaxRadius < raiderMinIslandRadius) continue;
                if (!isle.HasResources) continue;

                for (int i = 0; i < raidersPerIsland && posted < maxRaiders; i++, posted++)
                {
                    float radius = isle.MaxRadius + patrolClearance + i * 26f;
                    Combat.EnemyShip.Spawn(isle, radius, (n + i) % 2 == 0 ? 1 : -1,
                        $"Raider_{isle.name}_{i}");
                }
                n++;
                if (posted >= maxRaiders) break;
            }
        }

        /// Park the test targets before the reefs go down, so BuildReefs can
        /// keep the water around them clear — a reef hit on the approach would
        /// muddy exactly the thing these are here to measure.
        void BuildMonsters()
        {
            for (int i = 0; i < monsterCount; i++)
            {
                float spread = monsterCount > 1
                    ? (i / (monsterCount - 1f) - 0.5f) * monsterSpreadDeg
                    : 0f;
                float ang = (monsterBearingDeg + spread) * Mathf.Deg2Rad;
                var pos = new Vector3(
                    Mathf.Sin(ang) * monsterDistance, 0f, Mathf.Cos(ang) * monsterDistance);
                Combat.SeaMonster.Spawn(pos, $"SeaMonster_{i}");
            }
        }

        /// The home island was authored as stacked spheres before the mesh
        /// builder existed. Rebuild it in the same style as the rest so it
        /// doesn't read as a different game, keeping its transform (which
        /// VoyageManager references) intact.
        [SerializeField] float homeRadius = 95f;

        void RebuildHomeIsland()
        {
            Island home = null;
            foreach (var isle in FindObjectsByType<Island>(FindObjectsSortMode.None))
                if (isle.IsHome) { home = isle; break; }
            if (home == null) return;

            for (int i = home.transform.childCount - 1; i >= 0; i--)
                Destroy(home.transform.GetChild(i).gameObject);

            var profile = IslandMeshBuilder.BuildProfile(
                homeRadius, IslandMeshBuilder.IslandKind.SandAndDirt, 4242);
            var mesh = IslandMeshBuilder.Build(profile, out float maxHeight);

            var body = new GameObject("Land");
            body.transform.SetParent(home.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = new[] { sandMat, grassMat, rockMat };

            home.Configure("Home", 0f, homeRadius, true, false);
            home.SetProfile(profile);

            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            Destroy(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(home.transform, false);
            beacon.transform.localScale = new Vector3(2f, 46f, 2f);
            beacon.transform.localPosition = new Vector3(0f, maxHeight + 34f, 0f);
            var bm = MakeMat(new Color(1f, 0.55f, 0.25f));
            bm.EnableKeyword("_EMISSION");
            bm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            bm.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.2f) * 2.6f);
            beacon.GetComponent<MeshRenderer>().sharedMaterial = bm;
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

                // Never plant a reef on top of an island, or in the water the
                // test targets are meant to be approached through.
                bool clash = false;
                foreach (var isle in Island.All)
                {
                    Vector3 d = isle.transform.position - pos;
                    d.y = 0f;
                    if (d.magnitude < isle.MaxRadius + 70f) { clash = true; break; }
                }
                if (!clash)
                    foreach (var monster in Combat.SeaMonster.All)
                    {
                        Vector3 d = monster.transform.position - pos;
                        d.y = 0f;
                        if (d.magnitude < 120f) { clash = true; break; }
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
            island.SetProfile(profile);

            if (!shelterOnly)
            {
                var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beacon.name = "Beacon";
                Destroy(beacon.GetComponent<Collider>());
                beacon.transform.SetParent(root.transform, false);
                float beaconH = Mathf.Lerp(26f, 60f, radius / radiusRange.y);
                beacon.transform.localScale = new Vector3(1.5f, beaconH, 1.5f);
                beacon.transform.localPosition = new Vector3(0f, maxHeight + beaconH * 0.75f, 0f);
                var bm = MakeMat(kind.beaconColor);
                bm.EnableKeyword("_EMISSION");
                bm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                bm.SetColor("_EmissionColor", kind.beaconColor * 2.2f);
                beacon.GetComponent<MeshRenderer>().sharedMaterial = bm;

                var props = BuildProps(root.transform, island, kind.name, radius);
                island.RegisterProps(props);
                // The island holds exactly as much as there is standing on it.
                island.Configure(kind.name, props.Count, radius, false, false);
            }
            else
            {
                island.Configure("—", 0f, radius, false, false);
            }
        }

        /// Each resource gets its own silhouette so you can read an island's
        /// worth from the water. Props are deactivated as the crew strip it.
        List<GameObject> BuildProps(Transform parent, Island island, string kind, float radius)
        {
            var list = new List<GameObject>();
            // One prop is one unit of resource — ten trees means ten timber —
            // so the count has to stay small enough that the crew can actually
            // walk to every one of them in a reasonable stop.
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 0.13f), 5, 26);
            for (int i = 0; i < count; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(radius * 0.12f, island.RadiusAt(ang) * 0.82f);
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
                // Props are a FIXED real-world size. Scaling trees with the
                // island was quietly destroying the sense of scale — a tree is
                // a tree, and that's exactly what tells you how big the land is.
                prop.transform.localScale *= Random.Range(0.85f, 1.3f);

                // Each prop is a thing the crew can walk up to and work.
                var node = prop.AddComponent<ResourceNode>();
                node.Configure(kind, island, kind == "Stone" || kind == "Ore" ? 4 : 3);

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
