using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// CAN SHE RIDE IT. WaveSizeProbe says what the sea is; this says what the
/// ship does in it. Finds genuine deep water (the same depth ladder, because
/// a probe on a shelf measures the depth limit and not the storm), forces the
/// storm, sails her west across the swell for a minute and reports:
///
///   pitch and roll distributions against ShipMotor's SOFT LIMITS, and how
///     often those limits are actually pushing back -- the limits were
///     authored for a 9 m sea and a 25 degree wave face will ask for more
///     attitude than they allow, which reads as ploughing into a face
///     instead of riding up it;
///   the chase camera's height over the water and how often its 2.6 m floor
///     is what is holding it up;
///   speed against her own maximum, since a long face is a gravity ramp and
///     she can be pushed past hull speed going down one.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-ride.txt.
public class RideProbe : MonoBehaviour
{
    static readonly float[] DepthLadder = { -110f, -60f, -30f, -9f };
    const float PatchHalf = 800f;
    const float RunSeconds = 60f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("RideProbe: not in play mode"); return; }
        new GameObject("RideProbe").AddComponent<RideProbe>();
    }

    static bool DeepEverywhere(Vector3 c, float half, float need)
    {
        var h = Island.TerrainHeight;
        if (h == null) return false;
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
                if (h(c.x + i * half * 0.5f, c.z + j * half * 0.5f) > need) return false;
        return true;
    }

    static float Pct(float[] a, float p)
    {
        if (a.Length == 0) return 0f;
        int i = Mathf.Clamp(Mathf.RoundToInt(p * (a.Length - 1)), 0, a.Length - 1);
        return a[i];
    }

    static float Signed(float deg) => deg > 180f ? deg - 360f : deg;

    /// A serialised private float off a component, so the probe reports
    /// against the value the scene is actually running.
    static float ReadFloat(Object c, string field, float fallback)
    {
        if (c == null) return fallback;
        var f = c.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);
        return f != null ? (float)f.GetValue(c) : fallback;
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        var ocean = OceanRenderer.Instance;
        var pc = Resources.Load<OceanQuality>("Ocean/OceanQuality_PC");
        if (ocean != null && pc != null)
        {
            OceanQuality.Override(pc);
            ocean.enabled = false; ocean.enabled = true;
            var cl = FindAnyObjectByType<OceanClipmap>();
            if (cl != null) cl.Build();
            yield return null;
        }

        var motor = FindAnyObjectByType<ShipMotor>();
        var helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;
        var sea = SeaStateController.Instance;
        var rb = motor != null ? motor.GetComponent<Rigidbody>() : null;
        var cam = Camera.main;
        if (motor == null || rb == null) { Debug.LogError("RideProbe: no ship"); yield break; }

        Vector3 spot = Vector3.zero;
        bool found = false; float need = 0f;
        for (int d = 0; d < DepthLadder.Length && !found; d++)
        {
            need = DepthLadder[d];
            for (float dist = 2000f; dist <= 20000f && !found; dist += 250f)
                for (int b = 0; b < 16 && !found; b++)
                {
                    float a = b / 16f * Mathf.PI * 2f;
                    Vector3 c = new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist);
                    if (DeepEverywhere(c, PatchHalf, need)) { spot = c; found = true; }
                }
        }
        if (!found) { Debug.LogError("RideProbe: no deep water"); yield break; }

        rb.position = new Vector3(spot.x, 3f, spot.z);
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        if (sea != null) sea.ForceSeverity(1f);
        motor.SailOrder = 1f;
        yield return new WaitForSeconds(14f);          // let the storm land

        // Head west, into the swell (it travels +X, so west is bow-on).
        motor.AutopilotTarget = rb.position + new Vector3(-8000f, 0f, 0f);

        var pitches = new System.Collections.Generic.List<float>();
        var rolls = new System.Collections.Generic.List<float>();
        var camAbove = new System.Collections.Generic.List<float>();
        var speeds = new System.Collections.Generic.List<float>();
        int pitchAtLimit = 0, rollAtLimit = 0, camAtFloor = 0, n = 0;
        // READ the limits, never hardcode them. The first version of this
        // probe baked in 16 and 20, so after pitchLimit was raised to 26 it
        // went on cheerfully reporting "past the limit on 11.4% of frames"
        // against a limit that no longer existed -- a probe describing the
        // code it was written against instead of the code being measured.
        float PitchLimit = ReadFloat(motor, "pitchLimit", 16f);
        float RollLimit = ReadFloat(motor, "rollLimit", 20f);
        float CamFloor = cam != null
            ? ReadFloat(cam.GetComponent<SeaSick.CameraRig.ChaseCamera>()
                ?? FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>(),
                "minHeightAboveWater", 2.6f)
            : 2.6f;

        float t = 0f;
        while (t < RunSeconds)
        {
            t += Time.deltaTime;
            float p = Mathf.Abs(Signed(motor.transform.eulerAngles.x));
            float r = Mathf.Abs(Signed(motor.transform.eulerAngles.z));
            pitches.Add(p); rolls.Add(r);
            if (p > PitchLimit) pitchAtLimit++;
            if (r > RollLimit) rollAtLimit++;
            speeds.Add(rb.linearVelocity.magnitude);
            if (cam != null)
            {
                float surf = OceanSampler.SampleImmediate(cam.transform.position).height;
                float above = cam.transform.position.y - surf;
                camAbove.Add(above);
                if (above <= CamFloor + 0.15f) camAtFloor++;
            }
            n++;
            yield return null;
        }

        var pa = pitches.ToArray(); System.Array.Sort(pa);
        var ra = rolls.ToArray(); System.Array.Sort(ra);
        var ca = camAbove.ToArray(); System.Array.Sort(ca);
        var sa = speeds.ToArray(); System.Array.Sort(sa);

        sb.AppendLine("RideProbe -- what the ship does in the storm sea");
        sb.AppendLine("deep water at " + spot.ToString("F0") + " (seabed below "
            + need.ToString("F0") + " m), severity forced 1.00, "
            + RunSeconds.ToString("F0") + " s bow-on to the swell, " + n + " samples");
        sb.AppendLine();
        sb.AppendLine(string.Format("pitch   median {0,5:F1}   p90 {1,5:F1}   max {2,5:F1} deg   (soft limit {3:F0})",
            Pct(pa, 0.5f), Pct(pa, 0.9f), pa[pa.Length - 1], PitchLimit));
        sb.AppendLine(string.Format("        past the limit on {0:F1}% of frames", 100f * pitchAtLimit / n));
        sb.AppendLine(string.Format("roll    median {0,5:F1}   p90 {1,5:F1}   max {2,5:F1} deg   (soft limit {3:F0})",
            Pct(ra, 0.5f), Pct(ra, 0.9f), ra[ra.Length - 1], RollLimit));
        sb.AppendLine(string.Format("        past the limit on {0:F1}% of frames", 100f * rollAtLimit / n));
        sb.AppendLine();
        sb.AppendLine(string.Format("camera above water  median {0,5:F1}   p10 {1,5:F1}   min {2,5:F1} m   (floor {3:F1})",
            Pct(ca, 0.5f), Pct(ca, 0.1f), ca.Length > 0 ? ca[0] : 0f, CamFloor));
        sb.AppendLine(string.Format("        on the floor for {0:F1}% of frames", 100f * camAtFloor / n));
        sb.AppendLine();
        sb.AppendLine(string.Format("speed   median {0,5:F1}   p90 {1,5:F1}   max {2,5:F1} m/s",
            Pct(sa, 0.5f), Pct(sa, 0.9f), sa[sa.Length - 1]));
        sb.AppendLine();
        sb.AppendLine("A face of 12 deg typical and 25 deg at the biggest is what she has to");
        sb.AppendLine("follow. Pitch pinned at the limit means the spring is holding her bow");
        sb.AppendLine("down into a face she should be riding up.");

        motor.AutopilotTarget = null;
        if (sea != null) sea.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-ride.txt", sb.ToString());
        Debug.Log("RideProbe:\n" + sb);
        Destroy(gameObject);
    }
}
