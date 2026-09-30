using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Astra's worker tools v1 (Kevin approved 2026-09-30):** the seven
    /// meshes that replaced the stick-and-block tool placeholders
    /// (`art-staging/worker-tools-v1`, installed by `Dev/ToolKitImport`).
    ///
    /// Each FBX is ONE mesh in the tool frame, true metres, no animation:
    /// origin at the middle of the fist, +Y up the haft, +Z the working face,
    /// X the swing axis. So a tool is built as a bare MeshFilter +
    /// MeshRenderer at IDENTITY under a parent that is at unit scale (the
    /// villager's root, or the spear's root) and the procedural posing
    /// (`VillagerActing.PoseTool`, `HunterProps.PlaceSpear`) places it
    /// exactly as it placed the primitives. Nothing here scales, offsets or
    /// rotates the mesh: the reach and face numbers in those two files are
    /// the ones the mesh was authored to.
    ///
    /// The mesh and the shared `Art/Kits/Shared/GameColor` material are
    /// loaded once into statics, from the FBX under `Resources/Kits/Tools`
    /// (the material rides along as the FBX's dependency, so it is in the
    /// build). If any tool fails to load, `Attach` returns false and the
    /// caller falls back to its old primitive tool.
    public static class ToolKit
    {
        public const string Axe = "Axe", Hammer = "Hammer", Saw = "Saw", Hoe = "Hoe",
            StirPaddle = "StirPaddle", SpearStone = "SpearStone", SpearIron = "SpearIron";

        const string Dir = "Kits/Tools/";

        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly HashSet<string> failed = new HashSet<string>();
        static Material shared;

        /// The tool's mesh, or null (logged once) if its FBX is missing.
        public static Mesh MeshOf(string name)
        {
            if (meshes.TryGetValue(name, out var cached) && cached != null) return cached;
            if (failed.Contains(name)) return null;

            var go = Resources.Load<GameObject>(Dir + name);
            MeshFilter mf = go != null ? go.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf == null || mf.sharedMesh == null)
            {
                failed.Add(name);
                Debug.LogWarning("[ToolKit] no mesh at Resources/" + Dir + name + ": using the primitive tool");
                return null;
            }
            if (shared == null)
            {
                var mr = go.GetComponentInChildren<MeshRenderer>(true);
                if (mr != null && mr.sharedMaterial != null) shared = mr.sharedMaterial;
            }
            meshes[name] = mf.sharedMesh;
            return mf.sharedMesh;
        }

        /// The one material every tool shares (`GameColor`: the shader
        /// `SeaSick/Environment Toon` reading the FBX's vertex colours).
        static Material Shared()
        {
            if (shared != null) return shared;
            // The FBX carried no material (unmapped import): the same shader,
            // white base, so the vertex colours still show.
            var shader = Shader.Find("SeaSick/Environment Toon");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            shared = new Material(shader);
            shared.SetColor("_BaseColor", Color.white);
            return shared;
        }

        /// Hang the tool's mesh under `parent` (unit scale, identity local
        /// pose). False if the mesh could not be loaded; nothing is added.
        public static bool Attach(string name, Transform parent, bool castShadows)
        {
            Mesh mesh = MeshOf(name);
            if (mesh == null) return false;

            var go = new GameObject("Mesh_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Shared();
            r.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            return true;
        }
    }
}
