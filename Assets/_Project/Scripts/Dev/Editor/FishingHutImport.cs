using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Astra's fishing hut into `Resources/Settlement` (2026-09-27).**
    ///
    /// The kit (art-staging/fishing-hut-astra-lvl1-v1, the all-states FBX)
    /// is copied to `Art/AstraPlaytest/FishingHut/FishingHut.fbx` with its
    /// material remapped to `Astra_Building` in the .meta, exactly as
    /// `AstraPlaytestImport.Import` leaves the other kits. What the game
    /// loads is the wrapper `Resources/Settlement/fishinghut_astra.prefab`
    /// (`BuildPlans.FishingHut.prefab`), which is built HERE rather than by
    /// hand because a prefab names the FBX's meshes by import-time file ids.
    ///
    /// Built once, automatically, the first time the editor loads scripts
    /// with the FBX imported and no wrapper on disk (nothing happens while
    /// playing or compiling -- it waits). The menu item rebuilds it. Until
    /// it exists the plan still raises: `BuildingFactory.Dress` falls back
    /// to an extruded hut and warns once.
    ///
    /// The wrapper is the unpacked model with every stock/work visual
    /// hidden (the README's rule: `Output_Fish_01..04` and `Work_Catch` off
    /// until the ledger says otherwise -- `StationStockView` shows the rack
    /// and the bench from real stock). The empty crate and the markers
    /// (`Worker_Stand`, `Catch_Anchor`, `Output_Anchor`, `Shore_Direction`)
    /// stay as authored.
    public static class FishingHutImport
    {
        public const string Fbx = "Assets/_Project/Art/AstraPlaytest/FishingHut/FishingHut.fbx";
        public const string Wrapper = "Assets/_Project/Resources/Settlement/fishinghut_astra.prefab";

        [InitializeOnLoadMethod]
        static void Schedule() { EditorApplication.delayCall += BuildIfMissing; }

        static void BuildIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Wrapper) != null) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += BuildIfMissing; return; }
            Debug.Log("[FishingHut] " + Build());
        }

        [MenuItem("SeaSick/Art/Build fishing hut wrapper")]
        static void BuildMenu() => Debug.Log("[FishingHut] " + Build());

        /// Build (or rebuild) the wrapper. Returns a one-paragraph report;
        /// callable from `unity cmd eval` too.
        public static string Build()
        {
            var sb = new StringBuilder();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (asset == null) return "FAIL: no model at " + Fbx;

            var root = new GameObject("fishinghut_astra");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                model.transform.SetParent(root.transform, false);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                int hidden = 0;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    string stem = Stem(t.name);
                    if (stem.StartsWith("Output_Fish_", System.StringComparison.Ordinal) || stem == "Work_Catch")
                    {
                        t.gameObject.SetActive(false);
                        hidden++;
                    }
                }
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

                var rs = root.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) return "FAIL: the model has no renderers";
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var plan = SeaSick.World.BuildPlans.FishingHut;
                sb.Append($"bounds {b.size.x:0.00} x {b.size.z:0.00} x {b.size.y:0.00} m (centre {b.center.x:0.00}, {b.center.z:0.00}) ")
                  .Append($"vs plan {plan.footprint.x} x {plan.footprint.y} x {plan.ridge}; ");
                if (b.size.x > plan.footprint.x || b.size.z > plan.footprint.y || b.size.y > plan.ridge + 0.1f)
                    sb.Append("WARNING: model is bigger than the plan -- ");

                int markers = 0;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    switch (Stem(t.name))
                    {
                        case "Worker_Stand": case "Catch_Anchor": case "Output_Anchor": case "Shore_Direction":
                            markers++; break;
                    }
                sb.Append($"{hidden} stock/work visuals hidden, {markers}/4 markers; ");

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Wrapper));
                PrefabUtility.SaveAsPrefabAsset(root, Wrapper, out bool ok);
                sb.Append(ok ? "saved " + Wrapper : "FAIL: could not save " + Wrapper);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return sb.ToString();
        }

        static string Stem(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }
    }
}
