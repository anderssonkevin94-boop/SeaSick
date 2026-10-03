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

    /// A bare camp: one hunter, `spears` stone spears, `game` animals,
    /// `food` Food already in the larder (ceiling 30).
    static OutpostLedger Camp(int food, int spears, float game)
    {
        var l = new OutpostLedger();
        l.SetCentre(Vector3.zero);
        l.ceilingPer = 30;
        l.campfireLevel = 1;
        l.stationsMigrated = true;
        l.hands.Add(new OutpostHand { name = "Hunter", order = OutpostOrder.Gather, target = Res.Game });
        l.Store(Res.Food, true).whole = food;
        l.Store(Res.Hide, true);
        l.Store(Res.Spear, true).whole = spears;
        l.AddStanding(Res.Game, game, 0f);
        l.lastTicked = 0.0;
        return l;
    }

    /// **The hunt is a WALK (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md):**
    /// walk up to the beast, jab, carry, deposit -- no stalk timer. Steps a bare camp one quantum
    /// at a time and asserts, at every step: Hide whole moves only in a step
    /// that booked a carcass deposit, by exactly +1 (0 when full); Food by
    /// +4 (less when full) in that step and never otherwise; the herd drops
    /// a WHOLE animal only in a step that booked a kill; Hide `.part` stays
    /// 0. Then: count(deposits) == hide gained (larder-empty camp) and a
    /// full larder still takes the hide; a save mid-trip (JSON round trip)
    /// lands on the same books as the unsaved copy; one long tick lands on
    /// the same books as many short ones (D2).
    public static string Ledger()
    {
        var sb = new StringBuilder("HuntCheck.Ledger (hunt = trip)\n");
        bool pass = true;
        float q = OutpostLedger.QuantumDays * TimeOfDay.WorkDaySeconds;
        sb.AppendLine($"est. trip {TimeOfDay.WorkDaySeconds * new OutpostLedger().HuntTripDays(false):0}s (armed {TimeOfDay.WorkDaySeconds * new OutpostLedger().HuntTripDays(true):0}s), {new OutpostLedger().HuntTripPerDay(false):0.00} kills/day");

        foreach (int food in new[] { 0, 30 })
        {
            var l = Camp(food, 2, 6f);
            var h = l.hands[0];
            int kills = 0, deposits = 0, bad = 0;
            int food0 = l.Store(Res.Food).whole, hide0 = l.Store(Res.Hide).whole;
            float game0 = l.Stock(Res.Game).standing;
            double now = 0.0;
            for (int i = 0; i < 700; i++)
            {
                int f = l.Store(Res.Food).whole, hd = l.Store(Res.Hide).whole;
                float g = l.Stock(Res.Game).standing;
                int k = h.huntKills, d = h.huntDeposits;
                now += q;
                l.Tick(now + 1e-3);
                int df = l.Store(Res.Food).whole - f, dh = l.Store(Res.Hide).whole - hd;
                float dg = g - l.Stock(Res.Game).standing;
                int dk = h.huntKills - k, dd = h.huntDeposits - d;
                kills += dk; deposits += dd;
                // The camp eats (Food whole can fall by one in any step) and
                // the herd breeds back a hair a step, so: Food rises only in
                // a deposit step, by at most 4 (4 or 3 with an empty larder);
                // Hide moves only in a deposit step, by exactly one; the
                // herd loses a whole animal only in a kill step.
                bool foodOk = dd == 0 ? df <= 0
                    : df <= 4 * dd && (food == 30 || df >= 4 * dd - 1);
                bool ok = foodOk && dh == dd && Mathf.Abs(dg - dk) < 0.01f && l.Store(Res.Hide).part == 0f;
                if (!ok && bad++ < 3)
                    sb.AppendLine($"  BAD step {i}: food {df:+0;-0}, hide {dh:+0;-0}, game -{dg:0.000}, kills {dk}, deposits {dd}");
            }
            int hideGot = l.Store(Res.Hide).whole - hide0;
            bool camp = bad == 0 && deposits >= 3 && hideGot == deposits && kills >= deposits && kills <= deposits + 1;
            // Store-cap gate retired 2026-10-03: island stores are unlimited (Kevin). (food <= 30 larder ceiling)
            sb.AppendLine($"larder {food}/30: kills {kills}, deposits {deposits}, hide +{hideGot} (part {l.Store(Res.Hide).part}), food {food0} -> {l.Store(Res.Food).whole}, game {game0:0} -> {l.Stock(Res.Game).standing:0.00}, spear {l.Store(Res.Spear).whole + l.Store(Res.Spear).part:0.00}  {(camp ? "ok" : "FAIL")}");
            pass &= camp;
        }

        // Save mid-stalk and mid-carry, and one long tick vs many short.
        {
            var a = Camp(0, 2, 6f);
            double now = 0.0;
            string phases = "";
            for (int i = 0; i < 700; i++)
            {
                now += q;
                a.Tick(now + 1e-3);
                var h = a.hands[0];
                string ph = !h.HuntTrip ? "none" : h.huntKilled ? "carry" : "stalk";
                if ((ph == "stalk" && !phases.Contains("S")) || (ph == "carry" && !phases.Contains("C")))
                {
                    phases += ph == "stalk" ? "S" : "C";
                    var b = JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(a));
                    var c = JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(a));
                    double end = now + 3.0 * TimeOfDay.WorkDaySeconds;
                    for (double t = now + q; t <= end + 1e-6; t += q) a.Tick(t + 1e-3);
                    b.Tick(end + 1e-3);                     // saved, then one long tick
                    for (double t = now + q; t <= end + 1e-6; t += q) c.Tick(t + 1e-3);
                    now = end;
                    bool same = Books(a) == Books(b) && Books(a) == Books(c);
                    sb.AppendLine($"save mid-{ph}: live {Books(a)} | saved+1 tick {Books(b)} | saved+steps {Books(c)}  {(same ? "ok" : "FAIL")}");
                    pass &= same;
                    if (phases.Length >= 2) break;
                }
            }
            if (phases.Length < 2) { sb.AppendLine("save test: never saw both phases  FAIL"); pass = false; }
        }

        sb.AppendLine(pass ? "PASS" : "FAIL");
        return sb.ToString();
    }

    static string Books(OutpostLedger l)
    {
        var h = l.hands[0];
        return $"food {l.Store(Res.Food).whole}+{l.Store(Res.Food).part:0.###} hide {l.Store(Res.Hide).whole}+{l.Store(Res.Hide).part:0.###} game {l.Stock(Res.Game).standing:0.###} "
               + $"trip {(h.HuntTrip ? (h.huntKilled ? "carry" : "stalk") : "none")} leg {h.Leg} {h.legLeft:0.000}m {h.workLeft:0.000}s at {h.wx:0.00},{h.wz:0.00}";
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
        sb.AppendLine($"game {game0:0.00} -> {game1:0.00} (kills {game0 - game1:0.00}), leg {hunter.Leg} (work left {hunter.workLeft:0.0}s), trip {(hunter.HuntTrip ? (hunter.huntKilled ? "carry" : "walk up/jab") : "none")}");
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

    // --- play-mode timeline (2026-09-27) ------------------------------------

    static readonly System.Collections.Generic.List<string> timeline = new System.Collections.Generic.List<string>();
    static string lastKey;

    /// Start logging a line every time the hunt's phase, the herd, Food
    /// whole, Hide (whole/part) or the carcass-on-shoulders changes. Read
    /// with `Timeline()`.
    public static string Watch()
    {
        timeline.Clear();
        lastKey = null;
        UnityEditor.EditorApplication.update -= Sample;
        UnityEditor.EditorApplication.update += Sample;
        return "HuntCheck.Watch: logging";
    }

    public static string Unwatch()
    {
        UnityEditor.EditorApplication.update -= Sample;
        return "HuntCheck.Unwatch";
    }

    static void Sample()
    {
        if (!Application.isPlaying || camp == null || camp.Ledger == null || hunter == null) return;
        var l = camp.Ledger;
        string phase = !hunter.HuntTrip ? "none" : hunter.huntKilled ? "carry" : "stalk";
        bool carried = false;
        foreach (var w in CampWorker.Bodies)
        {
            if (w == null) continue;
            var props = w.GetComponent<HunterProps>();
            if (props != null && props.HasCarcass) carried = true;
        }
        var food = l.Store(Res.Food);
        var hide = l.Store(Res.Hide);
        float game = l.Stock(Res.Game) != null ? l.Stock(Res.Game).standing : 0f;
        string key = $"{phase} game {game:0.00} food {(food != null ? food.whole : 0)} hide {(hide != null ? hide.whole : 0)}+{(hide != null ? hide.part : 0f):0.###} carcass {(carried ? "ON" : "-")}";
        if (key == lastKey) return;
        lastKey = key;
        timeline.Add($"t={TimeOfDay.Seconds:0.0} {key} (food part {(food != null ? food.part : 0f):0.00}, kills {hunter.huntKills}, deposits {hunter.huntDeposits})");
    }

    public static string Timeline()
    {
        return "HuntCheck.Timeline (" + timeline.Count + ")\n" + string.Join("\n", timeline);
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
