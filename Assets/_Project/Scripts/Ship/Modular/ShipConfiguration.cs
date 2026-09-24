using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// What the player chose, and nothing else: module ids and slot choices.
    /// No positions, no meshes, no numbers derived from geometry -- those are
    /// recomputed by ShipAssembler from the library every time, so a save
    /// holding this survives any change of art.
    ///
    /// Its own versioned JSON document. Milestone 1 does NOT touch the save
    /// system; the plan is an ADDED field on SaveData.ShipSave (e.g.
    /// `public string modularConfigJson;`), which SaveGame already tolerates
    /// without a SaveData version bump.
    [Serializable]
    public class ShipConfiguration
    {
        /// Highest configuration schema this build reads. Bumped to 2
        /// 2026-09-25: guns are explicit `equipment` entries now, not
        /// implicit hull-form sockets (docs/SHIPYARD-API.md). A v1 document
        /// still reads (MigratedToV2 below), never refused.
        public const int SupportedSchemaVersion = 2;

        public int schemaVersion = SupportedSchemaVersion;
        public string sternId;
        public List<string> middleIds = new List<string>();
        public string bowId;
        public string rotorId;
        public string carrierId;
        public List<FittingChoice> fittings = new List<FittingChoice>();
        public List<EquipmentChoice> equipment = new List<EquipmentChoice>();

        // ---- the V3 reference presets ------------------------------------

        public const string V3Stern = "hull.stern.w1r2.v3";
        public const string V3Middle = "hull.middle.w1r2.v3";
        public const string V3Bow = "hull.bow.w1r2.v3";
        public const string ReinforcedRotor = "wheel.rotor.m1.reinforced";
        public const string TimberRotor = "wheel.rotor.m1.timber";
        public const string OversizedRotor = "wheel.rotor.m1l.oversized";
        public const string M1Carrier = "wheel.carrier.m1";
        public const string V3Chimney = "fitting.chimney.v3";
        public const string ChimneySocket = "stern/Chimney";
        public const string EquipmentCannon = "equipment.cannon.astra.v1";
        /// The stern's and bow's own authored gun-slot pair (local ids; both
        /// modules happen to name them the same way).
        const string GunSlotStar = "DeckSlot_1_-1", GunSlotPort = "DeckSlot_1_1";
        const string SternGunSlotStar = "DeckSlot_2_-1", SternGunSlotPort = "DeckSlot_2_1";

        /// Stern + bow, reinforced M1 wheel, chimney, 4 guns (V3 short assembly).
        public static ShipConfiguration Short() => WithMiddles(0);

        /// Stern + one middle + bow, 6 guns (V3 long assembly; today's ship).
        public static ShipConfiguration Long() => WithMiddles(1);

        /// **Guns are explicit equipment (2026-09-25).** The hull's 3 pairs
        /// stand where they always have: bow and stern always carry theirs;
        /// the middle pair stands on the FIRST bay only -- a second or third
        /// bay adds gun SLOTS, not guns (docs/SHIPYARD-API.md §10), so
        /// `WithMiddles(2)` and `WithMiddles(3)` carry the same 6 guns as
        /// `Long()`, just with unused slots further forward.
        public static ShipConfiguration WithMiddles(int n)
        {
            var c = new ShipConfiguration
            {
                sternId = V3Stern,
                bowId = V3Bow,
                rotorId = ReinforcedRotor,
                carrierId = M1Carrier,
            };
            for (int i = 0; i < n; i++) c.middleIds.Add(V3Middle);
            c.fittings.Add(new FittingChoice { socketId = ChimneySocket, moduleId = V3Chimney });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlotStar, moduleId = EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlotPort, moduleId = EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotStar, moduleId = EquipmentCannon });
            c.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotPort, moduleId = EquipmentCannon });
            if (n > 0)
            {
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlotStar, moduleId = EquipmentCannon });
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlotPort, moduleId = EquipmentCannon });
            }
            return c;
        }

        /// A v1 document (saved before 2026-09-25) never carried equipment --
        /// the prototype refused all of it, and guns were implicit hull-form
        /// sockets. This reproduces what she carried as explicit equipment so
        /// an old save's guns come back the same, SIMPLIFIED for what a v1
        /// config could ever actually be: the prototype policy at v1 allowed
        /// no hull shape other than `WithMiddles(n)` (0-3 W1-r2 middles, any
        /// rotor/fittings choice never affected capacity), so bow and stern
        /// always carried their pair and the middle pair always stood on the
        /// first bay -- exactly what `WithMiddles` still does. A config this
        /// build cannot build after migrating falls back to Long() with a
        /// warning regardless (ModularSave.Decode), so an unanticipated v1
        /// document is never silently wrong, only replaced.
        public ShipConfiguration MigratedToV2()
        {
            var m = Clone();
            if (m.schemaVersion >= SupportedSchemaVersion) return m;
            m.schemaVersion = SupportedSchemaVersion;
            if (m.equipment.Count > 0) return m; // already explicit; nothing to invent
            if (!string.IsNullOrEmpty(m.bowId))
            {
                m.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlotStar, moduleId = EquipmentCannon });
                m.equipment.Add(new EquipmentChoice { slotId = "bow/" + GunSlotPort, moduleId = EquipmentCannon });
            }
            if (!string.IsNullOrEmpty(m.sternId))
            {
                m.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotStar, moduleId = EquipmentCannon });
                m.equipment.Add(new EquipmentChoice { slotId = "stern/" + SternGunSlotPort, moduleId = EquipmentCannon });
            }
            if ((m.middleIds?.Count ?? 0) > 0)
            {
                m.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlotStar, moduleId = EquipmentCannon });
                m.equipment.Add(new EquipmentChoice { slotId = "middle[0]/" + GunSlotPort, moduleId = EquipmentCannon });
            }
            return m;
        }

        // ---- JSON ---------------------------------------------------------

        public string ToJson(bool pretty = false) => ModularJson.To(this, pretty);

        /// Parses without judging: an unknown future field is ignored, a
        /// newer schemaVersion is KEPT so ShipAssembler can refuse it with a
        /// readable reason. Returns null only for unreadable text.
        public static ShipConfiguration FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            ShipConfiguration c;
            try { c = ModularJson.From<ShipConfiguration>(json); }
            catch (Exception) { return null; }
            if (c == null) return null;
            if (c.middleIds == null) c.middleIds = new List<string>();
            if (c.fittings == null) c.fittings = new List<FittingChoice>();
            if (c.equipment == null) c.equipment = new List<EquipmentChoice>();
            return c;
        }

        public ShipConfiguration Clone() => FromJson(ToJson());

        // ---- value equality (round-trip tests) ---------------------------

        public bool ValueEquals(ShipConfiguration o)
        {
            if (o == null) return false;
            if (schemaVersion != o.schemaVersion) return false;
            if (!Same(sternId, o.sternId) || !Same(bowId, o.bowId)) return false;
            if (!Same(rotorId, o.rotorId) || !Same(carrierId, o.carrierId)) return false;
            if (Count(middleIds) != Count(o.middleIds)) return false;
            for (int i = 0; i < Count(middleIds); i++) if (!Same(middleIds[i], o.middleIds[i])) return false;
            if (Count(fittings) != Count(o.fittings)) return false;
            for (int i = 0; i < Count(fittings); i++)
                if (!Same(fittings[i]?.socketId, o.fittings[i]?.socketId) || !Same(fittings[i]?.moduleId, o.fittings[i]?.moduleId))
                    return false;
            if (Count(equipment) != Count(o.equipment)) return false;
            for (int i = 0; i < Count(equipment); i++)
            {
                var a = equipment[i]; var b = o.equipment[i];
                if (a == null || b == null) { if (a != b) return false; continue; }
                if (!Same(a.slotId, b.slotId) || !Same(a.moduleId, b.moduleId)) return false;
                if ((a.offsetU - b.offsetU).sqrMagnitude > 1e-10f) return false;
            }
            return true;
        }

        static int Count<T>(List<T> l) => l == null ? 0 : l.Count;
        // JsonUtility writes a null string as "", so treat the two as equal.
        static bool Same(string a, string b) => (a ?? "") == (b ?? "");
    }

    [Serializable]
    public class FittingChoice
    {
        /// Qualified socket: "<section instance>/<socket id>", e.g. "stern/Chimney".
        public string socketId;
        public string moduleId;
    }

    [Serializable]
    public class EquipmentChoice
    {
        /// Qualified slot: "<instance key>/<slot id>", e.g. "middle[0]/DeckSlot_1_1"
        /// or "fitting:stern/UpperDeckMount/CannonSocket_Port_1".
        public string slotId;
        public string moduleId;
        /// Shift from the slot's socket, in the slot module's authoring axes.
        /// Zero on fixed slots; the position on a free-placement deck area.
        public Vector3 offsetU;
    }
}
