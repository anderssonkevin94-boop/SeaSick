using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeaSick.Ship.Modular
{
    /// One number the shipyard UI shows, current vs proposed. The backend
    /// computes it; the UI only displays it (A1).
    [Serializable]
    public class ShipyardFigure
    {
        public string id;
        public string label;
        public string unit;
        public float current;
        public float proposed;
        /// False when the backend has no model for it yet (the UI shows "--").
        public bool available = true;
        /// True when the tuning behind it is not final (the UI should mark it).
        public bool provisional;
        public string note;

        public string Format(float v) => !available ? "--"
            : v.ToString(unit == "" ? "0" : "0.##", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
        public override string ToString() => $"{label}: {Format(current)} -> {Format(proposed)}{(provisional ? " (provisional)" : "")}";
    }

    [Serializable]
    public class ShipyardNote
    {
        public string code;
        public string message;
        public override string ToString() => $"{code}: {message}";
    }

    /// Validate's rich answer for the UI (A1/A5). `blocking` is empty when
    /// the draft can be applied (subject to CanRefitNow at apply time).
    public class ShipyardReport
    {
        public bool ok;
        public readonly List<Rejection> blocking = new List<Rejection>();
        public readonly List<ShipyardNote> warnings = new List<ShipyardNote>();
        public readonly List<ShipyardFigure> figures = new List<ShipyardFigure>();
        /// Per hull section of the draft: occupancy, `canRemove`, `reason`
        /// (bind the UI's `removalBlocker` here).
        public readonly List<SectionOccupancy> sections = new List<SectionOccupancy>();

        public SectionOccupancy Section(string key)
        {
            foreach (var o in sections) if (o.sectionKey == key) return o;
            return null;
        }
        public string currentRotorId, proposedRotorId;
        /// Proposed ship as loaded: draft above the keel from the module
        /// station tables, and from the sailing model's hull form. Both
        /// PROVISIONAL while the two models differ. NaN = above the deck line.
        public float tableDraftM = float.NaN, simDraftM = float.NaN;
        public float tableDraftCurrentM = float.NaN, simDraftCurrentM = float.NaN;
        /// Why she cannot be refitted right now ("" = she can). Not a draft
        /// problem: the same draft becomes applicable when she is moored.
        public string refitNowBlockedBecause = "";

        public ShipyardFigure Figure(string id)
        {
            foreach (var f in figures) if (f.id == id) return f;
            return null;
        }

        public string Summary()
        {
            var parts = new List<string>();
            if (!ok) foreach (var b in blocking) parts.Add(b.message);
            if (!string.IsNullOrEmpty(refitNowBlockedBecause)) parts.Add(refitNowBlockedBecause);
            return string.Join("\n", parts);
        }

        public static ShipyardReport From(ShipyardValidation v, ShipyardPlan current, ShipyardPlan proposed, float metresPerUnit)
        {
            var r = new ShipyardReport { ok = v.ok };
            r.blocking.AddRange(v.issues);
            r.proposedRotorId = v.rotorId ?? "";
            r.currentRotorId = current?.config?.rotorId ?? "";
            float k = metresPerUnit;
            void Add(string id, string label, string unit, Func<ShipyardPlan, float> get, bool provisional = false, string note = null)
            {
                r.figures.Add(new ShipyardFigure
                {
                    id = id, label = label, unit = unit, provisional = provisional, note = note,
                    current = current != null ? get(current) : 0f,
                    proposed = proposed != null ? get(proposed) : 0f,
                    available = current != null && proposed != null,
                });
            }
            Add("hullLength", "Hull length", "m", p => p.measure.waterlineLengthU * k);
            Add("overallLength", "Length overall", "m", p => p.assembly.overallLengthM);
            Add("beam", "Beam", "m", p => p.measure.beamU * k);
            Add("depth", "Depth", "m", p => p.measure.depthU * k);
            Add("sections", "Middle sections", "", p => p.config.middleIds?.Count ?? 0);
            r.figures.Add(new ShipyardFigure { id = "displacement", label = "Displacement (as loaded)", unit = "t", provisional = true,
                current = v.totalMassCurrentKg / 1000f, proposed = v.totalMassDraftKg / 1000f, available = current != null && proposed != null,
                note = "Lightship + cargo + crew + guns; floated on the module station tables." });
            Add("lightship", "Lightship mass", "t", p => p.lightshipKg / 1000f, true,
                "Sum of the sections' provisional lightship masses (module data).");
            r.figures.Add(new ShipyardFigure { id = "draft", label = "Draft (as loaded)", unit = "m", provisional = true,
                current = v.tableDraftCurrentM, proposed = v.tableDraftDraftM,
                available = current != null && proposed != null && !float.IsNaN(v.tableDraftCurrentM) && !float.IsNaN(v.tableDraftDraftM),
                note = "Above the keel, from module station tables (upright, level)." });
            r.figures.Add(new ShipyardFigure { id = "simDraft", label = "Draft in the sailing model (as loaded)", unit = "m", provisional = true,
                current = v.simDraftCurrentM, proposed = v.simDraftDraftM,
                available = current != null && proposed != null && !float.IsNaN(v.simDraftCurrentM) && !float.IsNaN(v.simDraftDraftM),
                note = "Same mass floated on the reshaped reference hull the physics sails on (approximation). Differs from the table draft while the two models differ." });
            r.tableDraftM = v.tableDraftDraftM; r.simDraftM = v.simDraftDraftM;
            r.tableDraftCurrentM = v.tableDraftCurrentM; r.simDraftCurrentM = v.simDraftCurrentM;
            Add("loadLine", "Displacement at load line", "t", p => p.loadDisplacementKg / 1000f, true,
                "Load line = a freeboard margin below the deck, anchored on the standard ship's full load.");
            Add("holdCells", "Hold (volume)", "", p => p.capacity.holdCells, true,
                "Enclosed volume below deck, anchored to 16 on the standard ship.");
            r.figures.Add(new ShipyardFigure { id = "weightAllowance", label = "Cargo weight allowance", unit = "t", provisional = true,
                current = v.weightAllowanceCurrentKg / 1000f, proposed = v.weightAllowanceDraftKg / 1000f,
                available = current != null && proposed != null,
                note = "Load-line displacement - lightship - crew - guns (ShipLoad weights), from the module station tables. Checked, NOT yet felt: cargo mass does not reach the steamer's physics." });
            r.figures.Add(new ShipyardFigure { id = "cargoWeight", label = "Cargo aboard", unit = "t", provisional = true,
                current = v.cargoKg / 1000f, proposed = v.cargoKg / 1000f });
            r.sections.AddRange(v.sections);
            Add("crewBerths", "Crew berths", "", p => p.capacity.crewStations, true,
                "Deck stations along the crew strip, anchored to 8 on the standard ship.");
            Add("gunSlots", "Usable gun/equipment slots", "", p => 0f, false,
                "No deck equipment can be fitted in the prototype.");
            Add("deckSlots", "Deck slots (reserved)", "", p => p.capacity.equipmentSlots, true);
            Add("guns", "Guns carried", "", p => p.capacity.guns);
            if (proposed != null && current != null && proposed.capacity.holdCells < current.capacity.holdCells)
                r.warnings.Add(new ShipyardNote { code = "HOLD_SMALLER", message = $"The hold shrinks from {current.capacity.holdCells} to {proposed.capacity.holdCells}." });
            if (proposed != null && current != null && proposed.capacity.crewStations < current.capacity.crewStations)
                r.warnings.Add(new ShipyardNote { code = "FEWER_BERTHS", message = $"Crew berths drop from {current.capacity.crewStations} to {proposed.capacity.crewStations}." });
            r.warnings.Add(new ShipyardNote { code = "PROVISIONAL_TUNING", message = "Capacity and hydrostatics are provisional and will be retuned." });
            return r;
        }
    }
}
