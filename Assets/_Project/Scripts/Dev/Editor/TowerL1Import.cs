using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The approved level 1 watchtower (2026-09-28),
    /// `art-staging/watchtower-chunky-lvl1-v5`.**
    ///
    /// Puts the prepared FBX (`Art/TowerL1/Models/watchtower-lvl1.fbx`)
    /// behind the existing `Resources/Settlement/watchtower_astra.prefab`
    /// wrapper. The wrapper keeps its path and GUID, so `BuildPlan.Watchtower`
    /// and every saved tower pick the new art up with no code change.
    ///
    /// - The model lives OUTSIDE `Art/AstraPlaytest`, so
    ///   `AstraPlaytestImport.Execute` (which flattens every material slot of
    ///   every FBX under that folder) never sees it.
    /// - Materials are remapped BY NAME STEM (the FBX's names carry Blender
    ///   suffixes, `SS_TowerL1_Wood.010`), never by submesh index, onto
    ///   `SeaSick/Environment Toon Textured`: colour map * `GameColor` *
    ///   `_BaseColor`. Wood and hemp reuse the level 1 wall's two textures;
    ///   peeled/stone has no map (vertex colour only).
    /// - The wrapper frame is measured from the imported markers, not
    ///   assumed: +Y up (the deck anchor sits straight above the root), +Z
    ///   the ladder side (Ladder_Bottom's horizontal offset), ground-centred.
    ///   The marker positions are then checked against the spec and the
    ///   import throws if any is off.
    ///
    /// Idempotent: run it again after re-exporting the FBX.
    public static class TowerL1Import
    {
        const string Root = "Assets/_Project/Art/TowerL1";
        const string Model = Root + "/Models/watchtower-lvl1.fbx";
        const string WallTextures = "Assets/_Project/Art/WallL1/Textures";
        const string Wrapper = "Assets/_Project/Resources/Settlement/watchtower_astra.prefab";
        const string ShaderName = "SeaSick/Environment Toon Textured";

        /// Spec positions in the wrapper frame (Blender (x, y, z) -> Unity
        /// (x, z, -y): Blender's -Y front becomes the wrapper's +Z).
        static readonly Dictionary<string, Vector3> Markers = new Dictionary<string, Vector3>
        {
            { "Ladder_Bottom", new Vector3(0f, 0.05f, 1.23f) },
            { "Ladder_Top", new Vector3(0f, 4.61f, 1.10f) },
            { "Lookout_Anchor", new Vector3(0f, 4.61f, 0f) },
        };

        [MenuItem("SeaSick/Art/Import level 1 watchtower (chunky V5)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mats = Materials(log);
            Import(mats, log);
            var frame = MeasureFrame(log);
            Wrap(frame, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[TowerL1] " + log);
            return log.ToString();
        }

        // --- materials -----------------------------------------------------------

        static Dictionary<string, Material> Materials(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
            // Already imported by WallL1Import with the approved settings
            // (sRGB, repeat, mips, 512, compressed): shared, not copied.
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>(WallTextures + "/wood-tile-512.png");
            var rope = AssetDatabase.LoadAssetAtPath<Texture2D>(WallTextures + "/rope-tile-512.png");
            if (wood == null || rope == null) throw new System.Exception("missing the level 1 wall textures in " + WallTextures);
            var set = new Dictionary<string, Material>
            {
                { "SS_TowerL1_Wood", Mat("SS_TowerL1_Wood", shader, wood) },
                { "SS_TowerL1_Hemp", Mat("SS_TowerL1_Hemp", shader, rope) },
                { "SS_TowerL1_Peeled_Stone", Mat("SS_TowerL1_Peeled_Stone", shader, null) },
            };
            log.AppendLine("materials: " + string.Join(", ", set.Keys));
            return set;
        }

        static Material Mat(string name, Shader shader, Texture2D map)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", map);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Ambient", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static string Stem(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 && name.Substring(dot + 1).All(char.IsDigit) ? name.Substring(0, dot) : name;
        }

        // --- the model -------------------------------------------------------------

        static void Import(Dictionary<string, Material> mats, StringBuilder log)
        {
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport);
            var mi = (ModelImporter)AssetImporter.GetAtPath(Model);
            if (mi == null) throw new System.Exception("no model at " + Model);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;  // flat authored normals
            mi.importTangents = ModelImporterTangents.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;

            // The FBX's own material names (with whatever Blender suffix the
            // current export carries): drop every remap, import once bare,
            // and read the embedded materials' names back.
            foreach (var id in mi.GetExternalObjectMap().Keys.ToList())
                if (id.type == typeof(Material)) mi.RemoveRemap(id);
            mi.SaveAndReimport();
            var source = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Material>().Select(m => m.name).Distinct().ToList();
            log.AppendLine("fbx materials: " + string.Join(", ", source));

            foreach (var name in source)
            {
                if (!mats.TryGetValue(Stem(name), out var target))
                    throw new System.Exception($"{Model}: material '{name}' has no SS_TowerL1_* match");
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), target);
            }
            mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || !mats.ContainsValue(m))
                        throw new System.Exception($"{Model}: {r.name} has an unmapped material {(m ? m.name : "null")}");
        }

        static Transform Find(Transform root, string stem)
            => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => Stem(t.name) == stem)
               ?? throw new System.Exception(root.name + ": no " + stem + " marker");

        /// The rotation that takes the imported model's axes to the wrapper
        /// frame: up toward the deck anchor (straight above the ground-centred
        /// origin), forward toward the ladder's foot.
        static Quaternion MeasureFrame(StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            try
            {
                go.transform.position = Vector3.zero;
                var anchor = Find(go.transform, "Lookout_Anchor").position;
                var bottom = Find(go.transform, "Ladder_Bottom").position;
                Vector3 up = anchor.normalized;
                Vector3 front = Vector3.ProjectOnPlane(bottom, up).normalized;
                var q = Quaternion.Inverse(Quaternion.LookRotation(front, up));
                log.AppendLine($"frame: imported up {up:F3} front {front:F3}");
                return q;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // --- the wrapper -----------------------------------------------------------

        static void Wrap(Quaternion frame, StringBuilder log)
        {
            string guid = AssetDatabase.AssetPathToGUID(Wrapper);
            if (string.IsNullOrEmpty(guid)) throw new System.Exception("missing wrapper " + Wrapper);

            var contents = PrefabUtility.LoadPrefabContents(Wrapper);
            try
            {
                var root = contents.transform;
                if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity)
                    throw new System.Exception("wrapper root is not at unit scale/identity");
                // The wrapper root carries no components of its own beyond
                // its Transform (checked, so a future gameplay component is
                // never silently dropped); only the visual child is replaced.
                var extra = contents.GetComponents<Component>().Where(c => !(c is Transform)).ToList();
                if (extra.Count > 0)
                    throw new System.Exception("wrapper root has components this import does not expect: "
                        + string.Join(", ", extra.Select(c => c.GetType().Name)));
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                // The model root keeps its own hierarchy (six meshes + three
                // markers) under a `Watchtower` node, as the old wrapper did.
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
                model.name = "Watchtower";
                model.transform.SetPositionAndRotation(Vector3.zero, frame * model.transform.rotation);
                model.transform.SetParent(root, false);
                // Markers under their exact runtime names (the runtime matches
                // on the stem anyway; exact names read better in a search).
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (Markers.ContainsKey(Stem(t.name))) t.name = Stem(t.name);

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                foreach (var kv in Markers)
                {
                    var p = root.InverseTransformPoint(Find(root, kv.Key).position);
                    log.AppendLine($"{kv.Key}: {p:F3} (want {kv.Value:F3})");
                    if ((p - kv.Value).magnitude > 0.01f)
                        throw new System.Exception($"{kv.Key} at {p:F3}, spec {kv.Value:F3}: the wrapper frame is off");
                }

                var b = Bounds(root);
                var rs = root.GetComponentsInChildren<MeshRenderer>(true);
                int subs = rs.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.subMeshCount);
                var used = rs.SelectMany(r => r.sharedMaterials).Distinct().Count();
                log.AppendLine($"bounds {b.min:F3}..{b.max:F3} size {b.size:F3}; tris {Tris(root)}; renderers {rs.Length}, submeshes {subs}, materials {used}");
                if (Mathf.Abs(b.max.y - 5.37f) > 0.02f || Mathf.Abs(b.min.y + 0.12f) > 0.02f)
                    throw new System.Exception($"height {b.min.y:F3}..{b.max.y:F3}, spec -0.12..5.37: the scale is off");
                if (b.size.x > 2.6f || b.size.z > 2.6f)
                    throw new System.Exception($"footprint {b.size.x:F2} x {b.size.z:F2} exceeds the 2.6 x 2.6 plot");

                PrefabUtility.SaveAsPrefabAsset(contents, Wrapper);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(Wrapper) != guid)
                throw new System.Exception("wrapper GUID changed");
        }

        static Bounds Bounds(Transform root)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static int Tris(Transform root)
            => root.GetComponentsInChildren<MeshFilter>(true).Sum(f =>
            {
                long n = 0;
                for (int i = 0; i < f.sharedMesh.subMeshCount; i++) n += f.sharedMesh.GetIndexCount(i);
                return (int)(n / 3);
            });
    }
}
