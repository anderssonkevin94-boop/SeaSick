using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Raised-deck (docs/RAISED-DECK.md sec 6) physics: finding whether an
    /// assembly is raised, and raising the reshaped reference hull's CoM, GM
    /// and roll gyradius by the installed raised sections' own upper-structure
    /// mass moment. Pure; only ever mutates the ONE `ShipyardPlan.data` it is
    /// handed, never the reference hull or the library.
    public static class RaisedDeckPhysics
    {
        /// The join profile of whichever raised family (e.g. W1xR) is
        /// actually installed, if any -- both a placed hull section naming
        /// that family AND standards.json carrying a real `upperDeckZU` for
        /// it (> deckZU) must hold before anything here fires. Null = a
        /// single-deck assembly, or the profile has no raised deck authored.
        public static JoinProfile FindRaisedProfile(AssemblyResult asm, ModuleLibrary lib)
        {
            if (asm == null || !asm.ok || lib == null) return null;
            foreach (var pm in asm.placed)
            {
                if (!ModuleKind.IsHull(pm.kind) || !lib.TryGet(pm.moduleId, out var d)) continue;
                if (string.IsNullOrEmpty(d.family)) continue;
                var prof = lib.FindProfile(d.family);
                if (prof != null && prof.upperDeckZU > prof.deckZU + 1e-4f) return prof;
            }
            return null;
        }

        /// One raised hull section's own extent along the ship, in
        /// `ShipyardPlan.data`'s own frame (ship-frame metres + viewOffset,
        /// the same conversion the funnel bounds use), and the family
        /// profile that raised it.
        public struct RaisedSectionRange { public float fromZ, toZ; public JoinProfile profile; }

        /// Every placed hull section that resolves a raised profile
        /// (docs/RAISED-SECTIONS.md sec 6), generalising `FindRaisedProfile`
        /// (which only ever returned the first one, correct only when a
        /// raised ship was all-or-nothing) to one range per section, so a
        /// mixed ship (e.g. raised stern + low middle + low bow) raises only
        /// the stations that actually belong to its raised section(s).
        public static List<RaisedSectionRange> FindRaisedSectionRanges(AssemblyResult asm, ModuleLibrary lib, float metresPerUnit, float viewOffsetZ)
        {
            var list = new List<RaisedSectionRange>();
            if (asm == null || !asm.ok || lib == null) return list;
            foreach (var pm in asm.placed)
            {
                if (!ModuleKind.IsHull(pm.kind) || !lib.TryGet(pm.moduleId, out var d) || string.IsNullOrEmpty(d.family)) continue;
                var prof = lib.FindProfile(d.family);
                if (prof == null || prof.upperDeckZU <= prof.deckZU + 1e-4f) continue;
                float fromZ = viewOffsetZ + pm.positionM.z + pm.boundsMinU.x * metresPerUnit;
                float toZ = viewOffsetZ + pm.positionM.z + pm.boundsMaxU.x * metresPerUnit;
                if (toZ < fromZ) (fromZ, toZ) = (toZ, fromZ);
                list.Add(new RaisedSectionRange { fromZ = fromZ, toZ = toZ, profile = prof });
            }
            return list;
        }

        /// The join profile of the first installed hull section with a
        /// known family (any family, raised or not) -- the keel datum for
        /// `RaiseCoM` on a single-deck ship carrying an upper-deck layer.
        public static JoinProfile AnyHullProfile(AssemblyResult asm, ModuleLibrary lib)
        {
            if (asm == null || !asm.ok || lib == null) return null;
            foreach (var pm in asm.placed)
                if (ModuleKind.IsHull(pm.kind) && lib.TryGet(pm.moduleId, out var d) && !string.IsNullOrEmpty(d.family))
                {
                    var prof = lib.FindProfile(d.family);
                    if (prof != null) return prof;
                }
            return null;
        }

        /// deckZU of whichever hull family is actually installed (every
        /// non-raised family shares 1.76 today, W1-r2 and W1x alike);
        /// 1.76 if the assembly did not resolve to any known family.
        public static float NonRaisedDeckZU(AssemblyResult asm, ModuleLibrary lib)
        {
            if (asm != null && asm.ok && lib != null)
                foreach (var pm in asm.placed)
                    if (ModuleKind.IsHull(pm.kind) && lib.TryGet(pm.moduleId, out var d) && !string.IsNullOrEmpty(d.family))
                    {
                        var prof = lib.FindProfile(d.family);
                        if (prof != null) return prof.deckZU;
                    }
            return 1.76f;
        }

        /// Raises `plan.data.kg/com.y/gm/gyradiusRoll` by the installed raised
        /// sections' own `upperStructure` block. `Reshaped` (sDepth == 1 on a
        /// raised hull, docs/RAISED-DECK.md sec 6) leaves kg/com exactly where
        /// the single-deck reference had them -- the sim has not yet been
        /// told the extra mass (already summed into `plan.lightshipKg`, since
        /// every hull.*.w1xr.v1.json's own `lightship.massKg` already
        /// includes its `upperStructure.massKg` once) stands high above the
        /// old deck. This splits the total back into "the rest of her" (at
        /// her un-raised kg) and the upper structure (at ITS OWN height) and
        /// re-derives kg/GM/roll gyradius as a mass-weighted combination, per
        /// docs/RAISED-DECK.md sec 6's formulas. No-op if nothing raised is
        /// installed (`raised` null) or no placed module carries the block.
        public static void RaiseCoM(ShipyardPlan p, AssemblyResult asm, ModuleLibrary lib, JoinProfile raised, float metresPerUnit)
        {
            if (p?.data == null || asm == null || lib == null || raised == null) return;
            var d = p.data;
            float keelU = raised.keelZU;
            float upperDeckM = (raised.upperDeckZU - keelU) * metresPerUnit;
            float beamM = d.beam;

            float upperMassSum = 0f, upperMomentSum = 0f; // moment = mass-metres above the keel
            // Deck cap / topside-wall breakdown for the roll gyradius's
            // parallel-axis sum, gathered in the same pass.
            var parts = new List<(float massKg, float heightAboveKeelM, float rSquared)>();

            foreach (var pm in asm.placed)
            {
                // Hull sections AND upper-deck layers (2026-09-27): every
                // Z in an upperStructure block is module-local, so the placed
                // origin's own Z is added (0 for a hull section).
                if (!(ModuleKind.IsHull(pm.kind) || pm.kind == ModuleKind.UpperDeck)
                    || !lib.TryGet(pm.moduleId, out var mdef) || mdef.upperStructure == null) continue;
                var us = mdef.upperStructure;
                if (us.massKg <= 0f) continue;
                float oz = pm.positionU.z;
                upperMassSum += us.massKg;
                float centroidM = (us.centroidZU + oz - keelU) * metresPerUnit;
                upperMomentSum += us.massKg * centroidM;
                float deckPlateM = us.deckZU > 0f ? (us.deckZU + oz - keelU) * metresPerUnit : upperDeckM;
                if (us.deckMassKg > 0f) parts.Add((us.deckMassKg, deckPlateM, beamM * beamM / 12f));
                if (us.wallMassKg > 0f)
                {
                    float wallM = (us.wallAreaCentroidZU + oz - keelU) * metresPerUnit;
                    parts.Add((us.wallMassKg, wallM, (beamM * 0.5f) * (beamM * 0.5f)));
                }
            }
            if (upperMassSum <= 0f) return;

            float totalMassKg = Mathf.Max(1f, p.lightshipKg);
            float baseMassKg = Mathf.Max(0f, totalMassKg - upperMassSum);
            float baseKgM = d.kg; // Reshaped left this at the un-raised hull's own kg (sD == 1)
            float newKgM = (baseMassKg * baseKgM + upperMomentSum) / totalMassKg;
            float delta = newKgM - baseKgM;

            d.kg = newKgM;
            d.com.y += delta;
            d.gm -= delta; // kb, bm unaffected: the underwater form is untouched by a raised deck

            // Roll gyradius, docs/RAISED-DECK.md sec 6:
            // k^2 = (M0 k0^2 + sum Mi (di^2 + ri^2)) / M
            // di = height of part i above the NEW CoM; ri^2 = B^2/12 for the
            // deck (a flat plate spanning the beam), (B/2)^2 for the walls
            // (a thin ring at the beam).
            float sumMiDiRi = 0f;
            foreach (var (massKg, heightM, rSquared) in parts)
            {
                float di = heightM - newKgM;
                sumMiDiRi += massKg * (di * di + rSquared);
            }
            float k0 = d.gyradiusRoll;
            float kRollSquared = (baseMassKg * k0 * k0 + sumMiDiRi) / totalMassKg;
            d.gyradiusRoll = Mathf.Sqrt(Mathf.Max(0f, kRollSquared));
        }
    }
}
