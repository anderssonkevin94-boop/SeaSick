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
                if (!ModuleKind.IsHull(pm.kind) || !lib.TryGet(pm.moduleId, out var mdef) || mdef.upperStructure == null) continue;
                var us = mdef.upperStructure;
                if (us.massKg <= 0f) continue;
                upperMassSum += us.massKg;
                float centroidM = (us.centroidZU - keelU) * metresPerUnit;
                upperMomentSum += us.massKg * centroidM;
                if (us.deckMassKg > 0f) parts.Add((us.deckMassKg, upperDeckM, beamM * beamM / 12f));
                if (us.wallMassKg > 0f)
                {
                    float wallM = (us.wallAreaCentroidZU - keelU) * metresPerUnit;
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
