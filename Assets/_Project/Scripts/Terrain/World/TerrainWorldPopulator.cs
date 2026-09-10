using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.World;
using Random = UnityEngine.Random;

namespace SeaSick.Terrain
{
    /// Populates the procedural archipelago with gameplay, replacing
    /// ArchipelagoGenerator: discovers islands by flood-filling the height
    /// field around home, gives each an Island (terrain-backed radial
    /// profile so anchoring, crew, raiders, minimap and camera keep working
    /// unchanged), resources + props + beacons by ring, then reefs, sea
    /// monsters and raiders. Runs once in Start; the height function is pure,
    /// so nothing waits on chunk streaming.
    public class TerrainWorldPopulator : MonoBehaviour
    {
        public TerrainSettings terrain;
        public WorldSettings world;
        [Tooltip("Home position (VoyageManager.HomePoint). The island containing the nearest land to it is home.")]
        public Transform homePoint;

        public int IslandCount { get; private set; }
        public Island Home { get; private set; }
        public bool Done { get; private set; }

        /// The flood-fill grid, kept rather than thrown away: 0 = water,
        /// -1 = land too small to be an island, k > 0 = component k-1. The
        /// minimap draws this instead of a circle per island, because a
        /// radius cannot describe a crescent (see MiniMap).
        public int[] LandMask { get; private set; }
        public int MaskSize { get; private set; }
        public float MaskCell { get; private set; }
        public Vector2 MaskOrigin { get; private set; }

        Island[] byComponent;

        /// The island a LandMask value belongs to, or null for water and for
        /// land that never became an island.
        public Island IslandForMask(int maskValue)
        {
            if (maskValue <= 0 || byComponent == null || maskValue > byComponent.Length) return null;
            return byComponent[maskValue - 1];
        }

        TerrainParams prm;
        NativeArray<float> lut;

        float Height(float x, float z) => TerrainHeight.Height(new float2(x, z), prm, lut);

        void Start()
        {
            if (terrain == null || world == null) { Debug.LogError("TerrainWorldPopulator: missing settings"); return; }
            if (world.seed != 0) Random.InitState(world.seed);
            prm = TerrainParams.From(terrain);
            lut = TerrainCurveLut.Bake(terrain.profileCurve, Allocator.Persistent);
            Island.TerrainHeight = Height;
            Island.BeachMaxSlope = world.beachMaxSlope;

            Vector3 home = homePoint != null ? homePoint.position : Vector3.zero;
            var islands = Discover(new float2(home.x, home.z));
            // Sorting reorders the list but not the mask's component ids, so
            // the mapping is rebuilt by id, not by position in the list.
            islands.Sort((a, b) => a.distToHome.CompareTo(b.distToHome));
            byComponent = new Island[islands.Count];
            for (int i = 0; i < islands.Count; i++)
                byComponent[islands[i].id] = BuildIsland(islands[i], i == 0, i, islands.Count);
            IslandCount = islands.Count;

            BuildMonsters(home);
            BuildReefs(home);
            BuildRaiders();
            Done = true;
        }

        void OnDestroy()
        {
            if (lut.IsCreated) lut.Dispose();
            if (Island.TerrainHeight == (System.Func<float, float, float>)Height) Island.TerrainHeight = null;
        }

        struct Found
        {
            public float2 centre;
            public float area;
            public float distToHome;
            public int id;        // component index, indexes LandMask values
        }

        /// Flood-fill land cells (height > 0.5 m) on a grid; one component =
        /// one island. Centre is the land cell nearest the centroid, so a
        /// crescent still gets a centre on its own ground.
        List<Found> Discover(float2 home)
        {
            float cell = world.scanCell;
            int n = Mathf.CeilToInt(world.discoveryRadius * 2f / cell);
            float2 origin = home - world.discoveryRadius;
            var land = new bool[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float2 p = origin + (new float2(i, j) + 0.5f) * cell;
                    land[j * n + i] = math.distance(p, home) <= world.discoveryRadius && Height(p.x, p.y) > 0.5f;
                }

            var seen = new bool[n * n];
            var mask = new int[n * n];
            var result = new List<Found>();
            var stack = new Stack<int>();
            var cells = new List<int>();
            for (int start = 0; start < land.Length; start++)
            {
                if (!land[start] || seen[start]) continue;
                cells.Clear();
                stack.Push(start); seen[start] = true;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    cells.Add(c);
                    int cx = c % n, cy = c / n;
                    if (cx > 0 && land[c - 1] && !seen[c - 1]) { seen[c - 1] = true; stack.Push(c - 1); }
                    if (cx < n - 1 && land[c + 1] && !seen[c + 1]) { seen[c + 1] = true; stack.Push(c + 1); }
                    if (cy > 0 && land[c - n] && !seen[c - n]) { seen[c - n] = true; stack.Push(c - n); }
                    if (cy < n - 1 && land[c + n] && !seen[c + n]) { seen[c + n] = true; stack.Push(c + n); }
                }
                float area = cells.Count * cell * cell;
                if (area < world.minIslandArea)
                {
                    // Real land, just not an island: draw it, don't populate it.
                    foreach (int c in cells) mask[c] = -1;
                    continue;
                }
                int id = result.Count;
                foreach (int c in cells) mask[c] = id + 1;
                float2 sum = float2.zero;
                foreach (int c in cells) sum += origin + (new float2(c % n, c / n) + 0.5f) * cell;
                float2 centroid = sum / cells.Count;
                float2 best = centroid; float bestD = float.MaxValue;
                foreach (int c in cells)
                {
                    float2 p = origin + (new float2(c % n, c / n) + 0.5f) * cell;
                    float d = math.distancesq(p, centroid);
                    if (d < bestD) { bestD = d; best = p; }
                }
                float2 nearestToHome = best; float nd = float.MaxValue;
                foreach (int c in cells)
                {
                    float2 p = origin + (new float2(c % n, c / n) + 0.5f) * cell;
                    float d = math.distancesq(p, home);
                    if (d < nd) { nd = d; nearestToHome = p; }
                }
                result.Add(new Found { centre = best, area = area, distToHome = math.sqrt(nd), id = id });
            }

            LandMask = mask;
            MaskSize = n;
            MaskCell = cell;
            MaskOrigin = new Vector2(origin.x, origin.y);
            return result;
        }

        /// Outline per sector: march out from the centre to the first point
        /// below the waterline. Beach flag: the ground over the first 12 m
        /// inland of that point rises gently.
        void MeasureProfile(float2 centre, out float[] outline, out bool[] hasBeach, out float meanRadius)
        {
            int sectors = Island.Sectors;
            outline = new float[sectors];
            hasBeach = new bool[sectors];
            float sum = 0f;
            for (int s = 0; s < sectors; s++)
            {
                float ang = s / (float)sectors * Mathf.PI * 2f;
                float2 dir = new float2(Mathf.Sin(ang), Mathf.Cos(ang));
                float r = 2f, shore = -1f;
                for (; r < 2000f; r += 2f)
                {
                    float2 p = centre + dir * r;
                    if (Height(p.x, p.y) < -0.3f) { shore = r; break; }
                }
                if (shore < 0f) shore = r;
                outline[s] = shore;
                sum += shore;
                float h0 = Height(centre.x + dir.x * (shore - 1f), centre.y + dir.y * (shore - 1f));
                float h1 = Height(centre.x + dir.x * (shore - 13f), centre.y + dir.y * (shore - 13f));
                hasBeach[s] = shore > 13f && (h1 - h0) / 12f < world.beachMaxSlope;
            }
            meanRadius = sum / sectors;
        }

        Island BuildIsland(Found f, bool isHome, int index, int total)
        {
            MeasureProfile(f.centre, out var outline, out var beach, out float meanR);
            float ring = Mathf.Clamp01(f.distToHome / world.discoveryRadius);

            var root = new GameObject(isHome ? "Island_Home" : "Island_" + index);
            root.transform.position = new Vector3(f.centre.x, 0f, f.centre.y);
            var island = root.AddComponent<Island>();
            island.SetTerrainProfile(outline, beach, meanR);

            if (isHome)
            {
                island.Configure("Home", 0f, meanR, true, false);
                root.AddComponent<Stockpile>();
                Home = island;
                // A mast one ship-length tall, standing ON the ground.
                //
                // It was 46, and MakeBeacon scales a Unity cylinder -- which
                // is two units tall -- so that was a 92 m column, sunk 12 m
                // into the hill and reaching 80 m over it. On the old home
                // island, a 111 m peak, that read as a tower on a summit. On
                // a 5.7 m one it is a girder through the middle of the
                // picture, fourteen times the relief of the land it marks.
                // Tied to the ship instead, it is a landmark you can judge
                // your distance off, which is what a beacon is for.
                const float BeaconHeight = SeaSick.World.WorldScale.ShipLength;
                var beacon = IslandPropFactory.MakeBeacon(new Color(1f, 0.55f, 0.25f),
                    BeaconHeight * 0.5f, 2.6f);
                beacon.transform.SetParent(root.transform, false);
                beacon.transform.localPosition = new Vector3(0f,
                    Height(f.centre.x, f.centre.y) + BeaconHeight * 0.5f, 0f);

                // The dock goes in BEFORE the scenery, because the scenery
                // bake is what a keep-out would have to be handed -- and
                // because a pier with trees growing through it is exactly the
                // sort of thing that only shows up once it is too late to
                // pass anything down.
                var site = HarbourSite.Find(root.transform.position,
                    HarbourSite.SearchRadiusFor(meanR), Height);
                Dock dock = null;
                if (site.found)
                {
                    var dockGo = DockBuilder.Build(root.transform, site, Height);
                    if (dockGo != null) dock = dockGo.GetComponent<Dock>();
                }

                // Where the village will stand. Measured once, here, so the
                // camera, the keep-out and anything placed later all frame
                // and site against the same ground.
                // Sited into the shot the player comes home to. The reach is
                // the shot's own measured half-width in portrait, which is
                // the tight axis by a factor of six -- a village that clears
                // it sideways clears it in every direction.
                var flat = SettlementSite.Find(root.transform.position,
                    HarbourSite.SearchRadiusFor(meanR), Height,
                    4f, terrain != null ? terrain.sandHeight + 0.5f : 3.7f,
                    dock != null ? dock.ViewCentre : default,
                    dock != null ? Dock.ViewHalfWidth : 0f);
                Village village = null;
                if (flat.found)
                {
                    var settlement = root.AddComponent<Settlement>();
                    settlement.Configure(flat);

                    // The village goes in before the trees for the same
                    // reason the dock did: the scenery is one baked mesh, so
                    // a clearing is something you reserve beforehand or never
                    // get. What is reserved is the CLEARING, not the
                    // buildings -- they are raised across a session, long
                    // after this mesh is welded shut.
                    village = root.AddComponent<Village>();
                    village.Configure(settlement, Height,
                        terrain != null ? terrain.sandHeight + 1.2f : 4.4f);
                    village.Reserve(root.transform.position, 8f);      // the beacon
                    if (site.found) village.Reserve(site.root, 14f);   // the head of the pier
                }

                Dress(root.transform, root.transform.position, meanR, island, index, village);
                return island;
            }

            bool shelterOnly = meanR < world.shelterOnlyBelowRadius;
            if (shelterOnly)
            {
                island.Configure("—", 0f, meanR, false, false);
                Dress(root.transform, root.transform.position, meanR, island, index);
                return island;
            }

            var kind = PickKind(ring);
            float beaconH = Mathf.Lerp(26f, 60f, Mathf.Clamp01(meanR / 200f));
            var b = IslandPropFactory.MakeBeacon(kind.beaconColor, beaconH, 2.2f);
            b.transform.SetParent(root.transform, false);
            b.transform.localPosition = new Vector3(0f, Height(f.centre.x, f.centre.y) + beaconH * 0.75f, 0f);

            var props = BuildProps(root.transform, island, kind.name, meanR);
            island.RegisterProps(props);
            island.Configure(kind.name, props.Count, meanR, false, false);
            Dress(root.transform, root.transform.position, meanR, island, index);
            return island;
        }

        /// Trees and scree, baked into one mesh. Scenery only -- the
        /// harvestable props are BuildProps and stay capped, because the eye
        /// needs hundreds of known-size objects to judge an island by and the
        /// economy does not.
        void Dress(Transform parent, Vector3 centre, float meanR, Island island, int index,
            Village village = null)
        {
            IslandScenery.Build(parent, centre, meanR, Height, terrain,
                ang => island.RadiusAt(ang), terrain.seed * 7919 + index, prm, island,
                village != null ? (System.Func<float, float, bool>)village.KeepOut : null);
        }

        WorldSettings.ResourceKind PickKind(float ring)
        {
            int best = 0;
            for (int i = 0; i < world.kinds.Length; i++)
                if (ring >= world.kinds[i].minRing) best = i;
            int pick = Random.value < 0.65f ? best : Random.Range(0, best + 1);
            return world.kinds[pick];
        }

        /// Props stand on ground that is above the beach band and not too
        /// steep, inside the shoreline on their bearing.
        List<GameObject> BuildProps(Transform parent, Island island, string kind, float meanR)
        {
            var list = new List<GameObject>();
            int count = Mathf.Clamp(Mathf.RoundToInt(meanR * world.propsPerRadius), 5, 26);
            for (int i = 0, attempts = 0; i < count && attempts < count * 12; attempts++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(meanR * 0.12f, island.RadiusAt(ang) * 0.82f);
                Vector3 p = island.SurfacePoint(ang, dist);
                if (p.y < terrain.seaLevel + 1.5f) continue;
                float slope = Mathf.Abs(Height(p.x + 2f, p.z) - Height(p.x - 2f, p.z)) / 4f;
                if (slope > 0.8f) continue;
                var prop = IslandPropFactory.Make(kind);
                prop.transform.SetParent(parent, true);
                prop.transform.position = p;
                prop.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                prop.transform.localScale *= Random.Range(0.85f, 1.3f);
                var node = prop.AddComponent<ResourceNode>();
                node.Configure(kind, island, kind == "Stone" || kind == "Ore" ? 4 : 3);
                list.Add(prop);
                i++;
            }
            return list;
        }

        void BuildMonsters(Vector3 home)
        {
            for (int i = 0; i < world.monsterCount; i++)
            {
                float spread = world.monsterCount > 1 ? (i / (world.monsterCount - 1f) - 0.5f) * world.monsterSpreadDeg : 0f;
                float ang = (world.monsterBearingDeg + spread) * Mathf.Deg2Rad;
                var pos = home + new Vector3(Mathf.Sin(ang) * world.monsterDistance, 0f, Mathf.Cos(ang) * world.monsterDistance);
                if (Height(pos.x, pos.z) > -world.reefMinDepth) continue; // not on a beach
                Combat.SeaMonster.Spawn(pos, "SeaMonster_" + i);
            }
        }

        void BuildReefs(Vector3 home)
        {
            var rockMat = IslandPropFactory.MakeMat(new Color(0.26f, 0.27f, 0.30f));
            var foamMat = IslandPropFactory.MakeMat(new Color(0.92f, 0.95f, 0.97f));
            for (int i = 0; i < world.reefCount; i++)
            {
                float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = Random.Range(world.reefMinDistance, world.reefMaxDistance);
                var pos = home + new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);
                if (Height(pos.x, pos.z) > -world.reefMinDepth) continue;
                bool clash = false;
                foreach (var monster in Combat.SeaMonster.All)
                {
                    Vector3 d = monster.transform.position - pos; d.y = 0f;
                    if (d.magnitude < 120f) { clash = true; break; }
                }
                if (clash) continue;

                float radius = Random.Range(world.reefRadiusRange.x, world.reefRadiusRange.y);
                var root = new GameObject("Reef_" + i);
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
                    rock.transform.localPosition = new Vector3(Mathf.Sin(ra) * rd, Random.Range(-0.4f, 0.9f), Mathf.Cos(ra) * rd);
                    rock.transform.localRotation = Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
                    rock.GetComponent<MeshRenderer>().sharedMaterial = rockMat;
                }
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

        void BuildRaiders()
        {
            if (world.raidersPerIsland <= 0 || world.maxRaiders <= 0) return;
            int posted = 0, n = 0;
            foreach (var isle in Island.All)
            {
                if (isle == null || isle.IsHome || isle.MaxRadius < world.raiderMinIslandRadius || !isle.HasResources) continue;
                for (int i = 0; i < world.raidersPerIsland && posted < world.maxRaiders; i++, posted++)
                {
                    float radius = isle.MaxRadius + world.patrolClearance + i * 26f;
                    Combat.EnemyShip.Spawn(isle, radius, (n + i) % 2 == 0 ? 1 : -1, "Raider_" + isle.name + "_" + i);
                }
                n++;
                if (posted >= world.maxRaiders) break;
            }
        }
    }
}
