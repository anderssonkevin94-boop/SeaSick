using UnityEngine;
using SeaSick.Crew;
using SeaSick.UI;
using SeaSick.World.Life;

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
    /// 3. **Throw line.** In reach of the hull's SIDE (not its centre), slow
    ///    enough: a big bottom-centre button that sends the nearest
    ///    `Available` hand to haul. Too fast: no button, a small hint
    ///    instead. Nobody free: a disabled button that says so.
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
        }

        void OnGUI()
        {
            if (Swimmer.All.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (helm == null) helm = FindAnyObjectByType<HelmInput>();

            int u = HudLayout.Unit;
            float tapHalf = u * TapRadiusUnits;

            Swimmer nearestInReach = null;
            float nearestReachDist = float.MaxValue;

            foreach (var s in Swimmer.All)
            {
                if (s == null) continue;
                DrawSwimmer(s, cam, u, tapHalf);

                float rd = Vector3.Distance(s.NearestHullSide(), s.WorldPosition);
                if (rd <= OverboardTuning.ThrowReachMetres && rd < nearestReachDist)
                {
                    nearestReachDist = rd;
                    nearestInReach = s;
                }
            }

            DrawSteeringLine(u);
            DrawThrowLine(nearestInReach, u);
        }

        // ------------------------------------------------- per-swimmer -----

        void DrawSwimmer(Swimmer s, Camera cam, int u, float tapHalf)
        {
            Vector3 sp = cam.WorldToScreenPoint(s.WorldPosition);
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
                DrawTapZone(gui, tapHalf, s);
                return;
            }

            // Clamp to a band that prefers the TOP and SIDES over the
            // bottom, where the helm stick lives (build brief item 2) — the
            // clamp rect's own bottom edge stops at the screen's vertical
            // middle, so even a swimmer dead astern (whose raw direction
            // points straight down) lands on that line instead of low over
            // the stick.
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = gui - centre;
            if (dir.sqrMagnitude < 1f) dir = Vector2.up;
            dir.Normalize();

            var bounds = new Rect(margin, margin,
                Screen.width - margin * 2f, Screen.height * 0.5f - margin);
            Vector2 edgePoint = ClampToRectEdge(centre, dir, bounds);

            float dist = Vector3.Distance(cam.transform.position, s.WorldPosition);
            DrawArrow(edgePoint, dir, s.TimeLeft01, dist, u);
            DrawTapZone(edgePoint, tapHalf, s);
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
        void DrawTapZone(Vector2 guiPoint, float half, Swimmer s)
        {
            var rect = new Rect(guiPoint.x - half, guiPoint.y - half, half * 2f, half * 2f);
            UIBlocker.Block(rect);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && helm != null)
                helm.SteerToward(s.transform, s.CrewName);
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

        void DrawThrowLine(Swimmer nearestInReach, int u)
        {
            if (nearestInReach == null || helm == null) return;
            var motor = helm.GetComponent<ShipMotor>();
            float speed = motor != null ? motor.CurrentSpeed : 0f;

            float btnH = Mathf.Max(u * 3.4f, 64f);
            float btnW = Mathf.Min(HudLayout.Safe.width - HudLayout.Pad * 2f, u * 26f);
            var safe = HudLayout.Safe;
            float top = HudLayout.BottomClustersTop;
            var rect = new Rect(safe.x + (safe.width - btnW) * 0.5f,
                top - HudLayout.Gap - btnH, btnW, btnH);

            if (speed > OverboardTuning.ThrowMaxSpeed)
            {
                // No button while she's making too much way — just the hint,
                // sized the same as the button would be so it doesn't jump
                // the moment she slows into reach.
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
            var agent = FindNearestAvailableCrew(nearestInReach);
            if (agent != null)
            {
                if (GUI.Button(rect, "Throw line")) agent.StartHaul(nearestInReach);
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
        /// already at the rail/hauling/ashore) to the swimmer's own side of
        /// the hull.
        static CrewAgent FindNearestAvailableCrew(Swimmer swimmer)
        {
            if (swimmer.Hull == null) return null;
            var roster = swimmer.Hull.GetComponentInParent<CrewRoster>();
            if (roster == null) return null;

            Vector3 rail = swimmer.NearestHullSide();
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
