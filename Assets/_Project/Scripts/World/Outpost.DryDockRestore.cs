using UnityEngine;

namespace SeaSick.World
{
    /// **A saved dry dock comes back (Kevin, iPhone, 2026-09-27: "I have to
    /// re-build my dry dock every time").**
    ///
    /// The bug was ORDER. `SaveGame.Apply` adopts every camp (step 5) and
    /// only then re-points `Dock.Home` at the player's own pier (step 5b).
    /// `Adopt` re-raises each saved row through `Raise(plan, at, yaw)`,
    /// which asks `CanPlaceDryDock`, whose "within `DryDockMaxFromHome` of
    /// `Dock.Home`" rule was answered against the HARBOUR on the home
    /// island -- hundreds of metres away -- so the row was refused, the
    /// spiral fallback cannot place a dry dock, the building was DROPPED and
    /// its `raised` row with it, while `built` still counted it. Every
    /// rebuild added another `built` entry; his save carried four.
    ///
    /// The fix, three parts:
    /// - `adoptingRows`: while `Adopt` stands saved rows, the home-berth
    ///   distance rule is not asked. It is a SITING rule; the saved spot
    ///   passed it the day it was built, and a dry dock is not demolished
    ///   because the berth it was built beside moved.
    /// - `ReconcileSpecialRows`: a pier or dry dock has no spiral fallback,
    ///   so a `built` entry with nothing standing for it is a phantom --
    ///   dropped, so the copy cap (`CanAddCopy`, which counts `built`) and
    ///   the build list tell the truth. Dry dock rows past the cap (one) are
    ///   skipped in `Adopt` before they are raised: the FIRST saved one
    ///   stands, the rest are dropped silently, no refund.
    /// - `ResiteOrphanDryDocks`: a save the bug already ate (its `built`
    ///   says DryDock, no `raised` row says where) gets one dry dock stood
    ///   back beside the home berth by the same `SnapDryDock` the player's
    ///   ghost uses -- called by `SaveGame` AFTER step 5b, when `Dock.Home`
    ///   is the right pier.
    public partial class Outpost
    {
        /// True only while `Adopt` re-raises the saved `raised` rows.
        bool adoptingRows;

        /// A camp whose ledger counted a dry dock with no saved spot; see
        /// `ResiteOrphanDryDocks`.
        bool orphanDryDock;

        /// Why the build list cannot offer `planId` here for want of a home
        /// berth (the dry dock only): none yet, or it is on another island.
        /// Null when nothing is in the way.
        public string HomeBerthNeed(string planId)
        {
            if (planId != BuildPlans.DryDock.id) return null;
            if (Dock.Home == null) return DryDockNeedsHome;
            var isle = Island.Home;
            if (isle != null && Island != null && isle != Island) return $"build it at {isle.name}";
            return null;
        }

        public const string DryDockNeedsHome =
            "needs a home berth: tie up at your pier, then Ship → Make home berth";

        /// Skip this saved row? True for a dry dock row once the camp
        /// already stands as many as its cap allows (one).
        bool SkipExtraDryDockRow(BuildPlan plan)
        {
            if (plan.kind != BuildKind.DryDock || ledger == null) return false;
            return CountOf(plan.id) >= ledger.CopyLimit(plan.id);
        }

        /// After `Adopt` has stood every row: make `built` agree with what
        /// stands for the plans the spiral cannot place.
        void ReconcileSpecialRows()
        {
            if (ledger == null || ledger.built == null) return;
            orphanDryDock = false;
            foreach (var plan in new[] { BuildPlans.Pier, BuildPlans.DryDock })
            {
                int standing = CountOf(plan.id);
                int counted = ledger.CountBuilt(plan.id);
                if (counted <= standing) continue;
                if (plan.kind == BuildKind.DryDock && standing == 0) orphanDryDock = true;
                for (int i = counted - standing; i > 0; i--) ledger.built.Remove(plan.id);
                Debug.LogWarning($"Outpost.Adopt: {name} counted {counted} {plan.label}(s), "
                    + $"{standing} stand -- dropped {counted - standing} phantom row(s)"
                    + (orphanDryDock && plan.kind == BuildKind.DryDock ? "; will re-site one beside the home berth" : ""));
            }
        }

        /// **Stand a lost dry dock back beside the home berth.** Called by
        /// `SaveGame` once the home berth is restored. Only a camp on the
        /// home berth's own island can hold the home dry dock; any other
        /// orphan is simply gone (its phantom row was already dropped).
        public static void ResiteOrphanDryDocks()
        {
            foreach (var o in all)
            {
                if (o == null || !o.orphanDryDock) continue;
                o.orphanDryDock = false;
                o.ResiteDryDock();
            }
        }

        void ResiteDryDock()
        {
            var home = Dock.Home;
            if (home == null || ledger == null || !Sited) return;
            if (Island.Nearest(home.Berth) != Island) return;
            if (CountOf(BuildPlans.DryDock.id) > 0) return;
            Vector3 berth = home.Berth;
            // Nearest first: rings round the berth, sixteen bearings each.
            for (float r = 10f; r <= BuildPlans.DryDockMaxFromHome; r += 6f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 picked = berth + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (!SnapDryDock(picked, out Vector3 c, out float yaw, out _)) continue;
                    var b = Raise(BuildPlans.DryDock, c, yaw);
                    if (b == null) continue;
                    ledger.built.Add(BuildPlans.DryDock.id);
                    Debug.Log($"Outpost: {name} -- re-sited the lost dry dock at ({c.x:F0},{c.z:F0})");
                    return;
                }
            Debug.LogWarning($"Outpost: {name} -- could not re-site the lost dry dock beside the home berth");
        }
    }
}
