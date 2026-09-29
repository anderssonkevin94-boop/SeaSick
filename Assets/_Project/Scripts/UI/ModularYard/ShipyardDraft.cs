using System;
using System.Collections.Generic;
using SeaSick.Ship.Modular;

namespace SeaSick.UI.ModularYard
{
    // Adapter boundary: implementation belongs to the gameplay integration.
    // TryApply must compare expected with live state and commit atomically.
    public interface IShipyardRefit
    {
        ShipConfiguration ReadCurrent();
        string Validate(ShipConfiguration draft);
        bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason);
        // ---- equipment + dry dock (2026-09-25, docs/SHIPYARD-API.md §15) --
        // Pure: never touch the ship, the dock or the save.
        IReadOnlyList<EquipmentSlotView> EquipmentSlots(ShipConfiguration draft);
        ShipyardEdit FitEquipment(ShipConfiguration draft, string slotId, string moduleId);
        ShipyardEdit RemoveEquipment(ShipConfiguration draft, string slotId);
    }

    public sealed class ShipyardDraft
    {
        readonly ModuleLibrary library;
        readonly IShipyardRefit backend;
        readonly Func<ShipConfiguration, int, string> removalBlocker;
        readonly Func<string, string, bool> allowed;
        readonly ShipConfiguration baseline;
        ShipConfiguration draft;
        readonly Stack<ShipConfiguration> undo = new Stack<ShipConfiguration>();
        public AssemblyResult Assembly { get; private set; }
        public string Message { get; private set; } = "";
        public string Highlight { get; private set; }
        public bool Committed { get; private set; }
        public event Action Changed;
        public int Count => draft.middleIds.Count;
        /// docs/RAISED-SECTIONS.md sec 5 relaxed the old raised-only cap of
        /// 2 middles (the all-or-nothing kit's own limit) to the library's
        /// general bound -- a fully-connected raised run of 3 is now
        /// data-legal (unverified against Astra's art, flagged elsewhere;
        /// nothing stops the player reaching it here).
        public int Maximum => Math.Min(3, library.MaxMiddles);
        /// Raised-deck family only (docs/RAISED-DECK.md sec 3): 0 middles
        /// with BOTH ends raised is refused (RAISED_DECK_BAYS), so
        /// RemoveMiddle refuses shrinking past 1 while every section is
        /// currently raised via `RaiseAll`/`IsRaisedDeck`.
        public int Minimum => IsRaisedDeck ? RaisedDeckMinMiddles : 0;
        const int RaisedDeckMinMiddles = 1;
        public string Rotor => draft.rotorId;
        public bool Dirty => !draft.ValueEquals(baseline);
        public bool CanUndo => undo.Count > 0 && !Committed;
        public bool HasBackend => backend != null;
        public ShipConfiguration Snapshot() => draft.Clone();
        public float OriginalLength { get; }

        /// Isolated seam for the Step 2 backend's `ShipConfiguration.layouts`
        /// renumbering (docs/SHIPYARD-SECTIONS-UI.md "Draft API additions":
        /// InsertMiddle/RemoveSection renumber `layouts` the same way they
        /// renumber `equipment`). That backend does not exist in this
        /// worktree yet -- wired through here rather than called by name
        /// from `InsertMiddle`/`RemoveSection` directly, so the one seam
        /// that needs the other agent's types is this single delegate, set
        /// by whoever builds the draft (`ShipyardModal.Open` for the real
        /// screen). Null is a no-op: only `equipment` renumbers until it is
        /// wired up.
        readonly Action<ShipConfiguration, int, int> renumberLayouts;

        public ShipyardDraft(ModuleLibrary library, ShipConfiguration current, IShipyardRefit backend = null,
            Func<ShipConfiguration, int, string> removalBlocker = null,
            Func<string, string, bool> allowed = null,
            Action<ShipConfiguration, int, int> renumberLayouts = null)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.backend = backend;
            this.removalBlocker = removalBlocker;
            this.allowed = allowed;
            this.renumberLayouts = renumberLayouts;
            baseline = (current ?? throw new ArgumentNullException(nameof(current))).Clone();
            draft = baseline.Clone();
            Assembly = ShipAssembler.Assemble(draft, library);
            if (!Assembly.ok) throw new ArgumentException(Reason(Assembly));
            OriginalLength = Assembly.overallLengthM;
        }

        static string Reason(AssemblyResult result) => result.rejections.Count == 0
            ? "This configuration is unavailable." : result.rejections[0].message;

        bool Refuse(string reason) { Message = reason; Changed?.Invoke(); return false; }

        bool Set(ShipConfiguration next, string highlight)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            // Slot configurations (schema 3): fits with no place any more go
            // to the store, deck guns follow the cannon fits.
            if (CoasterFamily.Is(next)) CoasterFamily.Wheel(next);
            if (next.UsesSlots) next = SlotModel.Normalized(next, library);
            var result = ShipAssembler.Assemble(next, library);
            if (!result.ok) return Refuse(Reason(result));
            if (next.ValueEquals(draft)) return false;
            undo.Push(draft.Clone()); draft = next; Assembly = result;
            Highlight = highlight; Message = ""; Changed?.Invoke(); return true;
        }

        /// True while the draft is built from the W1x (expanded-beam) family;
        /// false for the standard W1-r2 family (docs/SHIPYARD-API.md §15).
        (string sternId, string[] middleIds, string bowId) SectionIds(DeckLevel stern, IList<DeckLevel> middles, DeckLevel bow) =>
            IsCoaster ? CoasterFamily.ToIds(stern,middles,bow) : RaisedSections.ToIds(stern,middles,bow);
        public bool IsCoaster => CoasterFamily.Is(draft);
        public bool IsWideBeam => draft.sternId == ExpandedPresets.ExpandedStern;

        /// The middle module id for the draft's OWN current width/deck family
        /// -- never the standard one outright (that was the 2026-09-25 bug: a
        /// wide-beam ship's AddMiddle used to add a W1-r2 middle, which
        /// ShipAssembler refuses to join to a W1x stern/bow). Raised-deck
        /// (docs/RAISED-DECK.md sec 3) is its own family on top of wide.
        string MiddleIdForWidth() => IsCoaster ? CoasterFamily.Hull("middle",false) : IsRaisedDeck ? RaisedPresets.RaisedMiddle
            : IsWideBeam ? ExpandedPresets.ExpandedMiddle : ShipConfiguration.V3Middle;

        // First prototype appends/removes the bay immediately behind the bow.
        // Existing bay indices, and therefore equipment references, never shift.
        public bool AddMiddle()
        {
            string middleId = MiddleIdForWidth();
            if (!CanSelect(ModuleKind.Middle, middleId)) return Refuse("This section is unavailable.");
            if (Count >= Maximum) return Refuse("Maximum length for this hull.");
            var next = Snapshot(); next.middleIds.Add(middleId);
            return Set(next, ShipAssembler.MiddleKey(Count));
        }

        public bool RemoveMiddle()
        {
            if (Count == 0) return Refuse("The bow and stern must remain.");
            if (Count <= Minimum) return Refuse("With no middle bays, only one end of the ship can be raised.");
            if (backend != null && removalBlocker == null)
                return Refuse("Section availability is not connected yet.");
            string reason = RemovalReason();
            if (!string.IsNullOrEmpty(reason)) return Refuse(reason);
            var next = Snapshot();
            int idx = Count - 1;
            next.middleIds.RemoveAt(idx);
            // A gun standing on the bay that just left has nowhere to stand
            // any more (its slot id is gone); take it off to the dry dock
            // instead of refusing the shrink (2026-09-25, Kevin: "guns
            // without a slot go to the dry dock", the same equipment API a
            // player would use by hand -- see docs/SHIPYARD-API.md §15).
            string prefix = ShipAssembler.MiddleKey(idx) + "/";
            int orphaned = next.equipment.RemoveAll(e => e != null && e.slotId != null && e.slotId.StartsWith(prefix));
            bool applied = Set(next, "bow");
            if (applied && orphaned > 0)
            {
                Message = orphaned == 1 ? "1 gun will go to the dry dock." : $"{orphaned} guns will go to the dry dock.";
                Changed?.Invoke();
            }
            return applied;
        }

        // ---- section-by-section editing (docs/SHIPYARD-SECTIONS-UI.md Step 1) --

        /// Inserts a middle at `index` (0..Count), width/level matching its
        /// neighbours: a new section is RAISED only when BOTH the section
        /// that will sit aft of it and the one fwd of it are already
        /// raised (docs/SHIPYARD-SECTIONS-UI.md "Draft API additions");
        /// otherwise LOW, even on a wide raised ship. Every existing
        /// `middle[i]` at or past `index` renumbers by +1 in `equipment`
        /// (and `layouts`, once the Step 2 backend lands -- see
        /// `renumberLayouts`). Existing bays before `index` never move.
        public bool InsertMiddle(int index)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            if (Count >= Maximum) return Refuse("Maximum length for this hull.");
            index = Math.Max(0, Math.Min(index, Count));
            string middleId = MiddleIdForWidth();
            if (!CanSelect(ModuleKind.Middle, middleId)) return Refuse("This section is unavailable.");
            var next = Snapshot();
            if (IsWideBeam || IsCoaster)
            {
                var levels = CurrentLevels();
                var middles = new List<DeckLevel>(levels.middles);
                DeckLevel left = index == 0 ? levels.stern : middles[index - 1];
                DeckLevel right = index >= middles.Count ? levels.bow : middles[index];
                DeckLevel inserted = left == DeckLevel.Raised && right == DeckLevel.Raised ? DeckLevel.Raised : DeckLevel.Low;
                middles.Insert(index, inserted);
                var (sId, mIds, bId) = SectionIds(levels.stern, middles, levels.bow);
                next.sternId = sId; next.middleIds = new List<string>(mIds); next.bowId = bId;
            }
            else next.middleIds.Insert(index, middleId);
            RenumberMiddleKeys(next, index, +1);
            // A neighbour that turned from connected to a wall variant loses
            // its third-deck socket; its layer goes too (2026-09-27).
            UpperDeckLayers.DropOrphaned(next, library);
            return Set(next, ShipAssembler.MiddleKey(index));
        }

        /// Removes any ONE middle bay by its instance key -- unlike
        /// `RemoveMiddle` (last bay only), a section sheet lets the player
        /// pick which one goes. Guns standing on it go to the dry dock
        /// (same pattern as `RemoveMiddle`); every later `middle[i]`
        /// renumbers down by 1 in `equipment` (and `layouts`).
        public bool RemoveSection(string sectionKey)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            int index = MiddleIndex(sectionKey);
            string reason = RemovalReasonFor(index);
            if (!string.IsNullOrEmpty(reason)) return Refuse(reason);
            var next = Snapshot();
            // Its fitted modules go to the store (slot configs), never onto a neighbour.
            next.fits?.RemoveAll(f => f != null && f.section == sectionKey);
            next.middleIds.RemoveAt(index);
            if (IsWideBeam || IsCoaster)
            {
                var levels = CurrentLevels();
                var middles = new List<DeckLevel>(levels.middles);
                middles.RemoveAt(index);
                var (sId, mIds, bId) = SectionIds(levels.stern, middles, levels.bow);
                next.sternId = sId; next.middleIds = new List<string>(mIds); next.bowId = bId;
            }
            string prefix = ShipAssembler.MiddleKey(index) + "/";
            int orphaned = next.equipment.RemoveAll(e => e != null && e.slotId != null
                && (e.slotId.StartsWith(prefix) || e.slotId.StartsWith("fitting:" + prefix)));
            next.fittings?.RemoveAll(f => f != null && f.socketId != null && f.socketId.StartsWith(prefix));
            RenumberMiddleKeys(next, index, -1);
            UpperDeckLayers.DropOrphaned(next, library);
            string highlight = index > 0 ? ShipAssembler.MiddleKey(index - 1) : ShipAssembler.StdKeyStern;
            bool applied = Set(next, highlight);
            if (applied && orphaned > 0)
            {
                Message = orphaned == 1 ? "1 gun will go to the dry dock." : $"{orphaned} guns will go to the dry dock.";
                Changed?.Invoke();
            }
            return applied;
        }

        /// `equipment`'s (and, via `renumberLayouts`, `layouts`') own
        /// `middle[i]` keys, shifted by `delta` for every `i >= fromIndex`
        /// -- the general form `AddMiddle`/`RemoveMiddle` never needed
        /// because they only ever touched the LAST bay.
        void RenumberMiddleKeys(ShipConfiguration cfg, int fromIndex, int delta)
        {
            foreach (var e in cfg.equipment)
            {
                if (e == null || string.IsNullOrEmpty(e.slotId)) continue;
                int idx = SlotMiddleIndex(e.slotId);
                if (idx >= fromIndex) e.slotId = WithSlotMiddleIndex(e.slotId, idx + delta);
            }
            renumberLayouts?.Invoke(cfg, fromIndex, delta);
        }

        static int SlotMiddleIndex(string slotId)
        {
            int slash = slotId.IndexOf('/');
            return MiddleIndex(slash >= 0 ? slotId.Substring(0, slash) : slotId);
        }

        static string WithSlotMiddleIndex(string slotId, int newIndex)
        {
            int slash = slotId.IndexOf('/');
            return ShipAssembler.MiddleKey(newIndex) + (slash >= 0 ? slotId.Substring(slash) : "");
        }

        /// An opaque copy of the draft, taken when a section sheet opens
        /// (`BeginSection`) so `ResetSection` can put it back untouched.
        /// The prototype resets the WHOLE draft rather than isolating one
        /// section's own fields, same simplification `Undo` already makes --
        /// a section sheet is the only thing editing the draft while it is
        /// open, so the two read the same.
        public readonly struct DraftSnapshot
        {
            internal readonly ShipConfiguration configuration;
            internal DraftSnapshot(ShipConfiguration configuration) { this.configuration = configuration; }
        }

        public DraftSnapshot BeginSection(string key) => new DraftSnapshot(Snapshot());

        /// Puts the draft back to what `BeginSection(key)` captured. `key`
        /// is taken for symmetry with `BeginSection` and as the highlight
        /// to restore, not to scope the revert -- see `DraftSnapshot`.
        public void ResetSection(string key, DraftSnapshot snapshot)
        {
            if (Committed || snapshot.configuration == null) return;
            Set(snapshot.configuration.Clone(), key);
        }

        /// Swaps EVERY hull section between the W1-r2 and W1x families at
        /// once -- the two widths never mix (docs/SHIPYARD-API.md §9,
        /// enforced by ShipAssembler's join-profile check). Equipment is
        /// left untouched: the two families share the same slot ids (only
        /// their Y moved), so a fitted gun stays fitted.
        public bool SetWideBeam(bool wide)
        {
            if (IsCoaster) return Refuse("Widening is not available for this design yet.");
            // `IsRaisedDeck` alone (the whole-hull "every section raised,
            // connected" check) is too narrow a guard here now that a
            // single section can be raised on its own (docs/RAISED-SECTIONS.md
            // task item 4) -- e.g. just a raised stern (its own wall-forward
            // id, not the connected one IsRaisedDeck looks for) would
            // otherwise pass this guard and then get silently overwritten
            // to the standard-beam id below, discarding it with no refusal.
            if (!wide && AnySectionRaised()) return Refuse("A raised deck needs the wide beam.");
            if (IsWideBeam == wide) return false;
            string sternId = wide ? ExpandedPresets.ExpandedStern : ShipConfiguration.V3Stern;
            string bowId = wide ? ExpandedPresets.ExpandedBow : ShipConfiguration.V3Bow;
            if (!CanSelect(ModuleKind.Stern, sternId) || !CanSelect(ModuleKind.Bow, bowId))
                return Refuse("This beam is unavailable.");
            var next = Snapshot();
            next.sternId = sternId;
            next.bowId = bowId;
            string middleId = wide ? ExpandedPresets.ExpandedMiddle : ShipConfiguration.V3Middle;
            for (int i = 0; i < next.middleIds.Count; i++) next.middleIds[i] = middleId;
            return Set(next, ShipAssembler.StdKeyStern);
        }

        /// True while the draft is built from the raised-deck family
        /// (W1xR), on top of the wide beam (docs/RAISED-DECK.md sec 3).
        public bool IsRaisedDeck => draft.sternId == RaisedPresets.RaisedStern;

        /// Null when `SetRaisedDeck(true)`/`RaiseAll()` would succeed right
        /// now; the reason to show next to a disabled toggle otherwise
        /// (docs/RAISED-SECTIONS.md sec 5: needs wide beam, and at least one
        /// middle bay -- 0 middles can never have both ends raised at once).
        public string RaisedDeckUnavailableReason()
        {
            if (IsCoaster) return null;
            if (IsRaisedDeck) return null;
            if (!IsWideBeam && !IsCoaster) return "A raised deck needs the wide beam.";
            if (Count < 1) return "With no middle bays, only one end of the ship can be raised.";
            if (!CanSelect(ModuleKind.Stern, RaisedPresets.RaisedStern) || !CanSelect(ModuleKind.Bow, RaisedPresets.RaisedBow))
                return "This deck is unavailable.";
            return null;
        }

        /// Swaps EVERY hull section between the W1x (wide, single-deck) and
        /// W1xR (wide, raised-deck) families at once -- mirrors
        /// `SetWideBeam` exactly, one level up (docs/RAISED-DECK.md sec 3/8).
        /// Turning the raised deck OFF drops back to wide W1x, never to the
        /// standard beam (raised requires wide; `SetWideBeam` is the only
        /// path back to standard, and it refuses while raised). Kept as the
        /// entry point for existing callers/probes (`RaiseAll`/`LowerAll`
        /// below are thin aliases, docs/RAISED-SECTIONS.md task item 4).
        public bool SetRaisedDeck(bool raised)
        {
            if (IsCoaster) { var c=Snapshot(); c.sternId=CoasterFamily.Hull("stern",raised); c.bowId=CoasterFamily.Hull("bow",raised); for(int i=0;i<c.middleIds.Count;i++) c.middleIds[i]=CoasterFamily.Hull("middle",raised); CoasterFamily.Wheel(c); DropOrphanedGuns(c); return Set(c,"stern"); }
            if (IsRaisedDeck == raised) return false;
            if (raised)
            {
                string reason = RaisedDeckUnavailableReason();
                if (reason != null) return Refuse(reason);
            }
            var next = Snapshot();
            next.sternId = raised ? RaisedPresets.RaisedStern : ExpandedPresets.ExpandedStern;
            next.bowId = raised ? RaisedPresets.RaisedBow : ExpandedPresets.ExpandedBow;
            string middleId = raised ? RaisedPresets.RaisedMiddle : ExpandedPresets.ExpandedMiddle;
            for (int i = 0; i < next.middleIds.Count; i++) next.middleIds[i] = middleId;
            return Set(next, ShipAssembler.StdKeyStern);
        }

        /// "Raise all" / "Lower all" (docs/RAISED-SECTIONS.md task item 4):
        /// the replacement for the old all-or-nothing "Deck: Single/Raised"
        /// row, kept as the exact same uniform swap `SetRaisedDeck` already
        /// did -- raising every section still needs the RAISED_DECK_BAYS
        /// gate (0 middles can never have both ends raised at once), which
        /// `SetRaisedDeck(true)`'s existing `RaisedDeckUnavailableReason`
        /// check already enforces.
        public bool RaiseAll() => SetRaisedDeck(true);
        public bool LowerAll() => SetRaisedDeck(false);

        // ---- per-section raised deck (docs/RAISED-SECTIONS.md task item 4) --

        /// Every hull section instance key in the draft, aft to fore:
        /// "stern", "middle[0]".."middle[Count-1]", "bow" -- what the
        /// screen's tap targets iterate to build one big target per section.
        public IReadOnlyList<string> SectionKeys()
        {
            var keys = new List<string> { ShipAssembler.StdKeyStern };
            for (int i = 0; i < Count; i++) keys.Add(ShipAssembler.MiddleKey(i));
            keys.Add(ShipAssembler.StdKeyBow);
            return keys;
        }

        static int MiddleIndex(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith("middle[") || !key.EndsWith("]")) return -1;
            return int.TryParse(key.Substring(7, key.Length - 8), out int i) ? i : -1;
        }

        RaisedSections.SectionLevels CurrentLevels() => RaisedSections.FromIds(draft.sternId, draft.middleIds, draft.bowId);

        static void SetLevel(ref RaisedSections.SectionLevels levels, string key, DeckLevel value)
        {
            if (key == ShipAssembler.StdKeyStern) { levels.stern = value; return; }
            if (key == ShipAssembler.StdKeyBow) { levels.bow = value; return; }
            int idx = MiddleIndex(key);
            if (idx >= 0 && idx < levels.middles.Length) levels.middles[idx] = value;
        }

        /// Whether `key` (a `SectionKeys()` entry) is currently raised, read
        /// straight off the draft's own ids via `RaisedSections.FromIds`
        /// (never a second stored flag -- the id is the single source).
        public bool IsSectionRaised(string key)
        {
            var levels = CurrentLevels();
            if (key == ShipAssembler.StdKeyStern) return levels.stern == DeckLevel.Raised;
            if (key == ShipAssembler.StdKeyBow) return levels.bow == DeckLevel.Raised;
            int idx = MiddleIndex(key);
            return idx >= 0 && idx < levels.middles.Length && levels.middles[idx] == DeckLevel.Raised;
        }

        /// True while ANY section is raised, regardless of whether every
        /// section is (the narrower `IsRaisedDeck`, which only recognises
        /// the whole-hull "connected" id on the stern). Used to guard
        /// `SetWideBeam(false)` so a single raised section is never
        /// silently discarded by leaving the beam.
        public bool AnySectionRaised()
        {
            var levels = CurrentLevels();
            if (levels.stern == DeckLevel.Raised || levels.bow == DeckLevel.Raised) return true;
            foreach (var m in levels.middles) if (m == DeckLevel.Raised) return true;
            return false;
        }

        /// Null when `ToggleSection(key)` would raise the section right now
        /// (lowering never needs anything the ship does not already have);
        /// the reason to show next to a disabled tap target otherwise.
        /// Probes the REAL assembler with the levels this toggle would
        /// produce (via `RaisedSections.ToIds`) rather than re-deriving the
        /// rule by hand, so it never drifts from what `ToggleSection` itself
        /// would actually do.
        public string SectionUnavailableReason(string key)
        {
            if (!IsWideBeam && !IsCoaster) return "A raised deck needs the wide beam.";
            if (IsSectionRaised(key)) return null;
            var levels = CurrentLevels();
            SetLevel(ref levels, key, DeckLevel.Raised);
            var (sId, mIds, bId) = SectionIds(levels.stern, levels.middles, levels.bow);
            var probe = Snapshot();
            probe.sternId = sId; probe.middleIds = new List<string>(mIds); probe.bowId = bId;
            probe.equipment.Clear(); // hull-only probe; ToggleSection handles orphaned guns itself
            if (IsCoaster) CoasterFamily.Wheel(probe);
            var result = ShipAssembler.Assemble(probe, library);
            return result.ok ? null : Reason(result);
        }

        /// Flips one section between Low and Raised, recomputing every
        /// section's id from the new levels (`RaisedSections.ToIds` --
        /// toggling one section can change a NEIGHBOUR's own id too, e.g. a
        /// raised stern next to a middle that just went low switches from
        /// its connected id to its own wall-forward id). A gun whose slot
        /// id does not survive the new ids goes to the dry dock, same
        /// pattern as `RemoveMiddle`'s own orphaned-gun handling, just
        /// checked by real slot resolution instead of a key prefix (a
        /// toggle changes a module id, not a whole instance).
        public bool ToggleSection(string key)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            string reason = IsSectionRaised(key) ? null : SectionUnavailableReason(key);
            if (reason != null) return Refuse(reason);
            var levels = CurrentLevels();
            SetLevel(ref levels, key, IsSectionRaised(key) ? DeckLevel.Low : DeckLevel.Raised);
            var (sId, mIds, bId) = SectionIds(levels.stern, levels.middles, levels.bow);
            var next = Snapshot();
            next.sternId = sId; next.middleIds = new List<string>(mIds); next.bowId = bId;
            if (IsCoaster) CoasterFamily.Wheel(next);
            UpperDeckLayers.DropOrphaned(next, library); // a third deck needs its connected raised section
            int orphaned = DropOrphanedGuns(next);
            bool applied = Set(next, key);
            if (applied && orphaned > 0)
            {
                Message = orphaned == 1 ? "1 gun will go to the dry dock." : $"{orphaned} guns will go to the dry dock.";
                Changed?.Invoke();
            }
            return applied;
        }

        // ---- upper-deck layers (2026-09-27): third deck / gun foredeck --------

        /// The upper-deck layer this section can carry (third deck on a
        /// connected raised section, foredeck on the V3 bow), or null.
        public string UpperDeckOption(string key) => IsCoaster ? null : UpperDeckLayers.OptionFor(draft, key, library, out _);

        public bool HasUpperDeck(string key) => UpperDeckLayers.Has(draft, key);

        /// Fits or removes this section's upper-deck layer. Removing the
        /// foredeck sends its guns to the dry dock (the draft's own dock diff).
        public bool ToggleUpperDeck(string key)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            bool had = HasUpperDeck(key);
            if (!had && UpperDeckOption(key) == null) return Refuse("This section cannot carry an upper deck.");
            var next = UpperDeckLayers.With(Snapshot(), key, !had, library);
            int guns = draft.equipment.Count - next.equipment.Count;
            bool applied = Set(next, key);
            if (applied && guns > 0)
            {
                Message = guns == 1 ? "1 gun will go to the dry dock." : $"{guns} guns will go to the dry dock.";
                Changed?.Invoke();
            }
            return applied;
        }

        /// Removes every equipment entry whose slot id does not resolve on
        /// `next`'s OWN hull (probed bare, equipment-free, so a genuinely
        /// invalid hull is left for `Set()` to report normally) -- the
        /// general form of `RemoveMiddle`'s prefix removal, needed here
        /// because a toggle changes a module id in place rather than
        /// dropping a whole instance key.
        int DropOrphanedGuns(ShipConfiguration next)
        {
            var bare = next.Clone(); bare.equipment.Clear();
            var bareAsm = ShipAssembler.Assemble(bare, library);
            if (!bareAsm.ok) return 0;
            var validSlotIds = new HashSet<string>();
            foreach (var s in bareAsm.slots) if (s != null && s.role == SocketRole.DeckSlot) validSlotIds.Add(s.qualifiedId);
            var kept = new List<EquipmentChoice>();
            int dropped = 0;
            foreach (var e in next.equipment)
            {
                if (e != null && !string.IsNullOrEmpty(e.slotId) && validSlotIds.Contains(e.slotId)) kept.Add(e);
                else dropped++;
            }
            next.equipment = kept;
            return dropped;
        }

        // ---- guns + dry dock (2026-09-25) ------------------------------

        /// Every deck-gun slot of the draft (empty or fitted), for the
        /// screen's gun rows.
        public IReadOnlyList<EquipmentSlotView> EquipmentSlots() =>
            backend != null ? backend.EquipmentSlots(Snapshot()) : Array.Empty<EquipmentSlotView>();

        /// Fits a deck cannon on an empty slot. Whether one is actually in
        /// the dry dock is checked at Confirm time (ApplyRefit); bind a
        /// picker's enabled state to the report's `dryDock` rows so a tap
        /// that cannot succeed is never offered.
        public bool FitGun(string slotId)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            if (backend == null) return Refuse("Live refitting is not connected.");
            if (draft.UsesSlots)
            {
                // Slots: a cannon fit in the port that mount serves (the
                // phase-2 screen uses ShipyardSlotDraft instead).
                if (!SlotModel.CellForMount(draft, library, slotId, out var sec, out int deck, out var cell))
                    return Refuse("That gun slot has no gun port on this ship.");
                var nextFit = Snapshot();
                nextFit.fits.RemoveAll(f => f.SameCell(sec, deck, cell));
                nextFit.fits.Add(new SlotFit { section = sec, deck = deck, cell = cell, moduleId = SlotModel.Cannon(library.Catalog) });
                return Set(nextFit, slotId);
            }
            var edit = backend.FitEquipment(Snapshot(), slotId, ShipConfiguration.EquipmentCannon);
            if (!edit.ok) return Refuse(edit.message);
            return Set(edit.draft, slotId);
        }

        /// Sends the gun fitted at `slotId` to the dry dock.
        public bool RemoveGun(string slotId)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            if (backend == null) return Refuse("Live refitting is not connected.");
            if (draft.UsesSlots)
            {
                if (!SlotModel.CellForMount(draft, library, slotId, out var sec, out int deck, out var cell))
                    return Refuse("That gun slot has no gun port on this ship.");
                var nextOff = Snapshot();
                if (nextOff.fits.RemoveAll(f => f.SameCell(sec, deck, cell)) == 0) return Refuse("No gun stands there.");
                return Set(nextOff, slotId);
            }
            var edit = backend.RemoveEquipment(Snapshot(), slotId);
            if (!edit.ok) return Refuse(edit.message);
            return Set(edit.draft, slotId);
        }

        public bool ChooseWheel(string id)
        {
            if (!CanSelect(ModuleKind.Rotor, id)) return Refuse("This wheel is unavailable.");
            if (id != ShipConfiguration.TimberRotor && id != ShipConfiguration.ReinforcedRotor)
                return Refuse("That wheel is not available in this shipyard yet.");
            var next = Snapshot(); next.rotorId = id;
            return Set(next, ShipAssembler.StdKeyRotor);
        }

        public void Undo()
        {
            if (!CanUndo) return;
            draft = undo.Pop(); Assembly = ShipAssembler.Assemble(draft, library);
            Highlight = null; Message = ""; Changed?.Invoke();
        }

        public bool CanSelect(string kind, string id) => allowed == null || allowed(kind, id);
        public string RemovalReason() => Count == 0 ? "The bow and stern must remain." :
            Count <= Minimum ? "With no middle bays, only one end of the ship can be raised." :
            backend != null && removalBlocker == null ? "Section availability is not connected yet." :
            removalBlocker?.Invoke(Snapshot(), Count - 1);

        /// The general form of `RemovalReason()` -- any middle bay by its
        /// own index, not just the last one (the section sheet's "Remove
        /// this section" needs to ask about the bay it is showing, which is
        /// rarely the last). Null = removing it right now would succeed.
        public string RemovalReasonFor(int index)
        {
            if (index < 0 || index >= Count) return "Only a middle section can be removed.";
            if (Count <= Minimum) return "With no middle bays, only one end of the ship can be raised.";
            if (backend != null && removalBlocker == null) return "Section availability is not connected yet.";
            return removalBlocker?.Invoke(Snapshot(), index);
        }

        /// The Interior page's own `Set` seam: `WithBerths` (Step 2 backend)
        /// returns a whole new `ShipConfiguration`, not a draft mutation, so
        /// it goes through the same undo-tracked `Set` every other control
        /// uses rather than duplicating that bookkeeping in the UI.
        public bool ReplaceForInterior(ShipConfiguration next, string sectionKey) => Set(next, sectionKey);

        public string CannotConfirm()
        {
            if (Committed) return "This refit is already confirmed.";
            if (!Dirty) return "No changes yet.";
            if (backend == null) return "Preview only. Live refitting is not connected.";
            return backend.Validate(Snapshot());
        }

        public bool Confirm()
        {
            string reason = CannotConfirm();
            if (!string.IsNullOrEmpty(reason)) return Refuse(reason);
            if (!backend.TryApply(baseline.Clone(), Snapshot(), out reason))
                return Refuse(string.IsNullOrEmpty(reason) ? "Refit could not be applied. Your ship is unchanged." : reason);
            Committed = true; undo.Clear(); Message = "Refit confirmed.";
            Changed?.Invoke(); return true;
        }
    }
}
