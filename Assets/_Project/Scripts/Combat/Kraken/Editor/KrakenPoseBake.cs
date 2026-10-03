using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **Bakes the kraken's authored poses into `Settings/KrakenPoses.asset`
    /// and wires the prefab** (menu SeaSick/Art/Bake kraken poses, or
    /// `SeaSick.Combat.KrakenPoseBake.Run()` from eval).
    ///
    /// Samples each take of `Art/Octopus/Octopus_Poses.fbx` onto a scratch
    /// copy of the rest rig (`Octopus_Rigged.fbx`, same bone names and
    /// frames) and keeps every bone's local rotation plus the Root's local
    /// position, so the runtime never touches an AnimationClip. Then makes
    /// sure the prefab sits at `Resources/Creatures/Octopus.prefab` (moved
    /// with `AssetDatabase.MoveAsset`, so its GUID survives) and carries
    /// `KrakenArms` (pointed at the asset) and `Kraken`, with no Animator.
    /// Only components and serialized references change: the mesh, its
    /// materials and the model transform are Kevin's and are left exactly
    /// as they are. Idempotent.
    public static class KrakenPoseBake
    {
        const string PosesFbx = "Assets/_Project/Art/Octopus/Octopus_Poses.fbx";
        const string RigFbx = "Assets/_Project/Art/Octopus/Octopus_Rigged.fbx";
        const string AssetPath = "Assets/_Project/Settings/KrakenPoses.asset";
        const string OldPrefab = "Assets/_Project/Prefabs/Creatures/Octopus.prefab";
        const string NewPrefab = "Assets/_Project/Resources/Creatures/Octopus.prefab";

        [MenuItem("SeaSick/Art/Bake kraken poses")]
        static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder();

            // --- the takes, in P1, P2, P3 order (KrakenArms.Pose indexes them)
            var clips = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(PosesFbx))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) clips.Add(c);
            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (clips.Count == 0) return "KrakenPoseBake: no takes in " + PosesFbx;

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigFbx);
            if (rig == null) return "KrakenPoseBake: no rig at " + RigFbx;

            var scratch = Object.Instantiate(rig);
            scratch.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var rigRoot = scratch.transform.Find("OctopusRig");
                if (rigRoot == null) return "KrakenPoseBake: no OctopusRig in " + RigFbx;
                var bones = new List<Transform>();
                foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
                    if (t != rigRoot) bones.Add(t);
                Transform rootBone = bones.Find(b => b.name == "Root");

                var restPos = new Vector3[bones.Count];
                var restRot = new Quaternion[bones.Count];
                var restScale = new Vector3[bones.Count];
                for (int i = 0; i < bones.Count; i++)
                {
                    restPos[i] = bones[i].localPosition;
                    restRot[i] = bones[i].localRotation;
                    restScale[i] = bones[i].localScale;
                }
                Quaternion rigRot = rigRoot.localRotation;
                Vector3 rigPos = rigRoot.localPosition;

                var names = new string[bones.Count];
                for (int i = 0; i < bones.Count; i++) names[i] = bones[i].name;

                var baked = new KrakenPoses.Pose[clips.Count];
                for (int p = 0; p < clips.Count; p++)
                {
                    for (int i = 0; i < bones.Count; i++)
                    {
                        bones[i].localPosition = restPos[i];
                        bones[i].localRotation = restRot[i];
                        bones[i].localScale = restScale[i];
                    }
                    rigRoot.localRotation = rigRot;
                    rigRoot.localPosition = rigPos;

                    clips[p].SampleAnimation(scratch, 0f);

                    var rots = new Quaternion[bones.Count];
                    float maxDelta = 0f;
                    string maxBone = "";
                    for (int i = 0; i < bones.Count; i++)
                    {
                        rots[i] = bones[i].localRotation;
                        float d = Quaternion.Angle(restRot[i], rots[i]);
                        if (d > maxDelta) { maxDelta = d; maxBone = names[i]; }
                    }
                    string shortName = clips[p].name.Substring(clips[p].name.LastIndexOf('|') + 1);
                    baked[p] = new KrakenPoses.Pose
                    {
                        name = shortName,
                        rotations = rots,
                        rootPosition = rootBone != null ? rootBone.localPosition : Vector3.zero,
                    };
                    log.AppendLine($"  {shortName}: max bone delta {maxDelta:F1} deg ({maxBone}), " +
                                   $"Root {baked[p].rootPosition.ToString("F3")}, " +
                                   $"rig wrapper moved {Quaternion.Angle(rigRot, rigRoot.localRotation):F2} deg");
                }

                var asset = AssetDatabase.LoadAssetAtPath<KrakenPoses>(AssetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<KrakenPoses>();
                    AssetDatabase.CreateAsset(asset, AssetPath);
                }
                asset.SetBaked(names, baked);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                log.Insert(0, $"KrakenPoseBake: {clips.Count} poses x {names.Length} bones -> {AssetPath}\n");

                WirePrefab(asset, log);
            }
            finally
            {
                Object.DestroyImmediate(scratch);
            }
            return log.ToString();
        }

        static void WirePrefab(KrakenPoses asset, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(NewPrefab) == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources/Creatures"))
                    AssetDatabase.CreateFolder("Assets/_Project/Resources", "Creatures");
                string err = AssetDatabase.MoveAsset(OldPrefab, NewPrefab);
                if (!string.IsNullOrEmpty(err)) { log.AppendLine("  prefab move FAILED: " + err); return; }
                log.AppendLine("  prefab moved to " + NewPrefab + " (GUID kept)");
                // The old folder held only this prefab.
                if (AssetDatabase.IsValidFolder("Assets/_Project/Prefabs/Creatures")
                    && AssetDatabase.FindAssets("", new[] { "Assets/_Project/Prefabs/Creatures" }).Length == 0)
                    AssetDatabase.DeleteAsset("Assets/_Project/Prefabs/Creatures");
            }

            var root = PrefabUtility.LoadPrefabContents(NewPrefab);
            try
            {
                foreach (var an in root.GetComponentsInChildren<Animator>(true))
                {
                    Object.DestroyImmediate(an);
                    log.AppendLine("  removed an Animator");
                }
                var arms = root.GetComponent<KrakenArms>();
                if (arms == null) arms = root.AddComponent<KrakenArms>();
                arms.SetPosesForBake(asset);
                if (root.GetComponent<Kraken>() == null) root.AddComponent<Kraken>();
                PrefabUtility.SaveAsPrefabAsset(root, NewPrefab);
                log.AppendLine("  prefab wired: KrakenArms -> KrakenPoses, Kraken");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
