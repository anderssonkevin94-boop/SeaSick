using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What a villager's body is doing**, over the top of the walk cycle.
    ///
    /// The crew rig has two clips, Idle and Walk, and that is all it needs to
    /// have: a sawyer sawing and a man dangling from the Hand are arm, hip and
    /// head poses laid over whichever clip is playing, plus a primitive tool.
    ///
    /// Pure show. Touches no ledger, no order, no store.
    ///
    /// **Why a bone layer and not more clips.** The rig is generated
    /// (`tools/blender/seasick_crew.py`) and its two clips are baked sine
    /// tracks over eight bones: `root, hips, chest, head, arm_L, arm_R, leg_L,
    /// leg_R`, generic, no knee and no elbow. Adding six more takes and
    /// re-authoring a controller buys nothing a sine over the same eight bones
    /// does not — and every new clip would have to be re-exported the next
    /// time the proportions move. So this runs in `LateUpdate`, after the
    /// Animator has written its pose, and bends the result.
    ///
    /// **Rotations are applied about the CHARACTER's axes, never the bone's.**
    /// The generator's own `swing()` carries the same warning in Blender: a
    /// bone hanging straight down has whatever roll the exporter chose, so
    /// "rotate about local X" is unpredictable and is how a walk cycle comes
    /// out swinging sideways. `A = body · Euler(d) · body⁻¹` is an honest
    /// world rotation about the body's right/up/forward, and it survives any
    /// re-export.
    ///
    /// In the Unity frame the body faces +Z, so a POSITIVE pitch about +X
    /// swings a hanging arm BACKWARD, -90° puts it straight out in front, and
    /// -180° puts it overhead. Everything below is written in those terms.
    ///
    /// Null-safe by construction: a missing bone is a bone that is not bent,
    /// never an exception. A rig with none of them makes this component a
    /// no-op rather than a crash at a camp.
    public class VillagerActing : MonoBehaviour
    {
        public enum Mode { None, Chop, Saw, Hammer, Hoe, Stir, Carry, Dangle, Land }

        public Mode Current { get; private set; }

        /// The acting component on this body, added if it has none.
        public static VillagerActing On(Crew.CrewAgent hand)
        {
            if (hand == null) return null;
            var a = hand.GetComponent<VillagerActing>();
            return a != null ? a : hand.gameObject.AddComponent<VillagerActing>();
        }

        /// Change what the body is doing. `carrying` names the resource on
        /// the shoulder for `Carry` (a log for timber and boards, else a sack).
        ///
        /// Takes effect immediately as far as `Current` is concerned — the
        /// probes and the camp read it as the answer to "what is he doing" —
        /// but the POSE cross-fades, so a mode change is a movement rather
        /// than a snap.
        public void Set(Mode mode, string carrying = null)
        {
            if (Current == mode && load == carrying) return;
            Current = mode;
            load = carrying;
        }

        // --- tunables (runtime-added component: these consts ARE the dials) --

        /// Seconds to fade a pose in or out. Long enough that a change reads
        /// as a movement, short enough that a chop that becomes a carry does
        /// not look like a stumble.
        public static float FadeSeconds = 0.15f;
        /// How long the stagger after the Hand sets somebody down lasts.
        public static float LandSeconds = 0.6f;

        const float Chop_Period = 1.25f;
        const float Saw_Period = 1.05f;
        const float Hammer_Period = 0.85f;
        const float Hoe_Period = 1.5f;
        const float Stir_Period = 1.6f;

        /// Shoulder to fingertip, metres. The tool props hang from here.
        const float HandDrop = 0.60f;
        const float TAU = Mathf.PI * 2f;

        // --- state ----------------------------------------------------------

        string load;                 // what Set() was last told to carry
        Mode shown = Mode.None;      // what the POSE is doing, which lags Current
        string shownLoad;
        float weight;                // 0..1 blend of `shown`
        float clock;                 // free-running, for the sine cycles
        float landTimer;

        Transform hips, chest, head, armL, armR, legL, legR;
        Vector3 hipsRest;
        float side = 1f;             // +1 if arm_L sits on the body's +X side
        bool bound, tried;

        /// One prop per mode, built on first use and thereafter toggled. Kept
        /// by mode index so there is no dictionary and no per-frame lookup.
        readonly GameObject[] tools = new GameObject[9];
        GameObject carryProp;
        string carryPropFor;

        // Velocity, for the Dangle swing. Sampled off the transform rather
        // than asked of a Rigidbody: a parked hand has none.
        Vector3 lastAt;
        Vector3 velocity;
        bool sampled;

        void LateUpdate()
        {
            Bind();

            float dt = Time.deltaTime;
            clock += dt;
            TrackVelocity(dt);

            if (shown != Current)
            {
                // Out of the old pose before into the new one. One fade, not
                // two overlapping ones — two arms interpolating between three
                // poses is how a sawyer ends up waving.
                weight -= dt / Mathf.Max(0.01f, FadeSeconds);
                if (weight <= 0f)
                {
                    weight = 0f;
                    shown = Current;
                    shownLoad = load;
                    landTimer = 0f;
                    RefreshProps();
                }
            }
            else
            {
                if (shownLoad != load) { shownLoad = load; RefreshProps(); }
                weight = Mathf.MoveTowards(weight, shown == Mode.None ? 0f : 1f,
                    dt / Mathf.Max(0.01f, FadeSeconds));
            }

            // The stagger runs itself out and hands the body back. Nothing
            // else has to remember to clear it, which is what makes it safe
            // for the Hand to fire and forget.
            if (shown == Mode.Land && Current == Mode.Land)
            {
                landTimer += dt;
                if (landTimer >= LandSeconds) Current = Mode.None;
            }

            if (!bound || weight <= 0.0001f) return;
            Pose(weight);
        }

        void OnDisable()
        {
            // Switched off with the camp. The pose stops where it is; the
            // Animator owns the body again from the next frame.
            weight = 0f;
            shown = Mode.None;
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) tools[i].SetActive(false);
            if (carryProp != null) carryProp.SetActive(false);
        }

        void OnDestroy()
        {
            // The props live under BONES, not under this component's object,
            // so they outlive it unless they are taken down by hand.
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) Destroy(tools[i]);
            if (carryProp != null) Destroy(carryProp);
        }

        // --- the rig --------------------------------------------------------

        /// Find the eight bones ONCE. Names are the generator's, matched
        /// case-insensitively with a `contains` fallback, so a re-export that
        /// renames `chest` to `spine_01` still bends a spine.
        void Bind()
        {
            if (tried) return;
            tried = true;

            var all = GetComponentsInChildren<Transform>(true);
            hips = Bone(all, "hips", "hip", "pelvis");
            chest = Bone(all, "chest", "chest", "spine");
            head = Bone(all, "head", "head");
            armL = Bone(all, "arm_L", "arm_l");
            armR = Bone(all, "arm_R", "arm_r");
            legL = Bone(all, "leg_L", "leg_l");
            legR = Bone(all, "leg_R", "leg_r");

            if (hips != null) hipsRest = hips.localPosition;

            // WHICH SIDE arm_L is actually on, read off the rig rather than
            // assumed from the letter. The generator authors forward as +X and
            // yaws it into Blender's -Y on the way out, so the L/R of the name
            // is a mirror of the source, not a promise about the game's +X.
            if (armL != null)
            {
                float x = transform.InverseTransformPoint(armL.position).x;
                if (Mathf.Abs(x) > 0.001f) side = Mathf.Sign(x);
            }

            bound = armL != null || armR != null || chest != null || hips != null;
        }

        static Transform Bone(Transform[] all, string exact, params string[] contains)
        {
            for (int i = 0; i < all.Length; i++)
                if (string.Equals(all[i].name, exact, System.StringComparison.OrdinalIgnoreCase))
                    return all[i];
            for (int c = 0; c < contains.Length; c++)
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name.IndexOf(contains[c],
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return all[i];
            return null;
        }

        void TrackVelocity(float dt)
        {
            if (dt <= 0f) return;
            Vector3 here = transform.position;
            if (sampled) velocity = Vector3.Lerp(velocity, (here - lastAt) / dt,
                1f - Mathf.Exp(-8f * dt));
            lastAt = here;
            sampled = true;
        }

        // --- the poses ------------------------------------------------------

        void Pose(float w)
        {
            // Everything each mode wants to say, in one flat set of numbers.
            // Degrees about the BODY's axes: pitch fore-and-aft, yaw about the
            // spine, roll sideways. `armIn` is positive toward the centreline,
            // so it reads the same for both arms whichever side they are on.
            float armLPitch = 0f, armRPitch = 0f, armLIn = 0f, armRIn = 0f;
            float chestPitch = 0f, chestRoll = 0f, chestYaw = 0f;
            float headPitch = 0f, headYaw = 0f;
            float hipsPitch = 0f, hipsYaw = 0f, hipsDrop = 0f;
            float legLPitch = 0f, legRPitch = 0f;
            bool movesLegs = false;   // modes that own the legs and the hips' height

            switch (shown)
            {
                case Mode.Chop:
                {
                    // Two-handed, overhead, and the DOWN-stroke is the fast
                    // half — a symmetric sine reads as scrubbing, not felling.
                    float k = Strike(clock / Chop_Period, 0.62f);
                    armLPitch = armRPitch = Mathf.Lerp(-22f, -168f, k);
                    armLIn = armRIn = Mathf.Lerp(14f, 6f, k);
                    chestPitch = Mathf.Lerp(24f, -7f, k);
                    hipsPitch = Mathf.Lerp(9f, -3f, k);
                    headPitch = Mathf.Lerp(15f, 1f, k);
                    break;
                }

                case Mode.Saw:
                {
                    float s = Mathf.Sin(TAU * clock / Saw_Period);
                    armLPitch = armRPitch = -76f + 30f * s;
                    armLIn = armRIn = 10f;
                    chestPitch = 15f + 7f * s;
                    hipsYaw = 8f * s;
                    hipsPitch = 7f;
                    headPitch = 17f;
                    break;
                }

                case Mode.Hammer:
                {
                    // One arm. The other holds the work, which is what makes
                    // it read as a smith rather than a man waving.
                    float k = Strike(clock / Hammer_Period, 0.58f);
                    armLPitch = Mathf.Lerp(-18f, -152f, k);
                    armLIn = 8f;
                    armRPitch = -54f;
                    armRIn = 16f;
                    chestPitch = Mathf.Lerp(16f, 4f, k);
                    headPitch = 16f;
                    hipsPitch = 6f;
                    break;
                }

                case Mode.Hoe:
                {
                    // Reach out, bend, pull back. The bend is in the hips as
                    // well as the chest: a knee-less rig has nothing else to
                    // fold at.
                    float k = 0.5f - 0.5f * Mathf.Cos(TAU * clock / Hoe_Period);
                    armLPitch = armRPitch = Mathf.Lerp(-74f, -18f, k);
                    armLIn = armRIn = 9f;
                    chestPitch = Mathf.Lerp(36f, 17f, k);
                    hipsPitch = Mathf.Lerp(17f, 6f, k);
                    hipsDrop = -0.05f * (1f - k);
                    headPitch = 9f;
                    movesLegs = true;
                    break;
                }

                case Mode.Stir:
                {
                    float p = TAU * clock / Stir_Period;
                    armLPitch = -88f + 15f * Mathf.Cos(p);
                    armLIn = 20f + 12f * Mathf.Sin(p);
                    armRPitch = -26f;
                    armRIn = 6f;
                    chestPitch = 21f;
                    chestYaw = 4f * Mathf.Sin(p);
                    headPitch = 19f;
                    break;
                }

                case Mode.Carry:
                {
                    // One arm up steadying a shoulder load, the body leaning
                    // off it. The LEGS are deliberately untouched: this is the
                    // one acting mode that plays while they are walking, and
                    // the walk cycle is the thing that sells the weight.
                    armLPitch = -156f;
                    armLIn = 18f;
                    armRPitch = 8f;
                    chestRoll = -7f * side;
                    chestPitch = 5f;
                    headPitch = 4f;
                    break;
                }

                case Mode.Dangle:
                {
                    // Held by the scruff: arms up, legs swinging out of step
                    // with each other, the whole body trailing whichever way
                    // the Hand is dragging them.
                    float t = clock;
                    armLPitch = -166f + 9f * Mathf.Sin(t * 2.2f);
                    armRPitch = -170f + 9f * Mathf.Sin(t * 2.2f + 1.3f);
                    armLIn = armRIn = -6f;
                    legLPitch = 24f * Mathf.Sin(t * 2.6f);
                    legRPitch = 24f * Mathf.Sin(t * 2.6f + 2.1f);
                    hipsPitch = -9f;
                    chestPitch = -13f + 5f * Mathf.Sin(t * 1.4f);
                    headPitch = 17f + 6f * Mathf.Sin(t * 1.9f);
                    movesLegs = true;

                    Vector3 v = transform.InverseTransformDirection(velocity);
                    chestPitch += Mathf.Clamp(-v.z * 4f, -26f, 26f);
                    chestRoll += Mathf.Clamp(v.x * 4f, -26f, 26f);
                    hipsYaw += Mathf.Clamp(v.x * 2f, -14f, 14f);
                    break;
                }

                case Mode.Land:
                {
                    // Down hard and up slowly, once. `Pow` front-loads it so
                    // the crouch has already happened by the time the player
                    // has let go.
                    float q = Mathf.Clamp01(landTimer / Mathf.Max(0.05f, LandSeconds));
                    float c = Mathf.Sin(Mathf.PI * Mathf.Pow(q, 0.55f));
                    hipsDrop = -0.26f * c;
                    hipsPitch = 14f * c;
                    legLPitch = -28f * c;
                    legRPitch = 26f * c;
                    armLPitch = armRPitch = -64f * c;
                    armLIn = armRIn = -22f * c;
                    chestPitch = 27f * c;
                    headPitch = -12f * c;
                    movesLegs = true;
                    break;
                }
            }

            // `armIn` is toward the centreline, which is -X for the arm on the
            // +X side. A positive roll about +Z swings a hanging arm toward
            // +X, so the sign of "in" flips with the side the arm is on.
            Turn(armL, armLPitch, 0f, -armLIn * side, w);
            Turn(armR, armRPitch, 0f, armRIn * side, w);
            Turn(chest, chestPitch, chestYaw, chestRoll, w);
            Turn(head, headPitch, headYaw, 0f, w);
            Turn(hips, hipsPitch, hipsYaw, 0f, w);
            if (movesLegs)
            {
                Turn(legL, legLPitch, 0f, 0f, w);
                Turn(legR, legRPitch, 0f, 0f, w);
            }

            if (hips != null && hipsDrop != 0f)
            {
                // **Toward the REST position, not away from the current one.**
                // Both clips key every bone's location every frame, so adding
                // to what the Animator wrote would be safe today and would
                // silently integrate to infinity the day a clip stops doing
                // that. Lerping from the live pose keeps the blend smooth
                // without ever accumulating.
                Vector3 want = hipsRest + new Vector3(0f, hipsDrop, 0f);
                hips.localPosition = Vector3.Lerp(hips.localPosition, want, w);
            }
        }

        /// A strike cycle: 0 at the bottom of the stroke, 1 at the top, with
        /// `lift` of the period spent going up and the rest coming down.
        static float Strike(float cycles, float lift)
        {
            float u = Mathf.Repeat(cycles, 1f);
            return u < lift
                ? Mathf.SmoothStep(0f, 1f, u / lift)
                : 1f - Mathf.SmoothStep(0f, 1f, (u - lift) / (1f - lift));
        }

        /// Bend a bone about the BODY's axes, blended over whatever the
        /// Animator just wrote. See the class comment for why this is not a
        /// local-space Euler.
        void Turn(Transform b, float pitch, float yaw, float roll, float w)
        {
            if (b == null) return;
            if (pitch == 0f && yaw == 0f && roll == 0f) return;
            Quaternion body = transform.rotation;
            Quaternion a = body * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(body);
            b.rotation = Quaternion.Slerp(b.rotation, a * b.rotation, w);
        }

        // --- props ----------------------------------------------------------

        static readonly Dictionary<string, Material> propMats
            = new Dictionary<string, Material>();

        /// Shared across every villager in the world. One material per colour,
        /// never one per body — `CampPiles` learnt the same lesson about the
        /// stacks beside the fire.
        static Material Mat(string key, Color c)
        {
            if (propMats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find(
                WorldArtStyle.Instance != null
                    ? "SeaSick/Environment Toon"
                    : "Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            propMats[key] = m;
            return m;
        }

        void RefreshProps()
        {
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) tools[i].SetActive(false);
            if (carryProp != null) carryProp.SetActive(false);

            if (shown == Mode.Carry)
            {
                EnsureCarry();
                if (carryProp != null) carryProp.SetActive(true);
                return;
            }

            int m = (int)shown;
            if (m <= 0 || m >= tools.Length) return;
            if (!HasTool(shown)) return;
            if (tools[m] == null) tools[m] = BuildTool(shown);
            if (tools[m] != null) tools[m].SetActive(true);
        }

        static bool HasTool(Mode m) =>
            m == Mode.Chop || m == Mode.Saw || m == Mode.Hammer
            || m == Mode.Hoe || m == Mode.Stir;

        void EnsureCarry()
        {
            if (carryProp != null && carryPropFor == shownLoad) return;
            if (carryProp != null) Destroy(carryProp);
            carryPropFor = shownLoad;

            // **The look the CampWorker's old `carried` object had**, moved in
            // here so there is one owner of anything hanging off a villager.
            // A log for the things that come in lengths, a sack for the rest.
            string what = string.IsNullOrEmpty(shownLoad) ? Res.Timber : shownLoad;
            bool logs = what == Res.Timber || what == Res.Boards;
            var root = new GameObject("Carry_" + what);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0.20f * side, 1.45f, 0.06f);
            root.transform.localRotation = Quaternion.Euler(0f, 8f * side, 0f);

            if (logs)
            {
                var log = Prim(PrimitiveType.Cylinder, root.transform,
                    new Vector3(0.20f, 0.62f, 0.20f), Mat("log", Res.Colour(Res.Timber)));
                log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                var sack = Prim(PrimitiveType.Cube, root.transform,
                    new Vector3(0.34f, 0.30f, 0.30f), Mat("sack_" + what, Res.Colour(what)));
                sack.transform.localPosition = new Vector3(0f, 0.10f, 0f);
                sack.transform.localRotation = Quaternion.Euler(0f, 18f, 9f);
            }
            carryProp = root;
        }

        /// A tool in the hand. Primitive on purpose: at the zoom a camp is
        /// read from, a haft and a head is a recognisable axe and anything
        /// more is polygons nobody will ever see.
        GameObject BuildTool(Mode m)
        {
            if (armL == null) return null;

            var root = new GameObject("Tool_" + m);
            root.transform.SetParent(armL, false);
            // The hand is a fingertip's worth down the arm, and the prop is
            // squared to the BODY at bind time so it swings with the arm
            // afterwards without inheriting the bone's unknowable roll.
            root.transform.localPosition =
                armL.InverseTransformPoint(armL.position - transform.up * HandDrop);
            root.transform.localRotation = Quaternion.Inverse(armL.rotation) * transform.rotation;

            var wood = Mat("tool_haft", new Color(0.44f, 0.31f, 0.19f));
            var iron = Mat("tool_iron", new Color(0.42f, 0.44f, 0.48f));

            switch (m)
            {
                case Mode.Chop:
                {
                    var haft = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.045f, 0.62f, 0.045f), wood);
                    haft.transform.localPosition = new Vector3(0f, 0.26f, 0f);
                    var headB = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.075f, 0.17f, 0.035f), iron);
                    headB.transform.localPosition = new Vector3(0.03f, 0.55f, 0f);
                    headB.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
                    break;
                }
                case Mode.Saw:
                {
                    var blade = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.012f, 0.13f, 0.56f), iron);
                    blade.transform.localPosition = new Vector3(0f, 0.06f, 0.24f);
                    var grip = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.05f, 0.11f, 0.09f), wood);
                    grip.transform.localPosition = new Vector3(0f, 0.04f, -0.04f);
                    break;
                }
                case Mode.Hammer:
                {
                    var haft = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.04f, 0.38f, 0.04f), wood);
                    haft.transform.localPosition = new Vector3(0f, 0.16f, 0f);
                    var headB = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.085f, 0.085f, 0.17f), iron);
                    headB.transform.localPosition = new Vector3(0f, 0.34f, 0f);
                    break;
                }
                case Mode.Hoe:
                {
                    var haft = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.04f, 0.95f, 0.04f), wood);
                    haft.transform.localPosition = new Vector3(0f, 0.26f, 0f);
                    var blade = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.17f, 0.03f, 0.12f), iron);
                    blade.transform.localPosition = new Vector3(0f, -0.20f, 0.05f);
                    blade.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
                    break;
                }
                case Mode.Stir:
                {
                    var haft = Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(0.028f, 0.46f, 0.028f), wood);
                    haft.transform.localPosition = new Vector3(0f, 0.14f, 0f);
                    var bowl = Prim(PrimitiveType.Sphere, root.transform,
                        Vector3.one * 0.12f, iron);
                    bowl.transform.localPosition = new Vector3(0f, -0.11f, 0f);
                    break;
                }
            }
            return root;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }
    }
}
