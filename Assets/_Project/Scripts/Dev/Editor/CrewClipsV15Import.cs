using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SeaSick.World;
using Object = UnityEngine.Object;

namespace SeaSick.Dev
{
    /// **The v15 deckhand's authored work clips (2026-10-01)**, from the asset
    /// agent's `deckhand-v15-anims.fbx` (seasick_assets, crew-meshy-v15/anims).
    ///
    /// Only five of its 35 takes are imported -- `Saw` (level 1 lumber mill,
    /// lowered bench), `Chop` (a tree), `Mine` (a rock), `Carry` (a load held
    /// out on both arms, walking in place) and `Lookout` (watchtower) -- and
    /// only the clips: the FBX's mesh is never used, the game keeps
    /// `Art/AstraPlaytest/Crew/Deckhand.fbx` (same 16 bones, same paths).
    /// Generic, no avatar, exactly like Deckhand.fbx, so the clips bind by
    /// transform path.
    ///
    /// `AddStates` puts them in `CrewAnimator` as states named after the
    /// `VillagerActing.Mode` they drive (no transitions: `VillagerActing`
    /// cross-fades in and back out to Idle/Walk). `AstraPlaytestImport`
    /// rebuilds that controller from scratch and calls `AddStates` at the
    /// end, so re-running either importer keeps them.
    ///
    /// Idempotent. To add another clip: put its take in `Takes`, a state of
    /// the same name in `VillagerActing.ClipModes`.
    public static class CrewClipsV15Import
    {
        public const string Fbx = "Assets/_Project/Art/CrewClips/deckhand-v15-anims.fbx";
        const string Controller = "Assets/_Project/Resources/AstraPlaytest/CrewAnimator.controller";
        const string Crew = "Assets/_Project/Prefabs/CrewMember.prefab";
        const string TakePrefix = "Deckhand_Rig|Crew_";

        /// State names (= `VillagerActing.Mode`); the take is `Crew_<name>`. All loop.
        static readonly string[] Takes = { "Saw", "Chop", "Mine", "Carry", "Lookout" };

        [MenuItem("SeaSick/Art/Import v15 crew work clips (Saw, Chop, Mine, Carry, Lookout)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            ConfigureFbx(log);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            if (controller == null) throw new Exception("missing " + Controller + " (run AstraPlaytestImport first)");
            AddStates(controller, log);
            AssetDatabase.SaveAssets();
            Verify(log);
            Debug.Log("[CrewClipsV15] " + log);
            return log.ToString();
        }

        static void ConfigureFbx(StringBuilder log)
        {
            AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (mi == null) throw new Exception("no model importer at " + Fbx);
            mi.globalScale = 1f;
            mi.useFileScale = true;              // same units as Deckhand.fbx: bone positions match
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;   // Deckhand.fbx: Generic, no avatar
            mi.importAnimation = true;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.resampleCurves = true;

            var infos = mi.importedTakeInfos;
            var clips = Takes.Select(name =>
            {
                var take = infos.FirstOrDefault(t => t.name == TakePrefix + name);
                if (string.IsNullOrEmpty(take.name)) throw new Exception(Fbx + " has no take " + TakePrefix + name);
                float fps = take.sampleRate > 0 ? take.sampleRate : 30f;
                return new ModelImporterClipAnimation
                {
                    name = "Crew_" + name,
                    takeName = take.name,
                    firstFrame = Mathf.Round(take.startTime * fps),
                    lastFrame = Mathf.Round(take.stopTime * fps),
                    loopTime = true,
                    wrapMode = WrapMode.Loop,
                };
            }).ToArray();
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
            foreach (var c in clips) log.AppendLine($"clip {c.name}: frames {c.firstFrame}-{c.lastFrame}");
        }

        public static AnimationClip Clip(string name)
            => AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == "Crew_" + name && !c.name.StartsWith("__preview__"));

        /// Add (or refresh) the five states and the `ClipRate` parameter.
        /// Safe to call on a controller `AstraPlaytestImport` just rebuilt.
        public static void AddStates(AnimatorController controller, StringBuilder log)
        {
            if (!controller.parameters.Any(p => p.name == "ClipRate"))
                controller.AddParameter(new AnimatorControllerParameter
                    { name = "ClipRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var sm = controller.layers[0].stateMachine;
            for (int i = 0; i < Takes.Length; i++)
            {
                string name = Takes[i];
                var clip = Clip(name);
                if (clip == null) throw new Exception("no clip Crew_" + name + " in " + Fbx + " (run CrewClipsV15Import)");
                var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
                if (state == null) state = sm.AddState(name, new Vector3(520f, 40f + 70f * i, 0f));
                state.motion = clip;
                state.writeDefaultValues = true;
                // Carry walks in place: VillagerActing sets ClipRate from his
                // ground speed (0 while he stands).
                state.speedParameterActive = name == "Carry";
                state.speedParameter = name == "Carry" ? "ClipRate" : "";
                log?.AppendLine($"state {name} <- {clip.name} ({clip.length:F2} s, loop {clip.isLooping})");
            }
            EditorUtility.SetDirty(controller);
        }

        /// Every clip reaches the crew's bones; the prop offsets in
        /// `VillagerActing` still match the rig's rest pose.
        static void Verify(StringBuilder log)
        {
            var crew = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Crew));
            try
            {
                var vis = crew.transform.Find("Visual").gameObject;
                var all = vis.GetComponentsInChildren<Transform>(true);
                Transform hand = all.First(t => t.name == "hand.L"), spine = all.First(t => t.name == "spine");
                var rig = vis.transform.Find("Deckhand_Rig");

                // Rest pose (the prefab is saved unanimated): the tool frame
                // is the asset script's `tool_rest`, the socket its
                // `CARRY_SOCKET`, both in Blender source metres -> rig local
                // (-x, y, z) / 100.
                Vector3 toolAt = rig.TransformPoint(new Vector3(0.006398f, 0f, 0.00746f));
                Quaternion toolRot = Quaternion.LookRotation(rig.TransformDirection(Vector3.right), rig.TransformDirection(Vector3.down));
                Vector3 haveAt = hand.TransformPoint(VillagerActing.ToolGripLocal);
                float rotErr = Quaternion.Angle(hand.rotation * VillagerActing.ToolGripRot, toolRot);
                Vector3 sockAt = rig.TransformPoint(new Vector3(0f, -0.0025f, 0.00585f));
                Vector3 haveSock = spine.TransformPoint(VillagerActing.CarrySocketLocal);
                log.AppendLine($"tool grip rest {haveAt:F3} vs {toolAt:F3} ({Vector3.Distance(haveAt, toolAt) * 100f:F1} cm, {rotErr:F1} deg); "
                    + $"carry socket {haveSock:F3} vs {sockAt:F3}");
                if (Vector3.Distance(haveAt, toolAt) > 0.01f || rotErr > 2f || Vector3.Distance(haveSock, sockAt) > 0.01f)
                    log.AppendLine("  !! VillagerActing.ToolGripLocal/ToolGripRot/CarrySocketLocal no longer match this rig: re-measure");

                foreach (var name in Takes)
                {
                    var clip = Clip(name);
                    clip.SampleAnimation(vis, 0f);
                    Vector3 a = hand.position;
                    clip.SampleAnimation(vis, clip.length * 0.4f);
                    float moved = Vector3.Distance(a, hand.position);
                    log.AppendLine($"{name}: right fist moves {moved:F2} m over 40% of the clip");
                    if (name != "Lookout" && moved < 0.02f) throw new Exception(name + " clip did not reach the crew bones");
                }
            }
            finally { Object.DestroyImmediate(crew); }
        }
    }
}
