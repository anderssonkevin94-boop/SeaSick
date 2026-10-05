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
    ///     A Stop sticks for that miner; a newly assigned one restarts it.
    /// (c) away == watched: the same mine ticked in 0.01-day steps and in one
    ///     2-day tick books the same stone, within one load;
    /// (d) no runners: a full container goes to the store on his own back;
    /// (e) level 2 shortens the trip by the station step (x1.5 rate).
    ///
    /// Live ground (play mode with a camp loaded -- `Outpost.Home`, else the
    /// camp being loaded; SKIPPED, not failed, when there is none):
    /// (f) the flat camp centre is refused at eight headings with
    ///     `Outpost.MineNeedsCliff`;
    /// (g) somewhere within 250 m of it a snap is accepted by `CanPlace`, and
    ///     the snapped heading faces OUT of the hill: the ground behind the
    ///     lip rises past the walk grade and the apron in front is lower;
    ///     and the hill stands behind the art's left, centre and right.
    /// (b') booked at the drop, with a body driving the legs.
    public static class MineSelfTest
    {
        public static bool Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            sb.AppendLine("MineSelfTest");
            Plan(sb, ref fails);
            Trip(sb, ref fails);
            BookedAtDrop(sb, ref fails);
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
            bool sawUnder = false, ordered = false;
            int firstGain = -1, rackBefore = 0;
            for (int i = 0; i < 2000 && st != null && firstGain < 0; i++)
            {
                bool wasUnder = h.Hauling && h.haulFrom == HaulPlace.Shore && !h.haulPicked && h.Leg == TripLeg.AtPickup;
                Advance(l, ref now, Step);
                ordered |= st.HasOrder;
                bool isUnder = h.Hauling && h.haulFrom == HaulPlace.Shore && !h.haulPicked && h.Leg == TripLeg.AtPickup;
                if (wasUnder || isUnder) { under += (float)(Step * TimeOfDay.WorkDaySeconds); sawUnder = true; }
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
            // The player's Stop sticks: no auto-restart for the same miner;
            // a newly assigned one starts it again.
            l.StopOrder(si);
            for (int i = 0; i < 50; i++) Advance(l, ref now, Step);
            bool stuck = !st.HasOrder;
            var other = new OutpostHand { name = "Second", order = OutpostOrder.Work, target = Id };
            h.order = OutpostOrder.Idle; h.target = null;
            for (int i = 0; i < 5; i++) Advance(l, ref now, Step);
            l.hands.Add(other);
            for (int i = 0; i < 50; i++) Advance(l, ref now, Step);
            Gate(sb, ref fails, "mine-stop-sticks", stuck && st.HasOrder,
                $"stopped and stayed stopped {stuck}; a newly assigned miner restarted it {st.HasOrder}");
            Gate(sb, ref fails, "mine-first-gain-is-a-load", firstGain == BuildPlans.MineLoad,
                $"unwatched: the rack's first gain {firstGain} (want {BuildPlans.MineLoad})");
        }

        /// **Booked at the drop, watched (2026-10-05 fix round).** Unwatched,
        /// the walk out and the drop can land in one tick, so the carried,
        /// unbooked state is never seen. Here a body drives the legs one
        /// event at a time (`BodyAt` / `BodyWorked` / `BodyArrived`, what
        /// `CampWorker` reports): after the dig the 4 stone are in his arms
        /// and NOT in the rack or the store; the rack gains exactly 4 at the
        /// drop.
        static void BookedAtDrop(StringBuilder sb, ref int fails)
        {
            var l = Camp();
            var h = l.hands[0];
            int si = l.StationIndex(Id, 0);
            var st = l.StationAt(si);
            double now = 0.0;
            for (int i = 0; i < 400 && !(h.Hauling && h.haulFrom == HaulPlace.Shore); i++)
                Advance(l, ref now, 0.0005);
            bool started = st != null && h.Hauling && h.haulFrom == HaulPlace.Shore && !h.haulPicked;
            if (started && l.ShoreOf(si, out var stand, out _)) l.BodyAt(h, stand);
            bool carriedUnbooked = false;
            int gain = -1;
            string legs = "";
            for (int i = 0; i < 12 && started && gain < 0; i++)
            {
                int before = st.RackTotal;
                if (h.Hauling && h.Leg == TripLeg.AtPickup) l.BodyWorked(h, 10000f);
                else l.BodyArrived(h);
                if (h.Hauling && h.haulPicked && h.haulFrom == HaulPlace.Shore)
                    carriedUnbooked |= h.haulCount == BuildPlans.MineLoad && st.RackTotal == 0 && l.CountOf(Res.Stone) == 0;
                if (st.RackTotal != before) gain = st.RackTotal - before;
                legs += " " + (h.Hauling ? h.Leg.ToString() : "-");
            }
            Gate(sb, ref fails, "mine-stone-booked-at-drop",
                started && carriedUnbooked && gain == BuildPlans.MineLoad,
                $"bodied trip started {started}; carried {BuildPlans.MineLoad} unbooked {carriedUnbooked}, "
                + $"rack's gain at the drop {gain}; legs{legs}");
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

            // **Out to `GroundReach` (2026-10-05 fix round):** Kevin's only
            // good cliffs stood 160 m from his fire. Only spots with a steep
            // rise within a few metres are snapped (the snap is the costly
            // part), nearest ring first; the first accepted site is checked.
            bool found = false, facesOut = false, hillBehind = false;
            string detail = $"no accepted snap within {GroundReach:0} m of the camp centre";
            float grade = Walkability.Grade(Walkability.Feet.Man);
            int snaps = 0;
            for (float rad = 6f; rad <= GroundReach && !found; rad += 5f)
            {
                int n = Mathf.Max(24, Mathf.CeilToInt(2f * Mathf.PI * rad / 6f));
                for (int k = 0; k < n && !found; k++)
                {
                    float a = k * Mathf.PI * 2f / n;
                    Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * rad;
                    if (!SteepNear(camp, p, grade)) continue;
                    snaps++;
                    if (!camp.SnapMine(p, out Vector3 pivot, out float yaw, out _)) continue;
                    if (!camp.CanPlace(plan, pivot, yaw, out _)) continue;
                    found = true;
                    var q = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 fwd = q * Vector3.forward, right = q * Vector3.right;
                    Vector3 foot = pivot + fwd * BuildPlans.MineBuryMetres;
                    float h0 = camp.GroundAt(foot);
                    float behind = camp.GroundAt(foot - fwd * BuildPlans.MineCliffProbe) - h0;
                    float ahead = camp.GroundAt(foot + fwd * 2f) - h0;
                    facesOut = behind >= grade * BuildPlans.MineCliffProbe && ahead < behind * 0.5f;
                    // The hill behind the left, centre and right of the art.
                    hillBehind = true;
                    string cols = "";
                    for (int s = -1; s <= 1; s++)
                    {
                        Vector3 b = foot + right * (s * BuildPlans.MineBackHalfWidth);
                        float near = camp.GroundAt(b - fwd * BuildPlans.MineBackNearDepth) - h0;
                        float far = camp.GroundAt(b - fwd * BuildPlans.MineBackFarDepth) - h0;
                        hillBehind &= near >= BuildPlans.MineBackNearRise && far >= BuildPlans.MineBackFarRise;
                        cols += $" {(s < 0 ? "L" : s == 0 ? "C" : "R")} {near:0.0}/{far:0.0}";
                    }
                    detail = $"tap {p} ({rad:0} m out, {snaps} snaps) -> lip {pivot} yaw {yaw:0}°, rise behind {behind:0.00} m over "
                        + $"{BuildPlans.MineCliffProbe} m (need {grade * BuildPlans.MineCliffProbe:0.00}), 2 m ahead {ahead:+0.00;-0.00} m; "
                        + $"hill behind at {BuildPlans.MineBackNearDepth}/{BuildPlans.MineBackFarDepth} m:{cols} "
                        + $"(need {BuildPlans.MineBackNearRise}/{BuildPlans.MineBackFarRise})"
                        + (facesOut ? "" : " -- NOT FACING OUT") + (hillBehind ? "" : " -- HILL MISSING BEHIND");
                }
            }
            Gate(sb, ref fails, "mine-ground-slope-accepted-facing-out", found && facesOut, detail);
            Gate(sb, ref fails, "mine-ground-hill-behind-whole-width", found && hillBehind,
                found ? "left, centre and right of the art's back sunk in the hill" : detail);
        }

        /// How far from the camp centre the accepted-site search looks, m.
        const float GroundReach = 250f;

        /// A rise steeper than a man walks within 3 m of `p`, any of 8 ways.
        static bool SteepNear(Outpost camp, Vector3 p, float grade)
        {
            float h = camp.GroundAt(p);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Vector3 q = p + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3f;
                if (camp.GroundAt(q) - h >= grade * 3f) return true;
            }
            return false;
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
