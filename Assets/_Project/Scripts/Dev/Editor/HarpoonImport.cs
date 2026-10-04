using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The bow harpoon, phase 0 art (docs/PLAN-harpoon.md), from
    /// `art-staging/harpoon-v1` (see its CONTRACT.md).** Look-check import
    /// only: nothing in the game loads these yet.
    ///
    /// - **Copies** `HarpoonMount.fbx` + `HarpoonBarb.fbx` into
    ///   `Art/Harpoon/Models/` and `rope-look.json` into
    ///   `Resources/Harpoon/` (only when the bytes differ). The FBXs stay OUT
    ///   of Resources on purpose: a model and its wrapper prefab share a name,
    ///   and `Resources.Load<GameObject>("Harpoon/HarpoonMount")` would then
    ///   pick either one.
    /// - **Import settings as `StorageL1Import`** (file scale, flat authored
    ///   normals, no rig/animation, `preserveHierarchy`) plus
    ///   `bakeAxisConversion`. The wrapper UNPACKS the model and squares
    ///   every empty (`Swivel`, `Barb_Muzzle`, `Winch_Drum`,
    ///   `Harpooner_Stand`, `Line_Attach`) to the root frame (the FBX gives
    ///   them Blender's object axes), so code turns `Swivel` about its local
    ///   Y and spins `Winch_Drum` about its local X. MEASURED, never
    ///   assumed (the x100 Blender root trap, memory astra-rig-scale-trap):
    ///   every anchor is checked against the CONTRACT position and rotation.
    /// - **Materials** in `Resources/Harpoon/`, remapped BY STEM:
    ///   `SS_Harpoon_Timber` and `SS_Harpoon_Iron` on `SeaSick/Coaster Paint`,
    ///   the coaster hull's own shader (GameColor vertex colour; it darkens
    ///   iron-coloured vertices to charcoal itself, so the iron needs no
    ///   separate setup), and `SS_Harpoon_Rope` on
    ///   `SeaSick/Environment Toon Textured` with the wall's rope tile
    ///   (`SS_StorageL1_Hemp`'s recipe). The LineRenderer rope uses the same
    ///   material: the shader multiplies vertex colour, so a line's tint works.
    /// - **Wrappers** `Resources/Harpoon/HarpoonMount.prefab` and
    ///   `HarpoonBarb.prefab`: an identity root holding the model, frame
    ///   measured from the anchors (+Z forward, +Y up, metres, origin = deck
    ///   on the swivel axis / the barb's muzzle point).
    ///
    /// Run: `return SeaSick.Dev.HarpoonImport.Run();` Idempotent.
    public static class HarpoonImport
    {
        const string ModelDir = "Assets/_Project/Art/Harpoon/Models";
        const string ResDir = "Assets/_Project/Resources/Harpoon";
        const string MountPath = ModelDir + "/HarpoonMount.fbx";
        const string BarbPath = ModelDir + "/HarpoonBarb.fbx";
        const string JsonPath = ResDir + "/rope-look.json";
        public const string MountWrapper = ResDir + "/HarpoonMount.prefab";
        public const string BarbWrapper = ResDir + "/HarpoonBarb.prefab";
        const string RopeTile = "Assets/_Project/Art/WallL1/Textures/rope-tile-512.png";
        const string PaintShader = "SeaSick/Coaster Paint";
        const string ToonShader = "SeaSick/Environment Toon Textured";
        const string SourceRel = "art-staging/harpoon-v1";

        public const string Timber = "SS_Harpoon_Timber";
        public const string Rope = "SS_Harpoon_Rope";
        public const string Iron = "SS_Harpoon_Iron";

        const float Tolerance = 0.01f;

        /// Blender metres (x, y, z), Z up, forward -Y  ->  Unity (-x, z, -y).
        static Vector3 B(float x, float y, float z) => new Vector3(-x, z, -y);

        /// `export-verification.json` (2026-10-04 export; re-exported the
        /// same day after `build.py` put the winch coil at the deck origin,
        /// 1.2 m under the drum -- `Wrap` now checks nothing is below deck).
        static readonly Dictionary<string, Vector3> MountMarks = new Dictionary<string, Vector3>
        {
            { "Swivel", B(0f, 0f, 0.16f) },
            { "Barb_Muzzle", B(0f, -0.80f, 1.72f) },
            { "Winch_Drum", B(0f, 0.42f, 1.06f) },
            { "Harpooner_Stand", B(0f, 0.86f, 0.30f) },
        };
        static readonly Dictionary<string, Vector3> BarbMarks = new Dictionary<string, Vector3>
        {
            { "Line_Attach", B(0f, 0.335f, 0f) },
        };
        /// The CONTRACT's mesh bounds, converted (min, max).
        static readonly Bounds MountBounds = FromBlender(new Vector3(-0.899f, -0.802f, 0f), new Vector3(0.899f, 1.06f, 1.9f));
        static readonly Bounds BarbBounds = FromBlender(new Vector3(-0.19f, -0.55f, -0.067f), new Vector3(0.19f, 0.405f, 0.067f));

        static Bounds FromBlender(Vector3 min, Vector3 max)
        {
            var a = B(min.x, min.y, min.z);
            var b = B(max.x, max.y, max.z);
            var bb = new Bounds(a, Vector3.zero);
            bb.Encapsulate(b);
            return bb;
        }

        [MenuItem("SeaSick/Art/Import bow harpoon (phase 0 look check)")]
        public static string Run()
        {
            var log = new StringBuilder();
            try
            {
                Copy(log);
                var mats = Materials(log);
                ImportModel(MountPath, mats, log);
                ImportModel(BarbPath, mats, log);
                var frame = MeasureFrame(log);
                Wrap(MountPath, MountWrapper, "HarpoonMount", frame, MountMarks, MountBounds, log);
                Wrap(BarbPath, BarbWrapper, "HarpoonBarb", frame, BarbMarks, BarbBounds, log);
                AssetDatabase.SaveAssets();
                log.AppendLine("HARPOON_IMPORT_OK");
            }
            catch (System.Exception e) { log.AppendLine("FAILED: " + e.Message); Debug.LogException(e); }
            Debug.Log("[HarpoonImport] " + log);
            return log.ToString();
        }

        static string Stem(string name) => Regex.Replace(name, @"\.\d+$", "");

        // --- copy ------------------------------------------------------------------

        static void Copy(StringBuilder log)
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string src = Path.Combine(project, SourceRel);
            if (!Directory.Exists(src)) throw new System.Exception("no export folder " + src);
            EnsureFolder(ModelDir);
            EnsureFolder(ResDir);
            bool any = false;
            foreach (var (file, dst) in new[]
                { ("HarpoonMount.fbx", MountPath), ("HarpoonBarb.fbx", BarbPath), ("rope-look.json", JsonPath) })
            {
                string from = Path.Combine(src, file);
                if (!File.Exists(from)) throw new System.Exception("missing " + from);
                string to = Path.Combine(project, dst);
                if (File.Exists(to) && File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(from)))
                {
                    log.AppendLine($"copy: {file} unchanged");
                    continue;
                }
                File.Copy(from, to, true);
                any = true;
                log.AppendLine($"copy: {file} -> {dst} ({new FileInfo(to).Length / 1024} KB)");
            }
            if (any) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // --- materials -------------------------------------------------------------

        static Dictionary<string, Material> Materials(StringBuilder log)
        {
            var paint = Shader.Find(PaintShader);
            var toon = Shader.Find(ToonShader);
            if (paint == null) throw new System.Exception("Missing shader " + PaintShader);
            if (toon == null) throw new System.Exception("Missing shader " + ToonShader);
            var rope = AssetDatabase.LoadAssetAtPath<Texture2D>(RopeTile);
            if (rope == null) throw new System.Exception("missing " + RopeTile + " (run WallL1Import first)");
            var set = new Dictionary<string, Material>
            {
                { Timber, Mat(Timber, paint, null) },
                { Iron, Mat(Iron, paint, null) },
                { Rope, Mat(Rope, toon, rope) },
            };
            log.AppendLine($"materials: {Timber} + {Iron} on {PaintShader} (the hull's FCoasterPaint shader), {Rope} on {ToonShader} x {Path.GetFileName(RopeTile)}");
            return set;
        }

        static Material Mat(string name, Shader shader, Texture2D map)
        {
            string path = ResDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            if (m.HasProperty("_Tint")) m.SetColor("_Tint", Color.white);
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", map);
                m.SetTextureScale("_BaseMap", Vector2.one);
                m.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Ambient")) m.SetFloat("_Ambient", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // --- import ----------------------------------------------------------------

        static void ImportModel(string path, Dictionary<string, Material> mats, StringBuilder log)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new System.Exception("no model at " + path);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
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
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();

            var fbxNames = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (fbxNames.Count == 0) throw new System.Exception(path + ": no embedded materials to map");
            foreach (var n in fbxNames)
            {
                if (!mats.TryGetValue(Stem(n), out var target))
                    throw new System.Exception($"{path}: FBX material '{n}' matches none of {string.Join(", ", mats.Keys)}");
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), target);
            }
            mi.SaveAndReimport();
            log.AppendLine($"import {Path.GetFileName(path)}: {fbxNames.Count} FBX materials remapped ({string.Join(", ", fbxNames)})");

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var allowed = new HashSet<Material>(mats.Values);
            var slots = new StringBuilder();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                // Unity drops a slot no face uses (Mount_Base has no rope).
                var sm = r.sharedMaterials;
                slots.Append($" {r.name}[{string.Join(",", sm.Select(m => m ? m.name.Replace("SS_Harpoon_", "") : "null"))}]");
                foreach (var m in sm)
                    if (m == null || !allowed.Contains(m))
                        throw new System.Exception($"{path}: {r.name} has an unmapped material {(m ? m.name : "null")}");
            }
            log.AppendLine("  slots:" + slots);
        }

        // --- frames and wrappers -----------------------------------------------------

        static Transform Find(Transform root, string stem)
        {
            var hits = root.GetComponentsInChildren<Transform>(true).Where(t => Stem(t.name) == stem).ToList();
            if (hits.Count == 0) throw new System.Exception("model has no " + stem);
            if (hits.Count > 1) throw new System.Exception($"model has {hits.Count} objects named {stem}");
            return hits[0];
        }

        /// The rotation taking the imported mount onto the CONTRACT frame,
        /// from two anchor spans (swivel -> muzzle, swivel -> drum). Also the
        /// x100 guard: the measured span must equal the expected one.
        static Quaternion MeasureFrame(StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MountPath));
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var t = go.transform;
                Vector3 P(string n) => Find(t, n).position;
                Vector3 a1 = P("Barb_Muzzle") - P("Swivel"), a2 = P("Winch_Drum") - P("Swivel");
                Vector3 e1 = MountMarks["Barb_Muzzle"] - MountMarks["Swivel"], e2 = MountMarks["Winch_Drum"] - MountMarks["Swivel"];
                float ratio = a1.magnitude / e1.magnitude;
                if (Mathf.Abs(ratio - 1f) > 0.02f)
                    throw new System.Exception($"mount anchors {ratio:F3}x their CONTRACT spacing -- a file-scale problem (the x100 trap?)");
                var q = Quaternion.LookRotation(e1.normalized, Vector3.Cross(e1, e2)) * Quaternion.Inverse(Quaternion.LookRotation(a1.normalized, Vector3.Cross(a1, a2)));
                var ls = t.localScale;
                log.AppendLine($"frame: {q.eulerAngles:F1} (identity = the importer already matches the CONTRACT); model root scale {ls:F3}, rotation {t.localRotation.eulerAngles:F1}");
                return q;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Wrap(string modelPath, string wrapper, string rootName, Quaternion frame,
            Dictionary<string, Vector3> marks, Bounds want, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(wrapper) == null)
            {
                var fresh = new GameObject(rootName);
                PrefabUtility.SaveAsPrefabAsset(fresh, wrapper);
                Object.DestroyImmediate(fresh);
                log.AppendLine("created " + wrapper);
            }
            var contents = PrefabUtility.LoadPrefabContents(wrapper);
            try
            {
                var root = contents.transform;
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                model.name = rootName + "_Model";
                model.transform.SetParent(root, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = frame * model.transform.localRotation;
                // The FBX empties come in with Blender's object axes (local
                // +Z down, +Y aft in the mount frame). Unpack and square every
                // EMPTY to the root, keeping every child exactly where it is,
                // so `Swivel` yaws about its own Y and `Winch_Drum` spins
                // about its own X. Meshes keep their node rotation (their
                // vertices are baked against it).
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                int squared = Square(model.transform, root);
                log.AppendLine($"{rootName}: {squared} empties squared to the root frame");

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Every anchor where the CONTRACT says, axes = the root's.
                var line = new StringBuilder($"{rootName} anchors:");
                foreach (var kv in marks)
                {
                    var a = Find(root, kv.Key);
                    Vector3 at = root.InverseTransformPoint(a.position);
                    float miss = (at - kv.Value).magnitude;
                    Vector3 fwd = root.InverseTransformDirection(a.forward), up = root.InverseTransformDirection(a.up);
                    float off = Mathf.Max(Vector3.Angle(fwd, Vector3.forward), Vector3.Angle(up, Vector3.up));
                    float sc = a.lossyScale.x / root.lossyScale.x;
                    float parentOff = Quaternion.Angle(a.localRotation, Quaternion.identity);
                    line.Append($" {kv.Key} {at:F3} (miss {miss:F3} m, axes off {off:F1} deg, localRot off {parentOff:F1} deg, scale {sc:F3});");
                    if (miss > Tolerance)
                        throw new System.Exception($"{rootName} {kv.Key} at {at:F3}, CONTRACT says {kv.Value:F3}: frame or scale is off");
                    if (off > 0.5f)
                        throw new System.Exception($"{rootName} {kv.Key} forward {fwd:F3} up {up:F3}: not the CONTRACT's +Z/+Y");
                    if (Mathf.Abs(sc - 1f) > 0.01f)
                        throw new System.Exception($"{rootName} {kv.Key} carries scale {sc:F3} (the x100 trap)");
                }
                log.AppendLine(line.ToString());

                var b = RenderBounds(root);
                float bmiss = Mathf.Max((b.min - want.min).magnitude, (b.max - want.max).magnitude);
                log.AppendLine($"{rootName} bounds {b.min:F3}..{b.max:F3} (CONTRACT {want.min:F3}..{want.max:F3}, miss {bmiss:F3}), {Tris(root)} tris, {root.GetComponentsInChildren<MeshRenderer>(true).Length} renderers");
                if (bmiss > 0.02f) throw new System.Exception($"{rootName} bounds off the CONTRACT by {bmiss:F3} m");

                // Nothing under the deck (the mount's origin is the deck surface).
                if (rootName == "HarpoonMount")
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        float low = float.MaxValue;
                        foreach (var v in mf.sharedMesh.vertices) low = Mathf.Min(low, root.InverseTransformPoint(mf.transform.TransformPoint(v)).y);
                        if (low < -0.005f) throw new System.Exception($"{mf.name} reaches {low:F3} m below the deck");
                    }

                PrefabUtility.SaveAsPrefabAsset(contents, wrapper);
                log.AppendLine("wrapped " + wrapper);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        /// Top-down: give every transform without a mesh the root's rotation
        /// and unit scale, children's world poses unchanged.
        static int Square(Transform t, Transform root)
        {
            int n = 0;
            if (t.GetComponent<MeshFilter>() == null)
            {
                var kids = new List<(Transform t, Vector3 p, Quaternion q)>();
                foreach (Transform c in t) kids.Add((c, c.position, c.rotation));
                foreach (var k in kids) k.t.SetParent(root, true);
                t.rotation = root.rotation;
                t.localScale = Vector3.one;
                foreach (var k in kids) { k.t.SetParent(t, true); k.t.SetPositionAndRotation(k.p, k.q); }
                n++;
            }
            foreach (Transform c in t) n += Square(c, root);
            return n;
        }

        /// Mesh-space bounds (vertex-exact through the renderer's matrix).
        static Bounds RenderBounds(Transform root)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static int Tris(Transform root) =>
            root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null)
                .Sum(f => (int)Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => (long)f.sharedMesh.GetIndexCount(s)) / 3);
    }
}
