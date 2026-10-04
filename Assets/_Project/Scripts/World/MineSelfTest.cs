using System.Text;
using UnityEngine;

namespace SeaSick.World
{
    /// **Gates for the mine shaft (2026-10-05, Kevin's approved spec).**
    /// Run from the Unity CLI: `unity cmd eval --json --code
    /// 'SeaSick.World.MineSelfTest.Run()'` -- true when every counted gate
    /// passes; the report is logged either way.
    ///
    /// Books only (any time, no scene):
    /// (a) the plan: 8 timber, no stone, open at fire I, copies 1/1/2/2, a
    ///     station whose dig yields `MineLoad` stone and takes nothing;
    /// (b) one trip: an unordered, manned mine starts digging on its own; the
    ///     miner stands at the mouth (`MineMouthStand` out of the lip), his
    ///     dig timer is `MineTripSeconds` of work, carries 4 stone that are NOT in
    ///     the books while in his arms, and the rack gains exactly 4 the step
    ///     he drops them (delivery on arrival);
    /// (c) away == watched: the same mine ticked in 0.01-day steps and in one
    ///     2-day tick books the same stone, within one load;
    /// (d) no runners: a full container goes to the store on his own back;
    /// (e) level 2 shortens the trip by the station step (x1.5 rate).
    ///
    /// Live ground (play mode with a camp loaded -- `Outpost.Home`, else the
    /// camp being loaded; SKIPPED, not failed, when there is none):
    /// (f) the flat camp centre is refused at eight headings with
    ///     `Outpost.MineNeedsCliff`;
    /// (g) somewhere within 60 m of it a snap is accepted by `CanPlace`, and
    ///     the snapped heading faces OUT of the hill: the ground behind the
    ///     lip rises past the walk grade and the apron in front is lower.
    public static class MineSelfTest
    {
        public static bool Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            sb.AppendLine("MineSelfTest");
            Plan(sb, ref fails);
            Trip(sb, ref fails);
            AwayEqualsWatched(sb, ref fails);
            CarriesHome(sb, ref fails);
            LevelTwo(sb, ref fails);
            Ground(sb, ref fails);
            sb.AppendLine(fails == 0 ? "ALL PASS" : $"{fails} FAIL");
            if (fails == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
            return fails == 0;
        }

        static string Id => BuildPlans.Mine.id;

        // --- (a) ---------------------------------------------------------------

        static void Plan(StringBuilder sb, ref int fails)
        {
            var p = BuildPlans.Named(Id);
            var r = Economy.Recipes.Named(OutpostLedger.MineRecipeId);
            var l = new OutpostLedger { campfireLevel = 1 };
            int[] caps = { Economy.Techs.MaxCopies(Id, 1), Economy.Techs.MaxCopies(Id, 2),
                           Economy.Techs.MaxCopies(Id, 3), Economy.Techs.MaxCopies(Id, 4) };
            Gate(sb, ref fails, "mine-plan",
                p.id == Id && p.baseCost == 8 && p.baseStoneCost == 0 && p.kind == BuildKind.Mine
                && l.PlanUnlocked(Id) && OutpostLedger.IsStation(Id)
                && caps[0] == 1 && caps[1] == 1 && caps[2] == 2 && caps[3] == 2
                && r != null && r.station == Id && r.makes == Res.Stone && r.yield == BuildPlans.MineLoad
                && Economy.RecipeGraph.IsCatch(r),
                $"cost {p.baseCost} timber + {p.baseStoneCost} stone, fire I {l.PlanUnlocked(Id)}, "
                + $"copies {caps[0]}/{caps[1]}/{caps[2]}/{caps[3]}, dig {(r != null ? r.Describe() : "missing")}");
        }

        // --- (b) ---------------------------------------------------------------

        /// A mine 15 m east of the store, facing west (toward it), one miner.
        static OutpostLedger Camp(int level = 1, int runners = 0)
        {
            var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 1 };
            l.SetCentre(Vector3.zero);
            l.raised.Add(new BuiltBuilding { planId = Id, x = 15f, yaw = 270f, level = level });
            l.built.Add(Id);
            l.hands.Add(new OutpostHand { name = "Miner", order = OutpostOrder.Work, target = Id });
            for (int i = 0; i < runners; i++)
                l.hands.Add(new OutpostHand { name = "Idle" + i, order = OutpostOrder.Idle });
            l.Store(Res.Food, true).whole = 1000;
            l.lastTicked = 0.0;
            l.EnsureStations();
            return l;
        }

        static void Trip(StringBuilder sb, ref int fails)
        {
            var l = Camp();
            int si = l.StationIndex(Id, 0);
            var st = l.StationAt(si);
            var h = l.hands[0];
            bool mouthOk = l.ShoreOf(si, out var stand, out var inside)
                && Vector3.Distance(stand, new Vector3(15f - BuildPlans.MineMouthStand, 0f, 0f)) < 0.05f
                && inside.x > 15f;

            double now = 0.0;
            const double Step = 0.001;           // 0.18 s of a 180 s day
            float under = 0f;
            bool sawUnder = false, carriedUnbooked = false, ordered = false;
            int firstGain = -1, rackBefore = 0;
            for (int i = 0; i < 2000 && st != null && firstGain < 0; i++)
            {
                bool wasUnder = h.Hauling && h.haulFrom == HaulPlace.Shore && !h.haulPicked && h.Leg == TripLeg.AtPickup;
                Advance(l, ref now, Step);
                ordered |= st.HasOrder;
                bool isUnder = h.Hauling && h.haulFrom == HaulPlace.Shore && !h.haulPicked && h.Leg == TripLeg.AtPickup;
                if (wasUnder || isUnder) { under += (float)(Step * TimeOfDay.WorkDaySeconds); sawUnder = true; }
                if (h.Hauling && h.haulPicked && h.haulFrom == HaulPlace.Shore)
                    carriedUnbooked |= h.haulCount == BuildPlans.MineLoad && st.RackTotal == 0 && l.CountOf(Res.Stone) == 0;
                if (st.RackTotal != rackBefore) firstGain = st.RackTotal - rackBefore;
            }
            Gate(sb, ref fails, "mine-mouth-stand", mouthOk,
                $"stand {stand} inside {inside} for a lip at (15,0,0) facing -X");
            Gate(sb, ref fails, "mine-auto-order", ordered, "a manned mine with no order digs on repeat");
            // The dig is seconds of his EFFORT, paid at his `WorkFactor`
            // like every station's timer (mood, the day/night scale), so the
            // gate is the timer itself; the clock time is reported.
            float timer = l.MineTripSecondsAt(si) * Economy.EconomyFeel.StationSpeed;
            Gate(sb, ref fails, "mine-underground-time",
                sawUnder && Mathf.Abs(timer - BuildPlans.MineTripSeconds) <= 0.5f,
                $"dig timer {timer:0.0} s of work at feel x1 (want {BuildPlans.MineTripSeconds:0}); "
                + $"this camp's clock had him below {under:0.0} s");
            Gate(sb, ref fails, "mine-stone-booked-at-drop",
                carriedUnbooked && firstGain == BuildPlans.MineLoad,
                $"carried {BuildPlans.MineLoad} unbooked {carriedUnbooked}, rack's first gain {firstGain}");
        }

        // --- (c) ---------------------------------------------------------------

        static int Stone(OutpostLedger l) => l.CountOf(Res.Stone) + l.CarriedOf(Res.Stone);

        static void AwayEqualsWatched(StringBuilder sb, ref int fails)
        {
            var fine = Camp();
            var once = Camp();
            double a = 0.0, b = 0.0;
            for (int i = 0; i < 200; i++) Advance(fine, ref a, 0.01);
            Advance(once, ref b, 2.0);
            int sf = Stone(fine), so = Stone(once);
            Gate(sb, ref fails, "mine-away-equals-watched",
                sf > 0 && Mathf.Abs(sf - so) <= BuildPlans.MineLoad,
                $"2 days in 0.01 d steps {sf} stone, in one tick {so}");
        }

        // --- (d) ---------------------------------------------------------------

        static void CarriesHome(StringBuilder sb, ref int fails)
        {
            var l = Camp();
            double now = 0.0;
            int peak = 0;
            var st = l.StationOf(Id);
            for (int i = 0; i < 300; i++)
            {
                Advance(l, ref now, 0.01);
                if (st != null) peak = Mathf.Max(peak, st.RackTotal);
            }
            int stored = l.Store(Res.Stone) != null ? l.Store(Res.Stone).whole : 0;
            Gate(sb, ref fails, "mine-carries-home-without-runners",
                st != null && stored > 0 && peak <= st.OutputCap,
                $"3 days: {stored} stone in the store, container peaked {peak} of {(st != null ? st.OutputCap : 0)}");
        }

        // --- (e) ---------------------------------------------------------------

        static void LevelTwo(StringBuilder sb, ref int fails)
        {
            var one = Camp(1);
            var two = Camp(2);
            one.PlaceOrder(Id, OutpostLedger.MineRecipeId, OutpostLedger.RepeatOrder);
            two.PlaceOrder(Id, OutpostLedger.MineRecipeId, OutpostLedger.RepeatOrder);
            float s1 = one.MineTripSecondsAt(one.StationIndex(Id, 0));
            float s2 = two.MineTripSecondsAt(two.StationIndex(Id, 0));
            float mul = Economy.Techs.RateMul(Id, 2);
            Gate(sb, ref fails, "mine-level-2-faster",
                mul > 1f && Mathf.Abs(s2 - s1 / mul) < 0.5f,
                $"trip {s1:0.0} s at level 1, {s2:0.0} s at level 2 (rate x{mul:0.##})");
        }

        // --- (f), (g) ------------------------------------------------------------

        static void Ground(StringBuilder sb, ref int fails)
        {
            var camp = Outpost.Home != null ? Outpost.Home : CampLoading.Camp;
            if (camp == null || !camp.Sited)
            {
                sb.AppendLine("  SKIP mine-ground-* -- no surveyed camp in the scene (enter play mode on a save)");
                return;
            }
            var plan = BuildPlans.Mine;
            Vector3 c = camp.CampCentre;
            int refused = 0;
            string reasons = "";
            for (int k = 0; k < 8; k++)
            {
                bool ok = camp.CanPlace(plan, c, k * 45f, out string why);
                if (!ok && why == Outpost.MineNeedsCliff) refused++;
                else reasons += $" {k * 45}°:{(ok ? "accepted" : why)}";
            }
            Gate(sb, ref fails, "mine-ground-flat-refused", refused == 8,
                $"camp centre {c}: {refused}/8 headings refused with '{Outpost.MineNeedsCliff}'{reasons}");

            bool found = false, facesOut = false;
            string detail = "no accepted snap within 60 m of the camp centre";
            float grade = Walkability.Grade(Walkability.Feet.Man);
            for (float rad = 6f; rad <= 60f && !found; rad += 4f)
                for (int k = 0; k < 24 && !found; k++)
                {
                    float a = k * Mathf.PI * 2f / 24f;
                    Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * rad;
                    if (!camp.SnapMine(p, out Vector3 pivot, out float yaw, out _)) continue;
                    if (!camp.CanPlace(plan, pivot, yaw, out _)) continue;
                    found = true;
                    Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    Vector3 foot = pivot + fwd * BuildPlans.MineBuryMetres;
                    float h0 = camp.GroundAt(foot);
                    float behind = camp.GroundAt(foot - fwd * BuildPlans.MineCliffProbe) - h0;
                    float ahead = camp.GroundAt(foot + fwd * 2f) - h0;
                    facesOut = behind >= grade * BuildPlans.MineCliffProbe && ahead < behind * 0.5f;
                    detail = $"tap {p} -> lip {pivot} yaw {yaw:0}°, rise behind {behind:0.00} m over "
                        + $"{BuildPlans.MineCliffProbe} m (need {grade * BuildPlans.MineCliffProbe:0.00}), 2 m ahead {ahead:+0.00;-0.00} m"
                        + (facesOut ? "" : " -- NOT FACING OUT");
                }
            Gate(sb, ref fails, "mine-ground-slope-accepted-facing-out", found && facesOut, detail);
        }

        // --- harness -----------------------------------------------------------

        /// Through `Tick`, the game's own door (the quantum is the game's).
        static void Advance(OutpostLedger l, ref double now, double days)
        {
            now += days * TimeOfDay.WorkDaySeconds;
            l.Tick(now + 1e-3);
        }

        static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
        {
            if (!ok) fails++;
            sb.Append(ok ? "  PASS " : "  FAIL ").Append(name).Append(" -- ").AppendLine(detail);
        }
    }
}
