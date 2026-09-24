using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Every module definition plus the standards they are written against,
    /// validated on load. The library never guesses: a module whose file is
    /// newer than this code understands, whose kind is unknown, whose id is
    /// taken, or whose sockets name a standard that does not exist is left
    /// out and the reason is kept in `errors`.
    public class ModuleLibrary
    {
        /// Highest schemaVersion of standards.json and module files that this
        /// code reads. A newer file is refused, never half-read.
        public const int SupportedSchemaVersion = 1;

        public const string StandardsResource = "ShipModules/standards";
        public const string ModulesResourceFolder = "ShipModules/Modules";

        public ModuleStandards Standards { get; private set; }
        public readonly List<string> errors = new List<string>();
        readonly Dictionary<string, ModuleDef> byId = new Dictionary<string, ModuleDef>();
        readonly List<ModuleDef> ordered = new List<ModuleDef>();

        /// True when the standards loaded and no module was refused.
        public bool Ok => Standards != null && errors.Count == 0;
        /// True when the standards loaded (modules may still have been refused).
        public bool Usable => Standards != null;
        public float MetresPerUnit => Standards != null ? Standards.metresPerUnit : 0f;
        public int MaxMiddles => Standards != null ? Standards.maxMiddles : 0;
        public IReadOnlyList<ModuleDef> All => ordered;

        public ModuleDef Get(string id)
        {
            if (TryGet(id, out var def)) return def;
            throw new KeyNotFoundException($"No ship module with id '{id}'.");
        }

        public bool TryGet(string id, out ModuleDef def)
        {
            def = null;
            return !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out def);
        }

        // ---- loading ------------------------------------------------------

        /// In Unity: Resources/ShipModules/standards.json and every TextAsset
        /// under Resources/ShipModules/Modules.
        public static ModuleLibrary LoadFromResources()
        {
            var std = Resources.Load<TextAsset>(StandardsResource);
            var mods = Resources.LoadAll<TextAsset>(ModulesResourceFolder);
            var texts = new List<string>();
            var names = new List<string>();
            foreach (var t in mods) { texts.Add(t.text); names.Add(t.name); }
            if (std == null)
            {
                var lib = new ModuleLibrary();
                lib.errors.Add($"LIB_STANDARDS_MISSING: Resources/{StandardsResource}.json was not found.");
                return lib;
            }
            return FromJson(std.text, texts, names);
        }

        /// Headless and test entry: raw JSON strings. `sourceNames` (optional,
        /// same order) only improves error messages.
        public static ModuleLibrary FromJson(string standardsJson, IEnumerable<string> moduleJsons,
            IList<string> sourceNames = null)
        {
            var lib = new ModuleLibrary();
            lib.LoadStandards(standardsJson);
            if (lib.Standards == null) return lib;
            int i = 0;
            foreach (var json in moduleJsons)
            {
                string src = sourceNames != null && i < sourceNames.Count ? sourceNames[i] : $"module #{i}";
                lib.LoadModule(json, src);
                i++;
            }
            return lib;
        }

        void LoadStandards(string json)
        {
            ModuleStandards s;
            try { s = ModularJson.From<ModuleStandards>(json); }
            catch (Exception e) { errors.Add($"LIB_PARSE_ERROR: standards could not be read ({e.Message})."); return; }
            if (s == null) { errors.Add("LIB_PARSE_ERROR: standards file is empty."); return; }
            if (s.schemaVersion > SupportedSchemaVersion)
            {
                errors.Add($"LIB_SCHEMA_TOO_NEW: the standards file is schema {s.schemaVersion}, " +
                           $"this build reads up to {SupportedSchemaVersion}. Update the game before using this module data.");
                return;
            }
            if (!(s.metresPerUnit > 0f))
            {
                errors.Add("LIB_BAD_SCALE: standards.metresPerUnit must be a positive number.");
                return;
            }
            Standards = s;
        }

        void LoadModule(string json, string src)
        {
            ModuleDef d;
            try { d = ModularJson.From<ModuleDef>(json); }
            catch (Exception e) { errors.Add($"LIB_PARSE_ERROR: {src} could not be read ({e.Message})."); return; }
            if (d == null) { errors.Add($"LIB_PARSE_ERROR: {src} is empty."); return; }
            if (string.IsNullOrEmpty(d.id)) { errors.Add($"LIB_MISSING_ID: {src} has no id."); return; }
            if (d.schemaVersion > SupportedSchemaVersion)
            {
                errors.Add($"LIB_SCHEMA_TOO_NEW: module '{d.id}' is schema {d.schemaVersion}, " +
                           $"this build reads up to {SupportedSchemaVersion}; it was left out.");
                return;
            }
            if (byId.ContainsKey(d.id))
            {
                errors.Add($"LIB_DUPLICATE_ID: module id '{d.id}' is defined twice ({src}); the second copy was left out.");
                return;
            }
            if (!ModuleKind.IsKnown(d.kind))
            {
                errors.Add($"LIB_UNKNOWN_KIND: module '{d.id}' has kind '{d.kind}', which is not one of " +
                           $"{string.Join(", ", ModuleKind.All)}; it was left out.");
                return;
            }
            var bad = UnknownStandards(d);
            if (bad.Count > 0)
            {
                errors.Add($"LIB_UNKNOWN_STANDARD: module '{d.id}' refers to {string.Join(", ", bad)}, " +
                           "which standards.json does not define; it was left out.");
                return;
            }
            byId.Add(d.id, d);
            ordered.Add(d);
        }

        List<string> UnknownStandards(ModuleDef d)
        {
            var bad = new List<string>();
            if (d.sockets != null)
                foreach (var s in d.sockets)
                    if (s != null && !string.IsNullOrEmpty(s.standard) && !IsStandard(s.standard))
                        bad.Add($"socket '{s.id}' standard '{s.standard}'");
            if (d.kind == ModuleKind.Rotor && (d.rotor == null || !IsMount(d.rotor.mount)))
                bad.Add($"rotor mount '{d.rotor?.mount}'");
            if (d.kind == ModuleKind.Carrier && (d.carrier == null || !IsMount(d.carrier.mount)))
                bad.Add($"carrier mount '{d.carrier?.mount}'");
            if ((d.kind == ModuleKind.Fitting || d.kind == ModuleKind.UpperDeck)
                && (d.fitting == null || !IsSlotClass(d.fitting.socketClass)))
                bad.Add($"fitting socket class '{d.fitting?.socketClass}'");
            if (d.kind == ModuleKind.Equipment && (d.equipment == null || !IsSlotClass(d.equipment.equipmentClass)))
                bad.Add($"equipment class '{d.equipment?.equipmentClass}'");
            if (d.equipmentSlots != null)
                foreach (var e in d.equipmentSlots)
                    if (e?.classes != null)
                        foreach (var c in e.classes)
                            if (!IsSlotClass(c)) bad.Add($"slot '{e.id}' class '{c}'");
            return bad;
        }

        // ---- standards lookups -------------------------------------------

        public bool IsStandard(string id) => IsJoinProfile(id) || IsMount(id) || IsSlotClass(id);

        public bool IsJoinProfile(string id) => FindProfile(id) != null;
        public bool IsMount(string id) => FindMount(id) != null;

        public bool IsSlotClass(string id)
        {
            if (Standards?.slotClasses == null || string.IsNullOrEmpty(id)) return false;
            foreach (var c in Standards.slotClasses) if (c != null && c.id == id) return true;
            return false;
        }

        public JoinProfile FindProfile(string id)
        {
            if (Standards?.joinProfiles == null || string.IsNullOrEmpty(id)) return null;
            foreach (var p in Standards.joinProfiles) if (p != null && p.id == id) return p;
            return null;
        }

        public MountStandard FindMount(string id)
        {
            if (Standards?.mountStandards == null || string.IsNullOrEmpty(id)) return null;
            foreach (var m in Standards.mountStandards) if (m != null && m.id == id) return m;
            return null;
        }

        // ---- helpers the assembler shares -------------------------------

        public static SocketDef FindSocket(ModuleDef d, string role)
        {
            if (d?.sockets == null) return null;
            foreach (var s in d.sockets) if (s != null && s.role == role) return s;
            return null;
        }

        public static SocketDef FindSocketById(ModuleDef d, string id)
        {
            if (d?.sockets == null) return null;
            foreach (var s in d.sockets) if (s != null && s.id == id) return s;
            return null;
        }

        public static string Name(ModuleDef d) =>
            d == null ? "(none)" : string.IsNullOrEmpty(d.displayName) ? d.id : d.displayName;
    }
}
