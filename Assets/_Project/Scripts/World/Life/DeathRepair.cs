using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World.Life
{
    /// **Un-kill Bo** (Kevin's decision, 2026-10-04). His save has Bo
    /// "lost at sea" on day 649 with a tombstone, and a Bo on deck who went
    /// ashore and back aboard at Island_3 on day 708 and sails today. The
    /// death was real when it was logged; the body came back because the
    /// authored cast are fixed scene bodies the steamer switched on every
    /// load without asking the save who had died (`CrewNames.RetireTakenNames`
    /// closes that path). The living Bo keeps his name and his life; the
    /// false death and its grave go.
    ///
    /// **Identity-checked.** A grave is taken back only when its person
    /// demonstrably lives on: a SWITCHED-ON body aboard wears that identity
    /// (the life registry's key, the name `LifeRecord`/`GraveRecord`/
    /// `CrewAgent` share) AND that person's own life log carries on after
    /// the death day (`Bury` drops the record, so any later event is the
    /// body living as them). A body that is switched off (Ola, stood down
    /// by the steamer's 4-hand post) proves nothing: Ola stays dead. A
    /// person living on at a CAMP is not this rule's business.
    ///
    /// **One-time and idempotent.** Runs on the first load after this build
    /// and records that in the save (`SaveData.deathRepairDone`); the path
    /// it repairs is closed, so there is nothing for it to do later, and a
    /// grave it removed is gone, so a second run finds nothing anyway.
    public static class DeathRepair
    {
        /// Has this save had its one pass? Synced with `SaveData`.
        public static bool Done;

        /// Pure: is this death false -- does its person live on aboard?
        public static bool IsFalseDeath(GraveRecord g, LifeRecord life, bool activeBodyAboard)
        {
            if (g == null || string.IsNullOrEmpty(g.name) || !activeBodyAboard) return false;
            if (life == null || life.events == null || life.name != g.name) return false;
            foreach (var e in life.events)
                if (e != null && e.day > g.diedDay) return true;
            return false;
        }

        /// Pure: the graves to take back, in graveyard order.
        public static List<GraveRecord> FalseDeaths(IReadOnlyList<GraveRecord> graves,
            System.Func<string, LifeRecord> life, System.Func<string, bool> activeBodyAboard)
        {
            var list = new List<GraveRecord>();
            if (graves == null) return list;
            foreach (var g in graves)
                if (g != null && IsFalseDeath(g, life(g.name), activeBodyAboard(g.name))) list.Add(g);
            return list;
        }

        /// The load step (`SaveGame.Restore` 5a2, after `CastawayRepair`,
        /// before `CrewNames.RetireTakenNames`). Returns a log line or "".
        public static string RunOnLoad(Transform ship)
        {
            if (Done) return "";
            Done = true;
            var aboard = new HashSet<string>();
            if (ship != null)
                foreach (var a in ship.GetComponentsInChildren<Crew.CrewAgent>(false))
                {
                    if (a == null || !a.gameObject.activeSelf || CastawayField.IsFigure(a)) continue;
                    if (a.GetComponent<SeaSick.Combat.RaidWalker>() != null) continue;
                    if (!string.IsNullOrEmpty(a.DisplayName)) aboard.Add(a.DisplayName);
                }
            var undo = FalseDeaths(Lives.Graveyard,
                n => Lives.Records.TryGetValue(n, out var r) ? r : null,
                aboard.Contains);
            var sb = new System.Text.StringBuilder();
            foreach (var g in undo)
            {
                if (!Lives.Unbury(g)) continue;
                GravePlacementFlow.RemoveStone(g.name);
                sb.Append(g.name).Append(" (day ").Append(g.diedDay).Append(' ').Append(g.cause)
                  .Append(" taken back: alive aboard) ");
            }
            return sb.ToString();
        }
    }
}
