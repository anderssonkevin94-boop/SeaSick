#if UNITY_EDITOR
using System.Reflection;
using System.Text;
using SeaSick.CameraRig;
using SeaSick.Combat;
using SeaSick.Ship;
using SeaSick.Ship.Harpoon;
using SeaSick.Ship.Overboard;
using SeaSick.UI;
using SeaSick.UI.Sheets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SeaSick.Dev
{
    /// **The harpoon button is sealed** (2026-10-04, Kevin on the phone:
    /// "pressing the button to launch the harpoon is spotty at best. It's
    /// impossible to press it without it pressing on my ship"). A dev check,
    /// NOT game code: nothing references it. Play mode, at sea, under way,
    /// the sea HUD up (the harpoon button shows whenever the gun is fitted).
    ///
    /// * `Run()` -- the gates, synchronously. Takes the button rect's centre
    ///   and four inner points and asserts every tap consumer's own
    ///   press-down gate REFUSES each one: the boat stick
    ///   (`SeaStick.CanStartAt`), the camera stick
    ///   (`SeaCameraInput.CanBeginAt`), tap-to-lock
    ///   (`CombatLock.TapAllowedAt`, even where `WouldLock` is true), a
    ///   rescue steer zone (`RescueHud.TapZoneAllowed`) and the own-ship tap
    ///   (`WorldPicker.TapAllowedAt`, the Ship sheet). Also the button's size
    ///   in points and that the reserved rect is the drawn one.
    /// * `Press()` -- end to end: queues a real mouse press and release at
    ///   the button centre (`InputSystem.QueueStateEvent` on
    ///   `Mouse.current`; the Game view must have focus), then reports: did
    ///   the button take it, did the gun start its shot, is the lock / the
    ///   camera's `LockTarget` / the helm's `SteeringToward` / the open
    ///   sheet unchanged, was the stick never held. Asynchronous: read
    ///   `Result()` a few frames later. A started shot is cancelled before
    ///   the wind-up ends, so nothing is fired or hauled. Refused while the
    ///   line is out (a tap would cut it). IMGUI `GUI.Button`s never see
    ///   queued Input System events, so the rescue zones are `Run()`'s.
    /// * `ShipBehindButton()` -- the same two with the player's own hull
    ///   framed under the button: the chase camera is paused and turned
    ///   until the hull's centre projects on the button's centre (the look
    ///   offset orbits about her and keeps her centred, so no
    ///   `userYaw`/`userPitch` puts her bottom-right), the ray through the
    ///   centre is shown to hit her, then `Run()` + `Press()`. The camera's
    ///   pose and `enabled` are put back when the press is done.
    ///
    /// Eval: `return SeaSick.Dev.HarpoonTapCheck.Run();`, or `Press()` /
    /// `ShipBehindButton()` then `Result()`.
    public static class HarpoonTapCheck
    {
        const int MaxFrames = 120;
        const float InnerInset = 0.15f;

        static readonly FieldInfo PendingShot =
            typeof(HarpoonGun).GetField("pendingShot", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo Windup =
            typeof(HarpoonGun).GetField("windup01", BindingFlags.Instance | BindingFlags.NonPublic);

        // --- the press in flight ------------------------------------------------
        static bool running;
        static string result = "not run";
        static StringBuilder log;
        static int step, lastFrame, frames, preFails;
        static Mouse mouse, spareMouse;
        static bool ownMouse;
        static Vector2 pressAt;
        static HarpoonGun gun;
        static HelmInput helm;
        static CombatLock combat;
        static ChaseCamera chase;
        static int tapsBefore;
        static HarpoonState stateBefore;
        static bool hadTarget, steerBefore, sawStick, sawShot, cancelledShot;
        static IHittable lockBefore;
        static Transform lockTargetBefore;
        static ISheet sheetBefore;
        // --- ShipBehindButton's framing, put back afterwards ----------------------
        static bool framed, chaseWasEnabled;
        static Transform camT;
        static Vector3 camPos;
        static Quaternion camRot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Unhook();
            running = false;
            result = "not run";
            framed = false;
            mouse = spareMouse = null;
            chase = null;
        }

        /// The last `Press()` / `ShipBehindButton()` report, or "running".
        public static string Result()
        {
            if (running) return "running (step " + step + ", frame " + frames + ")";
            RemoveSpareMouse();
            return result;
        }

        /// A mouse this check had to add is removed here, outside the input
        /// update it finished in.
        static void RemoveSpareMouse()
        {
            if (spareMouse != null && spareMouse.added) InputSystem.RemoveDevice(spareMouse);
            spareMouse = null;
        }

        // =====================================================================
        // Run: every consumer's own gate, at five points inside the button
        // =====================================================================

        public static string Run()
        {
            if (!Application.isPlaying) return "FAIL: play mode only";
            var r = SeaHud.HarpoonRect;
            if (!SeaHud.HelmShowing || r.width <= 0f || r.height <= 0f)
                return "SKIP: the harpoon button is not showing (at sea, under way, gun fitted, no sheet or menu?)";
            Find();

            var sb = new StringBuilder();
            int fails = 0, checks = 0;
            float onePt = SeaHud.PtPx(1f);
            float minPx = SeaHud.PtPx(SeaHud.HarpoonMinPt);
            sb.Append("button rect ").Append(R(r)).Append(" px = ")
              .Append((r.width / onePt).ToString("F0")).Append(" x ").Append((r.height / onePt).ToString("F0"))
              .Append(" pt (dpi ").Append(Screen.dpi.ToString("F0")).Append(", ").Append(onePt.ToString("F2")).Append(" px/pt)\n");
            checks++;
            if (r.height + 0.5f < minPx || r.width + 0.5f < minPx)
            {
                fails++;
                sb.Append("FAIL size: under ").Append(SeaHud.HarpoonMinPt).Append(" pt\n");
            }
            var drawn = SeaHud.HarpoonDrawnRect;
            checks++;
            if (drawn.width <= 0f) sb.Append("note: the drawn rect is not laid out yet (first frame); run again\n");
            else
            {
                float d = Mathf.Max(Mathf.Max(Mathf.Abs(drawn.xMin - r.xMin), Mathf.Abs(drawn.xMax - r.xMax)),
                                    Mathf.Max(Mathf.Abs(drawn.yMin - r.yMin), Mathf.Abs(drawn.yMax - r.yMax)));
                bool same = d <= 2f;
                if (!same) fails++;
                sb.Append(same ? "ok" : "FAIL").Append(" reserved rect = drawn rect (off by ")
                  .Append(d.ToString("F1")).Append(" px; drawn ").Append(R(drawn)).Append(")\n");
            }

            var cam = Camera.main;
            var ship = gun != null ? gun.gameObject : null;
            for (int i = 0; i < 5; i++)
            {
                Vector2 gui = Point(r, i);
                Vector2 screen = new Vector2(gui.x, Screen.height - gui.y);
                bool stick = SeaStick.CanStartAt(screen);
                bool look = SeaCameraInput.CanBeginAt(screen);
                bool lockTap = CombatLock.TapAllowedAt(screen);
                bool would = combat != null && combat.isActiveAndEnabled && combat.WouldLock(screen);
                bool zone = RescueHud.TapZoneAllowed(gui);
                bool world = WorldPicker.TapAllowedAt(screen);
                string behind = Behind(cam, screen, ship);
                checks += 5;
                int bad = (stick ? 1 : 0) + (look ? 1 : 0) + (lockTap ? 1 : 0) + (zone ? 1 : 0) + (world ? 1 : 0);
                fails += bad;
                sb.Append(bad == 0 ? "ok   " : "FAIL ").Append(PointName(i)).Append(' ').Append(V(gui))
                  .Append(": stick ").Append(Word(stick))
                  .Append(", camera ").Append(Word(look))
                  .Append(", lock ").Append(Word(lockTap)).Append(" (WouldLock ").Append(would).Append(')')
                  .Append(", rescue zone ").Append(Word(zone))
                  .Append(", world tap ").Append(Word(world))
                  .Append("; behind it: ").Append(behind).Append('\n');
            }

            // A control: just left of the button, at its middle. Not judged
            // (other HUD may own it); it shows the gates are not simply shut.
            var ctrl = new Vector2(r.xMin - SeaHud.PtPx(12f), r.center.y);
            var ctrlScreen = new Vector2(ctrl.x, Screen.height - ctrl.y);
            sb.Append("control ").Append(V(ctrl)).Append(" (left of the button, not judged): stick ")
              .Append(Word(SeaStick.CanStartAt(ctrlScreen))).Append(", world tap ")
              .Append(Word(WorldPicker.TapAllowedAt(ctrlScreen))).Append('\n');

            string head = (fails == 0 ? "PASS" : "FAIL (" + fails + ")") + " HarpoonTapCheck.Run, "
                          + checks + " checks, screen " + Screen.width + "x" + Screen.height + "\n";
            return head + sb;
        }

        // =====================================================================
        // Press: a real mouse press + release at the button centre
        // =====================================================================

        public static string Press()
        {
            if (!Application.isPlaying) return "FAIL: play mode only";
            if (running) return "already running: read Result()";
            RemoveSpareMouse();
            preFails = 0;
            string why = StartPress(new StringBuilder("HarpoonTapCheck.Press\n"));
            return why ?? "started: read HarpoonTapCheck.Result() in a second";
        }

        /// Null when the press is under way, else why not (and nothing changed).
        static string StartPress(StringBuilder into)
        {
            var r = SeaHud.HarpoonRect;
            if (!SeaHud.HelmShowing || r.width <= 0f) return "SKIP: the harpoon button is not showing";
            Find();
            if (gun == null) return "SKIP: no HarpoonGun.Player";
            if (gun.State != HarpoonState.Ready && gun.State != HarpoonState.Reloading)
                return "SKIP: the gun is " + gun.State + "; a tap now would cut the line. Run it Ready.";
            if (PendingShot == null || Windup == null) return "FAIL: HarpoonGun.pendingShot / windup01 not found";

            mouse = Mouse.current;
            ownMouse = mouse == null;
            if (ownMouse) mouse = InputSystem.AddDevice<Mouse>("HarpoonTapCheckMouse");

            log = into;
            pressAt = new Vector2(r.center.x, Screen.height - r.center.y);
            tapsBefore = SeaHud.HarpoonTaps;
            stateBefore = gun.State;
            hadTarget = gun.Target != null;
            steerBefore = helm != null && helm.SteeringToward;
            lockBefore = combat != null ? combat.Locked : null;
            lockTargetBefore = chase != null ? chase.LockTarget : null;
            sheetBefore = Sheets.Current;
            sawStick = sawShot = cancelledShot = false;
            log.Append("press at ").Append(V(pressAt)).Append(" (screen space), gun ").Append(stateBefore)
               .Append(hadTarget ? ", target " + gun.TargetLabel : ", no target").Append('\n');

            InputSystem.QueueStateEvent(mouse, new MouseState { position = pressAt }.WithButton(MouseButton.Left));
            step = 1;
            frames = 0;
            lastFrame = Time.frameCount;
            running = true;
            InputSystem.onAfterUpdate -= Tick;
            InputSystem.onAfterUpdate += Tick;
            return null;
        }

        /// Once a player frame, right after input: frame 1 the press lands,
        /// frame 3 the release is processed, then a few frames to watch.
        static void Tick()
        {
            if (!running || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            frames++;

            // What last frame's Update did with the finger.
            if (helm != null && helm.StickInUse) sawStick = true;
            if (gun != null && PendingShot != null && (bool)PendingShot.GetValue(gun))
            {
                sawShot = true;
                // Cancelled long before the wind-up ends: nothing flies.
                PendingShot.SetValue(gun, false);
                Windup.SetValue(gun, 0f);
                cancelledShot = true;
            }

            switch (step)
            {
                case 1: step = 2; break;                     // the press was processed this frame
                case 2:
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = pressAt });
                    step = 3;
                    break;
                case 3: step = 4; frames = 0; break;         // the release was processed this frame
                default:
                    if (frames >= 6) Finish(null);
                    break;
            }
            if (running && frames > MaxFrames) Finish("FAIL: timed out");
        }

        static void Finish(string error)
        {
            Unhook();
            running = false;
            if (ownMouse) spareMouse = mouse;
            mouse = null;
            ownMouse = false;

            var sb = log ?? new StringBuilder();
            int fails = preFails;
            if (error != null) { fails++; sb.Append(error).Append('\n'); }
            int taps = SeaHud.HarpoonTaps - tapsBefore;
            fails += Judge(sb, taps == 1, "the button took the tap (" + taps + " tap" + (taps == 1 ? ")" : "s)"));
            if (stateBefore == HarpoonState.Ready && hadTarget)
                fails += Judge(sb, sawShot, "the gun started its shot (wind-up)"
                    + (cancelledShot ? ", cancelled by the check before it flew" : ""));
            else
                sb.Append("info: no shot expected (gun ").Append(stateBefore).Append(hadTarget ? "" : ", no target")
                  .Append("); the gun is ").Append(gun != null ? gun.State.ToString() : "gone").Append('\n');
            fails += Judge(sb, combat == null || ReferenceEquals(combat.Locked, lockBefore), "the combat lock is unchanged");
            fails += Judge(sb, chase == null || chase.LockTarget == lockTargetBefore, "the camera's LockTarget is unchanged");
            fails += Judge(sb, helm == null || helm.SteeringToward == steerBefore, "the helm's SteeringToward is unchanged");
            fails += Judge(sb, !sawStick && (helm == null || !helm.StickInUse), "the boat stick was never held");
            fails += Judge(sb, ReferenceEquals(Sheets.Current, sheetBefore),
                "no sheet opened (" + (Sheets.Current != null ? Sheets.Current.GetType().Name : "none") + ")");
            sb.Append("note: IMGUI tap zones (RescueHud) never see queued Input System events; Run() covers them\n");

            if (framed) Unframe(sb);
            result = (fails == 0 ? "PASS " : "FAIL (" + fails + ") ") + sb;
        }

        static int Judge(StringBuilder sb, bool ok, string what)
        {
            sb.Append(ok ? "ok   " : "FAIL ").Append(what).Append('\n');
            return ok ? 0 : 1;
        }

        static void Unhook() => InputSystem.onAfterUpdate -= Tick;

        // =====================================================================
        // ShipBehindButton: her own hull under the button, then Run + Press
        // =====================================================================

        public static string ShipBehindButton()
        {
            if (!Application.isPlaying) return "FAIL: play mode only";
            if (running) return "already running: read Result()";
            RemoveSpareMouse();
            var r = SeaHud.HarpoonRect;
            if (!SeaHud.HelmShowing || r.width <= 0f) return "SKIP: the harpoon button is not showing";
            Find();
            if (gun == null || chase == null) return "SKIP: no player gun or no ChaseCamera";
            var cam = chase.GetComponent<Camera>();
            if (cam == null) return "SKIP: the ChaseCamera has no Camera";

            // Frame her: the camera stays where it is and turns until the
            // hull's centre lies on the ray through the button's centre.
            camT = cam.transform;
            camPos = camT.position;
            camRot = camT.rotation;
            chaseWasEnabled = chase.enabled;
            chase.enabled = false;
            framed = true;
            Vector3 aim = HullCentre(gun.gameObject);
            Vector2 centre = new Vector2(r.center.x, Screen.height - r.center.y);
            Vector3 rayDir = cam.ScreenPointToRay(centre).direction;
            camT.rotation = Quaternion.FromToRotation(rayDir, (aim - camPos).normalized) * camRot;

            var sb = new StringBuilder("HarpoonTapCheck.ShipBehindButton\n");
            if (Camera.main != cam) sb.Append("note: Camera.main is not the chase camera (").Append(Camera.main != null ? Camera.main.name : "none")
                .Append("); WorldPicker and CombatLock aim with Camera.main\n");
            string behind = Behind(cam, centre, gun.gameObject);
            bool onHull = behind.StartsWith("own ship");
            sb.Append(onHull ? "ok   " : "FAIL ").Append("her hull is under the button centre: ").Append(behind).Append('\n');
            if (combat != null)
                sb.Append("info: an own-ship tap here locks nothing (WouldLock ").Append(combat.WouldLock(centre))
                  .Append("; CombatLock refuses PlayerHull); without the gate WorldPicker would open ")
                  .Append(SheetBehind(cam, centre)).Append('\n');
            string run = Run();
            sb.Append(run).Append('\n');
            bool runOk = onHull && run.StartsWith("PASS");
            preFails = runOk ? 0 : 1;

            string why = StartPress(sb);
            if (why != null)
            {
                sb.Append(why).Append('\n');
                Unframe(sb);
                result = (runOk ? "PASS (no press) " : "FAIL ") + sb;
                return result;
            }
            return "started: framed, Run() " + (runOk ? "passed" : "FAILED") + "; read HarpoonTapCheck.Result() in a second";
        }

        static void Unframe(StringBuilder sb)
        {
            if (!framed) return;
            framed = false;
            if (camT != null) camT.SetPositionAndRotation(camPos, camRot);
            if (chase != null) chase.enabled = chaseWasEnabled;
            sb.Append("camera pose and ChaseCamera.enabled restored\n");
        }

        // =====================================================================
        // Bits
        // =====================================================================

        static void Find()
        {
            gun = HarpoonGun.Player;
            helm = gun != null ? gun.GetComponent<HelmInput>() : null;
            combat = gun != null ? gun.GetComponent<CombatLock>() : null;
            if (combat == null) combat = CombatHud.Source;
            if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
        }

        /// 0 = the centre; 1-4 = inside each corner, `InnerInset` of the size in.
        static Vector2 Point(Rect r, int i)
        {
            if (i == 0) return r.center;
            float dx = r.width * InnerInset, dy = r.height * InnerInset;
            float x = (i == 1 || i == 3) ? r.xMin + dx : r.xMax - dx;
            float y = (i == 1 || i == 2) ? r.yMin + dy : r.yMax - dy;
            return new Vector2(x, y);
        }

        static string PointName(int i) =>
            i == 0 ? "centre" : i == 1 ? "top-left" : i == 2 ? "top-right" : i == 3 ? "bottom-left" : "bottom-right";

        /// What a world ray through `screen` meets first (solid colliders;
        /// triggers only if nothing solid): "own ship (collider)", another
        /// name, or "open sea".
        static string Behind(Camera cam, Vector2 screen, GameObject ship)
        {
            var col = FirstHit(cam, screen);
            if (col == null) return "open sea";
            if (ship != null && col.transform.IsChildOf(ship.transform)) return "own ship (" + col.name + ")";
            return col.name;
        }

        static string SheetBehind(Camera cam, Vector2 screen)
        {
            var col = FirstHit(cam, screen);
            var sheet = col != null ? Sheets.TryCreateFor(col) : null;
            return sheet != null ? sheet.GetType().Name : "nothing";
        }

        static Collider FirstHit(Camera cam, Vector2 screen)
        {
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screen);
            return Physics.Raycast(ray, out var hit, 6000f, ~0, QueryTriggerInteraction.Ignore) ? hit.collider : null;
        }

        /// The centre of her solid colliders' bounds (her pivot if she has none).
        static Vector3 HullCentre(GameObject ship)
        {
            var cols = ship.GetComponentsInChildren<Collider>();
            bool any = false;
            var b = new Bounds();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i].isTrigger || !cols[i].enabled) continue;
                if (!any) { b = cols[i].bounds; any = true; }
                else b.Encapsulate(cols[i].bounds);
            }
            return any ? b.center : ship.transform.position;
        }

        static string Word(bool allowed) => allowed ? "ALLOWED" : "refused";
        static string V(Vector2 v) => "(" + v.x.ToString("F0") + ", " + v.y.ToString("F0") + ")";
        static string R(Rect r) => "(" + r.x.ToString("F0") + ", " + r.y.ToString("F0") + ", "
                                   + r.width.ToString("F0") + " x " + r.height.ToString("F0") + ")";
    }
}
#endif
