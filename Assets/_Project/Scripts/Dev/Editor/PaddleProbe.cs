using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.Ocean;

/// The paddle boat's acceptance gate. Four legs, each measuring the thing it
/// claims to measure and nothing else.
///
/// 1. FLOAT, light. Pinned in a calm with an empty hold: resting draft, and
///    how much freeboard is left under the deck edge and the rail.
/// 2. FLOAT, laden. The same with a full hold, because the freeboard sink
///    amounts were authored for a 21 m sloop and will drown a 12 m boat if
///    they are carried over unscaled.
/// 3. DRIVE. Throttle open, straight: do the wheels turn at a rate that
///    matches the way she is making, or are they spinning free.
/// 4. HELM. Full helm at speed, then full helm with the throttle shut: the
///    wheels must be genuinely opposed and she must come round on the spot.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-paddle.txt. Plain C# for Coplay.
public class PaddleProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("PaddleProbe: not in play mode"); return; }
        new GameObject("PaddleProbe").AddComponent<PaddleProbe>();
    }

    // Measured off the model at import scale 3.4, relative to the ship
    // origin: deck amidships, rail top, keel. Re-measure these with
    // InspectBoatParts whenever SetupPaddleBoat.Scale changes.
    const float DeckY = 1.34f;
    const float RailTopY = 4.22f;
    const float KeelY = -1.02f;

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        ShipMotor motor = FindAnyObjectByType<ShipMotor>();
        PaddleDrive drive = FindAnyObjectByType<PaddleDrive>();
        SeaStateController sea = SeaStateController.Instance;
        if (motor == null) { Debug.LogError("PaddleProbe: no ShipMotor"); yield break; }
        Rigidbody rb = motor.GetComponent<Rigidbody>();

        // HelmInput reasserts Rudder and ThrottleOrder every frame (the recorded
        // trap). The first run of this probe measured a differential of
        // exactly 0.00 and a yaw of exactly 0.0 for that reason alone — the
        // probe was setting the helm and HelmInput was putting it straight
        // back. Silence it while the probe owns the ship.
        HelmInput helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;

        // Flat water and a pinned phase, so a draft number is a draft number.
        if (sea != null) sea.ForceSeverity(0f);
        OceanTime.Paused = true;
        OceanTime.Scrub(500f);
        yield return new WaitForSeconds(4f);

        sb.AppendLine("PaddleProbe — the paddle boat in the water");
        sb.AppendLine("deck edge " + DeckY.ToString("F2") + " m, rail top " + RailTopY.ToString("F2")
            + " m, keel " + KeelY.ToString("F2") + " m above the ship origin");
        sb.AppendLine("mass " + (rb != null ? rb.mass : 0f).ToString("F0") + " kg");
        sb.AppendLine();

        // ---- leg 1: floating light ----
        motor.CargoLoad = 0f;
        motor.BilgeLoad01 = 0f;
        motor.ThrottleOrder = 0f;
        yield return new WaitForSeconds(6f);
        float lightY = 0f, lightSub = 0f;
        for (int i = 0; i < 60; i++)
        {
            yield return null;
            lightY += motor.transform.position.y;
            lightSub += Submersion(motor);
        }
        lightY /= 60f; lightSub /= 60f;
        float lightSurface = SurfaceAt(motor);
        sb.AppendLine("LIGHT (empty hold)");
        Report(sb, motor, lightY, lightSurface, lightSub);

        // ---- leg 2: floating laden ----
        motor.CargoLoad = 1f;
        yield return new WaitForSeconds(6f);
        float ladenY = 0f, ladenSub = 0f;
        for (int i = 0; i < 60; i++)
        {
            yield return null;
            ladenY += motor.transform.position.y;
            ladenSub += Submersion(motor);
        }
        ladenY /= 60f; ladenSub /= 60f;
        sb.AppendLine("LADEN (full hold)");
        Report(sb, motor, ladenY, SurfaceAt(motor), ladenSub);
        sb.AppendLine("  sinkDepth applied " + motor.SinkDepth.ToString("F2") + " m");
        motor.CargoLoad = 0f;

        // ---- leg 3: the wheels drive her ----
        OceanTime.Paused = false;
        motor.ThrottleOrder = 1f;
        motor.Rudder = 0f;
        yield return new WaitForSeconds(14f);
        float speed = motor.CurrentSpeed;
        float pr = drive != null ? drive.PortRate : 0f;
        float sr = drive != null ? drive.StarboardRate : 0f;
        sb.AppendLine();
        sb.AppendLine("DRIVE (throttle open, helm amidships)");
        sb.AppendLine("  way " + speed.ToString("F2") + " m/s");
        sb.AppendLine("  wheel rates port " + pr.ToString("F2") + " stbd " + sr.ToString("F2") + " rad/s");
        float radius = drive != null ? drive.WheelRadius : 1f;
        sb.AppendLine("  rim speed " + (pr * radius).ToString("F2")
            + " m/s against " + speed.ToString("F2") + " m/s of way (radius "
            + radius.ToString("F2") + " m)");

        // ---- leg 4: helm ----
        motor.Rudder = 1f;
        yield return new WaitForSeconds(4f);
        float turnPort = drive != null ? drive.PortRate : 0f;
        float turnStbd = drive != null ? drive.StarboardRate : 0f;
        float yawUnder = rb != null ? rb.angularVelocity.y * Mathf.Rad2Deg : 0f;
        sb.AppendLine();
        sb.AppendLine("HELM HARD OVER, making way");
        sb.AppendLine("  port " + turnPort.ToString("F2") + " stbd " + turnStbd.ToString("F2")
            + " rad/s, difference " + (turnPort - turnStbd).ToString("F2"));
        sb.AppendLine("  yaw " + yawUnder.ToString("F1") + " deg/s");

        // Throttle shut, helm still hard over: she should turn in her length.
        motor.ThrottleOrder = 0f;
        yield return new WaitForSeconds(10f);
        float spinPort = drive != null ? drive.PortRate : 0f;
        float spinStbd = drive != null ? drive.StarboardRate : 0f;
        float yawSpin = rb != null ? rb.angularVelocity.y * Mathf.Rad2Deg : 0f;
        sb.AppendLine();
        sb.AppendLine("HELM HARD OVER, throttle shut (spin on the spot)");
        sb.AppendLine("  port " + spinPort.ToString("F2") + " stbd " + spinStbd.ToString("F2") + " rad/s");
        sb.AppendLine("  opposed: " + ((spinPort * spinStbd) < 0f ? "YES" : "no"));
        sb.AppendLine("  way " + motor.CurrentSpeed.ToString("F2") + " m/s, yaw " + yawSpin.ToString("F1") + " deg/s");

        motor.Rudder = 0f;
        if (sea != null) sea.ReleaseForce();

        // ---- gates ----
        float lightFreeboard = DeckY - (lightSurface - lightY);
        float ladenFreeboard = DeckY - (SurfaceAt(motor) - ladenY);
        bool gFloat = lightSub > 0.35f && lightSub < 0.80f;
        bool gLight = lightFreeboard > 0.55f;
        bool gLaden = ladenFreeboard > 0.20f;
        bool gDrive = speed > 3f;
        bool gDiff = Mathf.Abs(turnPort - turnStbd) > 0.5f;
        bool gSpin = (spinPort * spinStbd) < 0f && Mathf.Abs(yawSpin) > 4f;

        sb.AppendLine();
        sb.AppendLine("GATES");
        Gate(sb, "float-fraction    ", lightSub, "0.35..0.80", gFloat);
        Gate(sb, "freeboard light   ", lightFreeboard, "> 0.55 m", gLight);
        Gate(sb, "freeboard laden   ", ladenFreeboard, "> 0.20 m", gLaden);
        Gate(sb, "makes way         ", speed, "> 3 m/s", gDrive);
        Gate(sb, "wheels differential", turnPort - turnStbd, "> 0.5 rad/s", gDiff);
        Gate(sb, "spins on the spot ", yawSpin, "opposed and > 4 deg/s", gSpin);
        sb.AppendLine();
        sb.AppendLine(gFloat && gLight && gLaden && gDrive && gDiff && gSpin ? "ALL PASS" : "FAIL");

        System.IO.File.WriteAllText("/tmp/seasick-paddle.txt", sb.ToString());
        Debug.Log("PaddleProbe:\n" + sb);
        Destroy(gameObject);
    }

    static void Gate(StringBuilder sb, string name, float value, string want, bool ok)
    {
        sb.AppendLine("  " + name + " " + value.ToString("F2").PadLeft(8) + "   want " + want.PadRight(22) + (ok ? "PASS" : "FAIL"));
    }

    void Report(StringBuilder sb, ShipMotor motor, float shipY, float surfaceY, float sub)
    {
        float draftLine = surfaceY - shipY;   // where the water sits in ship coords
        sb.AppendLine("  ship y " + shipY.ToString("F2") + ", surface y " + surfaceY.ToString("F2"));
        sb.AppendLine("  waterline at " + draftLine.ToString("F2") + " m in ship coords");
        sb.AppendLine("  draft " + (draftLine - KeelY).ToString("F2")
            + " m, freeboard to deck " + (DeckY - draftLine).ToString("F2")
            + " m, to rail " + (RailTopY - draftLine).ToString("F2") + " m");
        sb.AppendLine("  submersion " + sub.ToString("F3"));
    }

    static float Submersion(ShipMotor motor)
    {
        BuoyantBody b = motor.GetComponent<BuoyantBody>();
        return b != null ? b.Submersion : 0f;
    }

    static float SurfaceAt(ShipMotor motor)
    {
        if (!OceanSampler.Ready) return 0f;
        return OceanSampler.SampleImmediate(motor.transform.position).height;
    }
}
