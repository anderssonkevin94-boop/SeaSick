using System.Collections.Generic;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.UI;
using SeaSick.World.Life;
using SeaSick.Ship.SeaLife;

namespace SeaSick.Ship.Overboard
{
    /// **The rescue side of man overboard** (phase 5b, docs/PLAN-DEATH-RESCUE.md
    /// "Man overboard", build brief items 1-5). IMGUI stand-in — same spirit
    /// as `Banner`/`Swimmer`'s own visuals: Astra owns `Scripts/UI`, this is
    /// throwaway `Scripts/Dev`-style drawing that lives here instead because
    /// it is gameplay, not a menu (it drives `HelmInput.SteerToward` and
    /// decides who gets sent to haul).
    ///
    /// Three jobs:
    /// 1. **The edge arrow.** For every swimmer off screen (or behind the
    ///    camera), an arrow clamped to the screen edge (preferring the top
    ///    and sides over the bottom, where the helm stick lives), carrying a
    ///    small arc for `TimeLeft01` and the distance in metres.
    /// 2. **Tap to steer.** A tap within `TapRadiusUnits` of a swimmer's
    ///    projected screen position — on screen, or on the edge arrow —
    ///    calls `HelmInput.SteerToward`. **This tap has to be consumed
    ///    BEFORE the sea stick starts on it**: `SeaStick.Feed` only claims a
    ///    touch that BEGINS somewhere `UIBlocker.Blocked` says is free, so
    ///    every hit zone here calls `UIBlocker.Block` on its own rect every
    ///    `OnGUI` event, which is enough on its own; the `GUI.Button`
    ///    underneath is what actually reads the tap.
    /// 3. **Sail over them** (2026-09-28, replaced the Throw line button):
    ///    within `BoardReachMetres` of the hull's side for `BoardSeconds`
    ///    and they climb aboard, a filling bar bottom-centre meanwhile.
    public class RescueHud : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<RescueHud>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("RescueHud");
            go.AddComponent<RescueHud>();
            DontDestroyOnLoad(go);
        }

        /// Tap-catcher half-size, in `HudLayout.Unit`s — the build brief's
        /// "~60pt", and `HudLayout.Unit` is this project's stand-in for a
        /// DPI-sane point (see `SeaStick`/`HelmInput`, which size every
        /// control off it with no separate DPI scale).
        const float TapRadiusUnits = 3.0f;
        const float EdgeMarginUnits = 2.6f;
        const int ArcSegments = 20;

        HelmInput helm;
        float nextHelmLookup;

        void Update()
        {
            // Looked up lazily and re-checked every second rather than every
            // frame `FindAnyObjectByType` — cheap either way with one ship,
            // but there is no reason to pay it sixty times a second for
            // something that only changes on a scene load.
            if (Time.time >= nextHelmLookup)
            {
                if (helm == null) helm = FindAnyObjectByType<HelmInput>();
                nextHelmLookup = Time.time + 1f;
            }
            TickBoarding(Time.deltaTime);
            OfferCard();
        }

        // ------------------------------------------------- sail-over pickup
        //
        // Kevin, 2026-09-28: *"i cant pick a guy up whose landed in the
        // water. it should be enough that i sail over him and a quick 2
        // second timer goes off and he climbs aboard."* No button, no speed
        // gate: keep a target within `BoardReachMetres` of the hull's side
        // (or under her) for `BoardSeconds` and it comes aboard. Drift out
        // of reach and the timer starts over. The scramble net shortens it,
        // the lifebuoy rack widens the reach.

        static readonly Dictionary<IOverboardTarget, float> boarding = new Dictionary<IOverboardTarget, float>();
        static readonly List<IOverboardTarget> boardScratch = new List<IOverboardTarget>();

        static float BoardReach => OverboardTuning.BoardReachMetres + OverboardModules.ThrowReachBonusMetres() * 0.5f;
        static float BoardTime => OverboardTuning.BoardSeconds * OverboardModules.HaulTimeMultiplier();

        static float SideGap(IOverboardTarget t)
        {
            if (t.Hull == null) return float.MaxValue;   // no ship to climb onto
            Vector3 a = t.NearestHullSide(), b = t.WorldPosition;
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void TickBoarding(float dt)
        {
            if (Time.timeScale <= 0f) return;
            CollectTargets();
            boardScratch.Clear();
            foreach (var kv in boarding) boardScratch.Add(kv.Key);
            foreach (var k in boardScratch)
                if (k == null || k.Resolved || !targets.Contains(k)) boarding.Remove(k);

            foreach (var t in targets)
            {
                if (t == null || t.Resolved || t.BeingHauled || !t.Boardable) continue;
                if (SideGap(t) > BoardReach) { boarding.Remove(t); continue; }
                boarding.TryGetValue(t, out float have);
                have += dt;
                if (have < BoardTime) { boarding[t] = have; continue; }
                boarding.Remove(t);
                var helper = FindNearestAvailableCrew(t);
                t.OnHauled(helper != null ? helper.DisplayName : "");
            }
        }

        /// Every overboard target right now, swimmers and floating cargo
        /// alike — scratch, rebuilt each `OnGUI` (cheap: at most a handful
        /// at once, same budget note as `Swimmer.Update`'s ocean sample).
        static readonly List<IOverboardTarget> targets = new List<IOverboardTarget>();

        static void CollectTargets()
        {
            targets.Clear();
            foreach (var s in Swimmer.All) if (s != null) targets.Add(s);
            foreach (var c in FloatingCargo.All) if (c != null) targets.Add(c);
            // "Things to find at sea" (2026-09-28): flotsam, a bottle and a
            // fish shoal all ride the same ring/arrow/tap-to-steer this HUD
            // already draws for a swimmer or lost cargo -- only the shoal
            // is excluded from the boarding timer below (`Boardable`).
            foreach (var f in FlotsamCrate.All) if (f != null) targets.Add(f);
            foreach (var b in MessageBottle.All) if (b != null) targets.Add(b);
            foreach (var sh in FishShoal.All) if (sh != null) targets.Add(sh);
            // Castaways on a board (Kevin, 2026-09-30): arrow + tap-to-steer
            // only (`Boardable` is false; `Voyage.CastawaySpawner` owns the
            // Pull aboard button), and not once that button is up.
            foreach (var cw in Castaway.All)
                if (cw != null && !cw.Pulling && !cw.InPullReach) targets.Add(cw);
        }

        void OnGUI()
        {
            // Sea markers belong to the sea view: ashore (the land HUD up)
            // they were drawn through the island at their bearing and could
            // not be pressed (Kevin, 2026-09-28, "unwanted artifact on screen").
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            CollectTargets();
            if (targets.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (helm == null) helm = FindAnyObjectByType<HelmInput>();

            int u = HudLayout.Unit;
            float tapHalf = u * TapRadiusUnits;

            foreach (var t in targets)
                if (t != null && !t.Resolved) DrawTarget(t, cam, u, tapHalf);

            // "steering to X" and "X is climbing aboard" are the sea action
            // card's now (`OfferCard`, from Update).
        }

        // ------------------------------------------------- per-target -----

        void DrawTarget(IOverboardTarget t, Camera cam, int u, float tapHalf)
        {
            Vector3 sp = cam.WorldToScreenPoint(t.WorldPosition);
            bool behind = sp.z < 0f;
            // Behind the camera, WorldToScreenPoint mirrors both axes through
            // the centre — flip back so the clamp below points the right way
            // (the standard off-screen-indicator trick).
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
            Vector2 gui = new Vector2(sp.x, Screen.height - sp.y);

            float margin = u * EdgeMarginUnits;
            bool offscreen = behind || gui.x < margin || gui.x > Screen.width - margin
                || gui.y < margin || gui.y > Screen.height - margin;

            if (!offscreen)
            {
                // In the harpoon's bow arc (or on its line) the harpoon
                // has it: no steer zone, so a thumb on its indicator marker
                // (which takes no tap) never turns the helm (2026-10-04).
                if (!HarpoonOwns(t)) DrawTapZone(gui, tapHalf, t);
                return;
            }

            // An edge arrow for something on top of the ship points at the
            // ship itself, and with a sheet open the IMGUI layer drew over
            // it (2026-09-30 phase 6 check): no arrow and no hidden tap
            // zone for either.
            if (SeaSick.UI.Sheets.SheetHost.FrameOpen || SeaSick.UI.Sheets.Sheets.Current != null) return;
            if (helm != null && NearShip(helm.transform.position, t.WorldPosition)) return;

            // Clamp to a band that prefers the TOP and SIDES over the
            // bottom, where the helm stick lives (build brief item 2) — the
            // clamp rect's own bottom edge stops at the screen's vertical
            // middle, so even a target dead astern (whose raw direction
            // points straight down) lands on that line instead of low over
            // the stick.
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = gui - centre;
            if (dir.sqrMagnitude < 1f) dir = Vector2.up;
            dir.Normalize();

            var bounds = new Rect(margin, margin,
                Screen.width - margin * 2f, Screen.height * 0.5f - margin);
            Vector2 edgePoint = ClampToRectEdge(centre, dir, bounds);
            if (MarkerBlocked(edgePoint, u)) return;

            float dist = Vector3.Distance(cam.transform.position, t.WorldPosition);
            DrawArrow(edgePoint, dir, t.TimeLeft01, dist, u);
            DrawTapZone(edgePoint, tapHalf, t);
        }

        /// **Markers stand down near the ship (2026-09-30).** A target within
        /// this many metres (flat) of the player ship is on the ship itself,
        /// so its edge arrow would point at the hull and read as clutter.
        public const float NearShipMetres = 25f;

        public static bool NearShip(Vector3 ship, Vector3 target)
        {
            float dx = ship.x - target.x, dz = ship.z - target.z;
            return dx * dx + dz * dz < NearShipMetres * NearShipMetres;
        }

        /// True when an IMGUI sea marker drawn at `gui` (GUI space) would
        /// land on the sea HUD (top bar, alerts, helm, action card) or on an
        /// open sheet. `SquallHud` shares it.
        public static bool MarkerBlocked(Vector2 gui, int u)
        {
            if (SeaSick.UI.Sheets.SheetHost.FrameOpen || SeaSick.UI.Sheets.Sheets.Current != null) return true;
            // The arrowhead plus its "NNm" label span roughly 3 units each way.
            float r = u * 3f;
            var box = new Rect(gui.x - r, gui.y - r, r * 2f, r * 2.6f);
            return SeaSick.UI.Sheets.SeaHud.Overlaps(box);
        }

        /// Where a ray from `origin` toward `dir` first leaves `r`.
        static Vector2 ClampToRectEdge(Vector2 origin, Vector2 dir, Rect r)
        {
            float t = float.MaxValue;
            if (dir.x > 1e-4f) t = Mathf.Min(t, (r.xMax - origin.x) / dir.x);
            else if (dir.x < -1e-4f) t = Mathf.Min(t, (r.xMin - origin.x) / dir.x);
            if (dir.y > 1e-4f) t = Mathf.Min(t, (r.yMax - origin.y) / dir.y);
            else if (dir.y < -1e-4f) t = Mathf.Min(t, (r.yMin - origin.y) / dir.y);
            if (t <= 0f || t == float.MaxValue) t = 100f;
            return origin + dir * t;
        }

        /// True when `t` is the harpoon's: in its bow arc, or the load on its
        /// line. Outside the arc the steer tap stays, to bring it in.
        static bool HarpoonOwns(IOverboardTarget t)
        {
            var gun = SeaSick.Ship.Harpoon.HarpoonGun.Player;
            if (gun == null || !gun.Available || !(t is SeaSick.Ship.Harpoon.IHarpoonable h)) return false;
            if (gun.LineOut && ReferenceEquals(gun.Target, h)) return true;
            var arc = gun.InArc;
            for (int i = 0; i < arc.Count; i++)
                if (ReferenceEquals(arc[i], h)) return true;
            return false;
        }

        /// The invisible tap-catcher, on screen or at the edge arrow — the
        /// thing that turns a tap into `HelmInput.SteerToward`. Blocks
        /// `SeaStick` off this rect FIRST (see the class doc), then reads
        /// the tap with an ordinary (styleless, so invisible) `GUI.Button`.
        ///
        /// **Never on the sea HUD's round buttons** (2026-10-04): a
        /// `GUI.Button` reads its tap whatever UI Toolkit drew over it, so a
        /// zone over the harpoon button (or the bolt) turned a fire tap into
        /// a steer as well. A target that close to a button gets no zone.
        void DrawTapZone(Vector2 guiPoint, float half, IOverboardTarget t)
        {
            if (!TapZoneAllowed(guiPoint)) return;
            var rect = ZoneRect(guiPoint, half);
            UIBlocker.Block(rect);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && helm != null)
                helm.SteerToward(t.Transform, t.Label);
        }

        static Rect ZoneRect(Vector2 guiPoint, float half) =>
            new Rect(guiPoint.x - half, guiPoint.y - half, half * 2f, half * 2f);

        /// May a steer tap zone be centred on this GUI-space point? Not where
        /// its square would touch the harpoon button or the bolt. Public for
        /// `HarpoonTapCheck`.
        public static bool TapZoneAllowed(Vector2 guiPoint)
        {
            var zone = ZoneRect(guiPoint, HudLayout.Unit * TapRadiusUnits);
            if (SeaSick.UI.Sheets.SeaHud.ButtonsOverlap(zone)) return false;
            // Nor on a panel the layout reserved (the bottom stack's Wheel
            // slot: helm strip, bolt, harpoon row): a target floating low on
            // the screen sat its invisible steer button 168 x 37 px into it.
            var issued = HudLayout.Issued;
            for (int i = 0; i < issued.Count; i++)
                if (issued[i].Overlaps(zone)) return false;
            return true;
        }

        void DrawArrow(Vector2 at, Vector2 dir, float timeLeft01, float distanceMetres, int u)
        {
            if (Event.current.type != EventType.Repaint) return;

            float ang = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;
            float size = u * 1.1f;
            var col = timeLeft01 < 0.35f ? new Color(0.95f, 0.2f, 0.15f)
                : timeLeft01 < 0.65f ? new Color(1f, 0.7f, 0.15f) : Color.white;

            // Small arc behind the arrowhead, filled to `timeLeft01` — same
            // draining-ring read as `Swimmer`'s own world-space ring, in
            // screen space for the off-screen case.
            DrawArc(at, size * 1.6f, 3f, timeLeft01, col);

            // The arrowhead itself: a triangle rotated to point at `dir`,
            // drawn as three thin rects (IMGUI's only primitive is a rect).
            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, at);
            var head = new Rect(at.x - size * 0.5f, at.y - size * 0.9f, size, size * 1.2f);
            UITheme.Rect(head, col);
            GUI.matrix = prev;

            string label = Mathf.RoundToInt(distanceMetres) + "m";
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(u * 0.85f),
                alignment = TextAnchor.MiddleCenter,
            };
            style.normal.textColor = col;
            GUI.Label(new Rect(at.x - u * 2f, at.y + size, u * 4f, u * 1.3f), label, style);
        }

        static void DrawArc(Vector2 c, float r, float thick, float t01, Color col)
        {
            int filled = Mathf.Clamp(Mathf.CeilToInt(ArcSegments * Mathf.Clamp01(t01)), 0, ArcSegments);
            var prev = GUI.matrix;
            float segLen = 2f * Mathf.PI * r / ArcSegments * 1.25f;
            for (int i = 0; i < ArcSegments; i++)
            {
                GUI.matrix = prev;
                GUIUtility.RotateAroundPivot(i * (360f / ArcSegments), c);
                var segCol = i < filled ? col : new Color(1f, 1f, 1f, 0.25f);
                UITheme.Rect(new Rect(c.x - segLen * 0.5f, c.y - r - thick * 0.5f, segLen, thick), segCol);
            }
            GUI.matrix = prev;
        }

        // ------------------------------------------------- steer readout ----

        // --- the sea action card (Kevin, 2026-09-30, island UI phase 6) -----
        //
        // The IMGUI "steering to X" box (Screen.height * 0.19) and the
        // green-filled "X is climbing aboard" / "Hauling in X" bars at
        // `BottomClustersTop` are offers to the sea HUD's one action card
        // (`UI.Sheets.SeaActions`): a boarding timer is information with its
        // fill as the card's bar (it needs no tap: keep her alongside), and a
        // steer is the card with ONE tap, "stop steering" (the throttle stays
        // the player's, as ever). The markers on the water stay IMGUI: they
        // are pinned to the swimmer, not HUD.

        readonly SeaSick.UI.Sheets.SeaActions.Joined steerTitle = new SeaSick.UI.Sheets.SeaActions.Joined();
        readonly SeaSick.UI.Sheets.SeaActions.Joined climbTitle = new SeaSick.UI.Sheets.SeaActions.Joined();
        System.Action cancelSteer;

        void OfferCard()
        {
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            const int P = SeaSick.UI.Sheets.SeaActions.PriorityRescue;

            // The fullest boarding timer: someone is coming aboard.
            IOverboardTarget best = null;
            float bestF = -1f;
            foreach (var kv in boarding)
            {
                if (kv.Key == null || kv.Key.Resolved) continue;
                float f = Mathf.Clamp01(kv.Value / Mathf.Max(0.01f, BoardTime));
                if (f > bestF) { bestF = f; best = kv.Key; }
            }
            if (best != null)
            {
                bool person = best.RescuePriority == 0;
                SeaSick.UI.Sheets.SeaActions.Offer(P + 10, person ? "MAN OVERBOARD" : "IN THE WATER",
                    person ? climbTitle.Of(best.Label, " is climbing aboard") : climbTitle.Of("Hauling in ", best.Label),
                    "Keep her alongside", null, false, bestF);
                return;
            }

            if (helm != null && helm.SteeringToward)
            {
                if (cancelSteer == null) cancelSteer = CancelSteer;
                SeaSick.UI.Sheets.SeaActions.Offer(P, "STEERING TO",
                    steerTitle.Of("Steering to ", helm.SteerTargetName),
                    "Your thumb keeps the throttle · tap to stop steering", cancelSteer);
            }
        }

        void CancelSteer() { if (helm != null) helm.CancelSteerToward(); }

        /// The nearest hand that is `Available` (station, not resting, not
        /// already at the rail/hauling/ashore) to the target's own side of
        /// the hull -- named as the one who helped it aboard.
        static CrewAgent FindNearestAvailableCrew(IOverboardTarget target)
        {
            if (target.Hull == null) return null;
            var roster = target.Hull.GetComponentInParent<CrewRoster>();
            if (roster == null) return null;

            Vector3 rail = target.NearestHullSide();
            CrewAgent best = null;
            float bestDist = float.MaxValue;
            foreach (var c in roster.All)
            {
                if (c == null || !c.gameObject.activeInHierarchy || !c.Available) continue;
                float d = Vector3.Distance(c.transform.position, rail);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }
    }
}
