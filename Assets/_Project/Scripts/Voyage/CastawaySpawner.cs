using UnityEngine;
using SeaSick.Combat;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.Ship.SeaLife;
using SeaSick.UI;
using SeaSick.World;
using SeaSick.World.Life;

namespace SeaSick.Voyage
{
    /// **People found floating in the water** (Kevin, 2026-09-30: *"Please
    /// add so people can be found floating in the water."*). A NEW person --
    /// never one of your crew -- clinging to a board (`Ship.SeaLife.Castaway`).
    ///
    /// Three jobs, all off the player's own ship:
    ///
    /// 1. **Spawn.** About one per `RecruitTuning.SeaCastawayEverySeconds`
    ///    (600 s, ±25 %) of live sailing, at most one in the water at a time,
    ///    `SeaCastawayAheadMin..Max` (110..200 m) ahead within
    ///    ±`SeaCastawayConeDeg` (60°) of the bow, so you can see and reach
    ///    them. Never near land (`SeaLifeSpawn.ClearOfLand`, the same 60 m
    ///    shore margin flotsam and bottles use), never over less than
    ///    `SeaCastawayMinDepth` (4 m) of water, never on a reef. The clock
    ///    only runs while `Sailing.IsLive` (not paused, not during the
    ///    away catch-up, not at anchor) and there is no fight: no raid party
    ///    ashore, no enemy raiding or chasing, nothing hostile within
    ///    `CombatClearMetres`. Own `System.Random` stream seeded off the
    ///    world seed -- never `UnityEngine.Random`, never the world-build
    ///    stream. Ignored, they drift off after `SeaCastawayLifeSeconds`
    ///    (240 s): no death, no penalty, and nothing is saved while they are
    ///    in the water.
    /// 2. **Pull aboard.** Within `SeaCastawayPullMetres` (12 m) of the
    ///    hull's side and slower than `SeaCastawayPullMaxSpeed` (3 m/s) a big
    ///    bottom-centre button, the same slot and IMGUI shape as
    ///    `CastawayHud`'s "Take X aboard". Tap: a line goes out, they are
    ///    drawn in for up to `PullSeconds`, then they board through
    ///    `BornVillager.Board` -- the path every camp-born hand takes -- so
    ///    the save carries them as a crew name with no new field. Never
    ///    refused for berths: nobody leaves a person in the sea for want of
    ///    a hammock, and they leave again at the next camp.
    /// 3. **Join a camp.** While anchored at any island with a camp, every
    ///    hand aboard whose life log says `PulledFromSea` and not yet
    ///    `WentAshore` goes ashore through `Outpost.LandSurplusFromRefit`
    ///    (`Outpost.Station` without the ship's-biscuit rations -- a
    ///    castaway brings nothing), so every arrival rule (the ledger row,
    ///    walking up from the landing, beds/mood through the ledger, the
    ///    life log) is the ordinary one.
    ///
    /// Self-installing, same shape as `SeaLifeDirector`: no scene wiring.
    public class CastawaySpawner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<CastawaySpawner>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("CastawaySpawner");
            go.AddComponent<CastawaySpawner>();
            DontDestroyOnLoad(go);
        }

        /// Anything hostile this close to the ship counts as a fight.
        const float CombatClearMetres = 300f;
        /// Keep spawns this far off a reef's edge.
        const float ReefClearMetres = 25f;
        /// A failed spot search tries again this soon, not a whole wait later.
        const float RetrySeconds = 5f;
        /// Longest the pull-in takes once tapped, and how close to the side
        /// counts as alongside.
        const float PullSeconds = 2.5f;
        const float PullDoneMetres = 2.5f;
        /// Salt for this stream, so it never shares a sequence with anything
        /// else seeded off the world seed.
        const int StreamSalt = 0x0CA57A3;

        ShipMotor motor;
        AnchorController anchor;
        float nextLookup;
        float nextLandingCheck;

        System.Random rng;
        float seaSeconds;
        float waitSeconds = -1f;

        Castaway current;
        float pullElapsed;
        string rescuer = "";

        // --- read by OnGUI, written in Update ---------------------------------
        bool inReach;
        bool tooFast;

        void OnEnable() => Castaway.Hauled += OnHauled;
        void OnDisable() => Castaway.Hauled -= OnHauled;

        void Update()
        {
            if (Time.time >= nextLookup)
            {
                if (motor == null) motor = FindAnyObjectByType<ShipMotor>();
                if (anchor == null) anchor = FindAnyObjectByType<AnchorController>();
                nextLookup = Time.time + 1f;
            }
            inReach = false;
            if (motor == null || Time.timeScale <= 0f) return;

            if (Time.time >= nextLandingCheck)
            {
                nextLandingCheck = Time.time + 1f;
                TickLanding();
            }

            if (current != null && current.Resolved) current = null;
            if (current != null) { TickCurrent(Time.deltaTime); return; }
            TickSpawn(Time.deltaTime);
        }

        // ------------------------------------------------------------ spawn --

        void TickSpawn(float dt)
        {
            if (!Sailing.IsLive(anchor)) return;
            if (InCombat(motor.transform.position)) return;
            EnsureRng();
            if (waitSeconds < 0f) waitSeconds = NextWait();

            seaSeconds += dt;
            if (seaSeconds < waitSeconds) return;

            if (TrySpawn(motor.transform, out _)) { seaSeconds = 0f; waitSeconds = NextWait(); }
            else seaSeconds = Mathf.Max(0f, waitSeconds - RetrySeconds);
        }

        void EnsureRng()
        {
            if (rng != null) return;
            int seed = SeaSick.Save.SaveGame.WorldSeed();
            rng = new System.Random(unchecked(seed * 486187739 ^ StreamSalt));
        }

        float NextWait()
        {
            float j = Mathf.Clamp01(RecruitTuning.SeaCastawayJitter);
            float f = 1f + j * (float)(rng.NextDouble() * 2.0 - 1.0);
            return Mathf.Max(30f, RecruitTuning.SeaCastawayEverySeconds * f);
        }

        bool TrySpawn(Transform hull, out string why)
        {
            why = "";
            if (!FindSpot(hull, out var spot)) { why = "no clear deep water ahead"; return false; }
            string name = PickName();
            current = Castaway.Spawn(name, hull, spot, rng);
            if (current == null) { why = "could not build the castaway"; return false; }
            pullElapsed = 0f;
            rescuer = "";
            Banner.Show("Someone is waving from the water ahead!");
            return true;
        }

        bool FindSpot(Transform hull, out Vector3 spot)
        {
            Vector3 origin = hull.position;
            Vector3 fwd = hull.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float cone = RecruitTuning.SeaCastawayConeDeg;
            float min = RecruitTuning.SeaCastawayAheadMin;
            float max = Mathf.Max(min, RecruitTuning.SeaCastawayAheadMax);
            for (int i = 0; i < 8; i++)
            {
                float ang = (float)(rng.NextDouble() * 2.0 - 1.0) * cone;
                float dist = Mathf.Lerp(min, max, (float)rng.NextDouble());
                Vector3 p = origin + Quaternion.Euler(0f, ang, 0f) * fwd * dist;
                p.y = 0f;
                if (!SeaLifeSpawn.ClearOfLand(p)) continue;
                if (!DeepEnough(p)) continue;
                if (!ClearOfReefs(p)) continue;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        static bool DeepEnough(Vector3 p)
        {
            var h = Island.TerrainHeight;
            return h == null || -h(p.x, p.z) >= RecruitTuning.SeaCastawayMinDepth;
        }

        static bool ClearOfReefs(Vector3 p)
        {
            foreach (var r in Reef.All)
            {
                if (r == null) continue;
                Vector3 d = r.transform.position - p; d.y = 0f;
                if (d.magnitude < r.Radius + ReefClearMetres) return false;
            }
            return true;
        }

        static bool InCombat(Vector3 at)
        {
            if (RaidParty.Active != null) return true;
            float clearSq = CombatClearMetres * CombatClearMetres;
            foreach (var e in EnemyShip.All)
            {
                if (e == null || !e.Alive) continue;
                if (e.Raiding || e.Current == EnemyShip.Duty.Chase) return true;
                Vector3 d = e.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude < clearSq) return true;
            }
            foreach (var m in SeaMonster.All)
            {
                if (m == null || !m.Alive) continue;
                Vector3 d = m.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude < clearSq) return true;
            }
            return false;
        }

        // ------------------------------------------------------------- names --

        /// The same hat new hands are named from (`CrewNames.Free`, which
        /// `VillagerNames`/the yard also draw on), skipping every name alive
        /// anywhere, in the graveyard, waiting on a beach, or with a life log
        /// already -- so a stranger never inherits somebody else's story.
        string PickName()
        {
            var taken = CrewNames.InUse();
            foreach (var g in Lives.Graveyard) if (g != null && !string.IsNullOrEmpty(g.name)) taken.Add(g.name);
            foreach (var c in Lives.Castaways) if (c != null && !string.IsNullOrEmpty(c.name)) taken.Add(c.name);
            foreach (var kv in Lives.Records) taken.Add(kv.Key);
            EnsureRng();
            return CrewNames.Free(rng.Next(), taken);
        }

        static bool NameTaken(string name) =>
            string.IsNullOrEmpty(name) || CrewNames.InUse().Contains(name)
            || Lives.IsTaken(name) || Lives.Records.ContainsKey(name);

        // ----------------------------------------------------------- in reach --

        void TickCurrent(float dt)
        {
            var c = current;
            c.SetHull(motor.transform);
            Vector3 side = c.NearestHullSide();
            Vector3 a = side, b = c.WorldPosition; a.y = 0f; b.y = 0f;
            float gap = Vector3.Distance(a, b);

            if (c.Pulling)
            {
                c.UpdatePullAnchor(side);
                pullElapsed += dt;
                if (gap <= PullDoneMetres || pullElapsed >= PullSeconds) Commit(c, rescuer);
                return;
            }

            inReach = gap <= RecruitTuning.SeaCastawayPullMetres;
            tooFast = motor.CurrentSpeed > RecruitTuning.SeaCastawayPullMaxSpeed;
            c.InPullReach = inReach;
        }

        void StartPull()
        {
            var c = current;
            if (c == null || c.Resolved || c.Pulling) return;
            var hand = NearestFreeHand(c);
            rescuer = hand != null ? hand.DisplayName : "";
            pullElapsed = 0f;
            c.BeginPull(c.NearestHullSide());
        }

        CrewAgent NearestFreeHand(Castaway c)
        {
            var roster = RosterOf(motor != null ? motor.transform : null);
            if (roster == null) return null;
            Vector3 rail = c.NearestHullSide();
            CrewAgent best = null;
            float bestDist = float.MaxValue;
            foreach (var a in roster.All)
            {
                if (a == null || !a.gameObject.activeInHierarchy || !a.Available) continue;
                float d = Vector3.Distance(a.transform.position, rail);
                if (d < bestDist) { bestDist = d; best = a; }
            }
            return best;
        }

        void OnHauled(Castaway c, string by)
        {
            if (c != null && c == current && !c.Resolved) Commit(c, by);
        }

        /// **Aboard.** Life log first (so the story knows where and by whom),
        /// then the body through the camp-born boarding path.
        void Commit(Castaway c, string by)
        {
            if (c == null || c.Resolved || motor == null) return;
            string name = c.CastawayName;
            // Something may have taken the name while they floated (a camp
            // recruiting the same one): draw again rather than make two.
            if (NameTaken(name)) name = PickName();

            Island near = Island.Nearest(c.WorldPosition);
            string off = near == null ? "open water" : near.IsHome ? "the home island" : near.gameObject.name;

            var r = Lives.Record(name);
            if (r != null)
            {
                if (r.bornDay < 0) r.bornDay = TimeOfDay.Day;
                // `Lives.Log` fills an empty `homeCamp` from `camp`; the
                // island they were found off is not a camp, and a filled
                // home would make their first landing read as a ferry.
                string keepHome = r.homeCamp;
                Lives.Log(name, LifeEvents.PulledFromSea, off, by);
                r.homeCamp = keepHome;
            }
            if (!string.IsNullOrEmpty(by)) Lives.Log(by, LifeEvents.RescuedOther, other: name);

            var hull = motor.transform;
            var agent = BornVillager.Board(name, hull);
            if (agent != null)
            {
                // Soaked and shaken: off stations a while, not sick.
                agent.ApplyRescueAftermath(0f, OverboardTuning.RescueOffStationSeconds);
                RosterOf(hull)?.Refresh();
                Banner.Show(name + " is aboard, and will join your next camp.");
            }
            else Debug.LogWarning("CastawaySpawner: nobody aboard to copy a body from for " + name);

            c.Resolve();
            current = null;
            rescuer = "";
            pullElapsed = 0f;
        }

        static CrewRoster RosterOf(Transform hull) =>
            hull == null ? null
                : (hull.GetComponentInParent<CrewRoster>() ?? hull.GetComponentInChildren<CrewRoster>(true));

        // ------------------------------------------------------------ landing --

        /// Anchored (or ashore) at an island with a camp: every castaway
        /// passenger aboard goes ashore and joins it.
        void TickLanding()
        {
            if (anchor == null || SeaSick.Save.AwayProgress.Running) return;
            if (anchor.CurrentState != AnchorController.State.Anchored
                && anchor.CurrentState != AnchorController.State.Ashore) return;
            var isle = anchor.CurrentIsland;
            var camp = isle != null ? Outpost.Of(isle) : null;
            if (camp == null || !camp.HasCamp) return;
            var roster = RosterOf(motor.transform);
            if (roster == null) return;

            int landed = 0;
            string last = "";
            foreach (var a in roster.All)
            {
                if (a == null || !a.gameObject.activeInHierarchy || !a.IsAboard) continue;
                string who = a.DisplayName;
                if (!AwaitingLanding(who)) continue;
                // `Station` minus the rations: see the class doc.
                if (camp.LandSurplusFromRefit(a)) { landed++; last = who; }
            }
            if (landed == 0) return;
            roster.Refresh();
            Banner.Show(landed == 1
                ? last + " goes ashore to join the camp."
                : landed + " people from the sea go ashore to join the camp.");
        }

        /// Pulled from the sea, and has not set foot in a camp since.
        static bool AwaitingLanding(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!Lives.Records.TryGetValue(name, out var r) || r == null || r.events == null) return false;
            bool pulled = false;
            foreach (var e in r.events)
            {
                if (e == null) continue;
                if (e.kind == LifeEvents.WentAshore) return false;
                if (e.kind == LifeEvents.PulledFromSea) pulled = true;
            }
            return pulled;
        }

        // ---------------------------------------------------------------- HUD --

        void OnGUI()
        {
            var c = current;
            if (c == null || c.Resolved) return;
            if (!c.Pulling && !inReach) return;
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;

            int u = HudLayout.Unit;
            float btnH = Mathf.Max(u * 3.4f, 64f);
            float btnW = Mathf.Min(HudLayout.Safe.width - HudLayout.Pad * 2f, u * 26f);
            var safe = HudLayout.Safe;
            var rect = new Rect(safe.x + (safe.width - btnW) * 0.5f,
                HudLayout.BottomClustersTop - HudLayout.Gap - btnH, btnW, btnH);

            if (c.Pulling)
            {
                UIBlocker.Block(rect);
                if (Event.current.type != EventType.Repaint) return;
                float t = Mathf.Clamp01(pullElapsed / PullSeconds);
                UITheme.Rect(rect, new Color(0f, 0f, 0f, 0.55f));
                UITheme.Rect(new Rect(rect.x, rect.y, rect.width * t, rect.height), new Color(0.3f, 0.75f, 0.4f, 0.85f));
                GUI.Label(rect, "Pulling " + c.CastawayName + " aboard", UITheme.Toast);
                return;
            }

            if (tooFast)
            {
                if (Event.current.type != EventType.Repaint) return;
                GUI.Label(rect, "Slow down to pull " + c.CastawayName + " aboard", UITheme.Toast);
                return;
            }

            // Only a real button takes the touch from the helm; the "slow
            // down" hint above leaves the stick free (same as `RescueHud`).
            UIBlocker.Block(rect);
            if (GUI.Button(rect, "Pull " + c.CastawayName + " aboard", UITheme.Button)) StartPull();
        }

        // ---------------------------------------------------------- dev hook --

        /// Dev/eval hook: a castaway `ahead` metres off the bow right now,
        /// ignoring the clock, land and combat checks (replaces any already
        /// in the water). Returns what happened.
        public static string DebugSpawn(float ahead = 45f)
        {
            var s = FindAnyObjectByType<CastawaySpawner>();
            var m = FindAnyObjectByType<ShipMotor>();
            if (s == null || m == null) return "no spawner or no ship";
            if (s.current != null && !s.current.Resolved) s.current.Resolve();
            s.motor = m;
            s.EnsureRng();
            Vector3 fwd = m.transform.forward; fwd.y = 0f;
            Vector3 spot = m.transform.position + fwd.normalized * ahead;
            s.current = Castaway.Spawn(s.PickName(), m.transform, spot, s.rng);
            s.pullElapsed = 0f;
            s.rescuer = "";
            return s.current != null ? "castaway " + s.current.CastawayName + " at " + spot : "spawn failed";
        }
    }
}
