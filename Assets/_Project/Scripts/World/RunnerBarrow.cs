using UnityEngine;

namespace SeaSick.World
{
    /// **The store runner's wheelbarrow** (2026-10-02, `art-staging/wheelbarrow-v2`
    /// variant A, `Resources/Kits/Carry/Wheelbarrow`, imported by `Dev.WheelbarrowImport`).
    ///
    /// Sits on a villager body. While its hand is a RUNNER (`IsRunner`: work
    /// order at the store hut) and the body is out and about, the body pushes
    /// a timber barrow: parented to the BODY ROOT (never a bone: Astra's rig
    /// carries x100 / ~92x bone scales, see the rig-scale trap), the grips
    /// where the `Carry` clip holds his fists, tipped up on its wheel while he
    /// walks and set down on its legs in front of him when he stands. The
    /// wheel turns by the distance he really covered. What he hauls
    /// (`VillagerActing.Load`, the same load `CampWorker` hands the arms)
    /// lies in the tray instead of in his arms, wholly inside it (`Fit`):
    /// logs and planks lengthwise as 0.5 m billets, stones and bricks in
    /// beds on the floor, small goods in the open carry crate; `VillagerActing.Barrow` hides the arm prop meanwhile and
    /// `BarrowArms` keeps his arms forward on the grips while he walks empty.
    ///
    /// Pure show, visual only: no collider, no ledger, no pathing. Driven
    /// by `Sync` from `CampWorker.Update` every frame; a body that is not
    /// synced this frame (flying, downed, aboard, no row) hides it.
    ///
    /// **Always his barrow (2026-10-03, Kevin: "make sure that the runners
    /// are a lot faster than the normal villager and that they always use
    /// their wheelbarrow").** He jogs behind it (`VillagerActing.PushGait`,
    /// Run legs, arms held on the grips) and the grips follow his fists
    /// (`VillagerActing.BarrowFists`), so this runs after the acting layer.
    /// Where a barrow cannot go with him -- up a ladder chain, lying down by
    /// the fire, a meal in his hands -- it is **parked in the world** on its
    /// legs where he left it (`Park`), empty, and he takes it up again when
    /// he is back beside it (`ReattachMetres`), or it catches him up after
    /// `ReattachSeconds`. It is placed by his YAW only, so a body rolled
    /// flat never rolls the barrow with him.
    [DefaultExecutionOrder(100)]
    public class RunnerBarrow : MonoBehaviour
    {
        // --- tunables ---------------------------------------------------------

        /// Degrees the barrow tips up on its axle while pushed ON FLAT GROUND
        /// (the README's 7.04 lifts the grips to 0.79 m; the v15 `Carry` clip
        /// holds his fists at ~0.75 m, so a little less). It sets the grips'
        /// height above his feet; on a slope `Settle` finds the pitch that
        /// keeps them there.
        [SerializeField] private float pushTilt = 5.5f;
        /// Barrow root (the ground point under his hips) against the body
        /// root while pushed: the tilted grips sit 0.36 m ahead of the barrow root, the Carry clip holds his fists 0.42 m ahead (measured, BarrowShot).
        /// x / z only: the height comes from the ground under the wheel (`Settle`).
        [SerializeField] private Vector3 pushOffset = new Vector3(0f, 0f, 0.06f);
        /// ...and while parked on its legs (a step in front of him, let go).
        [SerializeField] private Vector3 parkOffset = new Vector3(0f, 0f, 0.20f);
        /// Seconds to tip up / set down.
        [SerializeField] private float easeSeconds = 0.2f;
        /// Ground speed (m/s, smoothed) above which he is pushing.
        [SerializeField] private float moveSpeed = 0.15f;
        /// Cross-section scale of a pile-unit log / plank in the tray (the
        /// units are 1.6 m long; thickness as before: a 0.15 m log).
        [SerializeField] private float longScale = 0.6f;
        /// Scale of a pile-unit stone (0.49 m) / brick (0.36 m).
        [SerializeField] private float rockScale = 0.32f;
        [SerializeField] private float brickScale = 0.46f;

        /// Parked in the world (2026-10-03): he takes it again within this
        /// many metres (flat) of it...
        public static float ReattachMetres = 2.5f;
        /// ...or it is back with him after this many seconds away from it.
        public static float ReattachSeconds = 6f;
        /// Seconds the grips take to follow his fists (smoothing the stride's bob).
        const float FistFollowSeconds = 0.06f;

        /// Most of one load drawn in the tray (the ledger's count is the truth).
        const int MaxLogs = 6, MaxPlanks = 10, MaxRocks = 12, MaxBricks = 24;

        // **The tray's inside** (wheelbarrow v2 A, `art-staging/wheelbarrow-v2`,
        // Load frame: origin = `Load_Anchor`, the middle of the floor top).
        // Kevin 2026-10-02: the loads clipped through the tray's sides and
        // ends ("have some damn pride in your work"). Every layout below stays
        // inside this box, 1 cm clear of the floor, both side walls, the rear
        // rim rail and the foot of the slanted front; it may heap above the rim
        // only over the floor's own footprint. Proven for every load and count
        // by `art-staging/wheelbarrow-v2/check-fit.py` (mirror: layout_fit.py;
        // keep the numbers equal), and `Fit` holds anything else to it.
        const float InX = 0.26f, InBack = -0.277f, InFront = 0.285f, InFloor = 0.01f, MaxHeap = 0.42f;
        /// Logs and planks are cut to billets this long (metres) to lie in the
        /// 0.59 m tray; the pile unit is `UnitLength`.
        const float BilletLength = 0.50f, UnitLength = 1.60f;
        /// Wheel radius, metres (README: 0.48 m across).
        const float WheelRadius = 0.24f;
        /// ...to its corners: the rim is a 16-gon 0.24 m to the flats, so a
        /// rolling wheel reaches 0.24 / cos(11.25 deg) down. The axle sits
        /// this high: the wheel never dips into the ground, floats <= 5 mm.
        const float WheelReach = 0.2447f;
        /// Half the felloe's width (build.py WHEEL_T 0.11).
        const float TreadHalf = 0.055f;
        /// The leg feet in the barrow's root frame (build.py: 0.075 m square
        /// at x +-0.25, front edge 0.83 m ahead of his hips; outer edge here).
        const float LegX = 0.2875f, LegFront = 0.83f, LegBack = 0.755f, LegMidZ = 0.7925f;
        /// Least daylight under the legs while pushed, metres.
        const float LegClear = 0.03f;
        /// Half the span the ground slope is read over at the wheel, metres.
        const float Probe = 0.2f;
        /// His feet this far off the height field = no field here (flat).
        const float FieldSlack = 0.3f;
        /// Pitch limit about the axle, degrees.
        const float MaxPitch = 80f;

        // --- who pushes one -----------------------------------------------------

        /// **A runner**: a hand on the work order at the store hut
        /// (`OutpostLedger.IsRunner`).
        public static bool IsRunner(OutpostHand h) => OutpostLedger.IsRunner(h);

        /// **Called every frame from `CampWorker.Update`** for a body on the
        /// ground with a row. Adds the component the first time its hand is a
        /// runner; otherwise costs one `GetComponent`. `park` (2026-10-03):
        /// the barrow cannot go where he is going (a ladder climb, lying down
        /// by the fire) -- it is set down where he left it.
        public static void Sync(Component body, OutpostHand r, VillagerActing acting, bool park = false)
        {
            if (body == null) return;
            bool runner = IsRunner(r);
            var b = body.GetComponent<RunnerBarrow>();
            if (b == null)
            {
                if (!runner) return;
                b = body.gameObject.AddComponent<RunnerBarrow>();
            }
            b.acting = acting;
            b.syncFrame = Time.frameCount;
            b.parkWant = park;
            b.want = runner && !r.downed && !r.hiddenInHut && !r.hidingHut && !r.hidingCrouch
                     && !r.fetchingSpear && !r.defending && !r.pouting && string.IsNullOrEmpty(r.rescuing);
        }

        // --- state --------------------------------------------------------------

        VillagerActing acting;
        int syncFrame = -10;
        bool want;

        GameObject root;          // the barrow, child of this body
        Transform holder, tilt, wheel, load;
        string loadKey;
        Vector3 last;
        bool hasLast;
        float speed, push, spin;

        // Parked in the world (2026-10-03, `Park`).
        bool parkWant, parked;
        Vector3 parkPos;
        Quaternion parkRot = Quaternion.identity, parkTilt = Quaternion.identity;
        float awayFor;
        // His body the last two frames. A park is set down from the OLDER:
        // `CampWorker` asks for it (`Sync`) before his walk starts a climb,
        // so the first frame it hears of one he already stands at the foot.
        Vector3 bodyWas, bodyWas2;
        float yawWas, yawWas2;
        bool hasBodyWas, hasBodyWas2;
        // The grips' target: his fists' reach ahead of his root and their
        // height over his feet (live off `BarrowFists`, else the Carry
        // clip's measured numbers).
        float fistReach = FistReach, fistRise = -1f;

        /// True while the barrow is drawn (probes / shots).
        public bool Showing => root != null && root.activeSelf;

        void LateUpdate()
        {
            bool fresh = Time.frameCount - syncFrame <= 1;
            Step(Time.deltaTime, want && fresh);
        }

        void OnDisable()
        {
            if (root != null) root.SetActive(false);
            SetActing(false, false);
            hasLast = false;
            parked = false;
            hasBodyWas = hasBodyWas2 = false;
        }

        void OnDestroy()
        {
            SetActing(false, false);
            // The barrow hangs off the body, not this component: take it
            // down when only the component goes (edit mode: it goes with
            // the body, and DestroyImmediate is not allowed there).
            if (root != null && Application.isPlaying) Destroy(root);
        }

        /// One frame of the barrow (`LateUpdate`; an edit-mode shot drives it
        /// directly with `acting` set by `Bind`).
        public void Step(float dt, bool show)
        {
            if (acting == null) acting = GetComponent<VillagerActing>();
            if (!show || !Build())
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                SetActing(false, false);
                hasLast = false;
                push = 0f;
                parked = false;
                hasBodyWas = hasBodyWas2 = false;
                return;
            }
            if (!root.activeSelf) root.SetActive(true);

            // **Parked where he left it** (2026-10-03): asked for (a climb,
            // lying down), or his hands are on a meal; then held there until
            // he is back beside it or it has been away long enough.
            bool eating = acting != null && (acting.Current == VillagerActing.Mode.Reach || acting.Current == VillagerActing.Mode.Eat);
            if (parkWant || eating || (parked && !Reattach(dt)))
            {
                if (parkWant || eating) awayFor = 0f;   // the catch-up clock runs only once he has left it
                Park();
                RememberBody();
                return;
            }

            // How far he went this frame, along his facing (the wheel) and
            // flat (pushing or not). A jump of metres is a teleport, not a roll.
            Vector3 p = transform.position;
            float fwd = 0f, v = 0f;
            if (hasLast && dt > 0f)
            {
                Vector3 d = p - last;
                d.y = 0f;
                if (d.sqrMagnitude < 4f)
                {
                    v = d.magnitude / dt;
                    fwd = Vector3.Dot(d, transform.forward);
                }
            }
            last = p;
            hasLast = true;
            speed = Mathf.Lerp(speed, v, 1f - Mathf.Exp(-dt / 0.12f));
            bool moving = speed > moveSpeed;

            push = Mathf.MoveTowards(push, moving ? 1f : 0f, dt / Mathf.Max(0.01f, easeSeconds));
            float k = Mathf.SmoothStep(0f, 1f, push);
            FollowFists(dt);
            Settle(k);
            RememberBody();
            spin = Mathf.Repeat(spin + fwd / WheelRadius * Mathf.Rad2Deg, 360f);
            wheel.localRotation = Quaternion.Euler(spin, 0f, 0f);

            string res = null;
            int n = 0;
            if (acting != null && !string.IsNullOrEmpty(acting.Load)
                && (acting.Current == VillagerActing.Mode.Carry || acting.Current == VillagerActing.Mode.SetDown))
            {
                res = acting.Load;
                n = acting.LoadCount;
            }
            ShowLoad(res, n);
            SetActing(true, moving);
        }

        // --- on the ground ------------------------------------------------------

        /// Test hook (`BarrowShot`): the ground height at (x, z) instead of
        /// the island's height field. Null in the game.
        public System.Func<float, float, float> GroundOverride
        {
            get => groundOverride;
            set { groundOverride = value; settledK = -1f; }
        }
        System.Func<float, float, float> groundOverride;

        /// The ground under `q`, the way the villagers stand on it
        /// (`CampWorker`: the island's height field, raised to a worker pad's
        /// top). `flat` = no height field here (edit mode, a recompiled play
        /// session, a body off the field): level with his feet.
        float Ground(Vector3 q, bool flat)
        {
            if (GroundOverride != null) return GroundOverride(q.x, q.z);
            if (flat) return settleFeetY;
            var h = CameraRig.GroundPick.Height;
            return WorkerPad.Foot(q, h != null ? h(q.x, q.z) : settleFeetY);
        }

        Vector3 settledAt;
        float settledYaw, settledK = -1f;

        /// **Wheel on the ground, grips in his fists, legs never under it**
        /// (Kevin 2026-10-02: the wheel sank up to 10 cm while pushed; no
        /// clipping, ever). The holder keeps its x/z offset in front of him
        /// (`parkOffset` / `pushOffset`, blended by `k`; their y is unused)
        /// and its HEIGHT is set every frame from the ground under the wheel,
        /// not from his root: the axle sits just high enough that the wheel's
        /// lowest edge touches the ground plane under it, the barrow rolls
        /// onto both legs when parked (level while pushed: grips in his fists),
        /// and the pitch about the axle follows from the rest:
        /// - pushed: the grips at the height the `Carry` clip holds his fists
        ///   above his feet (`pushTilt` on flat ground), so uphill the handles
        ///   come down to him and downhill they lift; never so low that a
        ///   leg comes within `LegClear` of the ground under it;
        /// - parked: the lower leg foot on the ground (both, on a plane).
        /// Seven height samples, and none while he stands still.
        void Settle(float k) => Settle(k, transform.position, transform.eulerAngles.y);

        /// The same for a body standing at `p` facing `yaw` (a park is set
        /// down from where he stood LAST frame, before a climb moved him).
        void Settle(float k, Vector3 p, float yaw)
        {
            var art = Art.Load();
            if (settledK == k && (p - settledAt).sqrMagnitude < 1e-6f && Mathf.Abs(Mathf.DeltaAngle(yaw, settledYaw)) < 0.05f
                && !fistsLive)
                return;
            settledAt = p; settledYaw = yaw; settledK = k;
            settleYaw = Quaternion.Euler(0f, yaw, 0f);
            settleFeetY = p.y;

            // On a slope the pitch that keeps the grips at fist height also
            // swings them nearer or further (15 deg downhill left them 0.30 m
            // ahead of his hands): slide the barrow along his facing until the
            // grips are back at the reach his fists hold on flat ground.
            SettleAt(art, p, k, 0f);
            if (k <= 0f) return;
            Vector3 gripMid = (art.gripL + art.gripR) * 0.5f, fwd = settleYaw * Vector3.forward;
            float slide = 0f;
            for (int i = 0; i < 2; i++)
            {
                float reach = Vector3.Dot(tilt.TransformPoint(gripMid) - p, fwd);
                slide -= (reach - fistReach) * k;
                SettleAt(art, p, k, slide);
            }
        }

        /// The yaw the barrow is laid out on (his heading, never his roll).
        Quaternion settleYaw = Quaternion.identity;
        /// His feet's height for the settle (a park: where he stood last frame).
        float settleFeetY;
        bool fistsLive;

        /// **The grips go where his fists are** (2026-10-03): jogging, the
        /// fists ride the run's lean and bob (`VillagerActing.BarrowFists`);
        /// their reach and height, smoothed over `FistFollowSeconds`, replace
        /// the Carry clip's measured 0.42 m reach / flat-ground grip height.
        /// Otherwise they ease back to those.
        void FollowFists(float dt)
        {
            var art = Art.Load();
            if (art == null) return;
            float flatRise = FlatGripRise(art);
            if (fistRise < 0f) fistRise = flatRise;
            float wantReach = FistReach, wantRise = flatRise;
            Vector3 mid = default;
            fistsLive = acting != null && acting.BarrowFists(out mid);
            if (fistsLive)
            {
                Vector3 fwd = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Vector3.forward;
                wantReach = Vector3.Dot(mid - transform.position, fwd);
                wantRise = mid.y - transform.position.y;
            }
            float a = dt > 0f ? 1f - Mathf.Exp(-dt / FistFollowSeconds) : 1f;
            fistReach = Mathf.Lerp(fistReach, wantReach, a);
            fistRise = Mathf.Lerp(fistRise, wantRise, a);
        }

        /// The grips' height over his feet at `pushTilt` on flat ground.
        float FlatGripRise(Art art)
        {
            Vector3 grip = (art.gripL + art.gripR) * 0.5f;
            float pt = pushTilt * Mathf.Deg2Rad;
            return art.pivot.y + grip.y * Mathf.Cos(pt) - grip.z * Mathf.Sin(pt);
        }

        void RememberBody()
        {
            bodyWas2 = bodyWas; yawWas2 = yawWas; hasBodyWas2 = hasBodyWas;
            bodyWas = transform.position;
            yawWas = transform.eulerAngles.y;
            hasBodyWas = true;
        }

        /// **Set down and left in the world** (2026-10-03): on its legs where
        /// he stood two frames ago (before a climb took him up the ladder), and
        /// held there every frame though it hangs off his body; empty -- what
        /// he hauls is in his arms meanwhile (`VillagerActing.Barrow` off).
        void Park()
        {
            if (!parked)
            {
                parked = true;
                awayFor = 0f;
                settledK = -1f;
                if (hasBodyWas2) Settle(0f, bodyWas2, yawWas2);
                else if (hasBodyWas) Settle(0f, bodyWas, yawWas);
                else Settle(0f);
                parkPos = holder.position;
                parkRot = holder.rotation;
                parkTilt = tilt.localRotation;
                settledK = -1f;
            }
            holder.SetPositionAndRotation(parkPos, parkRot);
            tilt.localRotation = parkTilt;
            ShowLoad(null, 0);
            SetActing(false, false);
            hasLast = false;
            push = 0f;
            speed = 0f;
        }

        /// No longer asked to stay parked: back with him when he is beside
        /// it, or after `ReattachSeconds` (it catches him up). True = re-attached.
        bool Reattach(float dt)
        {
            Vector3 d = transform.position - parkPos;
            d.y = 0f;
            awayFor += dt;
            if (d.magnitude > ReattachMetres && awayFor < ReattachSeconds) return false;
            parked = false;
            settledK = -1f;
            return true;
        }

        /// The fists' reach ahead of his root while the `Carry` clip pushes
        /// (measured, BarrowShot: fists 0.42 m ahead; the flat-ground grips
        /// sit there too).
        const float FistReach = 0.42f;

        void SettleAt(Art art, Vector3 p, float k, float slide)
        {
            // By his yaw only, in the world (2026-10-03): a body laid flat by
            // the fire must not roll the barrow over with him.
            holder.SetPositionAndRotation(p + settleYaw * (Vector3.Lerp(parkOffset, pushOffset, k) + Vector3.forward * slide), settleYaw);
            Vector3 right = holder.right, ahead = holder.forward;
            right.y = 0f; ahead.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;

            // No height field under his own feet: level ground at his feet.
            bool flat = false;
            if (GroundOverride == null)
            {
                var h = CameraRig.GroundPick.Height;
                flat = h == null || Mathf.Abs(WorkerPad.Foot(p, h(p.x, p.z)) - p.y) > FieldSlack;
            }

            // The wheel: ground and its slope under the axle.
            Vector3 axle = holder.TransformPoint(art.pivot);
            float gf = Ground(axle + ahead * Probe, flat), gb = Ground(axle - ahead * Probe, flat);
            float gr = Ground(axle + right * Probe, flat), gl = Ground(axle - right * Probe, flat);
            float sz = (gf - gb) / (2f * Probe), sx = (gr - gl) / (2f * Probe);

            // The legs: the ground under each foot's outer edge (rest pose, close enough).
            float legR = Ground(holder.TransformPoint(new Vector3(LegX, 0f, LegMidZ)), flat);
            float legL = Ground(holder.TransformPoint(new Vector3(-LegX, 0f, LegMidZ)), flat);

            // Pushed, the grips stay level in his fists (no roll); parked, it
            // rolls onto both legs.
            float rollPark = Mathf.Atan2(legR - legL, 2f * LegX) * Mathf.Rad2Deg;
            float roll = Mathf.Lerp(rollPark, 0f, k);

            // The axle over the ground plane (gradient sz ahead, sx to the
            // right) so the wheel, a disc rolled by `roll` with a tread
            // `TreadHalf` either side, just touches it at its lowest edge:
            // h = R sqrt(sz^2 + (cos r + sx sin r)^2) + w |sin r - sx cos r|.
            float rr = roll * Mathf.Deg2Rad, sr0 = Mathf.Sin(rr), cr0 = Mathf.Cos(rr);
            float up = cr0 + sx * sr0;
            float axleY = (gf + gb + gr + gl) * 0.25f + WheelReach * Mathf.Sqrt(sz * sz + up * up)
                          + TreadHalf * Mathf.Abs(sr0 - sx * cr0);
            holder.position += Vector3.up * (axleY - axle.y);

            // The legs: the least pitch at which every foot corner is `clear`
            // over the ground plane under where it really ends up (pitched,
            // then rolled): for a foot point (x, y, z) of the axle frame,
            // y1 (c + sxL s) - sz z1 >= ground - axle - sz zm + sxL x (c - 1) - x s,
            // y1 = y cos a - z sin a, z1 = y sin a + z cos a (c, s: the roll).
            float footY = -art.pivot.y, zm = LegMidZ - art.pivot.z;
            float sxL = (legR - legL) / (2f * LegX), cp = cr0 + sxL * sr0;
            float LegsAt(float clear)
            {
                float best = -180f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * LegX;
                    float t = (side > 0 ? legR : legL) + clear - axleY - sz * zm + sxL * x * (cr0 - 1f) - x * sr0;
                    for (int e = 0; e < 2; e++)
                    {
                        float z = (e == 0 ? LegFront : LegBack) - art.pivot.z;
                        best = Mathf.Max(best, PitchFor(cp * footY - sz * z, -cp * z - sz * footY, t));
                    }
                }
                return best;
            }
            float park = LegsAt(0f);
            Vector3 grip = (art.gripL + art.gripR) * 0.5f;
            // Above his feet: his fists' height (`FollowFists`; the flat-ground
            // grip height until they are on the grips).
            float gripRise = fistRise >= 0f ? fistRise : FlatGripRise(art);
            float held = Mathf.Max(PitchFor(grip.y, -grip.z, p.y + gripRise - axleY), LegsAt(LegClear));
            float pitch = Mathf.Clamp(Mathf.Lerp(park, held, k), -MaxPitch, MaxPitch);
            // +X about the axle lifts the handles (the rear, -Z); the wheel
            // stays on the ground. The roll is about his forward, through the axle.
            tilt.localRotation = Quaternion.Euler(0f, 0f, roll) * Quaternion.Euler(pitch, 0f, 0f);
        }

        /// The pitch a (degrees, +X about the axle) solving A cos a + B sin a
        /// = t on the rising branch through the rest pose (B > 0). A point
        /// (y, z) of the axle frame stands y cos a - z sin a above the axle:
        /// A = y, B = -z.
        static float PitchFor(float a, float b, float t)
        {
            float rho = Mathf.Sqrt(a * a + b * b);
            if (rho < 1e-4f) return 0f;
            return (Mathf.Atan2(b, a) - Mathf.Acos(Mathf.Clamp(t / rho, -1f, 1f))) * Mathf.Rad2Deg;
        }

        /// Hand the arms over: no prop in them, arms forward while pushing.
        void SetActing(bool barrow, bool arms)
        {
            if (acting == null) return;
            acting.Barrow = barrow;
            acting.BarrowArms = barrow && arms;
        }

        // --- the barrow ---------------------------------------------------------

        bool Build()
        {
            if (root != null) return true;
            var art = Art.Load();
            if (art == null) return false;
            root = new GameObject("RunnerBarrow");
            root.transform.SetParent(transform, false);
            holder = root.transform;
            holder.localPosition = parkOffset;
            tilt = new GameObject("Tilt").transform;
            tilt.SetParent(holder, false);
            tilt.localPosition = art.pivot;
            Node(art.body, art.bodyNode, art.mat, tilt);
            wheel = new GameObject("Wheel").transform;
            wheel.SetParent(tilt, false);
            Node(art.wheel, art.wheelNode, art.wheelMat, wheel);
            load = new GameObject("Load").transform;
            load.SetParent(tilt, false);
            load.localPosition = art.anchor;
            loadKey = null;
            return true;
        }

        static void Node(Mesh mesh, Matrix4x4 m, Material mat, Transform parent)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = m.GetColumn(3);
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// The tray's contents, rebuilt only when the load changes.
        void ShowLoad(string res, int n)
        {
            string key = string.IsNullOrEmpty(res) ? "" : res + "x" + n;
            if (key == loadKey) return;
            loadKey = key;
            for (int i = load.childCount - 1; i >= 0; i--)
            {
                var c = load.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            if (key.Length == 0) return;
            // Everything hangs off one holder so `Fit` can hold it to the tray.
            var into = new GameObject("Contents").transform;
            into.SetParent(load, false);
            bool kit = false;
            switch (res)
            {
                case Res.Timber: kit = Logs(n, into); break;
                case Res.Boards:
                case Res.FineBoards: kit = Planks(n, res == Res.FineBoards, into); break;
                case Res.Stone:
                case Res.Ore: kit = Rocks(res, n, into); break;
                case Res.Brick: kit = Bricks(n, into); break;
            }
            if (!kit)
            {
                // A kit mesh failed half way: start the holder over.
                for (int i = into.childCount - 1; i >= 0; i--)
                {
                    var c = into.GetChild(i).gameObject;
                    c.transform.SetParent(null, false);   // Destroy is deferred: out of Fit's sight now
                    if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
                }
                // Everything else (small goods in the carry crate, bars, hides,
                // tools, game...): the arms' own layout, turned to lie along the
                // tray (the crate's long side runs front to back) and centred.
                var turn = new GameObject("CarryLayout").transform;
                turn.SetParent(into, false);
                turn.localRotation = Quaternion.Euler(0f, 90f, 0f);
                CarryLook.Build(res, n, turn);
                var pushed = turn.Find("Load");
                if (pushed != null) pushed.localPosition = Vector3.zero;
            }
            Fit(into);
        }

        /// **Held to the tray.** The layout's renderer bounds (mesh bounds,
        /// so every vertex) in the Load frame; if they leave the inside box,
        /// the whole layout is scaled down uniformly and set on the floor in
        /// the middle. The kit layouts are already inside (check-fit.py), so
        /// for them this does nothing; the carry crate (0.59 m long) shrinks
        /// to 96 %, a spear bundle a lot.
        void Fit(Transform into)
        {
            var toLoad = load.worldToLocalMatrix;
            bool any = false;
            Vector3 lo = Vector3.zero, hi = Vector3.zero;
            foreach (var mf in into.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = toLoad * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    if (!any) { lo = hi = p; any = true; }
                    else { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                }
            }
            if (!any) return;
            const float eps = 1e-4f;
            if (lo.x >= -InX - eps && hi.x <= InX + eps && lo.z >= InBack - eps && hi.z <= InFront + eps
                && lo.y >= InFloor - eps && hi.y <= MaxHeap + eps) return;
            Vector3 size = hi - lo;
            float s = Mathf.Min(1f, 2f * InX / Mathf.Max(size.x, 1e-4f),
                (InFront - InBack) / Mathf.Max(size.z, 1e-4f), (MaxHeap - InFloor) / Mathf.Max(size.y, 1e-4f));
            into.localScale = into.localScale * s;
            into.localPosition += new Vector3(-(lo.x + hi.x) * 0.5f * s, InFloor - lo.y * s,
                (InBack + InFront) * 0.5f - (lo.z + hi.z) * 0.5f * s);
        }

        /// One pile unit with its long axis (z) cut to a billet.
        static bool Unit(string res, Transform into, Vector3 at, float yaw, float k, float length)
        {
            var go = ResourceKit.Spawn(res, false, into, at, Quaternion.Euler(0f, yaw, 0f), k);
            if (go == null) return false;
            var sc = go.transform.localScale;
            sc.z *= length / (UnitLength * k);
            go.transform.localScale = sc;
            return true;
        }

        /// Logs lengthwise, stacked 3 / 2 / 1 (Timber_Unit: 1.60 m long on z,
        /// 0.245 thick, bottom origin), cut to `BilletLength`: 0.15 x 0.50 m.
        /// The top log rises 0.12 m over the rim, over the floor only.
        bool Logs(int n, Transform into)
        {
            n = Mathf.Clamp(n, 1, MaxLogs);
            float d = 0.245f * longScale;
            for (int i = 0; i < n; i++)
            {
                Vector3 at;
                if (i < 3) at = new Vector3((i - 1) * d * 1.02f, InFloor, 0f);
                else if (i < 5) at = new Vector3((i == 3 ? -0.5f : 0.5f) * d, InFloor + 0.86f * d, 0f);
                else at = new Vector3(0f, InFloor + 1.72f * d, 0f);
                at.z += ((i * 37) % 5 - 2) * 0.008f;
                if (!Unit(Res.Timber, into, at, ((i * 53) % 5 - 2) * 1.2f, longScale, BilletLength)) return false;
            }
            return true;
        }

        /// A bundle of planks lengthwise, two side by side, layer on layer
        /// (Boards_Unit: 1.60 x 0.25 wide x 0.075 m, long on z, bottom origin),
        /// cut to `BilletLength`. Ten = five layers, 0.23 m: under the rim.
        bool Planks(int n, bool fine, Transform into)
        {
            n = Mathf.Clamp(n, 1, MaxPlanks);
            float k = longScale * (fine ? 0.92f : 1f);
            float w = 0.25f * k, th = 0.075f * k;
            for (int i = 0; i < n; i++)
            {
                int layer = i / 2, col = i % 2;
                bool lone = n % 2 == 1 && i == n - 1;
                var at = new Vector3(lone ? 0f : (col - 0.5f) * (w + 0.01f), InFloor + layer * th, ((i * 17) % 3 - 1) * 0.012f);
                if (!Unit(Res.Boards, into, at, i % 2 == 0 ? 1f : -1f, k, BilletLength)) return false;
            }
            return true;
        }

        /// Stones / ore: a 3 x 3 bed (front row first, every other one turned a
        /// quarter), then three in the hollows on top. 0.16 m tall at twelve.
        static readonly Vector2[] RockTop = { new Vector2(-0.08f, 0.085f), new Vector2(0.08f, 0.085f), new Vector2(0f, -0.085f) };
        bool Rocks(string res, int n, Transform into)
        {
            n = Mathf.Clamp(n, 1, MaxRocks);
            float h = 0.31f * rockScale;
            for (int i = 0; i < n; i++)
            {
                Vector3 at = i < 9
                    ? new Vector3((i % 3 - 1) * 0.16f, InFloor, (1 - i / 3) * 0.17f)
                    : new Vector3(RockTop[i - 9].x, InFloor + 0.55f * h, RockTop[i - 9].y);
                float yaw = (i % 2) * 90f + ((i * 7) % 5 - 2) * 3f;
                if (ResourceKit.Spawn(res, false, into, at, Quaternion.Euler(0f, yaw, 0f), rockScale) == null) return false;
            }
            return true;
        }

        /// Bricks laid like a hod: twelve across the tray (3 x 4), then twelve
        /// crosswise on top (4 x 3). 0.11 m tall at twenty-four.
        bool Bricks(int n, Transform into)
        {
            n = Mathf.Clamp(n, 1, MaxBricks);
            float h = 0.12f * brickScale;
            for (int i = 0; i < n; i++)
            {
                int layer = i / 12, j = i % 12;
                Vector3 at;
                float yaw;
                if (layer == 0) { at = new Vector3((j % 3 - 1) * 0.172f, InFloor, 0.195f - (j / 3) * 0.13f); yaw = 0f; }
                else { at = new Vector3((j % 4 - 1.5f) * 0.13f, InFloor + h, (1 - j / 4) * 0.18f); yaw = 90f; }
                yaw += ((i * 7) % 3 - 1) * 2f;
                if (ResourceKit.Spawn(Res.Brick, false, into, at, Quaternion.Euler(0f, yaw, 0f), brickScale) == null) return false;
            }
            return true;
        }

        // --- the FBX, read once ---------------------------------------------------

        /// The barrow's meshes and markers in a clean, unit-scale frame: the
        /// README's (root = ground under his hips, +Z to the wheel), every
        /// node relative to the axle (`pivot`). The FBX's own empties carry
        /// x100 scales, so nothing is ever parented under them.
        sealed class Art
        {
            public const int Slots = 12;
            public Mesh body, wheel;
            public Material mat, wheelMat;
            public Matrix4x4 bodyNode, wheelNode;   // relative to the axle
            public Vector3 pivot, anchor;           // root frame
            public Vector3[] slots = new Vector3[Slots];   // tilt (axle) frame
            public Vector3 gripL, gripR;            // tilt (axle) frame

            static Art art;
            static bool tried;

            public static Art Load()
            {
                if (tried) return art;
                tried = true;
                var prefab = Resources.Load<GameObject>("Kits/Carry/Wheelbarrow");
                if (prefab == null)
                {
                    Debug.LogWarning("[RunnerBarrow] no Resources/Kits/Carry/Wheelbarrow: runners carry in their arms");
                    return null;
                }
                var top = prefab.transform;
                Transform Find(string n)
                {
                    foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                        if (t.name == n) return t;
                    return null;
                }
                var pivotT = Find("Tilt_Pivot");
                var bodyT = Find("Wheelbarrow_Body");
                var wheelT = Find("Wheel");
                var bodyF = bodyT != null ? bodyT.GetComponent<MeshFilter>() : null;
                var wheelF = wheelT != null ? wheelT.GetComponent<MeshFilter>() : null;
                if (pivotT == null || bodyF == null || wheelF == null)
                {
                    Debug.LogWarning("[RunnerBarrow] the wheelbarrow FBX is missing Tilt_Pivot / Wheelbarrow_Body / Wheel");
                    return null;
                }
                // **Turned back to face +Z.** The v1 export lands half round
                // (axle at z -1.55, like the carry crate); the README's frame
                // has the barrow in front of him.
                Matrix4x4 fix = Matrix4x4.identity;
                if (top.InverseTransformPoint(pivotT.position).z < 0f) fix = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));
                Vector3 Pt(Transform t) => fix.MultiplyPoint3x4(top.InverseTransformPoint(t.position));
                var a = new Art { pivot = Pt(pivotT) };
                var toAxle = Matrix4x4.Translate(-a.pivot) * fix * top.worldToLocalMatrix;
                a.body = bodyF.sharedMesh;
                a.wheel = wheelF.sharedMesh;
                a.mat = bodyF.GetComponent<MeshRenderer>().sharedMaterial;
                a.wheelMat = wheelF.GetComponent<MeshRenderer>().sharedMaterial;
                a.bodyNode = toAxle * bodyF.transform.localToWorldMatrix;
                a.wheelNode = toAxle * wheelF.transform.localToWorldMatrix;
                var anchorT = Find("Load_Anchor");
                a.anchor = (anchorT != null ? Pt(anchorT) : new Vector3(0f, 0.43f, 0.895f)) - a.pivot;
                for (int i = 0; i < Slots; i++)
                {
                    var s = Find("Slot_" + i);
                    a.slots[i] = s != null ? Pt(s) - a.pivot
                        : new Vector3((i % 3 - 1) * 0.18f, 0.43f, 1.1162f - (i / 3) * 0.1475f) - a.pivot;
                }
                var gl = Find("Grip_L");
                var gr = Find("Grip_R");
                a.gripL = (gl != null ? Pt(gl) : new Vector3(-0.25f, 0.64f, 0.32f)) - a.pivot;
                a.gripR = (gr != null ? Pt(gr) : new Vector3(0.25f, 0.64f, 0.32f)) - a.pivot;
                art = a;
                return art;
            }
        }

        /// Where the two grips are right now, world space (shots / probes).
        public bool Grips(out Vector3 left, out Vector3 right)
        {
            left = right = Vector3.zero;
            var art = Art.Load();
            if (art == null || tilt == null) return false;
            left = tilt.TransformPoint(art.gripL);
            right = tilt.TransformPoint(art.gripR);
            return true;
        }
    }
}
