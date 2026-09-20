using System.Text;
using UnityEngine;
using SeaSick.CameraRig;

/// **Pure edit-mode gates for the island view's device reader.** No scene,
/// no play mode — just `TwoFinger.Solve` and `GestureClassifier` fed known
/// numbers, in the same spirit as `LedgerProbe`: the property under test is
/// that the answer does not depend on incidental detail (how tall the
/// screen happens to be) that a player never chose.
public class IslandInputProbe : MonoBehaviour
{
    public static void Execute()
    {
        var sb = new StringBuilder();
        int fails = 0;

        // --- 1. a pure pinch has no twist -------------------------------------

        Vector2 a0 = new Vector2(100f, 200f), b0 = new Vector2(300f, 200f);
        Vector2 a1 = new Vector2(50f, 200f), b1 = new Vector2(350f, 200f);
        TwoFinger.Solve(a0, b0, a1, b1, 1000f,
            out float pinchZoom, out float pinchTwist, out _, out float pinchVert);
        sb.AppendLine($"PURE PINCH (spreading, no rotation): zoom {pinchZoom:F4}, "
            + $"twist {pinchTwist:F4} deg, sharedVertical {pinchVert:F4}");
        Gate(sb, ref fails, "pure-pinch-has-no-twist", Mathf.Abs(pinchTwist) <= 0.1f,
            $"{pinchTwist:F4} deg against a 0.1 deg budget");
        Gate(sb, ref fails, "pinch-spreading-zooms-in", pinchZoom < 1f,
            $"{pinchZoom:F4} (below 1 is in, matching IslandCam.ZoomAt)");

        // --- 2. a pure twist has no zoom ---------------------------------------

        Vector2 mid = new Vector2(200f, 200f);
        Vector2 ta0 = mid + new Vector2(-100f, 0f), tb0 = mid + new Vector2(100f, 0f);
        float rad = 30f * Mathf.Deg2Rad;
        Vector2 ra1 = Rotate(new Vector2(-100f, 0f), rad) + mid;
        Vector2 rb1 = Rotate(new Vector2(100f, 0f), rad) + mid;
        TwoFinger.Solve(ta0, tb0, ra1, rb1, 1000f,
            out float twistZoom, out float twistDeg, out _, out _);
        sb.AppendLine($"PURE TWIST (30 deg about the midpoint): zoom ratio {twistZoom:F5}, "
            + $"twist read back {twistDeg:F2} deg");
        Gate(sb, ref fails, "pure-twist-ratio-is-one",
            Mathf.Abs(twistZoom - 1f) <= 0.001f, $"{twistZoom:F5} against 1 +/- 0.1%");
        Gate(sb, ref fails, "the-twist-itself-reads-back",
            Mathf.Abs(Mathf.Abs(twistDeg) - 30f) < 0.5f, $"{twistDeg:F2} deg against 30");

        // --- 3. shared vertical: agreed motion is a tilt, opposed is not --------

        Vector2 sa0 = new Vector2(150f, 300f), sbb0 = new Vector2(350f, 320f);
        Vector2 sa1 = sa0 + new Vector2(0f, -40f), sb1 = sbb0 + new Vector2(0f, -60f);
        TwoFinger.Solve(sa0, sbb0, sa1, sb1, 1000f, out _, out _, out _, out float sharedDown);
        sb.AppendLine($"TWO FINGERS DOWN TOGETHER (-40 px and -60 px, of 1000): "
            + $"sharedVertical {sharedDown:F4}");
        Gate(sb, ref fails, "shared-vertical-is-detected",
            sharedDown < -0.035f && sharedDown > -0.045f,
            $"{sharedDown:F4} against the smaller of the two (-40/1000 = -0.04)");

        Vector2 oa1 = sa0 + new Vector2(0f, -40f), ob1 = sbb0 + new Vector2(0f, 40f);
        TwoFinger.Solve(sa0, sbb0, oa1, ob1, 1000f, out _, out _, out _, out float sharedOpposite);
        sb.AppendLine($"TWO FINGERS APART VERTICALLY (-40 px, +40 px): "
            + $"sharedVertical {sharedOpposite:F4}");
        Gate(sb, ref fails, "opposed-vertical-motion-is-rejected",
            Mathf.Abs(sharedOpposite) < 1e-5f, $"{sharedOpposite:F5} against exactly 0");

        // --- 4. the classifier does not know how tall the screen is ------------
        //
        // Every threshold is a FRACTION of screenH, so the same fractional
        // press has to classify the same way whether the frame is a phone's
        // 422 px or a desk's 2340.

        bool DragAt(float h)
        {
            var s = new IslandInput.PressSample(Vector2.zero,
                new Vector2(0f, h * (IslandInput.Feel.slopFrac + 0.002f)), 0f, 0.05f, h);
            return GestureClassifier.PastSlop(s);
        }
        bool dragOk = DragAt(422f) && DragAt(2340f);
        sb.AppendLine($"DRAG JUST PAST SLOP: agrees at 422 px ({DragAt(422f)}) "
            + $"and 2340 px ({DragAt(2340f)})");
        Gate(sb, ref fails, "a-drag-past-slop-agrees-at-both-heights", dragOk,
            "both heights must call it a drag");

        bool NoDragAt(float h)
        {
            var s = new IslandInput.PressSample(Vector2.zero,
                new Vector2(0f, h * IslandInput.Feel.slopFrac * 0.3f), 0f, 0.05f, h);
            return !GestureClassifier.PastSlop(s);
        }
        bool tapOk = NoDragAt(422f) && NoDragAt(2340f);
        sb.AppendLine($"A TAP-SIZED WOBBLE: no-drag agrees at 422 px ({NoDragAt(422f)}) "
            + $"and 2340 px ({NoDragAt(2340f)})");
        Gate(sb, ref fails, "a-tap-sized-wobble-agrees-at-both-heights", tapOk,
            "neither height may call it a drag");

        bool LongPressAt(float h)
        {
            var s = new IslandInput.PressSample(Vector2.zero,
                new Vector2(0f, h * IslandInput.Feel.slopFrac * 0.1f),
                0f, IslandInput.Feel.longPressSeconds + 0.05f, h);
            return GestureClassifier.IsLongPress(s);
        }
        bool longOk = LongPressAt(422f) && LongPressAt(2340f);
        sb.AppendLine($"LONG PRESS past the timer, inside slop: agrees at 422 px "
            + $"({LongPressAt(422f)}) and 2340 px ({LongPressAt(2340f)})");
        Gate(sb, ref fails, "a-long-press-agrees-at-both-heights", longOk,
            "both heights must call it a long press once the timer is past");

        bool DoubleTapAt(float h)
        {
            Vector2 second = new Vector2(0f, h * IslandInput.Feel.doubleTapFrac * 0.5f);
            return GestureClassifier.IsDoubleTap(Vector2.zero, 0f, second,
                IslandInput.Feel.doubleTapSeconds - 0.05f, h);
        }
        bool doubleOk = DoubleTapAt(422f) && DoubleTapAt(2340f);
        sb.AppendLine($"DOUBLE-TAP TIMING, inside both windows: agrees at 422 px "
            + $"({DoubleTapAt(422f)}) and 2340 px ({DoubleTapAt(2340f)})");
        Gate(sb, ref fails, "a-double-tap-agrees-at-both-heights", doubleOk,
            "both heights must call the second tap a double");

        // A double-tap too far apart, fractionally, must be rejected at
        // both heights too -- the same threshold has to hold both ways.
        bool NotDoubleTapAt(float h)
        {
            Vector2 second = new Vector2(0f, h * IslandInput.Feel.doubleTapFrac * 3f);
            return !GestureClassifier.IsDoubleTap(Vector2.zero, 0f, second,
                IslandInput.Feel.doubleTapSeconds - 0.05f, h);
        }
        bool notDoubleOk = NotDoubleTapAt(422f) && NotDoubleTapAt(2340f);
        sb.AppendLine($"TWO TAPS TOO FAR APART: rejected at 422 px "
            + $"({NotDoubleTapAt(422f)}) and 2340 px ({NotDoubleTapAt(2340f)})");
        Gate(sb, ref fails, "a-too-far-second-tap-agrees-at-both-heights", notDoubleOk,
            "both heights must refuse the double");

        // --- 5. the wheel round-trips -------------------------------------------

        foreach (float n in new[] { 1f, 3f, 7.5f, -2f })
        {
            float forward = GestureClassifier.WheelFactor(n);
            float back = GestureClassifier.WheelFactor(-n);
            float roundTrip = forward * back;
            sb.AppendLine($"WHEEL {n:F1} detents in, then the same number out: "
                + $"{forward:F5} * {back:F5} = {roundTrip:F6}");
            Gate(sb, ref fails, $"wheel-round-trips-at-{n:F1}-detents",
                Mathf.Abs(roundTrip - 1f) <= 1e-4f, $"{roundTrip:F6} against 1 +/- 1e-4");
        }

        sb.AppendLine();
        sb.AppendLine(fails == 0
            ? "PASS -- the classifier answers the same wherever the screen ends"
            : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
    }

    static Vector2 Rotate(Vector2 v, float rad)
        => new Vector2(v.x * Mathf.Cos(rad) - v.y * Mathf.Sin(rad),
                        v.x * Mathf.Sin(rad) + v.y * Mathf.Cos(rad));

    static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {detail}");
    }

    static void Report(string text)
    {
        Debug.Log("IslandInputProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/IslandInputProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
