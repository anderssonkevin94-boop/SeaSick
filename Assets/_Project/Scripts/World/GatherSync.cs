using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Makes the stone, ore and spice props on an island agree with the
    /// outpost ledger.** Timber has had this since `Outpost.SyncFelling`:
    /// trees come down nearest the camp outward to match `timberTaken`. The
    /// other three resources only ever decremented an abstract number, so a
    /// mined-out island still looked untouched.
    ///
    /// Same shape as felling, on purpose: the ledger is the only authority,
    /// the order is nearest-the-camp-first, and the hidden set is always a
    /// PREFIX of that order -- so the picture reproduces from the ledger on
    /// any visit, whatever the terrain streamer did in between, and a single
    /// number decides how many rocks are gone.
    ///
    /// What is taken: for these resources a gathering hand only moves
    /// `OutpostStock.standing` (`OutpostLedger.Step`; `timberTaken` is
    /// timber-only), so `taken = standingMax - standing`. Regrowth raises
    /// `standing` again (spice, `Res.RegrowPerDay`), which shortens the prefix
    /// and the same props come back -- which is why `ResourceNode.SetGathered`
    /// hides renderers rather than deactivating the object.
    ///
    /// One prop per `ResourceNode.UnitsPerProp` units (default 4). When the
    /// stock is worked out to under one unit every prop goes, so an exhausted
    /// seam does not leave a rounding remainder of rocks standing.
    ///
    /// Call site: one line at the end of `Outpost.CatchUp()`:
    /// `GatherSync.Sync(this);`
    public static class GatherSync
    {
        class Entry
        {
            public Island island;
            public Vector3 from;
            public readonly Dictionary<string, ResourceNode[]> order =
                new Dictionary<string, ResourceNode[]>();
        }

        static readonly Dictionary<Outpost, Entry> cache = new Dictionary<Outpost, Entry>();
        static readonly List<Outpost> dead = new List<Outpost>();
        static readonly List<ResourceNode> scratch = new List<ResourceNode>();

        /// Hide or show this outpost's non-timber props to match its ledger.
        /// Idempotent; cheap unless the camp moved or the props changed.
        public static void Sync(Outpost o)
        {
            if (o == null || o.Ledger == null) return;
            var island = o.Island;
            if (island == null) return;

            PruneDead();
            if (!cache.TryGetValue(o, out var e))
            {
                e = new Entry();
                cache[o] = e;
            }

            Vector3 c = o.CampCentre;
            bool moved = !ReferenceEquals(e.island, island)
                || (e.from - c).sqrMagnitude > 0.25f;
            if (moved)
            {
                e.island = island;
                e.from = c;
                e.order.Clear();
            }

            var stocks = o.Ledger.stocks;
            for (int i = 0; i < stocks.Count; i++)
            {
                var s = stocks[i];
                if (s == null || string.IsNullOrEmpty(s.resource)) continue;
                if (s.resource == Res.Timber || s.resource == Res.Food) continue;
                if (!Res.IsGatherable(s.resource)) continue;

                var order = OrderFor(e, island, s.resource);
                if (order.Length == 0) continue;

                float taken = Mathf.Max(0f, s.standingMax - s.standing);
                int per = order[0].UnitsPerProp;
                int want = Mathf.FloorToInt(taken / per);
                if (s.standing < 1f) want = order.Length;
                want = Mathf.Clamp(want, 0, order.Length);

                for (int k = 0; k < order.Length; k++)
                {
                    var n = order[k];
                    if (n != null) n.SetGathered(k < want);
                }
            }
        }

        /// Drop the cache for an outpost (or everything, with null): the next
        /// `Sync` rebuilds its order. Nothing calls this today; it is here so
        /// a probe or a scene reload has a handle.
        public static void Forget(Outpost o)
        {
            if (o == null) cache.Clear();
            else cache.Remove(o);
        }

        /// The island's live props of one resource, nearest the camp first.
        /// Rebuilt when the cached list no longer matches what is in
        /// `ResourceNode.All` (the island streamed out and back, a node was
        /// destroyed) -- checked by count and by identity, no allocation.
        static ResourceNode[] OrderFor(Entry e, Island island, string resource)
        {
            scratch.Clear();
            var all = ResourceNode.All;
            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                if (n == null || n.Home != island || n.Resource != resource) continue;
                // A rock on a building plot is the plot's (2026-09-23): the
                // clearing hides it, not the seam. Leaving it out here keeps
                // the seam's prefix a prefix of the rocks it actually owns.
                if (n.HeldBySite) continue;
                scratch.Add(n);
            }

            if (e.order.TryGetValue(resource, out var cached) && cached.Length == scratch.Count)
            {
                bool same = true;
                for (int i = 0; i < cached.Length && same; i++)
                    if (cached[i] == null || !scratch.Contains(cached[i])) same = false;
                if (same) return cached;
            }

            int count = scratch.Count;
            var keys = new Key[count];
            var arr = new ResourceNode[count];
            for (int i = 0; i < count; i++)
            {
                var n = scratch[i];
                Vector3 p = n.transform.position;
                Vector3 d = p - e.from;
                d.y = 0f;
                keys[i] = new Key { d2 = d.sqrMagnitude, tie = PosHash(p) };
                arr[i] = n;
            }
            System.Array.Sort(keys, arr, KeyOrder.Instance);
            e.order[resource] = arr;
            return arr;
        }

        struct Key { public float d2; public int tie; }

        class KeyOrder : IComparer<Key>
        {
            public static readonly KeyOrder Instance = new KeyOrder();
            public int Compare(Key a, Key b)
            {
                int c = a.d2.CompareTo(b.d2);
                return c != 0 ? c : a.tie.CompareTo(b.tie);
            }
        }

        /// A stable tie-break that depends only on where the prop stands, so
        /// two props at the same distance come off in the same order on every
        /// visit regardless of the order the populator made them in.
        static int PosHash(Vector3 p)
        {
            int x = Mathf.RoundToInt(p.x * 10f), y = Mathf.RoundToInt(p.y * 10f), z = Mathf.RoundToInt(p.z * 10f);
            unchecked { return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791); }
        }

        static void PruneDead()
        {
            dead.Clear();
            foreach (var kv in cache) if (kv.Key == null) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) cache.Remove(dead[i]);
        }
    }
}
