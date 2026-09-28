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
    ///    BEFORE `TouchHelm` reads it as a stop-tap or starts a stick**:
    ///    `TouchHelm.Feed` only claims a touch that BEGINS somewhere
    ///    `UIBlocker.Blocked` says is free (`TouchHelm.cs:220`), so every hit
    ///    zone here calls `UIBlocker.Block` on its own rect every `OnGUI`
    ///    event — the same thing the oars/ease buttons already do
    ///    (`HelmInput.cs`) — which is enough on its own; the `GUI.Button`
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
        /// DPI-sane point (see `TouchHelm`/`HelmInput`, which size every
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
        }

        void OnGUI()
        {
            CollectTargets();
            if (targets.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (helm == null) helm = FindAnyObjectByType<HelmInput>();

            int u = HudLayout.Unit;
            float tapHalf = u * TapRadiusUnits;

            // Every target within throw reach, PEOPLE FIRST then cargo
            // (build brief item 1), nearest within each group — so two
            // targets in reach at once (item 4) each get their own row
            // below rather than only the single nearest winning a button.
            inReach.Clear();
            foreach (var t in targets)
            {
                if (t == null || t.Resolved) continue;
                DrawTarget(t, cam, u, tapHalf);

                float rd = Vector3.Distance(t.NearestHullSide(), t.WorldPosition);
                if (rd <= OverboardTuning.ThrowReachMetres + OverboardModules.ThrowReachBonusMetres()) inReach.Add(t);
            }
            inReach.Sort((a, b) =>
            {
                int p = a.RescuePriority.CompareTo(b.RescuePriority);
                return p != 0 ? p : Vector3.Distance(a.NearestHullSide(), a.WorldPosition)
                    .CompareTo(Vector3.Distance(b.NearestHullSide(), b.WorldPosition));
            });

            DrawSteeringLine(u);
            DrawBoarding(u);
        }

        static readonly List<IOverboardTarget> inReach = new List<IOverboardTarget>();

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
                DrawTapZone(gui, tapHalf, t);
                return;
            }

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

            float dist = Vector3.Distance(cam.transform.position, t.WorldPosition);
            DrawArrow(edgePoint, dir, t.TimeLeft01, dist, u);
            DrawTapZone(edgePoint, tapHalf, t);
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

        /// The invisible tap-catcher, on screen or at the edge arrow — the
        /// thing that turns a tap into `HelmInput.SteerToward`. Blocks
        /// `TouchHelm` off this rect FIRST (see the class doc), then reads
        /// the tap with an ordinary (styleless, so invisible) `GUI.Button`.
        void DrawTapZone(Vector2 guiPoint, float half, IOverboardTarget t)
        {
            var rect = new Rect(guiPoint.x - half, guiPoint.y - half, half * 2f, half * 2f);
            UIBlocker.Block(rect);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && helm != null)
                helm.SteerToward(t.Transform, t.Label);
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

        void DrawSteeringLine(int u)
        {
            if (helm == null || !helm.SteeringToward) return;
            string label = "steering to " + helm.SteerTargetName;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(u * 0.95f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            style.normal.textColor = Color.white;
            float w = Mathf.Min(Screen.width - u * 2f, u * 20f);
            var r = new Rect((Screen.width - w) * 0.5f, u * 1.6f, w, u * 1.7f);
            if (Event.current.type == EventType.Repaint) GUI.Box(r, "");
            GUI.Label(r, label, style);
        }

        // ------------------------------------------------- throw line -------

        /// Hands already spoken for by a button drawn earlier this frame —
        /// so two targets in reach at once (build brief item 4) each get
        /// offered a DIFFERENT free hand rather than both buttons pointing
        /// at the same nearest one.
        static readonly HashSet<CrewAgent> claimedThisFrame = new HashSet<CrewAgent>();

        /// One stacked row per target in reach (`inReach`, already ordered
        /// people-first-then-cargo, nearest first within each) — growing
        /// UPWARD from the same bottom-centre anchor the single button used
        /// to sit at, so the first (highest-priority) row lands exactly
        /// where "Throw line" always has.
        /// "Anna is climbing aboard" with a filling bar, bottom-centre where
        /// the Throw line button used to be. Not a button: nothing to press.
        void DrawBoarding(int u)
        {
            if (boarding.Count == 0) return;
            float h = Mathf.Max(u * 2.4f, 48f);
            float w = Mathf.Min(HudLayout.Safe.width - HudLayout.Pad * 2f, u * 26f);
            var safe = HudLayout.Safe;
            float y = HudLayout.BottomClustersTop - HudLayout.Gap - h;
            foreach (var kv in boarding)
            {
                if (kv.Key == null || kv.Key.Resolved) continue;
                var rect = new Rect(safe.x + (safe.width - w) * 0.5f, y, w, h);
                float f = Mathf.Clamp01(kv.Value / Mathf.Max(0.01f, BoardTime));
                UITheme.Rect(rect, new Color(0f, 0f, 0f, 0.55f));
                UITheme.Rect(new Rect(rect.x, rect.y, rect.width * f, rect.height), new Color(0.3f, 0.75f, 0.4f, 0.85f));
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(u * 0.95f),
                    alignment = TextAnchor.MiddleCenter,
                };
                style.normal.textColor = Color.white;
                GUI.Label(rect, kv.Key.RescuePriority == 0
                    ? kv.Key.Label + " is climbing aboard"
                    : "Hauling in " + kv.Key.Label, style);
                y -= h + HudLayout.Gap;
            }
        }

        /// **Unused since 2026-09-28** (sail-over pickup replaced the
        /// button); kept for the jolly boat's sake of the shared haul code.
        void DrawThrowLines(int u)
        {
            if (inReach.Count == 0 || helm == null) return;
            var motor = helm.GetComponent<ShipMotor>();
            float speed = motor != null ? motor.CurrentSpeed : 0f;

            float btnH = Mathf.Max(u * 3.4f, 64f);
            float btnW = Mathf.Min(HudLayout.Safe.width - HudLayout.Pad * 2f, u * 26f);
            var safe = HudLayout.Safe;
            float top = HudLayout.BottomClustersTop;

            claimedThisFrame.Clear();
            for (int i = 0; i < inReach.Count; i++)
            {
                var rect = new Rect(safe.x + (safe.width - btnW) * 0.5f,
                    top - HudLayout.Gap - btnH * (i + 1) - HudLayout.Gap * i, btnW, btnH);
                DrawThrowLine(inReach[i], rect, speed);
            }
        }

        void DrawThrowLine(IOverboardTarget target, Rect rect, float speed)
        {
            if (speed > OverboardTuning.ThrowMaxSpeed)
            {
                // No button while she's making too much way — just the hint,
                // sized the same as the button would be so it doesn't jump
                // the moment she slows into reach.
                int u = HudLayout.Unit;
                var hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(u * 0.9f),
                    alignment = TextAnchor.MiddleCenter,
                };
                hintStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
                GUI.Label(rect, "slow down to throw a line", hintStyle);
                return;
            }

            UIBlocker.Block(rect);
            var agent = FindNearestAvailableCrew(target);
            if (agent != null)
            {
                claimedThisFrame.Add(agent);
                if (GUI.Button(rect, "Throw line to " + target.Label)) agent.StartHaul(target);
            }
            else
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(rect, "No free hands");
                GUI.enabled = wasEnabled;
            }
        }

        /// The nearest hand that is `Available` (station, not resting, not
        /// already at the rail/hauling/ashore) and not already claimed by an
        /// earlier row this frame, to the target's own side of the hull.
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
                if (claimedThisFrame.Contains(c)) continue;
                float d = Vector3.Distance(c.transform.position, rail);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }
    }
}
