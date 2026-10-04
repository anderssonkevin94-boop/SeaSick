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

        public string ResourceName => resourceName;

        /// **This island has a small ore outcrop (2026-09-30)** on top of
        /// its own kind: a few Ore `ResourceNode`s the populator stood on
        /// it, so an island inside the ore ring still has ore to gather.
        /// Set once at world build (`TerrainWorldPopulator`), derived from
        /// the world, never saved. `Outpost.EnsureOreStock` books its seam.
        public bool HasOreOutcrop { get; private set; }
        internal void MarkOreOutcrop() => HasOreOutcrop = true;
        public float Remaining => remaining;
        public float Radius => radius;
        /// **Is this the player's home island?** Not a property of the island
        /// any more (2026-09-29, Kevin: "remove the 'home' island ... once
        /// you've built a campfire and a pier/dock you can make it into your
        /// home island"): it is whichever island the home berth (`Dock.Home`)
        /// stands on, and there is none until the player names one.
        public bool IsHome => Home == this;

        /// **The name a player reads** ("Island 6", never "Island_6 (1)"):
        /// underscores to spaces, any clone suffix dropped. Every UI line that
        /// names an island uses this, not `gameObject.name` (2026-10-04).
        public string DisplayName
        {
            get
            {
                string n = name;
                if (string.IsNullOrEmpty(n)) return "";
                n = n.Replace('_', ' ');
                int paren = n.IndexOf(" (");
                if (paren > 0) n = n.Substring(0, paren);
                return n.Trim();
            }
        }

        /// The island the home berth is on, or null before there is one.
        /// Set only through `Dock.Home`.
        public static Island Home { get; private set; }
        internal static void SetHome(Island isle) => Home = isle;
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
                // The ground does not move: one answer per metre she moves
                // (refreshed every 30 frames regardless), not one per frame
                // -- the prompt asks every frame while she is in range.
                if (beachCacheFrame >= 0 && Time.frameCount - beachCacheFrame < 30
                    && (worldPos - beachCachePos).sqrMagnitude < 1f) return beachCache;
                Vector3 d = transform.position - worldPos; d.y = 0f;
                float len = d.magnitude;
                bool result = len < 1f ? profile.hasBeach[0]
                    : FindBeach(TerrainHeight, worldPos, len + 50f, BeachMaxSlope, out _);
                beachCacheFrame = Time.frameCount; beachCachePos = worldPos; beachCache = result;
                return result;
            }
            return profile.hasBeach[SectorOf(BearingTo(worldPos, transform.position))];
        }

        /// Rays round the whole compass for the landing check (10 deg apart).
        const int BeachRays = 36;
        /// A beach this far off (m, to its waterline) counts as "nearby"
        /// whatever the nearest shore is; the old 25 m walk from the nearest
        /// shore point still applies beyond it.
        public const float BeachReach = 80f, BeachWalk = 25f;
        /// Longest ray (m): the ship is only offered a landing over the
        /// shelf, so a shore further than this is not the one she is off.
        const float BeachRayCap = 160f;

        /// **Is there a landable beach near `from` (2026-10-04)?** Pure: any
        /// height function `(x, z) -> y`, sea level 0, no scene.
        ///
        /// Kevin's phone: "Sheer cliff" off bays and headlands with sand in
        /// plain view. The old check fanned 13 rays over +-60 deg of the
        /// line to the island's CENTRE and only counted a beach within 25 m
        /// of the nearest shore hit. Off a headland the beaches lie abeam or
        /// astern of that line; inside a bay the cliff walls are nearer than
        /// the sand at its head by more than 25 m. Now: 36 rays round the
        /// whole compass from the ship, the first land on each, and the
        /// nearest one that is a beach wins, if its waterline is within
        /// `BeachReach` (or within `BeachWalk` of the nearest shore).
        ///
        /// A beach is ground that rises less than `maxSlope` over the 12 m
        /// inland -- measured UPHILL (the terrain's own gradient), not
        /// along the ray: a ray grazing a steep coast at a shallow angle
        /// climbs it slowly and used to pass a cliff as sand.
        /// `beach` = the chosen waterline point (y = its height).
        public static bool FindBeach(System.Func<float, float, float> height, Vector3 from,
            float maxRay, float maxSlope, out Vector3 beach)
        {
            beach = from;
            maxRay = Mathf.Min(maxRay, BeachRayCap);
            float nearest = float.MaxValue, bestT = float.MaxValue;
            for (int k = 0; k < BeachRays; k++)
            {
                float a = k * (Mathf.PI * 2f / BeachRays);
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float cap = Mathf.Min(maxRay, Mathf.Max(BeachReach, nearest + BeachWalk));
                for (float t = 0f; t < cap; t += 2f)
                {
                    Vector3 p = from + dir * t;
                    float h0 = height(p.x, p.z);
                    if (h0 <= -0.3f) continue;
                    if (t < nearest) nearest = t;
                    if (t < bestT && UphillRise(height, p, dir, h0) / 12f < maxSlope)
                    {
                        bestT = t;
                        beach = new Vector3(p.x, h0, p.z);
                    }
                    break;
                }
            }
            return bestT < float.MaxValue && bestT <= Mathf.Max(BeachReach, nearest + BeachWalk);
        }

        /// Rise over the 12 m uphill of the waterline point `p`: the
        /// gradient's direction a few metres inland, the ray's when the
        /// ground there is flat (or slopes back toward the sea).
        static float UphillRise(System.Func<float, float, float> height, Vector3 p, Vector3 dir, float h0)
        {
            Vector3 c = p + dir * 3f;
            float gx = height(c.x + 2f, c.z) - height(c.x - 2f, c.z);
            float gz = height(c.x, c.z + 2f) - height(c.x, c.z - 2f);
            Vector3 up = new Vector3(gx, 0f, gz);
            if (up.sqrMagnitude < 1e-6f || Vector3.Dot(up, dir) <= 0f) up = dir;
            else up.Normalize();
            Vector3 q = p + up * 12f;
            // From the waterline (sea level), not from `h0`: a 2 m step can
            // land past a narrow cliff face onto its flat top, and the rise
            // from there was ~0 -- a cliff top passed as a beach.
            return height(q.x, q.z) - Mathf.Min(h0, 0f);
        }

        // ---- self-test (pure, no scene) ----------------------------------
        //
        //   tools/selftest-outside-editor/run.sh SeaSick.World.Island.BeachSelfTest

        public static string BeachSelfTest()
        {
            var sb = new System.Text.StringBuilder();
            int pass = 0, total = 0;
            void Check(string name, bool want, System.Func<float, float, float> h, Vector3 from)
            {
                total++;
                bool got = FindBeach(h, from, 1000f, 0.5f, out Vector3 at);
                if (got == want) { pass++; return; }
                sb.Append($"FAIL {name}: want {want} got {got} at ({at.x:F0},{at.z:F0})\n");
            }
            const float Deep = -5f;
            // Straight coast along z = 0, land to +z.
            float Cliff(float z) => z < 0f ? Deep : Mathf.Min(20f, z * 10f);
            float Sand(float z) => z < 0f ? Deep : z * 0.1f;
            Check("straight cliff coast: sheer cliff", false, (x, z) => Cliff(z), new Vector3(0f, 0f, -15f));
            Check("cliff here, sand 40 m along: beach", true,
                  (x, z) => x > 40f ? Sand(z) : Cliff(z), new Vector3(0f, 0f, -15f));
            Check("cliff here, sand 200 m along: sheer cliff", false,
                  (x, z) => x > 200f ? Sand(z) : Cliff(z), new Vector3(0f, 0f, -15f));
            // A bay 40 m wide: cliff walls at |x| >= 20, sand at its head (z = 60).
            Check("inside a bay, sand at its head 60 m off: beach", true,
                  (x, z) => Mathf.Abs(x) >= 20f ? Mathf.Min(20f, (Mathf.Abs(x) - 20f) * 10f + 0.5f) : Sand(z - 60f),
                  Vector3.zero);
            // Off a headland: a cliff peninsula (|x| < 30, z < 100) runs out
            // from the island; the ship lies beside it, and the sand is a
            // shore 50 m off her bow, ~140 deg off the line to the centre.
            Check("beside a headland, sand 50 m off the other way: beach", true,
                  (x, z) => z > 110f && x > 20f ? Sand(z - 110f)
                          : Mathf.Abs(x) < 30f && z < 100f ? 20f : Deep,
                  new Vector3(45f, 0f, 60f));
            // The same headland with no sand anywhere: a real cliff.
            Check("beside a headland, cliffs only: sheer cliff", false,
                  (x, z) => Mathf.Abs(x) < 30f && z < 100f ? 20f : Deep,
                  new Vector3(45f, 0f, 60f));
            // A steep (0.8) coast met by grazing rays: still a cliff.
            Check("steep coast at a grazing angle: sheer cliff", false,
                  (x, z) => z < 0f ? Deep : z * 0.8f, new Vector3(0f, 0f, -10f));
            Check("open water: nothing", false, (x, z) => Deep, Vector3.zero);
            sb.Insert(0, $"{(pass == total ? "PASS" : "FAIL")} {pass}/{total}\n");
            return sb.ToString().TrimEnd();
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

        /// The island whose SHORE is nearest -- not whose centre is.
        ///
        /// Centre distance is the wrong question in a world of lobed islands
        /// 900-2000 m across: a ship lying 20 m off a small island's beach is
        /// routinely nearer the CENTRE of a big one several hundred metres
        /// away, and then everything that asks this -- the landing prompt,
        /// the grounding check, the camera's hill clamp, the crew's pathing
        /// -- answers about an island the ship is nowhere near. Measured on
        /// the shipped world it cost **12 of Island_1's 34 approach bearings
        /// their landing prompt outright**, on the island whose shore lies
        /// closest to home and which the player therefore meets first.
        ///
        /// The gap goes negative inside an island, which is what the
        /// grounding check wants anyway.
        public static Island Nearest(Vector3 pos, bool requireResources = false)
        {
            Island best = null;
            float bestGap = float.MaxValue;
            foreach (var isle in All)
            {
                if (isle == null) continue;
                if (requireResources && !isle.HasResources) continue;
                Vector3 d = isle.transform.position - pos;
                d.y = 0f;
                float gap = d.magnitude - isle.RadiusToward(pos);
                if (gap < bestGap) { bestGap = gap; best = isle; }
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
