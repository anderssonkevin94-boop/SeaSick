using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.World;
using SeaSick.CameraRig;

/// **Does the land actually stay under the finger?**
///
/// Every gate here is driven through the public methods `IslandInput` calls —
/// `GrabBegin/Move/End`, `ZoomAt`, `OrbitAbout`, `FlyTo`, `Nudge`,
/// `KillMotion` — and measured by **projecting world points through the real
/// `Camera.main` after the frame has rendered**. Those two rules are the whole
/// design of this file:
///
///   * A gate that moved the camera by writing `Pan` itself would be testing
///     that `Pan` can be written. The bug this exists to catch is a gesture
///     that solves for the wrong pose, and the only way to catch it is to make
///     the gesture do the solving.
///   * A gate that asked `IslandCam` where it thinks the lens is would be
///     asking the arithmetic under test to mark its own work. The rendered
///     transform is a second, independent witness — and where the two are
///     compared (the terrain sweep) it is the DISAGREEMENT that is reported,
///     because a rig clamp that had to fire means the yield did not.
///
/// Thresholds are fractions of **screen height**, never pixels: the editor
/// Game view is 422 px tall here and the phone is 2340, and a bound in pixels
/// would be four different bounds. Run it at both shapes
/// (`RunProbe.ViewDesk()` / `ViewPhone()`) — restart play after changing the
/// aspect, or the HUD and the frustum disagree about what the window is.
///
/// Getting to an island is `CampProbe`'s approach, and deliberately the same
/// one: rank the standoffs by SHORE gap, ask the three questions
/// `AnchorController` asks in its order, then warp-and-retry rather than warp
/// once, because she has way on after casting off and drifts off the spot
/// between the warp and the test.
public class IslandCamProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("IslandCamProbe: not in play mode"); return; }
        var runner = new GameObject("IslandCamProbeRunner").AddComponent<IslandCamProbe>();
        runner.StartCoroutine(runner.Run());
    }

    StringBuilder sb;
    int fails;

    // The shape of the window the camera is judged in. Every threshold below
    // is a FRACTION of this and never a pixel count: the editor Game view is
    // 422 px tall here and the phone is 2340, and the same bound in pixels
    // would be two entirely different bounds.
    //
    // `Screen.height` inside a play-mode callback is the Game view; an
    // editor-context read is something else again. This file only ever runs
    // in play mode.
    float H => Mathf.Max(1f, Screen.height);
    float W => Mathf.Max(1f, Screen.width);

    Vector2 At(float fx, float fy) => new Vector2(W * fx, H * fy);

    System.Collections.IEnumerator Run()
    {
        sb = new StringBuilder();
        fails = 0;

        // **Read the screen from INSIDE a frame.** A coroutine's first segment
        // runs inside `StartCoroutine`, which here is inside `Execute`, which
        // is called from an editor script -- and `Screen` in that context is
        // the editor window, not the Game view. The first portrait run of this
        // probe announced itself as "1531x937 DESK" with the Game view set to
        // 1080x2340, which is DEV-TOOLS' oldest measurement trap.
        yield return new WaitForEndOfFrame();

        sb.AppendLine($"window {Screen.width}x{Screen.height}  "
            + $"(aspect {W / H:F3} — {(W >= H ? "DESK" : "PHONE")}; "
            + $"run the other one too)");

        // --- the one gate that needs nothing at all --------------------------
        //
        // First, because it is the promise the whole tilt curve rests on and
        // it costs no scene: the shipped composition must survive whatever
        // Kevin does to the other two points of the curve.
        float mid = IslandCam.AutoTilt(165f);
        sb.AppendLine();
        sb.AppendLine("THE TILT CURVE:");
        sb.AppendLine($"  8 m {IslandCam.AutoTilt(8f):F1}°   40 m {IslandCam.AutoTilt(40f):F1}°   "
            + $"165 m {mid:F2}°   300 m {IslandCam.AutoTilt(300f):F1}°   "
            + $"520 m {IslandCam.AutoTilt(520f):F1}°");
        Gate("the-curve-passes-through-the-shipped-shot",
            Mathf.Abs(mid - 32f) < 0.1f, $"165 m gives {mid:F3}°, not 32°");
        Gate("and-it-is-shallow-low-and-steep-high",
            IslandCam.AutoTilt(20f) < mid && IslandCam.AutoTilt(400f) > mid,
            $"{IslandCam.AutoTilt(20f):F1}° / {mid:F1}° / {IslandCam.AutoTilt(400f):F1}°");

        // --- who follows what ------------------------------------------------
        //
        // PRINTED, not gated. `IslandCam.Feel.nearGroundReach` is a claim
        // about where full-detail terrain exists, and that claim is only true
        // while the streamer follows the SHIP. If somebody retargets it, this
        // is where it shows up instead of as "the ground looks like origami
        // when I zoom in on the far side".
        sb.AppendLine();
        sb.AppendLine("WHAT EACH STREAMING SYSTEM FOLLOWS:");
        ReportFollowers();

        var anchor = Object.FindFirstObjectByType<AnchorController>();
        var motor = Object.FindFirstObjectByType<ShipMotor>();
        var chase = Object.FindFirstObjectByType<ChaseCamera>();
        if (anchor == null || motor == null || chase == null)
        {
            Fail("no ship or no chase camera in the scene");
            yield break;
        }

        // --- get her to an island --------------------------------------------

        Island target = null; Vector3 standOff = default; float bestGap = float.MaxValue;
        Vector3 from = motor.transform.position;
        foreach (var isle in Island.All)
        {
            if (isle == null || isle.IsHome) continue;
            for (int b = 0; b < 24; b++)
            {
                float ang = b / 24f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Vector3 ship = isle.transform.position + dir * (isle.RadiusAt(ang) + 18f);
                if (Island.TerrainHeight == null || Island.TerrainHeight(ship.x, ship.z) > -0.5f) continue;
                if (Island.Nearest(ship) != isle) continue;
                if (Island.FlatDistance(ship, isle.transform.position)
                    > isle.RadiusToward(ship) + 30f) continue;
                if (!isle.HasBeachToward(ship)) continue;
                float gap = Island.FlatDistance(ship, from);
                if (gap < bestGap) { bestGap = gap; target = isle; standOff = ship; }
            }
        }
        if (target == null) { Fail("no island in the world offers a beach to land on"); yield break; }

        if (anchor.CurrentState != AnchorController.State.Underway)
        {
            anchor.CastOff();
            float cast = Time.realtimeSinceStartup;
            while (anchor.CurrentState != AnchorController.State.Underway
                   && Time.realtimeSinceStartup - cast < 10f) yield return null;
        }

        bool landed = false; string why = "never tried";
        float landBy = Time.realtimeSinceStartup + 5f;
        while (!landed && Time.realtimeSinceStartup < landBy)
        {
            Warp(motor, standOff, Quaternion.LookRotation(target.transform.position - standOff));
            yield return new WaitForFixedUpdate();
            yield return null;
            landed = anchor.TryLand(out why);
        }
        sb.AppendLine();
        sb.AppendLine($"AT {target.name} (r {target.Radius:F0} m): {(landed ? "landed" : "COULD NOT LAND — " + why)}");
        Gate("she-can-land-there", landed, why);
        if (!landed) { Finish(); yield break; }

        while (Outpost.Surveying(target)) yield return null;

        var cam = Object.FindFirstObjectByType<IslandCam>();
        if (cam == null) { Fail("no IslandCam"); yield break; }

        // The view has to have ARRIVED. It is an exponential blend, so this is
        // not a formality -- and `Ready` is exactly what the input layer waits
        // for, so waiting for anything else here would gate a state the game
        // never actually lets a finger into.
        float readyBy = Time.realtimeSinceStartup + 25f;
        while (!cam.Ready && Time.realtimeSinceStartup < readyBy) yield return null;
        sb.AppendLine($"  the view came up in {25f - (readyBy - Time.realtimeSinceStartup):F1} s "
            + $"(blend {chase.OverviewLevel:F4})");
        Gate("the-view-comes-up", cam.Ready,
            $"blend still {chase.OverviewLevel:F3} after 25 s");
        if (!cam.Ready) { Finish(); yield break; }

        // **...and then for HER to lie still.** The composed shot is seated
        // from the ship's bearing, and for the first seconds after the anchor
        // goes down she is still being warped alongside -- the seat swings with
        // her (measured: 15 m and 6.4 degrees in two frames) and every gate
        // below that compares "before the touch" with "after" would be
        // measuring the mooring. A player can grab through that; a probe
        // asking whether the TOUCH moved the picture cannot.
        float stillBy = Time.realtimeSinceStartup + 25f;
        int stillFrames = 0;
        Vector3 lastLens = Camera.main.transform.position;
        while (stillFrames < 10 && Time.realtimeSinceStartup < stillBy)
        {
            yield return new WaitForEndOfFrame();
            Vector3 lens = Camera.main.transform.position;
            stillFrames = chase.OverviewSettled && Vector3.Distance(lens, lastLens) < 0.15f
                ? stillFrames + 1 : 0;
            lastLens = lens;
        }
        sb.AppendLine($"  she lay still {(stillFrames >= 10 ? "" : "— NOT, after 25 s —")} "
            + $"(rig settled {chase.OverviewSettled})");

        yield return new WaitForEndOfFrame();

        // The height field, read ONCE. It is a non-serialisable static that a
        // play-mode recompile nulls, and a probe that re-read it every loop
        // could measure half its sweep against a live field and half against
        // nothing without ever saying so.
        var hf = GroundPick.Height;
        sb.AppendLine($"  height field: {(hf != null ? "live" : "NULL — stop play and play again")}");

        // =====================================================================
        // 1. AN UNTOUCHED VIEW IS THE SHIPPED ONE
        // =====================================================================

        var shot = chase.Overview;
        float tilt0 = chase.CurrentTilt;
        float ground0 = shot.HasValue ? shot.Value.ground : -1f;
        // **What is actually ON SCREEN, which is not the same number.** An
        // untouched shot is not `free`, so the legibility clamp may be holding
        // it closer than the 165 m it is authored at (118 m at Island_1). The
        // latch has to keep the PICTURE, so that is what it is gated against
        // -- the first version of that gate compared it with the authored
        // number and failed a latch that was doing exactly the right thing.
        float shown0 = chase.CurrentSpan * 2f
            * Mathf.Tan(chase.OverviewFov * 0.5f * Mathf.Deg2Rad);
        sb.AppendLine();
        sb.AppendLine("UNTOUCHED:");
        sb.AppendLine($"  {ground0:F1} m of ground at {tilt0:F2}°, free {(shot.HasValue && shot.Value.free)}, "
            + $"hands on {cam.HandsOn}");
        Gate("an-untouched-view-is-32-degrees", Mathf.Abs(tilt0 - 32f) < 0.25f,
            $"{tilt0:F2}°");
        Gate("an-untouched-view-is-165-m-of-ground",
            Mathf.Abs(ground0 - cam.DefaultGround) < 1f,
            $"{ground0:F1} m against {cam.DefaultGround:F0}");
        // `Driven` is only REPORTED. It also goes true for a `LookAt` -- which
        // is what siting a camp does -- so an island that already has one
        // would fail a gate on it for a completely correct reason. What must
        // be false here is a HAND on the land.
        Gate("an-untouched-view-has-nobody-driving-it", !cam.HandsOn,
            $"handsOn {cam.HandsOn}");

        // =====================================================================
        // 2. THE FIRST TOUCH MOVES NOTHING
        //
        // Three things change at once when a hand arrives -- `free`, `direct`
        // and this component taking over the azimuth and the tilt -- and any
        // of them alone would jump the picture. Half a metre is the bound
        // because half a metre at 250 m is a fifth of a degree.
        // =====================================================================

        // **A control first.** An untouched shot is not `free`, so it is still
        // sliding to hold the ship in frame and she is still swinging at
        // anchor -- the lens is never perfectly still, and charging that drift
        // to the touch would be measuring the anchor. Two frames of doing
        // nothing, over the same two frames the touch will take.
        Vector3 driftFrom = Camera.main.transform.position;
        Quaternion driftRot = Camera.main.transform.rotation;
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        float drift = Vector3.Distance(Camera.main.transform.position, driftFrom);
        float driftTurn = Quaternion.Angle(Camera.main.transform.rotation, driftRot);

        Vector3 camBefore = Camera.main.transform.position;
        Quaternion rotBefore = Camera.main.transform.rotation;
        // Where the middle of the frame's ground is ON SCREEN before the touch:
        // the picture is what must not move, and metres of lens travel only
        // mean something once divided by how far away the ground is.
        bool hadMid = cam.GroundUnder(At(0.5f, 0.5f), out Vector3 midGround);
        Vector3 midBefore = hadMid ? Camera.main.WorldToScreenPoint(midGround) : Vector3.zero;

        cam.GrabBegin(At(0.5f, 0.5f));
        yield return new WaitForEndOfFrame();
        // Read BEFORE letting go. `GrabEnd` regrounds, and regrounding re-bases
        // the zoom number on the terrain the centre ray actually hits without
        // moving the picture -- 165 m aimed at a clearing's datum becomes 118 m
        // aimed at the hillside in front of it. That is correct, and it is not
        // what this gate is about: the LATCH must keep what was on screen.
        float latchedGround = cam.Ground;
        cam.GrabEnd();
        yield return new WaitForEndOfFrame();

        float jump = Mathf.Max(0f,
            Vector3.Distance(Camera.main.transform.position, camBefore) - drift);
        float turned = Mathf.Max(0f,
            Quaternion.Angle(Camera.main.transform.rotation, rotBefore) - driftTurn);
        sb.AppendLine();
        sb.AppendLine("THE FIRST TOUCH:");
        sb.AppendLine($"  doing nothing for the same two frames moved it {drift * 100f:F1} cm "
            + $"and turned it {driftTurn:F3}° — that is the control");
        sb.AppendLine($"  the touch moved it {jump * 100f:F1} cm more and turned it "
            + $"{turned:F3}° more; now {cam.Ground:F1} m of ground at {cam.TiltNow:F2}°, "
            + $"bearing {cam.AzimuthDeg:F1}°");
        // **Gated on the PICTURE, in screen heights.** The first version gated
        // the lens at half a metre, and failed a rig doing what it should: the
        // untouched shot chases a ship swinging at anchor through a 2.2/s low
        // pass, so it is always a metre or two behind its seat, and the touch
        // closes that gap quickly on purpose (`ChaseCamera.directOffset`). A
        // metre and a half of lens at 250 m is under 1 % of the frame, eased
        // over a third of a second. What would be a fault is the ground under
        // the middle of the screen JUMPING, so that is what is measured.
        float midShift = -1f;
        if (hadMid)
        {
            Vector3 midAfter = Camera.main.WorldToScreenPoint(midGround);
            midShift = Vector2.Distance(new Vector2(midBefore.x, midBefore.y),
                                        new Vector2(midAfter.x, midAfter.y)) / Screen.height;
        }
        sb.AppendLine($"  the ground at the middle of the frame shifted {midShift * 100f:F2}% of screen height "
            + $"({jump:F2} m of lens over the control's {drift:F2} m)");
        Gate("the-first-touch-moves-the-picture-almost-not-at-all",
            hadMid && midShift < 0.015f,
            hadMid ? $"{midShift * 100f:F2}% of screen height" : "no ground under the middle of the frame");
        Gate("and-does-not-turn-it", turned < 0.25f,
            $"{turned:F3}° over the control's {driftTurn:F3}°");
        Gate("and-the-latch-kept-the-zoom", Mathf.Abs(latchedGround - shown0) < 2f,
            $"{latchedGround:F1} m against the {shown0:F1} m that was on screen "
            + $"(authored {ground0:F0}; {cam.Ground:F1} m once regrounded)");
        Gate("and-the-tilt", Mathf.Abs(cam.TiltNow - tilt0) < 0.5f,
            $"{cam.TiltNow:F2}° against {tilt0:F2}°");

        // **Exactness is only promised once the leftover gap has gone.** The
        // rig takes the hand's motion 1:1 from the first frame, but it starts
        // wherever the low pass had got it to and closes that gap underneath
        // (`ChaseCamera.directOffset`). Until it has, a ray composed against
        // the asked-for pose is off by what is left -- so the gates below wait
        // for the rig to SAY the two poses are one, and how long that takes is
        // itself gated: it is the window in which a grab is not yet exact.
        float directT0 = Time.realtimeSinceStartup;
        while (!chase.OverviewDirect && Time.realtimeSinceStartup - directT0 < 5f)
            yield return new WaitForEndOfFrame();
        float directAfter = Time.realtimeSinceStartup - directT0;
        sb.AppendLine($"  the drawn pose became the asked-for pose {directAfter:F2} s after the touch");
        Gate("the-rig-hands-over-to-the-hand-within-a-second-and-a-half",
            chase.OverviewDirect && directAfter < 1.5f,
            chase.OverviewDirect ? $"{directAfter:F2} s" : "never, in 5 s");

        // =====================================================================
        // 3. THE GRABBED POINT STAYS UNDER THE CURSOR
        // =====================================================================

        // **From the middle of the island, not from where the dock composed
        // the shot.** The composed aim at Island_1 sits 154 m from the island's
        // middle and the reach is 163 m, so the first version of this dragged
        // the land straight into its own boundary and then reported the clamp
        // holding it there as a 9-21 % stray. The clamp has its own gate
        // further down; this one is about a hand on open ground.
        foreach (float zoom in new[] { 165f, 20f })
        {
            yield return ToTheMiddle(cam, target);
            cam.ZoomTo(zoom);
            yield return Settled();

            Vector2 p0 = At(0.5f, 0.42f);
            if (!cam.GroundUnder(p0, out Vector3 held))
            {
                sb.AppendLine($"  (nothing under the cursor at {zoom:F0} m — drag not run)");
                continue;
            }

            // How rough the ground it is dragged over is, because the grab
            // freezes a PLANE at the grabbed height: over a hillside the
            // terrain yield can stand the shot up mid-drag, and that is a
            // legitimate reason to be 2% out instead of 0.5%.
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i <= 8 && hf != null; i++)
            {
                Vector3 q = held + new Vector3(1f, 0f, 1f) * (i * zoom * 0.05f);
                float y = hf(q.x, q.z);
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
            float rough = hf != null ? hi - lo : 0f;
            float bound = rough > 8f ? 0.02f : 0.005f;

            float groundAtGrab = cam.Ground, tiltAtGrab = cam.TiltNow;
            Vector3 pivotAtGrab = cam.Pivot;
            cam.GrabBegin(p0);
            yield return new WaitForEndOfFrame();

            float worst = 0f;
            const int steps = 8;
            for (int i = 1; i <= steps; i++)
            {
                // 40% of screen HEIGHT, diagonally, so both axes are exercised.
                Vector2 p = p0 + new Vector2(H * 0.4f * 0.7f, H * 0.4f * 0.7f) * (i / (float)steps);
                cam.GrabMove(p);
                yield return new WaitForEndOfFrame();
                worst = Mathf.Max(worst, ScreenError(held, p));
            }
            cam.GrabEnd();
            yield return new WaitForEndOfFrame();

            sb.AppendLine();
            sb.AppendLine($"A 40%-OF-SCREEN DRAG AT {zoom:F0} M OF GROUND:");
            sb.AppendLine($"  the ground under the drag varies {rough:F1} m, so the bound is "
                + $"{bound * 100f:F1}%");
            // What a stray would be blamed on, read off the state rather than
            // guessed: a zoom that had not finished easing, a tilt the yield
            // moved, or a pivot the reach clamp stopped.
            sb.AppendLine($"  asked {zoom:F0} m, had {groundAtGrab:F1} m at the grab and {cam.Ground:F1} m after; "
                + $"tilt {tiltAtGrab:F1}° -> {cam.TiltNow:F1}°; pivot moved "
                + $"{Island.FlatDistance(pivotAtGrab, cam.Pivot):F1} m, now "
                + $"{Island.FlatDistance(cam.Pivot, cam.transform.position):F0} m from her and "
                + $"{Island.FlatDistance(cam.Pivot, target.transform.position):F0} m from the island's middle");
            sb.AppendLine($"  worst the held point ever strayed from the cursor: "
                + $"{worst * 100f:F2}% of screen height ({worst * H:F1} px)");
            Gate($"the-land-stays-under-the-cursor-at-{zoom:F0}-m", worst <= bound,
                $"{worst * 100f:F2}% against {bound * 100f:F1}%");
        }

        // =====================================================================
        // 4. ZOOM HOLDS WHAT IT IS AIMED AT
        // =====================================================================

        yield return ToTheMiddle(cam, target);
        cam.ZoomTo(165f);
        yield return Settled();

        Vector2 zp = At(0.34f, 0.60f);
        if (cam.GroundUnder(zp, out Vector3 aimedAt))
        {
            float worstStep = 0f;
            for (int i = 0; i < 4; i++)
            {
                cam.ZoomAt(zp, 0.8f);
                yield return new WaitForEndOfFrame();
                worstStep = Mathf.Max(worstStep, ScreenError(aimedAt, zp));
            }
            float closeTo = cam.Ground;
            for (int i = 0; i < 4; i++)
            {
                cam.ZoomAt(zp, 1f / 0.8f);
                yield return new WaitForEndOfFrame();
                worstStep = Mathf.Max(worstStep, ScreenError(aimedAt, zp));
            }
            float roundTrip = ScreenError(aimedAt, zp);

            sb.AppendLine();
            sb.AppendLine("ZOOM TO THE CURSOR:");
            sb.AppendLine($"  four steps in to {closeTo:F1} m and four back out to {cam.Ground:F1} m");
            sb.AppendLine($"  worst drift at any step {worstStep * 100f:F2}%, "
                + $"after the round trip {roundTrip * 100f:F2}% of screen height");
            Gate("zoom-holds-the-point-under-the-cursor", worstStep <= 0.01f,
                $"{worstStep * 100f:F2}%");
            Gate("and-a-round-trip-comes-back-to-the-same-place", roundTrip <= 0.01f,
                $"{roundTrip * 100f:F2}%");
            Gate("and-the-zoom-itself-came-back",
                Mathf.Abs(cam.Ground - 165f) < 3f, $"{cam.Ground:F1} m");
        }
        else sb.AppendLine("  (nothing under the cursor — the zoom gates did not run)");

        // =====================================================================
        // 5. AN ORBIT KEEPS ITS PIVOT
        // =====================================================================

        cam.Home();
        yield return ToTheMiddle(cam, target);
        cam.ZoomTo(165f);
        yield return Settled();

        Vector2 op = At(0.5f, 0.45f);

        // `Home` above handed the shot back, so the swing's first step is a
        // FIRST TOUCH again and the rig spends the next ~0.7 s closing its
        // leftover gap underneath it (section 2 gates that). Measured through
        // the real lens, that gap read as the pivot straying 2.8 % during the
        // swing and 0.1 % after it -- the hand-over, not the orbit. Take hold
        // first and wait for the two poses to be one, as the drags did.
        cam.GrabBegin(op);
        cam.GrabEnd();
        cam.KillMotion();
        float orbitDirectBy = Time.realtimeSinceStartup + 5f;
        while (!chase.OverviewDirect && Time.realtimeSinceStartup < orbitDirectBy)
            yield return new WaitForEndOfFrame();
        yield return Settled();

        if (cam.GroundUnder(op, out Vector3 orbitPoint))
        {
            float worstOrbit = 0f;
            for (int i = 0; i < 18; i++)                 // 18 x 5 = 90 degrees
            {
                cam.OrbitAbout(op, 5f, i < 2 ? 5f : (i == 2 ? -10f : 0f));
                yield return new WaitForEndOfFrame();
                worstOrbit = Mathf.Max(worstOrbit, ScreenError(orbitPoint, op));
            }
            float azSwung = cam.AzimuthDeg;
            cam.OrbitEnd();
            // Letting go of a swing leaves it coasting, which is the point of
            // it — but what is being measured here is `Reground`, so stop the
            // coast first. Through `KillMotion`, which is the same call a
            // press makes; a gate that stilled the camera by any other route
            // would be measuring its own hand on the brake.
            cam.KillMotion();
            yield return new WaitForEndOfFrame();
            float afterEnd = ScreenError(orbitPoint, op);

            sb.AppendLine();
            sb.AppendLine("A 90° SWING WITH ±10° OF TILT IN IT:");
            sb.AppendLine($"  bearing now {azSwung:F1}°, tilt {cam.TiltNow:F1}°");
            sb.AppendLine($"  worst the pivot strayed {worstOrbit * 100f:F2}%, "
                + $"and {afterEnd * 100f:F2}% after Reground");
            Gate("an-orbit-keeps-its-pivot", worstOrbit <= 0.01f,
                $"{worstOrbit * 100f:F2}%");
            Gate("and-regrounding-does-not-move-the-picture", afterEnd <= 0.01f,
                $"{afterEnd * 100f:F2}%");
            Gate("the-tilt-stayed-inside-its-limits",
                cam.TiltNow >= IslandCam.Feel.minTiltDeg - 0.01f
                && cam.TiltNow <= IslandCam.Feel.yieldMaxTiltDeg + 0.01f,
                $"{cam.TiltNow:F2}°");
        }
        else sb.AppendLine("  (nothing under the cursor — the orbit gates did not run)");

        // =====================================================================
        // 6. THE LENS NEVER GOES UNDER THE GROUND
        //
        // Measured TWICE and the gate is on the disagreement. The rig has its
        // own terrain clamp and it would make this pass on its own -- which
        // would be circular verification of the kind that has cost this
        // project a session before. What is actually being asked is whether
        // `IslandCam`'s own yield keeps the VIRTUAL pose legal, so that the
        // rig's clamp never has to fire. If the two poses differ, the yield
        // failed and every screen ray this frame was cast from somewhere the
        // camera is not.
        // =====================================================================

        cam.Home();
        yield return Settled();

        float worstVirtual = 99f, worstWater = 99f, worstDisagree = 0f;
        int states = 0, underTerrain = 0, underWater = 0;
        string worstWhere = "";

        for (int az = 0; az < 8; az++)
        {
            cam.OrbitAbout(At(0.5f, 0.5f), 45f, 0f);
            cam.OrbitEnd();
            cam.KillMotion();               // no coasting into the measurement
            for (int f = 0; f < 3; f++) yield return null;

            foreach (float g in new[] { 12f, 40f, 165f, 420f })
            {
                cam.ZoomTo(g);
                for (int f = 0; f < 20; f++) yield return null;

                for (int t = 0; t < 5; t++)
                {
                    // A transect, walked with the same held-key call the
                    // arrow keys make. `dt` is an argument, so one call is a
                    // controlled step rather than a frame count.
                    if (t > 0) cam.Nudge(0f, 1f, 0f, 0f, 0f, 0.75f);
                    for (int f = 0; f < 6; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    states++;
                    Vector3 lens = Camera.main.transform.position;
                    float terr = hf != null ? hf(lens.x, lens.z) : -9999f;
                    float overGround = lens.y - terr;
                    float overWater = lens.y;             // sea level is 0

                    if (cam.VirtualPose(out Vector3 seat, out _, out _))
                    {
                        float vGround = seat.y - (hf != null ? hf(seat.x, seat.z) : -9999f);
                        if (vGround < worstVirtual)
                        {
                            worstVirtual = vGround;
                            worstWhere = $"bearing {cam.AzimuthDeg:F0}°, {cam.Ground:F0} m of ground";
                        }
                        worstDisagree = Mathf.Max(worstDisagree, Vector3.Distance(seat, lens));
                    }
                    if (overGround < IslandCam.Feel.clearance - 0.1f) underTerrain++;
                    if (overWater < 2.6f) underWater++;
                    worstWater = Mathf.Min(worstWater, overWater);
                }
                // Walk back so the next zoom starts from roughly the middle.
                for (int t = 0; t < 4; t++)
                {
                    cam.Nudge(0f, -1f, 0f, 0f, 0f, 0.75f);
                    yield return null;
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine($"UNDER THE GROUND? {states} states "
            + "(8 bearings x 4 zooms x a 5-station transect):");
        sb.AppendLine($"  thinnest air under the VIRTUAL lens {worstVirtual:F2} m "
            + $"(wanted {IslandCam.Feel.clearance:F1}) at {worstWhere}");
        sb.AppendLine($"  lowest the RENDERED lens got over the water {worstWater:F2} m");
        sb.AppendLine($"  worst the rendered lens and the virtual one disagreed: "
            + $"{worstDisagree:F2} m");
        Gate("the-yield-keeps-the-virtual-lens-out-of-the-ground",
            worstVirtual >= IslandCam.Feel.clearance - 0.1f,
            $"{worstVirtual:F2} m of air where {IslandCam.Feel.clearance:F1} was wanted");
        Gate("so-the-rigs-clamp-never-has-to-fire", worstDisagree < 0.25f,
            $"{worstDisagree:F2} m apart — the clamp moved the lens and the rays are stale");
        Gate("the-rendered-lens-is-never-under-the-terrain", underTerrain == 0,
            $"{underTerrain} of {states} states");
        Gate("nor-under-the-water", underWater == 0,
            $"{underWater} of {states} states, lowest {worstWater:F2} m");

        // =====================================================================
        // 7. THE PIVOT STAYS INSIDE BOTH REACH DISCS
        // =====================================================================

        cam.ZoomTo(165f);
        yield return Settled();
        for (int i = 0; i < 40; i++)
        {
            cam.Nudge(1f, 1f, 0f, 0f, 0f, 1.0f);      // hard into the corner
            yield return null;
        }
        yield return new WaitForEndOfFrame();

        Vector3 pivot = cam.Pivot;
        float fromShip = Island.FlatDistance(pivot, anchor.transform.position);
        float fromIsland = Island.FlatDistance(pivot, target.transform.position);
        float islandReach = Mathf.Max(60f, target.Radius * 1.15f);
        sb.AppendLine();
        sb.AppendLine("FORTY SECONDS OF HOLDING THE ARROW KEYS:");
        sb.AppendLine($"  the middle of the frame is {fromShip:F1} m from her "
            + $"(reach {IslandCam.Feel.reachFromShip:F0}) and {fromIsland:F1} m from the island's "
            + $"middle (reach {islandReach:F0})");
        Gate("the-frame-cannot-be-walked-away-from-the-ship",
            fromShip <= IslandCam.Feel.reachFromShip + 1f,
            $"{fromShip:F1} m against {IslandCam.Feel.reachFromShip:F0}");
        Gate("nor-off-the-island", fromIsland <= islandReach + 1f,
            $"{fromIsland:F1} m against {islandReach:F0}");

        // And the near zoom is refused where the ground is not built.
        cam.Home();
        yield return Settled();

        // =====================================================================
        // 8. A DOUBLE-CLICK LANDS ON WHAT IT WAS AIMED AT
        // =====================================================================

        cam.ZoomTo(165f);
        yield return Settled();

        Vector2 fp = At(0.36f, 0.64f);
        if (cam.GroundUnder(fp, out Vector3 flyTarget))
        {
            cam.FlyTo(fp);
            yield return Settled();

            float off = ScreenError(flyTarget, At(0.5f, 0.5f));
            sb.AppendLine();
            sb.AppendLine("FLY TO WHAT IS UNDER THE CURSOR:");
            sb.AppendLine($"  it came to rest {off * 100f:F2}% of screen height off the middle, "
                + $"at {cam.Ground:F1} m of ground");
            Gate("a-fly-to-lands-on-the-middle-of-the-frame", off <= 0.02f,
                $"{off * 100f:F2}%");
            Gate("and-it-goes-in-close",
                cam.Ground <= IslandCam.Feel.flyToGround + 2f,
                $"{cam.Ground:F1} m against {IslandCam.Feel.flyToGround:F0}");
        }

        // =====================================================================
        // 9. A THROWN ISLAND COASTS, AND STOPS -- AND A HAND STOPS IT SOONER
        // =====================================================================

        cam.Home();
        cam.ZoomTo(165f);
        yield return Settled();

        yield return Fling(cam, false);
        float coasted = flungFor, ran = flungDistance;
        yield return Fling(cam, true);
        float killed = flungFor, ranKilled = flungDistance;

        sb.AppendLine();
        sb.AppendLine("THROWING THE ISLAND:");
        sb.AppendLine($"  let go: it ran {ran:F1} m and stopped after {coasted:F2} s");
        sb.AppendLine($"  let go and pressed: it ran {ranKilled:F1} m and stopped after {killed:F2} s");
        Gate("a-throw-actually-coasts", ran > 1f, $"{ran:F2} m — nothing carried on");
        Gate("and-stops-inside-a-second-and-a-half", coasted <= 1.5f, $"{coasted:F2} s");
        Gate("and-a-hand-on-the-land-stops-it-at-once",
            killed < 0.2f && ranKilled < ran * 0.5f,
            $"{killed:F2} s, {ranKilled:F2} m against {ran:F2} m");

        // =====================================================================
        // 10. WHAT A GRAB COSTS
        //
        // Measured on `GrabMove` itself, which is the call that happens on
        // every frame a finger is down. The suspect is the height field: the
        // terrain yield samples it, and this project has measured that
        // function at up to 13 us a call.
        // =====================================================================

        cam.ZoomTo(60f);
        yield return Settled();
        Vector2 cp = At(0.5f, 0.5f);
        cam.GrabBegin(cp);
        yield return new WaitForEndOfFrame();

        var watch = new System.Diagnostics.Stopwatch();
        const int costFrames = 60;
        for (int i = 0; i < costFrames; i++)
        {
            Vector2 p = cp + new Vector2(Mathf.Sin(i * 0.4f), Mathf.Cos(i * 0.4f)) * (H * 0.08f);
            watch.Start();
            cam.GrabMove(p);
            watch.Stop();
            yield return new WaitForEndOfFrame();
        }
        cam.GrabEnd();
        float perFrame = (float)watch.Elapsed.TotalMilliseconds / costFrames;

        sb.AppendLine();
        sb.AppendLine($"WHAT IT COSTS: {perFrame * 1000f:F0} us a frame while dragging "
            + $"({watch.Elapsed.TotalMilliseconds:F2} ms over {costFrames} frames)");
        Gate("a-grab-is-cheap", perFrame <= 0.3f, $"{perFrame:F3} ms a frame");

        // --- put it back ------------------------------------------------------
        cam.Home();
        yield return Settled();
        sb.AppendLine();
        sb.AppendLine($"HANDED BACK: {chase.CurrentTilt:F1}° at "
            + $"{(chase.Overview.HasValue ? chase.Overview.Value.ground : -1f):F0} m of ground, "
            + $"hands on {cam.HandsOn}");
        Gate("home-hands-the-shot-back", !cam.HandsOn && Mathf.Abs(chase.CurrentTilt - 32f) < 0.5f,
            $"handsOn {cam.HandsOn}, {chase.CurrentTilt:F2}°");

        Finish();
    }

    // =========================================================================
    // Measurement
    // =========================================================================

    /// Wait until the rendered lens has actually come to rest.
    ///
    /// A frame count is not a substitute. The rig eases with an exponential,
    /// so "60 frames" is 60 frames of an unknown frame time toward a target
    /// it never quite reaches — and an unfocused editor runs at about 10 fps,
    /// which turns one second of waiting into six. This watches the thing
    /// that has to be still: the camera the measurements are projected
    /// through.
    /// **Back to the middle of the island, standing still.**
    ///
    /// Every exactness section starts here, and it has to be re-done for each
    /// one: letting go of a drag THROWS the land (that is `GrabEnd`'s job, and
    /// it has its own gate), so the section before leaves the pivot coasting
    /// toward the reach clamp -- and a hand on land that is pinned against its
    /// own boundary cannot be followed, which reads exactly like broken
    /// maths. Measured: 0.00 % of stray on open ground, 41 % against the clamp.
    System.Collections.IEnumerator ToTheMiddle(IslandCam cam, Island isle)
    {
        cam.KillMotion();
        if (cam.FocusPoint.HasValue)
        {
            Vector3 toMiddle = isle.transform.position - cam.FocusPoint.Value;
            toMiddle.y = 0f;
            cam.PanTo(toMiddle);
        }
        yield return Settled();
    }

    System.Collections.IEnumerator Settled(float within = 0.05f, float giveUp = 6f)
    {
        float t0 = Time.realtimeSinceStartup;
        Vector3 last = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        int still = 0;
        while (Time.realtimeSinceStartup - t0 < giveUp)
        {
            yield return new WaitForEndOfFrame();
            Vector3 now = Camera.main != null ? Camera.main.transform.position : last;
            still = Vector3.Distance(now, last) < within ? still + 1 : 0;
            last = now;
            if (still >= 3) yield break;
        }
    }

    /// How far a world point sits from a screen point, **as a fraction of
    /// screen height**, projected through the camera that actually rendered.
    static float ScreenError(Vector3 world, Vector2 want)
    {
        var c = Camera.main;
        if (c == null) return 99f;
        Vector3 sp = c.WorldToScreenPoint(world);
        if (sp.z <= 0f) return 99f;                 // behind the lens
        return Vector2.Distance(new Vector2(sp.x, sp.y), want)
             / Mathf.Max(1f, Screen.height);
    }

    // The fling measurement, shared by the two legs so the "and a press kills
    // it" leg cannot accidentally be a different throw.
    float flungFor, flungDistance;

    System.Collections.IEnumerator Fling(IslandCam cam, bool pressAfter)
    {
        Vector2 p0 = At(0.5f, 0.5f);
        cam.GrabBegin(p0);
        yield return new WaitForEndOfFrame();
        for (int i = 1; i <= 6; i++)
        {
            cam.GrabMove(p0 + new Vector2(0f, Screen.height * 0.05f * i));
            yield return new WaitForEndOfFrame();
        }
        cam.GrabEnd();
        // Read AFTER `GrabEnd`, because `GrabEnd` regrounds: that moves the
        // pivot off the frozen grab plane and back on to the terrain WITHOUT
        // moving the picture, and counting it as coast would be measuring a
        // bookkeeping step as motion.
        Vector3 letGoAt = cam.Pivot;
        if (pressAfter) cam.KillMotion();

        float t0 = Time.unscaledTime;
        Vector3 last = cam.Pivot;
        float still = 0f;
        flungFor = 0f;
        while (Time.unscaledTime - t0 < 3f)
        {
            yield return null;
            Vector3 now = cam.Pivot;
            float step = Vector3.Distance(now, last);
            last = now;
            if (step < 0.01f) { still += Time.unscaledDeltaTime; if (still > 0.1f) break; }
            else { still = 0f; flungFor = Time.unscaledTime - t0; }
        }
        flungDistance = Vector3.Distance(cam.Pivot, letGoAt);
    }

    void ReportFollowers()
    {
        var streamer = Object.FindFirstObjectByType<SeaSick.Terrain.TerrainStreamer>();
        string lod0 = "(no settings)";
        if (streamer != null && streamer.settings != null)
            lod0 = $"LOD0 out to {streamer.settings.lod0Radius * streamer.settings.chunkSize:F0} m, "
                 + $"colliders to {streamer.settings.colliderRadius * streamer.settings.chunkSize:F0} m";
        sb.AppendLine("  TerrainStreamer     "
            + Name(streamer != null ? streamer.target : null) + "   (" + lod0 + ")");

        var shore = Object.FindFirstObjectByType<SeaSick.Terrain.TerrainShoreField>();
        sb.AppendLine("  TerrainShoreField   " + Name(shore != null ? shore.target : null));

        var horizon = Object.FindFirstObjectByType<SeaSick.Terrain.HorizonField>();
        sb.AppendLine("  HorizonField        " + Name(horizon != null ? horizon.Target : null));

        var clip = Object.FindFirstObjectByType<SeaSick.Ocean.OceanClipmap>();
        sb.AppendLine("  OceanClipmap        "
            + (clip == null ? "(absent)"
               : clip.FollowOverride != null ? Name(clip.FollowOverride)
               : Name(Camera.main != null ? Camera.main.transform : null) + "  (Camera.main)"));

        // SceneryLod keeps its own target private and defers to the
        // streamer's whenever there is one, so it is reported as what it
        // defers TO rather than by reaching into it.
        var lod = Object.FindFirstObjectByType<SeaSick.Terrain.SceneryLod>();
        sb.AppendLine("  SceneryLod          "
            + (lod == null ? "(absent)"
               : streamer != null && streamer.target != null
                   ? Name(streamer.target) + "  (defers to the streamer)"
                   : "Camera.main  (no streamer target to defer to)"));

        sb.AppendLine($"  IslandCam.Feel.nearGroundReach is {IslandCam.Feel.nearGroundReach:F0} m — "
            + "it is only honest while the streamer follows the SHIP.");
    }

    static string Name(Transform t) => t == null ? "(nothing)" : t.name;

    // =========================================================================

    void Gate(string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {(ok ? "" : detail)}");
    }

    void Fail(string why)
    {
        sb.AppendLine("FAIL: " + why);
        Report(sb.ToString());
        Destroy(gameObject);
    }

    void Finish()
    {
        sb.AppendLine();
        sb.AppendLine(fails == 0
            ? "PASS — the land stays under the finger"
            : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    static void Report(string text)
    {
        Debug.Log("IslandCamProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/IslandCamProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }

    static void Warp(ShipMotor motor, Vector3 to, Quaternion facing)
    {
        to.y = motor.transform.position.y;
        var rb = motor.GetComponent<Rigidbody>();
        motor.transform.SetPositionAndRotation(to, facing);
        if (rb != null)
        {
            rb.position = to;
            rb.rotation = facing;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        motor.AnchorPoint = to;
    }
}
