using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// **Astra ship cargo, Kevin approved 2026-09-29**: the barrels, sacks and
    /// crates `ShipCargoDisplay` stands on the coaster's deck, and which one
    /// a resource is carried in.
    ///
    /// Meshes come from `Resources/Kits/Cargo/` (installed by
    /// `Dev/ShipCargoImport`: v1 barrels and sacks, v2 crates; true metres,
    /// bottom-centre origin). Each is loaded ONCE and its material slots
    /// (wood, iron, cloth, rope -- all the shared GameColor toon material)
    /// merged into one submesh, so a prop is one draw. A prop that cannot be
    /// loaded is simply not drawn (logged once): the hold still holds it.
    public static class ShipCargoKit
    {
        public enum Kind { Crate, Sack, Barrel }

        const string Dir = "Kits/Cargo/";

        /// **Which container a resource rides in**, by its `ResDefs`
        /// category: food and drink in barrels, grain (wheat, flour) and raw
        /// bulk (timber, stone, ore, hide) in sacks, everything made
        /// (materials, gear, armour, ship stores) in crates.
        public static Kind KindOf(string resource)
        {
            if (resource == Res.Wheat || resource == Res.Flour) return Kind.Sack;
            switch (ResDefs.Category(resource))
            {
                case ResCategory.Food: return Kind.Barrel;
                case ResCategory.Raw: return Kind.Sack;
                default: return Kind.Crate;
            }
        }

        /// The model for a kind: `big` the large barrel / crate or the tall
        /// cream sack, else the small one / the ochre sack.
        public static string ModelName(Kind kind, bool big)
        {
            switch (kind)
            {
                case Kind.Barrel: return big ? "Cargo_Barrel_Large" : "Cargo_Barrel_Small";
                case Kind.Sack: return big ? "Cargo_Sack_Cream" : "Cargo_Sack_Ochre";
                default: return big ? "Cargo_Box_Large" : "Cargo_Box_Small";
            }
        }

        public sealed class Model
        {
            public Mesh mesh;
            public Material mat;
            /// Height of the top above the base, metres (what a stacked prop
            /// stands on).
            public float height;
        }

        static readonly Dictionary<string, Model> models = new Dictionary<string, Model>();

        /// Null if the model did not load (never retried per frame).
        public static Model Get(string name)
        {
            if (models.TryGetValue(name, out var cached) && (cached == null || cached.mesh != null))
                return cached;
            Model m = null;
            try
            {
                var prefab = Resources.Load<GameObject>(Dir + name);
                var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
                var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                if (mf != null && mf.sharedMesh != null && mr != null)
                {
                    var src = mf.sharedMesh;
                    var toRoot = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    Mesh mesh;
                    if (src.subMeshCount == 1 && toRoot == Matrix4x4.identity) mesh = src;
                    else
                    {
                        // One submesh, node pose folded in: one draw per prop.
                        var parts = new CombineInstance[src.subMeshCount];
                        for (int i = 0; i < parts.Length; i++)
                            parts[i] = new CombineInstance { mesh = src, subMeshIndex = i, transform = toRoot };
                        mesh = new Mesh { name = name + " (merged)" };
                        mesh.CombineMeshes(parts, true, true, false);
                        mesh.RecalculateBounds();
                        mesh.UploadMeshData(true);
                    }
                    m = new Model { mesh = mesh, mat = mr.sharedMaterial, height = mesh.bounds.max.y };
                }
                else Debug.LogWarning($"[ShipCargoKit] no mesh at Resources/{Dir}{name}: that prop is not drawn");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ShipCargoKit] {name} failed to load ({e.Message}): that prop is not drawn");
                m = null;
            }
            models[name] = m;
            return m;
        }
    }
}
