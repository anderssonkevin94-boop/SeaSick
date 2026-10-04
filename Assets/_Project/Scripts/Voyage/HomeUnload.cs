using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Voyage
{
    /// **What homecoming unloads (2026-10-04, Kevin: repair timber the player
    /// keeps aboard stays aboard at home).** Home docking unloads CARGO only.
    /// The ship's repair stock is hold Timber (`AnchorController.Repair` burns
    /// `VoyageManager.AmountOf(Timber)` at `HullIntegrity.timberPerHullPoint`
    /// = 12 for a full rebuild), so the first `RepairStockTimber` units of
    /// Timber aboard are not ordered ashore; every other kind, and the timber
    /// above the stock, is ordered "all of it, ashore" as before. The player
    /// can still carry the stock ashore (or aboard) with a Backpack-sheet
    /// order like any other. Plain C#, no scene.
    public static class HomeUnload
    {
        /// Timber that stays aboard at home: one full hull rebuild.
        public const int RepairStockTimber = 12;

        /// How many units of `res` the homecoming orders ashore, given
        /// `aboard` of it: `OutpostLedger.TransferAll` for cargo, a counted
        /// order for timber above the repair stock, 0 for nothing.
        public static int AshoreCount(string res, int aboard)
        {
            if (aboard <= 0) return 0;
            if (res != Res.Timber) return OutpostLedger.TransferAll;
            return Mathf.Max(0, aboard - RepairStockTimber);
        }

        /// Place the homecoming's ship -> store orders on `home`, one per
        /// kind in `kinds`. A kind with nothing to unload gets no order and
        /// any standing ashore order for it is dropped, so the stock cannot
        /// be carried off by an older order.
        public static void OrderAll(IList<string> kinds, IReadOnlyDictionary<string, int> held, OutpostLedger home)
        {
            if (kinds == null || held == null || home == null) return;
            for (int i = 0; i < kinds.Count; i++)
            {
                string res = kinds[i];
                int aboard = held.TryGetValue(res, out int n) ? n : 0;
                int ashore = AshoreCount(res, aboard);
                if (ashore > 0) home.OrderTransfer(res, ashore, toShip: false);
                else home.CancelTransfer(res, false);
            }
        }

        sealed class FakeCargo : ICargoSide
        {
            public bool Present => true;
            public int Room => 100;
            public int HeldOf(string res) => 0;
            public int Take(string res, int n) => 0;
            public int Give(string res, int n) => 0;
            public bool GangwayAt(out Vector3 at) { at = Vector3.zero; return true; }
        }

        /// **Edit-mode check, no scene.** `unity cmd eval --json --code
        /// 'SeaSick.Voyage.HomeUnload.SelfTest()'`.
        public static bool SelfTest()
        {
            var sb = new StringBuilder("HomeUnload.SelfTest\n");
            bool ok = true;
            void Gate(string name, bool pass, string detail)
            {
                ok &= pass;
                sb.Append(pass ? "  PASS " : "  FAIL ").Append(name).Append("  ").Append(detail).Append('\n');
            }

            Gate("cargo-all", AshoreCount(Res.Stone, 7) == OutpostLedger.TransferAll
                && AshoreCount(Res.Ore, 1) == OutpostLedger.TransferAll, "stone/ore -> all");
            Gate("timber-below-stock-stays", AshoreCount(Res.Timber, 12) == 0 && AshoreCount(Res.Timber, 5) == 0
                && AshoreCount(Res.Timber, 0) == 0, "12, 5, 0 aboard -> nothing ashore");
            Gate("timber-above-stock-unloads-extra", AshoreCount(Res.Timber, 30) == 18, "30 aboard -> 18 ashore");

            var l = new OutpostLedger { cargo = new FakeCargo() };
            // A stale "all timber ashore" order from an earlier docking.
            l.OrderTransfer(Res.Timber, OutpostLedger.TransferAll, toShip: false);
            var held = new Dictionary<string, int> { { Res.Timber, 10 }, { Res.Stone, 4 }, { Res.Ore, 3 } };
            OrderAll(new List<string> { Res.Timber, Res.Stone, Res.Ore }, held, l);
            Gate("repair-timber-order-dropped", l.TransferLeft(Res.Timber, false) == 0, "timber left " + l.TransferLeft(Res.Timber, false));
            Gate("cargo-ordered-all", l.TransferLeft(Res.Stone, false) == OutpostLedger.TransferAll
                && l.TransferLeft(Res.Ore, false) == OutpostLedger.TransferAll, "stone/ore standing");

            held[Res.Timber] = 25;
            OrderAll(new List<string> { Res.Timber }, held, l);
            Gate("timber-above-stock-counted-order", l.TransferLeft(Res.Timber, false) == 13, "timber left " + l.TransferLeft(Res.Timber, false));

            sb.Append(ok ? "ALL PASS" : "FAILED");
            if (ok) Debug.Log(sb.ToString()); else Debug.LogWarning(sb.ToString());
            return ok;
        }
    }
}
