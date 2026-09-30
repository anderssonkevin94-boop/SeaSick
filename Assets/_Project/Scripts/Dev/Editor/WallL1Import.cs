using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The approved level 1 wall (2026-09-28), `art-staging/wall-textured-v3`.**
    ///
    /// Puts the seven prepared FBXs (`Art/WallL1/Models`) behind the seven
    /// existing `Resources/Palisade/*.prefab` wrappers. The wrappers keep
    /// their paths and GUIDs, so `WallVisual` and every saved wall pick the
    /// new art up with no code change; `Gate_L1` / `Gate_Breached_3m` are not
    /// touched.
    ///
    /// - The kit lives OUTSIDE `Art/AstraPlaytest`, so
    ///   `AstraPlaytestImport.Execute` (which remaps every material of every
    ///   FBX under that folder to one building material) never sees it.
    /// - Materials are remapped BY NAME (`SS_WallL1_*`), not by submesh index,
    ///   onto `SeaSick/Environment Toon Textured`: colour map * the FBX's one
    ///   vertex colour layer (`GameColor`) * `_BaseColor`.
    /// - The wrapper frame is measured from the imported markers, not
    ///   assumed: +Z along the run (Snap_Start -> Snap_End), +Y up, rails
    ///   (Blender +Y, found from the pegs, which sit only on the rails) on
    ///   -X; origin on Snap_Start, or on Post_Center for the post.
    ///
    /// Idempotent: run it again after re-exporting the FBXs.
    public static class WallL1Import
    {
        const string Root = "Assets/_Project/Art/WallL1";
        const string Models = Root + "/Models";
        const string Wrappers = "Assets/_Project/Resources/Palisade";
        const string ShaderName = "SeaSick/Environment Toon Textured";

        static readonly string[] Pieces =
        {
            "Palisade_Run_1m_A", "Palisade_Run_1m_B", "Palisade_Run_1m_C",
            "Palisade_Filler_050m", "Palisade_Filler_025m", "Palisade_Breached_1m", "Palisade_Post",
        };

        static readonly Dictionary<string, float> Length = new Dictionary<string, float>
        {
            { "Palisade_Run_1m_A", 1f }, { "Palisade_Run_1m_B", 1f }, { "Palisade_Run_1m_C", 1f },
            { "Palisade_Filler_050m", 0.5f }, { "Palisade_Filler_025m", 0.25f },
            { "Palisade_Breached_1m", 1f }, { "Palisade_Post", 0.4f },
        };

        [MenuItem("SeaSick/Art/Import level 1 wall (textured V3)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mats = Materials(log);
            foreach (var p in Pieces) Import(Models + "/" + p + ".fbx", mats, log);
            var frame = MeasureFrame(log);
            foreach (var p in Pieces) Wrap(p, frame, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[WallL1] " + log);
            return log.ToString();
        }

        // --- textures + materials --------------------------------------------

        static Texture2D Texture(string file)
        {
            string path = Root + "/Textures/" + file;
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Bilinear;   // Blender's "Linear"
            ti.maxTextureSize = 512;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Dictionary<string, Material> Materials(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
            var wood = Texture("wood-tile-512.png");
            var rope = Texture("rope-tile-512.png");
            // Blender's peeled base colour is LINEAR (0.68, 0.425, 0.195); a
            // Material colour is authored in sRGB and linearised for the
            // shader in this linear-space project, so store its gamma form.
            var peeled = new Color(0.68f, 0.425f, 0.195f, 1f).gamma;
            var set = new Dictionary<string, Material>
            {
                { "SS_WallL1_Wood", Mat("SS_WallL1_Wood", shader, wood, Color.white) },
                { "SS_WallL1_Rope", Mat("SS_WallL1_Rope", shader, rope, Color.white) },
                { "SS_WallL1_Pegs", Mat("SS_WallL1_Pegs", shader, null, Color.white) },
                { "SS_WallL1_Peeled", Mat("SS_WallL1_Peeled", shader, null, peeled) },
            };
            log.AppendLine("materials: " + string.Join(", ", set.Keys));
            return set;
        }

        static Material Mat(string name, Shader shader, Texture2D map, Color tint)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", map);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Ambient", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // --- models ------------------------------------------------------------

        static void Import(string path, Dictionary<string, Material> mats, StringBuilder log)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
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
            // Read/Write ON since 2026-09-30: `WallVisual` merges the pieces
            // into one mesh per segment at runtime, which a player build can
            // only do from a CPU-readable mesh.
            mi.isReadable = true;
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || !mats.ContainsValue(m))
                        throw new System.Exception($"{path}: {r.name} has an unmapped material {(m ? m.name : "null")}");
        }

        // --- the wrapper frame -------------------------------------------------

        /// The rotation that takes the imported model's axes to the wrapper
        /// frame, measured once on run A (all seven pieces were exported with
        /// the same axes, which `Wrap` then checks piece by piece).
        static Quaternion MeasureFrame(StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Models + "/Palisade_Run_1m_A.fbx"));
            try
            {
                var s = Find(go.transform, "__Snap_Start").position;
                var e = Find(go.transform, "__Snap_End").position;
                var mf = go.GetComponentInChildren<MeshFilter>();
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;

                Vector3 along = (e - s).normalized;
                // Up: of the two axes across the run, the one the mesh spans
                // furthest (2.8 m of stake against a 0.4 m deep wall), signed
                // toward the tips (2.6 m above the root against 0.22 m of embed).
                Vector3 best = Vector3.zero; float bestSpan = -1f; float bestSign = 1f;
                foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
                {
                    if (Mathf.Abs(Vector3.Dot(axis, along)) > 0.5f) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var v in verts)
                    {
                        float d = Vector3.Dot(mf.transform.TransformPoint(v) - s, axis);
                        lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                    }
                    if (hi - lo > bestSpan) { bestSpan = hi - lo; best = axis; bestSign = hi > -lo ? 1f : -1f; }
                }
                Vector3 up = best * bestSign;
                var q = Quaternion.Inverse(Quaternion.LookRotation(along, up));

                // Rails: the pegs submesh (rails only) lies to the rails side.
                int pegs = System.Array.FindIndex(mf.GetComponent<MeshRenderer>().sharedMaterials,
                    m => m != null && m.name == "SS_WallL1_Pegs");
                if (pegs < 0) throw new System.Exception("run A has no pegs submesh to find the rails by");
                var idx = mesh.GetIndices(pegs);
                Vector3 c = Vector3.zero;
                foreach (int i in idx) c += mf.transform.TransformPoint(verts[i]);
                c /= idx.Length;
                float side = (q * (c - s)).x;
                log.AppendLine($"frame: along {along:F3} up {up:F3} -> rails at wrapper x {side:F3}");
                if (side > 0f)
                    throw new System.Exception("rails land on +X: the FBX is mirrored against the wrapper frame");
                return q;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static Transform Find(Transform root, string suffix)
            => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith(suffix))
               ?? throw new System.Exception(root.name + ": no " + suffix + " marker");

        // --- the wrappers --------------------------------------------------------

        static void Wrap(string piece, Quaternion frame, StringBuilder log)
        {
            string path = Wrappers + "/" + piece + ".prefab";
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) throw new System.Exception("missing wrapper " + path);

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var root = contents.transform;
                if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity)
                    throw new System.Exception(piece + ": wrapper root is not at unit scale/identity");
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Models + "/" + piece + ".fbx"));
                model.transform.SetPositionAndRotation(Vector3.zero, frame * model.transform.rotation);
                var anchor = Find(model.transform, piece == "Palisade_Post" ? "__Post_Center" : "__Snap_Start");
                model.transform.position -= anchor.position;

                // Flat, like the old wrappers: every node straight under the root.
                foreach (var t in model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform).ToList())
                    t.SetParent(root, true);
                Object.DestroyImmediate(model);
                // The Blender root empty (the piece's own name, 100x scale):
                // nothing hangs off it once flattened, and a child named
                // like the wrapper only confuses a hierarchy search.
                foreach (Transform t in root.Cast<Transform>().ToList())
                    if (t.name == piece && t.childCount == 0 && t.GetComponents<Component>().Length == 1)
                        Object.DestroyImmediate(t.gameObject);

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                var s = Find(root, "__Snap_Start").localPosition;
                var e = Find(root, "__Snap_End").localPosition;
                float len = Length[piece];
                var bounds = Bounds(root);
                log.AppendLine($"{piece}: start {s:F3} end {e:F3} (want +Z {len}) bounds {bounds.min:F2}..{bounds.max:F2} tris {Tris(root)}");
                if (Mathf.Abs(e.z - s.z - len) > 0.002f || Mathf.Abs(e.x - s.x) > 0.002f || Mathf.Abs(e.y - s.y) > 0.002f)
                    throw new System.Exception(piece + ": markers are not +Z " + len + " apart after the frame");

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(path) != guid)
                throw new System.Exception(piece + ": wrapper GUID changed");
        }

        static Bounds Bounds(Transform root)
        {
            var rs = root.GetComponentsInChildren<MeshRenderer>(true);
            var b = new Bounds();
            bool any = false;
            foreach (var r in rs)
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
            => root.GetComponentsInChildren<MeshFilter>(true).Sum(f => (int)(f.sharedMesh.triangles.Length / 3));
    }
}
