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
    /// demonstrably lives on: a body aboard wears that identity
    /// (the life registry's key, the name `LifeRecord`/`GraveRecord`/
    /// `CrewAgent` share) AND that person's own life log carries on after
    /// the death day (`Bury` drops the record, so any later event is the
    /// body living as them). Switched on or not: Kevin, same day, "un-kill
    /// Ola too" -- lost at sea on day 503, her body stood down by the
    /// steamer's 4-hand post, her log running on to day 708; she comes back
    /// ON as a counted hand (`ReturnLivingHands`). And "un-kill Pip too" -- a camp's LEDGER HAND with that
    /// identity whose log carries on after the death day counts the same
    /// (Pip, lost at sea day 19, a home hand since day 332). Removed for
    /// each: the grave record (`Lives.Unbury`) and its tombstone
    /// (`GravePlacementFlow.RemoveStone`); `Bury` already dropped the dead
    /// life record, and the living one stays.
    ///
    /// **One-time and idempotent.** Runs on the first load after this build
    /// and records that in the save (`SaveData.deathRepairDone`); the path
    /// it repairs is closed, so there is nothing for it to do later, and a
    /// grave it removed is gone, so a second run finds nothing anyway.
    public static class DeathRepair
    {
        /// Has this save had its one pass? Synced with `SaveData`.
        public static bool Done;

        /// Pure: is this death false -- does its person live on, switched on
        /// aboard or as a camp's ledger hand?
        public static bool IsFalseDeath(GraveRecord g, LifeRecord life, bool bodyAboard,
                                        bool ledgerHand = false)
        {
            bool lives = bodyAboard || ledgerHand;
            if (g == null || string.IsNullOrEmpty(g.name) || !lives) return false;
            if (life == null || life.events == null || life.name != g.name) return false;
            foreach (var e in life.events)
                if (e != null && e.day > g.diedDay) return true;
            return false;
        }

        /// Pure: the graves to take back, in graveyard order.
        public static List<GraveRecord> FalseDeaths(IReadOnlyList<GraveRecord> graves,
            System.Func<string, LifeRecord> life, System.Func<string, bool> bodyAboard,
            System.Func<string, bool> ledgerHand = null)
        {
            var list = new List<GraveRecord>();
            if (graves == null) return list;
            foreach (var g in graves)
                if (g != null && IsFalseDeath(g, life(g.name), bodyAboard(g.name),
                        ledgerHand != null && ledgerHand(g.name))) list.Add(g);
            return list;
        }

        /// The load step (`SaveGame.Restore` 5a2, after `CastawayRepair`,
        /// before `CrewNames.RetireTakenNames`). Returns a log line or "".
        public static string RunOnLoad(Transform ship)
        {
            if (Done) return "";
            Done = true;
            var rows = new HashSet<string>();
            foreach (var o in Outpost.All)
            {
                if (o == null || o.Ledger == null || o.Ledger.hands == null) continue;
                foreach (var h in o.Ledger.hands)
                    if (h != null && !string.IsNullOrEmpty(h.name)) rows.Add(h.name);
            }
            var aboard = new HashSet<string>();
            if (ship != null)
                foreach (var a in ship.GetComponentsInChildren<Crew.CrewAgent>(true))
                {
                    if (a == null || CastawayField.IsFigure(a)) continue;
                    if (a.GetComponent<SeaSick.Combat.RaidWalker>() != null) continue;
                    if (!string.IsNullOrEmpty(a.DisplayName)) aboard.Add(a.DisplayName);
                }
            var undo = FalseDeaths(Lives.Graveyard,
                n => Lives.Records.TryGetValue(n, out var r) ? r : null,
                aboard.Contains, rows.Contains);
            var sb = new System.Text.StringBuilder();
            foreach (var g in undo)
            {
                if (!Lives.Unbury(g)) continue;
                GravePlacementFlow.RemoveStone(g.name);
                sb.Append(g.name).Append(" (day ").Append(g.diedDay).Append(' ').Append(g.cause)
                  .Append(rows.Contains(g.name) ? " taken back: a camp hand) " : " taken back: alive aboard) ");
            }
            return sb.ToString();
        }

        // ------------------------------------------------ living hands back --

        /// Pure: does a switched-off body aboard come back on? Only a person
        /// who is alive (no grave, not a castaway), not at a camp (no ledger
        /// row), and HAS lived aboard (a life record with something in it --
        /// a fresh game's never-posted fifth authored hand has none, so a new
        /// voyage still starts with the steamer's four), with a hammock free.
        public static bool ShouldReturn(bool switchedOn, bool nameTaken, bool ledgerRow,
                                        bool hasLife, bool berthFree) =>
            !switchedOn && !nameTaken && !ledgerRow && hasLife && berthFree;

        /// **Her stood-down hands who are alive come back on** (2026-10-04,
        /// Ola). `SteamerBootstrap.Man` posts the steamer's first four
        /// authored bodies at every scene load, before the save is read, and
        /// a living fifth who has served aboard would be switched off again
        /// every time. Load step 5a2, after `RetireTakenNames`: each such
        /// body is switched on and the crew re-posted on her deck. The
        /// steamer only (`ShipyardService`); the ladder brig's `ManCrew`
        /// posts by berths. Returns a log line or "".
        public static string ReturnLivingHands(Transform ship)
        {
            if (ship == null) return "";
            var service = ship.GetComponent<SeaSick.Ship.Modular.ShipyardService>();
            var roster = ship.GetComponent<Crew.CrewRoster>();
            if (service == null || roster == null) return "";
            var rows = new HashSet<string>();
            foreach (var o in Outpost.All)
            {
                if (o == null || o.Ledger == null || o.Ledger.hands == null) continue;
                foreach (var h in o.Ledger.hands)
                    if (h != null && !string.IsNullOrEmpty(h.name)) rows.Add(h.name);
            }
            roster.Refresh();
            int aboard = roster.AboardCount, berths = service.CrewBerths;
            var sb = new System.Text.StringBuilder();
            foreach (var a in ship.GetComponentsInChildren<Crew.CrewAgent>(true))
            {
                if (a == null || CastawayField.IsFigure(a)) continue;
                if (a.GetComponent<SeaSick.Combat.RaidWalker>() != null) continue;
                string n = a.DisplayName;
                if (string.IsNullOrEmpty(n)) continue;
                bool hasLife = Lives.Records.TryGetValue(n, out var r) && r.events != null && r.events.Count > 0;
                if (!ShouldReturn(a.gameObject.activeSelf, Lives.IsTaken(n), rows.Contains(n),
                                  hasLife, aboard < berths)) continue;
                a.gameObject.SetActive(true);
                aboard++;
                sb.Append(n).Append(' ');
            }
            if (sb.Length == 0) return "";
            SeaSick.Steamer.SteamerBootstrap.RepostHands(ship.gameObject, service.ActiveData);
            roster.Refresh();
            return sb.Append("back on deck").ToString();
        }
    }
}
