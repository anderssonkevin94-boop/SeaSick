using System;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    // ---------------------------------------------------------------------
    // The modular-ship DATA CONTRACT (milestone 1). Plain [Serializable]
    // classes with public fields, so JsonUtility reads and writes them and
    // a missing field keeps the value written here. Every top-level
    // document carries its own schemaVersion; every module carries its own
    // content `version`. See docs/MODULAR-SHIPS.md for the field-by-field
    // description and the rules Astra's deliveries follow.
    //
    // All positions and sizes in these classes are in AUTHORING units
    // ("V8 units") on Blender's axes: +X bow, +Y port, +Z up. Nothing here
    // is in game metres; ModularScale is the one place that converts.
    // ---------------------------------------------------------------------

    /// String constants for `ModuleDef.kind`. Strings, not an enum, because
    /// JsonUtility writes enums as integers and a reordered enum would
    /// silently re-type every module file.
    public static class ModuleKind
    {
        public const string Stern = "Stern";
        public const string Middle = "Middle";
        public const string Bow = "Bow";
        public const string Rotor = "Rotor";
        public const string Carrier = "Carrier";
        public const string Fitting = "Fitting";
        public const string UpperDeck = "UpperDeck";
        public const string Equipment = "Equipment";

        public static readonly string[] All = { Stern, Middle, Bow, Rotor, Carrier, Fitting, UpperDeck, Equipment };

        public static bool IsKnown(string kind) => Array.IndexOf(All, kind) >= 0;
        public static bool IsHull(string kind) => kind == Stern || kind == Middle || kind == Bow;
    }

    /// String constants for `ModuleDef.status`.
    public static class ModuleStatus
    {
        /// Real authored geometry that is still a reference prototype.
        public const string Prototype = "prototype";
        /// No real geometry; the view draws a marked grey box. Assembles, but
        /// is listed in AssemblyResult.placeholders so UI can flag it.
        public const string Placeholder = "placeholder";
        /// Kept only so the rules can be tested against it (e.g. the V1 W1
        /// middle). Never offered to a player.
        public const string IncompatibleReference = "incompatible-reference";
        /// Signed off by Kevin and Astra (none yet).
        public const string Approved = "approved";
    }

    /// String constants for `SocketDef.role`.
    public static class SocketRole
    {
        /// The ship's aft end on a stern (not a join; no standard).
        public const string HullOrigin = "hull.origin";
        /// A join to the section aft of this one (standard = JoinProfile).
        public const string HullAft = "hull.aft";
        /// A join to the section forward of this one (standard = JoinProfile).
        public const string HullFwd = "hull.fwd";
        /// The bow's forward extreme, prow included (not a join; no standard).
        public const string HullTip = "hull.tip";
        /// The bow's stem at the height datum (the waterline end of the hull
        /// proper), decorative prow EXCLUDED. Not a join; no standard. Used to
        /// measure the hull's length for physics (Shipyard: HullMeasure).
        public const string HullStem = "hull.stem";
        public const string Wheel = "wheel";
        public const string FittingChimney = "fitting.chimney";
        public const string DeckUpper = "deck.upper";
        public const string DeckSlot = "deck.slot";
        public const string DeckArea = "deck.area";
    }

    /// String constants for `SocketDef.placementRule`.
    public static class PlacementRule
    {
        /// Use posU as written (module-local).
        public const string Fixed = "";
        /// X is replaced by the midpoint between the stern's aft interface and
        /// the bow's forward socket (the "assembled ship midpoint" of the V3
        /// README); Y and Z are taken from posU.
        public const string AssembledMidpoint = "assembled-midpoint";
    }

    // ---- standards.json --------------------------------------------------

    [Serializable]
    public class ModuleStandards
    {
        public int schemaVersion = 1;
        /// The ONE authoring-unit -> metre conversion. Uniform on all axes.
        public float metresPerUnit;
        public string axisConvention;
        /// Upper limit on repeated middle sections (Astra verified 0-3 bays).
        public int maxMiddles;
        /// One berth's cost in a section's interior space budget (A4 hold-
        /// cell units), 2026-09-25 (docs/SHIPYARD-SECTIONS-UI.md step 2).
        /// PROVISIONAL. 0/unset falls back to 0.5 (ModuleLibrary.BerthSpaceUnits).
        public float berthSpaceUnits;
        public JoinProfile[] joinProfiles;
        public MountStandard[] mountStandards;
        public SlotClass[] slotClasses;
        public string notes;
    }

    /// A hull cross-section interface. Two hull sections may join only if the
    /// forward socket of one and the aft socket of the next name the SAME
    /// profile id. Names of the modules are irrelevant.
    [Serializable]
    public class JoinProfile
    {
        public string id;
        public int version;
        public string status;
        public int profilePoints;
        public float halfBeamU;
        public float deckZU;
        public float keelZU;
        public string description;
        public string sourceNote;
        /// Raised-deck families only (e.g. W1xR): absolute authoring Z of the
        /// flush upper deck (4.20 for W1xR). 0 = not a raised profile.
        /// `deckZU` stays the flotation/depth datum (unchanged from the
        /// non-raised profile the family is built over) so `sDepth` stays 1;
        /// this is the NEW datum crew stand on and equipment above 1.76
        /// resolves against. See docs/RAISED-DECK.md sec 3/6.
        public float upperDeckZU;
    }

    /// A wheel mount interface (stern pocket + carrier + rotor).
    [Serializable]
    public class MountStandard
    {
        public string id;
        public int version;
        public string status;
        /// Reference rotor for this mount (the rotor authored against it).
        public float nominalRadius;
        public float sweptRadius;
        public float paddleWidth;
        /// Largest swept radius the pocket accepts.
        public float radiusLimit;
        /// Absolute authoring Z of the pocket ceiling (study README).
        public float pocketCeilingZU;
        public string description;
        public string sourceNote;
    }

    /// A vocabulary entry for fitting sockets and equipment slots.
    [Serializable]
    public class SlotClass
    {
        public string id;
        public string description;
    }

    // ---- one module file -------------------------------------------------

    [Serializable]
    public class ModuleDef
    {
        public int schemaVersion = 1;
        /// Stable, dotted, never reused ("hull.stern.w1r2.v3"). Saves and
        /// configurations refer to modules by this id only.
        public string id;
        /// Content revision of this id. Bump for a compatible change (same
        /// interfaces, same socket meanings); an incompatible change gets a
        /// NEW id instead.
        public int version = 1;
        public string kind;
        /// Interface family, e.g. "W1-r2". Informational; compatibility is
        /// decided by socket standards, never by family or file names.
        public string family;
        public string status;
        public string displayName;
        public string description;
        public string source;
        /// Length along +X from the aft interface (hull sections).
        public float lengthU;
        /// Module-local authoring AABB (placeholder boxes, overall extents).
        public Vector3 boundsMinU;
        public Vector3 boundsMaxU;
        public string boundsNote;
        public SocketDef[] sockets;
        /// Purely visual. Swapping these never changes placement or rules.
        public VisualPart[] visuals;
        public RotorSpec rotor;
        public CarrierSpec carrier;
        public FittingSpec fitting;
        public EquipmentSpec equipment;
        public EquipmentSlotDef[] equipmentSlots;
        public PassageDef[] passages;
        /// Future gameplay numbers. ALL unset in milestone 1.
        public PhysicalSpec physical;
        /// Hull sections: Astra's hydrostatic station table (upright level
        /// flotation of Hull_Shell only). Null/empty path = none.
        public HydrostaticsRef hydrostatics;
        /// Hull sections: provisional lightship mass. The shipyard report
        /// AND the sailing model weigh the ship with the sum of these (one
        /// mass source; docs/SHIPYARD-API.md §10). Any module may carry one.
        public LightshipSpec lightship;
        /// Hull sections: AUTHORED room aboard (A4), summed over the installed
        /// sections. Every field is provisional data. Null = none (a hull
        /// section without it cannot be planned: NO_CAPACITY).
        public CapacitySpec capacity;
        /// Raised-deck families only (docs/RAISED-DECK.md sec 6): the mass
        /// this module adds ABOVE its non-raised counterpart (the deck cap +
        /// topside walls from the old deck to the new upper deck), already
        /// included in `lightship.massKg` (NOT additive on top of it -- this
        /// block exists so the sim can find the extra mass's own height and
        /// raise the ship's CoM/GM/roll gyradius by it). Null = no raised
        /// upper structure on this module.
        public UpperStructureSpec upperStructure;
    }

    /// See `ModuleDef.upperStructure`. Every mass in kg, every height an
    /// ABSOLUTE authoring Z (module datum, same frame as a socket's posU.z),
    /// not a delta -- callers subtract the profile's keelZU themselves, the
    /// same way `kg`/`kb` are heights above the keel.
    [Serializable]
    public class UpperStructureSpec
    {
        /// deckMassKg + wallMassKg. Already inside lightship.massKg once.
        public float massKg;
        /// Mass-weighted: (deckMassKg*upperDeckZU + wallMassKg*wallAreaCentroidZU) / massKg.
        public float centroidZU;
        public float deckMassKg;
        public float deckAreaM2;
        public float wallMassKg;
        public float wallAreaM2;
        /// Area centroid of the topside walls (old deck to upper deck), Z.
        public float wallAreaCentroidZU;
        /// Height of the deck plate itself, module-local Z (2026-09-27, for
        /// UpperDeck fittings: third deck / foredeck). 0 = the installed
        /// raised profile's `upperDeckZU` (every hull.*.w1xr module, whose
        /// deck plate IS that profile's deck). Every Z in this block is
        /// module-local; `RaisedDeckPhysics.RaiseCoM` adds the placed
        /// module's own origin Z (0 for hull sections, the mount height for
        /// a fitting).
        public float deckZU;
        public string source;
    }

    /// Authored per-module capacity (A4, 2026-09-24). Tunable numbers, not
    /// derived from geometry. Seeded so stern + 1 middle + bow reproduces
    /// today's steamer exactly (16 hold cells, 8 berths, 3 gun pairs).
    [Serializable]
    public class CapacitySpec
    {
        /// Hold cells (volume) this section contributes.
        public ProvisionalInt holdCells;
        /// Crew berths (deck stations) this section contributes.
        public ProvisionalInt berths;
        /// Equipment-slot ids ON THIS MODULE that may carry a gun. A listed
        /// slot only counts if it exists, takes `equipment.deck-gun`, has a
        /// clearance box inside the section and clear of every crew passage
        /// of the assembled ship (checked by the shipyard, not trusted).
        /// One id per gun; a gun PAIR needs a port and a starboard id.
        public ProvisionalSlots gunSlots;
        /// OPTIONAL floor-area cap on this section's berths (2026-09-25,
        /// docs/SHIPYARD-SECTIONS-UI.md step 2), independent of the space
        /// budget. Null = uncapped by floor area; the budget alone caps it
        /// (`floor(budget / berthSpaceUnits)`).
        public ProvisionalInt maxBerths;
        /// How the numbers were chosen (for the next person to retune them).
        public string rule;
    }

    [Serializable]
    public class ProvisionalInt
    {
        public int value;
        public bool provisional = true;
        public string source;
    }

    [Serializable]
    public class ProvisionalSlots
    {
        public string[] ids;
        public bool provisional = true;
        public string source;
    }

    /// A tunable float with the same provenance shape as ProvisionalInt.
    [Serializable]
    public class ProvisionalFloat
    {
        public float value;
        public bool provisional = true;
        public string source;
    }

    [Serializable]
    public class HydrostaticsRef
    {
        /// Resources path without extension to the table JSON.
        public string resourcePath;
        public string sourceGeometrySha256;
        /// [keel, main deck] heights above the module datum, U. Never
        /// extrapolated above.
        public float[] validWaterlineZU;
        /// Vertical offset of this module's datum in the ship frame, U
        /// (0 for every W1-r2 section today).
        public float offsetZU;
        public string notes;
    }

    [Serializable]
    public class LightshipSpec
    {
        public float massKg;
        public bool provisional = true;
        public string rule;
    }

    [Serializable]
    public class SocketDef
    {
        public string id;
        public string role;
        /// A JoinProfile id (hull sockets), a MountStandard id (wheel
        /// sockets) or a SlotClass id (fitting / slot sockets).
        public string standard;
        /// Module-local authoring position.
        public Vector3 posU;
        /// Rotation about authoring +Z (degrees, counter-clockwise seen from
        /// above, i.e. bow towards port). Only equipment uses it today.
        public float yawDeg;
        /// Wheel sockets: largest rotor swept radius this pocket takes.
        public float radiusLimit;
        public string placementRule;
        public bool provisional;
        public string notes;
    }

    [Serializable]
    public class VisualPart
    {
        public string id;
        /// Resources path without extension, e.g.
        /// "ShipModules/Meshes/HullW1r2_v3/Stern_W1/Hull_Shell".
        public string resourcePath;
        public bool placeholder;
        /// Module-local authoring offset this ONE part is instantiated at,
        /// on top of the module's own origin (additive to
        /// ModularShipView.Build's per-module placement). Zero for every
        /// single-piece visual authored so far (W1-r2's kits export one
        /// whole mesh per named part, already in module-local space) --
        /// added 2026-09-25 for a MULTI-PART kit (the width-inserts family)
        /// whose Port/Starboard/Insert pieces are split out and carry their
        /// own manifest `local_position`. Unset = Vector3.zero, so every
        /// existing module JSON reads unchanged.
        public Vector3 localPositionU;
        /// Yaw of this ONE part about authoring +Z (same sense as a socket's
        /// yawDeg), applied about the part's own origin. Zero for every kit
        /// exported in the V8 convention. The cannon kit (cannon-astra-v1)
        /// imports with its muzzle along authoring +X (bow) instead of the
        /// -Y its README states, so its visual carries -90 to fire outboard
        /// at slot yaw 0; footprints and clearances are unaffected.
        public float yawDegU;
        /// Full authoring-axis rotation of this ONE part (degrees, about
        /// its own origin, Blender's XYZ Euler order -- X applied first,
        /// then Y, then Z, matching a manifest's own `rotation_radians`),
        /// composed with `yawDegU` (docs/RAISED-SECTIONS.md task item 3a).
        /// `yawDegU` alone only ever turns a part about authoring +Z; a
        /// part hinged about a DIFFERENT authoring axis (e.g. the raised
        /// stern/bow's hatch, tilted -80/+80 deg about authoring Y) has no
        /// correct value to put there -- putting the number in `yawDegU`
        /// anyway rotates the mesh about the wrong game axis. Zero (the
        /// default) for every part authored so far except the two hatches
        /// this field was added for; unset = Vector3.zero, so every
        /// existing module JSON reads unchanged.
        public Vector3 rotationDegU;
        public string notes;
    }

    [Serializable]
    public class RotorSpec
    {
        public string mount;
        public float nominalRadius;
        public float sweptRadius;
        public float paddleWidth;
        public string notes;
    }

    [Serializable]
    public class CarrierSpec
    {
        public string mount;
        public string notes;
    }

    [Serializable]
    public class FittingSpec
    {
        /// The SlotClass id of the socket this fitting plugs into.
        public string socketClass;
        public string originNote;
        /// UpperDeck fittings (2026-09-27): how far this layer raises its
        /// host section's walking deck (third deck: 2.44 u over the 4.20
        /// raised deck). > 0 = an ENCLOSED layer: the host section's
        /// stations get `HullFormData.RaiseDeck` by this much again and a
        /// chimney standing on the host rises with it. 0 = open platform
        /// (the foredeck): mass and CoM only.
        public float layerRiseU;
        /// Visual part ids of the HOST section this fitting covers and
        /// replaces (Astra's review scene hides them: the raised deck's
        /// rails/posts/planks, its hatch lid, helm, prow). ShipAssembler
        /// drops them from the host's placed visuals.
        public string[] hidesHostVisuals;
        /// Non-empty = Astra's contract says this art is unfinished; the
        /// shipyard report shows it as an ART_UNREVIEWED warning line.
        public string unreviewedNote;
    }

    [Serializable]
    public class EquipmentSpec
    {
        public string equipmentClass;
        /// Footprint box: X along the socket's forward, Y across, Z up.
        public Vector3 footprintU;
        public bool placeholder;
        public string notes;
        /// Provisional weight this ONE item adds to the weight allowance
        /// (WeightAllowanceKg) when fitted -- NOT to the ship's lightship
        /// mass/sim (that stays hull-only; see ShipyardPlanner.ModuleLightshipKg).
        /// Null = fall back to WeightModel.gunKg (a deck-gun) / 0.
        public ProvisionalFloat massKg;
        /// Provisional crew this ONE item needs when fitted (a deck-gun:
        /// CrewPerGun hands at the gun). Null = fall back to WeightModel.CrewPerGun.
        public ProvisionalInt crew;
    }

    /// A place equipment can stand. Its clearance box has its BASE CENTRE at
    /// the socket: x in [s.x - size.x/2, s.x + size.x/2], same for y,
    /// z in [s.z, s.z + size.z].
    [Serializable]
    public class EquipmentSlotDef
    {
        public string id;
        public string socketId;
        public Vector3 clearanceSizeU;
        public string[] classes;
        public bool provisional;
        public string notes;
    }

    /// A crew-passage exclusion: no equipment may overlap this box.
    /// Centre + full size, module-local.
    [Serializable]
    public class PassageDef
    {
        public string id;
        public Vector3 centreU;
        public Vector3 sizeU;
        public bool provisional;
        public string notes;
    }

    /// Future mass / buoyancy / capacity / propulsion inputs. Deliberately a
    /// separate block from geometry: NONE of these may be derived from mesh
    /// size, section length or wheel radius (a bigger wheel is not a faster
    /// ship). Every field stays `authored = false` until Kevin sets numbers.
    [Serializable]
    public class PhysicalSpec
    {
        public AuthoredValue massKg;
        public AuthoredValue displacementM3;
        public AuthoredValue cargoCapacity;
        public AuthoredValue crewCapacity;
        public AuthoredValue thrustCoefficient;
        public string notes;
    }

    /// Nullable-by-convention number (JsonUtility has no nullables).
    [Serializable]
    public class AuthoredValue
    {
        public bool authored;
        public float value;
        public string source;
    }
}
