using System.Collections.Generic;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// The cargo you can actually see: a deck load, lashed low and wide, so a
    /// full ship looks full and you can tell at a glance what you're carrying.
    ///
    /// **One pile per kind, drawn the way the camp draws it (2026-09-24).**
    /// Kevin, iPhone playtest: *"the cargo gets HUGE on the ship. much larger
    /// than it is on land. and it piles it in a very distracting and
    /// unreasonable way."* It was brig-era meshes two to three times the size
    /// of the camp's, stacked in arrival order (kinds mixed) into a 2 x 2
    /// tower that rose a layer every four units, each unit at a random yaw:
    /// 24 units stood about five metres tall on a 13.5 m launch. Now:
    ///
    /// - every kind gets ONE pile, drawn by `CampPiles.DrawPile` -- the same
    ///   units, sizes and layout as the pile by the fire, stowed square
    ///   (`tidy`: logs fore and aft, stone within +/-2 deg, no tilt);
    /// - a pile stops growing at `CampPiles.MaxDrawn` (a dozen), as ashore,
    ///   so no kind can build a tower -- about a metre high at most;
    /// - kinds sit side by side across the beam (`perRow` of them, `spacing`
    ///   apart), in the order they first came aboard, a row at a time; a
    ///   kind that is all unloaded frees its place for the next new kind.
    ///
    /// The layout defaults (serialized, authored for the brig in Sea.unity)
    /// put row 0 at `stackOrigin` and further rows `rowSpacing` aft of it. A
    /// hull that knows its own deck calls `Fit` instead (the steamer does, in
    /// `SteamerBootstrap`).
    public class ShipHold : MonoBehaviour
    {
        /// Row 0's centre, ship-local, on the deck.
        [SerializeField] Vector3 stackOrigin = new Vector3(0f, 2.95f, -4.4f);
        /// Kinds side by side across the beam.
        [SerializeField] int perRow = 2;
        /// Metres between the centres of neighbouring piles across the beam.
        [SerializeField] float spacing = 1.3f;
        /// Metres aft from one row of piles to the next (default layout only).
        [SerializeField] float rowSpacing = 1.8f;
        /// Most units the deck stands for, all kinds together.
        [SerializeField] int maxVisible = 24;

        /// Row centres from `Fit`, ship-local; null = the serialized layout.
        Vector3[] rows;

        sealed class Pile
        {
            public string res;
            /// Units of this kind the deck stands for (drawn: up to MaxDrawn).
            public int count;
            public int drawn = -1;
            public int slot;
            public Transform root;
        }

        readonly List<Pile> piles = new List<Pile>();
        Transform anchorRoot;
        VoyageManager voyage;
        string lastAdded;

        void Start()
        {
            voyage = FindFirstObjectByType<VoyageManager>();
            Root();
        }

        Transform Root()
        {
            if (anchorRoot != null) return anchorRoot;
            var root = new GameObject("HoldStack");
            root.transform.SetParent(transform, false);
            anchorRoot = root.transform;
            return anchorRoot;
        }

        /// **Fit the deck load to a hull.** `rowCentres` are ship-local
        /// centres of successive rows of piles, feet on the deck (a row past
        /// the last one steps on by the last gap); `kindsAcross` piles per
        /// row, `across` metres apart; `maxUnits` the most units drawn in
        /// all. Callable before or after `Start`; piles already on deck move.
        public void Fit(Vector3[] rowCentres, int kindsAcross, float across, int maxUnits)
        {
            rows = rowCentres != null && rowCentres.Length > 0 ? (Vector3[])rowCentres.Clone() : null;
            if (rows != null) stackOrigin = rows[0];
            perRow = Mathf.Max(1, kindsAcross);
            spacing = Mathf.Max(0.1f, across);
            maxVisible = Mathf.Max(1, maxUnits);
            foreach (var p in piles)
                if (p.root != null) p.root.localPosition = SlotLocal(p.slot);
        }

        /// `Fit` with evenly spaced rows: row n at `origin + n * rowStep` on z.
        public void Fit(Vector3 origin, int kindsAcross, float across, float rowStep, int maxUnits)
        {
            rowSpacing = Mathf.Abs(rowStep);
            Fit(new[] { origin, origin + new Vector3(0f, 0f, rowStep) }, kindsAcross, across, maxUnits);
        }

        /// Called when a crew member sets a unit down on deck.
        public void AddVisual(string resource)
        {
            if (string.IsNullOrEmpty(resource)) resource = Res.Timber;
            if (VisibleCount >= maxVisible) return;
            var p = Find(resource);
            if (p == null)
            {
                p = new Pile { res = resource, slot = FreeSlot() };
                var go = new GameObject("DeckLoad_" + resource);
                go.transform.SetParent(Root(), false);
                go.transform.localPosition = SlotLocal(p.slot);
                // Square to the ship. Lashed cargo does not sit at an angle.
                go.transform.localRotation = Quaternion.identity;
                p.root = go.transform;
                piles.Add(p);
            }
            p.count++;
            lastAdded = resource;
            Redraw(p);
        }

        /// Take one unit off the deck -- unloading at home, cargo over the
        /// side, a camp's hands carrying it ashore. The unit comes off the
        /// kind the deck is showing MORE of than the hold holds (the caller
        /// has already taken it out of `VoyageManager`); if none is over,
        /// off the biggest pile.
        public bool RemoveVisual()
        {
            if (piles.Count == 0) return false;
            Pile pick = null;
            if (voyage != null)
            {
                int over = 0;
                foreach (var p in piles)
                {
                    int o = p.count - voyage.HeldOf(p.res);
                    if (o > over) { over = o; pick = p; }
                }
            }
            if (pick == null)
                foreach (var p in piles)
                    if (pick == null || p.count > pick.count) pick = p;
            Take(pick, 1);
            return true;
        }

        /// Take one unit of `resource` off the deck, if any is drawn.
        public bool RemoveVisual(string resource)
        {
            var p = Find(resource);
            if (p == null) return false;
            Take(p, 1);
            return true;
        }

        public void ClearVisuals()
        {
            foreach (var p in piles) if (p.root != null) Destroy(p.root.gameObject);
            piles.Clear();
        }

        /// Units the deck stands for, all kinds (each pile DRAWS at most
        /// `CampPiles.MaxDrawn` of its own).
        public int VisibleCount
        {
            get
            {
                int n = 0;
                foreach (var p in piles) n += p.count;
                return n;
            }
        }

        /// Units of one kind the deck stands for.
        public int VisibleOf(string resource)
        {
            var p = Find(resource);
            return p != null ? p.count : 0;
        }

        /// Where a unit of `resource` is set down, in world space: beside its
        /// pile on the outboard side, or beside the free place a new kind
        /// would take. Crew walk here.
        public Vector3 DropPointFor(string resource)
        {
            var p = Find(resource);
            Vector3 local = SlotLocal(p != null ? p.slot : FreeSlot());
            float side = Mathf.Abs(local.x) > 0.05f ? Mathf.Sign(local.x) : 1f;
            local.x += side * spacing * 0.55f;
            return transform.TransformPoint(local);
        }

        /// `DropPointFor` the kind last set down (the pile the crew are
        /// feeding), or the next free place when the deck is empty.
        public Vector3 DropPoint => DropPointFor(Find(lastAdded) != null ? lastAdded : null);

        void LateUpdate()
        {
            // Keep the deck honest, kind by kind, if the hold changed without
            // going through us (mutiny ditching cargo, banking at home): no
            // pile shows more than `VoyageManager` holds of its kind.
            if (voyage == null) return;
            for (int i = piles.Count - 1; i >= 0; i--)
            {
                var p = piles[i];
                int held = voyage.HeldOf(p.res);
                if (p.count > held) Take(p, p.count - held);
            }
        }

        Pile Find(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return null;
            foreach (var p in piles) if (p.res == resource) return p;
            return null;
        }

        /// The first place no pile is using, so a kind that has gone ashore
        /// leaves its place to the next one aboard rather than a hole.
        int FreeSlot()
        {
            for (int s = 0; ; s++)
            {
                bool used = false;
                foreach (var p in piles) if (p.slot == s) { used = true; break; }
                if (!used) return s;
            }
        }

        /// Ship-local centre of place `slot`: across the beam first, then
        /// the next row.
        Vector3 SlotLocal(int slot)
        {
            int row = slot / perRow, col = slot % perRow;
            Vector3 c;
            if (rows != null)
            {
                if (row < rows.Length) c = rows[row];
                else
                {
                    Vector3 last = rows[rows.Length - 1];
                    Vector3 step = rows.Length > 1 ? last - rows[rows.Length - 2]
                                                   : new Vector3(0f, 0f, -rowSpacing);
                    c = last + step * (row - rows.Length + 1);
                }
            }
            else c = stackOrigin + new Vector3(0f, 0f, -rowSpacing * row);
            c.x += (col - (perRow - 1) * 0.5f) * spacing;
            return c;
        }

        void Take(Pile p, int n)
        {
            p.count -= n;
            if (p.count > 0) { Redraw(p); return; }
            if (p.root != null) Destroy(p.root.gameObject);
            piles.Remove(p);
        }

        static void Redraw(Pile p)
        {
            int want = Mathf.Min(p.count, CampPiles.MaxDrawn);
            if (want == p.drawn || p.root == null) return;
            p.drawn = want;
            CampPiles.DrawPile(p.root, p.res, want, true);
        }
    }
}
