using System;
using System.Collections.Generic;
using SeaSick.Steamer;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Result of ApplyRefit.
    public class ShipyardApplyResult
    {
        public bool ok;
        public readonly List<Rejection> issues = new List<Rejection>();
        /// The configuration the ship has AFTER the call (the new one on
        /// success, the unchanged previous one on failure). A copy.
        public ShipConfiguration configuration;
        public ShipyardValidation validation;

        public override string ToString()
        {
            if (ok) return "refitted: " + (validation != null ? validation.Summary() : "");
            var s = new List<string>();
            foreach (var i in issues) s.Add(i.ToString());
            return string.Join("\n", s);
        }
    }

    /// **The modular shipyard on the player's steamer** (docs/SHIPYARD-API.md).
    ///
    /// Added by `SteamerBootstrap.Convert`. Until a refit is confirmed (or a
    /// save carries a configuration) she is the standard long steamer drawn
    /// with the approved V8 art, exactly as before this component existed:
    /// `ModularActive` is false and nothing here runs.
    ///
    /// A refit rebuilds her IN PLACE through `SteamerBootstrap.Assemble` --
    /// the same construction path the conversion used -- on the same
    /// GameObject and Rigidbody, so everything that holds a reference to her
    /// keeps holding the right thing. Identity, damage (HullIntegrity's
    /// fraction), hold, crew, pose and mooring are untouched by construction;
    /// what changes is the drawing, the hull form and everything derived
    /// from it. See the API doc for why the ship object is not replaced.
    ///
    /// Validate / BuildPreview / CanRefitNow touch nothing. ApplyRefit is
    /// atomic: any exception rolls her back to the previous build.
    [DisallowMultipleComponent]
    public class ShipyardService : MonoBehaviour
    {
        /// The player's shipyard handle; null when the player is not on the
        /// steamer (the ladder ship) or before the conversion ran.
        public static ShipyardService Player { get; private set; }

        /// Raised after a successful ApplyRefit (and after a save's
        /// configuration was applied on load), with a copy of the new one.
        public event Action<ShipConfiguration> Refitted;

        /// (old ship, new ship), raised after every successful refit and
        /// after a save's configuration is applied. In this build the ship is
        /// rebuilt IN PLACE, so old == new; readers that rebind on it now keep
        /// working unchanged if a later build replaces the object.
        public static event Action<GameObject, GameObject> PlayerShipReplaced;

        /// Speed above which she is "under way" for the refit guard, m/s.
        public const float AtRestSpeed = 0.3f;

        HullFormData reference;
        GameObject referenceHull;
        Transform referenceWheel;
        ModuleLibrary library;
        ShipConfiguration current = ShipConfiguration.Long();
        ShipyardPlan currentPlan;
        ModularShipView view;
        bool modularActive;
        DryDock dock = DryDock.Empty();

        /// A copy of what she is built from. Mutating it changes nothing.
        public ShipConfiguration Current => current.Clone();
        /// A copy of what is in her dry dock (equipment a refit has taken
        /// off her, waiting to be fitted again). Mutating it changes nothing.
        public DryDock Dock => dock.Clone();
        /// True once she is drawn from modules (a refit, or a save that
        /// carried a configuration). False = the untouched standard steamer.
        public bool ModularActive => modularActive;
        /// The hull form she is sailing on now (read-only use).
        public HullFormData ActiveData => currentPlan != null ? currentPlan.data : reference;
        /// The assembly she is drawn from, or null while not ModularActive.
        public AssemblyResult ActiveAssembly => modularActive && currentPlan != null ? currentPlan.assembly : null;
        /// Where the drawn rotor spins (the drive's wheel); null while not ModularActive.
        public Transform RotorPivot => view != null ? view.RotorPivot : null;

        public ModuleLibrary Library
        {
            get
            {
                if (library == null) library = ModuleLibrary.LoadFromResources();
                return library;
            }
        }

        /// Called once by SteamerBootstrap.Convert with the reference hull
        /// form (today's steamer) and the V8 visual she was given.
        public void Bind(HullFormData referenceData, GameObject v8Hull, Transform v8Wheel)
        {
            reference = referenceData;
            referenceHull = v8Hull;
            referenceWheel = v8Wheel;
            current = ShipConfiguration.Long();
            currentPlan = null;
            modularActive = false;
            dock = DryDock.Empty();
            Player = this;
        }

        void OnDestroy()
        {
            if (Player == this) Player = null;
        }

        /// Statics outlive play mode here (domain reload is off).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Player = null;
            PlayerShipReplaced = null;
            PersistPathOverride = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TestFaultStage = null;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ---- TEST-ONLY fault injection (ShipyardUiProbe) ---------------------
        //
        // Compiled only in the editor and development builds; a release player
        // has none of this. Null (the default, and after every play-mode start)
        // = no fault, and the checks below are one string compare each.
        //
        // A probe sets TestFaultStage to one of the Fault* names and calls
        // ApplyRefit with a valid draft; the refit then throws a
        // ShipyardTestFaultException at that point, and the probe checks that
        // she comes back exactly as she was. The probe MUST put it back to null
        // (ShipyardUiProbe does so in a finally).
        //
        //   FaultAfterSnapshot -- in Rebuild, after the previous build was
        //       captured and the new hull's GameObject was created, before
        //       anything is drawn or assembled. Exercises Rebuild's catch.
        //   FaultAfterAssemble -- in Rebuild, after the new drawing is built
        //       AND SteamerBootstrap.Assemble has reshaped her physics to the
        //       new form, before the drawings are swapped. The hardest
        //       rollback: the old form must be re-assembled over the new one.
        //   FaultBeforePersist -- at the start of Persist, after a successful
        //       rebuild. SaveGame.SaveTo turns every exception into `false`, so
        //       the fault is caught right here and reported the same way a
        //       failed save is: ApplyRefit's SAVE_FAILED path (swap back).
        //       It differs from an unwritable path in that the save target is
        //       fine and nothing was written.

        /// Test-only: the refit stage at which to throw, or null (normal play).
        public static string TestFaultStage { get; set; }
        public const string FaultAfterSnapshot = "after-snapshot";
        public const string FaultAfterAssemble = "after-assemble";
        public const string FaultBeforePersist = "before-persist";

        static void TestFault(string stage)
        {
            if (TestFaultStage != null && TestFaultStage == stage) throw new ShipyardTestFaultException(stage);
        }
#endif

        // ---- reading --------------------------------------------------------

        public IReadOnlyList<string> AllowedModuleIds(string kind) => ShipyardPolicy.AllowedModuleIds(kind);

        /// Pure: checks the draft against the prototype policy, the module
        /// rules and what she is carrying now. Changes nothing.
        public ShipyardValidation Validate(ShipConfiguration draft) =>
            ShipyardPlanner.Validate(draft != null ? draft.Clone() : null, Library, reference, Snapshot());

        /// Validate, as the rich current-vs-proposed report the UI displays.
        public ShipyardReport Report(ShipConfiguration draft)
        {
            var v = Validate(draft);
            var r = ShipyardReport.From(v, v.currentPlan, v.draftPlan, Library.MetresPerUnit);
            if (!CanRefitNow(out string why)) r.refitNowBlockedBecause = why;
            r.dryDock.AddRange(DryDockPreview(draft));
            return r;
        }

        /// A detached, visual-only assembly for the UI to show and destroy.
        /// No physics, no colliders, no scripts that know the live ship.
        /// Built whenever the draft ASSEMBLES within the prototype policy,
        /// even if it would not fit what she carries now (the validation
        /// says so); null when it does not assemble.
        public GameObject BuildPreview(ShipConfiguration draft, Transform parent, out ShipyardValidation validation)
        {
            validation = Validate(draft);
            if (validation.assembly == null || !validation.assembly.ok) return null;
            foreach (var i in validation.issues)
                if (i.code == ShipyardCodes.NotInPrototype || i.code == ShipyardCodes.WheelRequired) return null;
            var go = new GameObject("ShipyardPreview");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<ModularShipView>();
            v.Build(validation.assembly);
            return go;
        }

        public GameObject BuildPreview(ShipConfiguration draft, Transform parent) => BuildPreview(draft, parent, out _);

        // ---- equipment editing (2026-09-25) ----------------------------------
        //
        // Pure: return a NEW draft, never touch the ship, the dock or the
        // save. `Fit` takes from the dock only at ApplyRefit time -- bind
        // your picker's "in stock" state to DryDockPreview so it can grey out
        // a module that is not there, but a Fit call itself never checks.

        public ShipyardEdit FitEquipment(ShipConfiguration draft, string slotId, string moduleId) =>
            ShipyardEquipment.Fit(draft, slotId, moduleId, Library);
        public ShipyardEdit RemoveEquipment(ShipConfiguration draft, string slotId) =>
            ShipyardEquipment.Remove(draft, slotId, Library);
        public ShipyardEdit MoveEquipment(ShipConfiguration draft, string fromSlotId, string toSlotId) =>
            ShipyardEquipment.Move(draft, fromSlotId, toSlotId, Library);
        /// Every deck-gun slot of `draft`: where it is, whether it is usable,
        /// and what (if anything) already occupies it.
        public IReadOnlyList<EquipmentSlotView> EquipmentSlots(ShipConfiguration draft) =>
            ShipyardEquipment.Slots(draft, Library, reference);
        /// What is in the dry dock now, and what would be after applying
        /// `draft` from the LIVE ship's current configuration (the same diff
        /// ApplyRefit uses) -- bind a picker's "in stock" / greyed-out state
        /// to this.
        public IReadOnlyList<DryDockRow> DryDockPreview(ShipConfiguration draft) =>
            ShipyardEquipment.DockPreview(current, draft, dock, Library);

        // ---- interior space budget (2026-09-25, docs/SHIPYARD-SECTIONS-UI.md step 2) --
        //
        // Pure: read-only for the draft, never touch the ship, the dock or
        // the save. `WithBerths` returns a NEW draft (see ShipyardInterior).

        /// `sectionKey`'s space budget, current choice and limits, for the
        /// UI's Interior page.
        public SectionSpaceView SectionSpace(ShipConfiguration draft, string sectionKey) =>
            ShipyardInterior.SectionSpace(draft, sectionKey, Library);

        /// A NEW draft with `sectionKey`'s berths set to `berths`, clamped.
        public ShipConfiguration WithBerths(ShipConfiguration draft, string sectionKey, int berths) =>
            ShipyardInterior.WithBerths(draft, sectionKey, berths, Library);

        /// Whether a refit may be applied right now, and if not, why (a
        /// sentence for the player) -- the FIRST blocker, in priority order.
        /// See `RefitBlockers` for every blocker at once (the ship sheet's
        /// Shipyard button, 2026-09-26).
        public bool CanRefitNow(out string reason)
        {
            var blockers = RefitBlockers();
            reason = blockers.Count > 0 ? blockers[0] : "";
            return blockers.Count == 0;
        }

        /// **Every reason a refit is blocked right now**, not just the
        /// first. `CanRefitNow` still exists and still means "the first of
        /// these, or none" -- `ApplyRefit` and the probes keep asking that
        /// question exactly as before. This is for a caller that wants to
        /// SHOW every blocker at once (the ship sheet's Shipyard button:
        /// Kevin's UX audit, 2026-09-26, "multiple blockers, list them all,
        /// not one at a time") rather than make the player fix one, tap
        /// again, and discover the next.
        ///
        /// The three fundamental checks (no hull form, module data
        /// unusable, a save still loading) still short-circuit: nothing
        /// downstream of them means anything if they fail. Everything after
        /// is independent and all of it is collected.
        public List<string> RefitBlockers()
        {
            var list = new List<string>();
            if (reference == null) { list.Add("This ship has no hull form to refit."); return list; }
            if (!Library.Usable) { list.Add("The ship module data could not be loaded."); return list; }
            if (SeaSick.Save.SaveGame.Restoring) { list.Add("A save is still loading."); return list; }

            // "At rest" is HORIZONTAL way, and tied up at the home dock is at
            // rest by definition (2026-09-24, ShipyardRefitProbe): the full
            // velocity includes the heave of a moored hull in a lively sea,
            // which exceeds 0.3 m/s constantly and refused refits at random
            // at the berth.
            var anchor = GetComponent<AnchorController>();
            var rb = GetComponent<Rigidbody>();
            // Anchored or alongside ANY pier counts too (2026-09-25, Kevin at an
            // outpost pier got "bring her to rest"): the anchor already made
            // her slow down to moor, and a hull lying at a pier drifts on the
            // swell faster than 0.3 m/s. The speed check is for no anchor.
            bool tiedUp = anchor != null && (anchor.AtHomeDock
                || anchor.CurrentState == AnchorController.State.Anchored
                || anchor.CurrentState == AnchorController.State.Ashore);
            if (!tiedUp && rb != null)
            {
                var v = rb.linearVelocity; v.y = 0f;
                if (v.magnitude > AtRestSpeed) list.Add("She is under way. Bring her to rest first.");
            }
            if (anchor != null)
            {
                var st = anchor.CurrentState;
                bool moored = anchor.AtHomeDock || st == AnchorController.State.Anchored || st == AnchorController.State.Ashore;
                if (!moored)
                {
                    list.Add("She must be anchored or alongside to be refitted.");
                }
                else if (!anchor.AtHomeDock)
                {
                    // Refits only happen at the home berth (2026-09-25, Kevin --
                    // stated while making the home berth itself switchable: he
                    // wants his pier at island_2, not anywhere she happens to be
                    // lying). Anchoring off a random island still counts as "at
                    // rest" above; it does not count as home.
                    list.Add($"Refits are done at your home berth ({SeaSick.World.Dock.HomeLabel}).");
                }
                else if (SeaSick.World.DryDockSlip.HomeSlip == null)
                {
                    // **The dry dock gate, 2026-09-26.** She can be moored at
                    // the home berth with nothing to refit HER on -- the
                    // shipyard is a building now (`SeaSick.World.DryDockSlip`),
                    // not just a place to be.
                    list.Add($"Build a dry dock next to your home berth ({SeaSick.World.Dock.HomeLabel}) to refit her.");
                }
            }
            else if (SeaSick.World.DryDockSlip.HomeSlip == null)
            {
                // No `AnchorController` at all (a probe rig, say): the
                // mooring checks above cannot run, but the dry dock still
                // can, and its absence is still worth saying.
                list.Add($"Build a dry dock next to your home berth ({SeaSick.World.Dock.HomeLabel}) to refit her.");
            }
            if (SeaSick.Combat.RaidParty.Active != null) list.Add("Not during a raid.");
            var lockOn = GetComponent<SeaSick.Combat.CombatLock>();
            if (lockOn != null && lockOn.Locked != null) list.Add("Not while she is in a fight.");
            foreach (var o in SeaSick.World.Outpost.All)
                if (o != null && o.Ledger != null && o.Ledger.AnyTransferPending())
                { list.Add("Cargo is being carried between the ship and the stores. Wait for the hands to finish."); break; }
            foreach (var c in FindObjectsByType<SeaSick.Crew.CrewAgent>(FindObjectsSortMode.None))
                if (c != null && c.HomeShip == transform && !c.IsAboard)
                { list.Add($"{c.DisplayName} is ashore. Call the hands back aboard first."); break; }
            return list;
        }

        // ---- applying -------------------------------------------------------

        /// Where ApplyRefit persists. Null = the player's save
        /// (`SaveGame.Path`) -- except in a probe session
        /// (`SaveGame.Suppressed`), where persistence is skipped unless a
        /// probe points this at a scratch file, so a probe never writes the
        /// player's save.
        public static string PersistPathOverride { get; set; }

        /// Rebuild her from `expected` to `draft`, then persist through the
        /// game's own save routine. Refused, with nothing changed, unless:
        /// the live configuration still equals `expected` (else STALE_DRAFT),
        /// CanRefitNow, and a fresh Validate of `draft` against the live ship
        /// all pass. If the save cannot be written she is rebuilt as she was
        /// and the call fails (SAVE_FAILED): on false, ship and save are both
        /// as they were. No resource is spent.
        public ShipyardApplyResult ApplyRefit(ShipConfiguration expected, ShipConfiguration draft)
        {
            var res = new ShipyardApplyResult();
            if (expected == null || !expected.ValueEquals(current))
            {
                res.issues.Add(new Rejection { code = ShipyardCodes.StaleDraft, partId = "",
                    message = "The ship changed since this plan was drawn up. Look again and confirm." });
                res.configuration = Current;
                return res;
            }
            if (!CanRefitNow(out string why))
            {
                res.issues.Add(new Rejection { code = ShipyardCodes.CannotRefitNow, partId = "", message = why });
                res.configuration = Current;
                return res;
            }
            var v = Validate(draft);
            res.validation = v;
            if (!v.ok)
            {
                res.issues.AddRange(v.issues);
                res.configuration = Current;
                return res;
            }

            // The dry dock (D5): equipment `expected` carries that `draft`
            // does not goes IN; equipment `draft` carries that `expected` did
            // not comes FROM it (a move between slots is neither -- same
            // module id, same count). Checked BEFORE anything is rebuilt, so
            // a refusal here leaves the ship untouched.
            var dockDiff = DryDock.Diff(expected, draft);
            bool dockChanges = dockDiff.Count > 0;
            // Surplus hands (2026-09-25, Kevin): never a refusal -- they go
            // ashore to the HOME settlement instead. Landing them is a real
            // scene move, same as the dry dock: it needs somewhere to land
            // THEM, so it needs the home berth too.
            int handsAshore = v.handsAshore;
            if (dockChanges || handsAshore > 0)
            {
                var homeAnchor = GetComponent<AnchorController>();
                if (homeAnchor == null || !homeAnchor.AtHomeDock)
                {
                    string message = dockChanges && handsAshore > 0
                        ? "She must be at her home dock to move equipment to or from storage and to land hands ashore; a plain refit between slots does not."
                        : dockChanges
                            ? "She must be at her home dry dock to move equipment to or from storage; a plain refit between slots does not."
                            : $"She must be at her home berth to land {Hands(handsAshore)} ashore.";
                    string code = dockChanges ? ShipyardCodes.DryDockNotHere : ShipyardCodes.HandsAshoreNeedHome;
                    res.issues.Add(new Rejection { code = code, partId = "", message = message });
                    res.configuration = Current;
                    return res;
                }
                if (dockChanges && !dock.CanApply(dockDiff, out string missingId))
                {
                    string missingName = missingId;
                    if (Library != null && Library.TryGet(missingId, out var missingDef)) missingName = ModuleLibrary.Name(missingDef);
                    res.issues.Add(new Rejection { code = ShipyardCodes.NotInDryDock, partId = missingId ?? "",
                        message = $"There is no {missingName} in the dry dock to fit." });
                    res.configuration = Current;
                    return res;
                }
            }

            var prevPlan = currentPlan;
            bool prevModular = modularActive;
            var prevDock = dock.Clone();

            // Land the surplus hands FIRST, so the new hull is built and
            // crewed for the number of hands she will actually carry --
            // never for more hands than she has berths (SteamerBootstrap.Man
            // still refuses to over-post a deck). Atomic with the rebuild and
            // the save below: any failure from here on restores them.
            var landedHands = new List<SeaSick.Crew.CrewAgent>();
            if (handsAshore > 0 && !LandSurplusHands(handsAshore, landedHands, out string landWhy))
            {
                res.issues.Add(new Rejection { code = ShipyardCodes.ApplyFailed, partId = "", message = landWhy });
                res.configuration = Current;
                return res;
            }

            if (!Rebuild(v.plan, out string fail))
            {
                RestoreLandedHands(landedHands);
                res.issues.Add(new Rejection { code = ShipyardCodes.ApplyFailed, partId = "", message = fail });
                res.configuration = Current;
                return res;
            }
            if (dockChanges) dock.Apply(dockDiff);
            if (!Persist(out string saveWhy))
            {
                // Swap back: the previous build, through the same path.
                string back = null;
                bool restored = prevModular && prevPlan != null ? Rebuild(prevPlan, out back) : RevertToReference(out back, false);
                dock = prevDock;
                RestoreLandedHands(landedHands);
                res.issues.Add(new Rejection { code = ShipyardCodes.SaveFailed, partId = "",
                    message = "The refit could not be saved (" + saveWhy + "), so it was undone." +
                              (restored ? "" : " Undoing it ALSO failed: " + back) });
                res.configuration = Current;
                return res;
            }
            res.ok = true;
            res.configuration = Current;
            Raise();
            return res;
        }

        static string Hands(int n) => n == 1 ? "1 hand" : $"{n} hands";

        /// **The outpost that owns the ship's home berth** (2026-09-25: the
        /// berth is now switchable, so this is no longer always
        /// `Outpost.Home` -- a home berth at island_2's pier lands hands
        /// with island_2's outpost). Resolved from `Dock.Home`'s own
        /// position, the same island lookup `AnchorController.ComeAlongside`
        /// uses when she ties up anywhere: `Island.Nearest` then
        /// `Outpost.Of`. For the harbour this still resolves to
        /// `Outpost.Home` -- the harbour's dock sits on the home island.
        static SeaSick.World.Outpost HomeBerthOutpost()
        {
            var dock = SeaSick.World.Dock.Home;
            if (dock == null) return null;
            var isle = SeaSick.World.Island.Nearest(dock.Berth);
            return isle != null ? SeaSick.World.Outpost.Of(isle) : SeaSick.World.Outpost.Home;
        }

        /// **Where the surplus goes.** Picks `n` hands currently aboard (the
        /// last posted, same order the sea-trial probes have always landed
        /// by -- who specifically is arbitrary, since GUNS_NEED_CREW already
        /// guarantees whoever is LEFT is enough to work her) and leaves them
        /// at the outpost that owns the home berth, through `Outpost.Station`
        /// -- the same "drop a hand at a camp" path a player uses by hand, so
        /// a landed hand becomes a real, named villager on the ledger, never
        /// despawned. False (with any partial landing already rolled back)
        /// if there is no such outpost to land them at, or a hand refuses to
        /// land (already claimed there under his own name).
        bool LandSurplusHands(int n, List<SeaSick.Crew.CrewAgent> landed, out string why)
        {
            why = null;
            var home = HomeBerthOutpost();
            if (home == null)
            {
                why = $"there is no settlement at {SeaSick.World.Dock.HomeLabel} to land {Hands(n)} ashore at";
                return false;
            }
            var crew = new List<SeaSick.Crew.CrewAgent>(GetComponentsInChildren<SeaSick.Crew.CrewAgent>(false));
            for (int i = crew.Count - 1; i >= 0 && landed.Count < n; i--)
            {
                var c = crew[i];
                if (c == null) continue;
                if (!home.LandSurplusFromRefit(c))
                {
                    RestoreLandedHands(landed);
                    why = $"{c.DisplayName} could not be landed ashore";
                    return false;
                }
                landed.Add(c);
            }
            if (landed.Count < n)
            {
                RestoreLandedHands(landed);
                why = $"only {landed.Count} of {n} hands could be landed ashore";
                return false;
            }
            GetComponent<SeaSick.Crew.CrewRoster>()?.Refresh();
            return true;
        }

        /// Undoes `LandSurplusHands`: every hand it moved comes back aboard,
        /// through the same path a player recalling a hand from a camp uses
        /// (`Outpost.Recall`), and that outpost's ledger row for him goes
        /// with it -- a rolled-back refit never leaves a stray villager
        /// behind who was never really landed. Same outpost lookup as the
        /// landing call: the home berth cannot move mid-refit, so it
        /// resolves to the same place.
        void RestoreLandedHands(List<SeaSick.Crew.CrewAgent> landed)
        {
            if (landed == null || landed.Count == 0) return;
            var home = HomeBerthOutpost();
            foreach (var c in landed)
                if (c != null) home?.Recall(c, transform);
            landed.Clear();
            GetComponent<SeaSick.Crew.CrewRoster>()?.Refresh();
        }

        bool Persist(out string why)
        {
            why = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { TestFault(FaultBeforePersist); }
            catch (ShipyardTestFaultException e) { why = e.Message; return false; }
#endif
            string path = PersistPathOverride;
            if (path == null)
            {
                if (SeaSick.Save.SaveGame.Suppressed)
                {
                    Debug.Log("[Shipyard] refit not persisted: a probe is driving this session (SaveGame.Suppressed).");
                    return true;
                }
                path = SeaSick.Save.SaveGame.Path;
            }
            if (SeaSick.Save.SaveGame.SaveTo(path, "refit")) return true;
            why = "the save routine refused or failed; see the console";
            return false;
        }

        /// The save's `ship.modular` field, or "" while she is the untouched
        /// standard steamer (so an old-format save is written as before).
        public string SaveField() => modularActive ? ModularSave.Encode(current) : "";

        /// The save's `ship.dryDock` field, or "" while the dock is empty (so
        /// a game that never used it writes exactly what it always wrote).
        public string DryDockField() => dock.entries.Count > 0 ? dock.ToJson() : "";

        /// Load path (SaveGame.Restore, alongside ApplyFromSave). A
        /// missing/unreadable field is an empty dock, never a refusal.
        public void ApplyDryDockFromSave(string field) => dock = DryDock.FromJson(field);

        /// Load path (SaveGame.Restore, before the hold and crew come back).
        /// Skips the refit guards and the cargo/crew checks -- the save was
        /// written from a ship that held what it holds -- but never the
        /// policy or the module rules: a field this build cannot build
        /// falls back to the standard steamer with a warning.
        public void ApplyFromSave(string field)
        {
            var cfg = ModularSave.Decode(field, Library, out bool has, out string warning);
            if (warning != null) Debug.LogWarning("[Shipyard] " + warning);
            if (!has)
            {
                // An old save: the standard steamer, drawn as she always was.
                if (modularActive && !RevertToReference(out string why))
                    Debug.LogError("[Shipyard] could not return her to the standard steamer: " + why);
                return;
            }
            var refPlan = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), Library, reference, null, out _);
            var plan = ShipyardPlanner.PlanFor(cfg, Library, reference, refPlan, out var asm);
            if (plan == null)
            {
                Debug.LogWarning("[Shipyard] saved configuration did not plan: " + (asm != null ? asm.Summary() : "no hull form"));
                return;
            }
            if (!Rebuild(plan, out string fail)) Debug.LogError("[Shipyard] loading her configuration failed: " + fail);
            else Raise();
        }

        void Raise()
        {
            try { Refitted?.Invoke(Current); }
            catch (Exception e) { Debug.LogException(e, this); }
            try { PlayerShipReplaced?.Invoke(gameObject, gameObject); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        /// Everything derived, in the construction path's own order; on any
        /// exception, the previous build is put back.
        bool Rebuild(ShipyardPlan plan, out string failure)
        {
            failure = null;
            var prevPlan = currentPlan;
            var prevConfig = current;
            bool prevModular = modularActive;
            var prevView = view;

            GameObject fresh = null;
            try
            {
                fresh = new GameObject("ModularHull");
                fresh.transform.SetParent(transform, false);
                fresh.transform.localPosition = plan.viewOffset;
                fresh.transform.localRotation = Quaternion.identity;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                TestFault(FaultAfterSnapshot);
#endif
                var newView = fresh.AddComponent<ModularShipView>();
                newView.Build(plan.assembly);
                if (newView.RotorPivot == null) throw new InvalidOperationException("the assembly drew no wheel");

                SteamerBootstrap.Assemble(gameObject, plan.data, fresh, newView.RotorPivot, null, Options(plan));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                TestFault(FaultAfterAssemble);
#endif

                // Committed: swap the drawings.
                if (prevView != null) { prevView.gameObject.SetActive(false); Destroy(prevView.gameObject); }
                if (referenceHull != null) referenceHull.SetActive(false);
                view = newView;
                currentPlan = plan;
                current = plan.config.Clone();
                modularActive = true;
                Debug.Log($"[Shipyard] refitted: {plan.assembly.overallLengthM:0.00} m overall, " +
                          $"s = ({plan.sLength:0.###}, {plan.sBeam:0.###}, {plan.sDepth:0.###}), {plan.capacity}");
                return true;
            }
            catch (Exception e)
            {
                failure = "the refit could not be built (" + e.Message + "); she is as she was.";
                Debug.LogException(e, this);
                if (fresh != null) { fresh.SetActive(false); Destroy(fresh); }
                // Put the previous build back through the same path.
                try
                {
                    if (prevModular && prevPlan != null && prevView != null)
                        SteamerBootstrap.Assemble(gameObject, prevPlan.data, prevView.gameObject, prevView.RotorPivot, null, Options(prevPlan));
                    else
                        AssembleReference();
                }
                catch (Exception e2)
                {
                    failure += " Rolling back ALSO failed: " + e2.Message;
                    Debug.LogException(e2, this);
                }
                view = prevView; currentPlan = prevPlan; current = prevConfig; modularActive = prevModular;
                return false;
            }
        }

        bool RevertToReference(out string why, bool raise = true)
        {
            why = null;
            try
            {
                AssembleReference();
                if (view != null) { view.gameObject.SetActive(false); Destroy(view.gameObject); }
                if (referenceHull != null) referenceHull.SetActive(true);
                view = null; currentPlan = null; current = ShipConfiguration.Long(); modularActive = false;
                if (raise) Raise();
                return true;
            }
            catch (Exception e) { why = e.Message; Debug.LogException(e, this); return false; }
        }

        void AssembleReference()
        {
            var o = SteamerBootstrap.BuildOptions.Standard;
            o.cloneHands = false; o.hands = CrewAboard(); o.refit = true;
            if (referenceHull != null) referenceHull.SetActive(true);
            SteamerBootstrap.Assemble(gameObject, reference, referenceHull, referenceWheel, null, o);
        }

        SteamerBootstrap.BuildOptions Options(ShipyardPlan plan) => new SteamerBootstrap.BuildOptions
        {
            holdCells = plan.capacity.holdCells,
            hands = CrewAboard(),
            cloneHands = false,
            deckLoad = plan.deckLoad,
            refit = true,
            fittedGuns = plan.fittedGuns,
        };

        // ---- the live snapshot ----------------------------------------------

        int CrewAboard()
        {
            int n = 0;
            foreach (var c in GetComponentsInChildren<SeaSick.Crew.CrewAgent>(false)) if (c != null) n++;
            return n;
        }

        /// What she carries now, for the planner's retention checks.
        public LiveShipSnapshot Snapshot()
        {
            var s = new LiveShipSnapshot { config = current.Clone(), crewAboard = CrewAboard(), hasChimney = true };
            var voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            if (voyage != null)
            {
                s.totalHeld = voyage.TotalHeld;
                foreach (var kv in voyage.HeldStores) if (kv.Value > 0) s.kindsOnDeck++;
            }
            return s;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// Thrown only by ShipyardService's test fault hook (TestFaultStage).
    public class ShipyardTestFaultException : Exception
    {
        public readonly string stage;
        public ShipyardTestFaultException(string stage)
            : base("TEST FAULT injected at refit stage '" + stage + "' (ShipyardService.TestFaultStage)") { this.stage = stage; }
    }
#endif
}
