using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Dev
{
    /// **The level 1 grain mill, a canvas awning over a quern (2026-10-03),
    /// `art-staging/grain-mill-lvl1-v1`.** A sibling of `MillL2Import`.
    ///
    /// Puts `Art/GrainMillL1/Models/mill-lvl1.fbx` behind its own wrapper,
    /// `Resources/Settlement/mill_l1.prefab` (created on the first run, GUID
    /// kept after), which `BuildPlans.Mill` now points at (it wore the
    /// sawmill, so flour showed as planks).
    ///
    /// - **Slots follow `StationStockView`'s convention as exported**:
    ///   `Input_Sheaf_01..06` under `Input_Container` (wheat sheaves) and
    ///   `Output_Sack_01..06` under `Output_Container` (flour sacks), 6 and 6
    ///   like the plan. The rack and pallet under them are separate static
    ///   meshes. `Flour_Heap` (the spout's heap, "shown while grinding") is
    ///   renamed `Bench_Cutting`, the alias the view shows while the station
    ///   works. Saved as an empty mill: slots and heap hidden.
    /// - **The quern turns with the miller.** `Quern_Runner` turns once per
    ///   1.5 s `Crew_Mill` loop (the FBX's object take, read as the first
    ///   non-preview clip: this export names it `Scene`, not `Mill1_Grind`).
    ///   The take is NOT played: its axis and turns are measured off it here
    ///   and handed to `MillQuern`, which turns the runner in phase with
    ///   whoever is milling at `Worker_Stand`.
    /// - One material: the export has a single unnamed material (the look is
    ///   all vertex colour), mapped to the plain, map-less
    ///   `SS_GrainMillL1_Plain` on `SeaSick/Environment Toon Textured`.
    /// - A `Worker_Pad` over the platform's top (the stand is 0.12 m up).
    /// - The wrapper frame is measured from the markers (up: the plane of
    ///   the ground marks toward the raised stand; right: pickup - dropoff, so
    ///   the input side lands on +X like the sawmill's; front: right x up).
    ///   Every marker is checked against the README (Blender (x, y, z) ->
    ///   wrapper (-x, z, -y)).
    ///
    /// Idempotent: run it again after re-exporting the FBX.
    public static class GrainMillL1Import
    {
        const string Root = "Assets/_Project/Art/GrainMillL1";
        const string ModelPath = Root + "/Models/mill-lvl1.fbx";
        const string MaterialPath = Root + "/Materials/SS_GrainMillL1_Plain.mat";
        const string Wrapper = "Assets/_Project/Resources/Settlement/mill_l1.prefab";
        const string ShaderName = "SeaSick/Environment Toon Textured";

        /// The plan's plot (`BuildPlans.Mill`) and ridge; the model must fit.
        const float FootX = 5.2f, FootZ = 4.8f, Ridge = 3.6f;
        const float MarkTolerance = 0.02f;

        static readonly Dictionary<string, Vector3> Marks = new Dictionary<string, Vector3>
        {
            { "Worker_Stand", new Vector3(-0.20f, 0.12f, -0.62f) },
            { "Worker_Approach", new Vector3(-0.60f, 0f, -1.75f) },
            { "Input_Pickup", new Vector3(2.00f, 0f, 1.55f) },
            { "Output_Dropoff", new Vector3(-2.00f, 0f, 1.55f) },
            { "Entrance_Anchor", new Vector3(0f, 0f, 1.50f) },
            { "Quern_Anchor", new Vector3(0.21f, 0.78f, -0.20f) },
        };

        static IEnumerable<string> Required()
        {
            yield return "Input_Container";
            for (int i = 1; i <= 6; i++) yield return $"Input_Sheaf_{i:00}";
            yield return "Output_Container";
            for (int i = 1; i <= 6; i++) yield return $"Output_Sack_{i:00}";
            yield return "Flour_Heap";
            yield return "Quern_Runner";
            yield return "Mill1_Platform";
            foreach (var m in Marks.Keys) yield return m;
        }

        [MenuItem("SeaSick/Art/Import level 1 grain mill (quern under canvas)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mat = Material(log);
            Import(mat, log);
            var frame = MeasureFrame(log);
            Wrap(frame, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[GrainMillL1] " + log);
            return log.ToString();
        }

        static string Stem(string name) => Regex.Replace(name, @"\.\d+$", "");

        // --- material ----------------------------------------------------------

        static Material Material(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, MaterialPath); }
            m.shader = shader;
            m.SetTexture("_BaseMap", null);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Ambient", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            log.AppendLine("material: " + m.name + " on " + ShaderName);
            return m;
        }

        // --- the model -----------------------------------------------------------

        static void Import(Material plain, StringBuilder log)
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (mi == null) throw new System.Exception("no model at " + ModelPath);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;  // flat authored normals
            mi.importTangents = ModelImporterTangents.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            // The runner's object take is read (not played): `Quern` measures
            // its axis off it.
            mi.importAnimation = true;
            mi.animationType = ModelImporterAnimationType.Legacy;
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

            var fbxNames = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (fbxNames.Count == 0) throw new System.Exception(ModelPath + ": no embedded materials to map");
            foreach (var n in fbxNames)
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), plain);
                log.AppendLine($"remap '{n}' -> {plain.name}");
            }
            mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != plain)
                        throw new System.Exception($"{ModelPath}: {r.name} has an unmapped material {(m ? m.name : "null")}");
        }

        // --- the frame -------------------------------------------------------------

        static Transform Find(Transform root, string stem)
        {
            var hits = root.GetComponentsInChildren<Transform>(true).Where(t => Stem(t.name) == stem).ToList();
            if (hits.Count == 0) throw new System.Exception("kit has no " + stem);
            if (hits.Count > 1) throw new System.Exception($"kit has {hits.Count} objects named {stem}");
            return hits[0];
        }

        static Quaternion MeasureFrame(StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                go.transform.position = Vector3.zero;
                var t = go.transform;
                Vector3 pick = Find(t, "Input_Pickup").position;
                Vector3 drop = Find(t, "Output_Dropoff").position;
                Vector3 gate = Find(t, "Worker_Approach").position;
                Vector3 stand = Find(t, "Worker_Stand").position;
                Vector3 door = Find(t, "Entrance_Anchor").position;

                Vector3 up = Vector3.Cross(drop - pick, gate - pick).normalized;
                if (Vector3.Dot(up, stand - gate) < 0f) up = -up;
                Vector3 right = Vector3.ProjectOnPlane(pick - drop, up).normalized;
                Vector3 front = Vector3.Cross(right, up).normalized;
                if (Vector3.Dot(front, door - stand) <= 0f)
                    throw new System.Exception("the entrance is behind the stand: the FBX is mirrored against the wrapper frame");
                var q = Quaternion.Inverse(Quaternion.LookRotation(front, up));
                log.AppendLine($"frame: up {up:F3} right {right:F3} front {front:F3}");
                return q;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // --- the wrapper -----------------------------------------------------------

        static void Wrap(Quaternion frame, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Wrapper) == null)
            {
                var fresh = new GameObject("mill_l1");
                PrefabUtility.SaveAsPrefabAsset(fresh, Wrapper);
                Object.DestroyImmediate(fresh);
            }
            string guid = AssetDatabase.AssetPathToGUID(Wrapper);

            var contents = PrefabUtility.LoadPrefabContents(Wrapper);
            try
            {
                var root = contents.transform;
                if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity)
                    throw new System.Exception("mill wrapper root is not at unit scale/identity");
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.name = "GrainMill_L1";
                // The legacy take is measured, never played.
                foreach (var an in model.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(an);
                model.transform.SetParent(root, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = frame * model.transform.localRotation;

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                foreach (var n in Required()) Find(root, n);

                foreach (var kv in Marks)
                {
                    Vector3 at = root.InverseTransformPoint(Find(root, kv.Key).position);
                    log.AppendLine($"{kv.Key}: {at:F3} (want {kv.Value:F2})");
                    if ((at - kv.Value).magnitude > MarkTolerance)
                        throw new System.Exception($"{kv.Key} at {at:F3}, README says {kv.Value:F2}: frame or scale is off");
                }

                int sheaves = Slots(Find(root, "Input_Container"), "Input_");
                int sacks = Slots(Find(root, "Output_Container"), "Output_");
                log.AppendLine($"slots: {sheaves} sheaves, {sacks} sacks");
                if (sheaves != 6 || sacks != 6)
                    throw new System.Exception($"slot counts {sheaves}/{sacks}, want 6/6");

                // The spout's heap shows while he grinds: the view's bench alias.
                Find(root, "Flour_Heap").name = "Bench_Cutting";

                Quern(root, model, log);

                var b = Bounds(root, _ => true);
                log.AppendLine($"bounds (all variants) {b.min:F2}..{b.max:F2} size {b.size:F2} (plan {FootX} x {FootZ}, ridge {Ridge})");
                if (b.min.x < -FootX * 0.5f || b.max.x > FootX * 0.5f || b.min.z < -FootZ * 0.5f || b.max.z > FootZ * 0.5f)
                    throw new System.Exception($"bounds {b.min:F2}..{b.max:F2} leave the {FootX} x {FootZ} footprint");
                if (b.max.y > Ridge) throw new System.Exception($"top {b.max.y:F2} m is over the {Ridge} m ridge");
                if (b.min.y < -0.2f || b.min.y > 0.05f)
                    throw new System.Exception($"lowest point {b.min.y:F2} m: the root is not on the ground");

                int all = Tris(root, _ => true);
                int empty = Tris(root, t => !IsStock(t) && Stem(t.name) != "Bench_Cutting");
                var rs = root.GetComponentsInChildren<MeshRenderer>(true);
                log.AppendLine($"tris: empty {empty}, all {all}; renderers {rs.Length}");

                Pad(root, log);

                // Saved as an EMPTY mill: stock and heap hidden
                // (`StationStockView` shows the ledger's truth once raised).
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string s = Stem(t.name);
                    if (IsSlotName(s) || s == "Bench_Cutting") t.gameObject.SetActive(false);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, Wrapper);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(Wrapper) != guid)
                throw new System.Exception("mill wrapper GUID changed");
            log.AppendLine("wrapper saved, GUID kept " + guid);
        }

        /// The platform's walking top (its faces at the stand's height),
        /// as a `Worker_Pad` child the runtime grounds bodies on.
        static void Pad(Transform root, StringBuilder log)
        {
            var mf = Find(root, "Mill1_Platform").GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) throw new System.Exception("Mill1_Platform has no mesh");
            var pts = mf.sharedMesh.vertices.Select(v => root.InverseTransformPoint(mf.transform.TransformPoint(v))).ToList();
            float standY = root.InverseTransformPoint(Find(root, "Worker_Stand").position).y;
            log.AppendLine($"platform y {pts.Min(p => p.y):F3}..{pts.Max(p => p.y):F3}");
            var topPts = pts.Where(p => Mathf.Abs(p.y - standY) < 0.015f).ToList();
            if (topPts.Count < 3) throw new System.Exception("no platform faces at the stand's height " + standY.ToString("F3"));
            float top = topPts.Average(p => p.y);
            float x0 = topPts.Min(p => p.x), x1 = topPts.Max(p => p.x);
            float z0 = topPts.Min(p => p.z), z1 = topPts.Max(p => p.z);

            var stand = Find(root, "Worker_Stand");
            var gate = Find(root, "Worker_Approach");
            Vector3 s = root.InverseTransformPoint(stand.position);
            log.AppendLine($"pad: top {top:F3} x {x0:F2}..{x1:F2} z {z0:F2}..{z1:F2}");
            if (Mathf.Abs(top - s.y) > MarkTolerance)
                throw new System.Exception($"pad top {top:F3} but Worker_Stand at {s.y:F3}");
            if (s.x < x0 || s.x > x1 || s.z < z0 || s.z > z1)
                throw new System.Exception("Worker_Stand is off the pad");

            var go = new GameObject("Worker_Pad");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, top, (z0 + z1) * 0.5f);
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<WorkerPad>().Configure(new Vector2(x1 - x0, z1 - z0), stand, gate);
        }

        /// The runner stone's spin, measured off the FBX's object take: the
        /// local axis (sign included), turns per loop and the loop length.
        /// Handed to a `MillQuern` on the model root.
        static void Quern(Transform root, GameObject model, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) throw new System.Exception(ModelPath + ": no runner take");
            var runner = Find(root, "Quern_Runner");
            var probe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                Transform pr = Find(probe.transform, "Quern_Runner");
                const float dt = 1f / 120f;
                clip.SampleAnimation(probe, 0f);
                Quaternion r0 = pr.localRotation;
                clip.SampleAnimation(probe, dt);
                (Quaternion.Inverse(r0) * pr.localRotation).ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) { ang = 360f - ang; axis = -axis; }
                float turns = ang / dt * clip.length / 360f;
                clip.SampleAnimation(probe, clip.length);
                float end = Quaternion.Angle(r0, pr.localRotation);
                log.AppendLine($"quern: take '{clip.name}' {clip.length:F3} s; axis {axis:F3} {turns:F2} turns; loop closes {end:F1} deg");
                if (Mathf.Abs(clip.length - 1.5f) > 0.05f) throw new System.Exception("runner take is not 1.5 s");
                if (Mathf.Abs(turns - 1f) > 0.1f) throw new System.Exception("runner does not turn once per loop");
                var q = model.AddComponent<MillQuern>();
                q.Configure(runner, r0, axis, 1f);
                runner.localRotation = r0;   // saved at the take's frame 0
            }
            finally { Object.DestroyImmediate(probe); }
        }

        static bool IsSlotName(string s)
            => Regex.IsMatch(s, @"^(Input_Sheaf|Output_Sack)_\d+$");

        static bool IsStock(Transform t)
        {
            for (var p = t; p != null; p = p.parent) if (IsSlotName(Stem(p.name))) return true;
            return false;
        }

        static int Slots(Transform group, string prefix)
            => group.GetComponentsInChildren<Transform>(true)
                .Count(t => t != group && Regex.IsMatch(Stem(t.name), "^" + prefix + @".*\d+$"));

        static Bounds Bounds(Transform root, System.Func<Transform, bool> keep)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!keep(mf.transform) || mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static int Tris(Transform root, System.Func<Transform, bool> keep)
            => root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && keep(f.transform))
                .Sum(f => f.sharedMesh.triangles.Length / 3);
    }
}
