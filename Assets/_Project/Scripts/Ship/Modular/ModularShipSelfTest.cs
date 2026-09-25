using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Plain-C# gates for the modular-ship core (library, assembler,
    /// configuration JSON, scale contract). No scene, no GameObjects.
    ///
    /// In the editor:
    ///   unity cmd eval --json --code 'return SeaSick.Ship.Modular.ModularShipSelfTest.Run();'
    /// Headless (no Unity): tools/modular-selftest.sh, which calls RunWith()
    /// with the same JSON read from disk.
    public static class ModularShipSelfTest
    {
        public static string Report { get; private set; } = "";
        public static int Passed { get; private set; }
        public static int Failed { get; private set; }

        /// Editor/player entry: reads the JSON from Resources, checks that the
        /// visual parts resolve through Resources.Load, and logs the report.
        public static bool Run()
        {
            var std = Resources.Load<TextAsset>(ModuleLibrary.StandardsResource);
            var mods = Resources.LoadAll<TextAsset>(ModuleLibrary.ModulesResourceFolder);
            var texts = new List<string>(); var names = new List<string>();
            foreach (var t in mods) { texts.Add(t.text); names.Add(t.name); }
            var hull = Resources.Load<TextAsset>(SeaSick.Steamer.HullFormData.ResourcePath);
            bool ok = RunWith(std != null ? std.text : "", texts, names,
                path => Resources.Load<GameObject>(path) != null, hull != null ? hull.text : null,
                path => { var t = Resources.Load<TextAsset>(path); return t != null ? t.text : null; });
            if (ok) Debug.Log(Report); else Debug.LogError(Report);
            return ok;
        }

        /// `resourceExists` (optional) checks each VisualPart.resourcePath.
        public static bool RunWith(string standardsJson, IList<string> moduleJsons, IList<string> names = null,
            Func<string, bool> resourceExists = null, string hullFormJson = null, Func<string, string> readResourceText = null)
        {
            var sb = new StringBuilder();
            int fails = 0, passes = 0;
            void Gate(string name, bool ok, string detail)
            {
                if (ok) passes++; else fails++;
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(name).Append(" -- ").AppendLine(detail);
            }
            sb.AppendLine("[ModularShipSelfTest]");
            try { Body(standardsJson, moduleJsons, names, resourceExists, Gate); }
            catch (Exception e) { fails++; sb.AppendLine("  FAIL exception -- " + e); }
            try { ShipyardSelfTest.Body(standardsJson, moduleJsons, names, hullFormJson, readResourceText, (n, ok, d) => Gate(n, ok, d)); }
            catch (Exception e) { fails++; sb.AppendLine("  FAIL shipyard exception -- " + e); }
            try { ExpandedHullValidation.Body(standardsJson, moduleJsons, names, readResourceText, (n, ok, d) => Gate(n, ok, d)); }
            catch (Exception e) { fails++; sb.AppendLine("  FAIL expanded-hull exception -- " + e); }
            try { RaisedDeckValidation.Body(standardsJson, moduleJsons, names, hullFormJson, readResourceText, (n, ok, d) => Gate(n, ok, d)); }
            catch (Exception e) { fails++; sb.AppendLine("  FAIL raised-deck exception -- " + e); }
            Passed = passes; Failed = fails;
            sb.AppendLine($"ModularShipSelfTest: {passes} PASS, {fails} FAIL");
            Report = sb.ToString();
            return fails == 0;
        }

        delegate void GateFn(string name, bool ok, string detail);

        const float Tol = 1e-3f;
        static bool Near(float a, float b) => Mathf.Abs(a - b) <= Tol;
        static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude <= Tol;
        static string S(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        static void Body(string stdJson, IList<string> mods, IList<string> names, Func<string, bool> exists, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            // Module count derives from the data (mods.Count, the actual
            // library folder) rather than a hard-coded number -- the raised-
            // sections work (docs/RAISED-SECTIONS.md) grew the library to 25;
            // a stale literal here failed for reasons unrelated to whatever
            // was actually being changed. lib.All.Count == mods.Count just
            // proves every module in the folder loaded with no duplicates.
            Gate("library-loads", lib.Ok && lib.All.Count == mods.Count && Near(lib.MetresPerUnit, 0.5f) && lib.MaxMiddles == 3,
                $"ok={lib.Ok} modules={lib.All.Count} k={lib.MetresPerUnit} maxMiddles={lib.MaxMiddles} errors=[{string.Join(" | ", lib.errors)}]");
            if (!lib.Usable) return;

            // ---- scale contract ---------------------------------------
            var port = ModularScale.AuthoringToGame(new Vector3(0f, 3.45f, 0f), 0.5f);
            Gate("axis-port-is-game-minus-x", Near(port, new Vector3(-1.725f, 0f, 0f)), $"authoring y=+3.45 -> {S(port)}");
            var up = ModularScale.AuthoringToGame(new Vector3(9.3f, 0f, 1.76f), 0.5f);
            Gate("axis-bow-is-game-z-up-is-y", Near(up, new Vector3(0f, 0.88f, 4.65f)), $"(9.30,0,1.76) -> {S(up)}");
            var yaw = ModularScale.AuthoringYawToGame(90f) * Vector3.forward;
            Gate("yaw-towards-port-is-game-minus-x", Near(yaw, new Vector3(-1f, 0f, 0f)), $"bow yawed 90 deg to port -> {S(yaw)}");

            // ---- short -----------------------------------------------
            var shortR = ShipAssembler.Assemble(ShipConfiguration.Short(), lib);
            var st = shortR.Find("stern"); var bw = shortR.Find("bow");
            Gate("short-assembles", shortR.ok && shortR.placeholders.Count == 0, shortR.Summary());
            Gate("short-stern-at-zero", st != null && Near(st.positionM, Vector3.zero), st != null ? S(st.positionM) : "missing");
            Gate("short-bow-at-9.30u", bw != null && Near(bw.positionU, new Vector3(9.3f, 0f, 0f)) && Near(bw.positionM, new Vector3(0f, 0f, 4.65f)),
                bw != null ? $"{S(bw.positionU)} u -> {S(bw.positionM)} m" : "missing");
            Gate("short-length-10.14m", Near(shortR.overallLengthM, 10.14f) && Near(shortR.hullLengthM, 10.14f),
                $"overall {shortR.overallLengthM:0.###} m, hull {shortR.hullLengthM:0.###} m");
            var rot = shortR.Find("rotor"); var car = shortR.Find("carrier");
            Gate("short-wheel-at-socket", rot != null && car != null && Near(rot.positionM, new Vector3(0f, 0.175f, 0.36f))
                && Near(car.positionM, rot.positionM) && shortR.hasWheel && Near(shortR.wheelAxleM, rot.positionM),
                rot != null && car != null ? $"rotor {S(rot.positionM)} carrier {S(car.positionM)}" : "missing");
            Gate("short-wheel-overhang", Near(shortR.wheelOverhangAftM, (1.6222284f - 0.72f) * 0.5f), $"{shortR.wheelOverhangAftM:0.###} m aft of the stern interface");
            var ch = shortR.Find("fitting:" + ShipConfiguration.ChimneySocket);
            Gate("short-chimney-at-midpoint", ch != null && Near(ch.positionM, new Vector3(0f, 0.88f, 5.07f)), ch != null ? S(ch.positionM) : "missing");
            SlotAt(shortR, "stern/DeckSlot_2_1", out var portSlot);
            Gate("port-slot-at-game-minus-x", portSlot != null && Near(portSlot.positionM, new Vector3(-1.725f, 0.88f, 2.75f)) && portSlot.provisional,
                portSlot != null ? S(portSlot.positionM) : "missing");

            // ---- long and repeated -----------------------------------
            var longR = ShipAssembler.Assemble(ShipConfiguration.Long(), lib);
            var m0 = longR.Find("middle[0]"); var lb = longR.Find("bow");
            Gate("long-positions", longR.ok && m0 != null && lb != null && Near(m0.positionM.z, 4.65f) && Near(lb.positionM.z, 7.65f)
                && Near(longR.overallLengthM, 13.14f),
                longR.ok ? $"middle {m0.positionM.z:0.###} bow {lb.positionM.z:0.###} length {longR.overallLengthM:0.###}" : longR.Summary());
            var lch = longR.Find("fitting:" + ShipConfiguration.ChimneySocket);
            Gate("long-chimney-at-midpoint", lch != null && Near(lch.positionM.z, 6.57f), lch != null ? S(lch.positionM) : "missing");

            var three = ShipAssembler.Assemble(ShipConfiguration.WithMiddles(3), lib);
            bool rep = three.ok;
            var zs = new List<string>();
            for (int i = 0; i < 3 && rep; i++)
            {
                var m = three.Find($"middle[{i}]");
                rep &= m != null && Near(m.positionM.z, 4.65f + 3f * i);
                if (m != null) zs.Add(m.positionM.z.ToString("0.###"));
            }
            var b3 = three.Find("bow");
            rep &= b3 != null && Near(b3.positionM.z, 13.65f);
            Gate("three-middles-repeat", rep, three.ok ? $"middles z [{string.Join(", ", zs)}] bow {b3?.positionM.z:0.###}" : three.Summary());

            var four = ShipAssembler.Assemble(ShipConfiguration.WithMiddles(4), lib);
            Gate("four-middles-rejected", !four.ok && four.HasCode("TOO_MANY_MIDDLES") && four.placed.Count == 0, Codes(four));

            // ---- wheels ------------------------------------------------
            var timberCfg = ShipConfiguration.Long(); timberCfg.rotorId = ShipConfiguration.TimberRotor;
            var timber = ShipAssembler.Assemble(timberCfg, lib);
            Gate("rotor-swap-changes-only-rotor-visual", OnlyRotorDiffers(longR, timber, out var why), why);

            var bigCfg = ShipConfiguration.Short(); bigCfg.rotorId = ShipConfiguration.OversizedRotor;
            var big = ShipAssembler.Assemble(bigCfg, lib);
            string bigMsg = First(big, "WHEEL_MOUNT_MISMATCH");
            Gate("oversized-wheel-rejected", !big.ok && big.placed.Count == 0 && bigMsg != null && bigMsg.Contains("M1-L")
                && bigMsg.Contains("1.64") && bigMsg.Contains("2.15"), bigMsg ?? Codes(big));

            var noCarrier = ShipConfiguration.Short(); noCarrier.carrierId = "";
            Gate("wheel-without-carrier-rejected", ShipAssembler.Assemble(noCarrier, lib).HasCode("CARRIER_MISSING"), "rotor with no carrier");

            // ---- hull rules --------------------------------------------
            var v1Cfg = ShipConfiguration.Short(); v1Cfg.middleIds.Add("hull.middle.w1.v1");
            var v1 = ShipAssembler.Assemble(v1Cfg, lib);
            string v1Msg = First(v1, "JOIN_PROFILE_MISMATCH");
            Gate("v1-middle-rejected", !v1.ok && v1Msg != null && v1Msg.Contains("W1-r2") && v1Msg.Contains("profile W1,"), v1Msg ?? Codes(v1));

            var w2Cfg = ShipConfiguration.Short(); w2Cfg.middleIds.Add("hull.middle.w2broad.placeholder");
            var w2 = ShipAssembler.Assemble(w2Cfg, lib);
            Gate("w2-placeholder-rejected", !w2.ok && w2.HasCode("JOIN_PROFILE_MISMATCH"), Codes(w2));

            var bowFirst = ShipConfiguration.Short(); bowFirst.sternId = ShipConfiguration.V3Bow;
            var bf = ShipAssembler.Assemble(bowFirst, lib);
            Gate("bow-first-rejected", !bf.ok && bf.HasCode("STERN_WRONG_KIND"), First(bf, "STERN_WRONG_KIND") ?? Codes(bf));

            var twoSterns = ShipConfiguration.Short(); twoSterns.middleIds.Add(ShipConfiguration.V3Stern);
            var ts = ShipAssembler.Assemble(twoSterns, lib);
            Gate("two-sterns-rejected", !ts.ok && ts.HasCode("MIDDLE_WRONG_KIND"), First(ts, "MIDDLE_WRONG_KIND") ?? Codes(ts));

            var bowMid = ShipConfiguration.Short(); bowMid.middleIds.Add(ShipConfiguration.V3Bow);
            Gate("bow-in-middle-rejected", ShipAssembler.Assemble(bowMid, lib).HasCode("MIDDLE_WRONG_KIND"), "bow in the middle list");

            var noBow = ShipConfiguration.Short(); noBow.bowId = "";
            Gate("missing-bow-rejected", ShipAssembler.Assemble(noBow, lib).HasCode("BOW_MISSING"), "no bow");

            var unknown = ShipConfiguration.Short(); unknown.middleIds.Add("hull.middle.does-not-exist");
            var un = ShipAssembler.Assemble(unknown, lib);
            Gate("unknown-id-rejected", !un.ok && un.HasCode("UNKNOWN_MODULE"), Codes(un));

            // ---- fittings and placeholders ------------------------------
            var deckCfg = ShipConfiguration.Short();
            deckCfg.fittings.Add(new FittingChoice { socketId = "stern/UpperDeckMount", moduleId = "deck.upper.partial.placeholder" });
            var deck = ShipAssembler.Assemble(deckCfg, lib);
            Gate("upper-deck-placeholder-flagged", deck.ok && deck.placeholders.Contains("fitting:stern/UpperDeckMount"),
                deck.ok ? $"placeholders [{string.Join(", ", deck.placeholders)}]" : deck.Summary());
            var wrongFit = ShipConfiguration.Short();
            wrongFit.fittings.Add(new FittingChoice { socketId = "stern/UpperDeckMount", moduleId = ShipConfiguration.V3Chimney });
            Gate("fitting-class-mismatch-rejected", ShipAssembler.Assemble(wrongFit, lib).HasCode("FITTING_CLASS_MISMATCH"), "chimney on the upper-deck mount");

            // ---- equipment ---------------------------------------------
            const string Cannon = "equipment.cannon.placeholder";
            var gunCfg = ShipConfiguration.Long();
            gunCfg.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = Cannon });
            var gun = ShipAssembler.Assemble(gunCfg, lib);
            SlotAt(gun, "middle[0]/DeckSlot_0_1", out var gunSlot);
            Gate("cannon-on-slot-ok", gun.ok && gun.placeholders.Contains("equipment:middle[0]/DeckSlot_0_1")
                && gunSlot != null && gunSlot.occupiedBy == Cannon, gun.ok ? $"slot {S(gunSlot.positionM)} reservations {gun.reservations.Count}" : gun.Summary());

            var passCfg = ShipConfiguration.Long();
            passCfg.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckArea", moduleId = Cannon });
            var pass = ShipAssembler.Assemble(passCfg, lib);
            Gate("cannon-in-passage-rejected", !pass.ok && pass.HasCode("EQUIPMENT_BLOCKS_PASSAGE"), First(pass, "EQUIPMENT_BLOCKS_PASSAGE") ?? Codes(pass));

            // middle[1] (Long only ever fits real guns on middle[0]'s own
            // slots -- docs/SHIPYARD-API.md §10) so the free-placed
            // cannon here has no real gun to collide with.
            var sideCfg = ShipConfiguration.WithMiddles(2);
            sideCfg.equipment.Add(new EquipmentChoice { slotId = "middle[1]/DeckArea", moduleId = Cannon, offsetU = new Vector3(0f, 3.45f, 0f) });
            var side = ShipAssembler.Assemble(sideCfg, lib);
            Gate("cannon-free-placed-beside-passage-ok", side.ok, side.ok ? "deck area, 3.45 u to port (touches, does not enter, the passage)" : side.Summary());

            var both = ShipConfiguration.Long();
            both.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = Cannon });
            both.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckArea", moduleId = Cannon, offsetU = new Vector3(-1.5f, 3.45f, 0f) });
            Gate("equipment-overlap-rejected", ShipAssembler.Assemble(both, lib).HasCode("EQUIPMENT_OVERLAP"), "free cannon on top of the slot cannon");

            var twice = ShipConfiguration.Long();
            twice.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = Cannon });
            twice.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = Cannon });
            Gate("slot-taken-rejected", ShipAssembler.Assemble(twice, lib).HasCode("EQUIPMENT_SLOT_TAKEN"), "same slot twice");

            var nudge = ShipConfiguration.Long();
            nudge.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = Cannon, offsetU = new Vector3(0f, -1f, 0f) });
            Gate("cannon-outside-clearance-rejected", ShipAssembler.Assemble(nudge, lib).HasCode("EQUIPMENT_EXCEEDS_CLEARANCE"), "slot cannon moved 1 u inboard");

            var upGun = ShipConfiguration.Short();
            upGun.fittings.Add(new FittingChoice { socketId = "stern/UpperDeckMount", moduleId = "deck.upper.partial.placeholder" });
            upGun.equipment.Add(new EquipmentChoice { slotId = "fitting:stern/UpperDeckMount/CannonSocket_Port_1", moduleId = Cannon });
            var ug = ShipAssembler.Assemble(upGun, lib);
            // FINDING, not a wish: at 0.5 m/u the study's raised deck (Z 4.42)
            // is only 2.66 u = 1.33 m above the main deck, so its cannon
            // clearances sit inside the 1.7 m crew column over the provisional
            // main-deck passage. The rule must say so (open question in
            // docs/MODULAR-SHIPS.md), not silently allow it.
            string ugMsg = First(ug, "EQUIPMENT_BLOCKS_PASSAGE");
            Gate("upper-deck-cannon-hits-main-passage-headroom", !ug.ok && ugMsg != null && ugMsg.Contains("stern/CrewPassage_Main"),
                ugMsg ?? Codes(ug));

            var noSlot = ShipConfiguration.Short();
            noSlot.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = Cannon });
            Gate("slot-on-absent-section-rejected", ShipAssembler.Assemble(noSlot, lib).HasCode("EQUIPMENT_SLOT_UNKNOWN"), "short ship has no middle[0]");

            // ---- configuration JSON ------------------------------------
            var cfg = ShipConfiguration.WithMiddles(2);
            cfg.fittings.Add(new FittingChoice { socketId = "stern/UpperDeckMount", moduleId = "deck.upper.partial.placeholder" });
            cfg.equipment.Add(new EquipmentChoice { slotId = "middle[1]/DeckArea", moduleId = Cannon, offsetU = new Vector3(0.25f, -3.4f, 0f) });
            string json = cfg.ToJson();
            var back = ShipConfiguration.FromJson(json);
            Gate("config-round-trip", back != null && back.ValueEquals(cfg) && back.schemaVersion == ShipConfiguration.SupportedSchemaVersion && back.middleIds.Count == 2, json);

            string future = json.Substring(0, json.Length - 1) + ",\"futureField\":42,\"futureBlock\":{\"a\":[1,2,3],\"b\":\"x\"}}";
            var fut = ShipConfiguration.FromJson(future);
            Gate("config-unknown-field-ignored", fut != null && fut.ValueEquals(cfg), "extra fields futureField/futureBlock");

            var newer = ShipConfiguration.FromJson(json.Replace("\"schemaVersion\":" + ShipConfiguration.SupportedSchemaVersion, "\"schemaVersion\":99"));
            var nr = ShipAssembler.Assemble(newer, lib);
            Gate("config-schema-99-rejected", newer != null && newer.schemaVersion == 99 && !nr.ok && nr.HasCode("CONFIG_SCHEMA_TOO_NEW"),
                First(nr, "CONFIG_SCHEMA_TOO_NEW") ?? Codes(nr));

            // ---- library validation --------------------------------------
            var newLib = ModuleLibrary.FromJson(stdJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"), mods);
            var nl = ShipAssembler.Assemble(ShipConfiguration.Short(), newLib);
            Gate("library-schema-too-new-rejected", !newLib.Usable && newLib.errors.Exists(e => e.StartsWith("LIB_SCHEMA_TOO_NEW"))
                && nl.HasCode("LIBRARY_INVALID"), string.Join(" | ", newLib.errors));

            var dupList = new List<string>(mods);
            dupList.Add(Mini("hull.middle.w1r2.v3", "Middle", ""));
            var dup = ModuleLibrary.FromJson(stdJson, dupList);
            Gate("duplicate-id-rejected", !dup.Ok && dup.errors.Exists(e => e.StartsWith("LIB_DUPLICATE_ID")) && dup.All.Count == mods.Count,
                string.Join(" | ", dup.errors));

            var oddList = new List<string>(mods);
            oddList.Add(Mini("test.sail", "Sail", ""));
            oddList.Add(Mini("test.badstd", "Middle", "Z9"));
            oddList.Add(Mini("test.newer", "Middle", "", 2));
            var odd = ModuleLibrary.FromJson(stdJson, oddList);
            Gate("library-refuses-bad-modules", odd.errors.Exists(e => e.StartsWith("LIB_UNKNOWN_KIND"))
                && odd.errors.Exists(e => e.StartsWith("LIB_UNKNOWN_STANDARD"))
                && odd.errors.Exists(e => e.StartsWith("LIB_SCHEMA_TOO_NEW")) && odd.All.Count == mods.Count,
                string.Join(" | ", odd.errors));

            // ---- data hygiene -------------------------------------------
            bool unset = true; string authored = "";
            foreach (var d in lib.All)
            {
                var p = d.physical;
                if (p == null) continue;
                foreach (var a in new[] { p.massKg, p.displacementM3, p.cargoCapacity, p.crewCapacity, p.thrustCoefficient })
                    if (a != null && a.authored) { unset = false; authored += d.id + " "; }
            }
            Gate("physical-numbers-unset", unset, unset ? "no mass/capacity/thrust authored anywhere" : "authored in: " + authored);

            bool rotorsOnlyRotor = true;
            foreach (var d in lib.All)
                if (d.kind == ModuleKind.Rotor && (d.visuals == null || d.visuals.Length != 1)) rotorsOnlyRotor = false;
            Gate("rotors-are-one-part", rotorsOnlyRotor, "each rotor module has exactly one visual part (the spinning rotor)");

            if (exists != null)
            {
                var missing = new List<string>(); int n = 0;
                foreach (var d in lib.All)
                    if (d.visuals != null)
                        foreach (var v in d.visuals) { n++; if (!exists(v.resourcePath)) missing.Add(v.resourcePath); }
                Gate("visual-parts-resolve", missing.Count == 0, missing.Count == 0 ? $"{n} parts" : "missing: " + string.Join(", ", missing));
            }
        }

        static string Mini(string id, string kind, string std, int schema = 1) =>
            "{\"schemaVersion\":" + schema + ",\"id\":\"" + id + "\",\"version\":1,\"kind\":\"" + kind +
            "\",\"sockets\":[{\"id\":\"AftSocket\",\"role\":\"hull.aft\",\"standard\":\"" + std + "\",\"posU\":{\"x\":0,\"y\":0,\"z\":0}}]}";

        static bool SlotAt(AssemblyResult r, string q, out ResolvedSlot slot)
        {
            slot = null;
            foreach (var s in r.slots) if (s.qualifiedId == q) { slot = s; return true; }
            return false;
        }

        static string First(AssemblyResult r, string code)
        {
            foreach (var x in r.rejections) if (x.code == code) return x.message;
            return null;
        }

        static string Codes(AssemblyResult r)
        {
            if (r.ok) return "unexpectedly OK";
            var c = new List<string>();
            foreach (var x in r.rejections) c.Add(x.code);
            return string.Join(", ", c);
        }

        static bool OnlyRotorDiffers(AssemblyResult a, AssemblyResult b, out string why)
        {
            why = "";
            if (!a.ok || !b.ok) { why = a.Summary() + " / " + b.Summary(); return false; }
            if (a.placed.Count != b.placed.Count) { why = "placement counts differ"; return false; }
            for (int i = 0; i < a.placed.Count; i++)
            {
                var x = a.placed[i]; var y = b.placed[i];
                if (x.instanceKey != y.instanceKey || !Near(x.positionM, y.positionM)) { why = $"{x.instanceKey} moved"; return false; }
                bool sameVisual = x.visuals.Length == y.visuals.Length;
                for (int j = 0; sameVisual && j < x.visuals.Length; j++) sameVisual = x.visuals[j].resourcePath == y.visuals[j].resourcePath;
                if (x.instanceKey == "rotor")
                {
                    if (sameVisual || x.moduleId == y.moduleId) { why = "rotor visual did not change"; return false; }
                    why = $"rotor {x.visuals[0].resourcePath} -> {y.visuals[0].resourcePath} at the same {S(x.positionM)}";
                }
                else if (!sameVisual || x.moduleId != y.moduleId) { why = $"{x.instanceKey} changed too"; return false; }
            }
            return true;
        }
    }
}
