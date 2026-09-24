using System;
using System.Collections.Generic;

namespace SeaSick.Ship.Modular
{
    // ---------------------------------------------------------------------
    // The shipyard's dry dock (2026-09-25, Kevin): where equipment a refit
    // removes from the ship goes, and where a refit that fits equipment
    // takes it from. Generic by module id -- not gun-specific, so any future
    // equipment kind uses the same store. Pure, JSON-serialisable, its own
    // document persisted next to the ship's configuration (ShipSave.dryDock,
    // additive field, same pattern as ShipSave.modular). Starts empty; a
    // missing/unreadable field is an empty dock, never a refusal.
    // See docs/SHIPYARD-API.md for the diff rule ApplyRefit follows.
    // ---------------------------------------------------------------------

    [Serializable]
    public class DryDockEntry
    {
        public string moduleId;
        public int count;
    }

    [Serializable]
    public class DryDock
    {
        public int schemaVersion = 1;
        public List<DryDockEntry> entries = new List<DryDockEntry>();

        public static DryDock Empty() => new DryDock();

        public int Count(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId) || entries == null) return 0;
            foreach (var e in entries) if (e != null && e.moduleId == moduleId) return e.count;
            return 0;
        }

        public void Add(string moduleId, int n = 1)
        {
            if (string.IsNullOrEmpty(moduleId) || n <= 0) return;
            foreach (var e in entries)
                if (e != null && e.moduleId == moduleId) { e.count += n; return; }
            entries.Add(new DryDockEntry { moduleId = moduleId, count = n });
        }

        /// False (and unchanged) if fewer than `n` are in stock.
        public bool TryTake(string moduleId, int n = 1)
        {
            if (string.IsNullOrEmpty(moduleId) || n <= 0) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.moduleId != moduleId) continue;
                if (e.count < n) return false;
                e.count -= n;
                if (e.count <= 0) entries.RemoveAt(i);
                return true;
            }
            return false;
        }

        public DryDock Clone() => FromJson(ToJson());

        public string ToJson() => ModularJson.To(this);

        public static DryDock FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return Empty();
            DryDock d;
            try { d = ModularJson.From<DryDock>(json); }
            catch (Exception) { return Empty(); }
            if (d == null) return Empty();
            if (d.entries == null) d.entries = new List<DryDockEntry>();
            // Drop zero/negative rows a hand-edited or half-written save
            // might carry -- Count/TryTake would treat them as absent anyway.
            d.entries.RemoveAll(e => e == null || string.IsNullOrEmpty(e.moduleId) || e.count <= 0);
            return d;
        }

        public bool ValueEquals(DryDock o)
        {
            if (o == null) return false;
            var ids = new HashSet<string>();
            foreach (var e in entries) if (e != null) ids.Add(e.moduleId);
            foreach (var e in o.entries) if (e != null) ids.Add(e.moduleId);
            foreach (var id in ids) if (Count(id) != o.Count(id)) return false;
            return true;
        }

        // ---- the apply-time diff (ApplyRefit) ------------------------------

        /// One module id's net change: positive = must come FROM the dock
        /// (fitted that were not aboard before); negative = goes INTO the
        /// dock (removed that were aboard before). Zero-delta ids (including
        /// a pure move between slots, which never changes the ship's count of
        /// that module) never appear.
        public struct Delta
        {
            public string moduleId;
            public int delta;
        }

        /// `expected` -> `draft`, by module id only (a move to a different
        /// slot is not a delta; see the type comment).
        public static List<Delta> Diff(ShipConfiguration expected, ShipConfiguration draft)
        {
            var exp = Counts(expected);
            var dr = Counts(draft);
            var ids = new HashSet<string>(exp.Keys);
            foreach (var id in dr.Keys) ids.Add(id);
            var list = new List<Delta>();
            foreach (var id in ids)
            {
                int e = exp.TryGetValue(id, out var ev) ? ev : 0;
                int d = dr.TryGetValue(id, out var dv) ? dv : 0;
                if (d != e) list.Add(new Delta { moduleId = id, delta = d - e });
            }
            list.Sort((a, b) => string.Compare(a.moduleId, b.moduleId, StringComparison.Ordinal));
            return list;
        }

        static Dictionary<string, int> Counts(ShipConfiguration c)
        {
            var m = new Dictionary<string, int>();
            if (c?.equipment == null) return m;
            foreach (var e in c.equipment)
            {
                if (e == null || string.IsNullOrEmpty(e.moduleId)) continue;
                m[e.moduleId] = (m.TryGetValue(e.moduleId, out var n) ? n : 0) + 1;
            }
            return m;
        }

        /// True if every delta>0 (taken from the dock) has enough stock;
        /// `missing` names the first module id that does not.
        public bool CanApply(List<Delta> diff, out string missing)
        {
            missing = null;
            foreach (var d in diff)
                if (d.delta > 0 && Count(d.moduleId) < d.delta) { missing = d.moduleId; return false; }
            return true;
        }

        /// Applies IN PLACE. Caller must have checked CanApply first (this
        /// does not re-check, so it must never be called on a diff that
        /// would take more than is in stock).
        public void Apply(List<Delta> diff)
        {
            foreach (var d in diff)
            {
                if (d.delta < 0) Add(d.moduleId, -d.delta);
                else if (d.delta > 0) TryTake(d.moduleId, d.delta);
            }
        }
    }
}
