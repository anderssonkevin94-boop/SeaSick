using System.Text;
using UnityEngine;
using SeaSick.World.Economy;

namespace SeaSick.World
{
    /// **Plain-C# gate for the two farm-sheet bugs (2026-10-04, Kevin).**
    /// No scene: one bare ledger with a level-2 farm and hand-made plots.
    /// Run: `tools/selftest-outside-editor/run.sh SeaSick.World.FarmPlotSelfTest.Run`
    /// or `unity cmd eval --json --code 'SeaSick.World.FarmPlotSelfTest.Run()'`.
    ///
    /// Bug 1 -- the sheet showed the crop's FULL yield on a part-harvested
    /// plot: the tile line (`FarmPlot.StateLine`) must read `RipeUnits`.
    /// Bug 2 -- `SetPlotCrop` on a ripe plot relabelled the standing harvest
    /// as the new crop: now the pick is queued (`nextCrop`), the ripe crop
    /// keeps its identity and units, `Harvested()` applies the queue, and
    /// the queue survives a save (JsonUtility; old saves read "" = none).
    public static class FarmPlotSelfTest
    {
        public static bool Run()
        {
            var sb = new StringBuilder("FarmPlotSelfTest\n");
            int fails = 0;
            var l = new OutpostLedger();
            l.raised.Add(new BuiltBuilding { planId = BuildPlans.Farm.id, level = 2 });
            System.Func<string, string> name = r => r;

            // --- bug 1: tile line shows what is still standing ----------------
            var part = new FarmPlot { crop = Res.Wheat, state = PlotState.Ripe, left = 3 };
            var whole = new FarmPlot { crop = Res.Wheat, state = PlotState.Ripe };
            string partLine = part.StateLine(name, "0:00"), wholeLine = whole.StateLine(name, "0:00");
            Gate(sb, ref fails, "part-harvested-shows-remaining",
                part.RipeUnits == 3 && partLine == "ripe · 3", $"'{partLine}' (yield {FoodBook.Crop(Res.Wheat).yield})");
            Gate(sb, ref fails, "untouched-ripe-shows-full-yield",
                wholeLine == $"ripe · {FoodBook.Crop(Res.Wheat).yield}", $"'{wholeLine}'");

            // --- bug 2: a ripe plot keeps its harvest, the pick is queued ------
            var p = new FarmPlot { crop = Res.Wheat, state = PlotState.Ripe, left = 6, repeat = true };
            l.plots.Add(p);
            bool ok = l.SetPlotCrop(p, Res.Potato, true);
            Gate(sb, ref fails, "ripe-keeps-identity-and-amount",
                ok && p.crop == Res.Wheat && p.state == PlotState.Ripe && p.RipeUnits == 6 && p.nextCrop == Res.Potato,
                $"crop {p.crop}, {p.RipeUnits} ripe, next '{p.nextCrop}'");
            Gate(sb, ref fails, "tile-says-next",
                p.StateLine(name, "").EndsWith("next: " + Res.Potato) && p.StateLine(name, "").StartsWith("ripe · 6"),
                $"'{p.StateLine(name, "").Replace("\n", " / ")}'");

            // save round-trip of the queued crop (and a part-ripe count)
            var copy = JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(l));
            var cp = copy.plots.Count == 1 ? copy.plots[0] : null;
            Gate(sb, ref fails, "queued-crop-survives-save",
                cp != null && cp.crop == Res.Wheat && cp.nextCrop == Res.Potato && cp.RipeUnits == 6 && cp.state == PlotState.Ripe,
                cp == null ? "no plot" : $"crop {cp.crop}, next '{cp.nextCrop}', {cp.RipeUnits} ripe");
            var old = JsonUtility.FromJson<FarmPlot>("{\"crop\":\"" + Res.Wheat + "\",\"state\":2,\"repeat\":true}");
            Gate(sb, ref fails, "old-save-has-no-queue", old != null && !old.HasNext && old.nextCrop == "", $"next '{old?.nextCrop}'");

            // re-picking the standing crop cancels the queue; repeat toggle keeps it
            l.SetPlotRepeat(p, false);
            Gate(sb, ref fails, "repeat-toggle-keeps-queue", p.nextCrop == Res.Potato && !p.repeat, $"next '{p.nextCrop}', repeat {p.repeat}");
            l.SetPlotCrop(p, Res.Wheat, true);
            Gate(sb, ref fails, "repick-standing-cancels-queue", p.nextCrop == "" && p.crop == Res.Wheat && p.RipeUnits == 6, $"next '{p.nextCrop}'");

            // the queue applies after the harvest
            l.SetPlotCrop(p, Res.Potato, false);
            string picked = copy.plots[0].Harvested();   // the loaded copy, queue intact
            var cq = copy.plots[0];
            Gate(sb, ref fails, "queue-applies-after-harvest",
                picked == Res.Wheat && cq.crop == Res.Potato && cq.nextCrop == "" && cq.state == PlotState.Empty && cq.left == 0,
                $"basket crop {picked}; plot now {cq.crop}/{cq.state}, next '{cq.nextCrop}'");
            string picked2 = p.Harvested();
            Gate(sb, ref fails, "queue-wins-over-repeat-off",
                picked2 == Res.Wheat && p.crop == Res.Potato && p.state == PlotState.Empty, $"plot now {p.crop} (repeat {p.repeat})");

            // no queue: repeat keeps the crop, no repeat clears (unchanged)
            var rep = new FarmPlot { crop = Res.Wheat, state = PlotState.Ripe, repeat = true };
            var one = new FarmPlot { crop = Res.Wheat, state = PlotState.Ripe, repeat = false };
            rep.Harvested(); one.Harvested();
            Gate(sb, ref fails, "no-queue-old-behaviour", rep.crop == Res.Wheat && one.crop == "", $"repeat '{rep.crop}', once '{one.crop}'");

            // growing / empty plots still change at once (nothing standing to lose)
            var g = new FarmPlot { crop = Res.Potato, state = PlotState.Growing, grown = 0.1f };
            l.plots.Add(g);
            l.SetPlotCrop(g, Res.Carrot, true);
            Gate(sb, ref fails, "growing-plot-dug-up-at-once",
                g.crop == Res.Carrot && g.state == PlotState.Empty && !g.HasNext, $"{g.crop}/{g.state}");

            // ripe + Clear: harvest still picked, then bare; locked crop refused
            var c = new FarmPlot { crop = Res.Wheat, state = PlotState.Ripe, repeat = true, nextCrop = Res.Potato };
            l.SetPlotCrop(c, "", true);
            c.Harvested();
            Gate(sb, ref fails, "ripe-clear-picks-then-bare", c.crop == "" && !c.HasNext, $"crop '{c.crop}'");
            var lock3 = new FarmPlot { crop = Res.Wheat, state = PlotState.Ripe };
            bool refused = !l.SetPlotCrop(lock3, Res.Apple, true);
            Gate(sb, ref fails, "locked-crop-refused-no-queue", refused && !lock3.HasNext, $"refused {refused}");

            sb.AppendLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
            Debug.Log(sb.ToString());
            return fails == 0;
        }

        static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {name}: {detail}");
            if (!ok) fails++;
        }
    }
}
