using System.Text;
using SeaSick.Crew;
using SeaSick.Save;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEditor;
using UnityEngine;

/// **Gather party check (2026-09-27), play mode.**
///
///     unity cmd eval --json --code 'GatherPartyCheck.Run("Island_2"); return "started";'
///     ... wait ~2-4 min ...
///     unity cmd eval --json --code 'return GatherPartyCheck.Report;'
///
/// Anchors off `islandName` (or uses the island she is already lying at when
/// the name is empty), sends a 2-hand stone party for 10 at 4x time, and
/// reports: the Send result, cast-off blocked while ashore, trips, hold
/// stone before/after, loose rocks hidden, the stop reason; then a second
/// party is sent and recalled after 6 s to show the recall brings everyone
/// back. Driven off `EditorApplication.update` (an Editor-assembly
/// MonoBehaviour cannot be added to a scene object).
public static class GatherPartyCheck
{
    public static string Report = "not run";

    enum Step { Moor, WaitAnchored, Send, WaitParty, SendRecall, WaitRecall, Done }
    static Step step;
    static string isleName;
    static float t0, stepAt;
    static AnchorController anchor;
    static VoyageManager voyage;
    static GatherParty party;
    static Island isle;
    static int stone0, hidden0;
    static bool castOffBlocked, sawAshore;
    static readonly StringBuilder sb = new StringBuilder();

    public static void Run(string islandName)
    {
        sb.Clear();
        if (!Application.isPlaying) { Report = "enter play mode first"; return; }
        anchor = Object.FindFirstObjectByType<AnchorController>();
        voyage = Object.FindFirstObjectByType<VoyageManager>();
        if (anchor == null || voyage == null) { Report = "no ship"; return; }
        party = GatherParty.For(anchor);
        isleName = islandName;
        step = Step.Moor;
        t0 = Time.realtimeSinceStartup;
        Report = "running";
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Line(string s) { sb.AppendLine(s); Report = sb.ToString(); }

    static void Finish(string why)
    {
        Time.timeScale = 1f;
        EditorApplication.update -= Tick;
        Line("END: " + why);
        Debug.Log("GatherPartyCheck\n" + Report);
        step = Step.Done;
    }

    static int HiddenRocks()
    {
        var rocks = isle != null ? SeaSick.Terrain.SceneryRocks.On(isle) : null;
        return rocks != null ? rocks.HiddenCount : -1;
    }

    static void Tick()
    {
        if (!Application.isPlaying) { EditorApplication.update -= Tick; Report += "\nplay mode ended"; return; }
        float now = Time.realtimeSinceStartup;
        if (now - t0 > 420f) { Finish("timed out in " + step); return; }

        switch (step)
        {
            case Step.Moor:
            {
                if (string.IsNullOrEmpty(isleName) && anchor.CurrentIsland != null)
                {
                    isle = anchor.CurrentIsland;
                    Line("using current island " + isle.name);
                    step = Step.WaitAnchored; stepAt = now; break;
                }
                isle = null;
                foreach (var i in Island.All) if (i != null && i.name == isleName) isle = i;
                if (isle == null) { Finish("no island " + isleName); return; }
                var camp = Outpost.Of(isle);
                if (camp != null && camp.HasCamp) Line("note: " + isle.name + " HAS a camp; the button only shows camp-less");
                if (anchor.CurrentIsland == isle) { step = Step.WaitAnchored; stepAt = now; break; }
                if (anchor.CurrentState != AnchorController.State.Underway) { anchor.CastOff(); break; }
                // A beach bearing, 15 m off the shore, in deep enough water.
                Vector3 best = default; bool found = false;
                for (int k = 0; k < 72 && !found; k++)
                {
                    float a = k * Mathf.PI * 2f / 72f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 at = isle.transform.position + dir * (isle.RadiusToward(isle.transform.position + dir * 100f) + 15f);
                    if (!isle.HasBeachToward(at)) continue;
                    if (Island.TerrainHeight != null && Island.TerrainHeight(at.x, at.z) > -0.5f) continue;
                    best = at; found = true;
                }
                if (!found) { Finish("no beach bearing on " + isle.name); return; }
                var motor = anchor.GetComponent<ShipMotor>();
                float yaw = Quaternion.LookRotation(isle.transform.position - best).eulerAngles.y;
                SaveGame.Warp(motor, best, yaw);
                if (!anchor.MoorAt(isle)) { Finish("MoorAt refused (" + anchor.CurrentState + ")"); return; }
                Line("moored off " + isle.name);
                step = Step.WaitAnchored; stepAt = now;
                break;
            }

            case Step.WaitAnchored:
                if (anchor.CurrentState == AnchorController.State.Anchored && now - stepAt > 3f)
                { step = Step.Send; stepAt = now; }
                else if (now - stepAt > 60f) Finish("never anchored (" + anchor.CurrentState + ")");
                break;

            case Step.Send:
            {
                var opts = GatherParty.Survey(isle, anchor.PartyLanding(), party);
                foreach (var o in opts) Line($"option {o.resource}: {o.sources} sources, {o.units} units in reach");
                stone0 = voyage.HeldOf(Res.Stone);
                hidden0 = HiddenRocks();
                bool ok = party.Send(Res.Stone, 10, 2, out string why);
                Line($"send 2 hands for 10 stone: {ok} {why}  (hold stone {stone0}, room {party.Room}, rocks hidden {hidden0})");
                if (!ok) { Finish("send refused"); return; }
                Time.timeScale = 4f;
                step = Step.WaitParty; stepAt = now;
                break;
            }

            case Step.WaitParty:
                if (anchor.CurrentState == AnchorController.State.Ashore)
                {
                    sawAshore = true;
                    anchor.CastOff();   // must be refused while hands are ashore
                    castOffBlocked = anchor.CurrentState == AnchorController.State.Ashore;
                }
                if (!party.Out)
                {
                    int stone1 = voyage.HeldOf(Res.Stone);
                    Line($"party back: trips {GatherParty.LastTrips}, sources taken {GatherParty.LastSourcesTaken}, "
                        + $"delivered {GatherParty.LastDelivered}, stop '{GatherParty.LastStop}'");
                    Line($"hold stone {stone0} -> {stone1}; rocks hidden {hidden0} -> {HiddenRocks()}");
                    Line($"ship went Ashore: {sawAshore}; cast-off blocked while ashore: {castOffBlocked}; now {anchor.CurrentState}");
                    var camp = Outpost.Of(isle);
                    Line("camp after party: " + (camp == null ? "none" : $"HasCamp {camp.HasCamp}, Building {camp.Building}"));
                    step = Step.SendRecall; stepAt = now;
                }
                else if (now - stepAt > 240f) Finish("party still out after 240 s: " + party.StatusLine);
                break;

            case Step.SendRecall:
                if (anchor.CurrentState != AnchorController.State.Anchored) { if (now - stepAt > 30f) Finish("not anchored for recall test"); break; }
                if (!party.Send(Res.Stone, GatherParty.FillHold, 2, out string w2))
                { Line("recall test: send refused (" + w2 + ")"); Finish("done (no recall test)"); return; }
                step = Step.WaitRecall; stepAt = now;
                break;

            case Step.WaitRecall:
                if (party.Out && !party.Recalling && now - stepAt > 6f)
                {
                    party.Recall("check recall");
                    Line("recall sent: " + party.StatusLine);
                }
                if (!party.Out)
                {
                    int aboard = 0, total = 0;
                    foreach (var c in anchor.GetComponentsInChildren<CrewAgent>(true)) { total++; if (c.IsAboard) aboard++; }
                    Line($"recall: back aboard in {now - stepAt:F0} s real, stop '{GatherParty.LastStop}', "
                        + $"hands aboard {aboard}/{total}, state {anchor.CurrentState}, hold stone {voyage.HeldOf(Res.Stone)}");
                    Finish("done");
                }
                else if (now - stepAt > 120f) Finish("recall never finished: " + party.StatusLine);
                break;
        }
    }
}
