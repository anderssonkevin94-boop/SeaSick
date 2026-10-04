using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Voyage
{
    /// **The one-time repair of the retired home bank** (2026-10-04).
    ///
    /// From home becoming a camp (6a136c5e, 2026-09-29) until 2026-10-04,
    /// docking at the home pier banked the hold into the save's top-level
    /// `banked` -- a number only the shipyard read -- and never into the home
    /// camp's store. Kevin: *"I came back with iron ore on my ship and it just
    /// vanished when I docked."* Nothing was destroyed; it was misbooked.
    /// This moves every banked unit into the home camp's store (island stores
    /// are unlimited since 2026-10-03, so it never refuses) and clears the
    /// bank.
    ///
    /// Plain C#, no scene: `VoyageManager.RepairLegacyBank` calls it on load
    /// (`SaveGame.Restore`, after the home berth is known) and at every
    /// homecoming. Idempotent: an empty bank moves nothing. With no home
    /// ledger the bank is left exactly as it was.
    public static class HomeBankRepair
    {
        /// Move `banked` into `home`'s store and clear it. Returns the units
        /// moved (0 = nothing to do, or no home ledger to take them).
        public static int Apply(IDictionary<string, int> banked, OutpostLedger home)
        {
            if (banked == null || banked.Count == 0 || home == null) return 0;
            int moved = 0;
            foreach (var kv in banked)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                // Straight onto the store's count, not `Add`: `Add` answers 0
                // on bare ground (`KeepsAnything`), and a repair must never
                // be the thing that loses the goods a second time.
                home.Store(kv.Key, true).whole += kv.Value;
                moved += kv.Value;
            }
            banked.Clear();
            return moved;
        }

        /// **Edit-mode check, no scene.** `unity cmd eval --json --code
        /// 'SeaSick.Voyage.HomeBankRepair.SelfTest()'` -- true when every gate
        /// passes; logs the report either way. Kevin's a1 numbers: store
        /// Timber 18 / Stone 0, bank Timber 27 / Stone 26 / Ore 20.
        public static bool SelfTest()
        {
            var sb = new StringBuilder("HomeBankRepair.SelfTest\n");
            bool ok = true;
            void Gate(string name, bool pass, string detail)
            {
                ok &= pass;
                sb.Append(pass ? "  PASS " : "  FAIL ").Append(name).Append("  ").Append(detail).Append('\n');
            }

            var l = new OutpostLedger();
            l.Store(Res.Timber, true).whole = 18;
            l.Store(Res.Stone, true).whole = 0;
            var bank = new Dictionary<string, int>
            {
                { Res.Timber, 27 }, { Res.Stone, 26 }, { Res.Ore, 20 },
            };
            int moved = HomeBankRepair.Apply(bank, l);
            int t = l.Store(Res.Timber).whole, s = l.Store(Res.Stone).whole;
            int o = l.Store(Res.Ore) != null ? l.Store(Res.Ore).whole : -1;
            Gate("moves-every-unit", moved == 73, "moved " + moved + " (want 73)");
            Gate("home-store", t == 45 && s == 26 && o == 20, $"timber {t} stone {s} ore {o} (want 45/26/20)");
            Gate("bank-cleared", bank.Count == 0, "bank rows " + bank.Count);

            int again = HomeBankRepair.Apply(bank, l);
            Gate("idempotent", again == 0 && l.Store(Res.Timber).whole == 45
                && l.Store(Res.Stone).whole == 26 && l.Store(Res.Ore).whole == 20,
                "second apply moved " + again);

            var kept = new Dictionary<string, int> { { Res.Ore, 5 } };
            int none = HomeBankRepair.Apply(kept, null);
            Gate("no-home-keeps-bank", none == 0 && kept.Count == 1 && kept[Res.Ore] == 5,
                "moved " + none + ", rows " + kept.Count);

            var junk = new Dictionary<string, int> { { "", 3 }, { Res.Stone, 0 }, { Res.Boards, -2 } };
            var l2 = new OutpostLedger();
            int j = HomeBankRepair.Apply(junk, l2);
            Gate("junk-rows-ignored", j == 0 && junk.Count == 0 && l2.Store(Res.Stone) == null,
                "moved " + j);

            sb.Append(ok ? "ALL PASS" : "FAILED");
            if (ok) Debug.Log(sb.ToString()); else Debug.LogWarning(sb.ToString());
            return ok;
        }
    }
}
