using UnityEngine;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.World;

namespace SeaSick.Ocean
{
    /// <summary>
    /// **"Weather you can see coming"** (2026-09-28, GDD §2 pillars, §6
    /// "Sailing & ocean"/"Seasickness"): a squall as a PLACE, not a dice
    /// roll that lands on the ship. One is born off to a side of the bow,
    /// drifts across the water, and dissipates -- visible from a kilometre
    /// out (`SquallVisuals`) with a warning banner and an edge arrow
    /// (`SquallHud`) long before it arrives, so running for an island or
    /// riding it out slow is a choice made with time to make it, per
    /// Kevin's brief.
    ///
    /// **The spectrum is global.** `WeatherField.cs` states the hard
    /// constraint this system lives inside: one FFT for the whole ocean, so
    /// a squall cannot literally make the water rough only inside its disc.
    /// What it CAN do -- the same trick `SeaStateController.TargetHsAt`
    /// already plays with the westward storm region -- is drive the GLOBAL
    /// target off the SHIP's distance to something local. `SquallVisuals`
    /// is the part that is actually local and moves on its own; the sea
    /// state itself rises and falls with how deep the ship is inside the
    /// disc, through `SeaStateController.SetExternalBoost`.
    /// </summary>
    public class SquallDirector : MonoBehaviour
    {
        public static SquallDirector Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<SquallDirector>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("SquallDirector");
            go.AddComponent<SquallDirector>();
            DontDestroyOnLoad(go);
        }

        ShipMotor ship;
        AnchorController anchor;
        float nextShipLookup;

        float sailingClock;
        float nextSpawnAt = -1f;

        bool active;
        Vector3 centre;
        Vector3 driftDir;
        float life;
        bool warnedInside;
        bool warnedThrough;

        SquallVisuals visuals;

        /// For the HUD's edge arrow / distance readout.
        public bool Active => active;
        public Vector3 Centre => centre;
        public float Radius => SquallTuning.Radius;
        public float LifeSeconds => life;

        void Awake()
        {
            Instance = this;
            visuals = gameObject.AddComponent<SquallVisuals>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (Time.time >= nextShipLookup)
            {
                if (ship == null) ship = FindAnyObjectByType<ShipMotor>();
                if (anchor == null && ship != null) anchor = ship.GetComponent<AnchorController>();
                nextShipLookup = Time.time + 1f;
            }
            if (ship == null) { PublishBoost(0f); return; }

            float dt = Time.deltaTime;

            if (!active)
            {
                visuals.SetActive(false);
                PublishBoost(0f);
                if (!Sailing.IsLive(anchor)) return;
                if (nextSpawnAt < 0f) nextSpawnAt = SquallTuning.EverySeconds * Random.Range(0.7f, 1.3f);
                sailingClock += dt;
                if (sailingClock >= nextSpawnAt) TrySpawn();
                return;
            }

            // A live squall drifts and ages whether or not the ship is still
            // under way -- weather does not pause because she dropped anchor
            // to wait it out.
            centre += driftDir * SquallTuning.DriftSpeed * dt;
            life -= dt;

            float shipDist = Vector3.Distance(Flat(ship.transform.position), Flat(centre));
            float radius = SquallTuning.Radius;
            float inside01 = Mathf.Clamp01(1f - shipDist / Mathf.Max(1f, radius));

            // Riding it out SLOW is meant to be genuinely gentler (build
            // brief item 4), on top of whatever SmoothnessMeter already
            // reads off actual hull motion (a slow hull slams less through
            // the same sea for free). This is the belt-and-braces second
            // knob: the squall's OWN contribution eases off at low speed.
            float speed01 = Mathf.Clamp01(ship.CurrentSpeed / Mathf.Max(0.1f, SquallTuning.SpeedForFullBoost));
            float speedFactor = Mathf.Lerp(SquallTuning.MinSpeedFactor, 1f, speed01);
            PublishBoost(inside01 * speedFactor);

            visuals.SetActive(true);
            visuals.UpdateVisual(centre, radius, Mathf.Clamp01(1f - shipDist / (radius * 3f)));

            bool insideNow = shipDist < radius;
            if (insideNow && !warnedInside)
            {
                warnedInside = true;
                warnedThrough = false;
                Banner.Show("In the squall -- ease her down");
            }
            else if (!insideNow && warnedInside && !warnedThrough)
            {
                warnedThrough = true;
                Banner.Show("Through the squall");
            }

            if (life <= 0f) EndSquall();
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void TrySpawn()
        {
            sailingClock = 0f;
            nextSpawnAt = SquallTuning.EverySeconds * Random.Range(0.7f, 1.3f);

            Vector3 shipPos = ship.transform.position;
            float headingRad = ship.Heading * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            // A handful of tries at different bearings/ranges before giving
            // up for this cycle -- simpler than solving for the nearest camp
            // island analytically, and squalls are rare enough that a miss
            // just tries again next timer.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float dist = Random.Range(SquallTuning.MinSpawnDistance, SquallTuning.MaxSpawnDistance);
                float side = Random.value < 0.5f ? -1f : 1f;
                float aheadAngleDeg = Random.Range(20f, 65f); // off to one side of the bow
                float ang = aheadAngleDeg * Mathf.Deg2Rad;
                Vector3 dir = (fwd * Mathf.Cos(ang) + right * side * Mathf.Sin(ang)).normalized;
                Vector3 candidate = shipPos + dir * dist;

                if (NearCampIsland(candidate)) continue;

                Spawn(candidate, shipPos, fwd, dist);
                return;
            }
        }

        bool NearCampIsland(Vector3 candidate)
        {
            foreach (var o in Outpost.All)
            {
                if (o == null) continue;
                if (Vector3.Distance(Flat(candidate), Flat(o.transform.position)) < SquallTuning.CampClearance)
                    return true;
            }
            return false;
        }

        void Spawn(Vector3 spawnCentre, Vector3 shipPos, Vector3 shipFwd, float spawnDist)
        {
            centre = spawnCentre;
            // Heads back across the ship's line of advance rather than off
            // into open water on its own -- "on a heading that crosses the
            // ship's likely path", per the brief.
            Vector3 towardCourse = (shipPos + shipFwd * spawnDist) - spawnCentre;
            towardCourse.y = 0f;
            driftDir = towardCourse.sqrMagnitude > 1f ? towardCourse.normalized : -shipFwd;
            life = SquallTuning.LifeSeconds;
            warnedInside = false;
            warnedThrough = false;
            active = true;

            string compass = CompassName(BearingDeg(shipPos, centre));
            bool crosses = WillCrossPath(shipPos, shipFwd * ship.CurrentSpeed,
                centre, driftDir * SquallTuning.DriftSpeed, life);
            string crossText = crosses ? ", heading this way" : ", may pass clear";
            Banner.Show($"A squall to the {compass}{crossText}");
        }

        static float BearingDeg(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
        }

        static readonly string[] CompassNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        static string CompassName(float bearingDeg) => CompassNames[Mathf.RoundToInt(bearingDeg / 45f) % 8];

        /// Closest approach of the two straight-line extrapolations over the
        /// squall's remaining life -- "on a heading that crosses the ship's
        /// likely path" said as a number rather than eyeballed at spawn.
        static bool WillCrossPath(Vector3 shipPos, Vector3 shipVel, Vector3 squallPos, Vector3 squallVel, float horizonSeconds)
        {
            Vector3 relPos = squallPos - shipPos; relPos.y = 0f;
            Vector3 relVel = squallVel - shipVel; relVel.y = 0f;
            float a = relVel.sqrMagnitude;
            float t = a > 1e-4f ? -Vector3.Dot(relPos, relVel) / a : 0f;
            t = Mathf.Clamp(t, 0f, horizonSeconds);
            Vector3 closest = relPos + relVel * t;
            return closest.magnitude < SquallTuning.Radius * 1.3f;
        }

        void EndSquall()
        {
            active = false;
            visuals.SetActive(false);
            PublishBoost(0f);
        }

        void PublishBoost(float boost01)
        {
            var ctrl = SeaStateController.Instance;
            if (ctrl != null) ctrl.SetExternalBoost(boost01, SquallTuning.InsideSeverity);
        }

        // ---------------------------------------------------------- dev ----

        /// LIFE panel "Squall ahead": force one 500 m off the bow, heading
        /// straight down the ship's own course, so the whole sequence can be
        /// seen and tested without waiting out the real timer.
        public void DebugSpawnAhead()
        {
            if (ship == null) ship = FindAnyObjectByType<ShipMotor>();
            if (ship == null) return;
            Vector3 shipPos = ship.transform.position;
            float headingRad = ship.Heading * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
            Vector3 candidate = shipPos + fwd * 500f;
            driftDir = -fwd;
            life = SquallTuning.LifeSeconds;
            warnedInside = false;
            warnedThrough = false;
            active = true;
            centre = candidate;
            string compass = CompassName(BearingDeg(shipPos, centre));
            Banner.Show($"A squall to the {compass}, heading this way");
        }

        /// LIFE panel "Clear weather": dissipate whatever is live right now.
        public void DebugClear() => EndSquall();
    }
}
