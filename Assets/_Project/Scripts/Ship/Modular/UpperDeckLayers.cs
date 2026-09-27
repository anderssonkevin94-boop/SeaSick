using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Upper-deck layers (2026-09-27, Kevin: "import whatever assets Astra has
    /// made (even if she hasn't pushed them; if they need fixing later I'll
    /// deal with that), so that includes ... the upper decks"): Astra's
    /// third deck (art-staging/modular-third-layer-v1, one module per
    /// connected raised stern/middle/bow) and the raised gun foredeck
    /// (art-staging/modular-foredeck-v1, narrow, on the V3 bow). Both are
    /// `UpperDeck` modules fitted on a hull section's `deck.upper` socket
    /// (a `FittingChoice`, like the chimney), so ShipAssembler's existing
    /// socket-class check decides where each can go. Pure; no scene.
    public static class UpperDeckLayers
    {
        public const string ThirdStern = "deck.third.stern.w1xr.v1";
        public const string ThirdMiddle = "deck.third.middle.w1xr.v1";
        public const string ThirdBow = "deck.third.bow.w1xr.v1";
        public const string Foredeck = "deck.foredeck.w1r2.v1";

        public const string CodeArtUnreviewed = "ART_UNREVIEWED";
        public const string CodeThirdDeckPartial = "THIRD_DECK_PARTIAL";
        public const string CodeThirdDeckNoAccess = "THIRD_DECK_NO_ACCESS";
        public const string CodeGunsUnderThirdDeck = "GUNS_UNDER_THIRD_DECK";
        public const string CodeForedeckChimney = "FOREDECK_CHIMNEY_CLOSE";

        /// The `deck.upper` socket of `hull` and the offered UpperDeck module
        /// whose socket class matches it; null when this section takes none
        /// (e.g. a raised wall variant, or a low W1x section).
        public static string OptionFor(ModuleDef hull, ModuleLibrary lib, out string socketLocal)
        {
            socketLocal = null;
            if (hull?.sockets == null || lib == null) return null;
            foreach (var s in hull.sockets)
            {
                if (s == null || s.role != SocketRole.DeckUpper) continue;
                foreach (var id in ShipyardPolicy.AllowedModuleIds(ModuleKind.UpperDeck))
                    if (lib.TryGet(id, out var d) && d.fitting != null && d.fitting.socketClass == s.standard)
                    {
                        socketLocal = s.id;
                        return id;
                    }
            }
            return null;
        }

        /// The option for the section `sectionKey` of `cfg` (by its current
        /// module id), or null.
        public static string OptionFor(ShipConfiguration cfg, string sectionKey, ModuleLibrary lib, out string socketQualified)
        {
            socketQualified = null;
            string id = HullIdOf(cfg, sectionKey);
            if (id == null || lib == null || !lib.TryGet(id, out var hull)) return null;
            string m = OptionFor(hull, lib, out var local);
            if (m != null) socketQualified = sectionKey + "/" + local;
            return m;
        }

        /// Whether `cfg` fits an upper-deck layer on `sectionKey`.
        public static bool Has(ShipConfiguration cfg, string sectionKey)
        {
            if (cfg?.fittings == null) return false;
            foreach (var f in cfg.fittings)
                if (f != null && f.socketId != null && f.socketId.StartsWith(sectionKey + "/") && IsLayerId(f.moduleId)) return true;
            return false;
        }

        public static bool IsLayerId(string moduleId) =>
            moduleId == ThirdStern || moduleId == ThirdMiddle || moduleId == ThirdBow || moduleId == Foredeck;

        /// A NEW config with the layer on `sectionKey` fitted (`on`) or taken
        /// off. Taking it off also drops any equipment standing on it (the
        /// foredeck's guns -- the draft's own dry-dock diff sends them to the
        /// dock). An UNCHANGED copy when the section offers no layer.
        public static ShipConfiguration With(ShipConfiguration cfg, string sectionKey, bool on, ModuleLibrary lib)
        {
            var next = cfg != null ? cfg.Clone() : new ShipConfiguration();
            next.fittings ??= new List<FittingChoice>();
            next.equipment ??= new List<EquipmentChoice>();
            if (!on)
            {
                var gone = new List<string>();
                next.fittings.RemoveAll(f =>
                {
                    bool drop = f != null && f.socketId != null && f.socketId.StartsWith(sectionKey + "/") && IsLayerId(f.moduleId);
                    if (drop) gone.Add("fitting:" + f.socketId + "/");
                    return drop;
                });
                next.equipment.RemoveAll(e => e?.slotId != null && gone.Exists(g => e.slotId.StartsWith(g)));
                return next;
            }
            if (Has(next, sectionKey)) return next;
            string m = OptionFor(next, sectionKey, lib, out var socket);
            if (m == null) return next;
            next.fittings.Add(new FittingChoice { socketId = socket, moduleId = m });
            return next;
        }

        /// Drops every upper-deck layer whose host section no longer offers
        /// that exact module on that socket (a deck-level toggle turned a
        /// connected raised id into a wall variant, or lowered it), plus
        /// equipment standing on it. Returns how many layers were dropped.
        public static int DropOrphaned(ShipConfiguration cfg, ModuleLibrary lib)
        {
            if (cfg?.fittings == null || lib == null) return 0;
            var gone = new List<string>();
            cfg.fittings.RemoveAll(f =>
            {
                if (f == null || !IsLayerId(f.moduleId) || string.IsNullOrEmpty(f.socketId)) return false;
                int slash = f.socketId.LastIndexOf('/');
                string section = slash > 0 ? f.socketId.Substring(0, slash) : f.socketId;
                string m = OptionFor(cfg, section, lib, out var socket);
                bool drop = m != f.moduleId || socket != f.socketId;
                if (drop) gone.Add("fitting:" + f.socketId + "/");
                return drop;
            });
            cfg.equipment?.RemoveAll(e => e?.slotId != null && gone.Exists(g => e.slotId.StartsWith(g)));
            return gone.Count;
        }

        /// The upper-deck layers placed on the hull section `sectionKey`.
        public static List<(PlacedModule placed, ModuleDef def)> On(AssemblyResult asm, ModuleLibrary lib, string sectionKey)
        {
            var list = new List<(PlacedModule, ModuleDef)>();
            if (asm?.placed == null || lib == null) return list;
            foreach (var pm in asm.placed)
                if (pm.kind == ModuleKind.UpperDeck && ShipAssembler.UpperDeckHostKey(pm.instanceKey) == sectionKey
                    && lib.TryGet(pm.moduleId, out var d))
                    list.Add((pm, d));
            return list;
        }

        public static bool AnyInstalled(AssemblyResult asm)
        {
            if (asm?.placed == null) return false;
            foreach (var pm in asm.placed) if (pm.kind == ModuleKind.UpperDeck) return true;
            return false;
        }

        /// The layers' own authored hold cells/berths, summed.
        public static void ExtraCapacity(List<(PlacedModule placed, ModuleDef def)> layers, out int hold, out int berths)
        {
            hold = 0; berths = 0;
            foreach (var (_, d) in layers)
            {
                var c = d.capacity;
                if (c == null) continue;
                if (c.holdCells != null) hold += Mathf.Max(0, c.holdCells.value);
                if (c.berths != null) berths += Mathf.Max(0, c.berths.value);
            }
        }

        public struct EnclosedRange { public float fromZ, toZ, riseU, topDeckZU; public string sectionKey; }

        /// One range per ENCLOSED layer (fitting.layerRiseU > 0): its host
        /// section's own extent in `ShipyardPlan.data`'s frame (same
        /// conversion as RaisedDeckPhysics.FindRaisedSectionRanges) and the
        /// rise. `topDeckZU` = the layer's new walking deck, authoring Z.
        public static List<EnclosedRange> EnclosedRanges(AssemblyResult asm, ModuleLibrary lib, float metresPerUnit, float viewOffsetZ)
        {
            var list = new List<EnclosedRange>();
            if (asm == null || !asm.ok || lib == null) return list;
            foreach (var pm in asm.placed)
            {
                if (pm.kind != ModuleKind.UpperDeck || !lib.TryGet(pm.moduleId, out var d) || d.fitting == null || d.fitting.layerRiseU <= 0f) continue;
                string host = ShipAssembler.UpperDeckHostKey(pm.instanceKey);
                var hp = host != null ? asm.Find(host) : null;
                if (hp == null) continue;
                float fromZ = viewOffsetZ + hp.positionM.z + hp.boundsMinU.x * metresPerUnit;
                float toZ = viewOffsetZ + hp.positionM.z + hp.boundsMaxU.x * metresPerUnit;
                if (toZ < fromZ) (fromZ, toZ) = (toZ, fromZ);
                list.Add(new EnclosedRange { fromZ = fromZ, toZ = toZ, riseU = d.fitting.layerRiseU,
                    topDeckZU = pm.positionU.z + d.fitting.layerRiseU, sectionKey = host });
            }
            return list;
        }

        /// Report lines for a draft carrying upper-deck layers: the art is
        /// Astra's unreviewed prototype (ART_UNREVIEWED, once per module),
        /// and the specific gaps her READMEs name.
        public static List<ShipyardNote> Warnings(AssemblyResult asm, ModuleLibrary lib)
        {
            var notes = new List<ShipyardNote>();
            if (asm == null || !asm.ok || lib == null) return notes;
            var seen = new HashSet<string>();
            var thirdHosts = new HashSet<string>();
            PlacedModule foredeck = null;
            foreach (var pm in asm.placed)
            {
                if (pm.kind != ModuleKind.UpperDeck || !lib.TryGet(pm.moduleId, out var d)) continue;
                if (d.fitting != null && !string.IsNullOrEmpty(d.fitting.unreviewedNote) && seen.Add(d.id))
                    notes.Add(new ShipyardNote { code = CodeArtUnreviewed, message = d.fitting.unreviewedNote });
                if (d.fitting != null && d.fitting.layerRiseU > 0f) thirdHosts.Add(ShipAssembler.UpperDeckHostKey(pm.instanceKey));
                if (d.id == Foredeck) foredeck = pm;
            }
            if (thirdHosts.Count > 0)
            {
                int hullSections = 0;
                foreach (var pm in asm.placed) if (ModuleKind.IsHull(pm.kind)) hullSections++;
                if (thirdHosts.Count < hullSections)
                    notes.Add(new ShipyardNote { code = CodeThirdDeckPartial,
                        message = "The third deck does not run the whole ship: its open ends have no finished bulkheads or edge guards yet (art unreviewed)." });
                if (!thirdHosts.Contains(ShipAssembler.StdKeyStern) && !thirdHosts.Contains(ShipAssembler.StdKeyBow))
                    notes.Add(new ShipyardNote { code = CodeThirdDeckNoAccess,
                        message = "Only the stern and bow third-deck pieces carry a hatch and ladder; this third deck has no way up yet." });
                foreach (var pm in asm.placed)
                {
                    if (pm.kind != ModuleKind.Equipment || !pm.instanceKey.StartsWith("equipment:")) continue;
                    string slot = pm.instanceKey.Substring("equipment:".Length);
                    int slash = slot.IndexOf('/');
                    string section = slash > 0 ? slot.Substring(0, slash) : slot;
                    if (!thirdHosts.Contains(section)) continue;
                    notes.Add(new ShipyardNote { code = CodeGunsUnderThirdDeck,
                        message = "Guns on a section under the third deck now stand inside its walls; firing through them is not modelled yet." });
                    break;
                }
            }
            if (foredeck != null)
            {
                var ch = asm.Find("fitting:" + ShipConfiguration.ChimneySocket);
                // Stair foot at bow-local X 2.02 (foredeck manifest); Astra:
                // the compact hull's chimney is close to the stair approach.
                if (ch != null && lib.TryGet(ch.moduleId, out var cd)
                    && foredeck.positionU.x + 2.02f - (ch.positionU.x + cd.boundsMaxU.x) < 3f)
                    notes.Add(new ShipyardNote { code = CodeForedeckChimney,
                        message = "The chimney stands close to the foredeck stair approach; crew movement there is not tested yet." });
            }
            return notes;
        }

        static string HullIdOf(ShipConfiguration cfg, string sectionKey)
        {
            if (cfg == null || string.IsNullOrEmpty(sectionKey)) return null;
            if (sectionKey == ShipAssembler.StdKeyStern) return cfg.sternId;
            if (sectionKey == ShipAssembler.StdKeyBow) return cfg.bowId;
            if (sectionKey.StartsWith("middle[") && sectionKey.EndsWith("]")
                && int.TryParse(sectionKey.Substring(7, sectionKey.Length - 8), out int i)
                && cfg.middleIds != null && i >= 0 && i < cfg.middleIds.Count)
                return cfg.middleIds[i];
            return null;
        }
    }
}
