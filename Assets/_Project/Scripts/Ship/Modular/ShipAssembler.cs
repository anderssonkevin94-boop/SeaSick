using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// One reason a configuration was refused. `code` is stable (tests, UI
    /// icons, analytics); `message` is a sentence a player or designer can
    /// act on; `partId` is the config entry at fault.
    [Serializable]
    public class Rejection
    {
        public string code;
        public string partId;
        public string message;
        public override string ToString() => $"{code} [{partId}]: {message}";
    }

    /// One module placed in the ship. Positions are the module ORIGIN (its
    /// aft interface at the height datum for hull sections, the axle centre
    /// for rotor/carrier, the base centre for the chimney).
    [Serializable]
    public class PlacedModule
    {
        public string moduleId;
        public string kind;
        /// "stern", "middle[0]", "bow", "rotor", "carrier",
        /// "fitting:stern/Chimney", "equipment:middle[0]/DeckSlot_1_1".
        public string instanceKey;
        public Vector3 positionU;   // ship frame, authoring units
        public Vector3 positionM;   // ship frame, game metres
        public Quaternion rotation = Quaternion.identity; // game
        public bool placeholder;
        public Vector3 boundsMinU;  // module-local
        public Vector3 boundsMaxU;
        public VisualPart[] visuals;
    }

    [Serializable]
    public class ResolvedSlot
    {
        public string qualifiedId;
        public string role;
        public Vector3 positionM;
        public Vector3 clearanceMinM;
        public Vector3 clearanceMaxM;
        public string[] classes;
        public bool provisional;
        public string occupiedBy;
    }

    [Serializable]
    public class Reservation
    {
        /// "passage" or "equipment".
        public string kind;
        public string id;
        public Vector3 minU, maxU;  // ship frame, authoring
        public Vector3 minM, maxM;  // ship frame, game metres
    }

    [Serializable]
    public class AssemblyResult
    {
        public bool ok;
        public List<Rejection> rejections = new List<Rejection>();
        public float metresPerUnit;
        public List<PlacedModule> placed = new List<PlacedModule>();
        public bool hasWheel;
        public Vector3 wheelAxleM;
        /// Stern aft interface to bow forward socket (prow included, as the
        /// manifest measures "total assembly length").
        public float hullLengthM;
        /// Same span today: the decorative prow is part of the bow's length.
        /// Kept separate so a bow that authors a detachable ornament can
        /// report both.
        public float overallLengthM;
        /// How far the wheel's swept circle reaches aft of the stern's aft
        /// interface (0 if it does not).
        public float wheelOverhangAftM;
        public List<ResolvedSlot> slots = new List<ResolvedSlot>();
        public List<Reservation> reservations = new List<Reservation>();
        /// Instance keys of modules whose status is "placeholder".
        public List<string> placeholders = new List<string>();

        public PlacedModule Find(string instanceKey)
        {
            foreach (var p in placed) if (p.instanceKey == instanceKey) return p;
            return null;
        }

        public bool HasCode(string code)
        {
            foreach (var r in rejections) if (r.code == code) return true;
            return false;
        }

        public string Summary()
        {
            if (ok) return $"OK: {placed.Count} modules, {overallLengthM.ToString("0.00", CultureInfo.InvariantCulture)} m";
            var parts = new List<string>();
            foreach (var r in rejections) parts.Add(r.message);
            return string.Join("\n", parts);
        }
    }

    /// Configuration + library -> placed modules, or the list of reasons it
    /// cannot be built. Pure C#: no GameObjects, no physics, no scene.
    ///
    /// Decision (milestone 1): on ANY rejection, `ok` is false and the
    /// placement lists are EMPTY, but every reason found is reported, not
    /// just the first. A half-built ship is never handed to a view or to
    /// gameplay; the caller keeps showing the last valid one.
    public static class ShipAssembler
    {
        const float Eps = 1e-3f;

        /// Raised-deck family (docs/RAISED-DECK.md sec 2): the chimney's
        /// assembled-midpoint X on a W1xR ship needs this offset so a
        /// one-middle raised ship lands on the raised kit's own manifest
        /// position (today's unmodified midpoint formula overshoots by
        /// 0.84 u). standards.json/SocketDef have no per-family offset
        /// field for this (by design -- see hull.stern.w1xr.v1.json's
        /// Chimney socket note); it lives here instead. Unverified for a
        /// two-middle raised ship (Astra's manifest only gives the
        /// one-middle chimney_position) but applied the same way per the
        /// spec's note.
        const float RaisedChimneyMidpointOffsetU = -0.84f;
        const string RaisedFamily = "W1xR";

        /// Raised-deck family bay-count gate (docs/RAISED-DECK.md sec 3):
        /// the kit only closes with 1 or 2 middle bays.
        const int RaisedDeckMinMiddles = 1, RaisedDeckMaxMiddles = 2;

        public const string StdKeyStern = "stern";
        public const string StdKeyBow = "bow";
        public const string StdKeyRotor = "rotor";
        public const string StdKeyCarrier = "carrier";
        public static string MiddleKey(int i) => $"middle[{i}]";

        class Instance
        {
            public ModuleDef def;
            public Vector3 originU;
            public Quaternion rotation = Quaternion.identity;
            public string key;
        }

        public static AssemblyResult Assemble(ShipConfiguration cfg, ModuleLibrary lib)
        {
            var r = new AssemblyResult();
            if (cfg == null) { Reject(r, "CONFIG_MISSING", "", "There is no ship configuration to build."); return r; }
            if (cfg.schemaVersion > ShipConfiguration.SupportedSchemaVersion)
            {
                Reject(r, "CONFIG_SCHEMA_TOO_NEW", "schemaVersion",
                    $"This ship was saved by a newer version of the game (configuration format {cfg.schemaVersion}; " +
                    $"this version reads up to {ShipConfiguration.SupportedSchemaVersion}). Update the game to open it.");
                return r;
            }
            if (lib == null || !lib.Usable)
            {
                string why = lib == null ? "no module library" : string.Join(" ", lib.errors);
                Reject(r, "LIBRARY_INVALID", "", $"The ship module data could not be loaded: {why}");
                return r;
            }
            r.metresPerUnit = lib.MetresPerUnit;

            var instances = new Dictionary<string, Instance>();
            var order = new List<Instance>();

            // ---- 1. hull chain ------------------------------------------
            var middles = cfg.middleIds ?? new List<string>();
            if (string.IsNullOrEmpty(cfg.sternId))
                Reject(r, "STERN_MISSING", StdKeyStern, "A ship needs exactly one stern section, at the aft end.");
            if (string.IsNullOrEmpty(cfg.bowId))
                Reject(r, "BOW_MISSING", StdKeyBow, "A ship needs exactly one bow section, at the forward end.");
            if (middles.Count > lib.MaxMiddles)
                Reject(r, "TOO_MANY_MIDDLES", "middleIds",
                    $"This ship has {middles.Count} middle sections; the most the current hull family supports is {lib.MaxMiddles}.");

            var chain = new List<(string key, string id, string kind)>();
            if (!string.IsNullOrEmpty(cfg.sternId)) chain.Add((StdKeyStern, cfg.sternId, ModuleKind.Stern));
            for (int i = 0; i < middles.Count; i++) chain.Add((MiddleKey(i), middles[i], ModuleKind.Middle));
            if (!string.IsNullOrEmpty(cfg.bowId)) chain.Add((StdKeyBow, cfg.bowId, ModuleKind.Bow));

            Instance prev = null;
            bool chainIntact = !string.IsNullOrEmpty(cfg.sternId) && !string.IsNullOrEmpty(cfg.bowId);
            foreach (var (key, id, kind) in chain)
            {
                var def = Resolve(r, lib, key, id);
                if (def == null) { chainIntact = false; prev = null; continue; }
                if (def.kind != kind)
                {
                    RejectWrongKind(r, key, def, kind);
                    chainIntact = false; prev = null; continue;
                }
                var inst = new Instance { def = def, key = key };
                if (kind == ModuleKind.Stern)
                {
                    inst.originU = Vector3.zero;
                }
                else if (prev != null)
                {
                    var fwd = ModuleLibrary.FindSocket(prev.def, SocketRole.HullFwd);
                    var aft = ModuleLibrary.FindSocket(def, SocketRole.HullAft);
                    if (fwd == null)
                    {
                        Reject(r, "SOCKET_MISSING", prev.key, $"{ModuleLibrary.Name(prev.def)} has no forward join, so nothing can be fitted ahead of it.");
                        chainIntact = false;
                    }
                    if (aft == null)
                    {
                        Reject(r, "SOCKET_MISSING", key, $"{ModuleLibrary.Name(def)} has no aft join, so it cannot be fitted behind anything.");
                        chainIntact = false;
                    }
                    if (fwd != null && aft != null)
                    {
                        if (fwd.standard != aft.standard)
                            Reject(r, "JOIN_PROFILE_MISMATCH", key,
                                $"{ModuleLibrary.Name(def)} joins with profile {aft.standard}, but {ModuleLibrary.Name(prev.def)} needs " +
                                $"{fwd.standard} — their cross-sections differ even though the section names may match, so the hull would not close.");
                        inst.originU = prev.originU + fwd.posU - aft.posU;
                    }
                }
                else chainIntact = false;
                instances[key] = inst;
                order.Add(inst);
                prev = inst;
            }

            instances.TryGetValue(StdKeyStern, out var stern);
            instances.TryGetValue(StdKeyBow, out var bow);

            // Raised-deck bay count (docs/RAISED-DECK.md sec 3): a mixed
            // family is already refused above by JOIN_PROFILE_MISMATCH (the
            // raised sockets carry standard "W1xR", which no W1x/W1-r2
            // socket names), so by the time either end is confirmed W1xR the
            // whole intact chain is raised. 0 or >= 3 middles closes the kit
            // wrong; refused with its own readable code, in addition to
            // whatever TOO_MANY_MIDDLES already said for the >= 3 case.
            bool raisedEnd = (stern != null && stern.def.family == RaisedFamily) || (bow != null && bow.def.family == RaisedFamily);
            if (raisedEnd && (middles.Count < RaisedDeckMinMiddles || middles.Count > RaisedDeckMaxMiddles))
                Reject(r, "RAISED_DECK_BAYS", "middleIds", "A raised deck is built for one or two middle bays.");

            Vector3 aftEndU = Vector3.zero, tipU = Vector3.zero;
            if (stern != null)
            {
                var o = ModuleLibrary.FindSocket(stern.def, SocketRole.HullOrigin);
                aftEndU = stern.originU + (o != null ? o.posU : Vector3.zero);
            }
            if (chainIntact && bow != null)
            {
                var t = ModuleLibrary.FindSocket(bow.def, SocketRole.HullTip);
                tipU = bow.originU + (t != null ? t.posU : new Vector3(bow.def.lengthU, 0f, 0f));
            }

            // ---- 2. wheel ------------------------------------------------
            SocketDef wheel = stern != null ? ModuleLibrary.FindSocket(stern.def, SocketRole.Wheel) : null;
            if (!string.IsNullOrEmpty(cfg.rotorId))
            {
                var rotor = Resolve(r, lib, StdKeyRotor, cfg.rotorId);
                if (rotor != null && rotor.kind != ModuleKind.Rotor) { RejectWrongKind(r, StdKeyRotor, rotor, ModuleKind.Rotor); rotor = null; }
                if (rotor != null && stern != null)
                {
                    if (wheel == null)
                        Reject(r, "WHEEL_SOCKET_MISSING", StdKeyRotor, $"{ModuleLibrary.Name(stern.def)} has no wheel pocket, so it cannot carry {ModuleLibrary.Name(rotor)}.");
                    else
                    {
                        float limit = wheel.radiusLimit > 0f ? wheel.radiusLimit : (lib.FindMount(wheel.standard)?.radiusLimit ?? 0f);
                        if (rotor.rotor.mount != wheel.standard)
                            Reject(r, "WHEEL_MOUNT_MISMATCH", StdKeyRotor,
                                $"{ModuleLibrary.Name(rotor)} needs an {rotor.rotor.mount} stern housing; this stern's wheel pocket is {wheel.standard} " +
                                $"(fits radius ≤ {F(limit)}, this wheel sweeps {F(rotor.rotor.sweptRadius)}).");
                        else if (rotor.rotor.sweptRadius > limit + 1e-5f)
                            Reject(r, "WHEEL_TOO_LARGE", StdKeyRotor,
                                $"{ModuleLibrary.Name(rotor)} sweeps a radius of {F(rotor.rotor.sweptRadius)}, but this stern's {wheel.standard} pocket " +
                                $"fits at most {F(limit)}; the paddles would strike the housing.");
                        var inst = new Instance { def = rotor, key = StdKeyRotor, originU = stern.originU + wheel.posU };
                        instances[StdKeyRotor] = inst; order.Add(inst);
                    }
                }
                if (string.IsNullOrEmpty(cfg.carrierId))
                    Reject(r, "CARRIER_MISSING", StdKeyCarrier, "A paddle wheel needs a carrier (the fixed bearings and frame) to hang in.");
            }
            if (!string.IsNullOrEmpty(cfg.carrierId))
            {
                var carrier = Resolve(r, lib, StdKeyCarrier, cfg.carrierId);
                if (carrier != null && carrier.kind != ModuleKind.Carrier) { RejectWrongKind(r, StdKeyCarrier, carrier, ModuleKind.Carrier); carrier = null; }
                if (carrier != null && stern != null)
                {
                    if (wheel == null)
                        Reject(r, "WHEEL_SOCKET_MISSING", StdKeyCarrier, $"{ModuleLibrary.Name(stern.def)} has no wheel pocket for {ModuleLibrary.Name(carrier)}.");
                    else
                    {
                        if (carrier.carrier.mount != wheel.standard)
                            Reject(r, "CARRIER_MOUNT_MISMATCH", StdKeyCarrier,
                                $"{ModuleLibrary.Name(carrier)} is built for an {carrier.carrier.mount} pocket; this stern's wheel pocket is {wheel.standard}.");
                        var inst = new Instance { def = carrier, key = StdKeyCarrier, originU = stern.originU + wheel.posU };
                        instances[StdKeyCarrier] = inst; order.Add(inst);
                    }
                }
            }

            // ---- 3. fittings ---------------------------------------------
            var takenSockets = new HashSet<string>();
            foreach (var f in cfg.fittings ?? new List<FittingChoice>())
            {
                if (f == null) continue;
                string part = f.socketId ?? "";
                if (!SplitQualified(part, out var hostKey, out var socketLocal) || !instances.TryGetValue(hostKey, out var host)
                    || !ModuleKind.IsHull(host.def.kind))
                {
                    Reject(r, "FITTING_SOCKET_UNKNOWN", part, $"There is no fitting socket '{part}' on this ship.");
                    continue;
                }
                var socket = ModuleLibrary.FindSocketById(host.def, socketLocal);
                if (socket == null)
                {
                    Reject(r, "FITTING_SOCKET_UNKNOWN", part, $"{ModuleLibrary.Name(host.def)} has no fitting socket '{socketLocal}'.");
                    continue;
                }
                var def = Resolve(r, lib, part, f.moduleId);
                if (def == null) continue;
                if (def.kind != ModuleKind.Fitting && def.kind != ModuleKind.UpperDeck)
                {
                    Reject(r, "FITTING_WRONG_KIND", part, $"{ModuleLibrary.Name(def)} is a {def.kind.ToLowerInvariant()}, not a fitting; it cannot go on socket {socketLocal}.");
                    continue;
                }
                if (def.fitting.socketClass != socket.standard)
                {
                    Reject(r, "FITTING_CLASS_MISMATCH", part, $"{ModuleLibrary.Name(def)} fits a '{def.fitting.socketClass}' socket; '{socketLocal}' is a '{socket.standard}' socket.");
                    continue;
                }
                if (!takenSockets.Add(part))
                {
                    Reject(r, "FITTING_SOCKET_TAKEN", part, $"Socket {socketLocal} already has a fitting.");
                    continue;
                }
                var pos = host.originU + socket.posU;
                if (socket.placementRule == PlacementRule.AssembledMidpoint)
                {
                    if (!chainIntact) continue; // the hull reasons are already reported
                    pos.x = (aftEndU.x + tipU.x) * 0.5f;
                    if (host.def.family == RaisedFamily) pos.x += RaisedChimneyMidpointOffsetU;
                }
                var inst = new Instance { def = def, key = "fitting:" + part, originU = pos,
                    rotation = ModularScale.AuthoringYawToGame(socket.yawDeg) };
                instances[inst.key] = inst; order.Add(inst);
            }

            // ---- 4. passages and slots -----------------------------------
            var passages = new List<Reservation>();
            foreach (var inst in order)
                if (inst.def.passages != null)
                    foreach (var p in inst.def.passages)
                    {
                        if (p == null) continue;
                        var c = inst.originU + p.centreU;
                        passages.Add(MakeBox("passage", $"{inst.key}/{p.id}", c - p.sizeU * 0.5f, c + p.sizeU * 0.5f, r.metresPerUnit));
                    }

            var slotOccupant = new Dictionary<string, string>();
            var equipBoxes = new List<Reservation>();
            var equipInstances = new List<Instance>();
            foreach (var e in cfg.equipment ?? new List<EquipmentChoice>())
            {
                if (e == null) continue;
                string part = e.slotId ?? "";
                if (!SplitQualified(part, out var hostKey, out var slotLocal) || !instances.TryGetValue(hostKey, out var host))
                {
                    Reject(r, "EQUIPMENT_SLOT_UNKNOWN", part, $"There is no equipment slot '{part}' on this ship.");
                    continue;
                }
                var slot = FindSlot(host.def, slotLocal);
                var socket = slot != null ? ModuleLibrary.FindSocketById(host.def, slot.socketId) : null;
                if (slot == null || socket == null)
                {
                    Reject(r, "EQUIPMENT_SLOT_UNKNOWN", part, $"{ModuleLibrary.Name(host.def)} has no equipment slot '{slotLocal}'.");
                    continue;
                }
                var def = Resolve(r, lib, part, e.moduleId);
                if (def == null) continue;
                if (def.kind != ModuleKind.Equipment)
                {
                    Reject(r, "EQUIPMENT_WRONG_KIND", part, $"{ModuleLibrary.Name(def)} is not deck equipment.");
                    continue;
                }
                if (slot.classes == null || Array.IndexOf(slot.classes, def.equipment.equipmentClass) < 0)
                {
                    Reject(r, "EQUIPMENT_CLASS_NOT_ALLOWED", part,
                        $"{ModuleLibrary.Name(def)} ({def.equipment.equipmentClass}) is not allowed on {slotLocal}; it takes {Join(slot.classes)}.");
                    continue;
                }
                if (slotOccupant.ContainsKey(part) && socket.role != SocketRole.DeckArea)
                {
                    Reject(r, "EQUIPMENT_SLOT_TAKEN", part, $"Slot {slotLocal} already holds {slotOccupant[part]}.");
                    continue;
                }
                bool swap = Mathf.Abs(Mathf.Repeat(socket.yawDeg, 180f) - 90f) < 1f;
                var fp = def.equipment.footprintU;
                if (swap) fp = new Vector3(fp.y, fp.x, fp.z);
                var baseC = socket.posU + e.offsetU;
                var fMin = new Vector3(baseC.x - fp.x * 0.5f, baseC.y - fp.y * 0.5f, baseC.z);
                var fMax = new Vector3(baseC.x + fp.x * 0.5f, baseC.y + fp.y * 0.5f, baseC.z + fp.z);
                var cs = slot.clearanceSizeU;
                var cMin = new Vector3(socket.posU.x - cs.x * 0.5f, socket.posU.y - cs.y * 0.5f, socket.posU.z);
                var cMax = new Vector3(socket.posU.x + cs.x * 0.5f, socket.posU.y + cs.y * 0.5f, socket.posU.z + cs.z);
                if (!Inside(fMin, fMax, cMin, cMax))
                {
                    bool tooBig = fp.x > cs.x + Eps || fp.y > cs.y + Eps || fp.z > cs.z + Eps;
                    Reject(r, "EQUIPMENT_EXCEEDS_CLEARANCE", part, tooBig
                        ? $"{ModuleLibrary.Name(def)} does not fit the space reserved at {slotLocal} (needs {V(fp)}, the slot keeps {V(cs)} clear)."
                        : $"{ModuleLibrary.Name(def)} would stick out of the space reserved at {slotLocal}; move it back inside the slot's {V(cs)} clearance.");
                    continue;
                }
                // Fixed slots reserve their whole clearance (room to work the
                // gun); free deck-area placement reserves the footprint.
                var resMin = socket.role == SocketRole.DeckArea ? fMin : cMin;
                var resMax = socket.role == SocketRole.DeckArea ? fMax : cMax;
                var box = MakeBox("equipment", part, host.originU + resMin, host.originU + resMax, r.metresPerUnit);
                bool bad = false;
                foreach (var p in passages)
                    if (Overlap(box, p))
                    {
                        Reject(r, "EQUIPMENT_BLOCKS_PASSAGE", part,
                            $"{ModuleLibrary.Name(def)} at {slotLocal} would stand in the crew passage ({p.id}); the crew need that way kept clear.");
                        bad = true; break;
                    }
                if (!bad)
                    foreach (var other in equipBoxes)
                        if (Overlap(box, other))
                        {
                            Reject(r, "EQUIPMENT_OVERLAP", part,
                                $"{ModuleLibrary.Name(def)} at {slotLocal} overlaps the space already reserved for {other.id}.");
                            bad = true; break;
                        }
                if (bad) continue;
                equipBoxes.Add(box);
                slotOccupant[part] = def.id;
                equipInstances.Add(new Instance { def = def, key = "equipment:" + part, originU = host.originU + baseC,
                    rotation = host.rotation * ModularScale.AuthoringYawToGame(socket.yawDeg) });
            }
            order.AddRange(equipInstances);

            // ---- 5. result -----------------------------------------------
            if (r.rejections.Count > 0) { r.ok = false; return r; }

            float k = r.metresPerUnit;
            foreach (var inst in order)
            {
                var d = inst.def;
                r.placed.Add(new PlacedModule
                {
                    moduleId = d.id,
                    kind = d.kind,
                    instanceKey = inst.key,
                    positionU = inst.originU,
                    positionM = ModularScale.AuthoringToGame(inst.originU, k),
                    rotation = inst.rotation,
                    placeholder = d.status == ModuleStatus.Placeholder,
                    boundsMinU = d.boundsMinU,
                    boundsMaxU = d.boundsMaxU,
                    visuals = d.visuals ?? new VisualPart[0],
                });
                if (d.status == ModuleStatus.Placeholder) r.placeholders.Add(inst.key);
                if (d.equipmentSlots != null)
                    foreach (var s in d.equipmentSlots)
                    {
                        var sock = s != null ? ModuleLibrary.FindSocketById(d, s.socketId) : null;
                        if (sock == null) continue;
                        string q = $"{inst.key}/{s.id}";
                        var cs = s.clearanceSizeU;
                        var mn = inst.originU + new Vector3(sock.posU.x - cs.x * 0.5f, sock.posU.y - cs.y * 0.5f, sock.posU.z);
                        var mx = inst.originU + new Vector3(sock.posU.x + cs.x * 0.5f, sock.posU.y + cs.y * 0.5f, sock.posU.z + cs.z);
                        ModularScale.AuthoringBoxToGame(mn, mx, k, out var gMin, out var gMax);
                        r.slots.Add(new ResolvedSlot
                        {
                            qualifiedId = q,
                            role = sock.role,
                            positionM = ModularScale.AuthoringToGame(inst.originU + sock.posU, k),
                            clearanceMinM = gMin,
                            clearanceMaxM = gMax,
                            classes = s.classes ?? new string[0],
                            provisional = s.provisional || sock.provisional,
                            occupiedBy = slotOccupant.TryGetValue(q, out var occ) ? occ : "",
                        });
                    }
            }
            r.reservations.AddRange(passages);
            r.reservations.AddRange(equipBoxes);

            r.hullLengthM = ModularScale.AuthoringLengthToMetres(tipU.x - aftEndU.x, k);
            r.overallLengthM = r.hullLengthM;
            if (instances.TryGetValue(StdKeyRotor, out var rot))
            {
                r.hasWheel = true;
                r.wheelAxleM = ModularScale.AuthoringToGame(rot.originU, k);
                float reachAft = aftEndU.x - (rot.originU.x - rot.def.rotor.sweptRadius);
                r.wheelOverhangAftM = Mathf.Max(0f, ModularScale.AuthoringLengthToMetres(reachAft, k));
            }
            r.ok = true;
            return r;
        }

        // ---- helpers ------------------------------------------------------

        static ModuleDef Resolve(AssemblyResult r, ModuleLibrary lib, string part, string id)
        {
            if (lib.TryGet(id, out var def)) return def;
            Reject(r, "UNKNOWN_MODULE", part, $"No ship part called '{id}' exists in the module library.");
            return null;
        }

        static void RejectWrongKind(AssemblyResult r, string key, ModuleDef def, string wanted)
        {
            string code = wanted == ModuleKind.Stern ? "STERN_WRONG_KIND"
                : wanted == ModuleKind.Bow ? "BOW_WRONG_KIND"
                : wanted == ModuleKind.Middle ? "MIDDLE_WRONG_KIND"
                : "WRONG_KIND";
            string what = def.kind == ModuleKind.Stern ? "a stern; a ship has exactly one, at the aft end"
                : def.kind == ModuleKind.Bow ? "a bow; a ship has exactly one, at the forward end"
                : $"a {def.kind.ToLowerInvariant()} part";
            Reject(r, code, key, $"{ModuleLibrary.Name(def)} is {what}. The {key} position needs a {wanted.ToLowerInvariant()}.");
        }

        static void Reject(AssemblyResult r, string code, string part, string message)
        {
            r.rejections.Add(new Rejection { code = code, partId = part, message = message });
            r.ok = false;
        }

        static EquipmentSlotDef FindSlot(ModuleDef d, string id)
        {
            if (d.equipmentSlots == null) return null;
            foreach (var s in d.equipmentSlots) if (s != null && s.id == id) return s;
            return null;
        }

        /// "middle[0]/DeckSlot_1_1" -> ("middle[0]", "DeckSlot_1_1"). Split at
        /// the LAST slash so fitting instance keys may contain slashes.
        static bool SplitQualified(string q, out string host, out string local)
        {
            host = local = null;
            if (string.IsNullOrEmpty(q)) return false;
            int i = q.LastIndexOf('/');
            if (i <= 0 || i == q.Length - 1) return false;
            host = q.Substring(0, i);
            local = q.Substring(i + 1);
            return true;
        }

        static Reservation MakeBox(string kind, string id, Vector3 minU, Vector3 maxU, float k)
        {
            ModularScale.AuthoringBoxToGame(minU, maxU, k, out var mn, out var mx);
            return new Reservation { kind = kind, id = id, minU = minU, maxU = maxU, minM = mn, maxM = mx };
        }

        /// Strict overlap: boxes that only touch do not overlap.
        static bool Overlap(Reservation a, Reservation b) =>
            a.minU.x < b.maxU.x - Eps && b.minU.x < a.maxU.x - Eps &&
            a.minU.y < b.maxU.y - Eps && b.minU.y < a.maxU.y - Eps &&
            a.minU.z < b.maxU.z - Eps && b.minU.z < a.maxU.z - Eps;

        static bool Inside(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) =>
            aMin.x >= bMin.x - Eps && aMin.y >= bMin.y - Eps && aMin.z >= bMin.z - Eps &&
            aMax.x <= bMax.x + Eps && aMax.y <= bMax.y + Eps && aMax.z <= bMax.z + Eps;

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => $"{F(v.x)} x {F(v.y)} x {F(v.z)}";
        static string Join(string[] a) => a == null || a.Length == 0 ? "nothing" : string.Join(", ", a);
    }
}
