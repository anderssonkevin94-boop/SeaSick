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
        int beachCacheFrame = -1; Vector3 beachCachePos; bool beachCache, beachCacheBeyond, beachCacheFound; Vector3 beachCacheAt;

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
                bool beyond = false; Vector3 at = worldPos;
                bool result = len < 1f ? profile.hasBeach[0]
                    : FindBeach(TerrainHeight, worldPos, len + 50f, LandingSlope, out at, out beyond);
                beachCacheFrame = Time.frameCount; beachCachePos = worldPos; beachCache = result;
                beachCacheBeyond = beyond; beachCacheAt = at; beachCacheFound = result && len >= 1f;
                return result;
            }
            return profile.hasBeach[SectorOf(BearingTo(worldPos, transform.position))];
        }

        /// **The step ashore for the beach `HasBeachToward(worldPos)` just
        /// found** (2026-10-04): 2 m inland of its waterline. False when it
        /// found none (or answered from the sector table). Read at the
        /// moment of finding and KEPT (`BeachHoldState`): at a sand sliver
        /// a metre off, asking again finds nothing.
        public bool BeachStepToward(Vector3 worldPos, out Vector3 step)
        {
            step = worldPos;
            if (TerrainHeight == null || !HasBeachToward(worldPos) || !beachCacheFound) return false;
            step = LandingStep(TerrainHeight, worldPos, beachCacheAt);
            return true;
        }

        /// **A held beach and the step ashore it was found with.** `Update`
        /// each time the landing is asked (`found`: this frame's raw answer;
        /// `stepFound`/`step`: `BeachStepToward`'s). The card stays on by
        /// `BeachHold`; the landing uses `Step`, the last FOUND step, never
        /// a fresh query from wherever she has drifted to. `Reset` on a new
        /// island.
        public struct BeachHoldState
        {
            public Vector3 heldAt, step;
            public float heldTime;
            public bool hasStep;
            public void Reset() { heldAt = step = Vector3.zero; heldTime = -1f; hasStep = false; }
            public bool Update(bool found, bool stepFound, Vector3 foundStep, Vector3 pos, float now)
            {
                if (found && stepFound) { step = foundStep; hasStep = true; }
                bool on = BeachHold(found, pos, now, ref heldAt, ref heldTime);
                if (!on) hasStep = false;
                return on;
            }
        }

        /// **Sand on this coast, but past the walk** (2026-10-04): when
        /// `HasBeachToward(worldPos)` says cliff, is there a beach within
        /// `BeachSeen` that is further than the nearest shore + `BeachWalk`?
        /// `at` = its waterline, for the cliff card's pointer.
        public bool SandBeyondReach(Vector3 worldPos, out Vector3 at)
        {
            at = worldPos;
            if (TerrainHeight == null || HasBeachToward(worldPos) || !beachCacheBeyond) return false;
            at = beachCacheAt;
            return true;
        }

        /// The pointer's numbers: flat distance to `at` rounded to 10 m (at
        /// least 10) and its side relative to her heading `fwd` -- 0 ahead,
        /// 1 starboard, 2 astern, 3 port (45 deg either side of each). Pure,
        /// no garbage: the card's cache key is built from these every frame.
        public static void SandPointer(Vector3 ship, Vector3 fwd, Vector3 at, out int metres, out int side)
        {
            Vector3 to = at - ship; to.y = 0f; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            metres = Mathf.Max(10, Mathf.RoundToInt(to.magnitude / 10f) * 10);
            float ahead = Vector3.Dot(to, fwd), right = to.x * fwd.z - to.z * fwd.x;
            float ang = Mathf.Atan2(right, ahead) * Mathf.Rad2Deg;
            side = Mathf.Abs(ang) <= 45f ? 0 : Mathf.Abs(ang) >= 135f ? 2 : ang > 0f ? 1 : 3;
        }

        static readonly string[] SideWords = { "ahead", "starboard", "astern", "port" };
        /// "Sand 60 m astern": at most "Sand 120 m starboard", 20 characters.
        public static string SandPointerText(int metres, int side) => $"Sand {metres} m {SideWords[side & 3]}";

        /// **The landing check's slope: can a man walk up from the
        /// waterline** (`Walkability`'s man grade, tan 33 deg = 0.65), never
        /// stricter than `BeachMaxSlope`. Measured 2026-10-04 on the seed-1337
        /// world, 4969 shore crossings x 7 approach angles: against 0.5 the
        /// uphill rule refused 960 shore points the old along-the-ray rule
        /// had called beach, 95 % of them 0.5-0.65 slopes a villager walks
        /// (the old rule passed them only because a slanting ray reads a
        /// fraction of the slope). At the man grade it refuses 78 and
        /// accepts 76 new ones -- the old coverage, minus faces steeper than
        /// anyone can climb, and independent of the approach angle.
        public static float LandingSlope => Mathf.Max(BeachMaxSlope, Walkability.Grade(Walkability.Feet.Man));

        /// **Hold a found beach (2026-10-04).** At a narrow sand sliver
        /// `FindBeach` is true on a ~1 m column and false a metre either side
        /// (2 m ray steps, 10 deg rays), so "Land here" / "Sheer cliff"
        /// blinked as she drifted. A beach, once found, stays offered while
        /// she is within `BeachHoldRadius` of where it was last found OR it
        /// was found within `BeachHoldSeconds`; cliff -> beach is immediate.
        /// Pure: the caller keeps `heldAt`/`heldTime` (time < 0 = nothing
        /// held) per island and resets them when the island changes.
        public const float BeachHoldRadius = 6f, BeachHoldSeconds = 1f;
        public static bool BeachHold(bool found, Vector3 pos, float now, ref Vector3 heldAt, ref float heldTime)
        {
            if (found) { heldAt = pos; heldTime = now; return true; }
            if (heldTime < 0f) return false;
            float dx = pos.x - heldAt.x, dz = pos.z - heldAt.z;
            return dx * dx + dz * dz <= BeachHoldRadius * BeachHoldRadius
                || now - heldTime <= BeachHoldSeconds;
        }

        /// **Where a landing party steps ashore (2026-10-04)**: the beach
        /// `FindBeach` offered from `from`, or false (no terrain, no beach).
        /// The plank used to run to the island-centre bearing's outline
        /// point, which off a headland or in a bay is the cliff the card
        /// just said she was not landing on.
        public bool TryLandingStep(Vector3 from, out Vector3 step)
        {
            step = from;
            if (TerrainHeight == null) return false;
            Vector3 d = transform.position - from; d.y = 0f;
            if (!FindBeach(TerrainHeight, from, d.magnitude + 50f, LandingSlope, out Vector3 beach)) return false;
            step = LandingStep(TerrainHeight, from, beach);
            return true;
        }

        /// A beach's waterline point (`FindBeach`'s `beach`) moved
        /// `LandingInland` metres on along the line from `from`, on the
        /// ground: dry sand to step onto, not the shallows. Pure.
        public const float LandingInland = 2f;
        public static Vector3 LandingStep(System.Func<float, float, float> height, Vector3 from, Vector3 beach)
        {
            Vector3 dir = beach - from; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Vector3 p = beach + dir * LandingInland;
            p.y = height(p.x, p.z);
            return p.y >= beach.y ? p : beach;
        }

        /// Rays round the whole compass for the landing check (10 deg apart).
        const int BeachRays = 36;
        /// A beach counts only within this walk (m) of the nearest shore --
        /// the old check's reach, so the plank to it is never much longer
        /// than before (2026-10-04: an 80 m reach meant an 80 m plank).
        public const float BeachWalk = 25f;
        /// Further than that, sand out to here (m) is still found, so the
        /// cliff card can point at it ("Sand 60 m astern").
        public const float BeachSeen = 120f;
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
        /// `BeachWalk` of the nearest shore. A beach further than that comes
        /// back as `beyond` (and in `beach`), for the cliff card's pointer.
        ///
        /// A beach is ground that rises less than `maxSlope` over the 12 m
        /// inland -- measured UPHILL (the terrain's own gradient), not
        /// along the ray: a ray grazing a steep coast at a shallow angle
        /// climbs it slowly and used to pass a cliff as sand.
        /// `beach` = the chosen waterline point (y = its height).
        public static bool FindBeach(System.Func<float, float, float> height, Vector3 from,
            float maxRay, float maxSlope, out Vector3 beach)
            => FindBeach(height, from, maxRay, maxSlope, out beach, out _);

        public static bool FindBeach(System.Func<float, float, float> height, Vector3 from,
            float maxRay, float maxSlope, out Vector3 beach, out bool beyond)
        {
            beach = from; beyond = false;
            maxRay = Mathf.Min(maxRay, BeachRayCap);
            float nearest = float.MaxValue, bestT = float.MaxValue;
            for (int k = 0; k < BeachRays; k++)
            {
                float a = k * (Mathf.PI * 2f / BeachRays);
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float cap = Mathf.Min(maxRay, BeachSeen);
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
            if (bestT == float.MaxValue) return false;
            if (bestT <= nearest + BeachWalk) return true;
            beyond = true;
            return false;
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
            Check("cliff here, sand 25 m along (within the walk): beach", true,
                  (x, z) => x > 25f ? Sand(z) : Cliff(z), new Vector3(0f, 0f, -15f));
            Check("cliff here, sand 200 m along: sheer cliff", false,
                  (x, z) => x > 200f ? Sand(z) : Cliff(z), new Vector3(0f, 0f, -15f));
            // A bay 40 m wide: cliff walls at |x| >= 20, sand at its head
            // (z = 60): past the walls' 20 m + 25 m walk, so refused, and the
            // card points at it -- she heads -Z, so it lies astern.
            {
                total++;
                System.Func<float, float, float> bay = (x, z) =>
                    Mathf.Abs(x) >= 20f ? Mathf.Min(20f, (Mathf.Abs(x) - 20f) * 10f + 0.5f) : Sand(z - 60f);
                bool got = FindBeach(bay, Vector3.zero, 1000f, 0.5f, out Vector3 far, out bool beyond);
                SandPointer(Vector3.zero, Vector3.back, far, out int m, out int sd);
                string txt = SandPointerText(m, sd);
                if (!got && beyond && txt == "Sand 60 m astern") pass++;
                else sb.Append($"FAIL bay sand 60 m off: want refused + 'Sand 60 m astern', got {got} beyond {beyond} '{txt}'\n");
            }
            // Off a headland: a cliff peninsula (|x| < 30, z < 100); she lies
            // beside it 15 m off, sand 50 m off her bow (heading +Z): refused
            // (past 15 + 25 m), pointed at ahead.
            {
                total++;
                System.Func<float, float, float> hd = (x, z) => z > 110f && x > 20f ? Sand(z - 110f)
                        : Mathf.Abs(x) < 30f && z < 100f ? 20f : Deep;
                Vector3 from = new Vector3(45f, 0f, 60f);
                bool got = FindBeach(hd, from, 1000f, 0.5f, out Vector3 far, out bool beyond);
                SandPointer(from, Vector3.forward, far, out int m, out int sd);
                if (!got && beyond && sd == 0 && m == 50) pass++;
                else sb.Append($"FAIL headland sand 50 m ahead: got {got} beyond {beyond} '{SandPointerText(m, sd)}'\n");
            }
            // Pointer sides: starboard is +X when she heads +Z.
            {
                total++;
                SandPointer(Vector3.zero, Vector3.forward, new Vector3(34f, 0f, 3f), out int m, out int sd);
                if (SandPointerText(m, sd) == "Sand 30 m starboard") pass++;
                else sb.Append($"FAIL pointer starboard: '{SandPointerText(m, sd)}'\n");
            }
            // The same headland with no sand anywhere: a real cliff.
            Check("beside a headland, cliffs only: sheer cliff", false,
                  (x, z) => Mathf.Abs(x) < 30f && z < 100f ? 20f : Deep,
                  new Vector3(45f, 0f, 60f));
            // A steep (0.8) coast met by grazing rays: still a cliff.
            Check("steep coast at a grazing angle: sheer cliff", false,
                  (x, z) => z < 0f ? Deep : z * 0.8f, new Vector3(0f, 0f, -10f));
            Check("open water: nothing", false, (x, z) => Deep, Vector3.zero);
            // A 0.6 slope (31 deg): steeper than the 0.5 knob, walkable by a
            // man (33 deg) -- a beach at the landing slope.
            {
                total++;
                bool got = FindBeach((x, z) => z < 0f ? Deep : z * 0.6f, new Vector3(0f, 0f, -10f),
                                     1000f, LandingSlope, out _);
                if (got) pass++; else sb.Append("FAIL 0.6 slope at the landing slope: want true got false\n");
                total++;
                got = FindBeach((x, z) => z < 0f ? Deep : z * 0.8f, new Vector3(0f, 0f, -10f),
                                1000f, LandingSlope, out _);
                if (!got) pass++; else sb.Append("FAIL 0.8 slope at the landing slope: want false got true\n");
            }
            // The landing step is the found sand, not the cliff in front of
            // her: cliff coast here, sand from x = 25 along it.
            {
                System.Func<float, float, float> hl = (x, z) => x > 25f ? Sand(z) : Cliff(z);
                Vector3 from = new Vector3(0f, 0f, -15f);
                total++;
                if (FindBeach(hl, from, 1000f, 0.5f, out Vector3 b))
                {
                    Vector3 st = LandingStep(hl, from, b);
                    bool onSand = st.x > 25f && st.z >= 0f && st.y >= 0f && st.y < 1f
                                  && (st - b).magnitude <= LandingInland + 0.5f;
                    if (onSand) pass++;
                    else sb.Append($"FAIL landing step off the sand: ({st.x:F1},{st.y:F1},{st.z:F1})\n");
                }
                else sb.Append("FAIL landing step: no beach found\n");
            }
            // The hold: a sliver found at x = 0, lost a metre on.
            void Hold(string name, bool want, bool got) { total++; if (got == want) pass++; else sb.Append($"FAIL hold {name}: want {want} got {got}\n"); }
            Vector3 hAt = Vector3.zero; float hT = -1f;
            Hold("nothing found yet", false, BeachHold(false, Vector3.zero, 0f, ref hAt, ref hT));
            Hold("found", true, BeachHold(true, new Vector3(10f, 0f, 0f), 5f, ref hAt, ref hT));
            Hold("1 m on, 0.2 s later", true, BeachHold(false, new Vector3(11f, 0f, 0f), 5.2f, ref hAt, ref hT));
            Hold("5 m on, 10 s later (parked by the sliver)", true, BeachHold(false, new Vector3(10f, 0f, 5f), 15f, ref hAt, ref hT));
            Hold("20 m on but 0.5 s after (fast)", true, BeachHold(false, new Vector3(30f, 0f, 0f), 5.5f, ref hAt, ref hT));
            Hold("20 m on, 2 s after: cliff", false, BeachHold(false, new Vector3(30f, 0f, 0f), 7f, ref hAt, ref hT));
            Hold("found again at once", true, BeachHold(true, new Vector3(30f, 0f, 0f), 7.1f, ref hAt, ref hT));
            // The held step: found once at a sliver, then lost -- the landing
            // still has the step found then (Island_28, 2026-10-04: asking
            // again from the ship and from the held spot both found nothing
            // and the plank fell back onto the cliff foot).
            {
                var hs = new BeachHoldState(); hs.Reset();
                Vector3 sandStep = new Vector3(3f, 0.2f, 12f);
                Hold("state: nothing yet, no step", false, hs.Update(false, false, default, Vector3.zero, 0f) || hs.hasStep);
                Hold("state: found with its step", true, hs.Update(true, true, sandStep, Vector3.zero, 1f) && hs.hasStep);
                Hold("state: lost 0.5 m on, held, SAME step", true,
                     hs.Update(false, false, default, new Vector3(0.5f, 0f, 0f), 1.2f) && hs.hasStep && hs.step == sandStep);
                Hold("state: hold over, no step left", false,
                     hs.Update(false, false, default, new Vector3(40f, 0f, 0f), 9f) || hs.hasStep);
            }
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
