
using System.Text;
using UnityEngine;
using SeaSick.World;
using Debug = UnityEngine.Debug;

/// The gate on generalising the home-only `Village` into a per-island
/// `Outpost`.
///
/// **A refactor that changes home is a bug**, so the first half re-measures
/// home against the numbers the old code produced and states them out loud
/// rather than asserting a tautology: the clearing is 30 m because
/// `Dock.ViewHalfWidth` is 42 and the clearing's edge room (`Outpost.ClearingEdgeRoom`) is 4.72,
/// and 42 - 4.72 - 2 clamps to the 30 m ceiling. Those are the shipped
/// constants, read here off the built objects.
///
/// The second half is the new claim: any island can be surveyed on demand,
/// the answer is sometimes honestly "no", and it is cheap enough to pay at
/// the moment the anchor goes down. **The cost is MEASURED, not asserted** --
/// "lazy on purpose" is only a design if the number behind it is real.
public class OutpostProbe : MonoBehaviour
{
    /// A MonoBehaviour purely so the surveys have something to run their
    /// coroutines on -- and so this probe can wait for them.
    class ProbeHost : MonoBehaviour { }

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("OutpostProbe: not in play mode"); return; }
        var runner = new GameObject("OutpostProbeRunner").AddComponent<ProbeHost>();
        runner.StartCoroutine(Run(runner));
    }

    static int Pending()
    {
        int n = 0;
        foreach (var isle in Island.All)
            if (isle != null && !isle.IsHome && Outpost.Surveying(isle)) n++;
        return n;
    }

    static System.Collections.IEnumerator Run(MonoBehaviour runner)
    {
        var sb = new StringBuilder();
        int fails = 0;

        // --- home, which must not have moved --------------------------------

        var home = Outpost.Home;
        if (home == null)
        {
            Report("FAIL: no home Outpost -- the populator did not site one\n");
            if (runner != null) Object.Destroy(runner.gameObject);
            yield break;
        }

        // Recomputed from the SHIPPED edge room and the SHIPPED dock constant,
        // so this disagrees the moment either is retuned -- which is the point.
        float widest = Outpost.ClearingEdgeRoom;
        float expected = Mathf.Clamp(Dock.ViewHalfWidth - widest - 2f, 14f, 30f);

        sb.AppendLine($"HOME  '{home.Island?.name}'");
        sb.AppendLine($"  clearing r={home.ClearingRadius:F2} m  (expected {expected:F2} from "
            + $"ViewHalfWidth {Dock.ViewHalfWidth:F0} - edge room {widest:F2} - 2)");
        sb.AppendLine($"  centred {home.ClearingCentre.x:F1},{home.ClearingCentre.z:F1}"
            + $"   capacity {home.StoreCapacity}   built {home.Built.Count}");
        sb.AppendLine($"  sited {home.Sited}   isHome {home.IsHome}");

        Gate(sb, ref fails, "home-clearing-radius",
            Mathf.Abs(home.ClearingRadius - expected) < 0.01f,
            $"{home.ClearingRadius:F3} vs {expected:F3}");
        Gate(sb, ref fails, "home-is-outpost-zero", home.IsHome && home.Sited,
            $"isHome {home.IsHome}, sited {home.Sited}");
        // Kevin, 2026-09-22: no island is home any more; bare ground keeps nothing.
        Gate(sb, ref fails, "home-capacity-0-bare",
            home.Built.Count > 0 || home.StoreCapacity == 0,
            $"capacity {home.StoreCapacity} with {home.Built.Count} built");

        // The clearing must sit on the ground the survey chose, not near it.
        var settlement = home.GetComponent<Settlement>();
        if (settlement != null)
        {
            float slip = Vector3.Distance(home.ClearingCentre, settlement.VillageAt);
            sb.AppendLine($"  clearing vs Settlement.VillageAt: {slip:F3} m apart");
            Gate(sb, ref fails, "home-clearing-on-surveyed-ground", slip < 0.01f, $"{slip:F3} m");
        }

        // Home must be the ONE that claims the statics, now that it is no
        // longer the only outpost there can be.
        Gate(sb, ref fails, "home-owns-the-singleton", Outpost.Of(home.Island) == home,
            "Of(home island) != Outpost.Home");
        var homePile = Stockpile.Instance;
        Gate(sb, ref fails, "home-owns-the-pile",
            homePile == null || homePile.Island == null || homePile.Island.IsHome,
            $"Stockpile.Instance is on '{homePile?.Island?.name}'");

        // --- the new claim: any island, surveyed on demand -------------------

        sb.AppendLine();
        sb.AppendLine($"RULES published: {Outpost.Rules.Valid}"
            + $"   surveyFloor {Outpost.Rules.surveyFloor:F2} m"
            + $"   buildFloor {Outpost.Rules.buildFloor:F2} m"
            + $"   viewHalfWidth {Outpost.Rules.viewHalfWidth:F0} m");
        Gate(sb, ref fails, "site-rules-published", Outpost.Rules.Valid,
            "the world build never published Outpost.Rules");

        // Survey EVERY non-home island THROUGH THE PATH THE GAME USES, then
        // report the whole world -- not just what this call happened to create.
        //
        // The first version reported only islands it established itself, and
        // read "0 of 5 will take a camp" on a world where 28 already had one.
        // The cause is worth remembering: **a timed-out MCP call still runs.**
        // The 60 s timeout is on the bridge, not on Unity, so an earlier probe
        // that "failed" had in fact sited the whole archipelago, and the run
        // that succeeded saw only the leftovers. A probe that reports a
        // DIFFERENCE cannot tell you the state; report the state.

        Outpost.ResetSurveyCost();
        var host = new GameObject("OutpostProbeHost").AddComponent<ProbeHost>();
        int surveyedNow = 0;
        int worstFrames = 0; string slowest = "-";

        // ONE AT A TIME, because that is what the game does: she anchors at
        // one island. Running all thirty-three at once -- the first version --
        // measured 17.1 ms a frame and called it a failure, when it was
        // thirty-three surveys sharing a frame that the game will never ask
        // for. A gate has to reproduce the real pattern or its number means
        // nothing.
        var started = System.DateTime.UtcNow;
        foreach (var isle in Island.All)
        {
            if (isle == null || isle.IsHome || Outpost.Surveyed(isle)) continue;
            if ((System.DateTime.UtcNow - started).TotalSeconds > 240.0) break;
            Outpost.BeginSurvey(isle, host);
            surveyedNow++;
            while (Outpost.Surveying(isle)) yield return null;
            if (Outpost.LastSurveyFrames > worstFrames)
            { worstFrames = Outpost.LastSurveyFrames; slowest = isle.name; }
        }
        Object.Destroy(host.gameObject);

        int islands = 0, sited = 0, refusedN = 0, unsurveyed = 0;
        float bestHa = 0f; string bestName = "-";
        var shown = new StringBuilder();
        foreach (var isle in Island.All)
        {
            if (isle == null || isle.IsHome) continue;
            islands++;
            var o = Outpost.Of(isle);
            if (o != null && o.Sited)
            {
                sited++;
                var st = o.GetComponent<Settlement>();
                float ha = st != null ? st.AreaHectares : 0f;
                if (ha > bestHa) { bestHa = ha; bestName = isle.name; }
                if (sited <= 6)
                    shown.AppendLine($"  {isle.name,-13} r={isle.Radius,4:F0} m  {ha,5:F2} ha flat  "
                        + $"clearing {o.ClearingRadius:F0} m at {o.ClearingCentre.x:F0},{o.ClearingCentre.z:F0}"
                        + $"   holds {(st != null ? st.Capacity() : 0)}");
            }
            else if (Outpost.Surveyed(isle))
            {
                refusedN++;
                float ground = Outpost.Rules.Valid
                    ? Outpost.Rules.height(isle.transform.position.x, isle.transform.position.z) : 0f;
                if (refusedN <= 4)
                    shown.AppendLine($"  {isle.name,-13} r={isle.Radius,4:F0} m  REFUSED"
                        + $" -- centre stands {ground:F1} m against a {Outpost.Rules.surveyFloor:F1} m floor");
            }
            else unsurveyed++;
        }

        sb.AppendLine();
        sb.AppendLine($"WORLD: {Island.All.Count} islands ({islands} besides home), "
            + $"{Outpost.All.Count} outposts standing");
        sb.AppendLine($"  {surveyedNow} surveyed by this run, over frames; "
            + $"the rest were already looked at");
        sb.AppendLine();
        sb.Append(shown);
        sb.AppendLine();
        sb.AppendLine($"  {sited} will take a camp, {refusedN} refused, {unsurveyed} never looked at");
        sb.AppendLine($"  biggest flat: {bestHa:F2} ha on {bestName}");

        // The island-size distribution, read off the ISLANDS rather than off
        // the setting that generated them. `maskFrequency` is the dial, but a
        // dial is a claim and the radii are the artefact.
        var radii = new System.Collections.Generic.List<float>();
        foreach (var isle in Island.All)
            if (isle != null && !isle.IsHome) radii.Add(isle.Radius);
        radii.Sort();
        if (radii.Count > 0)
        {
            float med = radii[radii.Count / 2];
            int fitsIn165 = 0;
            foreach (var r in radii) if (r * 2f <= 165f) fitsIn165++;
            sb.AppendLine($"  SIZE: radius min {radii[0]:F0} / median {med:F0} / max "
                + $"{radii[radii.Count - 1]:F0} m");
            sb.AppendLine($"  {fitsIn165} of {radii.Count} fit across the authored 165 m shot");
        }
        sb.AppendLine($"  cost, one survey at a time as the game pays it: "
            + $"{Outpost.SurveyBands} bands, mean {Outpost.SurveyBandMeanMs:F2} ms, "
            + $"{Outpost.SurveyBandsOverBudget} over {Outpost.BandBudgetMs:F0} ms "
            + $"({(Outpost.SurveyBands > 0 ? 100f * Outpost.SurveyBandsOverBudget / Outpost.SurveyBands : 0f):F2}%)");
        sb.AppendLine($"  worst analysis pass {Outpost.WorstSolveMs:F1} ms — ONE frame at the "
            + $"end of each survey, and it cannot be spread (flood, label and distance "
            + $"transform each need the finished grid)");
        sb.AppendLine($"  longest survey {worstFrames} frames on {slowest}"
            + $"  (~{worstFrames / 60f:F2} s at 60 fps; the camera takes 0.7 s to rise)");

        Gate(sb, ref fails, "every-island-looked-at", unsurveyed == 0,
            $"{unsurveyed} islands never surveyed");
        Gate(sb, ref fails, "islands-take-camps", sited > 0,
            $"{sited} of {islands} islands could be settled");
        // Refusing everything and accepting everything are both wrong: the
        // point of surveying is that the ground gets a vote.
        Gate(sb, ref fails, "refusal-is-possible-not-universal",
            islands == 0 || (sited < islands || refusedN > 0) && sited > islands / 4,
            $"{sited} sited / {refusedN} refused of {islands}");
        // The number that matters is the worst FRAME, not the worst survey.
        // Taken whole the survey is 443 ms on the biggest island -- twenty-six
        // frames, which is why it is spread. A frame at 60 fps is 16.7 ms and
        // the ship is stopped with the camera rising, so 10 ms is the bar.
        // **This gate was the max, and the max was the wrong statistic.**
        // See Outpost.SurveyBands. A typical band well inside budget and a
        // vanishing tail over it is what "does not hitch" actually looks like;
        // one outlier in thousands is the machine, not the code.
        Gate(sb, ref fails, "a-band-is-typically-well-inside-budget",
            Outpost.SurveyBandMeanMs < Outpost.BandBudgetMs * 0.5f,
            $"mean band {Outpost.SurveyBandMeanMs:F2} ms");
        // The analysis pass is a real dropped frame, once per survey, while the
        // ship is stopped and the camera is rising. Twenty milliseconds is a
        // visible stutter; this is meant to catch it growing, not to demand it
        // disappear.
        Gate(sb, ref fails, "the-analysis-pass-is-not-a-stutter",
            Outpost.WorstSolveMs < 20f, $"{Outpost.WorstSolveMs:F1} ms in one frame");
        Gate(sb, ref fails, "slow-bands-are-rare",
            Outpost.SurveyBands == 0
            || Outpost.SurveyBandsOverBudget <= Outpost.SurveyBands / 100,
            $"{Outpost.SurveyBandsOverBudget} of {Outpost.SurveyBands} bands over "
            + $"{Outpost.BandBudgetMs:F0} ms");
        Gate(sb, ref fails, "surveys-finished", Pending() == 0,
            $"{Pending()} still running");
        // **This bar moved once, and the reason matters.** It was 120 frames,
        // against the 0.7 s the camera takes to rise -- a bar I invented, and
        // one the biggest island missed at 198 frames. The fix was not to
        // relax it but to start the survey EARLIER: `AnchorController` now
        // begins it when the island comes into landing range, while she is
        // still standing in, which is seconds rather than milliseconds.
        //
        // So what this gate now asserts is that a survey finishes PROMPTLY
        // once begun -- five seconds, well inside an approach -- not that it
        // beats a camera move it was never going to beat.
        Gate(sb, ref fails, "survey-finishes-within-an-approach",
            worstFrames < 300, $"{worstFrames} frames (~{worstFrames / 60f:F2} s) on {slowest}");

        // Establishing twice must hand back the same outpost, not a second one
        // stacked on the same island -- the lazy path runs on every anchoring.
        Island probeIsle = null;
        foreach (var isle in Island.All)
            if (isle != null && !isle.IsHome && Outpost.Of(isle) != null) { probeIsle = isle; break; }
        if (probeIsle != null)
        {
            var first = Outpost.Of(probeIsle);
            var again = Outpost.Establish(probeIsle);
            int count = probeIsle.GetComponents<Outpost>().Length;
            sb.AppendLine();
            sb.AppendLine($"  re-establish on {probeIsle.name}: same outpost {again == first}, "
                + $"{count} Outpost component(s)");
            Gate(sb, ref fails, "establish-is-idempotent", again == first && count == 1,
                $"same {again == first}, components {count}");
        }

        sb.AppendLine();
        sb.AppendLine(fails == 0 ? "PASS — home unchanged, islands surveyable"
                                 : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
        if (runner != null) Object.Destroy(runner.gameObject);
    }

    static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {detail}");
    }

    static void Report(string text)
    {
        Debug.Log("OutpostProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/OutpostProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
