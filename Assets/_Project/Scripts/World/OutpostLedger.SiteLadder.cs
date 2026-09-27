using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **What a free hand does about the blueprints (Kevin, 2026-09-27).**
    /// *"I have two blueprints that need resources but right now they just
    /// stand around the campfire."* His ladder, for a hand on the Build order
    /// (which every idle hand is enlisted into by `EnlistFree` while a
    /// blueprint stands):
    ///
    /// 1. a site short of something the STORE (or a station's output rack)
    ///    has -> fetch it there (oldest site first; each armful is capped at
    ///    what is still short net of loads already walking, so several hands
    ///    split across materials and sites instead of all taking the same);
    /// 2. a site with work ON the plot (clearing, or hammering once every
    ///    material is in) -> go and do it; the site with the fewest hands on
    ///    it first, oldest breaking ties, so two sites are both worked;
    /// 3. a site short of timber/stone the store lacks -> cut or quarry it
    ///    off the island straight to the site;
    /// 4. nothing -> the site shows an ISSUE (`SiteIssue`) when nothing can
    ///    supply it, and the hand waits by the fire.
    ///
    /// A hand keeps the plot he was sent to (`workSite`) until its plot work
    /// is done, then asks the ladder again. The BODY reads the same answer
    /// (`BuildSiteFor`), which is the bug this fixes: bodies walked to
    /// `Focus` (the oldest unfinished site) while the books walked the queue,
    /// so with the first site blocked on a material nobody could get, the
    /// books waited for a body at the second site that never came, and the
    /// crew stood at the fire.
    /// </summary>
    public partial class OutpostLedger
    {
        /// The plot each hand was sent to clear or hammer. Not saved: after a
        /// load the ladder simply chooses again.
        [System.NonSerialized] readonly Dictionary<OutpostHand, PendingBuild> workSite =
            new Dictionary<OutpostHand, PendingBuild>();

        /// **Stuck-hand safeguard.** A DRIVEN body only walks toward a
        /// clearing plot while it holds a claim on a tree/rock there
        /// (`Outpost.ClaimClearing`, `CampWorker`); when every obstruction on
        /// his plot is somebody else's (or already gone), the body parks
        /// where it stands and never approaches -- so `WalkToPlot`'s reach
        /// check fails forever and the ladder's sticky `workSite` would hold
        /// him on a plot he can never work. Distinguishes that from an
        /// ordinary approach (still walking, just not there yet) by whether
        /// the body has actually MOVED since the last failed check; a hand
        /// parked in the same spot for `StallLimit` consecutive ladder steps
        /// is dropped back to the ladder so the next `LadderStep` picks again
        /// (a fetch trip, a different plot, or the fire).
        [System.NonSerialized] readonly Dictionary<OutpostHand, int> clearStall =
            new Dictionary<OutpostHand, int>();
        [System.NonSerialized] readonly Dictionary<OutpostHand, Vector3> clearStallPos =
            new Dictionary<OutpostHand, Vector3>();
        const int StallLimit = 4;

        static bool PlotWork(PendingBuild s) =>
            s != null && !s.Complete && (!s.Cleared || s.Stocked);

        PendingBuild StickySite(OutpostHand h)
        {
            if (h == null || !workSite.TryGetValue(h, out var s)) return null;
            if (sites == null || !PlotWork(s) || !sites.Contains(s)) { workSite.Remove(h); return null; }
            return s;
        }

        /// **The plot this builder is working** (clearing or hammering), or
        /// null when he is fetching or has nothing on a plot. The body walks
        /// here; the site sheet names him on it.
        public PendingBuild BuildSiteFor(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Build) return null;
            return StickySite(h);
        }

        /// Builders on this plot and not walking a load.
        public int HammerCrew(PendingBuild site)
        {
            if (site == null || hands == null) return 0;
            int n = 0;
            foreach (var h in hands)
                if (h != null && !h.downed && !h.Hauling && BuildSiteFor(h) == site) n++;
            return n;
        }

        /// One rung of the ladder. True = a trip was started or plot work
        /// finished with day left over (ask again); false = the day is spent
        /// or there is nothing to do.
        bool LadderStep(OutpostHand h, ref float budget, float scale)
        {
            var site = StickySite(h);
            if (site == null)
            {
                if (FetchForSites(h, false)) return true;          // 1. from the store
                site = PickPlot(h);                                // 2. clear / hammer
                if (site == null) return FetchForSites(h, true);   // 3. off the island
                workSite[h] = site;
            }
            if (!WalkToPlot(h, site, ref budget, scale))
            {
                // Stall guard: a driven body stuck outside reach of an
                // uncleared plot (nothing left there for him to claim) never
                // closes the distance on his own -- release him rather than
                // hold the plot forever.
                if (h.driven && !site.Cleared)
                {
                    Vector3 at = HandAt(h);
                    bool moved = !clearStallPos.TryGetValue(h, out var last) ||
                        Vector3.Distance(at, last) > 0.05f;
                    clearStallPos[h] = at;
                    if (moved) clearStall.Remove(h);
                    else
                    {
                        clearStall.TryGetValue(h, out int stalls);
                        if (++stalls > StallLimit)
                        {
                            clearStall.Remove(h);
                            clearStallPos.Remove(h);
                            workSite.Remove(h);
                            return budget > Eps;
                        }
                        clearStall[h] = stalls;
                    }
                }
                return false;
            }
            clearStall.Remove(h);
            clearStallPos.Remove(h);
            if (budget <= Eps) return false;
            if (!site.Cleared)
            {
                PayClear(h, site, ref budget);
                if (h.Hauling) return true;       // a cleared log to carry
                if (!site.Cleared) return false;  // the day went on the plot
            }
            if (site.Stocked)
            {
                PayBuild(site, ref budget);
                if (!site.Complete) return false;
                // `Outpost.FinishReady` stands the mesh up on the next CatchUp.
                away.raised.Add(site.planId);
            }
            // Plot work done (raised, or cleared and still waiting on stock):
            // 3. "when it's complete, look for the next thing to do".
            workSite.Remove(h);
            return budget > Eps;
        }

        /// The plot to send a free hand to: one with clearing (and an
        /// obstruction nobody else is on) or hammering, fewest hands first,
        /// oldest breaking ties.
        PendingBuild PickPlot(OutpostHand h)
        {
            if (sites == null) return null;
            PendingBuild best = null;
            int bestCrew = int.MaxValue;
            foreach (var s in sites)
            {
                if (!PlotWork(s)) continue;
                int crew = 0;
                foreach (var kv in workSite)
                    if (kv.Value == s && kv.Key != h && kv.Key.order == OutpostOrder.Build) crew++;
                // Clearing: one hand per obstruction left, as the bodies claim.
                if (!s.Cleared && crew >= s.ClearLeft) continue;
                if (crew < bestCrew) { best = s; bestCrew = crew; }
            }
            return best;
        }

        /// Start a fetch for the oldest site short of something: from the
        /// store or a station rack (`field` false), or cut/quarried off the
        /// island (`field` true).
        bool FetchForSites(OutpostHand h, bool field)
        {
            if (sites == null) return false;
            foreach (var site in sites)
            {
                if (site == null || site.Complete || site.Stocked) continue;
                for (int k = 0; k < 3; k++)
                {
                    string res = k == 0 ? Res.Timber : k == 1 ? Res.Stone : Res.Brick;
                    int need = NetShort(site, res);
                    if (need <= 0) continue;
                    int cap = Mathf.Min(Res.Armful(res), need);
                    if (!field)
                    {
                        int pileFree = StoreFree(res);
                        if (pileFree > 0)
                        {
                            StartTimedTrip(h, res, Mathf.Min(cap, pileFree), HaulPlace.Store, -1, HaulPlace.Site, -1, site);
                            return true;
                        }
                        if (stations != null)
                            for (int i = 0; i < stations.Count; i++)
                            {
                                int free = RowFree(i, stations[i]?.Rack(res), false);
                                if (free <= 0) continue;
                                StartTimedTrip(h, res, Mathf.Min(cap, free), HaulPlace.Station, i, HaulPlace.Site, -1, site);
                                return true;
                            }
                        continue;
                    }
                    if (res == Res.Brick) continue;   // nobody quarries a brick
                    int standing = FieldFree(res);
                    if (standing <= 0) continue;
                    StartTimedTrip(h, res, Mathf.Min(cap, standing), HaulPlace.Field, -1, HaulPlace.Site, -1, site);
                    return true;
                }
            }
            return false;
        }

        /// `WalkTo` the plot; a DRIVEN body clearing a big plot works at its
        /// trees and rocks, which can stand well out from the centre, so it
        /// counts as on the plot anywhere within the footprint's reach.
        bool WalkToPlot(OutpostHand h, PendingBuild site, ref float budget, float scale)
        {
            if (h.driven && !site.Cleared)
            {
                Vector3 p = HandAt(h), g = site.At;
                p.y = g.y = 0f;
                return Vector3.Distance(p, g) <= PlotReach(site);
            }
            return WalkTo(h, site.At, ref budget, scale);
        }

        static float PlotReach(PendingBuild s)
        {
            float r = OnSiteMetres;
            var plan = BuildPlans.Named(s.planId);
            r += 0.5f * Mathf.Max(plan.footprint.x, plan.footprint.y);
            r += 0.5f * s.WallLength;
            return r + 4f;
        }

        // --- the issue: a site nothing can supply ------------------------------

        /// **Why this site cannot be stocked by anyone**, as a sentence for
        /// the site sheet, or null when every missing material has a source
        /// (the store, a station rack, the island, or a load already walking
        /// into the store). Hands never take station jobs on their own, so a
        /// crafted material names the station the player should staff.
        public string SiteIssue(PendingBuild p) => Issue(p, false);

        /// The same, as the two or three words over the blueprint.
        public string SiteIssueShort(PendingBuild p) => Issue(p, true);

        string Issue(PendingBuild p, bool brief)
        {
            if (p == null || p.Complete || p.Stocked) return null;
            for (int k = 0; k < 3; k++)
            {
                string res = k == 0 ? Res.Timber : k == 1 ? Res.Stone : Res.Brick;
                if (NetShort(p, res) <= 0) continue;
                if (SiteSourceExists(res)) continue;
                if (InFlightTo(HaulPlace.Store, -1, res) > 0) continue;   // somebody is bringing it in
                if (res == Res.Brick)
                    return brief ? "! Needs bricks"
                        : "Needs bricks: put someone on the quarry to make them.";
                if (res == Res.Stone)
                    return brief ? "! No stone"
                        : "No stone to be found: none in the store and no rock left to quarry.";
                return brief ? "! No timber"
                    : "No timber to be found: none in the store and no trees left to cut.";
            }
            return null;
        }
    }
}
