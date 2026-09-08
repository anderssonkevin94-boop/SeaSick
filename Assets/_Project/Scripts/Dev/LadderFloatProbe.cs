using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// Does every rung of the ladder float where she was drawn?
///
/// Sixteen of the twenty hulls are interpolated and have never been in water.
/// Their displacements match the four authored anchors exactly, which is a
/// strong reason to expect them to float — and not the same thing as having
/// floated them. This is the gate before any game system is wired to them.
///
/// It deliberately drives the REAL path: each rig is a Rigidbody with a
/// `Shipyard` on it and the probe calls `Apply(i)`, the same call the upgrade
/// buttons make. A probe that builds its own parallel rig can pass while the
/// game is broken.
///
/// The second question it answers is whether the rig each rung is given
/// actually IS her. `HydrostaticLayout` solves the probes from her measured
/// volume, waterplane, second moment and centre of buoyancy; pass 2 reads all
/// four back off the array that was built and prints them against her book.
/// That check used to be a solve for `probe_lift` -- the 0.55 x draft fudge
/// that made her float. Nothing is solved now: she floats because the rig
/// displaces what she displaces, so the honest gate is whether it does.
public class LadderFloatProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("LadderFloatProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<LadderFloatProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("LadderFloatProbe").AddComponent<LadderFloatProbe>();
    }

    const float Gap = 6f;
    /// As flat as the controller will go. The first run used 1.2 m and read
    /// scatter of +/-0.3 m on every rung, which is not float error -- it is the
    /// wave. A hull sitting on a 1.2 m sea is 0.6 m up and 0.6 m down.
    const float CalmHs = 0.25f;
    const float Tolerance = 0.03f;      // metres of waterline error we accept
    /// Long enough to average WHOLE WAVE PERIODS. One second was about a fifth
    /// of one, so pass 1 measured noise and pass 2 then spent three iterations
    /// chasing it -- which is how a probe reports a 0.03 to 1.03 spread on a
    /// rule that is actually holding.
    const float SampleSecs = 18f;

    class Rig
    {
        public LadderNode n;
        public Shipyard yard;
        public Rigidbody rb;
        public BuoyancyProbeSet probes;
        double sum, sumSq; int count;

        public void Reset() { sum = 0; sumSq = 0; count = 0; }

        /// Mean height of the hull's ORIGIN above the water under her. The FBX
        /// origin is the drawn waterline amidships, so a hull floating where
        /// she was drawn reads zero — no reference marks, no eyeballing.
        public void Sample()
        {
            var p = rb.position;
            double e = p.y - OceanSampler.SampleImmediate(p).height;
            sum += e; sumSq += e * e;
            count++;
        }

        public float MeanErr => count > 0 ? (float)(sum / count) : 0f;

        /// Reported alongside the mean so a real bias can be told from wave
        /// noise. A mean of 0.01 with a spread of 0.30 says nothing at all.
        public float Spread
        {
            get
            {
                if (count < 2) return 0f;
                double m = sum / count;
                return Mathf.Sqrt((float)(sumSq / count - m * m));
            }
        }
    }

    readonly List<Rig> rigs = new List<Rig>();
    GameObject root;

    IEnumerator Start()
    {
        var sb = new StringBuilder("=== LadderFloatProbe ===\n");
        var sea = SeaStateController.Instance;
        if (sea == null || !OceanSampler.Ready)
        { Debug.LogError("LadderFloatProbe: no sea controller / sampler"); yield break; }
        if (ShipLadder.Count == 0)
        { Debug.LogError("LadderFloatProbe: no ladder manifest"); yield break; }

        TimeOfDay.SetTime01(0.36f);
        TimeOfDay.Paused = true;

        // Open water. The region envelope flattens the sea near land, so a
        // float test run off the home island measures shelter, not the sea.
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        Vector3 site = home + new Vector3(2600f, 0f, 0f);
        site.y = 0f;
        if (sea.Follow != null) sea.Follow = null;
        sea.ForceHs(CalmHs);

        root = new GameObject("LadderRow");
        root.transform.position = site;

        float x = 0f;
        for (int i = 0; i < ShipLadder.Count; i++)
        {
            var n = ShipLadder.Node(i);
            x += n.beam * 0.5f + Gap;
            var go = new GameObject($"Rung{i:00}_{n.label.Replace(' ', '_')}");
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(site.x + x, 0f, site.z);

            var rb = go.AddComponent<Rigidbody>();
            var probes = go.AddComponent<BuoyancyProbeSet>();
            go.AddComponent<BuoyantBody>();
            var yard = go.AddComponent<Shipyard>();
            yard.Apply(i);                       // the same call the buttons make

            rigs.Add(new Rig { n = n, yard = yard, rb = rb, probes = probes });
            x += n.beam * 0.5f;
        }
        sb.AppendLine($"site {site.x:F0},{site.z:F0} — {ShipLadder.Count} rungs, "
                      + $"row {x:F0} m, Hs {CalmHs:F1} m");

        yield return new WaitForSeconds(8f);     // let the heave oscillation die

        // --- pass 1: do they float where they were drawn? --------------------
        yield return Measure(SampleSecs);
        sb.AppendLine("\nrung  label              draft   mass t   err m  spread   verdict");
        int bad = 0;
        foreach (var r in rigs)
        {
            bool ok = Mathf.Abs(r.MeanErr) <= Tolerance;
            if (!ok) bad++;
            sb.AppendLine($"{r.n.node,4}  {r.n.label,-17} {r.n.draft,6:F2} "
                        + $"{r.n.mass_kg / 1000f,8:F1} {r.MeanErr,7:F3} {r.Spread,7:F3}  "
                        + $"{(ok ? "floats as drawn" : "OFF HER LINE")}");
        }
        sb.AppendLine($"  {ShipLadder.Count - bad}/{ShipLadder.Count} within "
                      + $"{Tolerance:F2} m of their drawn waterline");

        // --- pass 2: is the rig she was given actually her? ------------------
        // Read the four numbers back off the probe array and print them
        // against the hull's own book. The one that used to be wrong by 2x is
        // BM: the authored rig put its probes at 0.32-0.41 of the beam where
        // her waterplane's RMS radius is 0.24, and a probe rig's stiffness is
        // its geometry and nothing else.
        sb.AppendLine("\nrung  label                 volume m3        waterplane m2"
                    + "           BM m              KB m");
        sb.AppendLine("                              rig    hull       rig    hull"
                    + "       rig   hull       rig   hull");
        float worstBm = 0f, worstKb = 0f, worstAw = 0f;
        foreach (var r in rigs)
        {
            var buoy = r.yard.GetComponent<BuoyantBody>();
            if (buoy == null) continue;
            BuoyancyProbeSet.RestHydrostatics(r.probes.Probes, buoy.TotalVolume,
                r.n.draft, out float v, out float aw, out float bm, out float kb);
            worstBm = Mathf.Max(worstBm, Mathf.Abs(bm - r.n.bm_m) / Mathf.Max(0.01f, r.n.bm_m));
            worstKb = Mathf.Max(worstKb, Mathf.Abs(kb - r.n.kb_above_keel_m) / Mathf.Max(0.01f, r.n.kb_above_keel_m));
            worstAw = Mathf.Max(worstAw, Mathf.Abs(aw - r.n.waterplane_m2) / Mathf.Max(0.01f, r.n.waterplane_m2));
            sb.AppendLine($"{r.n.node,4}  {r.n.label,-17} {v,8:F2}{r.n.volume_m3,8:F2}  "
                        + $"{aw,8:F2}{r.n.waterplane_m2,8:F2}  "
                        + $"{bm,6:F2}{r.n.bm_m,7:F2}  {kb,8:F2}{r.n.kb_above_keel_m,7:F2}");
        }
        sb.AppendLine($"  worst error: waterplane {worstAw:P1}, BM {worstBm:P1}, "
                    + $"KB {worstKb:P1}");
        sb.AppendLine("  (the rig it replaced measured BM +95% and KB +40% on the brig, "
                    + "which is the 2.8x the inclining experiment saw)");

        Debug.Log(sb.ToString());
    }

    IEnumerator Measure(float seconds)
    {
        foreach (var r in rigs) r.Reset();
        float t0 = Time.time;
        while (Time.time - t0 < seconds)
        {
            yield return new WaitForFixedUpdate();
            foreach (var r in rigs) r.Sample();
        }
    }
}
