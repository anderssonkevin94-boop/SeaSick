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
    /// **The v15 deckhand's authored clips (2026-10-01)**, from the asset
    /// agent's `deckhand-v15-anims.fbx` (seasick_assets, crew-meshy-v15/anims).
    ///
    /// EVERY take in the file (`Deckhand_Rig|Crew_<Name>`) is imported, and
    /// only the clips: the FBX's mesh is never used, the game keeps
    /// `Art/AstraPlaytest/Crew/Deckhand.fbx` (same 16 bones, same paths).
    /// Generic, no avatar, exactly like Deckhand.fbx, so the clips bind by
    /// transform path. Loops loop; the one-shots (`OneShots`, per the asset
    /// README / clips.json) play once and hold their last frame.
    ///
    /// `AddStates` puts each in `CrewAnimator` as a state named after the
    /// take (`Saw`, `SickRail`, `GunFire`...). No transitions: `VillagerActing`
    /// (camp jobs) and `CrewAgent` (aboard, ashore parties) cross-fade in and
    /// back out to Idle/Walk. **Idle and Walk are the authored v15 takes**
    /// (the generated sine clips stay in `AstraPlaytestImport` as the fallback,
    /// behind `UseGeneratedIdleWalk`). `AstraPlaytestImport` rebuilds the
    /// controller from scratch and calls `AddStates` at the end, so re-running
    /// either importer keeps them.
    ///
    /// Speed-driven states (in place, the game moves him): the walks, the
    /// runs, SickWalk and Gangway follow `WalkRate` (set by `CrewAgent` from his ground speed),
    /// Carry and HuntWalk follow `ClipRate` (set by `VillagerActing`).
    ///
    /// **Two sources.** `Fbx` (`deckhand-v15c-anims.fbx`, seasick_assets
    /// a8b6125, 42 takes) is the newest and gives every take but one;
    /// `LookoutFbx` (the 1829c65 file) keeps the REWORKED Lookout (deck
    /// corner, fists on the rail tops), which the newer file does not have
    /// and the game's corner stand spot is built for. `TakeFrom` says which.
    ///
    /// Idempotent.
    public static class CrewClipsV15Import
    {
        public const string Fbx = "Assets/_Project/Art/CrewClips/deckhand-v15c-anims.fbx";
        public const string LookoutFbx = "Assets/_Project/Art/CrewClips/deckhand-v15-anims.fbx";
        /// Takes taken from `LookoutFbx` instead of `Fbx`.
        static readonly string[] FromLookoutFbx = { "Lookout" };
        static string TakeFrom(string name) => FromLookoutFbx.Contains(name) ? LookoutFbx : Fbx;
        const string Controller = "Assets/_Project/Resources/AstraPlaytest/CrewAnimator.controller";
        const string Crew = "Assets/_Project/Prefabs/CrewMember.prefab";
        const string TakePrefix = "Deckhand_Rig|Crew_";

        /// The takes that play once and hold their last frame (clips.json
        /// `loop: false`). Everything else loops.
        static readonly string[] OneShots = { "PickUp", "SetDown", "Hunt", "SickCollapse", "ThrowLine", "GunFire" };
        /// States whose playback rate follows his ground speed.
        static readonly string[] WalkRated = { "Walk", "WalkBrisk", "WalkTired", "WalkDeck", "Run", "RunScared", "SickWalk", "Gangway" };
        static readonly string[] ClipRated = { "Carry", "HuntWalk" };

        /// **Fallback switch:** true puts the GENERATED sine Idle/Walk
        /// (`AstraPlaytestImport.MakeClip`) back in the Idle and Walk states.
        public static bool UseGeneratedIdleWalk = false;

        /// State names, one per take in the FBX (`Crew_<name>`), read off
        /// the importer. The FBX is the list: a take added there is a state
        /// here on the next run.
        static string[] Takes
        {
            get
            {
                var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
                if (mi == null) return new string[0];
                return mi.importedTakeInfos.Where(t => t.name.StartsWith(TakePrefix))
                    .Select(t => t.name.Substring(TakePrefix.Length))
                    .Concat(FromLookoutFbx).Distinct().ToArray();
            }
        }

        [MenuItem("SeaSick/Art/Import v15 crew clips (all takes)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceSynchronousImport);
            ConfigureFbx(Fbx, Takes.Where(t => TakeFrom(t) == Fbx).ToArray(), log);
            ConfigureFbx(LookoutFbx, FromLookoutFbx, log);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            if (controller == null) throw new Exception("missing " + Controller + " (run AstraPlaytestImport first)");
            AddStates(controller, log);
            AssetDatabase.SaveAssets();
            Verify(log);
            Debug.Log("[CrewClipsV15] " + log);
            return log.ToString();
        }

        static void ConfigureFbx(string Fbx, string[] Takes, StringBuilder log)
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
                    loopTime = !OneShots.Contains(name),
                    wrapMode = OneShots.Contains(name) ? WrapMode.ClampForever : WrapMode.Loop,
                };
            }).ToArray();
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
            foreach (var c in clips) log.AppendLine($"clip {c.name}: frames {c.firstFrame}-{c.lastFrame}{(c.loopTime ? "" : " once")}");
        }

        public static AnimationClip Clip(string name)
            => AssetDatabase.LoadAllAssetsAtPath(TakeFrom(name)).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == "Crew_" + name && !c.name.StartsWith("__preview__"));

        /// Add (or refresh) a state per take, the `ClipRate` and `WalkRate`
        /// parameters, and put the v15 Idle/Walk in the locomotion states.
        /// Safe to call on a controller `AstraPlaytestImport` just rebuilt.
        public static void AddStates(AnimatorController controller, StringBuilder log)
        {
            foreach (var param in new[] { "ClipRate", "WalkRate" })
                if (!controller.parameters.Any(p => p.name == param))
                    controller.AddParameter(new AnimatorControllerParameter
                        { name = param, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var sm = controller.layers[0].stateMachine;
            var takes = Takes;
            for (int i = 0; i < takes.Length; i++)
            {
                string name = takes[i];
                var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
                bool locomotion = name == "Idle" || name == "Walk";
                // The generated sine pair stays the fallback: the switch keeps
                // whatever AstraPlaytestImport put there.
                if (locomotion && UseGeneratedIdleWalk && state != null && state.motion != null) continue;
                var clip = Clip(name);
                if (clip == null) throw new Exception("no clip Crew_" + name + " in " + Fbx + " (run CrewClipsV15Import)");
                if (state == null) state = sm.AddState(name, new Vector3(520f + 260f * (i / 12), 40f + 60f * (i % 12), 0f));
                state.motion = clip;
                state.writeDefaultValues = true;
                string rate = WalkRated.Contains(name) ? "WalkRate" : ClipRated.Contains(name) ? "ClipRate" : "";
                state.speedParameterActive = rate != "";
                state.speedParameter = rate;
                log?.AppendLine($"state {name} <- {clip.name} ({clip.length:F2} s, loop {clip.isLooping}{(rate != "" ? ", rate " + rate : "")})");
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
                    if (moved < 0.002f)
                    {
                        // Some takes hold the fist still (Lookout, Soaked,
                        // SickRail...): any bone moving at all proves binding.
                        float any = 0f;
                        var bones = vis.GetComponentsInChildren<Transform>(true);
                        var before = new Quaternion[bones.Length];
                        clip.SampleAnimation(vis, 0f);
                        for (int b = 0; b < bones.Length; b++) before[b] = bones[b].localRotation;
                        for (float t = 0.1f; t < 1f; t += 0.1f)
                        {
                            clip.SampleAnimation(vis, clip.length * t);
                            for (int b = 0; b < bones.Length; b++) any = Mathf.Max(any, Quaternion.Angle(before[b], bones[b].localRotation));
                        }
                        if (any < 1f) throw new Exception(name + " clip did not reach the crew bones");
                    }
                }
            }
            finally { Object.DestroyImmediate(crew); }
        }
    }
}
