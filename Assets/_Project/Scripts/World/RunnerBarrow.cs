using UnityEngine;

namespace SeaSick.World
{
    /// **The store runner's wheelbarrow** (2026-10-02, `art-staging/wheelbarrow-v1`,
    /// `Resources/Kits/Carry/Wheelbarrow`, imported by `Dev.WheelbarrowImport`).
    ///
    /// Sits on a villager body. While its hand is a RUNNER (`IsRunner`: work
    /// order at the store hut) and the body is out and about, the body pushes
    /// a timber barrow: parented to the BODY ROOT (never a bone: Astra's rig
    /// carries x100 / ~92x bone scales, see the rig-scale trap), the grips
    /// where the `Carry` clip holds his fists, tipped up on its wheel while he
    /// walks and set down on its legs in front of him when he stands. The
    /// wheel turns by the distance he really covered. What he hauls
    /// (`VillagerActing.Load`, the same load `CampWorker` hands the arms)
    /// lies in the tray instead of in his arms: logs and planks lengthwise,
    /// stones and bricks on the tray's `Slot_n` grid, small goods in the open
    /// carry crate; `VillagerActing.Barrow` hides the arm prop meanwhile and
    /// `BarrowArms` keeps his arms forward on the grips while he walks empty.
    ///
    /// Pure show, visual only: no collider, no ledger, no pathing. Driven
    /// by `Sync` from `CampWorker.Update` every frame; a body that is not
    /// synced this frame (flying, downed, aboard, no row) hides it.
    public class RunnerBarrow : MonoBehaviour
    {
        // --- tunables ---------------------------------------------------------

        /// Degrees the barrow tips up on its axle while pushed (the README's
        /// 7.04 lifts the grips to 0.79 m; the v15 `Carry` clip holds his
        /// fists at ~0.75 m, so a little less).
        [SerializeField] private float pushTilt = 5.5f;
        /// Barrow root (the ground point under his hips) against the body
        /// root while pushed: the tilted grips sit 0.36 m ahead of the barrow root, the Carry clip holds his fists 0.42 m ahead (measured, BarrowShot).
        [SerializeField] private Vector3 pushOffset = new Vector3(0f, 0f, 0.06f);
        /// ...and while parked on its legs (a step in front of him, let go).
        [SerializeField] private Vector3 parkOffset = new Vector3(0f, 0f, 0.20f);
        /// Seconds to tip up / set down.
        [SerializeField] private float easeSeconds = 0.2f;
        /// Ground speed (m/s, smoothed) above which he is pushing.
        [SerializeField] private float moveSpeed = 0.15f;
        /// Scale of a pile-unit log / plank in the tray (the units are 1.6 m).
        [SerializeField] private float longScale = 0.6f;
        /// Scale of a pile-unit stone (0.49 m) / brick (0.36 m) on one slot.
        [SerializeField] private float rockScale = 0.34f;
        [SerializeField] private float brickScale = 0.48f;

        /// Most of one load drawn in the tray (the ledger's count is the truth).
        const int MaxLogs = 6, MaxPlanks = 10, MaxBricks = 24;
        /// Wheel radius, metres (README: 0.48 m across).
        const float WheelRadius = 0.24f;

        // --- who pushes one -----------------------------------------------------

        /// **A runner**: a hand on the work order at the store hut
        /// (`OutpostLedger.IsRunner`).
        public static bool IsRunner(OutpostHand h) => OutpostLedger.IsRunner(h);

        /// **Called every frame from `CampWorker.Update`** for a body on the
        /// ground with a row. Adds the component the first time its hand is a
        /// runner; otherwise costs one `GetComponent`.
        public static void Sync(Component body, OutpostHand r, VillagerActing acting)
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
                return;
            }
            if (!root.activeSelf) root.SetActive(true);

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
            holder.localPosition = Vector3.Lerp(parkOffset, pushOffset, k);
            // +X about the axle lifts the handles (the rear, -Z); the wheel
            // stays on the ground.
            tilt.localRotation = Quaternion.Euler(pushTilt * k, 0f, 0f);
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
            var art = Art.Load();
            switch (res)
            {
                case Res.Timber: if (Logs(n)) return; break;
                case Res.Boards:
                case Res.FineBoards: if (Planks(n, res == Res.FineBoards)) return; break;
                case Res.Stone:
                case Res.Ore: if (OnSlots(res, n, rockScale, 12, art, 37f)) return; break;
                case Res.Brick: if (OnSlots(res, n, brickScale, MaxBricks, art, 0f)) return; break;
            }
            // Everything else (small goods in the carry crate, bars, hides,
            // tools, game...): the arms' own layout, turned to lie along the
            // tray (the crate's long side runs front to back) and centred.
            var turn = new GameObject("CarryLayout").transform;
            turn.SetParent(load, false);
            turn.localRotation = Quaternion.Euler(0f, 90f, 0f);
            CarryLook.Build(res, n, turn);
            var pushed = turn.Find("Load");
            if (pushed != null) pushed.localPosition = Vector3.zero;
        }

        /// Logs lengthwise, stacked 3 / 2 / 1 (Timber_Unit: 1.60 m long on z,
        /// 0.245 thick, bottom origin).
        bool Logs(int n)
        {
            n = Mathf.Clamp(n, 1, MaxLogs);
            float d = 0.245f * longScale;
            for (int i = 0; i < n; i++)
            {
                Vector3 at;
                if (i < 3) at = new Vector3((i - 1) * d * 1.02f, 0f, 0f);
                else if (i < 5) at = new Vector3((i == 3 ? -0.5f : 0.5f) * d, 0.86f * d, 0f);
                else at = new Vector3(0f, 1.72f * d, 0f);
                at.z += ((i * 37) % 5 - 2) * 0.015f;
                var yaw = Quaternion.Euler(0f, ((i * 53) % 5 - 2) * 1.2f, 0f);
                if (ResourceKit.Spawn(Res.Timber, false, load, at, yaw, longScale) == null) return false;
            }
            return true;
        }

        /// A bundle of planks lengthwise, two side by side, layer on layer
        /// (Boards_Unit: 1.60 x 0.25 wide x 0.075 m, long on z, bottom origin).
        bool Planks(int n, bool fine)
        {
            n = Mathf.Clamp(n, 1, MaxPlanks);
            float k = longScale * (fine ? 0.92f : 1f);
            float w = 0.25f * k, th = 0.075f * k;
            for (int i = 0; i < n; i++)
            {
                int layer = i / 2, col = i % 2;
                bool lone = n % 2 == 1 && i == n - 1;
                var at = new Vector3(lone ? 0f : (col - 0.5f) * (w + 0.01f), layer * th, ((i * 17) % 3 - 1) * 0.02f);
                var yaw = Quaternion.Euler(0f, (i % 2 == 0 ? 1f : -1f), 0f);
                if (ResourceKit.Spawn(Res.Boards, false, load, at, yaw, k) == null) return false;
            }
            return true;
        }

        /// One unit per tray slot, front row first; a second layer on top
        /// once the twelve are full.
        bool OnSlots(string res, int n, float k, int max, Art art, float yawStep)
        {
            n = Mathf.Clamp(n, 1, max);
            float h = (res == Res.Brick ? 0.12f : 0.31f) * k;
            for (int i = 0; i < n; i++)
            {
                Vector3 at = art.slots[i % Art.Slots] - art.anchor + new Vector3(0f, (i / Art.Slots) * h, 0f);
                var yaw = Quaternion.Euler(0f, yawStep * i + ((i * 7) % 3 - 1) * 3f, 0f);
                if (ResourceKit.Spawn(res, false, load, at, yaw, k) == null) return false;
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
