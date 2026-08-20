using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

/// Measures whether the storm now LOOKS like one, and proves the camera fix.
///
/// The camera A/B runs back to back at the same spot in the same play session,
/// because sea state drifts over minutes and runs from separate sessions are
/// not comparable (identical code has scored 83%, 50% and 17% before now).
///
/// Runs after ChaseCamera so it samples the settled rig, not a half-applied one.
[DefaultExecutionOrder(500)]
public class StormPicture : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-stormpicture.txt";
    public static float West = 1500f;

    SeaSick.Ship.ShipMotor motor;
    SeaSick.CameraRig.ChaseCamera chase;
    SeaSick.World.SkyDirector sky;
    Camera cam;
    Vector3 hold;
    bool sampling;

    // running stats
    int n;
    float vpMin, vpMax, vpSum, vpSumSq;
    float gapMin, gapMax, gapSum;
    float shipMin, shipMax;
    float stormSum;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("StormPicture: not in play mode"); return; }
        var old = FindAnyObjectByType<StormPicture>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("StormPicture").AddComponent<StormPicture>();
    }

    static void SetPrivate(object o, string field, object v)
    {
        var f = o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) { Debug.LogWarning($"StormPicture: no field {field}"); return; }
        f.SetValue(o, v);
    }

    void Reset()
    {
        n = 0; vpSum = vpSumSq = gapSum = stormSum = 0f;
        vpMin = gapMin = shipMin = 99999f;
        vpMax = gapMax = shipMax = -99999f;
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        chase = FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>();
        sky = SeaSick.World.SkyDirector.Instance;
        cam = Camera.main;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null || chase == null || cam == null)
        {
            Debug.LogError("StormPicture: missing ship/camera"); yield break;
        }

        var sb = new StringBuilder();
        var field = SeaSick.Ocean.WaveField.Instance;

        // ---- deep in the western storm ----
        hold = home + new Vector3(-West, 0f, 0f);
        motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);
        yield return new WaitForSeconds(6f);

        Vector2 p = new Vector2(hold.x, hold.z);
        sb.AppendLine($"WHERE  {West}m west of home");
        if (field != null)
            sb.AppendLine($"SEA    storm {field.StormAmount01(p):F2}  region {field.RegionScale(p):F2}  " +
                          $"seaState {field.SeaState01:F2}  severity {field.SeaSeverity01(p, Time.time):F2}");
        sb.AppendLine($"SKY    storminess {(sky != null ? sky.Storminess01 : -1f):F2}");
        sb.AppendLine($"LIGHT  sun {RenderSettings.sun?.intensity:F2} {ColorStr(RenderSettings.sun != null ? RenderSettings.sun.color : Color.black)}" +
                      $"  ambientSky {ColorStr(RenderSettings.ambientSkyColor)}");
        sb.AppendLine($"FOG    {RenderSettings.fogStartDistance:F0} .. {RenderSettings.fogEndDistance:F0}  {ColorStr(RenderSettings.fogColor)}");
        sb.AppendLine($"SKYMAT {RenderSettings.skybox?.shader?.name}  overcast " +
                      $"{(RenderSettings.skybox != null ? RenderSettings.skybox.GetFloat("_Overcast") : -1f):F2}" +
                      $"  scud {(RenderSettings.skybox != null ? RenderSettings.skybox.GetFloat("_Scud") : -1f):F2}");

        // ---- camera A/B, same spot, back to back ----
        yield return Window(sb, "OLD RIG (pinned to mean sea level)", 0f, 0f, 0f);
        yield return Window(sb, "NEW RIG (rides the swell, drops in a storm)", 1f, 9f, 6f);

        // ---- spray ----
        var spindrift = GameObject.Find("Spindrift")?.GetComponent<ParticleSystem>();
        var mist = GameObject.Find("SeaMist")?.GetComponent<ParticleSystem>();
        var spray = motor.GetComponent<SeaSick.Ocean.StormSpray>();
        sb.AppendLine();
        sb.AppendLine($"SPRAY  spindrift {(spindrift != null ? spindrift.particleCount : -1)} alive  " +
                      $"mist {(mist != null ? mist.particleCount : -1)} alive");
        if (spray != null)
        {
            int before = spray.Emitted;
            yield return new WaitForSeconds(2f);
            sb.AppendLine($"       sea mean {spray.SeaMean:F1}m  crest {spray.SeaCrest:F1}m  " +
                          $"threshold {spray.CrestThreshold:F1}m");
            sb.AppendLine($"       torn off the tops: {((spray.Emitted - before) / 2f):F0} particles/s");
        }

        yield return new WaitForSeconds(1.5f);
        Shoot("/tmp/seasick-sky-storm.png");
        yield return new WaitForSeconds(2.5f);
        Shoot("/tmp/seasick-sky-storm2.png");
        yield return new WaitForSeconds(2.5f);
        Shoot("/tmp/seasick-sky-storm3.png");
        yield return new WaitForSeconds(1.5f);

        // ---- home water, for the comparison ----
        hold = home + new Vector3(-120f, 0f, 0f);
        motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);
        yield return new WaitForSeconds(9f);
        Vector2 hp = new Vector2(hold.x, hold.z);
        sb.AppendLine();
        sb.AppendLine($"HOME   120m out — storm {(field != null ? field.StormAmount01(hp) : -1f):F2}  " +
                      $"storminess {(sky != null ? sky.Storminess01 : -1f):F2}");
        sb.AppendLine($"       fog {RenderSettings.fogStartDistance:F0}..{RenderSettings.fogEndDistance:F0}  " +
                      $"sun {RenderSettings.sun?.intensity:F2}  " +
                      $"overcast {(RenderSettings.skybox != null ? RenderSettings.skybox.GetFloat("_Overcast") : -1f):F2}");
        Shoot("/tmp/seasick-sky-home.png");

        System.IO.File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("StormPicture: done\n" + sb);
    }

    IEnumerator Window(StringBuilder sb, string label, float share, float drop, float pull)
    {
        SetPrivate(chase, "heaveShare", share);
        SetPrivate(chase, "stormDrop", drop);
        SetPrivate(chase, "stormPullIn", pull);
        yield return new WaitForSeconds(3f);   // let the rig settle before sampling

        Reset();
        sampling = true;
        yield return new WaitForSeconds(9f);
        sampling = false;

        float mean = n > 0 ? vpSum / n : 0f;
        float var = n > 0 ? Mathf.Max(0f, vpSumSq / n - mean * mean) : 0f;
        sb.AppendLine();
        sb.AppendLine($"-- {label}  ({n} samples)");
        // Normalised, because the sea drifts: the first run had 4.2m of heave
        // in one window and 12.4m in the next, which made the raw spreads
        // uncomparable. Spread per metre of heave is the honest number.
        float heave = Mathf.Max(0.1f, shipMax - shipMin);
        sb.AppendLine($"   ship in frame (viewport y): {vpMin:F3} .. {vpMax:F3}   " +
                      $"spread {(vpMax - vpMin):F3}  sd {Mathf.Sqrt(var):F3}");
        sb.AppendLine($"   spread PER METRE of heave:  {((vpMax - vpMin) / heave):F4}   " +
                      $"<- the comparable one");
        sb.AppendLine($"   camera above ship:          {gapMin:F1} .. {gapMax:F1} m  mean {(n > 0 ? gapSum / n : 0f):F1} m");
        sb.AppendLine($"   ship heave over the window: {(shipMax - shipMin):F1} m " +
                      $"({shipMin:F1} .. {shipMax:F1})");
        sb.AppendLine($"   storminess: {(n > 0 ? stormSum / n : 0f):F2}");
    }

    void Shoot(string path)
    {
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
    }

    static string ColorStr(Color c) => $"({c.r:F2},{c.g:F2},{c.b:F2})";

    void LateUpdate()
    {
        if (motor == null) return;
        Vector3 mp = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, mp.y, hold.z);
        if (!sampling || cam == null) return;

        float shipY = motor.transform.position.y;
        float camY = cam.transform.position.y;
        float vp = cam.WorldToViewportPoint(motor.transform.position).y;

        n++;
        vpSum += vp; vpSumSq += vp * vp;
        if (vp < vpMin) vpMin = vp;
        if (vp > vpMax) vpMax = vp;
        float gap = camY - shipY;
        gapSum += gap;
        if (gap < gapMin) gapMin = gap;
        if (gap > gapMax) gapMax = gap;
        if (shipY < shipMin) shipMin = shipY;
        if (shipY > shipMax) shipMax = shipY;
        stormSum += sky != null ? sky.Storminess01 : 0f;
    }
}
