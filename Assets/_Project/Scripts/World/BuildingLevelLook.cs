using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What a building's level LOOKS like, until real art exists**
    /// (Kevin, 2026-09-26). Level 2 is already real underneath --
    /// `OutpostLedger.LevelOf`, `Economy.Techs.Upgrades`, the upgrade button
    /// on `StationSheet` -- but no kit carries a second model yet, so a
    /// levelled-up building is told apart the cheap way: multiplied gold,
    /// not a flat fill, so the walls and thatch still shade like walls and
    /// thatch and only read warmer.
    ///
    /// One row per level, added here and nowhere else -- see `For`. When a
    /// real per-level model exists, `LevelLook.prefabOverride` is the seam
    /// it plugs into: `BuildingFactory.Raise`/`Dress` would read it instead
    /// of (or ahead of) the tint, and every caller of `Apply` below stays
    /// exactly as it is, because "what does this building look like at this
    /// level" would still resolve to one place.
    public static class BuildingLevelLook
    {
        public struct LevelLook
        {
            /// Multiplied onto each material's own colour.
            public Color tint;
            /// 0 = no tint at all (level 1, and the 0 an old save reads).
            /// 1 = `tint` at full strength. Between fades it in.
            public float strength;
            /// **The seam for real per-level art (unused today).** No kit
            /// has a second model yet; the field exists so the day one does,
            /// nothing about how a level is LOOKED UP has to change, only
            /// what this returns.
            public string prefabOverride;
        }

        static readonly LevelLook None = new LevelLook { tint = Color.white, strength = 0f };

        /// Level 2's gold (~#E8B84A), Kevin's colour for "upgraded, no art
        /// yet". Multiplied at just over half strength: enough to read from
        /// across the clearing, not so much it flattens the shading.
        static readonly LevelLook Gold = new LevelLook
        {
            tint = new Color(0.91f, 0.72f, 0.29f),
            strength = 0.55f,
        };

        /// The look for `level`. Add level 3 as one more branch here --
        /// nothing else in this file, or any caller of `Apply`, changes.
        public static LevelLook For(int level) => level >= 2 ? Gold : None;

        // --- applying it, cheaply -------------------------------------------

        /// One tinted clone per (source material, level), shared by every
        /// building that reaches this level -- never a `new Material` per
        /// renderer, and never a mutation of the source itself (the source
        /// is very often shared: `BuildingFactory.Mat`'s own cache for the
        /// extruded fallback, or Astra's kit material for every hut in the
        /// camp, level 1 and level 2 alike). Keyed on the Material
        /// reference, so it survives a scene reload the same way
        /// `BuildingFactory.mats` does: a destroyed entry reads back as
        /// Unity's fake `null` and is remade rather than served stale.
        static readonly Dictionary<(Material src, int level), Material> tinted = new();

        /// What a tinted clone was cloned FROM, so re-applying a level (a
        /// second upgrade, later, or the same building raised again on
        /// load) starts from the untinted colour every time instead of
        /// compounding the multiply onto its own last result.
        static readonly Dictionary<Material, Material> sourceOfClone = new();

        /// Colour every mesh under `root` for `level`. Safe to call on a
        /// building that will never be above level 1 -- `For(1)` has zero
        /// strength, so every renderer is simply set back to its own
        /// (untinted) material, which is a no-op the first time and undoes
        /// a tint if a level ever needs to go the other way.
        public static void Apply(Transform root, int level)
        {
            if (root == null) return;
            var look = For(level);
            // **Every slot, 2026-09-28**: the textured level 1 kits (wall,
            // tower, lumber mill) carry up to four materials per renderer,
            // and `sharedMaterial` is only the first of them.
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var slots = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    var current = slots[i];
                    if (current == null) continue;
                    var source = (sourceOfClone.TryGetValue(current, out var s) && s != null) ? s : current;
                    var want = look.strength <= 0f ? source : TintOf(source, level, look);
                    if (want != current) { slots[i] = want; changed = true; }
                }
                if (changed) r.sharedMaterials = slots;
            }
        }

        static Material TintOf(Material source, int level, LevelLook look)
        {
            var key = (source, level);
            if (tinted.TryGetValue(key, out var m) && m != null) return m;

            m = new Material(source) { name = source.name + " (Lv" + level + ")" };
            var mul = Color.Lerp(Color.white, look.tint, Mathf.Clamp01(look.strength));
            TintProperty(m, "_BaseColor", mul);
            TintProperty(m, "_Color", mul);
            tinted[key] = m;
            sourceOfClone[m] = source;
            return m;
        }

        /// Handles both colour properties in play across the kits this
        /// project uses -- URP Lit and `SeaSick/Environment Toon` carry
        /// `_BaseColor`; anything still on a `_Color` shader (Standard, an
        /// older imported material) carries that instead. A material with
        /// neither is left alone rather than guessed at.
        static void TintProperty(Material m, string prop, Color mul)
        {
            if (!m.HasProperty(prop)) return;
            var c = m.GetColor(prop);
            m.SetColor(prop, new Color(c.r * mul.r, c.g * mul.g, c.b * mul.b, c.a));
        }
    }
}
