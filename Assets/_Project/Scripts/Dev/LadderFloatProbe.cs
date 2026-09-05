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
/// The second question it answers is whether the measured rule still holds.
/// `probe keel = drawn keel + 0.55 x draft` was solved on five hulls across a
/// 19x range; if it is a property of the probe LAYOUT rather than a
/// coincidence of those five, it should hold on all twenty. The probe reports
/// the lift each rung actually wants so the rule can be checked, not assumed.
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
    const float SolveSecs = 10f;

    class Rig
    {
        public LadderNode n;
        public Shipyard yard;
        public Rigidbody rb;
        public BuoyancyProbeSet probes;
        public float keelY;             // probe keel, as applied
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

            rigs.Add(new Rig { n = n, yard = yard, rb = rb, probes = probes,
                               keelY = n.ProbeKeelY });
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

        // --- pass 2: what lift does each rung actually want? -----------------
        // Solve it the way the fleet was solved: nudge the probe keel by the
        // error and re-measure, three times. If 0.55 is a property of the probe
        // LAYOUT it will come back at 0.55 on hulls nobody tuned.
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (var r in rigs)
            {
                r.keelY += r.MeanErr;
                r.probes.SetProbes(BuoyancyProbeSet.FleetLayout(
                    r.n.length * 0.86f, r.n.beam * 0.9f, r.n.draft, r.keelY, r.n.RailY));
            }
            yield return new WaitForSeconds(3f);
            yield return Measure(SolveSecs);
        }

        sb.AppendLine("\nrung  label              draft   solved lift   ratio   stated 0.55");
        float lo = 99f, hi = -99f;
        foreach (var r in rigs)
        {
            float lift = r.keelY + r.n.draft;
            float ratio = lift / Mathf.Max(0.001f, r.n.draft);
            lo = Mathf.Min(lo, ratio); hi = Mathf.Max(hi, ratio);
            sb.AppendLine($"{r.n.node,4}  {r.n.label,-17} {r.n.draft,6:F2} "
                        + $"{lift,12:F3} {ratio,7:F3} {r.n.probe_lift,13:F3}");
        }
        sb.AppendLine($"  ratio spans {lo:F3} to {hi:F3} across a "
                      + $"{ShipLadder.Node(ShipLadder.Count - 1).draft / ShipLadder.Node(0).draft:F0}x "
                      + $"range of draft — the rule is {(hi - lo < 0.06f ? "HOLDING" : "NOT holding")}");

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
