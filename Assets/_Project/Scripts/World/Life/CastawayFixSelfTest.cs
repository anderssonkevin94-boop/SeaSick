using System.Collections.Generic;

namespace SeaSick.World.Life
{
    /// **Gate for the 2026-10-04 castaway/berth fix** (Kevin's phone: "IN THE
    /// WATER · No free berth" at his own home pier). Plain C#, no scene:
    ///
    ///   tools/selftest-outside-editor/run.sh SeaSick.World.Life.CastawayFixSelfTest.Run
    ///   unity cmd eval --json --code 'return SeaSick.World.Life.CastawayFixSelfTest.Run();'
    ///
    /// Fixtures are his save's own people (seasick-save-a2.json, day 795):
    /// Mara (washed up on Island_6 day 650, lived on aboard to day 708),
    /// Mabel (washed up on Island_6 day 649, nobody since), Dorrit (a
    /// stranger CastawayField made up). Returns "PASS n/n" or the failures.
    public static class CastawayFixSelfTest
    {
        public static string Run()
        {
            var sb = new System.Text.StringBuilder();
            int pass = 0, total = 0;
            void Check(string name, bool ok, string detail = "")
            {
                total++;
                if (ok) { pass++; return; }
                sb.Append("FAIL ").Append(name).Append(detail.Length > 0 ? ": " + detail : "").Append('\n');
            }

            // ---- (a) berths on a modular plan --------------------------------
            var plan = new SeaSick.Ship.Modular.ShipyardPlan
            {
                capacity = new SeaSick.Ship.Modular.ShipCapacity { crewStations = 12 },
            };
            int berths = SeaSick.Ship.CrewBerths.OfPlan(plan);
            Check("modular plan: berths are the plan's crew stations", berths == 12, "got " + berths);
            Check("modular plan: 5 aboard of 12 is not refused (Kevin's ship)",
                  SeaSick.Ship.CrewBerths.Refusal(5, berths) == null);
            Check("modular plan: 12 aboard of 12 is refused",
                  SeaSick.Ship.CrewBerths.Refusal(12, berths) != null);
            Check("refusal names the capacity, not the head count",
                  (SeaSick.Ship.CrewBerths.Refusal(12, 12) ?? "").Contains("12 taken"));
            Check("untouched steamer: the reference ship's berths",
                  SeaSick.Ship.CrewBerths.OfPlan(null) == SeaSick.Ship.Modular.ShipyardPlanner.ReferenceCrewStations);
            Check("ladder brig: a stood-down yard's 0 still floors at 1 (brig only)",
                  SeaSick.Ship.CrewBerths.OfRung(0) == 1);

            // ---- (b) the castaway card never hides the dock card -------------
            int land = SeaSick.UI.Sheets.SeaActions.PriorityLand;
            Check("alongside / at a pier, no berth: no castaway card",
                  SeaSick.Ship.Overboard.CastawayHud.CardPriority(true, true) < 0);
            Check("alongside / at a pier, a berth free: no castaway card either",
                  SeaSick.Ship.Overboard.CastawayHud.CardPriority(true, false) < 0);
            int info = SeaSick.Ship.Overboard.CastawayHud.CardPriority(false, true);
            Check("no berth at sea: an info card under the land/dock card",
                  info == SeaSick.UI.Sheets.SeaActions.PriorityOther && info < land, "got " + info);
            Check("a berth free at sea: Take aboard still outranks landing",
                  SeaSick.Ship.Overboard.CastawayHud.CardPriority(false, false) > land);

            // ---- (c) identity, not a name match ------------------------------
            var mara = Life("Mara", Ev(LifeEvents.WashedAshore, "Island_6", 650),
                            Ev(LifeEvents.Overboard, "", 680), Ev(LifeEvents.WentAboard, "Island_3", 708));
            var mabel = Life("Mabel", Ev(LifeEvents.Born, "Island_6", 111),
                             Ev(LifeEvents.Overboard, "", 649), Ev(LifeEvents.WashedAshore, "Island_6", 649));
            var dorrit = Life("Dorrit");
            var unknown = Life("Wren", Ev(LifeEvents.WentAboard, "Island_6", 300));
            var cMara = Rec("Mara", "Island_6");
            var cMabel = Rec("Mabel", "Island_6");
            var cDorrit = Rec("Dorrit", "Island_8");
            var cWren = Rec("Wren", "Island_6");

            Check("Mara: his by her own life log", CastawayRepair.IsOneOfOurs(cMara, mara, false));
            Check("Dorrit: a stranger is never his", !CastawayRepair.IsOneOfOurs(cDorrit, dorrit, true));
            Check("a stranger stays a stranger even if a body aboard wears the name",
                  CastawayRepair.Decide(cDorrit, dorrit, true, false, true, true) == CastawayRepair.Verdict.Keep);
            Check("no provenance at all: left as a rescuable castaway",
                  CastawayRepair.Decide(cWren, unknown, false, false, false, true) == CastawayRepair.Verdict.Keep);
            Check("exCrew flag alone is provenance (new records)",
                  CastawayRepair.IsOneOfOurs(new CastawayRecord { name = "X", island = "I", exCrew = true }, null, false));

            Check("Mara: aboard and lived on -> the castaway copy goes",
                  CastawayRepair.Decide(cMara, mara, false, false, true, true) == CastawayRepair.Verdict.DropDuplicate);
            Check("a body wearing the name with no life since -> NOT a duplicate (impostor body)",
                  CastawayRepair.Decide(cMabel, mabel, false, false, true, true) == CastawayRepair.Verdict.WalkToCamp);
            Check("Mabel: his, nobody else is her, camp on the island -> walks to the camp",
                  CastawayRepair.Decide(cMabel, mabel, false, false, false, true) == CastawayRepair.Verdict.WalkToCamp);
            Check("his, no camp on the island -> waits for the ship",
                  CastawayRepair.Decide(cMabel, mabel, false, false, false, false) == CastawayRepair.Verdict.Keep);
            Check("a ledger row already holds them -> the castaway copy goes",
                  CastawayRepair.Decide(cMabel, mabel, false, true, false, true) == CastawayRepair.Verdict.DropDuplicate);

            // ---- (c) the repair is idempotent --------------------------------
            var world = new FakeWorld();
            world.lives["Mara"] = mara; world.lives["Mabel"] = mabel; world.lives["Dorrit"] = dorrit;
            world.strangers.Add("Dorrit");
            world.aboard.Add("Mara");
            world.camps.Add("Island_6");
            var list = new List<CastawayRecord> { cDorrit, cMabel, cMara };

            int changed1 = ApplyOnce(list, world);
            Check("first run: Mara dropped, Mabel walked home, Dorrit kept",
                  changed1 == 2 && list.Count == 1 && list[0].name == "Dorrit" && world.rows.Contains("Mabel"),
                  "changed " + changed1 + ", left " + list.Count);
            int changed2 = ApplyOnce(list, world);
            Check("second run changes nothing", changed2 == 0 && list.Count == 1, "changed " + changed2);

            // Interrupted half way: the row was added, the record survived.
            var list3 = new List<CastawayRecord> { Rec("Mabel", "Island_6") };
            int changed3 = ApplyOnce(list3, world);
            Check("a record left beside its own row is dropped, not walked twice",
                  changed3 == 1 && list3.Count == 0 && CountRows(world, "Mabel") == 1);

            string head = "PASS " + pass + "/" + total;
            return pass == total ? head : head + "\n" + sb;
        }

        // ---- fixtures -------------------------------------------------------

        static LifeEvent Ev(string kind, string camp, int day) =>
            new LifeEvent { kind = kind, camp = camp, day = day };

        static LifeRecord Life(string name, params LifeEvent[] events) =>
            new LifeRecord { name = name, events = new List<LifeEvent>(events) };

        static CastawayRecord Rec(string name, string island) =>
            new CastawayRecord { name = name, island = island };

        sealed class FakeWorld : CastawayRepair.IWorld
        {
            public readonly Dictionary<string, LifeRecord> lives = new Dictionary<string, LifeRecord>();
            public readonly HashSet<string> strangers = new HashSet<string>();
            public readonly List<string> rows = new List<string>();
            public readonly HashSet<string> aboard = new HashSet<string>();
            public readonly HashSet<string> camps = new HashSet<string>();
            public LifeRecord Life(string n) => lives.TryGetValue(n, out var r) ? r : null;
            public bool IsStranger(string n) => strangers.Contains(n);
            public bool InLedger(string n) => rows.Contains(n);
            public bool BodyAboard(string n) => aboard.Contains(n);
            public bool CampOnIsland(string i) => camps.Contains(i);
        }

        static int CountRows(FakeWorld w, string n)
        {
            int k = 0;
            foreach (var r in w.rows) if (r == n) k++;
            return k;
        }

        /// The scene half's bookkeeping, on the fake: a dropped record goes,
        /// a walked one becomes a row and goes. Returns records changed.
        static int ApplyOnce(List<CastawayRecord> list, FakeWorld w)
        {
            int changed = 0;
            foreach (var kv in CastawayRepair.Plan(list, w))
            {
                if (kv.Value == CastawayRepair.Verdict.Keep) continue;
                if (kv.Value == CastawayRepair.Verdict.WalkToCamp && !w.rows.Contains(kv.Key.name))
                    w.rows.Add(kv.Key.name);
                list.Remove(kv.Key);
                changed++;
            }
            return changed;
        }
    }
}
