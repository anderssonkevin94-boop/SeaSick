using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Dev
{
    /// **The level 2 sawmill, the brick-footed saw shed (2026-10-01),
    /// seasick_assets claude/beautiful-mendel-wxvyo6 @50783ea,
    /// `sawmill-l2-concept`.** A sibling of `MillL1Import`.
    ///
    /// Puts `Art/MillL2/Models/sawmill-lvl2.fbx` behind its own wrapper,
    /// `Resources/Settlement/sawmill_l2.prefab` (created on the first run),
    /// the level 2 look `BuildingLevelLook` swaps in. Same plot, same
    /// markers as level 1 except `Worker_Stand`, which moves beside the crank
    /// (README: Blender (-0.95, 0.62) -> wrapper (0.95, -0.62)). Materials:
    /// the level 1 mill's own three (`Art/MillL1/Materials`), by stem.
    ///
    /// - No state kit yet: the input logs and output boards are the level 1
    ///   slots (`StationStockView` drives them), and the log on the saw
    ///   table (`Bench2_Cutting`) is renamed `Bench_Cutting`, so it shows
    ///   only while the sawyer works -- the level 1 bench convention.
    /// - The wheels: `Mill2_CrankWheel` turns once and `Saw2_Wheel` five
    ///   times per 1.2 s `Crew_Crank` loop (the FBX's object take). The take
    ///   is NOT played: its axes and turns are measured off it here and
    ///   handed to `MillCrankWheels`, which turns the wheels in phase with
    ///   whoever is cranking.
    /// - A `Worker_Pad` over the brick plinth's top, as level 1's platform.
    ///
    /// Idempotent.
    public static class MillL2Import
    {
        const string Root = "Assets/_Project/Art/MillL2";
        const string ModelPath = Root + "/Models/sawmill-lvl2.fbx";
        const string MaterialDir = "Assets/_Project/Art/MillL1/Materials";
        const string TextureDir = "Assets/_Project/Art/WallL1/Textures";
        const string Wrapper = "Assets/_Project/Resources/Settlement/sawmill_l2.prefab";
        const string ShaderName = "SeaSick/Environment Toon Textured";

        const float FootX = 7.56f, FootZ = 5.85f, Ridge = 3.84f;
        const float MarkTolerance = 0.02f;

        const string Wood = "SS_LumberL1_Wood";
        const string Hemp = "SS_LumberL1_Hemp";
        const string Plain = "SS_LumberL1_Canvas_Endgrain_Stone";

        /// README marker coordinates, Blender (x, y, z) -> wrapper
        /// (-x, z, -y): front -Y becomes +Z, and the input side (Blender -X)
        /// becomes +X.
        static readonly Dictionary<string, Vector3> Marks = new Dictionary<string, Vector3>
        {
            { "Worker_Stand", new Vector3(0.95f, 0.16f, -0.62f) },
            { "Worker_Approach", new Vector3(0.50f, 0f, -1.80f) },
            { "Input_Pickup", new Vector3(2.30f, 0f, 1.68f) },
            { "Output_Dropoff", new Vector3(-2.20f, 0f, 1.58f) },
            { "Entrance_Anchor", new Vector3(0f, 0f, 1.52f) },
            { "Bench_Anchor", new Vector3(0f, 0f, -0.04f) },
        };

        /// Where the sawyer comes onto the plinth at level 2 (see `Wrap`).
        static readonly Vector3 ApproachL2 = new Vector3(2.05f, 0f, -1.75f);

        /// Every name runtime code or the contract relies on, by stem.
        static IEnumerable<string> Required()
        {
            yield return "Input_Container";
            for (int i = 1; i <= 6; i++) yield return $"Input_Log_{i:00}";
            yield return "Output_Container";
            for (int i = 1; i <= 12; i++) yield return $"Output_Plank_{i:00}";
            yield return "Bench2_Cutting";
            yield return "Mill2_CrankWheel";
            yield return "Saw2_Wheel";
            yield return "Mill2_BrickPlinth";
            foreach (var m in Marks.Keys) yield return m;
        }

        [MenuItem("SeaSick/Art/Import level 2 sawmill (saw shed)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mats = Materials(log);
            Import(mats, log);
            var frame = MeasureFrame(log);
            Wrap(frame, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[MillL2] " + log);
            return log.ToString();
        }

        static string Stem(string name) => Regex.Replace(name, @"\.\d+$", "");

        // --- materials ---------------------------------------------------------

        static Dictionary<string, Material> Materials(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
            // The wall's tiles, already imported by `WallL1Import` (sRGB,
            // repeat, mips, 512, compressed) -- shared, not copied.
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/wood-tile-512.png");
            var rope = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/rope-tile-512.png");
            if (wood == null || rope == null)
                throw new System.Exception("wall tiles missing under " + TextureDir + " (run WallL1Import first)");
            var set = new Dictionary<string, Material>
            {
                { Wood, Mat(Wood, shader, wood) },
                { Hemp, Mat(Hemp, shader, rope) },
                // Canvas, cut ends, stone and the painted sign: GameColor only.
                { Plain, Mat(Plain, shader, null) },
            };
            log.AppendLine("materials: " + string.Join(", ", set.Keys) + " on " + ShaderName);
            return set;
        }

        static Material Mat(string name, Shader shader, Texture2D map)
        {
            string path = MaterialDir + "/" + name + ".mat";
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

        // --- the model -----------------------------------------------------------

        static void Import(Dictionary<string, Material> mats, StringBuilder log)
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
            // The wheels' object take is read (not played): `Wheels` measures
            // their axes off it.
            mi.importAnimation = true;
            mi.animationType = ModelImporterAnimationType.Legacy;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            // Drop any earlier remaps so the FBX's own (suffixed) names show.
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();

            var fbxNames = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (fbxNames.Count == 0) throw new System.Exception(ModelPath + ": no embedded materials to map");
            var seen = new HashSet<string>();
            foreach (var n in fbxNames)
            {
                // The concept's new parts (brick, iron, the saw table) carry
                // no material, only `GameColor`: the plain, map-less one.
                string stem = n == "No Name" ? Plain : Stem(n);
                if (!mats.TryGetValue(stem, out var target))
                    throw new System.Exception($"{ModelPath}: FBX material '{n}' matches none of {string.Join(", ", mats.Keys)}");
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), target);
                seen.Add(stem);
                log.AppendLine($"remap {n} -> {target.name}");
            }
            foreach (var k in mats.Keys)
                if (!seen.Contains(k)) throw new System.Exception($"{ModelPath}: no FBX material with stem {k}");
            mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || !mats.ContainsValue(m))
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

        /// The rotation taking the imported model's axes to the wrapper
        /// frame, from the markers alone.
        static Quaternion MeasureFrame(StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                // Native rotation kept: `Wrap` applies `frame` on top of it.
                go.transform.position = Vector3.zero;
                var t = go.transform;
                Vector3 pick = Find(t, "Input_Pickup").position;
                Vector3 drop = Find(t, "Output_Dropoff").position;
                Vector3 gate = Find(t, "Worker_Approach").position;
                Vector3 stand = Find(t, "Worker_Stand").position;
                Vector3 bench = Find(t, "Bench_Anchor").position;
                Vector3 door = Find(t, "Entrance_Anchor").position;

                // Up: the plane of three ground marks, toward the raised stand.
                Vector3 up = Vector3.Cross(drop - pick, gate - pick).normalized;
                if (Vector3.Dot(up, stand - bench) < 0f) up = -up;
                // Front along bench -> entrance: both sit on the kit's centre
                // line (Blender x = 0). The rear gate is 0.5 m off it, so a
                // gate -> entrance front turned the whole mill ~8.6 degrees.
                Vector3 front = Vector3.ProjectOnPlane(door - bench, up).normalized;
                if (front.sqrMagnitude < 0.5f) throw new System.Exception("bench and entrance coincide: no front");
                var q = Quaternion.Inverse(Quaternion.LookRotation(front, up));
                float inputX = (q * (pick - door)).x;
                log.AppendLine($"frame: up {up:F3} front {front:F3} -> input side at wrapper x {inputX:F2}");
                if (inputX <= 0f)
                    throw new System.Exception("input side lands on -X: the FBX is mirrored against the wrapper frame");
                return q;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // --- the wrapper -----------------------------------------------------------

        static void Wrap(Quaternion frame, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Wrapper) == null)
            {
                var fresh = new GameObject("sawmill_l2");
                PrefabUtility.SaveAsPrefabAsset(fresh, Wrapper);
                Object.DestroyImmediate(fresh);
            }
            string guid = AssetDatabase.AssetPathToGUID(Wrapper);

            var contents = PrefabUtility.LoadPrefabContents(Wrapper);
            try
            {
                var root = contents.transform;
                if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity)
                    throw new System.Exception("sawmill wrapper root is not at unit scale/identity");
                // The old visual goes; the root and its own components stay.
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.name = "LumberMill_L2";
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

                // Every name, exactly once.
                foreach (var n in Required()) Find(root, n);

                // Markers where the README puts them.
                foreach (var kv in Marks)
                {
                    Vector3 at = root.InverseTransformPoint(Find(root, kv.Key).position);
                    log.AppendLine($"{kv.Key}: {at:F3} (want {kv.Value:F2})");
                    if ((at - kv.Value).magnitude > MarkTolerance)
                        throw new System.Exception($"{kv.Key} at {at:F3}, README says {kv.Value:F2}: frame or scale is off");
                }

                // Slot groups: what StationStockView will find.
                int logs = Slots(Find(root, "Input_Container"), "Input_");
                int planks = Slots(Find(root, "Output_Container"), "Output_");
                log.AppendLine($"slots: {logs} logs, {planks} planks");
                if (logs != 6 || planks != 12)
                    throw new System.Exception($"slot counts {logs}/{planks}, want 6/12");
                // The log on the saw table: level 1's bench name, so it
                // shows only while he works (`StationStockView`).
                var cutting = Find(root, "Bench2_Cutting");
                if (cutting.parent != Find(root, "Bench_Anchor"))
                    throw new System.Exception("Bench2_Cutting is not under Bench_Anchor");
                cutting.name = "Bench_Cutting";

                Wheels(root, model, log);

                // **The approach moves (2026-10-01).** Level 1's rear gate
                // (0.50, -1.80) is inside the level 2 back wall (bricks to
                // 0.65 m, boards above), so a sawyer could not reach his
                // stand by it. The way in is past the grindstone (not a solid,
                // `BuildingSolidsBake`) and the sign post, from the back-right corner outside the
                // plinth: `Worker_Approach` stands there, and the stand ->
                // approach line clears both (`CampPath.ViaApproach`).
                var gate = Find(root, "Worker_Approach");
                gate.position = root.TransformPoint(ApproachL2);
                log.AppendLine($"Worker_Approach moved to {ApproachL2:F2} (level 1's rear gate is inside the back wall)");

                // Bounds of every variant inside the plan's footprint/ridge,
                // centred on the root.
                var b = Bounds(root, _ => true);
                log.AppendLine($"bounds (all variants) {b.min:F2}..{b.max:F2}");
                if (Mathf.Abs(b.min.x) > FootX * 0.5f || Mathf.Abs(b.max.x) > FootX * 0.5f
                    || Mathf.Abs(b.min.z) > FootZ * 0.5f || Mathf.Abs(b.max.z) > FootZ * 0.5f)
                    throw new System.Exception($"bounds {b.min:F2}..{b.max:F2} leave the {FootX} x {FootZ} footprint");
                if (b.max.y > Ridge) throw new System.Exception($"top {b.max.y:F2} m is over the {Ridge} m ridge");
                if (b.min.y < -0.2f || b.min.y > 0.05f)
                    throw new System.Exception($"lowest point {b.min.y:F2} m: the root is not on the ground");

                int all = Tris(root, _ => true);
                int empty = Tris(root, t => !IsStock(t) && !IsBenchVariant(t));
                var rs = root.GetComponentsInChildren<MeshRenderer>(true);
                log.AppendLine($"tris: empty {empty}, all {all}; renderers {rs.Length}, submeshes {rs.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.subMeshCount)}");

                Pad(root, log);

                // Saved as an EMPTY mill: stock and bench states hidden
                // (`StationStockView` shows the ledger's truth once raised).
                // The three result boards stay active under their hidden
                // parent; the view counts them itself.
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string s = Stem(t.name);
                    if (IsSlotName(s) || s == "Bench_Cutting")
                        t.gameObject.SetActive(false);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, Wrapper);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(Wrapper) != guid)
                throw new System.Exception("sawmill wrapper GUID changed");
            log.AppendLine("wrapper saved, GUID kept " + guid);
        }

        /// The plinth's walking top (its faces at the stand's 0.16 m, the
        /// proud bricks and footing courses below it left out), as a
        /// `Worker_Pad` child the runtime grounds bodies on.
        static void Pad(Transform root, StringBuilder log)
        {
            var mf = Find(root, "Mill2_BrickPlinth").GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) throw new System.Exception("Mill2_BrickPlinth has no mesh");
            var pts = mf.sharedMesh.vertices.Select(v => root.InverseTransformPoint(mf.transform.TransformPoint(v))).ToList();
            float standY = root.InverseTransformPoint(Find(root, "Worker_Stand").position).y;
            log.AppendLine($"plinth y {pts.Min(p => p.y):F3}..{pts.Max(p => p.y):F3}");
            var topPts = pts.Where(p => Mathf.Abs(p.y - standY) < 0.015f).ToList();
            if (topPts.Count < 3) throw new System.Exception("no plinth faces at the stand's height " + standY.ToString("F3"));
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

        /// The two wheels' spin, measured off the FBX's object take: the
        /// local axis each turns about (sign included: crank `-360 deg x t`),
        /// how many turns per loop, and the loop length. Handed to a
        /// `MillCrankWheels` on the model root.
        static void Wheels(Transform root, GameObject model, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) throw new System.Exception(ModelPath + ": no wheel take");
            var crank = Find(root, "Mill2_CrankWheel");
            var saw = Find(root, "Saw2_Wheel");
            var probe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                Transform pc = Find(probe.transform, "Mill2_CrankWheel"), ps = Find(probe.transform, "Saw2_Wheel");
                const float dt = 1f / 120f;
                clip.SampleAnimation(probe, 0f);
                Quaternion c0 = pc.localRotation, s0 = ps.localRotation;
                clip.SampleAnimation(probe, dt);
                (Quaternion.Inverse(c0) * pc.localRotation).ToAngleAxis(out float ca, out Vector3 cAxis);
                (Quaternion.Inverse(s0) * ps.localRotation).ToAngleAxis(out float sa, out Vector3 sAxis);
                if (ca > 180f) { ca = 360f - ca; cAxis = -cAxis; }
                if (sa > 180f) { sa = 360f - sa; sAxis = -sAxis; }
                float cTurns = ca / dt * clip.length / 360f, sTurns = sa / dt * clip.length / 360f;
                // The whole loop: back where it started.
                clip.SampleAnimation(probe, clip.length);
                float cEnd = Quaternion.Angle(c0, pc.localRotation), sEnd = Quaternion.Angle(s0, ps.localRotation);
                log.AppendLine($"wheels: take '{clip.name}' {clip.length:F3} s; crank axis {cAxis:F3} {cTurns:F2} turns, saw axis {sAxis:F3} {sTurns:F2} turns; loop closes {cEnd:F1}/{sEnd:F1} deg");
                if (Mathf.Abs(clip.length - 1.2f) > 0.05f) throw new System.Exception("wheel take is not 1.2 s");
                if (Mathf.Abs(cTurns - 1f) > 0.1f || Mathf.Abs(sTurns - 5f) > 0.3f)
                    throw new System.Exception("wheel turns are not 1 and 5 per loop");
                var w = model.AddComponent<MillCrankWheels>();
                w.Configure(crank, c0, cAxis, 1f, saw, s0, sAxis, 5f, clip.length);
                // Saved at the take's frame 0.
                crank.localRotation = c0;
                saw.localRotation = s0;
            }
            finally { Object.DestroyImmediate(probe); }
        }

        static bool IsSlotName(string s)
            => Regex.IsMatch(s, @"^(Input_Log|Output_Plank)_\d+$");

        static bool IsStock(Transform t)
        {
            for (var p = t; p != null; p = p.parent) if (IsSlotName(Stem(p.name))) return true;
            return false;
        }

        static bool IsBenchVariant(Transform t)
            => Under(t, "Bench_Cutting");

        static bool Under(Transform t, string stem)
        {
            for (var p = t; p != null; p = p.parent) if (Stem(p.name) == stem) return true;
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
