using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// A place to anchor. Every island is shelter; some also carry resources.
    /// Keeps a static registry so the swell warning and navigation can ask
    /// "where is the nearest land?" without searching the scene.
    public class Island : MonoBehaviour
    {
        public static readonly List<Island> All = new List<Island>();

        /// Bearings the shoreline is measured on. This outlived the radial
        /// mesher it came from: the terrain populator marches out on these
        /// bearings, and every query below indexes by sector.
        public const int Sectors = 46;

        /// The measured shape of one island — how far the shore is on each
        /// bearing, and whether that bearing can be landed on. Measured off
        /// the terrain height field, never authored.
        public struct Profile
        {
            public float radius;       // mean shoreline distance
            public float[] outline;    // shoreline distance per sector
            public bool[] hasBeach;    // can the ship land on this bearing?
        }

        [SerializeField] string resourceName = "Timber";
        [SerializeField] float remaining;
        [SerializeField] float radius = 40f;
        [SerializeField] bool isHome;

        public string ResourceName => resourceName;
        public float Remaining => remaining;
        public float Radius => radius;
        public bool IsHome => isHome;
        public bool HasResources => remaining > 0.5f;
        /// How close the ship must be before the anchor option appears.
        public float AnchorRadius => radius + 30f;

        float startingAmount;
        bool hasHill;
        readonly List<GameObject> props = new List<GameObject>();

        Profile profile;
        bool hasProfile;

        /// Terrain-backed islands: the world height function (x, z) → y.
        /// When set, SurfacePoint follows the real ground instead of the
        /// radial mesh model. Set by the terrain populator.
        public static System.Func<float, float, float> TerrainHeight;
        /// Max rise per metre over the 12 m inland of the waterline for a
        /// landing to count as a beach (terrain-backed islands).
        public static float BeachMaxSlope = 0.5f;
        int beachCacheFrame = -1; Vector3 beachCachePos; bool beachCache;

        /// True only when the profile is fully populated. The arrays are what
        /// every query below indexes into, so a half-built profile must fall
        /// back to the plain radius rather than throwing mid-frame.
        public bool HasProfile => hasProfile
            && profile.outline != null && profile.outline.Length > 0
            && profile.hasBeach != null && profile.hasBeach.Length > 0;

        /// Outline + beach flags per sector, measured off the terrain.
        public void SetTerrainProfile(float[] outline, bool[] hasBeach, float meanRadius)
        {
            profile = new Profile { outline = outline, hasBeach = hasBeach, radius = meanRadius };
            hasProfile = true;
            radius = meanRadius;
        }

        static int SectorOf(float angleRad)
        {
            int s = Mathf.RoundToInt(angleRad / (Mathf.PI * 2f) * Sectors);
            return ((s % Sectors) + Sectors) % Sectors;
        }

        static float BearingTo(Vector3 from, Vector3 to)
        {
            Vector3 d = from - to;
            return Mathf.Atan2(d.x, d.z);
        }

        /// The shoreline distance on a given bearing — islands aren't circles,
        /// so collision and docking both need the real outline.
        public float RadiusAt(float angleRad)
        {
            if (!HasProfile) return radius;
            return profile.outline[SectorOf(angleRad)];
        }

        public float RadiusToward(Vector3 worldPos) => RadiusAt(BearingTo(worldPos, transform.position));

        /// Can a ship land here? Cliff sectors drop sheer into the water.
        public bool HasBeachToward(Vector3 worldPos)
        {
            if (!HasProfile) return true;
            if (TerrainHeight != null)
            {
                // Judge the NEAREST shore within ±60° of the line to the centre:
                // on a big island the centre line can hit a cliff while the
                // beach the ship is actually facing is 20 m away.
                if (beachCacheFrame == Time.frameCount && (worldPos - beachCachePos).sqrMagnitude < 4f) return beachCache;
                Vector3 c = transform.position;
                Vector3 d = c - worldPos; d.y = 0f;
                float len = d.magnitude;
                bool result = false;
                if (len < 1f) result = profile.hasBeach[0];
                else
                {
                    float baseAng = Mathf.Atan2(d.x, d.z);
                    // Any beach within a short walk (25 m) of the nearest shore
                    // point counts: the crew can land beside a bank.
                    float nearest = float.MaxValue;
                    var hits = new System.Collections.Generic.List<(float t, float rise)>(13);
                    for (int k = -6; k <= 6; k++)
                    {
                        float a = baseAng + k * 10f * Mathf.Deg2Rad;
                        Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        float maxT = Mathf.Min(len + 50f, nearest + 25f);
                        for (float t = 0f; t < maxT; t += 2f)
                        {
                            Vector3 p = worldPos + dir * t;
                            if (TerrainHeight(p.x, p.z) > -0.3f)
                            {
                                Vector3 q = p + dir * 12f;
                                hits.Add((t, TerrainHeight(q.x, q.z) - TerrainHeight(p.x, p.z)));
                                nearest = Mathf.Min(nearest, t);
                                break;
                            }
                        }
                    }
                    foreach (var (t, rise) in hits)
                        if (t <= nearest + 25f && rise / 12f < BeachMaxSlope) { result = true; break; }
                }
                beachCacheFrame = Time.frameCount; beachCachePos = worldPos; beachCache = result;
                return result;
            }
            return profile.hasBeach[SectorOf(BearingTo(worldPos, transform.position))];
        }

        /// Largest shoreline distance, for spawn spacing and safety margins.
        public float MaxRadius
        {
            get
            {
                if (!HasProfile) return radius;
                float m = 0f;
                foreach (var o in profile.outline) m = Mathf.Max(m, o);
                return m;
            }
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Configure(string resource, float amount, float islandRadius, bool home,
            bool islandHasHill = true)
        {
            resourceName = resource;
            remaining = amount;
            startingAmount = Mathf.Max(1f, amount);
            radius = islandRadius;
            isHome = home;
            hasHill = islandHasHill;
        }

        /// Resource props (trees, boulders…) so the island shows what it is —
        /// and visibly empties as the crew strip it.
        public void RegisterProps(List<GameObject> resourceProps)
        {
            props.Clear();
            props.AddRange(resourceProps);
        }

        /// Book-keeping only. Props are ResourceNodes now and remove themselves
        /// when the crew fell them — the old code here also hid props as the
        /// count drained, which made trees vanish unharvested and the island
        /// report stock that no longer existed.
        public float Extract(float amount)
        {
            float take = Mathf.Min(amount, remaining);
            remaining -= take;
            return take;
        }

        /// A working spot for crew member `index` of `total`, spread across the
        /// shore FACING the ship — so the shore party stays on screen.
        public Vector3 ShorePoint(int index, int total, Vector3 shipPos)
        {
            Vector3 toShip = shipPos - transform.position;
            toShip.y = 0f;
            if (toShip.sqrMagnitude < 0.01f) toShip = Vector3.forward;
            float baseAng = Mathf.Atan2(toShip.x, toShip.z);
            float spread = Mathf.Deg2Rad * 46f;
            float ang = baseAng + (total <= 1 ? 0f : Mathf.Lerp(-spread, spread, index / (float)(total - 1)));
            // Stand just inland of the waterline on that bearing.
            return SurfacePoint(ang, RadiusAt(ang) * 0.78f);
        }

        /// A point on the island's visible surface. Follows the generated mesh
        /// when there is one, so props sit on the ground instead of inside it.
        public Vector3 SurfacePoint(float angleRad, float distFromCentre)
        {
            if (TerrainHeight != null && HasProfile)
            {
                int s = SectorOf(angleRad);
                float d = Mathf.Min(distFromCentre, profile.outline[s] * 0.96f);
                float x = transform.position.x + Mathf.Sin(angleRad) * d;
                float z = transform.position.z + Mathf.Cos(angleRad) * d;
                return new Vector3(x, TerrainHeight(x, z), z);
            }
            // Legacy dome shape (the authored home island).
            float dd = Mathf.Min(distFromCentre, radius * 0.97f);
            float t = dd / radius;
            float ly = -0.12f * radius + 0.2f * radius * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            if (hasHill)
            {
                float hillReach = radius * 0.6f;
                if (dd < hillReach)
                {
                    float u = dd / hillReach;
                    float hillY = radius * 0.05f + radius * 0.275f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                    ly = Mathf.Max(ly, hillY);
                }
            }
            return transform.position + new Vector3(Mathf.Sin(angleRad) * dd, ly, Mathf.Cos(angleRad) * dd);
        }

        public static Island Nearest(Vector3 pos, bool requireResources = false)
        {
            Island best = null;
            float bestSq = float.MaxValue;
            foreach (var isle in All)
            {
                if (isle == null) continue;
                if (requireResources && !isle.HasResources) continue;
                Vector3 d = isle.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = isle; }
            }
            return best;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
