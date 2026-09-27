using System.Collections.Generic;
using System.Text;
using SeaSick.Terrain;
using SeaSick.World;
using UnityEngine;

/// **Scenery rocks are stone (2026-09-27).** Kevin, phone: *"there are so
/// many rocks on the island but I guess they don't qualify as a stone
/// resource, please change so they do."* See `SceneryRocks` (the index and
/// the hide), `SceneryStone` (a camp's nodes on them), GDD 2026-09-27.
///
/// PLAY mode, Sea.unity, a camp. Each entry point is one `unity cmd eval`:
///
///   `SceneryStoneCheck.Run("Island_2")` -- inventory: loose rocks indexed
///       vs rock stamps left as landform, how many stand as stone nodes and
///       why the rest do not (off the grid / beach / unreachable / cap), the
///       size tally (2/4/8), the Stone seam (standing / max / sizing
///       version) against what the kit deposits alone would give, and
///       whether the gathered picture is a prefix of the seam (hidden rocks
///       vs the ledger's taken).
///   `SceneryStoneCheck.Start("Island_2", hands, scale)` -- puts up to
///       `hands` free hands on Gather Stone (the real trip path: ledger
///       booking, walked trips, `GatherSync` hiding the rock when the swing
///       is done) and runs game time at `scale`x. Records a baseline.
///   `SceneryStoneCheck.Report()` -- stone gained, scenery rocks gone since
///       Start, every one of them collapsed IN THE MESH (not just flagged),
///       none outside the camp's reach, and the measured hide cost
///       (`SceneryRocks.LastFlushMs` / `MaxFlushMs`, one upload per dirty
///       cell per frame).
///   `SceneryStoneCheck.Reload()` -- the save/unload half: the ledger goes
///       through a JSON round trip (what the save writes) and must come back
///       with the same Stone seam and sizing version; then the island's
///       scenery nodes are destroyed, every rock is stood back up, and one
///       `CatchUp` must stand the same nodes and hide EXACTLY the same rocks
///       again. Run `Report()` a frame later for the re-hide's upload cost.
///       (A full quit-and-load is Kevin's phone test; this proves the
///       picture is a function of the saved books.)
///   `SceneryStoneCheck.Stop()` -- game time back to 1x.
///
/// e.g. `unity cmd eval --json --code 'return SceneryStoneCheck.Run("Island_2");'`
public static class SceneryStoneCheck
{
    static Outpost camp;
    static float stone0, store0;
    static HashSet<int> hidden0 = new HashSet<int>();

    static Outpost Find(string island)
    {
        foreach (var o in Outpost.All)
        {
            if (o == null || o.Ledger == null) continue;
            if (string.IsNullOrEmpty(island) ? o.Watched : (o.Island != null && o.Island.name == island)) return o;
        }
        return null;
    }

    static readonly List<ResourceNode> nodes = new List<ResourceNode>();

    /// This island's Stone nodes; `scenery` true/false picks the kind.
    static List<ResourceNode> Nodes(Outpost o, bool scenery)
    {
        nodes.Clear();
        foreach (var n in ResourceNode.All)
        {
            if (n == null || n.Home != o.Island || n.Resource != Res.Stone) continue;
            bool s = n.Deposit != null && n.Deposit.IsScenery;
            if (s == scenery) nodes.Add(n);
        }
        return nodes;
    }

    static HashSet<int> HiddenSet(SceneryRocks rocks)
    {
        var set = new HashSet<int>();
        if (rocks == null) return set;
        for (int i = 0; i < rocks.Count; i++) if (rocks.IsHidden(i)) set.Add(i);
        return set;
    }

    public static string Run(string island = null)
    {
        if (!Application.isPlaying) return "SceneryStoneCheck.Run: enter play mode first";
        var o = Find(island);
        if (o == null) return $"SceneryStoneCheck.Run: no camp on '{island ?? "(watched)"}'";
        o.CatchUp();
        var rocks = SceneryRocks.On(o);
        var l = o.Ledger;
        var sb = new StringBuilder($"SceneryStoneCheck.Run {o.Island.name}\n");
        if (rocks == null) return sb + "no SceneryRocks on this island (home dressing, or the bake is not done)";

        int[] bySize = new int[3];
        int[] bySource = new int[4];
        for (int i = 0; i < rocks.Count; i++)
        {
            var r = rocks.RockAt(i);
            int u = SceneryRocks.UnitsOf(r);
            bySize[u == 2 ? 0 : u == 4 ? 1 : 2]++;
            bySource[(int)r.source]++;
        }
        sb.AppendLine($"indexed loose rocks {rocks.Count} (lone {bySource[0]}, scree {bySource[1]}, shore {bySource[2]}, accent {bySource[3]}); "
            + $"size small/medium/large {bySize[0]}/{bySize[1]}/{bySize[2]}; landform kept {rocks.LandformCount}");
        sb.AppendLine($"materialized {rocks.Materialized}: last stand made {SceneryStone.LastMade} of {SceneryStone.LastIndexed} "
            + $"(off grid {SceneryStone.LastOffGrid}, beach {SceneryStone.LastLow}, unreachable {SceneryStone.LastUnreachable}, over cap {SceneryStone.LastOverCap})");

        float sceneryUnits = 0f, kitUnits = 0f;
        int sceneryN = 0, sceneryHeld = 0, sceneryGone = 0;
        foreach (var n in Nodes(o, true))
        {
            sceneryN++;
            if (n.HeldBySite) { sceneryHeld++; continue; }
            sceneryUnits += n.Deposit.Units;
            if (n.Gathered) sceneryGone++;
        }
        int kitN = 0;
        foreach (var n in Nodes(o, false))
        {
            kitN++;
            if (n.Deposit != null && !n.HeldBySite) kitUnits += n.Deposit.Units;
        }
        var s = l.Stock(Res.Stone);
        sb.AppendLine($"stone nodes: scenery {sceneryN} ({sceneryHeld} held by plots, {sceneryGone} gathered, {sceneryUnits:0} units), kit {kitN} ({kitUnits:0} units)");
        if (s != null)
            sb.AppendLine($"seam: standing {s.standing:0.0} / max {s.standingMax:0.0}, sizing v{l.stoneDepositsV} (code v{OutpostLedger.StoneDepositsVersion}); "
                + $"kit deposits alone would size it to {kitUnits:0}, with scenery {kitUnits + sceneryUnits:0}; store {l.StoreCountOf(Res.Stone)}");
        else sb.AppendLine("seam: NO Stone stock on this camp");
        sb.AppendLine($"rocks hidden in the index {rocks.HiddenCount}; flush last {rocks.LastFlushMs:0.000} ms, worst {rocks.MaxFlushMs:0.000} ms over {rocks.Flushes} flushes");
        return sb.ToString();
    }

    public static string Start(string island = null, int hands = 3, float scale = 30f)
    {
        if (!Application.isPlaying) return "SceneryStoneCheck.Start: enter play mode first";
        camp = Find(island);
        if (camp == null) return $"SceneryStoneCheck.Start: no camp on '{island ?? "(watched)"}'";
        camp.CatchUp();
        var l = camp.Ledger;
        var rocks = SceneryRocks.On(camp);
        if (rocks == null) return "SceneryStoneCheck.Start: no SceneryRocks on this island";
        var sb = new StringBuilder($"SceneryStoneCheck.Start {camp.Island.name}\n");
        int put = 0;
        foreach (var h in l.hands)
        {
            if (put >= hands) break;
            if (h == null || h.Hauling) continue;
            if (h.order == OutpostOrder.Gather && h.target == Res.Stone) { put++; continue; }
            if (h.order != OutpostOrder.Idle && h.order != OutpostOrder.Gather) continue;
            camp.OrderGather(h, Res.Stone);
            put++;
        }
        var s = l.Stock(Res.Stone);
        stone0 = s != null ? s.standing : 0f;
        store0 = l.StoreCountOf(Res.Stone);
        hidden0 = HiddenSet(rocks);
        rocks.ResetFlushStats();
        TimeOfDay.Scale = Mathf.Max(1f, scale);
        sb.AppendLine($"{put} hands on Stone, seam {stone0:0.0}, store {store0}, rocks already hidden {hidden0.Count}, time x{TimeOfDay.Scale}");
        return sb.ToString();
    }

    public static string Report()
    {
        if (!Application.isPlaying) return "SceneryStoneCheck.Report: play mode only";
        if (camp == null || camp.Ledger == null) return "SceneryStoneCheck.Report: run Start first";
        var l = camp.Ledger;
        var rocks = SceneryRocks.On(camp);
        if (rocks == null) return "SceneryStoneCheck.Report: the island's scenery is gone (streamed out?)";
        var s = l.Stock(Res.Stone);
        var sb = new StringBuilder($"SceneryStoneCheck.Report {camp.Island.name}\n");
        sb.AppendLine($"seam {stone0:0.0} -> {(s != null ? s.standing : 0f):0.0}, store {store0} -> {l.StoreCountOf(Res.Stone)}");

        int gone = 0, inMesh = 0, unreachable = 0;
        var now = HiddenSet(rocks);
        foreach (int i in now)
        {
            if (hidden0.Contains(i)) continue;
            gone++;
            if (rocks.CollapsedInMesh(i)) inMesh++;
            if (!CampPath.Reachable(camp, rocks.RockAt(i).at)) unreachable++;
        }
        int back = 0;
        foreach (int i in hidden0) if (!now.Contains(i)) back++;
        bool pass = gone > 0 && inMesh == gone && unreachable == 0;
        sb.AppendLine($"scenery rocks gone since Start {gone} (collapsed in mesh {inMesh}, unreachable {unreachable}), stood back up {back}");
        sb.AppendLine($"hide cost: last flush {rocks.LastFlushMs:0.000} ms, worst {rocks.MaxFlushMs:0.000} ms, {rocks.Flushes} flushes");
        sb.AppendLine(pass ? "PASS" : gone == 0 ? "WAIT (nothing gathered yet -- run longer)" : "FAIL");
        return sb.ToString();
    }

    public static string Reload()
    {
        if (!Application.isPlaying) return "SceneryStoneCheck.Reload: play mode only";
        if (camp == null) camp = Find(null);
        if (camp == null || camp.Ledger == null) return "SceneryStoneCheck.Reload: no camp";
        var l = camp.Ledger;
        var rocks = SceneryRocks.On(camp);
        if (rocks == null) return "SceneryStoneCheck.Reload: no SceneryRocks on this island";
        var sb = new StringBuilder($"SceneryStoneCheck.Reload {camp.Island.name}\n");
        bool pass = true;

        // 1. The books survive the save's serialiser.
        var s = l.Stock(Res.Stone);
        var copy = JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(l));
        var cs = copy != null ? copy.Stock(Res.Stone) : null;
        bool booksOk = s != null && cs != null && Mathf.Abs(cs.standing - s.standing) < 1e-3f
                       && Mathf.Abs(cs.standingMax - s.standingMax) < 1e-3f && copy.stoneDepositsV == l.stoneDepositsV;
        pass &= booksOk;
        sb.AppendLine($"ledger JSON round trip: {(booksOk ? "same seam + version" : "DIFFERENT")}");

        // 2. The island unloaded and back: nodes gone, rocks stood up, one CatchUp.
        var before = HiddenSet(rocks);
        int nodes0 = Nodes(camp, true).Count;
        foreach (var n in new List<ResourceNode>(Nodes(camp, true))) Object.DestroyImmediate(n.gameObject);
        for (int i = 0; i < rocks.Count; i++) rocks.SetHidden(i, false);
        rocks.Materialized = false;
        GatherSync.Forget(camp);
        SceneryStone.Forget(camp);
        rocks.ResetFlushStats();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        camp.CatchUp();
        watch.Stop();
        var after = HiddenSet(rocks);
        int nodes1 = Nodes(camp, true).Count;
        bool same = before.SetEquals(after) && nodes0 == nodes1;
        pass &= same;
        sb.AppendLine($"nodes {nodes0} -> {nodes1}, hidden {before.Count} -> {after.Count}, same set {before.SetEquals(after)}; CatchUp {watch.Elapsed.TotalMilliseconds:0.00} ms (mesh upload is next LateUpdate: Report())");
        sb.AppendLine(pass ? "PASS" : "FAIL");
        return sb.ToString();
    }

    public static string Stop()
    {
        TimeOfDay.Scale = 1f;
        return "SceneryStoneCheck.Stop: time x1";
    }
}
