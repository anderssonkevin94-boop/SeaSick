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

        public ShipyardDraft(ModuleLibrary library, ShipConfiguration current, IShipyardRefit backend = null,
            Func<ShipConfiguration, int, string> removalBlocker = null,
            Func<string, string, bool> allowed = null)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.backend = backend;
            this.removalBlocker = removalBlocker;
            this.allowed = allowed;
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
            var result = ShipAssembler.Assemble(next, library);
            if (!result.ok) return Refuse(Reason(result));
            if (next.ValueEquals(draft)) return false;
            undo.Push(draft.Clone()); draft = next; Assembly = result;
            Highlight = highlight; Message = ""; Changed?.Invoke(); return true;
        }

        /// True while the draft is built from the W1x (expanded-beam) family;
        /// false for the standard W1-r2 family (docs/SHIPYARD-API.md §15).
        public bool IsWideBeam => draft.sternId == ExpandedPresets.ExpandedStern;

        /// The middle module id for the draft's OWN current width/deck family
        /// -- never the standard one outright (that was the 2026-09-25 bug: a
        /// wide-beam ship's AddMiddle used to add a W1-r2 middle, which
        /// ShipAssembler refuses to join to a W1x stern/bow). Raised-deck
        /// (docs/RAISED-DECK.md sec 3) is its own family on top of wide.
        string MiddleIdForWidth() => IsRaisedDeck ? RaisedPresets.RaisedMiddle
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

        /// Swaps EVERY hull section between the W1-r2 and W1x families at
        /// once -- the two widths never mix (docs/SHIPYARD-API.md §9,
        /// enforced by ShipAssembler's join-profile check). Equipment is
        /// left untouched: the two families share the same slot ids (only
        /// their Y moved), so a fitted gun stays fitted.
        public bool SetWideBeam(bool wide)
        {
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
            if (IsRaisedDeck) return null;
            if (!IsWideBeam) return "A raised deck needs the wide beam.";
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
            if (!IsWideBeam) return "A raised deck needs the wide beam.";
            if (IsSectionRaised(key)) return null;
            var levels = CurrentLevels();
            SetLevel(ref levels, key, DeckLevel.Raised);
            var (sId, mIds, bId) = RaisedSections.ToIds(levels.stern, levels.middles, levels.bow);
            var probe = Snapshot();
            probe.sternId = sId; probe.middleIds = new List<string>(mIds); probe.bowId = bId;
            probe.equipment.Clear(); // hull-only probe; ToggleSection handles orphaned guns itself
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
            var (sId, mIds, bId) = RaisedSections.ToIds(levels.stern, levels.middles, levels.bow);
            var next = Snapshot();
            next.sternId = sId; next.middleIds = new List<string>(mIds); next.bowId = bId;
            int orphaned = DropOrphanedGuns(next);
            bool applied = Set(next, key);
            if (applied && orphaned > 0)
            {
                Message = orphaned == 1 ? "1 gun will go to the dry dock." : $"{orphaned} guns will go to the dry dock.";
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
            var edit = backend.FitEquipment(Snapshot(), slotId, ShipConfiguration.EquipmentCannon);
            if (!edit.ok) return Refuse(edit.message);
            return Set(edit.draft, slotId);
        }

        /// Sends the gun fitted at `slotId` to the dry dock.
        public bool RemoveGun(string slotId)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            if (backend == null) return Refuse("Live refitting is not connected.");
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
