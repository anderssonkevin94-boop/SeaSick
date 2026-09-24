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

        /// A copy of what she is built from. Mutating it changes nothing.
        public ShipConfiguration Current => current.Clone();
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
        }

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

        /// Whether a refit may be applied right now, and if not, why (a
        /// sentence for the player).
        public bool CanRefitNow(out string reason)
        {
            reason = "";
            if (reference == null) { reason = "This ship has no hull form to refit."; return false; }
            if (!Library.Usable) { reason = "The ship module data could not be loaded."; return false; }
            if (SeaSick.Save.SaveGame.Restoring) { reason = "A save is still loading."; return false; }
            var rb = GetComponent<Rigidbody>();
            if (rb != null && rb.linearVelocity.magnitude > AtRestSpeed)
            { reason = "She is under way. Bring her to rest first."; return false; }
            var anchor = GetComponent<AnchorController>();
            if (anchor != null)
            {
                var st = anchor.CurrentState;
                bool moored = anchor.AtHomeDock || st == AnchorController.State.Anchored || st == AnchorController.State.Ashore;
                if (!moored) { reason = "She must be anchored or alongside to be refitted."; return false; }
            }
            if (SeaSick.Combat.RaidParty.Active != null) { reason = "Not during a raid."; return false; }
            var lockOn = GetComponent<SeaSick.Combat.CombatLock>();
            if (lockOn != null && lockOn.Locked != null) { reason = "Not while she is in a fight."; return false; }
            foreach (var o in SeaSick.World.Outpost.All)
                if (o != null && o.Ledger != null && o.Ledger.AnyTransferPending())
                { reason = "Cargo is being carried between the ship and the stores. Wait for the hands to finish."; return false; }
            foreach (var c in FindObjectsByType<SeaSick.Crew.CrewAgent>(FindObjectsSortMode.None))
                if (c != null && c.HomeShip == transform && !c.IsAboard)
                { reason = $"{c.DisplayName} is ashore. Call the hands back aboard first."; return false; }
            return true;
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
            var prevPlan = currentPlan;
            bool prevModular = modularActive;
            if (!Rebuild(v.plan, out string fail))
            {
                res.issues.Add(new Rejection { code = ShipyardCodes.ApplyFailed, partId = "", message = fail });
                res.configuration = Current;
                return res;
            }
            if (!Persist(out string saveWhy))
            {
                // Swap back: the previous build, through the same path.
                string back = null;
                bool restored = prevModular && prevPlan != null ? Rebuild(prevPlan, out back) : RevertToReference(out back, false);
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

        bool Persist(out string why)
        {
            why = null;
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
                var newView = fresh.AddComponent<ModularShipView>();
                newView.Build(plan.assembly);
                if (newView.RotorPivot == null) throw new InvalidOperationException("the assembly drew no wheel");

                SteamerBootstrap.Assemble(gameObject, plan.data, fresh, newView.RotorPivot, null, Options(plan));

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
}
