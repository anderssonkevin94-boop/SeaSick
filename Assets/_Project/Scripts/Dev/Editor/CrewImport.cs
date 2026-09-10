using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SeaSick.Dev
{
    /// Binds tools/blender/seasick_crew.py's FBX, and then MEASURES what Unity
    /// actually built from it.
    ///
    /// Every trap the fleet import hit was a CORRECT asset rendering wrong, and
    /// none of them were visible in a screenshot: materialLocation defaulting to
    /// External so Unity quietly extracts its own material and the remap binds
    /// nothing; the remap key being the name UNITY gave the material rather than
    /// Blender's. Both apply here, so both are handled and then read back.
    ///
    /// The crew add two of their own:
    ///   - a clip that does not loop is a crew member who walks once and freezes;
    ///   - vertex colour is the entire colour scheme, so if the importer drops it
    ///     or gamma-shifts it, the whole asset is grey and nothing says so.
    public static class CrewImport
    {
        const string Fbx = "Assets/_Project/Art/Crew/crew.fbx";
        const string MatDir = "Assets/_Project/Materials/Crew";
        const string ShaderName = "SeaSick/Crew Vertex Color";

        // Blender's object names. Unity may rename what it imports, which is
        // exactly why nothing below matches on these except as a hint.
        const string Cloth = "SS_CrewCloth";
        const string Skin = "SS_CrewSkin";

        [MenuItem("SeaSick/Crew/Bind the crew materials")]
        public static void Bind()
        {
            var imp = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (imp == null) { Debug.LogError("[Crew] no importer at " + Fbx); return; }

            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = true;
            imp.optimizeGameObjects = false;
            imp.importNormals = ModelImporterNormals.Import;   // flat shading is IN the mesh
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.animationCompression = ModelImporterAnimationCompression.Off;

            // Every clip loops. A crew member whose idle plays once is a statue,
            // and Unity's default for an imported take is loopTime = false.
            var clips = imp.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                int bar = clips[i].name.LastIndexOf('|');
                if (bar >= 0) clips[i].name = clips[i].name.Substring(bar + 1);
                clips[i].loopTime = true;
                clips[i].loopPose = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            // The remap key is the name UNITY gave the material, not Blender's.
            // Once the remap is in place the FBX no longer carries embedded
            // materials, so this comes back EMPTY on every run after the
            // first -- and an empty list silently remaps nothing, which looks
            // identical to success on a machine where the remap was lost.
            // Fall back to the names the generator writes.
            var names = AssetDatabase.LoadAllAssetsAtPath(Fbx)
                .OfType<Material>().Select(m => m.name).Distinct().ToList();
            if (names.Count == 0) names = new List<string> { Cloth, Skin };
            var sh = Shader.Find(ShaderName);
            if (sh == null) { Debug.LogError("[Crew] shader not found: " + ShaderName); return; }
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "Crew");

            foreach (var n in names)
            {
                bool isSkin = n.ToLowerInvariant().Contains("skin");
                string path = MatDir + "/" + (isSkin ? "Crew_Skin" : "Crew_Cloth") + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(sh) { name = isSkin ? "Crew_Skin" : "Crew_Cloth" };
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = sh;
                // Cloth: vertex colour IS the colour, so the tint must be white
                // or every garment is multiplied twice. Skin: this is the value
                // CrewAgent overwrites per frame; it is only the resting look.
                mat.SetColor("_BaseColor", isSkin
                    ? new Color(0.87f, 0.65f, 0.48f, 1f) : Color.white);
                EditorUtility.SetDirty(mat);
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
            }
            AssetDatabase.SaveAssets();
            imp.SaveAndReimport();
            Debug.Log("[Crew] remapped " + string.Join(", ", names));
            Check();
        }

        [MenuItem("SeaSick/Crew/Check the import")]
        public static void Check()
        {
            var sb = new StringBuilder("[Crew] import check\n");
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (root == null) { Debug.LogError("[Crew] nothing at " + Fbx); return; }

            var go = Object.Instantiate(root);
            try
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendFormat("  height {0:F3} m (WorldScale.Person is 1.700)\n", b.size.y);
                sb.AppendFormat("  beam {0:F3} m across, {1:F3} m fore-and-aft\n",
                                b.size.x, b.size.z);

                // Which way does he face? The cap brim is the only thing that
                // sticks out horizontally, so the furthest vertex from the
                // centreline at brim height IS the front.
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
                sb.AppendFormat("  {0} skinned renderer(s), {1} bone(s)\n",
                                smrs.Length, smrs.Length > 0 ? smrs[0].bones.Length : 0);
                foreach (var smr in smrs)
                {
                    var m = smr.sharedMesh;
                    var cols = m.colors;
                    float lo = 9f, hi = -9f;
                    foreach (var c in cols) { lo = Mathf.Min(lo, c.r); hi = Mathf.Max(hi, c.r); }
                    sb.AppendFormat("  {0,-12} tris {1,4}  verts {2,4}  vcol {3}  " +
                                    "red {4:F3}..{5:F3}  mat {6}\n",
                                    m.name, m.triangles.Length / 3, m.vertexCount,
                                    cols.Length > 0 ? "YES" : "MISSING",
                                    cols.Length > 0 ? lo : 0f, cols.Length > 0 ? hi : 0f,
                                    smr.sharedMaterial != null
                                        ? smr.sharedMaterial.name + " / " +
                                          smr.sharedMaterial.shader.name : "NONE");
                }

                float front = -99f, back = 99f;
                foreach (var smr in smrs)
                    foreach (var v in smr.sharedMesh.vertices)
                        if (v.y > 1.55f) { front = Mathf.Max(front, v.z); back = Mathf.Min(back, v.z); }
                sb.AppendFormat("  cap brim reaches {0:+0.000;-0.000} m on Z, back of head {1:+0.000;-0.000} m\n",
                                front, back);
                sb.AppendFormat("  -> faces {0}\n",
                                front > -back ? "+Z (correct for Unity)" : "-Z (WRONG, needs a yaw)");
            }
            finally { Object.DestroyImmediate(go); }

            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>())
                sb.AppendFormat("  clip {0,-10} {1:F2}s  loop={2}\n",
                                c.name, c.length, c.isLooping);
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------ wiring --

        const string CtrlDir = "Assets/_Project/Animation/Crew";
        const string CtrlPath = CtrlDir + "/CrewAnimator.controller";
        const string Prefab = "Assets/_Project/Prefabs/CrewMember.prefab";

        [MenuItem("SeaSick/Crew/Build the animator")]
        public static AnimatorController BuildController()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Animation"))
                AssetDatabase.CreateFolder("Assets/_Project", "Animation");
            if (!AssetDatabase.IsValidFolder(CtrlDir))
                AssetDatabase.CreateFolder("Assets/_Project/Animation", "Crew");

            // Unity leaves __preview__ copies of every take in the model; they
            // are the inspector's scrub clips, not the ones to play.
            var clips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToDictionary(c => c.name, c => c);
            if (!clips.ContainsKey("Crew_Idle") || !clips.ContainsKey("Crew_Walk"))
            { Debug.LogError("[Crew] clips missing; run Bind first"); return null; }

            AssetDatabase.DeleteAsset(CtrlPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = ac.layers[0].stateMachine;

            var idle = sm.AddState("Idle");
            idle.motion = clips["Crew_Idle"];
            var walk = sm.AddState("Walk");
            walk.motion = clips["Crew_Walk"];
            sm.defaultState = idle;

            // Hysteresis, not one threshold: a crew member easing to a stop
            // sits on a single boundary for a second or two and flickers
            // between the two clips while they do it.
            var go = idle.AddTransition(walk);
            go.hasExitTime = false; go.duration = 0.10f;
            go.AddCondition(AnimatorConditionMode.Greater, 0.30f, "Speed");

            var stop = walk.AddTransition(idle);
            stop.hasExitTime = false; stop.duration = 0.16f;
            stop.AddCondition(AnimatorConditionMode.Less, 0.18f, "Speed");

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        [MenuItem("SeaSick/Crew/Wire the prefab")]
        public static void Wire()
        {
            var ac = BuildController();
            if (ac == null) return;
            // Bind() reimports the FBX, which destroys every object loaded
            // from it. Anything held across that call is fake-null.
            AssetDatabase.Refresh();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null) { Debug.LogError("[Crew] no model at " + Fbx); return; }

            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                // Out with the sphere and the capsule.
                foreach (var t in root.transform.Cast<Transform>().ToList())
                    if (t.GetComponent<MeshFilter>() != null || t.name == "Visual")
                        Object.DestroyImmediate(t.gameObject);

                var vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
                vis.name = "Visual";
                vis.transform.SetParent(root.transform, false);
                vis.transform.localPosition = Vector3.zero;
                vis.transform.localRotation = Quaternion.identity;
                vis.transform.localScale = Vector3.one;

                // `??` is the wrong operator on a UnityEngine.Object: it uses
                // real null, not Unity's overloaded ==, so a fake-null
                // component (one whose native side is gone after a reimport)
                // sails straight through the coalesce and every use of it
                // throws "there is no Animator attached". Check it explicitly.
                var anim = vis.GetComponent<Animator>();
                if (anim == null) anim = vis.AddComponent<Animator>();
                if (anim == null)
                { Debug.LogError("[Crew] could not put an Animator on the visual"); return; }
                anim.runtimeAnimatorController = ac;
                anim.applyRootMotion = false;   // CrewAgent owns the transform
                anim.updateMode = AnimatorUpdateMode.Normal;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var skin = vis.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Where(r => r.sharedMaterial != null && r.sharedMaterial.name
                        .IndexOf("skin", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .Cast<Renderer>().ToArray();
                if (skin.Length == 0)
                    Debug.LogWarning("[Crew] no skin renderer found; tint will fall back");

                var agent = root.GetComponent<Crew.CrewAgent>();
                var so = new SerializedObject(agent);
                var tr = so.FindProperty("tintRenderers");
                tr.arraySize = skin.Length;
                for (int i = 0; i < skin.Length; i++)
                    tr.GetArrayElementAtIndex(i).objectReferenceValue = skin[i];
                so.FindProperty("animator").objectReferenceValue = anim;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                Debug.LogFormat("[Crew] wired: visual '{0}', animator '{1}', " +
                                "tintRenderers {2} ({3})",
                                vis.name, ac.name, skin.Length,
                                skin.Length > 0 ? skin[0].name : "-");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            CheckPrefab();
        }

        [MenuItem("SeaSick/Crew/Check the prefab")]
        public static void CheckPrefab()
        {
            var sb = new StringBuilder("[Crew] prefab check\n");
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var go = Object.Instantiate(pf);
            try
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendFormat("  stands {0:F3} m, feet at y={1:F3} (origin should be the sole)\n",
                                b.size.y, b.min.y);
                sb.AppendFormat("  renderers {0}, primitives left over: {1}\n", rs.Length,
                    go.GetComponentsInChildren<MeshFilter>().Count(m =>
                        m.sharedMesh != null && (m.sharedMesh.name == "Sphere" ||
                                                 m.sharedMesh.name == "Capsule")));
                var an = go.GetComponentInChildren<Animator>();
                sb.AppendFormat("  animator {0}, controller {1}, rootMotion {2}\n",
                                an != null ? "yes" : "MISSING",
                                an != null && an.runtimeAnimatorController != null
                                    ? an.runtimeAnimatorController.name : "NONE",
                                an != null && an.applyRootMotion);
                var agent = go.GetComponent<Crew.CrewAgent>();
                var so = new SerializedObject(agent);
                var tr = so.FindProperty("tintRenderers");
                for (int i = 0; i < tr.arraySize; i++)
                {
                    var r = tr.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                    sb.AppendFormat("  tints {0} ({1})\n", r != null ? r.name : "null",
                        r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "-");
                }
                if (tr.arraySize == 0) sb.Append("  tints NOTHING SET -- will fall back\n");
            }
            finally { Object.DestroyImmediate(go); }
            Debug.Log(sb.ToString());
        }

        /// Does the clip actually reach the bones?
        ///
        /// An imported Generic clip addresses its curves by PATH, relative to
        /// whatever object carries the Animator. Put the Animator one level up
        /// -- on the crew root instead of on the model root -- and every path
        /// misses by one segment: the controller runs, the state machine
        /// transitions, the profiler shows the clip playing, and the crew
        /// member stands perfectly still. Nothing reports it. So sample the
        /// clip in edit mode and measure whether a foot actually moved.
        [MenuItem("SeaSick/Crew/Does the animation reach the bones")]
        public static void CheckAnimation()
        {
            var sb = new StringBuilder("[Crew] animation binding\n");
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var go = Object.Instantiate(pf);
            try
            {
                var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault();
                if (smr == null) { Debug.LogError("[Crew] no skinned mesh"); return; }
                var anim = go.GetComponentInChildren<Animator>();
                if (anim == null) { Debug.LogError("[Crew] no animator"); return; }
                var foot = smr.bones.FirstOrDefault(b => b.name == "leg_L");
                var arm = smr.bones.FirstOrDefault(b => b.name == "arm_L");
                if (foot == null) { Debug.LogError("[Crew] no leg_L bone"); return; }

                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(Fbx)
                             .OfType<AnimationClip>()
                             .Where(c => !c.name.StartsWith("__preview__"))
                             .OrderBy(c => c.name))
                {
                    float lo = 9f, hi = -9f, alo = 9f, ahi = -9f;
                    for (int i = 0; i <= 16; i++)
                    {
                        // SampleAnimation resolves curve paths relative to the
                        // object it is handed, and the clips are authored
                        // relative to the ANIMATOR's object (Visual), not to
                        // the crew root one level above it. Handing it the
                        // wrong root does not fail loudly -- it flings things.
                        clip.SampleAnimation(anim.gameObject, clip.length * i / 16f);
                        if (i == 0)
                            sb.AppendFormat("    [{0}] root at {1}, foot at {2}\n",
                                clip.name, go.transform.position, foot.position);
                        // local Z of the tip, in the crew member's own frame:
                        // how far fore-and-aft the limb has swung.
                        float z = anim.transform.InverseTransformPoint(
                            foot.TransformPoint(new Vector3(0f, -0.80f, 0f))).z;
                        lo = Mathf.Min(lo, z); hi = Mathf.Max(hi, z);
                        float az = anim.transform.InverseTransformPoint(
                            arm.TransformPoint(new Vector3(0f, -0.65f, 0f))).z;
                        alo = Mathf.Min(alo, az); ahi = Mathf.Max(ahi, az);
                    }
                    sb.AppendFormat("  {0,-10} foot swings {1:F3} m fore-and-aft, " +
                                    "hand {2:F3} m  -> {3}\n",
                                    clip.name, hi - lo, ahi - alo,
                                    (hi - lo) > 0.002f ? "REACHES THE BONES"
                                                       : "NOTHING MOVED");
                }
            }
            finally { Object.DestroyImmediate(go); }
            Debug.Log(sb.ToString());
        }

        public static void Execute() { Bind(); Wire(); CheckAnimation(); }
    }
}
