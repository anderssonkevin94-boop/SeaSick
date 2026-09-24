using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Astra's per-module station table (schemaVersion 1): upright, level
    /// flotation of `Hull_Shell` only, module-local Blender axes, heights
    /// ABOVE THE MODULE DATUM (not draft, not sea level). Only the fields the
    /// shipyard uses are declared; the per-station area arrays are kept for
    /// the next step (see docs/SHIPYARD-API.md) and ignored for now.
    [Serializable]
    public class HydroTable
    {
        public const int SupportedSchemaVersion = 1;
        public int schemaVersion;
        public string module;
        public string sourceGeometrySha256;
        public float[] validWaterlineZU;
        public float[] hullXRangeU;
        public float[] waterlineZU;
        public float[] integratedVolumeU3;

        public float KeelZU => validWaterlineZU != null && validWaterlineZU.Length > 0 ? validWaterlineZU[0] : waterlineZU[0];
        public float DeckZU => validWaterlineZU != null && validWaterlineZU.Length > 1 ? validWaterlineZU[1] : waterlineZU[waterlineZU.Length - 1];

        public bool Valid(out string why)
        {
            why = null;
            if (schemaVersion > SupportedSchemaVersion) { why = $"schema {schemaVersion} is newer than {SupportedSchemaVersion}"; return false; }
            if (waterlineZU == null || integratedVolumeU3 == null || waterlineZU.Length < 2 || waterlineZU.Length != integratedVolumeU3.Length)
            { why = "waterline and volume arrays missing or of different length"; return false; }
            for (int i = 1; i < waterlineZU.Length; i++)
                if (waterlineZU[i] <= waterlineZU[i - 1] || integratedVolumeU3[i] < integratedVolumeU3[i - 1])
                { why = "waterlines not ascending or volume not monotonic"; return false; }
            return true;
        }

        /// Immersed volume, U^3, with the water at module-local height `z`.
        /// 0 at or below the keel; linear between samples (approximate);
        /// NaN above the valid range -- never extrapolated or clamped.
        public float VolumeAt(float z)
        {
            if (!(z > waterlineZU[0])) return 0f;
            if (z > DeckZU + 1e-5f) return float.NaN;
            int last = waterlineZU.Length - 1;
            if (z >= waterlineZU[last]) return integratedVolumeU3[last];
            int i = 0;
            while (i < last - 1 && z >= waterlineZU[i + 1]) i++;
            float t = (z - waterlineZU[i]) / (waterlineZU[i + 1] - waterlineZU[i]);
            return Mathf.Lerp(integratedVolumeU3[i], integratedVolumeU3[i + 1], t);
        }
    }

    /// The assembled hull's upright hydrostatics, summed over its installed
    /// sections at one SHIP-LOCAL waterline (A3/H2). Pure.
    public class AssemblyHydrostatics
    {
        public readonly List<(string key, HydroTable table, float offsetZU)> sections = new List<(string, HydroTable, float)>();
        public float metresPerUnit;
        public float lightshipKg;
        public bool lightshipProvisional;
        public string missing;

        public bool Ok => missing == null && sections.Count > 0;
        /// Ship-frame keel (lowest section keel) and deck limit (lowest deck), U.
        public float KeelZU { get { float k = float.MaxValue; foreach (var s in sections) k = Mathf.Min(k, s.table.KeelZU + s.offsetZU); return k; } }
        public float DeckZU { get { float d = float.MaxValue; foreach (var s in sections) d = Mathf.Min(d, s.table.DeckZU + s.offsetZU); return d; } }

        /// m^3 at ship-local waterline z (U). NaN above the deck limit.
        public float VolumeM3At(float zU)
        {
            float v = 0f;
            foreach (var s in sections) v += s.table.VolumeAt(zU - s.offsetZU);
            return v * metresPerUnit * metresPerUnit * metresPerUnit;
        }

        public float DeckVolumeM3 => VolumeM3At(DeckZU);

        /// The waterline (U, ship-local) at which she displaces `massKg`.
        /// False when it would be above the deck limit (OVERLOADED) or the
        /// mass is not a finite positive number.
        public bool SolveWaterline(float massKg, float density, out float zU, out float draftM)
        {
            zU = float.NaN; draftM = float.NaN;
            if (!Ok || !(massKg >= 0f) || float.IsInfinity(massKg)) return false;
            float need = massKg / density;
            float lo = KeelZU, hi = DeckZU;
            if (need > VolumeM3At(hi)) return false;
            if (need <= 0f) { zU = lo; draftM = 0f; return true; }
            for (int i = 0; i < 50; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (VolumeM3At(mid) < need) lo = mid; else hi = mid;
            }
            zU = 0.5f * (lo + hi);
            draftM = (zU - KeelZU) * metresPerUnit;
            return true;
        }

        public static AssemblyHydrostatics For(AssemblyResult asm, ModuleLibrary lib)
        {
            var h = new AssemblyHydrostatics { metresPerUnit = asm != null ? asm.metresPerUnit : 0f };
            if (asm == null || !asm.ok || lib == null) { h.missing = "no assembly"; return h; }
            foreach (var p in asm.placed)
            {
                if (!ModuleKind.IsHull(p.kind) || !lib.TryGet(p.moduleId, out var d)) continue;
                var t = lib.Hydrostatics(d.id);
                if (t == null) { h.missing = $"{ModuleLibrary.Name(d)} has no hydrostatic table"; continue; }
                h.sections.Add((p.instanceKey, t, d.hydrostatics != null ? d.hydrostatics.offsetZU : 0f));
                if (d.lightship != null) { h.lightshipKg += d.lightship.massKg; h.lightshipProvisional |= d.lightship.provisional; }
            }
            return h;
        }
    }
}
