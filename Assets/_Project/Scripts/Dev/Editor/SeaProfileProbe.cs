using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.World;

/// WHERE is the sea mountainous, and where does it lie down. Walks west from
/// home in steps and reports, at each range: the seabed, the region envelope,
/// which term of the envelope is binding, and the Hs actually measured there.
///
/// This exists because "mountainous seas out west only" is a claim about
/// GEOGRAPHY, and every other probe measures one spot. It is also the check
/// on the depth-limited envelope: that term is supposed to lie the sea down
/// over shallow water and do nothing in the deep, and the only way to know it
/// is not quietly flattening water the player sails through is to look along
/// the whole route.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-seaprofile.txt.
public class SeaProfileProbe : MonoBehaviour
{
    const float StepM = 400f;
    const float MaxRange = 9000f;
    const int PatchSide = 25;      // 25 x 25 samples
    const float PatchStep = 40f;   // over 1000 m

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SeaProfileProbe: not in play mode"); return; }
        new GameObject("SeaProfileProbe").AddComponent<SeaProfileProbe>();
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

        var sea = SeaStateController.Instance;
        var region = RegionField.Instance;
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;
        var rb = motor != null ? motor.GetComponent<Rigidbody>() : null;
        if (motor != null) { motor.SailOrder = 0f; motor.Rudder = 0f; }
        if (sea != null) sea.ForceSeverity(1f);

        var terrain = Island.TerrainHeight;
        float nominal = ocean != null && ocean.Settings != null ? ocean.Settings.nominalHs : 0f;

        sb.AppendLine("SeaProfileProbe -- how the sea builds on the way west");
        sb.AppendLine("severity FORCED to 1.00 everywhere, so this shows the ENVELOPE and the");
        sb.AppendLine("bathymetry, not the weather ramp. nominalHs of the sea in force: "
            + nominal.ToString("F1") + " m");
        sb.AppendLine("The shore grid follows the SHIP, so the ship is moved to each station.");
        sb.AppendLine();
        sb.AppendLine("  range      seabed   envelope   measured Hs   binding term");

        for (float r = 0f; r <= MaxRange; r += StepM)
        {
            Vector3 spot = new Vector3(-r, 0f, 0f);
            if (rb != null)
            {
                rb.position = new Vector3(spot.x, 2f, spot.z);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            // The terrain streamer and the shore grid both centre on the ship,
            // so they need a moment to catch up before the envelope is real.
            yield return new WaitForSeconds(2.5f);

            float bed = terrain != null ? terrain(spot.x, spot.z) : 0f;
            float env = region != null ? region.Evaluate(new Vector2(spot.x, spot.z)) : 1f;

            int n = PatchSide * PatchSide;
            var q = new NativeArray<float3>(n, Allocator.TempJob);
            var res = new NativeArray<OceanSample>(n, Allocator.TempJob);
            for (int i = 0; i < PatchSide; i++)
                for (int j = 0; j < PatchSide; j++)
                    q[i * PatchSide + j] = new float3(
                        spot.x + (i - PatchSide / 2) * PatchStep, 0f,
                        spot.z + (j - PatchSide / 2) * PatchStep);
            OceanSampler.SampleBatch(q, res, default).Complete();
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++) { float h = res[i].height; sum += h; sumSq += (double)h * h; }
            q.Dispose(); res.Dispose();
            float mean = (float)(sum / n);
            float hs = 4f * Mathf.Sqrt(Mathf.Max(0f, (float)(sumSq / n) - mean * mean));

            // Which term is holding the envelope down?
            float depth = -bed;
            float cap = nominal > 0.01f ? 0.55f * depth / nominal : 999f;
            string why = "open sea";
            if (cap < env + 0.001f && cap < 0.999f) why = "DEPTH LIMIT (" + depth.ToString("F0") + " m of water)";
            else if (env < 0.35f) why = "shore/island falloff";
            else if (env < 0.95f) why = "shelf or region";

            sb.AppendLine(string.Format("{0,7:F0} m   {1,7:F0} m   {2,7:F2}   {3,8:F1} m     {4}",
                r, bed, env, hs, why));
        }

        if (sea != null) sea.ReleaseForce();
        sb.AppendLine();
        sb.AppendLine("Measured Hs is over a 1000 m patch, so it is noisy for a 500 m sea");
        sb.AppendLine("(see the WaveSizeProbe note); read the SHAPE of the column, not each row.");

        System.IO.File.WriteAllText("/tmp/seasick-seaprofile.txt", sb.ToString());
        Debug.Log("SeaProfileProbe:\n" + sb);
        Destroy(gameObject);
    }
}
