using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SeaSick.Ship.SeaLife
{
    /// **Astra's large fish (approved 2026-09-29, `art-staging/large-fish-v1`).**
    ///
    /// The FBX is copied to `Art/SeaLife/LargeFish/large-fish-rigged.fbx`
    /// (art-staging stays the source). This postprocessor pins its import:
    ///
    /// - Generic rig, animation imported, no embedded materials (the six
    ///   palette materials are only a fallback; `GameColor` vertex colours
    ///   carry the palette), no cameras/lights, normals IMPORTED (the facets
    ///   are in the mesh), no blend shapes.
    /// - The FBX's only take is called "Scene" (Blender's anim stack); it is
    ///   renamed `Swim_Slow_Loop`, keeps its full imported range (frames
    ///   1-49 at 24 fps, 2 s) and gets Loop Time. Endpoints already match, so
    ///   no Loop Pose blending.
    /// - The six palette submeshes are merged into ONE, so the fish is one
    ///   draw with one vertex-colour material -- and so a second material slot
    ///   (`FishUnderwater`, phone tier) re-draws the whole mesh.
    ///
    /// Scale: the file carries the usual Blender x100 on its two root nodes
    /// (fileScale 0.01); world size is right at globalScale 1 -- measured
    /// 5.92 m long, dorsal fin top 1.60 m above the pivot. Orientation: the
    /// head is on Unity -X (tail bones run toward +X), so the art child is
    /// yawed +90 deg to face the movement root's +Z.
    ///
    /// `BuildPrefab` (menu SeaSick/Sea Life/Build large fish prefab) makes
    /// the materials, the one-state AnimatorController and
    /// `Resources/SeaLife/LargeFish.prefab`. Idempotent.
    public class LargeFishImport : AssetPostprocessor
    {
        public const string Dir = "Assets/_Project/Art/SeaLife/LargeFish";
        public const string Fbx = Dir + "/large-fish-rigged.fbx";
        const string ClipName = "Swim_Slow_Loop";
        const string PrefabDir = "Assets/_Project/Resources/SeaLife";
        const string PrefabPath = PrefabDir + "/LargeFish.prefab";
        const string OpaqueMatPath = Dir + "/LargeFish_Body.mat";
        const string UnderwaterMatPath = Dir + "/LargeFish_Underwater.mat";
        const string ControllerPath = Dir + "/LargeFish.controller";
        const string OpaqueShader = "SeaSick/Crew Vertex Color";
        const string UnderwaterShader = "SeaSick/Fish Underwater";

        bool Mine => assetPath == Fbx;

        void OnPreprocessModel()
        {
            if (!Mine) return;
            var imp = (ModelImporter)assetImporter;
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = true;
            imp.optimizeGameObjects = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importNormals = ModelImporterNormals.Import;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importVisibility = false;
            imp.isReadable = false;
            imp.meshCompression = ModelImporterMeshCompression.Off;
        }

        void OnPreprocessAnimation()
        {
            if (!Mine) return;
            var imp = (ModelImporter)assetImporter;
            var clips = imp.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            // One take in the file; keep its full imported range.
            var c = clips[0];
            c.name = ClipName;
            c.loopTime = true;
            c.loopPose = false;
            imp.clipAnimations = new[] { c };
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!Mine) return;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = smr.sharedMesh;
                if (m == null || m.subMeshCount <= 1) continue;
                var all = new List<int>(m.triangles.Length);
                for (int s = 0; s < m.subMeshCount; s++) all.AddRange(m.GetTriangles(s));
                m.subMeshCount = 1;
                m.SetTriangles(all, 0, true);
                var mats = smr.sharedMaterials;
                smr.sharedMaterials = mats.Length > 0 ? new[] { mats[0] } : mats;
            }
        }

        [MenuItem("SeaSick/Sea Life/Build large fish prefab")]
        public static string BuildPrefab()
        {
            AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (fbx == null) return "[LargeFish] no FBX at " + Fbx;

            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx))
                if (o is AnimationClip ac && ac.name == ClipName) clip = ac;
            if (clip == null) return "[LargeFish] clip " + ClipName + " missing";

            var opaque = MakeMat(OpaqueMatPath, OpaqueShader, "LargeFish_Body");
            if (opaque == null) return "[LargeFish] shader missing: " + OpaqueShader;
            opaque.SetColor("_BaseColor", Color.white);   // vertex colour IS the colour
            var underwater = MakeMat(UnderwaterMatPath, UnderwaterShader, "LargeFish_Underwater");
            if (underwater == null) return "[LargeFish] shader missing: " + UnderwaterShader;
            EditorUtility.SetDirty(opaque);
            EditorUtility.SetDirty(underwater);

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, clip);
            else
            {
                var sm = ctrl.layers[0].stateMachine;
                if (sm.defaultState == null) sm.defaultState = sm.AddState(ClipName);
                sm.defaultState.motion = clip;
                EditorUtility.SetDirty(ctrl);
            }

            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/_Project/Resources", "SeaLife");

            var rootGo = new GameObject("LargeFish");
            try
            {
                var art = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                art.name = "Art";
                art.transform.SetParent(rootGo.transform, false);
                art.transform.localPosition = Vector3.zero;
                art.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);  // head (-X) -> root +Z

                var anim = art.GetComponent<Animator>();
                if (anim == null) anim = art.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var smr = art.GetComponentInChildren<SkinnedMeshRenderer>();
                smr.sharedMaterials = new[] { opaque };
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.skinnedMotionVectors = false;
                smr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                smr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                var fish = rootGo.AddComponent<LargeFish>();
                var so = new SerializedObject(fish);
                so.FindProperty("animator").objectReferenceValue = anim;
                so.FindProperty("body").objectReferenceValue = smr;
                so.FindProperty("opaqueMaterial").objectReferenceValue = opaque;
                so.FindProperty("underwaterMaterial").objectReferenceValue = underwater;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
            }
            finally { Object.DestroyImmediate(rootGo); }
            AssetDatabase.SaveAssets();
            return Check();
        }

        static Material MakeMat(string path, string shaderName, string name)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null) return null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = sh;
            return mat;
        }

        [MenuItem("SeaSick/Sea Life/Check large fish")]
        public static string Check()
        {
            var sb = new StringBuilder("[LargeFish] ");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return sb.Append("no prefab").ToString();
            var go = Object.Instantiate(prefab);
            try
            {
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                var m = smr.sharedMesh;
                var b = smr.bounds;
                // Head = the end away from the tail bones.
                Transform tail = null;
                foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name == "TailFin") tail = t;
                sb.AppendFormat("tris {0} submeshes {1} vcol {2} bones {3} | size x{4:F2} y{5:F2} z{6:F2} | top {7:F2} | tailZ {8:F2} (want <0: faces +Z) | mats {9}",
                    m.triangles.Length / 3, m.subMeshCount, m.colors.Length > 0, smr.bones.Length,
                    b.size.x, b.size.y, b.size.z, b.max.y, tail != null ? tail.position.z : 0f,
                    smr.sharedMaterials.Length);
                var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
                foreach (var c in imp.clipAnimations)
                    sb.AppendFormat(" | clip {0} {1}-{2} loop {3}", c.name, c.firstFrame, c.lastFrame, c.loopTime);
            }
            finally { Object.DestroyImmediate(go); }
            return sb.ToString();
        }
    }
}
