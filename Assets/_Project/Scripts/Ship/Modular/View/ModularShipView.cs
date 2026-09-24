using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Draws an AssemblyResult. Visual only: no physics, no colliders, no
    /// ShipMotor / PaddleDrive -- this is deliberately NOT the sailing ship.
    ///
    /// One child GameObject per placed module, at the module origin in game
    /// metres. Each VisualPart is loaded with Resources.Load and scaled by
    /// the library's metresPerUnit (the FBX meshes are in authoring units).
    /// Missing parts and placeholder modules become a grey box of the
    /// module's authored bounds named "PLACEHOLDER ...". The rotor hangs
    /// under `RotorPivot`, the only transform that may rotate (local X);
    /// the carrier is static.
    public class ModularShipView : MonoBehaviour
    {
        [Tooltip("Extra rotation applied to every imported FBX part, on top of the rotation Unity's importer gave the model root. " +
                 "Identity until the first import has been inspected: the V8 export convention already maps Blender (x,y,z) to game (-y,z,x), " +
                 "so this should stay identity unless that inspection shows otherwise. Never used to hide a wrong socket.")]
        [SerializeField] Quaternion visualAxisFix = Quaternion.identity;

        [Tooltip("Optional. Empty = the shared vertex-colour material every Astra kit uses (IslandScenery.SceneryMaterial).")]
        [SerializeField] Material materialOverride;

        public Transform RotorPivot { get; private set; }
        public AssemblyResult Current { get; private set; }
        /// Resource paths that failed to load in the last Build.
        public readonly List<string> missingParts = new List<string>();

        static Material placeholderMaterial;

        public void Build(AssemblyResult result)
        {
            Clear();
            if (result == null || !result.ok) return;
            Current = result;
            float k = result.metresPerUnit;
            var mat = materialOverride != null ? materialOverride : SeaSick.Terrain.IslandScenery.SceneryMaterial();

            foreach (var p in result.placed)
            {
                var go = new GameObject($"{p.instanceKey} ({p.moduleId})");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = p.positionM;
                go.transform.localRotation = p.rotation;
                Transform host = go.transform;
                if (p.kind == ModuleKind.Rotor)
                {
                    RotorPivot = new GameObject("RotorPivot").transform;
                    RotorPivot.SetParent(go.transform, false);
                    host = RotorPivot;
                }

                bool drewAny = false;
                if (!p.placeholder && p.visuals != null)
                    foreach (var part in p.visuals)
                    {
                        if (part == null || part.placeholder || string.IsNullOrEmpty(part.resourcePath)) continue;
                        var prefab = Resources.Load<GameObject>(part.resourcePath);
                        if (prefab == null) { missingParts.Add(part.resourcePath); continue; }
                        var inst = Instantiate(prefab, host, false);
                        inst.name = string.IsNullOrEmpty(part.id) ? prefab.name : part.id;
                        // Keep whatever root rotation/scale the importer baked;
                        // only the authoring->metre factor is added, plus this
                        // part's own offset (multi-part visuals only; zero for
                        // every single-piece visual, unchanged from before).
                        inst.transform.localPosition = ModularScale.AuthoringToGame(part.localPositionU, k);
                        inst.transform.localRotation = visualAxisFix * prefab.transform.localRotation;
                        inst.transform.localScale = prefab.transform.localScale * k;
                        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        {
                            var mats = r.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                            if (mats.Length == 0) mats = new[] { mat };
                            r.sharedMaterials = mats;
                        }
                        drewAny = true;
                    }
                if (!drewAny) AddPlaceholderBox(host, p, k);
            }
        }

        public void Clear()
        {
            Current = null;
            RotorPivot = null;
            missingParts.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
        }

        void AddPlaceholderBox(Transform host, PlacedModule p, float k)
        {
            ModularScale.AuthoringBoxToGame(p.boundsMinU, p.boundsMaxU, k, out var mn, out var mx);
            var box = new GameObject("PLACEHOLDER " + p.moduleId);
            box.transform.SetParent(host, false);
            box.transform.localPosition = (mn + mx) * 0.5f;
            box.transform.localScale = Vector3.Max(mx - mn, Vector3.one * 0.05f);
            box.AddComponent<MeshFilter>().sharedMesh = GreyCube();
            if (placeholderMaterial == null)
                placeholderMaterial = new Material(SeaSick.Terrain.IslandScenery.SceneryMaterial()) { name = "Modular placeholder" };
            box.AddComponent<MeshRenderer>().sharedMaterial = placeholderMaterial;
        }

        static Mesh greyCube;

        /// A unit cube with grey vertex colours (the shared material draws
        /// vertex colour), built by hand so no collider comes with it.
        static Mesh GreyCube()
        {
            if (greyCube != null) return greyCube;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            var grey = new Color32(128, 128, 128, 255);
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var f in axes)
            {
                var u = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(f, u);
                int b = v.Count;
                v.Add((f - u - w) * 0.5f); v.Add((f + u - w) * 0.5f); v.Add((f + u + w) * 0.5f); v.Add((f - u + w) * 0.5f);
                for (int i = 0; i < 4; i++) { n.Add(f); c.Add(grey); }
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            greyCube = new Mesh { name = "PlaceholderCube" };
            greyCube.SetVertices(v); greyCube.SetNormals(n); greyCube.SetColors(c); greyCube.SetTriangles(t, 0);
            greyCube.RecalculateBounds();
            return greyCube;
        }
    }
}
