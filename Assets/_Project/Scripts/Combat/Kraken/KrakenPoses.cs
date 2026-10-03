using UnityEngine;

namespace SeaSick.Combat
{
    /// **The kraken's authored poses as plain data.** Baked once in the
    /// editor (`KrakenPoseBake`, menu SeaSick/Art/Bake kraken poses) out of the
    /// takes in `Art/Octopus/Octopus_Poses.fbx`, so the phone build never needs
    /// an Animator, an AnimationClip or an editor-only sampling API to hold
    /// Kevin's approved P1: `KrakenArms` reads the rotations straight off this
    /// asset. Re-run the bake after re-exporting the poses FBX.
    ///
    /// Every pose stores one local rotation per bone in `bones` order, plus
    /// the Root bone's local position (the poses lift the whole rig 0.05 m in
    /// rig units, ~2.5 m at scale 49, against the rest FBX).
    public class KrakenPoses : ScriptableObject
    {
        [System.Serializable]
        public class Pose
        {
            public string name;
            public Quaternion[] rotations;
            public Vector3 rootPosition;
        }

        [SerializeField] string[] bones;
        [SerializeField] Pose[] poses;

        public int BoneCount => bones != null ? bones.Length : 0;
        public int PoseCount => poses != null ? poses.Length : 0;
        public string BoneName(int i) => bones[i];
        public Pose Get(int i) => poses != null && i >= 0 && i < poses.Length ? poses[i] : null;

#if UNITY_EDITOR
        public void SetBaked(string[] boneNames, Pose[] bakedPoses)
        {
            bones = boneNames;
            poses = bakedPoses;
        }
#endif
    }
}
