using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// One hand's part in a building site, for the site sheet: "Gale is
    /// fetching timber (2 in her arms)", "Tam is building".
    public struct SiteHand
    {
        public OutpostHand hand;
        /// "fetching timber", "cutting timber", "quarrying stone",
        /// "clearing the plot", "building", "waiting".
        public string verb;
        /// What is in the arms on the way to the site, or null.
        public string res;
        public int load;
    }

    /// <summary>
    /// **Who is doing what at a building site, and the hammer clock
    /// (Kevin, 2026-09-27).** *"we are just talking about building time
    /// here, so that timer is only active/relevant when resources are in
    /// place and someone is working on the building"* and *"it would be
    /// nice to have information such as 'x is gathering resources for the
    /// build, or y is building'."*
    ///
    /// The RULE (enforced by `PayBuild`, which is only reached from
    /// `BuilderDay` on a site that is `Stocked` and `Cleared`, out of a
    /// builder's own labour): the hammer timer runs ONLY while every
    /// material is delivered AND at least one builder is standing at the
    /// site (on the Build order, not walking a load). N builders hammer
    /// `EconomyTuning.CrewSpeed(N)` = N^0.75 times as fast as one --
    /// diminishing returns. Otherwise it is paused, and the site says why:
    /// "waiting for 3 stone" or "no builder".
    ///
    /// Read-only. The HAULING state it reads (`OutpostHand.Hauling`,
    /// `haulRes`, `haulCount`, `haulFrom`, `haulTo`) belongs to the
    /// delivery code; this file only interprets it, so whoever changes how
    /// loads arrive keeps these fields meaning "what is in his arms and
    /// where it is going" and the sheet stays right.
    /// </summary>
    public partial class OutpostLedger
    {
        /// Build-order hands standing at a site rather than walking a load:
        /// the crew whose labour `PayBuild` spends.
        public int HammerCrew()
        {
            int n = 0;
            if (hands == null) return 0;
            foreach (var h in hands)
                if (h != null && h.order == OutpostOrder.Build && !h.Hauling) n++;
            return n;
        }

        /// The site a free builder is working right now, mirroring
        /// `BuilderDay`'s walk down the queue: the first unfinished site
        /// that is being cleared or hammered. Null when every site is
        /// waiting on materials.
        public PendingBuild WorkingSite
        {
            get
            {
                if (sites == null) return null;
                foreach (var s in sites)
                {
                    if (s == null || s.Complete) continue;
                    if (!s.Cleared || s.Stocked) return s;
                }
                return null;
            }
        }

        /// The oldest site still short of `res` -- the one a load of it on
        /// its way lands in (deliveries fill the queue oldest-first).
        PendingBuild SiteForLoad(string res)
        {
            if (sites == null) return null;
            foreach (var s in sites)
                if (s != null && !s.Complete && RemainingOf(s, res) > 0) return s;
            return null;
        }

        /// **Everybody whose current task is this site**, with what they are
        /// doing. Builders walking a load count for the site it lands in;
        /// builders at the camp count for `WorkingSite`.
        public List<SiteHand> WhoIsOn(PendingBuild site)
        {
            var list = new List<SiteHand>();
            if (site == null || hands == null) return list;
            foreach (var h in hands)
            {
                if (h == null || h.order != OutpostOrder.Build) continue;
                if (h.Hauling)
                {
                    if (h.haulTo != HaulPlace.Site || SiteForLoad(h.haulRes) != site) continue;
                    string label = ResLabel(h.haulRes);
                    string verb = h.haulFrom == HaulPlace.Field
                        ? (h.haulRes == Res.Timber ? "cutting timber" : h.haulRes == Res.Stone ? "quarrying stone" : "fetching " + label)
                        : "fetching " + label;
                    list.Add(new SiteHand { hand = h, verb = verb, res = h.haulRes, load = h.haulCount });
                    continue;
                }
                if (BuildSiteFor(h) != site) continue;
                list.Add(new SiteHand { hand = h, verb = !site.Cleared ? "clearing the plot" : "building" });
            }
            return list;
        }

        static string ResLabel(string res) => Economy.ResDefs.Label(res);

        /// **Seconds of hammering left at the crew standing there now**, or
        /// -1 when the timer is paused (not stocked, not cleared, or nobody
        /// at the site). Real seconds at 1x time, before hunger.
        public float HammerSecondsLeft(PendingBuild p)
        {
            if (p == null || !p.Stocked || !p.Cleared || p.Complete) return -1f;
            int crew = HammerCrew(p);
            if (crew <= 0) return -1f;
            float days = Mathf.Max(0f, p.LabourNeeded - p.built) / Economy.EconomyTuning.CrewSpeed(crew);
            return days * TimeOfDay.WorkDaySeconds;
        }

        /// **The site's one status line**: "hammering · 42 s left" only
        /// while the timer runs; otherwise what it is waiting for --
        /// "clearing the plot", "waiting for 3 stone", "no builder",
        /// "waiting its turn".
        public string SiteLine(PendingBuild p)
        {
            if (p == null) return "";
            if (p.Complete) return "going up";
            bool anyBuilder = false;
            foreach (var h in hands)
                if (h != null && h.order == OutpostOrder.Build) { anyBuilder = true; break; }
            if (p.Stocked && p.Cleared)
            {
                float s = HammerSecondsLeft(p);
                if (s >= 0f) return $"hammering · {Mathf.CeilToInt(s)} s left";
                return anyBuilder ? "stocked · a builder is on the way" : "stocked · no builder";
            }
            if (!p.Cleared)
                return anyBuilder ? "clearing the plot" : "no builder to clear the plot";
            var want = new List<string>(3);
            int t = Mathf.Max(0, p.needed - p.done), st = Mathf.Max(0, p.stoneNeeded - p.stoneDone),
                b = Mathf.Max(0, p.brickNeeded - p.brickDone);
            if (t > 0) want.Add($"{t} timber");
            if (st > 0) want.Add($"{st} stone");
            if (b > 0) want.Add(b == 1 ? "1 brick" : $"{b} bricks");
            string need = "waiting for " + string.Join(", ", want);
            return anyBuilder ? need : need + " · no builder";
        }
    }
}
