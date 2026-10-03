using SeaSick.Save;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.UI.Menus;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **The kraken in the wild** (GDD §6 "The Kraken", build step 4): the
    /// one thing that decides WHEN a kraken surfaces, and warns the player
    /// first. Everything the kraken does once it is up is `Kraken`; this is
    /// only the dice, the deep-water test and the ten seconds of "something
    /// is under there".
    ///
    /// **The rules (approved 2026-10-03).**
    /// <list type="bullet">
    /// <item>**Deep water only.** Never inside `minDistanceFromHome` of home
    ///   (`VoyageManager.HomePoint`, the start point the raider rings are
    ///   laid from), and never anywhere the sea floor is shallower than
    ///   `minDeepDepth` or an island or reef is near: the test reads
    ///   `Island.TerrainHeight` (the analytic height field, valid at any
    ///   x, z) at the spot and on a ring round it, and keeps clear of
    ///   `Island.All` shores and `Reef.All` rocks. So it can never surface
    ///   on a beach, a reef or the shelf.</item>
    /// <item>**Distance is the difficulty.** A roll every
    ///   `checkIntervalSeconds` while she is sailing live: a base chance that
    ///   climbs with every kilometre past the minimum, multiplied at night and
    ///   in a storm, capped.</item>
    /// <item>**At most one per voyage**, plus a cooldown after it has gone.
    ///   There is no voyage object in the game, so a voyage is defined here
    ///   as "since she last lay anchored, ashore or at home": the flag
    ///   resets whenever `AnchorController` reads Anchored/Ashore or
    ///   `VoyageManager.AtHome`.</item>
    /// <item>**Only live sailing.** The same guard every sea system uses
    ///   (`Sailing.IsLive`: not paused, not offline catch-up, underway), and
    ///   not with the home card, a menu, the shipyard or the loading screen
    ///   up. Never saved: `Kraken` is transient and so is every flag here.</item>
    /// <item>**Never an ambush.** `warningSeconds` before it surfaces the
    ///   spot is picked ~`spawnDistance` off a bow quarter (never dead
    ///   ahead), a toast ("Something huge below..."), a vibration and a
    ///   `KrakenShadow` (dark shadow + bubbling) show there, and
    ///   `SeaEdgeMarkers` points at `WarningPoint`. The shadow is a living
    ///   thing pacing the ship: it keeps station off her bow quarter, so the
    ///   breach still lands on a quarter if she holds her course or turns.
    ///   If by the end it would surface somewhere shallow, behind her or
    ///   near land, it quietly does not (and the voyage keeps its one).</item>
    /// </list>
    ///
    /// Self-installing singleton, same shape as `SquallDirector`: one per
    /// boot, `DontDestroyOnLoad`, no scene wiring.
    public class KrakenDirector : MonoBehaviour
    {
        public static KrakenDirector Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<KrakenDirector>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("KrakenDirector");
            go.AddComponent<KrakenDirector>();
            DontDestroyOnLoad(go);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            WarningPoint = null;
            WarningSecondsLeft = 0f;
        }

        /// Where the water is darkening, flat (y = 0), while a warning runs;
        /// null otherwise. `SeaEdgeMarkers` reads it.
        public static Vector3? WarningPoint { get; private set; }

        /// Seconds until it surfaces while `WarningPoint` has a value, else 0.
        public static float WarningSecondsLeft { get; private set; }

        /// A warning is running right now.
        public static bool Warning => WarningPoint.HasValue;

        /// Off the bow, never dead ahead: the angle to either side of the
        /// ship's heading the spot is picked in, degrees. `Kraken` itself
        /// keeps 35..62; this sits inside it.
        const float QuarterMinDeg = 38f, QuarterMaxDeg = 62f;
        /// A spot may be this far round (from dead ahead) at the moment it
        /// surfaces before it is called off as "behind her".
        const float QuarterAbortDeg = 115f;
        /// The tell never gets closer to the ship than this, flat metres,
        /// before it is called off (a ring the ship already sailed over).
        const float MinSurfaceMetres = 30f;
        /// Re-test the spot the shadow is chasing this often, seconds.
        const float RetestSeconds = 0.3f;
        /// Seconds the next roll waits after a warning was called off.
        const float CalledOffDelay = 30f;

        ShipMotor ship;
        AnchorController anchor;
        VoyageManager voyage;
        float nextLookup;

        float checkClock;
        float lastEndedAt = -99999f;
        bool usedThisVoyage;

        // The kraken this director raised, to tell its Ended from a dev one.
        Kraken ours;
        bool haveOurs;

        // ---- the warning ----
        bool warning;
        bool warnDev;
        float warnTotal, warnLeft;
        float side, angleDeg;
        Vector3 point;           // the shadow, flat
        Vector3 want;            // where it is chasing
        bool wantOk;
        float nextRetest;
        bool endHaptic;
        KrakenShadow tell;

        void Awake() => Instance = this;

        void OnEnable() => Kraken.Ended += OnEnded;

        void OnDisable()
        {
            Kraken.Ended -= OnEnded;
            CancelWarning();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnded(Kraken k, KrakenOutcome outcome)
        {
            if (!haveOurs || !ReferenceEquals(k, ours)) return;
            haveOurs = false;
            ours = null;
            lastEndedAt = Time.time;
        }

        void Update()
        {
            if (Time.time >= nextLookup)
            {
                if (ship == null) ship = FindAnyObjectByType<ShipMotor>();
                if (anchor == null && ship != null) anchor = ship.GetComponent<AnchorController>();
                if (voyage == null) voyage = FindAnyObjectByType<VoyageManager>();
                nextLookup = Time.time + 1f;
            }
            if (ship == null) { CancelWarning(); return; }

            // One that vanished without announcing itself (a scene change)
            // still starts the cooldown.
            if (haveOurs && ours == null) { haveOurs = false; lastEndedAt = Time.time; }

            TrackVoyage();

            float dt = Time.deltaTime;
            if (warning) { TickWarning(dt); return; }
            if (dt <= 0f) return;

            if (!KrakenSpawnTuning.enabled || !CanRoll()) { checkClock = 0f; return; }
            checkClock += dt;
            if (checkClock < Mathf.Max(3f, KrakenSpawnTuning.checkIntervalSeconds)) return;
            checkClock = 0f;
            Roll();
        }

        // ------------------------------------------------------------- voyage

        /// A voyage ends when she lies anchored, ashore or at home; the next
        /// one gets its kraken back. Runs whether or not she is "live".
        void TrackVoyage()
        {
            bool landed = voyage != null && voyage.AtHome;
            if (!landed && anchor != null)
            {
                var s = anchor.CurrentState;
                landed = s == AnchorController.State.Anchored || s == AnchorController.State.Ashore;
            }
            if (landed) usedThisVoyage = false;
        }

        /// Everything that must be true to even roll the dice.
        bool CanRoll()
        {
            if (!Sailing.IsLive(anchor)) return false;
            if (voyage != null && voyage.AtHome) return false;
            return !ScreenBlocked();
        }

        /// The home card, a menu, the shipyard and the loading screen: the
        /// sea is not being sailed even if the clock is running.
        static bool ScreenBlocked()
        {
            if (GameMenus.Current != GameMenus.Mode.None) return true;
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return true;
            var loading = LoadingScreen.Instance;
            return loading != null && !loading.Finished;
        }

        // --------------------------------------------------------------- odds

        /// Flat metres from home (`VoyageManager.HomePoint`; the world origin
        /// if there is none), the distance the raider rings are measured in.
        float HomeDistance(Vector3 p)
        {
            Vector3 home = voyage != null && voyage.HomePoint != null ? voyage.HomePoint.position : Vector3.zero;
            return Flat(p - home).magnitude;
        }

        /// The chance of one roll at `metresFromHome`, weather and night
        /// included. Zero inside the minimum.
        public static float ChanceAt(float metresFromHome)
        {
            float over = metresFromHome - KrakenSpawnTuning.minDistanceFromHome;
            if (over < 0f) return 0f;
            float c = KrakenSpawnTuning.baseChancePerCheck + KrakenSpawnTuning.chancePerExtraKm * (over / 1000f);
            if (Sailing.IsNight) c *= Mathf.Max(1f, KrakenSpawnTuning.nightMultiplier);
            float storm = Mathf.Clamp01(Sailing.Storminess01);
            c *= 1f + (Mathf.Max(1f, KrakenSpawnTuning.stormMultiplier) - 1f) * storm;
            return Mathf.Clamp(c, 0f, KrakenSpawnTuning.maxChancePerCheck);
        }

        void Roll()
        {
            if (Kraken.Active != null || usedThisVoyage) return;
            if (Time.time - lastEndedAt < KrakenSpawnTuning.cooldownSeconds) return;
            if (Island.TerrainHeight == null) return;   // the world is not built yet

            Vector3 shipPos = ship.transform.position;
            float chance = ChanceAt(HomeDistance(shipPos));
            if (chance <= 0f || Random.value >= chance) return;

            // The ship herself must be in open deep water, not skirting a shelf.
            if (!DeepAndClear(shipPos)) return;
            BeginWarning(false);
        }

        // --------------------------------------------------------- deep water

        /// The ring the deep test samples round a spot, metres: the body is
        /// ~65 m across, so half of that and a little over.
        const float RingMetres = 40f;

        /// True if the sea is at least `minDeepDepth` deep at `p` and on a
        /// ring round it, and no island shore or reef rock is within
        /// `minIslandClearance` of the ring's edge. 9 height samples plus a
        /// scan of the island and reef lists: fine at a few Hz.
        public static bool DeepAndClear(Vector3 p)
        {
            var h = Island.TerrainHeight;
            if (h == null) return false;   // an unknown bottom must never read as deep
            float need = KrakenSpawnTuning.minDeepDepth;
            if (-h(p.x, p.z) < need) return false;
            for (int k = 0; k < 8; k++)
            {
                float a = k * (Mathf.PI * 2f / 8f);
                if (-h(p.x + Mathf.Sin(a) * RingMetres, p.z + Mathf.Cos(a) * RingMetres) < need) return false;
            }

            float clear = KrakenSpawnTuning.minIslandClearance + RingMetres;
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                if (Flat(isle.transform.position - p).magnitude - isle.MaxRadius < clear) return false;
            }
            foreach (var reef in Reef.All)
            {
                if (reef == null) continue;
                if (Flat(reef.transform.position - p).magnitude - reef.Radius < clear) return false;
            }
            return true;
        }

        // ---------------------------------------------------------- the warning

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        Vector3 ShipForward()
        {
            Vector3 f = Flat(ship.transform.forward);
            return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        }

        /// The point `spawnDistance` off the bow, `ang` degrees to `sideSign`.
        Vector3 QuarterPoint(Vector3 shipPos, Vector3 fwd, float sideSign, float ang)
        {
            Vector3 dir = Quaternion.Euler(0f, sideSign * ang, 0f) * fwd;
            Vector3 p = shipPos + dir * Mathf.Max(20f, KrakenSpawnTuning.spawnDistance);
            p.y = 0f;
            return p;
        }

        /// A bow quarter with deep, clear water, tried on both sides at a few
        /// angles. `dev` skips the water test (a dev summon near home has no
        /// deep water to find).
        bool TryPick(bool dev, out float sideSign, out float ang, out Vector3 spot)
        {
            Vector3 shipPos = ship.transform.position;
            Vector3 fwd = ShipForward();
            float first = Random.value < 0.5f ? -1f : 1f;
            for (int i = 0; i < 8; i++)
            {
                sideSign = (i % 2 == 0) ? first : -first;
                ang = Random.Range(QuarterMinDeg, QuarterMaxDeg);
                spot = QuarterPoint(shipPos, fwd, sideSign, ang);
                if (dev || DeepAndClear(spot)) return true;
            }
            sideSign = 1f; ang = QuarterMinDeg; spot = default;
            return false;
        }

        bool BeginWarning(bool dev)
        {
            if (warning || Kraken.Active != null) return false;
            if (!TryPick(dev, out side, out angleDeg, out point)) return false;

            warning = true;
            warnDev = dev;
            warnTotal = Mathf.Max(3f, KrakenSpawnTuning.warningSeconds);
            warnLeft = warnTotal;
            want = point;
            wantOk = true;
            nextRetest = RetestSeconds;
            endHaptic = false;
            if (!dev) usedThisVoyage = true;

            if (tell == null) tell = KrakenShadow.Create();
            tell.Set(point, 0f, Vector3.zero);
            WarningPoint = point;
            WarningSecondsLeft = warnLeft;

            Banner.Show("Something huge below…", 4f);
            OverboardHaptics.Warning();
            return true;
        }

        void TickWarning(float dt)
        {
            // Called off for good: she has landed, the clock is offline, or a
            // menu took over. A pause only freezes it (dt is 0 at timeScale 0).
            bool landed = (anchor != null && anchor.CurrentState != AnchorController.State.Underway)
                          || (voyage != null && voyage.AtHome);
            if (landed || AwayProgress.Running || GameMenus.Current == GameMenus.Mode.Home)
            {
                CancelWarning();
                return;
            }
            if (dt <= 0f || Time.timeScale <= 0f) return;

            warnLeft -= dt;
            Vector3 shipPos = ship.transform.position;
            Vector3 fwd = ShipForward();

            // The shadow paces the ship: it chases the bow-quarter point
            // (led a little by her own velocity) at a hair more than her
            // speed, and holds where it is whenever that point is not deep.
            want = QuarterPoint(shipPos, fwd, side, angleDeg) + Flat(ship.Velocity) * 0.3f;
            nextRetest -= dt;
            if (nextRetest <= 0f)
            {
                nextRetest = RetestSeconds;
                wantOk = warnDev || DeepAndClear(want);
            }
            Vector3 before = point;
            if (wantOk)
            {
                float speed = Mathf.Max(6f, ship.CurrentSpeed * 1.25f + 2f);
                point = Vector3.MoveTowards(point, want, speed * dt);
            }
            Vector3 travel = dt > 0f ? (point - before) / dt : Vector3.zero;

            float t01 = Mathf.Clamp01(1f - warnLeft / warnTotal);
            float intensity = t01 * t01 * (3f - 2f * t01);
            if (tell != null) tell.Set(point, intensity, travel);
            WarningPoint = point;
            WarningSecondsLeft = Mathf.Max(0f, warnLeft);
            FrameWarning();

            // A second pulse shortly before the breach.
            if (!endHaptic && warnLeft <= 2.5f)
            {
                endHaptic = true;
                OverboardHaptics.Warning();
            }

            if (warnLeft <= 0f) Surface();
        }

        void Surface()
        {
            Vector3 shipPos = ship.transform.position;
            Vector3 toSpot = Flat(point - shipPos);
            float dist = toSpot.magnitude;
            float ang = dist > 1f ? Vector3.Angle(ShipForward(), toSpot) : 180f;
            bool ok = warnDev || (DeepAndClear(point) && dist >= MinSurfaceMetres
                                  && dist <= KrakenSpawnTuning.spawnDistance * 2f && ang <= QuarterAbortDeg);
            Vector3 spot = point;
            bool dev = warnDev;
            EndWarning();

            if (!ok || Kraken.Active != null)
            {
                // It lost her, or the water is wrong: the shadow just passes
                // on. The voyage keeps its one, the next roll waits a while.
                if (!dev) usedThisVoyage = false;
                checkClock = -CalledOffDelay;
                return;
            }

            spot.y = 0f;
            var k = Kraken.SummonAt(spot, shipPos, dev);
            if (k == null)
            {
                if (!dev) usedThisVoyage = false;
                return;
            }
            if (!dev) { ours = k; haveOurs = true; }
        }

        // ------------------------------------------------------------- the camera

        /// Swing the chase camera onto the shadow while the warning runs
        /// (2026-10-03: on the portrait screen the boil sat outside the frame
        /// on its bow quarter, so "never an ambush" was a toast and nothing
        /// to look at). The kraken's own `SeaFocus` shot, at a shadow's
        /// height; released the frame before it surfaces so `Kraken` takes
        /// the focus over. A real lock outranks it, and it only ever clears
        /// what it set.
        void FrameWarning()
        {
            if (cam == null) cam = FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>();
            if (cam == null || !KrakenTuning.frameCamera || cam.LockTarget != null) { ReleaseWarningCamera(); return; }
            var cur = cam.SeaFocus;
            if (cur.HasValue && !(ownsCam && cur.Value == camPoint)) { ownsCam = false; return; }
            camPoint = new Vector3(point.x, 0f, point.z);
            cam.SeaFocus = camPoint;
            ownsCam = true;
            cam.SeaFocusHeight = 6f;
            cam.SeaFocusRadius = 22f;
            cam.SeaFocusShip01 = SeaSick.UI.HudLayout.Wide ? KrakenTuning.camShip01Desk : KrakenTuning.camShip01;
            cam.SeaFocusTop01 = KrakenTuning.camTop01;
            cam.SeaFocusElevDeg = KrakenTuning.camElevDeg;
            cam.SeaFocusSwingDeg = KrakenTuning.camSwingDeg;
            cam.SeaFocusMaxBack = KrakenTuning.camMaxBack;
        }

        void ReleaseWarningCamera()
        {
            if (cam != null && ownsCam && cam.SeaFocus.HasValue && cam.SeaFocus.Value == camPoint)
                cam.SeaFocus = null;
            ownsCam = false;
        }

        SeaSick.CameraRig.ChaseCamera cam;
        Vector3 camPoint;
        bool ownsCam;

        /// The tell stops and fades; the markers drop it.
        void EndWarning()
        {
            ReleaseWarningCamera();
            warning = false;
            WarningPoint = null;
            WarningSecondsLeft = 0f;
            if (tell != null) { tell.Finish(); tell = null; }
        }

        /// A warning that never ends in a kraken: she landed, the game went
        /// offline, or the director is going away. Gives the voyage its one
        /// back, since nothing came.
        void CancelWarning()
        {
            if (!warning) return;
            bool dev = warnDev;
            EndWarning();
            if (!dev) usedThisVoyage = false;
        }

        // ------------------------------------------------------------------ dev

        /// Dev (Settings -> dev row): start a warning NOW off a bow quarter,
        /// skipping the dice, the home distance, the cooldown, the voyage rule
        /// and the deep-water test, so the whole warning -> breach sequence
        /// can be seen anywhere. The kraken it raises is a dev summon. Returns
        /// a line for a banner. Also the eval entry point:
        /// `SeaSick.Combat.KrakenDirector.DevWarnNow()`.
        public static string DevWarnNow()
        {
            var d = Instance;
            if (d == null) return "No kraken director.";
            if (d.ship == null) d.ship = FindAnyObjectByType<ShipMotor>();
            if (d.ship == null) return "No ship to warn.";
            if (Kraken.Active != null) return "The kraken is already up.";
            if (d.warning) return "The water is already darkening.";
            return d.BeginWarning(true)
                ? "Something stirs below: it surfaces in " + Mathf.RoundToInt(d.warnTotal) + " s."
                : "Could not pick a spot.";
        }

        /// Dev: one line on why a kraken would or would not roll right now.
        public static string DevStatus()
        {
            var d = Instance;
            if (d == null || d.ship == null) return "No ship.";
            Vector3 p = d.ship.transform.position;
            var h = Island.TerrainHeight;
            string depth = h != null ? Mathf.RoundToInt(-h(p.x, p.z)) + " m deep" : "no height field";
            float home = d.HomeDistance(p);
            float cd = Mathf.Max(0f, KrakenSpawnTuning.cooldownSeconds - (Time.time - d.lastEndedAt));
            return (home / 1000f).ToString("0.0") + " km from home, " + depth
                + (DeepAndClear(p) ? ", deep and clear" : ", not deep and clear")
                + ", " + (ChanceAt(home) * 100f).ToString("0.0") + "% a roll"
                + ", cooldown " + Mathf.CeilToInt(cd) + " s"
                + (d.usedThisVoyage ? ", one already this voyage" : "")
                + (d.warning ? ", warning running" : "");
        }
    }
}
