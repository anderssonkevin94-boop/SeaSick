using UnityEngine;

namespace SeaSick.Combat
{
    /// **Drives the kraken's seven arms by hand, every frame, no Animator.**
    ///
    /// The base pose is a blend from the rig's rest (arms spread low, tips
    /// already hooked) to Kevin's approved P1 (arms towering around the head,
    /// the front pair low so the face stays clear) by `Raise01`. On top of
    /// that runs the life: a wave travelling base to tip along each arm,
    /// curling about the bone's local X (the rig's contract: positive X curls
    /// toward the suckers) with a smaller side sway about local Z, every arm
    /// on its own phase so they never move as a row; and a slow breath on the
    /// mantle.
    ///
    /// **The curl budget.** The rig notes measured the arms clean to ~50 deg
    /// a bone; past that the suckers on the inside of a coil jam. The wave is
    /// clamped per bone to whatever the pose underneath has left of that 50,
    /// so a bone P1 already bends hard barely moves and a straight one sways
    /// freely -- no slider setting can push an arm into a jam.
    ///
    /// **Hooks for the swat (step 2).** Any arm can be pulled off the shared
    /// pose toward another baked pose (`SetArmPose`: P2 windup, P3 slam, or
    /// back to rest) by a weight the caller animates, and its life can be
    /// quietened (`SetArmLife`) so a coiled windup holds still. Nothing here
    /// decides WHEN; `Kraken` (and later the swat) does.
    ///
    /// Runs in LateUpdate so it has the last word on the bones each frame.
    public class KrakenArms : MonoBehaviour
    {
        public const int ArmCount = 7;
        public const int BonesPerArm = 10;
        const int ArmBones = ArmCount * BonesPerArm;

        /// Index into the baked poses. Rest is the rig's own bind.
        public enum Pose { Rest = -1, Surfaced = 0, Windup = 1, Slam = 2 }

        [SerializeField] KrakenPoses poses;
        [SerializeField] float curlBudgetDeg = 50f;

        Transform rootBone, body;
        readonly Transform[] arm = new Transform[ArmBones];
        readonly Quaternion[] armRest = new Quaternion[ArmBones];
        Quaternion bodyRest;
        Vector3 rootRest;

        // Baked poses resolved to this rig's bones: [pose][armBone].
        Quaternion[][] poseArm;
        Quaternion[] poseBody;
        Vector3[] poseRoot;

        readonly float[] armPhase = new float[ArmCount];
        readonly Pose[] overridePose = new Pose[ArmCount];
        readonly float[] overrideWeight = new float[ArmCount];
        readonly float[] armLife = new float[ArmCount];
        // The swat's aim (step 2): a world point the arm's last joint reaches
        // for by CCD on top of everything above, by `aimWeight`, plus a hook
        // curled into the last bones.
        readonly Vector3[] aimTarget = new Vector3[ArmCount];
        readonly float[] aimWeight = new float[ArmCount];
        readonly float[] aimHook = new float[ArmCount];
        readonly Quaternion[] poseScratch = new Quaternion[BonesPerArm];

        float raise01;
        bool ready;

        /// 0 = rest (tucked low, for under water), 1 = the surfaced P1.
        public float Raise01
        {
            get => raise01;
            set => raise01 = Mathf.Clamp01(value);
        }

        public bool Ready => ready;

        /// One bone of one arm, 0 at the mantle to 9 at the tip. Read-only use:
        /// this component rewrites every bone's rotation each LateUpdate.
        public Transform ArmBone(int armIndex, int bone) =>
            ready ? arm[armIndex * BonesPerArm + bone] : null;

        public Transform Body => body;

        /// Pull one arm off the shared pose toward `pose` by `weight` (0..1).
        /// The caller animates the weight; 0 hands the arm back.
        public void SetArmPose(int armIndex, Pose pose, float weight)
        {
            if (armIndex < 0 || armIndex >= ArmCount) return;
            overridePose[armIndex] = pose;
            overrideWeight[armIndex] = Mathf.Clamp01(weight);
        }

        public void ClearArmPose(int armIndex) => SetArmPose(armIndex, Pose.Surfaced, 0f);

        /// Reach this arm's last joint for `worldTarget` by `weight` (0..1, 0
        /// hands it back), with `hookDeg` of extra curl spread over its last
        /// four bones (positive = toward the suckers: a coil over a target).
        /// Solved every LateUpdate by CCD inside the rig's curl budget, so an
        /// out-of-reach target leaves the arm stretched toward it, never torn.
        public void SetArmAim(int armIndex, Vector3 worldTarget, float weight, float hookDeg = 0f)
        {
            if (armIndex < 0 || armIndex >= ArmCount) return;
            aimTarget[armIndex] = worldTarget;
            aimWeight[armIndex] = Mathf.Clamp01(weight);
            aimHook[armIndex] = hookDeg;
        }

        public void ClearArmAim(int armIndex) => SetArmAim(armIndex, Vector3.zero, 0f);

        /// The tip of an arm, world: its last joint plus most of a bone length
        /// on along the line from the joint before. Read after LateUpdate for
        /// this frame's pose.
        public Vector3 ArmTipPosition(int armIndex)
        {
            if (!ready || armIndex < 0 || armIndex >= ArmCount) return transform.position;
            Vector3 last = arm[armIndex * BonesPerArm + BonesPerArm - 1].position;
            Vector3 prev = arm[armIndex * BonesPerArm + BonesPerArm - 2].position;
            return last + (last - prev) * 0.8f;
        }

        /// How much of the travelling wave this arm wears, 0..1 (default 1).
        public void SetArmLife(int armIndex, float weight)
        {
            if (armIndex < 0 || armIndex >= ArmCount) return;
            armLife[armIndex] = Mathf.Clamp01(weight);
        }

        void Awake()
        {
            for (int a = 0; a < ArmCount; a++)
            {
                // Spread round the clock plus a jitter, so neighbours are never
                // in step and no two spawns sway the same way.
                armPhase[a] = a * 2.39996f + Random.Range(-0.6f, 0.6f);
                armLife[a] = 1f;
                overridePose[a] = Pose.Surfaced;
                aimWeight[a] = 0f;
            }
            Bind();
        }

        void Bind()
        {
            // Names, not a path: the FBX wraps the bones in `OctopusRig`, and a
            // re-export may move that wrapper without renaming a single bone.
            var all = GetComponentsInChildren<Transform>(true);
            Transform Find(string n)
            {
                foreach (var t in all) if (t.name == n) return t;
                return null;
            }

            rootBone = Find("Root");
            body = Find("Body");
            if (rootBone == null || body == null)
            {
                Debug.LogError("[KrakenArms] Root/Body bones not found under " + name);
                return;
            }
            for (int a = 0; a < ArmCount; a++)
            for (int j = 0; j < BonesPerArm; j++)
            {
                var t = Find("Arm" + a + "_" + j.ToString("00"));
                if (t == null)
                {
                    Debug.LogError("[KrakenArms] missing bone Arm" + a + "_" + j.ToString("00"));
                    return;
                }
                arm[a * BonesPerArm + j] = t;
                armRest[a * BonesPerArm + j] = t.localRotation;
            }
            bodyRest = body.localRotation;
            rootRest = rootBone.localPosition;

            if (poses == null || poses.PoseCount == 0)
            {
                Debug.LogError("[KrakenArms] no baked poses -- run SeaSick/Art/Bake kraken poses");
                return;
            }

            int n = poses.PoseCount;
            poseArm = new Quaternion[n][];
            poseBody = new Quaternion[n];
            poseRoot = new Vector3[n];
            for (int p = 0; p < n; p++)
            {
                var src = poses.Get(p);
                poseArm[p] = new Quaternion[ArmBones];
                poseRoot[p] = src.rootPosition;
                poseBody[p] = bodyRest;
                for (int i = 0; i < ArmBones; i++) poseArm[p][i] = armRest[i];
                for (int b = 0; b < poses.BoneCount; b++)
                {
                    string bn = poses.BoneName(b);
                    if (bn == "Body") { poseBody[p] = src.rotations[b]; continue; }
                    int idx = ArmIndexOf(bn);
                    if (idx >= 0) poseArm[p][idx] = src.rotations[b];
                }
            }
            ready = true;
        }

        /// "Arm3_07" -> 37, anything else -> -1. Bind-time only.
        static int ArmIndexOf(string bone)
        {
            if (bone.Length != 7 || !bone.StartsWith("Arm") || bone[4] != '_') return -1;
            int a = bone[3] - '0', j = (bone[5] - '0') * 10 + (bone[6] - '0');
            if (a < 0 || a >= ArmCount || j < 0 || j >= BonesPerArm) return -1;
            return a * BonesPerArm + j;
        }

        Quaternion PoseRot(Pose pose, int i) =>
            pose == Pose.Rest || (int)pose >= poseArm.Length ? armRest[i] : poseArm[(int)pose][i];

        void LateUpdate()
        {
            if (!ready) return;

            float t = Time.time;
            int surfaced = (int)Pose.Surfaced;

            // The mantle and the rig root follow the shared raise only; the
            // breath rides on the mantle so the whole crown of arms heaves
            // with it, a degree or two.
            rootBone.localPosition = Vector3.Lerp(rootRest, poseRoot[surfaced], raise01);
            float breath = Mathf.Sin(t * 0.75f) * KrakenTuning.breathDeg;
            body.localRotation = Quaternion.Slerp(bodyRest, poseBody[surfaced], raise01)
                                 * Quaternion.Euler(breath, 0f, breath * 0.4f);

            float amp = KrakenTuning.swayAmplitudeDeg;
            float side = KrakenTuning.sideSwayDeg;
            float omega = KrakenTuning.swaySpeed * Mathf.PI * 2f;
            float k = Mathf.PI * 2f / Mathf.Max(1f, KrakenTuning.swayWavelength);

            for (int a = 0; a < ArmCount; a++)
            {
                float w = overrideWeight[a];
                Pose op = overridePose[a];
                float life = armLife[a];
                for (int j = 0; j < BonesPerArm; j++)
                {
                    int i = a * BonesPerArm + j;
                    Quaternion rest = armRest[i];
                    Quaternion pose = Quaternion.Slerp(rest, poseArm[surfaced][i], raise01);
                    if (w > 0f) pose = Quaternion.Slerp(pose, PoseRot(op, i), w);

                    // More life toward the tip: the roots are thick and carry
                    // the head, the tips are what read as alive.
                    float env = (0.35f + 0.65f * j / (BonesPerArm - 1f)) * life;
                    float phase = omega * t - k * j + armPhase[a];
                    float curl = Mathf.Sin(phase) * amp * env;
                    float headroom = Mathf.Max(0f, curlBudgetDeg - Quaternion.Angle(rest, pose));
                    curl = Mathf.Clamp(curl, -headroom, headroom);
                    float sway = Mathf.Sin(phase * 0.71f + 1.3f) * side * env;

                    arm[i].localRotation = pose * Quaternion.Euler(curl, 0f, sway);
                }
                if (aimWeight[a] > 0.001f) SolveAim(a);
            }
        }

        /// CCD from the second-to-last joint back to the root, reaching the
        /// last joint for the aim target, then blended over the posed arm by
        /// the aim weight. Each bone stays inside the curl budget measured
        /// from the rig's rest (the root inside 70 % of it: its junction with
        /// the mantle is where the rig notes measured the worst stretch).
        void SolveAim(int a)
        {
            int b0 = a * BonesPerArm;
            for (int j = 0; j < BonesPerArm; j++) poseScratch[j] = arm[b0 + j].localRotation;
            Vector3 target = aimTarget[a];
            Transform end = arm[b0 + BonesPerArm - 1];
            for (int it = 0; it < 8; it++)
            {
                for (int j = BonesPerArm - 2; j >= 0; j--)
                {
                    Transform bone = arm[b0 + j];
                    Vector3 bp = bone.position;
                    Vector3 toEnd = end.position - bp, toTarget = target - bp;
                    if (toEnd.sqrMagnitude < 1e-4f || toTarget.sqrMagnitude < 1e-4f) continue;
                    Quaternion turn = Quaternion.FromToRotation(toEnd, toTarget);
                    turn = Quaternion.RotateTowards(Quaternion.identity, turn, 30f);
                    bone.rotation = turn * bone.rotation;
                    float budget = j == 0 ? curlBudgetDeg * 0.7f : curlBudgetDeg;
                    Quaternion rest = armRest[b0 + j];
                    float ang = Quaternion.Angle(rest, bone.localRotation);
                    if (ang > budget)
                        bone.localRotation = Quaternion.Slerp(rest, bone.localRotation, budget / ang);
                }
                if ((end.position - target).sqrMagnitude < 0.25f) break;
            }
            float w = aimWeight[a];
            float hook = aimHook[a];
            for (int j = 0; j < BonesPerArm; j++)
            {
                Quaternion q = Quaternion.Slerp(poseScratch[j], arm[b0 + j].localRotation, w);
                if (j >= BonesPerArm - 4 && hook != 0f)
                    q *= Quaternion.Euler(hook * w * 0.25f, 0f, 0f);
                arm[b0 + j].localRotation = q;
            }
        }

#if UNITY_EDITOR
        public void SetPosesForBake(KrakenPoses baked) => poses = baked;
#endif
    }
}
