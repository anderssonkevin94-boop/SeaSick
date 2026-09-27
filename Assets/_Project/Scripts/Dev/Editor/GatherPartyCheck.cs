using System.Collections.Generic;
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
///
/// **Persistence gates (2026-09-27)** -- `Run(island, persist: true)`, on a
/// CAMP-LESS island (the take is booked by name into its ledger,
/// `OutpostLedger.GroundTaken`). After the 10-stone party is back, instead
/// of the recall test:
///   P1 books: every source cut was booked; the island's Stone `standing`
///      and `standingMax` both fell by the same units (the camp's derived
///      "taken" is unchanged, so `GatherSync` hides nothing extra);
///   P2 picture: rocks hidden now == rocks hidden before + exactly the
///      rocks booked by name (none extra, none missing);
///   P3 island unload/reload: the island's stone nodes destroyed, every
///      rock stood back up, caches forgotten, one `CatchUp` -> the same
///      hidden set;
///   P4 save -> the real `GameMenus.LoadSlotAndReload` (manual slot m5, in
///      a scratch save folder, never Kevin's) -> after the restore, the
///      same hidden set, the same named rocks and the same Stone stock on
///      the rebuilt island.
///
///     unity cmd eval --json --code 'GatherPartyCheck.Run("Island_5", true); return "started";'
public static class GatherPartyCheck
{
    public static string Report = "not run";

    enum Step { Moor, WaitAnchored, Send, WaitParty, SendRecall, WaitRecall, Persist, WaitReload, Done }
    static Step step;
    static string isleName;
    static float t0, stepAt;
    static AnchorController anchor;
    static VoyageManager voyage;
    static GatherParty party;
    static Island isle;
    static int stone0, hidden0;
    static bool castOffBlocked, sawAshore;
    static bool persist, allPass;
    static HashSet<int> hiddenBefore, hiddenAfter;
    static int named0;
    static float standing0, max0, standing1, max1;
    static AnchorController oldAnchor;
    static List<int> namedRocks;
    static readonly StringBuilder sb = new StringBuilder();

    public static void Run(string islandName) => Run(islandName, false);

    public static void Run(string islandName, bool persistGates)
    {
        sb.Clear();
        persist = persistGates;
        allPass = true;
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
        if (persist) { SaveSlots.DirectoryOverride = null; Line(allPass ? "PERSIST: PASS" : "PERSIST: FAIL"); }
        Line("END: " + why);
        Debug.Log("GatherPartyCheck\n" + Report);
        step = Step.Done;
    }

    static int HiddenRocks()
    {
        var rocks = isle != null ? SeaSick.Terrain.SceneryRocks.On(isle) : null;
        return rocks != null ? rocks.HiddenCount : -1;
    }

    static OutpostLedger Books()
    {
        var o = isle != null ? Outpost.Of(isle) : null;
        return o != null ? o.Ledger : null;
    }

    static HashSet<int> HiddenSet()
    {
        var set = new HashSet<int>();
        var rocks = isle != null ? SeaSick.Terrain.SceneryRocks.On(isle) : null;
        if (rocks != null) for (int i = 0; i < rocks.Count; i++) if (rocks.IsHidden(i)) set.Add(i);
        return set;
    }

    static string Minus(HashSet<int> a, HashSet<int> b)
    {
        var d = new List<int>();
        foreach (int i in a) if (!b.Contains(i)) d.Add(i);
        return d.Count == 0 ? "none" : "[" + string.Join(",", d) + "]";
    }

    static bool SameList(List<int> a, List<int> b)
    {
        if (a == null || b == null) return a == b;
        return new HashSet<int>(a).SetEquals(b) && a.Count == b.Count;
    }

    static void Gate(string what, bool ok, string detail)
    {
        allPass &= ok;
        Line($"{(ok ? "PASS" : "FAIL")}  {what}  ({detail})");
    }

    /// P1-P3 in one frame, then the save and the real reload (P4 next).
    static void PersistGates()
    {
        var o = Outpost.Of(isle);
        var l = o != null ? o.Ledger : null;
        var rocks = SeaSick.Terrain.SceneryRocks.On(isle);
        if (l == null || rocks == null) { Gate("P0 island has books and rocks", false, "ledger " + (l != null) + ", rocks " + (rocks != null)); Finish("no books"); return; }
        var s = l.Stock(Res.Stone);
        standing1 = s != null ? s.standing : -1f;
        max1 = s != null ? s.standingMax : -1f;

        // P1 the books.
        Gate("P1 every source cut was booked", party.BookedSources == GatherParty.LastSourcesTaken && party.BookedSources > 0,
            $"booked {party.BookedSources}, cut {GatherParty.LastSourcesTaken}");
        Gate("P1 standing and ceiling fell together", s != null
            && Mathf.Abs((standing0 - standing1) - (max0 - max1)) < 0.01f && standing1 < standing0,
            $"standing {standing0:0.0}->{standing1:0.0}, max {max0:0.0}->{max1:0.0}");

        // P2 the picture: before + exactly the named rocks.
        namedRocks = new List<int>(l.takenRocks);
        var newNamed = new HashSet<int>();
        for (int i = named0; i < namedRocks.Count; i++) newNamed.Add(namedRocks[i]);
        hiddenAfter = HiddenSet();
        var expect = new HashSet<int>(hiddenBefore);
        expect.UnionWith(newNamed);
        Gate("P2 hidden = before + named (no extra rock vanished)", hiddenAfter.SetEquals(expect),
            $"before {hiddenBefore.Count}, named +{newNamed.Count}, now {hiddenAfter.Count}; extra {Minus(hiddenAfter, expect)}, missing {Minus(expect, hiddenAfter)}");

        // P3 the island unloaded and back (the `SceneryStoneCheck.Reload` recipe).
        foreach (var n in rocks.GetComponentsInChildren<ResourceNode>(true)) Object.DestroyImmediate(n.gameObject);
        for (int i = 0; i < rocks.Count; i++) rocks.SetHidden(i, false);
        rocks.Materialized = false;
        GatherSync.Forget(o);
        SceneryStone.Forget(o);
        o.CatchUp();
        var h3 = HiddenSet();
        Gate("P3 island unload/reload: same hidden set", h3.SetEquals(hiddenAfter),
            $"{h3.Count} vs {hiddenAfter.Count}; back {Minus(hiddenAfter, h3)}, extra {Minus(h3, hiddenAfter)}");

        // P4 save, then the real load path.
        SaveSlots.DirectoryOverride = System.IO.Path.Combine(Application.temporaryCachePath, "gatherparty-check");
        System.IO.Directory.CreateDirectory(SaveSlots.DirectoryOverride);
        if (!SaveSlots.SaveManual("m5", "GatherPartyCheck", out string err)) { Gate("P4 saved", false, err); Finish("save failed"); return; }
        Line("P4 saved to m5 in " + SaveSlots.DirectoryOverride + "; LoadSlotAndReload ...");
        oldAnchor = anchor;
        Time.timeScale = 1f;
        step = Step.WaitReload;
        stepAt = Time.realtimeSinceStartup;
        SeaSick.UI.Menus.GameMenus.LoadSlotAndReload("m5");
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
                hiddenBefore = HiddenSet();
                var books0 = Books();
                var st0 = books0 != null ? books0.Stock(Res.Stone) : null;
                named0 = books0 != null && books0.takenRocks != null ? books0.takenRocks.Count : 0;
                standing0 = st0 != null ? st0.standing : -1f;
                max0 = st0 != null ? st0.standingMax : -1f;
                if (persist && books0 == null) { Finish("no ledger on " + isle.name + " (survey not done?)"); return; }
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
                    step = persist ? Step.Persist : Step.SendRecall; stepAt = now;
                }
                else if (now - stepAt > 240f) Finish("party still out after 240 s: " + party.StatusLine);
                break;

            case Step.Persist:
                PersistGates();
                break;

            case Step.WaitReload:
            {
                if (now - stepAt > 240f) { Gate("P4 reload finished", false, "timed out"); Finish("reload never finished"); return; }
                var a = Object.FindFirstObjectByType<AnchorController>();
                if (a == null || a == oldAnchor || SaveGame.Restoring || now - stepAt < 3f) break;
                isle = null;
                foreach (var i in Island.All) if (i != null && i.name == isleName) isle = i;
                var o = isle != null ? Outpost.Of(isle) : null;
                // The adopted ledger is the one with the named takes; a fresh
                // survey's has none, so this also waits out the restore.
                if (o == null || o.Ledger == null || !o.Ledger.HasGroundTaken
                    || SeaSick.Terrain.SceneryRocks.On(isle) == null)
                {
                    if (now - stepAt > 120f && !SaveGame.Restoring)
                    { Gate("P4 island ledger restored with its named takes", false, SaveGame.LastRestoreNote); Finish("ledger not restored"); }
                    break;
                }
                Gate("P4 restore ok", SaveGame.LastRestoreOk, SaveGame.LastRestoreNote);
                o.CatchUp();
                var l = o.Ledger;
                var s = l.Stock(Res.Stone);
                var h = HiddenSet();
                Gate("P4 same named rocks after load", SameList(l.takenRocks, namedRocks),
                    $"{(l.takenRocks != null ? l.takenRocks.Count : 0)} vs {namedRocks.Count}");
                Gate("P4 same hidden rocks after load (none back, none extra)", h.SetEquals(hiddenAfter),
                    $"{h.Count} vs {hiddenAfter.Count}; back {Minus(hiddenAfter, h)}, extra {Minus(h, hiddenAfter)}");
                Gate("P4 same Stone stock after load", s != null && Mathf.Abs(s.standing - standing1) < 0.01f
                    && Mathf.Abs(s.standingMax - max1) < 0.01f,
                    s != null ? $"{s.standing:0.0}/{s.standingMax:0.0} vs {standing1:0.0}/{max1:0.0}" : "no stock");
                Gate("P4 still camp-less", !o.HasCamp, "HasCamp " + o.HasCamp);
                Finish("done");
                break;
            }

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
