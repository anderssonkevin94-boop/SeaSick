using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Astra's resource kit v1 (Kevin approved 2026-09-30):** the meshes that
    /// replaced the cylinder, cube and squashed-block placeholders for Timber,
    /// Boards, Stone, Ore and Brick (`art-staging/resource-kit-v1`, installed
    /// by `Dev/ResourceKitImport`).
    ///
    /// Each family has a `_Unit` (ONE unit of a pile: true metres, bottom-centre
    /// origin, long axis along z) and a `_CarryUnit` (ONE carried item at the
    /// size the villager's arms already carried it: centre-grip origin). Every
    /// piece of runtime layout, count and ownership stays where it was --
    /// `CampPiles` (piles, cargo), `VillagerActing` (carried loads) and
    /// `BuildSite` (the delivered stack) still decide how many and where; this
    /// only supplies the one unit. The fixed `_Carry` / `_Stack` compositions
    /// are review art and are not used.
    ///
    /// Meshes and the shared `Art/Kits/Shared/GameColor` material (the shader
    /// `SeaSick/Environment Toon` reading the FBX's `GameColor` vertex colours)
    /// are loaded ONCE into statics from `Resources/Kits/Resources`, and every
    /// unit is a bare MeshFilter + MeshRenderer on that one material, so piles
    /// batch. If a mesh cannot be loaded `Spawn` returns null (logged once) and
    /// the caller draws its old primitive: nothing is ever invisible.
    ///
    /// Also loads the food kit's `Resources/Kits/Food/<Name>_Unit` for the food
    /// a villager carries (`SpawnFood`); the food PILES are `CampPiles`' own.
    public static class ResourceKit
    {
        const string Dir = "Kits/Resources/";
        const string FoodDir = "Kits/Food/";

        /// One mesh ready to instance: the mesh, the shared material, and
        /// whatever pose the FBX node carries above the mesh (identity when
        /// the importer baked the axis conversion, as it does).
        sealed class Entry
        {
            public Mesh mesh;
            public Material mat;
            public Quaternion rot;
            public Vector3 scale;
            public Vector3 size;   // footprint x, height y, depth z, metres
        }

        /// Null = "no model / failed to load", so the primitive is drawn and
        /// nothing is retried per frame.
        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        static Material fallbackMat;

        /// The kit family a resource id draws as, or null (Fine boards, tools,
        /// food and the rest keep what they had).
        public static string Family(string resource)
        {
            if (resource == Res.Timber) return "Timber";
            if (resource == Res.Boards) return "Boards";
            if (resource == Res.Stone) return "Stone";
            if (resource == Res.Ore) return "Ore";
            if (resource == Res.Brick) return "Brick";
            return null;
        }

        /// One unit of `resource` (`carry` false: the pile unit, base on
        /// y = 0; true: the carried item, centred on its grip) hung under
        /// `parent` at `localPos` / `localRot`. Null, nothing added, if the
        /// resource has no kit mesh or it did not load.
        public static GameObject Spawn(string resource, bool carry, Transform parent,
            Vector3 localPos, Quaternion localRot, float scale = 1f)
        {
            string family = Family(resource);
            if (family == null) return null;
            var e = EntryFor(Dir, family + (carry ? "_CarryUnit" : "_Unit"));
            return e == null ? null : Make(e, "Kit_" + family, parent, localPos, localRot, scale);
        }

        /// One food from the food kit (`Resources/Kits/Food/<Name>_Unit`,
        /// base origin), for a carried load. Null if that food has no model.
        public static GameObject SpawnFood(string resource, Transform parent,
            Vector3 localPos, Quaternion localRot, float scale = 1f)
        {
            if (string.IsNullOrEmpty(resource)) return null;
            var e = EntryFor(FoodDir, resource + "_Unit");
            return e == null ? null : Make(e, "Kit_" + resource, parent, localPos, localRot, scale);
        }

        /// A food unit's size (x footprint, y height, z depth), or zero if it
        /// has no model. Lets a carried load space itself by what it holds.
        public static Vector3 FoodSize(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return Vector3.zero;
            var e = EntryFor(FoodDir, resource + "_Unit");
            return e == null ? Vector3.zero : e.size;
        }

        static GameObject Make(Entry e, string name, Transform parent, Vector3 pos, Quaternion rot, float scale)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = rot * e.rot;
            t.localScale = e.scale * scale;
            go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = e.mat != null ? e.mat : Fallback();
            return go;
        }

        static Entry EntryFor(string dir, string name)
        {
            string key = dir + name;
            if (entries.TryGetValue(key, out var cached) && (cached == null || cached.mesh != null))
                return cached;

            Entry e = null;
            try
            {
                // The FBXs sit in a folder that is itself called Resources
                // (`Resources/Kits/Resources/`); Unity resolves a nested one
                // from either root, so try the full path and the bare name.
                var prefab = Resources.Load<GameObject>(key);
                if (prefab == null && dir == Dir) prefab = Resources.Load<GameObject>(name);
                var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
                if (mf != null && mf.sharedMesh != null)
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    // The node's own pose above the mesh: identity for the baked
                    // axis conversion; kept so a non-baked import still lies flat.
                    var node = mf.transform.localToWorldMatrix;
                    var scale = node.lossyScale;
                    e = new Entry
                    {
                        mesh = mf.sharedMesh,
                        mat = mr != null ? mr.sharedMaterial : null,
                        rot = node.rotation,
                        scale = scale,
                        size = Vector3.Scale(mf.sharedMesh.bounds.size, scale),
                    };
                }
                // A food with no model (dishes, flour, generic Food) is normal
                // and silent; a missing resource-kit mesh is a fault.
                else if (prefab != null || dir == Dir)
                    Debug.LogWarning($"[ResourceKit] no mesh at Resources/{key}: drawing the primitive");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ResourceKit] {key} failed to load ({ex.Message}): drawing the primitive");
                e = null;
            }
            entries[key] = e;
            return e;
        }

        /// The FBX carried no material (unmapped import): the same shader,
        /// white base, so the vertex colours still show. One instance.
        static Material Fallback()
        {
            if (fallbackMat != null) return fallbackMat;
            var shader = Shader.Find("SeaSick/Environment Toon");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            fallbackMat = new Material(shader);
            fallbackMat.SetColor("_BaseColor", Color.white);
            return fallbackMat;
        }
    }
}
