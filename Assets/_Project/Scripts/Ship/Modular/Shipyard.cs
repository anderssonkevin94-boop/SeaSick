using System;
using System.Collections.Generic;
using System.Globalization;
using SeaSick.Steamer;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    // ---------------------------------------------------------------------
    // The prototype shipyard, PURE C# half (headless-testable): the policy
    // of what the prototype offers, the measurement of an assembled hull,
    // the physics hull it implies, its authored capacity and mass, and
    // the retention checks that refuse a refit which would lose something.
    // Nothing here touches a GameObject, the live ship, resources or the
    // save. `ShipyardService` (Runtime/) is the MonoBehaviour that feeds it
    // a snapshot of the live ship and applies a confirmed plan.
    // See docs/SHIPYARD-API.md.
    // ---------------------------------------------------------------------

    /// Stable rejection codes added by the shipyard on top of ShipAssembler's.
    public static class ShipyardCodes
    {
        public const string NotInPrototype = "NOT_IN_PROTOTYPE";
        public const string WheelRequired = "WHEEL_REQUIRED";
        public const string CargoWouldNotFit = "CARGO_WOULD_NOT_FIT";
        public const string CrewWouldNotFit = "CREW_WOULD_NOT_FIT";
        public const string EquipmentWouldBeLost = "EQUIPMENT_WOULD_BE_LOST";
        public const string NoReferenceHull = "NO_REFERENCE_HULL";
        public const string CannotRefitNow = "CANNOT_REFIT_NOW";
        public const string ApplyFailed = "APPLY_FAILED";
        public const string StaleDraft = "STALE_DRAFT";
        public const string SaveFailed = "SAVE_FAILED";
        public const string Overloaded = "OVERLOADED";
        public const string NoHydrostatics = "NO_HYDROSTATICS";
        /// A hull section carries no authored `capacity` block (A4).
        public const string NoCapacity = "NO_CAPACITY";
        /// A section (or a fitted module) has no `lightship` mass, so the
        /// sailing model cannot be weighed from module data.
        public const string NoMassData = "NO_MASS_DATA";
        /// The SAILING MODEL (reshaped reference hull, weighed with the module
        /// lightship sum + load) would float outside its keel..deck range.
        /// Never clamped.
        public const string SimOutOfRange = "SIM_OUT_OF_RANGE";
        /// Fitted guns need more hands at the guns than the draft has berths
        /// for (2026-09-25: guns are explicit equipment; nothing strikes them
        /// automatically any more -- see EquipmentWouldBeLost).
        public const string GunsNeedCrew = "GUNS_NEED_CREW";
        /// ApplyRefit would need to take a module from the dry dock that is
        /// not there (fewer in stock than the draft asks for).
        public const string NotInDryDock = "NOT_IN_DRY_DOCK";
        /// ApplyRefit would add to or take from the dry dock, but she is not
        /// at her home berth (AnchorController.AtHomeDock).
        public const string DryDockNotHere = "DRY_DOCK_NOT_HERE";
    }

    /// What the prototype shipyard offers (D5): W1-r2 V3 stern and bow,
    /// 0-3 V3 middles, the M1 timber or reinforced rotor on the M1 carrier,
    /// and the V3 chimney on the stern's chimney socket. Everything else is
    /// refused with NOT_IN_PROTOTYPE, IN ADDITION to every milestone-1 rule.
    public static class ShipyardPolicy
    {
        static readonly string[] Sterns = { ShipConfiguration.V3Stern };
        static readonly string[] Middles = { ShipConfiguration.V3Middle };
        static readonly string[] Bows = { ShipConfiguration.V3Bow };
        static readonly string[] Rotors = { ShipConfiguration.TimberRotor, ShipConfiguration.ReinforcedRotor };
        static readonly string[] Carriers = { ShipConfiguration.M1Carrier };
        static readonly string[] Fittings = { ShipConfiguration.V3Chimney };
        static readonly string[] Equipment = { ShipConfiguration.EquipmentCannon };
        static readonly string[] None = new string[0];

        /// The module ids the UI may offer for one kind (ModuleKind.*).
        /// UpperDeck is still empty in the prototype; Equipment offers the
        /// one deck cannon (2026-09-25).
        public static IReadOnlyList<string> AllowedModuleIds(string kind)
        {
            switch (kind)
            {
                case ModuleKind.Stern: return Sterns;
                case ModuleKind.Middle: return Middles;
                case ModuleKind.Bow: return Bows;
                case ModuleKind.Rotor: return Rotors;
                case ModuleKind.Carrier: return Carriers;
                case ModuleKind.Fitting: return Fittings;
                case ModuleKind.Equipment: return Equipment;
                default: return None;
            }
        }

        /// Prototype-only reasons. Does not repeat the assembler's rules.
        public static List<Rejection> Check(ShipConfiguration cfg, ModuleLibrary lib)
        {
            var r = new List<Rejection>();
            if (cfg == null) return r;
            Only(r, lib, "stern", cfg.sternId, Sterns);
            for (int i = 0; i < (cfg.middleIds?.Count ?? 0); i++)
                Only(r, lib, ShipAssembler.MiddleKey(i), cfg.middleIds[i], Middles);
            Only(r, lib, "bow", cfg.bowId, Bows);
            if (string.IsNullOrEmpty(cfg.rotorId))
                r.Add(new Rejection { code = ShipyardCodes.WheelRequired, partId = "rotor",
                    message = "A paddle steamer needs her wheel; choose a timber or reinforced M1 wheel." });
            else Only(r, lib, "rotor", cfg.rotorId, Rotors);
            if (!string.IsNullOrEmpty(cfg.carrierId)) Only(r, lib, "carrier", cfg.carrierId, Carriers);
            foreach (var f in cfg.fittings ?? new List<FittingChoice>())
            {
                if (f == null) continue;
                if (lib != null && lib.TryGet(f.moduleId, out var fd) && fd.kind == ModuleKind.UpperDeck)
                {
                    r.Add(Reject(f.socketId, "Raised decks are not part of the prototype shipyard yet."));
                    continue;
                }
                if (Array.IndexOf(Fittings, f.moduleId) < 0 || f.socketId != ShipConfiguration.ChimneySocket)
                    r.Add(Reject(f.socketId, $"Only the chimney, on the stern's chimney socket, can be fitted in the prototype shipyard ('{f.moduleId}' on '{f.socketId}')."));
            }
            // Equipment (2026-09-25): the one deck cannon is offered; the
            // module rules (slot class, clearance, passages, overlap) and the
            // retention/crew checks below do the rest.
            foreach (var e in cfg.equipment ?? new List<EquipmentChoice>())
                Only(r, lib, e?.slotId ?? "", e?.moduleId, Equipment);
            return r;
        }

        static void Only(List<Rejection> r, ModuleLibrary lib, string part, string id, string[] allowed)
        {
            if (string.IsNullOrEmpty(id) || Array.IndexOf(allowed, id) >= 0) return;
            ModuleDef d = null;
            lib?.TryGet(id, out d);
            string why;
            if (d == null) return; // UNKNOWN_MODULE comes from the assembler
            if (d.kind == ModuleKind.Rotor && d.rotor != null && d.rotor.mount != "M1")
                why = $"{ModuleLibrary.Name(d)} is not part of the prototype shipyard: only the M1 timber and reinforced wheels are offered.";
            else if (ModuleKind.IsHull(d.kind) && d.family != "W1-r2")
                why = $"{ModuleLibrary.Name(d)} is not part of the prototype shipyard: only the W1-r2 low-deck hull is offered (no wider hulls yet).";
            else if (d.status == ModuleStatus.Placeholder || d.status == ModuleStatus.IncompatibleReference)
                why = $"{ModuleLibrary.Name(d)} is a {d.status} part and cannot be built.";
            else
                why = $"{ModuleLibrary.Name(d)} is not part of the prototype shipyard.";
            r.Add(new Rejection { code = ShipyardCodes.NotInPrototype, partId = part, message = why });
        }

        static Rejection Reject(string part, string message) =>
            new Rejection { code = ShipyardCodes.NotInPrototype, partId = part ?? "", message = message };
    }

    /// Hull dimensions MEASURED from an assembly, in authoring units. The
    /// reshape factors are these divided by the same measurement of the
    /// reference ship (`ShipConfiguration.Long()`), so the reference is
    /// exactly (1, 1, 1) by construction (x / x == 1 for any finite x != 0).
    [Serializable]
    public struct HullMeasure
    {
        /// Stern aft end to the bow's stem at the height datum, decorative
        /// prow EXCLUDED (bow socket role "hull.stem"; falls back to the
        /// "hull.tip" if a bow has no stem socket).
        public float waterlineLengthU;
        /// 2 x the widest shell half-breadth among the hull sections' join
        /// profiles (rails excluded).
        public float beamU;
        /// Keel to main deck, the deepest among the hull sections' profiles.
        public float depthU;
        public bool stemFound;

        public static bool TryMeasure(AssemblyResult r, ModuleLibrary lib, out HullMeasure m)
        {
            m = default;
            if (r == null || !r.ok || lib == null) return false;
            float aft = float.MaxValue, stem = float.MinValue;
            foreach (var p in r.placed)
            {
                if (!ModuleKind.IsHull(p.kind) || !lib.TryGet(p.moduleId, out var d)) continue;
                if (d.kind == ModuleKind.Stern)
                {
                    var o = ModuleLibrary.FindSocket(d, SocketRole.HullOrigin);
                    aft = Mathf.Min(aft, p.positionU.x + (o != null ? o.posU.x : 0f));
                }
                if (d.kind == ModuleKind.Bow)
                {
                    var s = ModuleLibrary.FindSocket(d, SocketRole.HullStem);
                    m.stemFound = s != null;
                    if (s == null) s = ModuleLibrary.FindSocket(d, SocketRole.HullTip);
                    stem = Mathf.Max(stem, p.positionU.x + (s != null ? s.posU.x : d.lengthU));
                }
                if (d.sockets != null)
                    foreach (var s in d.sockets)
                    {
                        if (s == null || (s.role != SocketRole.HullAft && s.role != SocketRole.HullFwd)) continue;
                        var prof = lib.FindProfile(s.standard);
                        if (prof == null) continue;
                        m.beamU = Mathf.Max(m.beamU, 2f * prof.halfBeamU);
                        m.depthU = Mathf.Max(m.depthU, prof.deckZU - prof.keelZU);
                    }
            }
            if (aft == float.MaxValue || stem == float.MinValue) return false;
            m.waterlineLengthU = stem - aft;
            return m.waterlineLengthU > 0f && m.beamU > 0f && m.depthU > 0f;
        }
    }

    /// Room aboard: the SUM of the installed hull sections' AUTHORED,
    /// provisional `capacity` blocks (A4). Seeded so Long reproduces today's
    /// 16 / 8 / 6 exactly; nothing here is derived from geometry.
    [Serializable]
    public struct ShipCapacity
    {
        /// Hold cells (VoyageManager.SetHoldCapacity): sum of `holdCells`.
        public int holdCells;
        /// Crew berths (deck stations): sum of `berths`.
        public int crewStations;
        /// Fixed deck/equipment slots in the assembly (deck.slot sockets).
        /// Nothing may be fitted to them by hand in the prototype yet.
        public int equipmentSlots;
        /// Authored gun slots that are usable: listed in a section's
        /// `capacity.gunSlots`, exist, take a deck gun, have clearance inside
        /// the section and stand clear of every crew passage.
        public int gunSlots;
        /// Guns she CARRIES (both sides): the hull form's gun pairs that have
        /// a usable port+starboard slot pair in their section, capped by
        /// berths (CrewPerGun hands per gun). Extra slots grant no guns.
        public int guns;

        public override string ToString() =>
            $"hold {holdCells}, crew {crewStations}, slots {equipmentSlots}, gun slots {gunSlots}, guns {guns}";
    }

    /// One installed hull section's authored capacity and where it is.
    [Serializable]
    public class SectionCapacity
    {
        public string sectionKey, moduleId;
        /// Ship-frame z span, m. The ship's ends are open-ended (+-Infinity).
        public float aftZ, fwdZ;
        public int holdCells, berths;
        /// Usable gun-slot ids (validated), and why any listed one is not.
        public List<string> gunSlotIds = new List<string>();
        public List<string> gunSlotProblems = new List<string>();
        /// Ship-frame z of each usable port+starboard slot pair.
        public List<float> gunPairZs = new List<float>();
        public bool authored;
    }

    /// One thing positioned on the hull that a refit must keep a place for.
    [Serializable]
    public class PositionedItem
    {
        public string id;
        public string label;
        public Vector3 position;
    }

    /// One gun standing on the assembled hull (2026-09-25). Derived from the
    /// assembly's placed Equipment modules -- never from the hull form any
    /// more; the slot is where she stands. Ship frame (the plan's viewOffset
    /// already folded into positionM.z).
    [Serializable]
    public class FittedGun
    {
        /// Qualified equipment slot, e.g. "middle[0]/DeckSlot_1_1".
        public string slotId;
        public string moduleId;
        public Vector3 positionM;
        /// "port" or "starboard" (game +X = starboard).
        public string side;
        /// This one gun's provisional weight and crew (EquipmentSpec.massKg
        /// / .crew, falling back to WeightModel defaults when unauthored).
        public float massKg;
        public int crew;
    }

    /// What things weigh (A2). The service fills it from the game's existing
    /// mass accounting (`ShipLoad.CargoUnitKg`, `ShipLoad.CrewKg`); the gun
    /// weight is the lightest calibre in `ShipLoad` (a swivel) because the
    /// steamer's guns have no calibre of their own -- PROVISIONAL.
    [Serializable]
    public struct WeightModel
    {
        public float cargoUnitKg, crewKg, gunKg;
        public static WeightModel Default => new WeightModel { cargoUnitKg = 500f, crewKg = 90f, gunKg = 500f };
        /// Hands a gun needs: `CannonBattery` works each gun with ONE named
        /// hand (`CrewRoster.GunCrew(index)`), so 1 -- from the code.
        public const int CrewPerGun = 1;
    }

    /// What the live ship carries, as the planner needs it. The service
    /// fills this from the scene; tests build it by hand.
    [Serializable]
    public class LiveShipSnapshot
    {
        public ShipConfiguration config;
        public int totalHeld;
        /// Distinct kinds on deck (ShipHold draws one pile per kind).
        public int kindsOnDeck;
        public int crewAboard;
        public bool hasChimney = true;
        public WeightModel weights = WeightModel.Default;
    }

    /// Everything a validated configuration implies. Read-only for the UI;
    /// `data` and the placement numbers are what ApplyRefit uses.
    public class ShipyardPlan
    {
        public ShipConfiguration config;
        public AssemblyResult assembly;
        public HullMeasure measure;
        /// Reshape factors against the reference ship (Long = 1, 1, 1).
        public float sLength = 1f, sBeam = 1f, sDepth = 1f;
        /// How far the stern (and its wheel, well, rudder, helm) moved along
        /// the ship, m (+ forward). Half the change in drawn waterline length.
        public float sternShiftM;
        /// The physics hull: the reference HullFormData reshaped, stern
        /// fittings pinned to the drawn stern. At the reference: the SAME
        /// object values, bit for bit.
        public HullFormData data;
        /// Local position of the ModularShipView under the ship root: the
        /// assembly's datum (authoring Z = 0) at the waterline, its drawn axle
        /// on the physics axle in z.
        public Vector3 viewOffset;
        /// Funnel extent along the ship, ship frame (for the deck load).
        public float funnelAftZ, funnelFwdZ;
        public ShipCapacity capacity;
        /// Per installed hull section, aft to fore: its authored capacity.
        public List<SectionCapacity> sections = new List<SectionCapacity>();
        /// Hull sections without a capacity block / without lightship data
        /// (null = none missing). Blocking (NO_CAPACITY / NO_MASS_DATA).
        public string capacityMissing, massMissing;
        /// Every gun standing on this hull (2026-09-25: explicit equipment,
        /// not hull-form sockets). `data.gunSockets` is set from the
        /// starboard-side entries here, in ship frame, so CannonBattery.Fit
        /// (which mirrors starboard to port) draws and works them unchanged.
        public List<FittedGun> fittedGuns = new List<FittedGun>();
        /// Sum of the fitted guns' own provisional weight/crew (the weight
        /// model's per-item numbers, not a flat count x constant any more).
        public float gunsWeightKg;
        public int gunsCrewNeeded;
        public DeckLoadPlan deckLoad;
        /// Upright hydrostatics of the ASSEMBLED hull, from Astra's per-module
        /// station tables (H2). The report's displacement and draft come from
        /// here; the live physics still runs on `data` (H4).
        public AssemblyHydrostatics hydro;
        /// Sum of the installed modules' provisional lightship masses (H5),
        /// kg. THE mass: `data.massKg` is set to it, so the sailing model
        /// weighs her with it too (one mass source).
        public float lightshipKg;
        /// The design load line, U below the deck limit (the reference ship's
        /// full load -- 16 cargo, 8 hands, her guns -- just reaches it; the
        /// same margin on every hull). PROVISIONAL. And her displacement
        /// there, kg.
        public float loadLineMarginU, loadDisplacementKg;

        /// Cargo weight she can take with `crew` aboard, kg (A2): displacement
        /// at the load line - lightship - crew - the fitted guns' own weight
        /// (their module mass, summed; replaces the old flat guns x gunKg).
        public float WeightAllowanceKg(int crew, WeightModel w) =>
            loadDisplacementKg - lightshipKg - crew * w.crewKg - gunsWeightKg;
    }

    /// One hull section of a draft: what is in it and whether it can go.
    [Serializable]
    public class SectionOccupancy
    {
        /// "stern", "middle[0]", ..., "bow".
        public string sectionKey;
        public string moduleId;
        /// Ship-frame z span, m (aft, fwd).
        public float aftZ, fwdZ;
        /// Hold cells this section contributes (its AUTHORED `capacity`) and
        /// the cargo units attributed to it (the load spread pro rata).
        public int holdCells, cargoCells;
        /// Authored berths of this section, and hands standing on it.
        public int berths, crew;
        /// Usable authored gun slots of this section, and guns standing in it.
        public int gunSlots, guns;
        /// Positioned things standing in it: "gun pair 2", "funnel", "deck load pile 1".
        public List<string> equipment = new List<string>();
        public bool canRemove;
        public string reason = "";
    }

    /// Validate()'s answer.
    public class ShipyardValidation
    {
        public bool ok;
        public readonly List<Rejection> issues = new List<Rejection>();
        public AssemblyResult assembly;
        /// Drawn hull at the waterline, prow excluded, metres.
        public float hullLengthM;
        /// Stern aft interface to the prow tip, metres.
        public float overallLengthM;
        public int middleSections;
        public string rotorId;
        public ShipCapacity capacityCurrent;
        public ShipCapacity capacityDraft;
        /// Cargo weight room with the hands now aboard, kg (A2).
        public float weightAllowanceCurrentKg, weightAllowanceDraftKg;
        /// What the cargo aboard weighs now, kg.
        public float cargoKg;
        /// Total mass (lightship + cargo + crew + guns) and the draft above
        /// the keel it floats at, from the module station tables (NaN when
        /// it would be above the deck line).
        public float totalMassCurrentKg, totalMassDraftKg;
        public float tableDraftCurrentM = float.NaN, tableDraftDraftM = float.NaN;
        /// The same masses floated on the SAILING MODEL's hull form (static
        /// strip-sum solve; see ShipyardPlanner.SimStaticDraft).
        public float simDraftCurrentM = float.NaN, simDraftDraftM = float.NaN;
        /// Per hull section of the DRAFT: what occupies it and whether that
        /// section may be taken out (A-UI `removalBlocker`).
        public List<SectionOccupancy> sections = new List<SectionOccupancy>();
        /// Internal: what ApplyRefit builds. Null unless ok.
        public ShipyardPlan plan;
        /// The draft's plan even when it is refused (null if it does not
        /// assemble), and the live ship's plan -- for the report's figures.
        public ShipyardPlan draftPlan, currentPlan;

        public bool HasCode(string code)
        {
            foreach (var i in issues) if (i.code == code) return true;
            return false;
        }

        public string Summary()
        {
            if (ok) return $"OK: {hullLengthM:0.00} m hull, {middleSections} bay(s), {rotorId}, {capacityCurrent} -> {capacityDraft}";
            var s = new List<string>();
            foreach (var i in issues) s.Add(i.ToString());
            return string.Join("\n", s);
        }
    }

    /// Where the deck load's rows go (SteamerBootstrap.FitDeckLoad's layout,
    /// pure so the retention check and the bootstrap use the SAME numbers).
    [Serializable]
    public class DeckLoadPlan
    {
        public const float PileLength = 1.7f, Across = 1.5f, Clear = 0.15f;
        public Vector3[] rows;
        public int abreast;

        /// Row 0 between the helm and the funnel, rows 1-2 forward of it;
        /// kinds abreast inside the crew's stations. Mirrors the 2026-09-24
        /// layout exactly (see SteamerBootstrap.FitDeckLoad for the why).
        public static DeckLoadPlan Lay(HullFormData data, float funnelAft, float funnelFwd)
        {
            float aftLimit = data.helm.z + 0.45f;
            float room = (funnelAft - Clear) - aftLimit;
            float z0 = room >= PileLength
                ? (funnelAft - Clear + aftLimit) * 0.5f
                : funnelAft - Clear - PileLength * 0.5f;
            float z1 = funnelFwd + Clear + PileLength * 0.5f;
            int si = ShipyardPlanner.NearestStation(data, z0);
            float half = data.HalfBreadthAt(si, data.stations[si].deckY);
            int abreast = Mathf.Clamp(Mathf.FloorToInt(2f * (half - 0.7f) / Across), 1, 3);
            return new DeckLoadPlan
            {
                abreast = abreast,
                rows = new[]
                {
                    new Vector3(0f, ShipyardPlanner.DeckYAt(data, z0) + 0.01f, z0),
                    new Vector3(0f, ShipyardPlanner.DeckYAt(data, z1) + 0.01f, z1),
                    new Vector3(0f, ShipyardPlanner.DeckYAt(data, z1 + PileLength + 0.1f) + 0.01f, z1 + PileLength + 0.1f),
                },
            };
        }

        /// ShipHold.SlotLocal's layout for pile slot n.
        public Vector3 Slot(int n)
        {
            int per = Mathf.Max(1, abreast);
            int row = n / per, col = n % per;
            Vector3 c;
            if (row < rows.Length) c = rows[row];
            else
            {
                Vector3 last = rows[rows.Length - 1];
                Vector3 step = rows.Length > 1 ? last - rows[rows.Length - 2] : new Vector3(0f, 0f, -1f);
                c = last + step * (row - rows.Length + 1);
            }
            c.x += (col - (per - 1) * 0.5f) * Across;
            return c;
        }
    }

    /// Draft configuration + reference hull + live snapshot -> a plan, or
    /// the reasons it cannot be built. Pure; touches nothing.
    public static class ShipyardPlanner
    {
        /// Today's numbers on the reference ship (SteamerBootstrap).
        public const int ReferenceHoldCells = 16;
        public const int ReferenceCrewStations = 8;

        public static ShipyardValidation Validate(ShipConfiguration draft, ModuleLibrary lib,
            HullFormData reference, LiveShipSnapshot live) => Validate(draft, lib, reference, live, true);

        public static ShipyardValidation Validate(ShipConfiguration draft, ModuleLibrary lib,
            HullFormData reference, LiveShipSnapshot live, bool occupancy)
        {
            var v = new ShipyardValidation();
            if (reference == null)
            {
                v.issues.Add(new Rejection { code = ShipyardCodes.NoReferenceHull, partId = "",
                    message = "The ship's hull form could not be loaded, so no refit can be planned." });
                return v;
            }
            var w = live != null ? live.weights : WeightModel.Default;
            var refPlan = PlanFor(ShipConfiguration.Long(), lib, reference, null, w, out _);
            var draftPlan = PlanFor(draft, lib, reference, refPlan, w, out var asm);
            v.assembly = asm;
            v.issues.AddRange(ShipyardPolicy.Check(draft, lib));
            // A hull change whose draft still lists a gun on a slot that no
            // longer exists fails assembly with the generic
            // EQUIPMENT_SLOT_UNKNOWN; translate that one case to the
            // friendlier, dry-dock-flavoured EQUIPMENT_WOULD_BE_LOST (D5/D6
            // "never discard, readable refusal") so the player is told to
            // take the gun off first rather than shown a raw slot-id error.
            if (asm != null)
                foreach (var rj in asm.rejections)
                    v.issues.Add(TranslateVanishedGunSlot(rj, draft, lib) ?? rj);
            if (asm != null && asm.ok)
            {
                v.overallLengthM = asm.overallLengthM;
                v.middleSections = draft.middleIds?.Count ?? 0;
                v.rotorId = draft.rotorId;
            }
            if (draftPlan != null)
            {
                v.hullLengthM = draftPlan.measure.waterlineLengthU * asm.metresPerUnit;
                v.capacityDraft = draftPlan.capacity;
            }
            ShipyardPlan curPlan = null;
            if (live?.config != null && refPlan != null)
            {
                curPlan = live.config.ValueEquals(ShipConfiguration.Long())
                    ? refPlan : PlanFor(live.config, lib, reference, refPlan, w, out _);
                if (curPlan != null)
                {
                    v.capacityCurrent = curPlan.capacity;
                    v.weightAllowanceCurrentKg = curPlan.WeightAllowanceKg(live.crewAboard, w);
                }
            }
            if (live != null)
            {
                v.cargoKg = live.totalHeld * w.cargoUnitKg;
                if (draftPlan != null) v.weightAllowanceDraftKg = draftPlan.WeightAllowanceKg(live.crewAboard, w);
            }
            if (refPlan == null && v.issues.Count == 0)
                v.issues.Add(new Rejection { code = ShipyardCodes.NoReferenceHull, partId = "",
                    message = "The reference ship (stern, one bay, bow) could not be assembled from the module data." });

            if (draftPlan != null && draftPlan.capacityMissing != null)
                v.issues.Add(new Rejection { code = ShipyardCodes.NoCapacity, partId = "",
                    message = "No capacity is written down for " + draftPlan.capacityMissing + ", so her hold, berths and guns cannot be worked out." });
            if (draftPlan != null && draftPlan.massMissing != null)
                v.issues.Add(new Rejection { code = ShipyardCodes.NoMassData, partId = "",
                    message = "No lightship mass is written down for " + draftPlan.massMissing + ", so she cannot be weighed." });

            if (draftPlan != null && live != null)
            {
                CheckRetention(v.issues, live, curPlan, draftPlan);
                // **Refused, not struck** (Kevin + Astra, 2026-09-24, kept
                // 2026-09-25 now that guns are explicit equipment: "Reject
                // changes that cannot safely retain existing equipment";
                // "never silently discard anything"). A hull change whose
                // draft still references a gun on a slot that no longer
                // exists is EQUIPMENT_WOULD_BE_LOST (translated from the
                // assembler's EQUIPMENT_SLOT_UNKNOWN, above); a gun the draft
                // keeps but cannot crew is GUNS_NEED_CREW, below. Nothing
                // strikes a gun silently any more -- see the dry dock (D5).
                if (draftPlan.gunsCrewNeeded > draftPlan.capacity.crewStations)
                    v.issues.Add(new Rejection { code = ShipyardCodes.GunsNeedCrew, partId = "guns",
                        message = $"{draftPlan.capacity.guns} guns need {draftPlan.gunsCrewNeeded} hands at the guns and this ship has berths for {draftPlan.capacity.crewStations}. " +
                                  $"Take {draftPlan.gunsCrewNeeded - draftPlan.capacity.crewStations} guns off to the dry dock first." });
                float total = live.totalHeld * w.cargoUnitKg + live.crewAboard * w.crewKg + draftPlan.gunsWeightKg;
                v.totalMassDraftKg = draftPlan.lightshipKg + total;
                if (!draftPlan.hydro.Ok)
                    v.issues.Add(new Rejection { code = ShipyardCodes.NoHydrostatics, partId = "",
                        message = "This hull's flotation tables are missing (" + draftPlan.hydro.missing + "), so its draft cannot be worked out." });
                else if (!TableDraft(draftPlan, v.totalMassDraftKg, out v.tableDraftDraftM))
                    v.issues.Add(new Rejection { code = ShipyardCodes.Overloaded, partId = "",
                        message = $"At {v.totalMassDraftKg / 1000f:0.0} t she would float above her deck line and flood. Lighten her first." });
                // The sailing model, weighed with the SAME mass: outside its
                // keel..deck range is a refusal, never a clamp.
                if (!SimStaticDraft(draftPlan.data, v.totalMassDraftKg, out v.simDraftDraftM) || !(v.simDraftDraftM > 0f))
                    v.issues.Add(new Rejection { code = ShipyardCodes.SimOutOfRange, partId = "sim",
                        message = $"At {v.totalMassDraftKg / 1000f:0.0} t her sailing model would float outside its keel-to-deck range. Lighten her first." });
                if (curPlan != null && curPlan.hydro.Ok)
                {
                    v.totalMassCurrentKg = curPlan.lightshipKg + live.totalHeld * w.cargoUnitKg + live.crewAboard * w.crewKg + curPlan.gunsWeightKg;
                    TableDraft(curPlan, v.totalMassCurrentKg, out v.tableDraftCurrentM);
                    SimStaticDraft(curPlan.data, v.totalMassCurrentKg, out v.simDraftCurrentM);
                }
                if (occupancy) v.sections = Occupancy(draftPlan, live, lib, reference, refPlan);
            }

            v.ok = v.issues.Count == 0 && draftPlan != null;
            if (v.ok) v.plan = draftPlan;
            v.draftPlan = draftPlan;
            v.currentPlan = curPlan;
            return v;
        }

        /// `rejection` is EQUIPMENT_SLOT_UNKNOWN and its partId names an
        /// equipment slot the draft's OWN config still lists a deck-gun on ->
        /// a friendlier EQUIPMENT_WOULD_BE_LOST (null = pass the rejection
        /// through unchanged).
        static Rejection TranslateVanishedGunSlot(Rejection rejection, ShipConfiguration draft, ModuleLibrary lib)
        {
            if (rejection == null || rejection.code != "EQUIPMENT_SLOT_UNKNOWN" || draft?.equipment == null) return null;
            foreach (var e in draft.equipment)
            {
                if (e == null || e.slotId != rejection.partId) continue;
                string name = e.moduleId;
                if (lib != null && lib.TryGet(e.moduleId, out var d) && d.equipment?.equipmentClass == "equipment.deck-gun")
                    name = ModuleLibrary.Name(d);
                return new Rejection { code = ShipyardCodes.EquipmentWouldBeLost, partId = e.slotId,
                    message = $"{name} at {e.slotId} would have no place on this ship (its slot is gone); take it off to the dry dock first." };
            }
            return null;
        }

        /// The plan for one configuration. `refPlan` null = this IS the
        /// reference (factors 1, shift 0). Null when it does not assemble.
        public static ShipyardPlan PlanFor(ShipConfiguration cfg, ModuleLibrary lib, HullFormData reference,
            ShipyardPlan refPlan, out AssemblyResult asm) => PlanFor(cfg, lib, reference, refPlan, WeightModel.Default, out asm);

        public static ShipyardPlan PlanFor(ShipConfiguration cfg, ModuleLibrary lib, HullFormData reference,
            ShipyardPlan refPlan, WeightModel w, out AssemblyResult asm)
        {
            if (cfg == null) { asm = ShipAssembler.Assemble(null, lib); return null; }
            asm = ShipAssembler.Assemble(cfg, lib);
            if (!asm.ok || reference == null) return null;
            if (!HullMeasure.TryMeasure(asm, lib, out var m)) return null;
            float k = asm.metresPerUnit;
            var p = new ShipyardPlan { config = cfg.Clone(), assembly = asm, measure = m };
            if (refPlan != null)
            {
                p.sLength = m.waterlineLengthU / refPlan.measure.waterlineLengthU;
                p.sBeam = m.beamU / refPlan.measure.beamU;
                p.sDepth = m.depthU / refPlan.measure.depthU;
                p.sternShiftM = (refPlan.measure.waterlineLengthU - m.waterlineLengthU) * 0.5f * k;
            }
            p.data = reference.Reshaped(p.sLength, p.sBeam, p.sDepth);
            p.data.PinSternFittings(reference, p.sternShiftM);
            // The drawn axle sits on the physics axle along the ship; the
            // datum (authoring Z = 0) on the waterline.
            float axleLocalZ = asm.hasWheel ? asm.wheelAxleM.z : 0f;
            p.viewOffset = new Vector3(0f, 0f, p.data.wheelAxle.z - axleLocalZ);

            // The funnel, from the assembly (base-centre pivot, bounds in U).
            p.funnelAftZ = -0.4f; p.funnelFwdZ = 0.4f;
            var ch = asm.Find("fitting:" + ShipConfiguration.ChimneySocket);
            if (ch != null)
            {
                p.funnelAftZ = p.viewOffset.z + ch.positionM.z + ch.boundsMinU.x * k;
                p.funnelFwdZ = p.viewOffset.z + ch.positionM.z + ch.boundsMaxU.x * k;
            }
            p.deckLoad = DeckLoadPlan.Lay(p.data, p.funnelAftZ, p.funnelFwdZ);

            int slots = 0;
            foreach (var s in asm.slots) if (s.role == SocketRole.DeckSlot) slots++;

            // Capacity (A4): the installed sections' AUTHORED blocks, summed.
            p.sections = SectionCapacities(asm, lib, p.viewOffset.z, out p.capacityMissing);
            int hold = 0, berths = 0, gunSlots = 0;
            foreach (var sc in p.sections) { hold += sc.holdCells; berths += sc.berths; gunSlots += sc.gunSlotIds.Count; }

            // Guns (2026-09-25): explicit equipment, not hull-form sockets.
            // Every fitted equipment.deck-gun in the assembly is a gun,
            // standing exactly where its slot put it; the assembler already
            // refused anything that does not fit or overlaps a passage or
            // another item, so nothing here re-validates placement.
            p.fittedGuns = FittedGunsOf(asm, lib, p.viewOffset.z, w);
            foreach (var g in p.fittedGuns) { p.gunsWeightKg += g.massKg; p.gunsCrewNeeded += g.crew; }
            // `data.gunSockets` (starboard only) stays a courtesy copy for the
            // self-test's hull-form comparisons and `Reshaped`/`Scaled` -- the
            // live battery no longer reads it. SteamerBootstrap.Man is handed
            // `p.fittedGuns` itself (each gun with its own side and position)
            // and fits `CannonBattery` directly from that, unmirrored, so a
            // lone unpaired gun (a port gun sent to the dry dock, starboard
            // kept) still gets a correctly-counted, correctly-positioned
            // battery instead of a mirrored phantom.
            var star = new List<Vector3>();
            foreach (var g in p.fittedGuns) if (g.side == "starboard") star.Add(g.positionM);
            p.data.gunSockets = star.ToArray();
            p.capacity = new ShipCapacity
            {
                holdCells = hold, crewStations = berths, equipmentSlots = slots, gunSlots = gunSlots,
                guns = p.fittedGuns.Count,
            };

            // Weight (A2/H2/H5), from the module station tables.
            const float rho = HullFormData.SeaWaterDensity;
            p.hydro = AssemblyHydrostatics.For(asm, lib);
            // ONE mass source: the installed modules' lightship sum weighs her
            // in the report AND in the sailing model (HullFormBody reads
            // data.massKg for rb.mass, inertia, damping and stiffness). Her
            // geometry (volume, design draft) stays the reshaped form's, so her
            // static waterline in the sim is wherever that mass floats her.
            p.lightshipKg = ModuleLightshipKg(asm, lib, out p.massMissing);
            if (p.massMissing == null) p.data.massKg = p.lightshipKg;
            else p.lightshipKg = p.data.massKg;
            if (p.hydro.Ok)
            {
                if (refPlan == null)
                {
                    float fullLoad = p.lightshipKg + ReferenceHoldCells * w.cargoUnitKg + ReferenceCrewStations * w.crewKg + p.gunsWeightKg;
                    p.loadLineMarginU = p.hydro.SolveWaterline(fullLoad, rho, out float zLoad, out _) ? p.hydro.DeckZU - zLoad : 0f;
                }
                else p.loadLineMarginU = refPlan.loadLineMarginU;
                p.loadDisplacementKg = rho * p.hydro.VolumeM3At(p.hydro.DeckZU - p.loadLineMarginU);
            }
            else p.loadDisplacementKg = p.lightshipKg;
            return p;
        }

        /// Sum of every placed module's `lightship.massKg` (hull sections
        /// must carry one; wheel, carrier, chimney carry none today = 0).
        /// `missing` names the hull sections without one (null = none).
        public static float ModuleLightshipKg(AssemblyResult asm, ModuleLibrary lib, out string missing)
        {
            missing = null;
            float kg = 0f;
            foreach (var pm in asm.placed)
            {
                if (!lib.TryGet(pm.moduleId, out var d)) continue;
                if (d.lightship != null && d.lightship.massKg > 0f) kg += d.lightship.massKg;
                else if (ModuleKind.IsHull(d.kind)) missing = missing == null ? ModuleLibrary.Name(d) : missing + ", " + ModuleLibrary.Name(d);
            }
            return kg;
        }

        /// Every fitted equipment.deck-gun in the assembly, ship frame
        /// (`viewZ` = the plan's viewOffset.z). The assembler has already
        /// validated placement (slot class, clearance, passage, overlap);
        /// this just reads the result and each gun's own provisional
        /// weight/crew, falling back to the weight model's defaults for a
        /// module that does not author them.
        public static List<FittedGun> FittedGunsOf(AssemblyResult asm, ModuleLibrary lib, float viewZ, WeightModel w)
        {
            var list = new List<FittedGun>();
            if (asm == null) return list;
            foreach (var pm in asm.placed)
            {
                if (pm.kind != ModuleKind.Equipment || !lib.TryGet(pm.moduleId, out var d) || d.equipment == null) continue;
                if (d.equipment.equipmentClass != "equipment.deck-gun") continue;
                list.Add(new FittedGun
                {
                    slotId = pm.instanceKey.StartsWith("equipment:") ? pm.instanceKey.Substring("equipment:".Length) : pm.instanceKey,
                    moduleId = pm.moduleId,
                    positionM = new Vector3(pm.positionM.x, pm.positionM.y, viewZ + pm.positionM.z),
                    side = pm.positionM.x > 0f ? "starboard" : "port",
                    massKg = d.equipment.massKg != null ? d.equipment.massKg.value : w.gunKg,
                    crew = d.equipment.crew != null ? d.equipment.crew.value : WeightModel.CrewPerGun,
                });
            }
            return list;
        }

        /// The installed hull sections, aft to fore, with their authored
        /// capacity, ship-frame z span (the ends open-ended) and VALIDATED gun
        /// slots. A listed gun slot counts only if: it is one of the module's
        /// equipment slots on a deck.slot socket, it takes a deck gun, its
        /// clearance box is non-empty, inside the section (length, and the
        /// join profile's half-beam) and overlaps no crew passage of the
        /// assembled ship (strict, as ShipAssembler checks equipment).
        public static List<SectionCapacity> SectionCapacities(AssemblyResult asm, ModuleLibrary lib, float viewZ, out string missing)
        {
            missing = null;
            var list = new List<SectionCapacity>();
            float k = asm.metresPerUnit;
            const float Eps = 1e-3f;
            foreach (var pm in asm.placed)
            {
                if (!ModuleKind.IsHull(pm.kind) || !lib.TryGet(pm.moduleId, out var d)) continue;
                float len = d.lengthU;
                if (d.kind == ModuleKind.Bow)
                {
                    var stem = ModuleLibrary.FindSocket(d, SocketRole.HullStem);
                    if (stem != null) len = stem.posU.x;
                }
                var sc = new SectionCapacity { sectionKey = pm.instanceKey, moduleId = pm.moduleId,
                    aftZ = viewZ + pm.positionM.z, fwdZ = viewZ + pm.positionM.z + len * k };
                list.Add(sc);
                var c = d.capacity;
                sc.authored = c != null && c.holdCells != null && c.berths != null;
                if (!sc.authored) { missing = missing == null ? ModuleLibrary.Name(d) : missing + ", " + ModuleLibrary.Name(d); continue; }
                sc.holdCells = Mathf.Max(0, c.holdCells.value);
                sc.berths = Mathf.Max(0, c.berths.value);
                if (c.gunSlots?.ids == null) continue;

                float half = 0f;
                if (d.sockets != null)
                    foreach (var so in d.sockets)
                        if (so != null && (so.role == SocketRole.HullAft || so.role == SocketRole.HullFwd))
                        {
                            var prof = lib.FindProfile(so.standard);
                            if (prof != null) half = Mathf.Max(half, prof.halfBeamU);
                        }
                var port = new List<float>(); var star = new List<float>();
                foreach (var id in c.gunSlots.ids)
                {
                    string why = null;
                    EquipmentSlotDef es = null;
                    if (d.equipmentSlots != null) foreach (var e in d.equipmentSlots) if (e != null && e.id == id) es = e;
                    var so = es != null ? ModuleLibrary.FindSocketById(d, es.socketId) : null;
                    if (sc.gunSlotIds.Contains(id)) why = "listed twice";
                    else if (es == null || so == null) why = "no such equipment slot";
                    else if (so.role != SocketRole.DeckSlot) why = "not a fixed deck slot";
                    else if (es.classes == null || Array.IndexOf(es.classes, "equipment.deck-gun") < 0) why = "does not take a deck gun";
                    else if (!(es.clearanceSizeU.x > 0f && es.clearanceSizeU.y > 0f && es.clearanceSizeU.z > 0f)) why = "no clearance";
                    Vector3 mn = default, mx = default;
                    if (why == null)
                    {
                        var cs = es.clearanceSizeU;
                        mn = new Vector3(so.posU.x - cs.x * 0.5f, so.posU.y - cs.y * 0.5f, so.posU.z);
                        mx = new Vector3(so.posU.x + cs.x * 0.5f, so.posU.y + cs.y * 0.5f, so.posU.z + cs.z);
                        if (mn.x < -Eps || mx.x > len + Eps || (half > 0f && (mn.y < -half - Eps || mx.y > half + Eps)))
                            why = "clearance sticks out of the section";
                    }
                    if (why == null)
                    {
                        mn += pm.positionU; mx += pm.positionU;
                        foreach (var r in asm.reservations)
                            if (r.kind == "passage"
                                && mn.x < r.maxU.x - Eps && r.minU.x < mx.x - Eps
                                && mn.y < r.maxU.y - Eps && r.minU.y < mx.y - Eps
                                && mn.z < r.maxU.z - Eps && r.minU.z < mx.z - Eps)
                            { why = "clearance overlaps the crew passage " + r.id; break; }
                    }
                    if (why != null) { sc.gunSlotProblems.Add($"{pm.instanceKey}/{id}: {why}"); continue; }
                    sc.gunSlotIds.Add(id);
                    (so.posU.y < 0f ? port : star).Add(viewZ + pm.positionM.z + so.posU.x * k);
                }
                port.Sort(); star.Sort();
                for (int i = 0; i < Mathf.Min(port.Count, star.Count); i++) sc.gunPairZs.Add(0.5f * (port[i] + star[i]));
            }
            if (list.Count > 0) { list[0].aftZ = float.NegativeInfinity; list[list.Count - 1].fwdZ = float.PositiveInfinity; }
            return list;
        }

        /// The table waterline for a total mass: draft above the keel in m,
        /// false = above the deck limit (OVERLOADED) or no tables.
        public static bool TableDraft(ShipyardPlan p, float totalKg, out float draftM) =>
            p.hydro.SolveWaterline(totalKg, HullFormData.SeaWaterDensity, out _, out draftM);

        /// z of each hand's deck station (SteamerBootstrap.DeckStation's
        /// spacing -- keep in step), for the per-section occupancy.
        public static List<float> CrewStationZs(HullFormData d, int hands)
        {
            var zs = new List<float>();
            int pairs = Mathf.Max(1, (hands + 1) / 2);
            float zFwd = d.lwl * 0.30f;
            float zAft = Mathf.Max(d.helm.z + 1.0f, d.well != null ? d.well.fwdZ + 1.0f : -d.lwl * 0.2f);
            for (int n = 0; n < hands; n++)
            {
                int pair = n / 2;
                zs.Add(pairs > 1 ? Mathf.Lerp(zFwd, zAft, pair / (float)(pairs - 1)) : zFwd);
            }
            return zs;
        }

        /// The SAILING MODEL's static draft for a mass: HullFormBody's own
        /// buoyant volume (each station's `AreaAt(level) x dz`, the strip
        /// sum it integrates) solved for `massKg`. Draft above the lowest
        /// keel, m. False above the deck line. PROVISIONAL comparison value.
        public static bool SimStaticDraft(HullFormData d, float massKg, out float draftM)
        {
            draftM = float.NaN;
            if (d?.stations == null || d.stations.Length == 0) return false;
            float keel = float.MaxValue, deck = float.MaxValue;
            foreach (var st in d.stations) { keel = Mathf.Min(keel, st.keelY); deck = Mathf.Min(deck, st.deckY); }
            float need = massKg / HullFormData.SeaWaterDensity;
            if (need > VolumeTo(d, deck)) return false;
            float lo = keel, hi = deck;
            for (int i = 0; i < 50; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (VolumeTo(d, mid) < need) lo = mid; else hi = mid;
            }
            draftM = 0.5f * (lo + hi) - keel;
            return true;
        }

        /// Immersed volume with the water at `level` (ship frame), m^3.
        public static float VolumeTo(HullFormData d, float level)
        {
            float v = 0f;
            for (int i = 0; i < d.StationCount; i++) v += d.AreaAt(i, level) * d.stations[i].dz;
            return v;
        }


        /// The deck strip SteamerBootstrap.DeckStation spreads the hands over:
        /// from +0.30 L forward to 1 m ahead of the helm (or the wheel well).
        /// Capacity no longer derives from it (A4: authored per module); only
        /// the self-test's `derived-capacity-cross-check` prints it.
        public static float CrewStrip(HullFormData d)
        {
            float zFwd = d.lwl * 0.30f;
            float zAft = Mathf.Max(d.helm.z + 1.0f, d.well != null ? d.well.fwdZ + 1.0f : -d.lwl * 0.2f);
            return Mathf.Max(0.01f, zFwd - zAft);
        }

        static List<SectionOccupancy> Occupancy(ShipyardPlan p, LiveShipSnapshot live, ModuleLibrary lib,
            HullFormData reference, ShipyardPlan refPlan)
        {
            var list = new List<SectionOccupancy>();
            foreach (var sc in p.sections)
                list.Add(new SectionOccupancy { sectionKey = sc.sectionKey, moduleId = sc.moduleId, aftZ = sc.aftZ, fwdZ = sc.fwdZ,
                    holdCells = sc.holdCells, berths = sc.berths, gunSlots = sc.gunSlotIds.Count });
            if (list.Count == 0) return list;
            var cellShare = new List<float>();
            foreach (var o in list) cellShare.Add(o.holdCells);
            Share(list, cellShare, live.totalHeld, (o, n) => o.cargoCells = n);
            SectionOccupancy At(float z) { foreach (var o in list) if (z >= o.aftZ && z < o.fwdZ) return o; return list[list.Count - 1]; }
            foreach (var z in CrewStationZs(p.data, live.crewAboard)) At(z).crew++;
            foreach (var g in p.fittedGuns)
            {
                var o = At(g.positionM.z);
                o.guns += 1;
                o.equipment.Add($"gun, {g.side}");
            }
            if (p.assembly.Find("fitting:" + ShipConfiguration.ChimneySocket) != null)
                At(0.5f * (p.funnelAftZ + p.funnelFwdZ)).equipment.Add("funnel");
            for (int i = 0; i < live.kindsOnDeck; i++) At(p.deckLoad.Slot(i).z).equipment.Add($"deck load pile {i + 1}");

            foreach (var o in list)
            {
                if (!o.sectionKey.StartsWith("middle[")) { o.canRemove = false; o.reason = $"A ship needs her {o.sectionKey}."; continue; }
                int idx = int.Parse(o.sectionKey.Substring(7, o.sectionKey.Length - 8));
                var without = p.config.Clone();
                without.middleIds.RemoveAt(idx);
                var w = ShipyardPlanner.Validate(without, lib, reference, live, false);
                o.canRemove = w.ok;
                o.reason = w.ok ? "" : w.issues[0].message;
            }
            return list;
        }

        /// Largest-remainder split of `total` in proportion to `weights`.
        static void Share(List<SectionOccupancy> list, List<float> weights, int total, Action<SectionOccupancy, int> set)
        {
            float sum = 0f; foreach (var x in weights) sum += Mathf.Max(0f, x);
            var got = new int[list.Count]; var rem = new float[list.Count]; int used = 0;
            for (int i = 0; i < list.Count; i++)
            {
                float exact = sum > 0f ? total * Mathf.Max(0f, weights[i]) / sum : 0f;
                got[i] = Mathf.FloorToInt(exact); rem[i] = exact - got[i]; used += got[i];
            }
            while (used < total)
            {
                int best = 0;
                for (int i = 1; i < list.Count; i++) if (rem[i] > rem[best]) best = i;
                got[best]++; rem[best] = -1f; used++;
            }
            for (int i = 0; i < list.Count; i++) set(list[i], got[i]);
        }

        static void CheckRetention(List<Rejection> issues, LiveShipSnapshot live, ShipyardPlan cur, ShipyardPlan draft)
        {
            if (live.totalHeld > draft.capacity.holdCells)
                issues.Add(new Rejection { code = ShipyardCodes.CargoWouldNotFit, partId = "hold",
                    message = $"She is carrying {live.totalHeld} loads and this ship's hold takes {draft.capacity.holdCells}. Unload {live.totalHeld - draft.capacity.holdCells} first; nothing is thrown overboard." });
            float room = draft.WeightAllowanceKg(live.crewAboard, live.weights);
            float cargoKg = live.totalHeld * live.weights.cargoUnitKg;
            if (cargoKg > room + 1f)
                issues.Add(new Rejection { code = ShipyardCodes.CargoWouldNotFit, partId = "weight",
                    message = $"Her cargo weighs {cargoKg / 1000f:0.0} t and this ship can carry {Mathf.Max(0f, room) / 1000f:0.0} t with {live.crewAboard} hands and her guns aboard. Unload first; nothing is thrown overboard." });
            if (live.crewAboard > draft.capacity.crewStations)
                issues.Add(new Rejection { code = ShipyardCodes.CrewWouldNotFit, partId = "crew",
                    message = $"{live.crewAboard} hands are aboard and this ship has stations for {draft.capacity.crewStations}. Land {live.crewAboard - draft.capacity.crewStations} first." });
            foreach (var item in EquipmentLost(live, cur, draft))
                issues.Add(new Rejection { code = ShipyardCodes.EquipmentWouldBeLost, partId = item.id,
                    message = $"{item.label} would have no place on this ship; the refit is refused rather than leave it behind." });
        }

        /// Everything positioned on the CURRENT hull that has a valid place
        /// there but would have none on the draft (D6): the deck-load piles
        /// actually carried, and the funnel. Guns are no longer diffed here
        /// (2026-09-25): a gun the draft cannot keep a place for already
        /// fails assembly (EQUIPMENT_SLOT_UNKNOWN, translated to
        /// EQUIPMENT_WOULD_BE_LOST in Validate) or is refused GUNS_NEED_CREW;
        /// nothing strikes a gun silently any more.
        public static List<PositionedItem> EquipmentLost(LiveShipSnapshot live, ShipyardPlan cur, ShipyardPlan draft)
        {
            var lost = new List<PositionedItem>();
            if (live == null || draft == null) return lost;
            bool draftChimney = draft.assembly?.Find("fitting:" + ShipConfiguration.ChimneySocket) != null;
            if (live.hasChimney && !draftChimney)
                lost.Add(new PositionedItem { id = "chimney", label = "The funnel" });
            if (cur == null) return lost;
            for (int s = 0; s < live.kindsOnDeck; s++)
            {
                var a = cur.deckLoad.Slot(s);
                if (!OnDeck(cur.data, a, 0.2f)) continue;
                var b = draft.deckLoad.Slot(s);
                if (!OnDeck(draft.data, b, 0.2f))
                    lost.Add(new PositionedItem { id = $"deckload{s}", label = $"Deck load pile {s + 1}", position = b });
            }
            return lost;
        }

        /// A point (ship frame) stands on the deck: inside the hull's length,
        /// forward of the wheel well, inside the planking by `inboard`.
        public static bool OnDeck(HullFormData d, Vector3 p, float inboard)
        {
            if (d?.stations == null || d.stations.Length == 0) return false;
            var first = d.stations[0]; var last = d.stations[d.stations.Length - 1];
            if (p.z < first.z - 0.5f * first.dz || p.z > last.z + 0.5f * last.dz) return false;
            if (d.well != null && p.z < d.well.fwdZ) return false;
            int si = NearestStation(d, p.z);
            float half = d.HalfBreadthAt(si, d.stations[si].deckY);
            return Mathf.Abs(p.x) <= half - inboard + 1e-3f;
        }

        public static int NearestStation(HullFormData data, float z)
        {
            int si = 0; float best = float.MaxValue;
            for (int i = 0; i < data.StationCount; i++)
            {
                float d = Mathf.Abs(data.stations[i].z - z);
                if (d < best) { best = d; si = i; }
            }
            return si;
        }

        public static float DeckYAt(HullFormData data, float z) => data.stations[NearestStation(data, z)].deckY;

        public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// The ShipSave.modular field (D-S). Empty = the ship was never refitted:
    /// she is the reference Long() and keeps today's drawing.
    public static class ModularSave
    {
        /// `hasConfig` false for a missing/empty field (old save). A present
        /// but unusable field (unreadable, future module id, not in the
        /// prototype) falls back to Long() with a warning -- never refuses
        /// the save.
        public static ShipConfiguration Decode(string json, ModuleLibrary lib, out bool hasConfig, out string warning)
        {
            warning = null;
            hasConfig = !string.IsNullOrEmpty(json);
            if (!hasConfig) return ShipConfiguration.Long();
            var cfg = ShipConfiguration.FromJson(json);
            if (cfg == null)
            {
                warning = "the saved ship configuration could not be read; she comes back as the standard long steamer.";
                return ShipConfiguration.Long();
            }
            // A v1 document never carried equipment (guns were implicit); give
            // it explicit guns before checking it against today's rules, so
            // an existing refitted save loads with the same guns.
            if (cfg.schemaVersion < ShipConfiguration.SupportedSchemaVersion) cfg = cfg.MigratedToV2();
            var why = new List<Rejection>(ShipyardPolicy.Check(cfg, lib));
            why.AddRange(ShipAssembler.Assemble(cfg, lib).rejections);
            if (why.Count > 0)
            {
                var codes = new List<string>();
                foreach (var w in why) codes.Add(w.code + (string.IsNullOrEmpty(w.partId) ? "" : " " + w.partId));
                warning = $"the saved ship configuration is not buildable here ({string.Join(", ", codes)}); she comes back as the standard long steamer.";
                return ShipConfiguration.Long();
            }
            return cfg;
        }

        public static string Encode(ShipConfiguration cfg) => cfg != null ? cfg.ToJson() : "";
    }
}
