using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The Storehouse is merged into the store hut (Kevin 2026-10-04):**
    /// *"merge them into the right building. Remove the building I built on
    /// the other side of the wall."* Its runner progression (posts 2 / 4 / 6,
    /// barrow and jog perks) is the store hut's levels now
    /// (OutpostLedger.Runners.cs), and the plan is gone from the game.
    ///
    /// **The one-time load repair.** Kevin asked for his Storehouse to be
    /// taken down, so this is the ONE exception to "the game never moves a
    /// building" (buildings-never-move): every Storehouse in a saved camp is
    /// removed from the books before `Outpost.Adopt` raises anything (so its
    /// body is never spawned), and what it cost comes back to that camp's
    /// store. Nothing else on the camp is touched -- no other building, wall,
    /// road, station or pile.
    ///
    /// - Each standing Storehouse: its `built` id, its `raised` row and any
    ///   old plan-wide level row go. Refund per copy (`RefundOf`): the build
    ///   (25 timber + 4 stone, +25 % a copy after the first, rounded up, the
    ///   way `BuildPlans.PriceForCopy` priced it) plus every level it was
    ///   raised to (L2 8 brick + 4 fine boards, L3 14 brick + 8 fine boards).
    ///   The numbers are the deleted tables', frozen here.
    /// - A Storehouse blueprint still in the queue: dropped, and whatever had
    ///   been carried to it comes back, as a cancel would (`CancelPending`).
    /// - Its runners: onto a store hut post with room (pinned to that copy,
    ///   still runners), else Idle -- NOT the reserve. A runner going Idle
    ///   puts a load already in his barrow into the store (a planned one is
    ///   just cancelled).
    ///
    /// Idempotent: a camp with nothing left that says "Storehouse" is a
    /// no-op. One log line per camp it changed.
    public partial class OutpostLedger
    {
        /// The plan id old saves carry. Nothing builds one any more.
        public const string LegacyStorehouseId = "Storehouse";

        /// **What one Storehouse cost, by copy and level** -- the deleted
        /// cost tables (`BuildPlans.Storehouse` 25 timber + 4 stone; `Techs`
        /// Storehouse L2 8 brick + 4 fine boards, L3 14 brick + 8 fine
        /// boards), so the refund cannot drift with the store hut's prices.
        /// `copy` 0 is the first (100 %), 1 the second (125 %).
        public static List<Economy.Ingredient> StorehouseRefundOf(int level, int copy)
        {
            float mul = 1f + 0.25f * Mathf.Max(0, copy);
            int Up(int n) => Mathf.CeilToInt(n * mul - 1e-4f);
            var r = new List<Economy.Ingredient>
            {
                Economy.Cost.I(Res.Timber, Up(25)),
                Economy.Cost.I(Res.Stone, Up(4)),
            };
            if (level >= 2) { r.Add(Economy.Cost.I(Res.Brick, 8)); r.Add(Economy.Cost.I(Res.FineBoards, 4)); }
            if (level >= 3) { r.Add(Economy.Cost.I(Res.Brick, 14)); r.Add(Economy.Cost.I(Res.FineBoards, 8)); }
            return r;
        }

        /// **Take every Storehouse out of this camp's books** (see the class
        /// note). Returns the one log line it wrote, or null when there was
        /// nothing to do.
        public string MergeStorehouses()
        {
            string id = LegacyStorehouseId;
            bool any = (built != null && built.Contains(id))
                || (raised != null && raised.Exists(r => r != null && r.planId == id))
                || (sites != null && sites.Exists(p => p != null && p.planId == id))
                || (levels != null && levels.Exists(l => l != null && l.planId == id))
                || (hands != null && hands.Exists(h => h != null && h.target == id))
                || (postsLost != null && postsLost.Exists(p => p != null && p.planId == id));
            if (!any) return null;

            var refund = new Dictionary<string, int>();
            void Give(string res, int n)
            {
                if (n <= 0) return;
                Store(res, true).whole += n;
                refund.TryGetValue(res, out int k);
                refund[res] = k + n;
            }

            // The standing ones: one refund per copy, at that copy's level.
            int standing = built != null ? built.RemoveAll(b => b == id) : 0;
            var levelsOf = new List<int>();
            if (raised != null)
                for (int i = 0; i < raised.Count; i++)
                    if (raised[i] != null && raised[i].planId == id)
                        levelsOf.Add(raised[i].level > 0 ? raised[i].level : LegacyLevelOf(id));
            standing = Mathf.Max(standing, levelsOf.Count);
            var words = new List<string>();
            for (int k = 0; k < standing; k++)
            {
                int lv = k < levelsOf.Count ? levelsOf[k] : LegacyLevelOf(id);
                foreach (var line in StorehouseRefundOf(lv, k)) Give(line.res, line.n);
                words.Add("L" + lv);
            }
            raised?.RemoveAll(r => r != null && r.planId == id);
            levels?.RemoveAll(l => l != null && l.planId == id);

            // A drawing still in the queue: what was carried to it comes back.
            int drawn = 0;
            if (sites != null)
                for (int i = sites.Count - 1; i >= 0; i--)
                {
                    var p = sites[i];
                    if (p == null || p.planId != id) continue;
                    Give(Res.Timber, p.done);
                    Give(Res.Stone, p.stoneDone);
                    Give(Res.Brick, p.brickDone);
                    sites.RemoveAt(i);
                    drawn++;
                }
            // As `Outpost.CancelPending`: builders down tools only when the
            // queue is empty.
            if (drawn > 0 && !Building && hands != null)
                foreach (var h in hands)
                    if (h != null && h.order == OutpostOrder.Build) { h.order = OutpostOrder.Idle; h.target = ""; }

            // Its runners: a store hut post with room, else Idle.
            var moved = new List<string>();
            var idled = new List<string>();
            var unloaded = new List<string>();
            if (hands != null)
                foreach (var h in hands)
                {
                    if (h == null || h.target != id) continue;
                    if (h.order != OutpostOrder.Work) { h.target = ""; h.workPin = 0; continue; }
                    int to = -1;
                    int copies = CountBuilt(StorageId);
                    for (int o = 0; o < copies && to < 0; o++)
                        if (WorkersAt(StorageId, o, h) < StationCapacity(StorageId, o)) to = o;
                    if (to >= 0)
                    {
                        h.target = StorageId;
                        h.workPin = to + 1;
                        moved.Add(h.name);
                        continue;
                    }
                    if (h.Hauling)
                    {
                        if (h.haulPicked)
                        {
                            Store(h.haulRes, true).whole += h.haulCount;
                            unloaded.Add($"{h.haulCount} {h.haulRes}");
                            ClearHaul(h);
                        }
                        else CancelPlanned(h);
                    }
                    h.order = OutpostOrder.Idle;
                    h.target = "";
                    h.workPin = 0;
                    h.playerIdle = false;
                    idled.Add(h.name);
                }
            postsLost?.RemoveAll(p => p != null && p.planId == id);

            var sb = new System.Text.StringBuilder();
            sb.Append($"[Ledger] Storehouse merged into the store hut, camp ({keyX},{keyZ}): ");
            sb.Append(standing > 0 ? $"{standing} taken down ({string.Join(", ", words)})" : "none standing");
            if (drawn > 0) sb.Append($", {drawn} blueprint(s) dropped");
            if (refund.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in refund) parts.Add($"{kv.Value} {kv.Key}");
                sb.Append("; refunded " + string.Join(", ", parts));
            }
            if (moved.Count > 0) sb.Append("; to store hut posts: " + string.Join(", ", moved));
            if (idled.Count > 0) sb.Append("; no free post, now Idle: " + string.Join(", ", idled));
            if (unloaded.Count > 0) sb.Append("; barrow loads into the store: " + string.Join(", ", unloaded));
            string log = sb.ToString();
            Debug.Log(log);
            return log;
        }
    }
}
