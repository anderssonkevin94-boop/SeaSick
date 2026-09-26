using System.Text;
using SeaSick.World;
using UnityEngine;

/// **Hunting check (2026-09-26): kill, meat, hide, the beast, the carcass.**
///
/// Kevin's phone report: the hunter "hunts" but the animal does not die, is
/// not carried home, and no furs reach the store. Three entry points, each
/// one `unity cmd eval` call, each returning a plain report string:
///
///   `HuntCheck.Ledger()` -- EDIT or play mode, no scene: a bare ledger with
///       the larder FULL, one stone spear, a hunter on six animals, ticked 4
///       game days through `Tick` (the game's own door). PASS when hide
///       lands, the herd drops and the spear wears (the 2026-09-26 rule: a
///       full larder no longer blocks the hide).
///   `HuntCheck.Start(scale)` -- PLAY mode, a watched camp: gives the camp a
///       stone spear if it holds none (DEV ONLY: writes the books), puts a
///       hunter on Game (an existing one, else the first idle/gathering
///       hand) and runs game time at `scale`x (`TimeOfDay.Scale`). Records
///       a baseline.
///   `HuntCheck.Report(capturePath)` -- PLAY mode: kills / Food / Hide /
///       spear vs the baseline, the hunter's stall reason, what his body is
///       doing (quarry, spear drawn, carcass on shoulders) and the herd
///       (alive, lying dead, shouldered). Optional screenshot to
///       `capturePath` (written at the end of the frame).
///   `HuntCheck.Stop()` -- game time back to 1x.
///
/// e.g. `unity cmd eval --json --code 'return HuntCheck.Ledger();'`
public static class HuntCheck
{
    // --- ledger only ---------------------------------------------------------

    public static string Ledger()
    {
        var sb = new StringBuilder("HuntCheck.Ledger\n");
        var l = new OutpostLedger();
        l.SetCentre(Vector3.zero);
        l.ceilingPer = 30;
        l.campfireLevel = 1;
        l.stationsMigrated = true;
        l.hands.Add(new OutpostHand { name = "Hunter", order = OutpostOrder.Gather, target = Res.Game });
        l.Store(Res.Food, true).whole = 30;          // larder full: the case that used to block
        l.Store(Res.Spear, true).whole = 1;
        l.AddStanding(Res.Game, 6f, 0f);
        l.lastTicked = 0.0;

        float game0 = l.Stock(Res.Game).standing;
        int food0 = l.CountOf(Res.Food), hide0 = l.CountOf(Res.Hide), spear0 = l.CountOf(Res.Spear);
        string stall0 = l.StallReason(l.hands[0]) ?? "none";

        double now = 0.0;
        for (int d = 0; d < 4; d++)
        {
            now += TimeOfDay.DayLength;
            l.Tick(now + 1e-3);
        }

        float game1 = l.Stock(Res.Game).standing;
        var hideRow = l.Store(Res.Hide);
        float hide1 = hideRow != null ? hideRow.whole + hideRow.part : 0f;
        var spearRow = l.Store(Res.Spear);
        float spear1 = spearRow != null ? spearRow.whole + spearRow.part : 0f;
        float kills = game0 - game1;

        sb.AppendLine($"stall at start: {stall0}");
        sb.AppendLine($"game {game0:0.00} -> {game1:0.00}  (kills {kills:0.00})");
        sb.AppendLine($"food {food0} -> {l.CountOf(Res.Food)} (ceiling {l.ceilingPer}; meat past it is lost)");
        sb.AppendLine($"hide {hide0} -> {hide1:0.00}  (expect ~kills x {HidePer()})");
        sb.AppendLine($"spear {spear0} -> {spear1:0.00}  (stone wears {SeaSick.World.Economy.Techs.SpearWear(Res.Spear):0.00}/kill)");
        sb.AppendLine($"per animal: {Res.MeatPerAnimal} food + {HidePer()} hide");
        bool pass = kills > 0.5f && hide1 >= kills * HidePer() - 0.05f && spear1 < spear0;
        sb.AppendLine(pass ? "PASS" : "FAIL");
        return sb.ToString();
    }

    static int HidePer()
    {
        int n = 0;
        foreach (var d in SeaSick.World.Economy.Techs.HuntDrops) if (d.res == Res.Hide) n += d.n;
        return n;
    }

    // --- play mode -------------------------------------------------------------

    static Outpost camp;
    static OutpostHand hunter;
    static float game0;
    static int food0, hide0;
    static float spear0;

    public static string Start(float scale = 30f)
    {
        if (!Application.isPlaying) return "HuntCheck.Start: enter play mode first";
        camp = null;
        foreach (var o in Outpost.All)
            if (o != null && o.Watched && o.HasCamp && o.Ledger != null) { camp = o; break; }
        if (camp == null) return "HuntCheck.Start: no watched camp (land at one first)";
        var l = camp.Ledger;
        if (l.Stock(Res.Game) == null || camp.FaunaHere() == null) return "HuntCheck.Start: no herd on this island";

        var sb = new StringBuilder("HuntCheck.Start\n");
        if (l.SpearInHand() == null)
        {
            l.Store(Res.Spear, true).whole += 1;
            sb.AppendLine("gave the camp 1 stone spear (dev)");
        }

        hunter = null;
        foreach (var h in l.hands)
            if (h != null && h.order == OutpostOrder.Gather && h.target == Res.Game) { hunter = h; break; }
        if (hunter == null)
            foreach (var h in l.hands)
                if (h != null && (h.order == OutpostOrder.Idle || h.order == OutpostOrder.Gather) && !h.Hauling) { hunter = h; break; }
        if (hunter == null) return sb + "no free hand to send hunting";
        if (hunter.target != Res.Game) camp.OrderGather(hunter, Res.Game);

        game0 = l.Stock(Res.Game).standing;
        food0 = l.CountOf(Res.Food);
        hide0 = l.CountOf(Res.Hide);
        spear0 = SpearHeld(l);
        TimeOfDay.Scale = Mathf.Max(1f, scale);
        sb.AppendLine($"camp {camp.name}, hunter {hunter.name}, game {game0:0.00}, food {food0}, hide {hide0}, spear {spear0:0.00}, time x{TimeOfDay.Scale}");
        sb.AppendLine($"stall: {l.StallReason(hunter) ?? "none"}");
        return sb.ToString();
    }

    public static string Report(string capturePath = null)
    {
        if (!Application.isPlaying) return "HuntCheck.Report: play mode only";
        if (camp == null || camp.Ledger == null || hunter == null) return "HuntCheck.Report: run HuntCheck.Start first";
        var l = camp.Ledger;
        var sb = new StringBuilder("HuntCheck.Report\n");
        float game1 = l.Stock(Res.Game) != null ? l.Stock(Res.Game).standing : 0f;
        sb.AppendLine($"game {game0:0.00} -> {game1:0.00} (kills {game0 - game1:0.00}), progress {l.HuntProgress01():0.00}");
        sb.AppendLine($"food {food0} -> {l.CountOf(Res.Food)}, hide {hide0} -> {l.CountOf(Res.Hide)}, spear {spear0:0.00} -> {SpearHeld(l):0.00} ({l.SpearInHand() ?? "none"})");
        sb.AppendLine($"hunter {hunter.name}: order {hunter.order}/{hunter.target}, stall '{l.StallReason(hunter) ?? "none"}'");

        foreach (var w in CampWorker.Bodies)
        {
            if (w == null) continue;
            var props = w.GetComponent<HunterProps>();
            if (props == null) continue;
            Transform spear = null;
            foreach (Transform c in w.transform) if (c.name.StartsWith("HunterSpear")) spear = c;
            var q = w.Quarry;
            sb.AppendLine($"  body {w.name}: quarry {(q != null ? q.name + (q.Dead ? " (dead)" : "") : "none")}, "
                + $"spear {(spear != null && spear.gameObject.activeSelf ? spear.name : "hidden")}, carcass {(props.HasCarcass ? "ON SHOULDERS" : "no")}");
        }

        var herd = camp.FaunaHere();
        int alive = 0;
        if (herd != null && herd.Animals != null)
            foreach (var a in herd.Animals) if (a != null && !a.Dead) alive++;
        int lying = 0, carried = 0;
        foreach (var a in Object.FindObjectsByType<Animal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a == null || !a.Dead) continue;
            if (a.Shouldered) carried++; else lying++;
        }
        sb.AppendLine($"herd: {alive} alive (books {Mathf.CeilToInt(game1)}), {lying} dead lying, {carried} shouldered");

        if (!string.IsNullOrEmpty(capturePath))
        {
            ScreenCapture.CaptureScreenshot(capturePath);
            sb.AppendLine("capture -> " + capturePath + " (written at end of frame)");
        }
        return sb.ToString();
    }

    public static string Stop()
    {
        TimeOfDay.Scale = 1.0;
        return "HuntCheck.Stop: time x1";
    }

    static float SpearHeld(OutpostLedger l)
    {
        float n = 0f;
        foreach (var s in SeaSick.World.Economy.Techs.HuntingSpears)
        {
            var row = l.Store(s);
            if (row != null) n += row.whole + row.part;
        }
        return n;
    }
}
