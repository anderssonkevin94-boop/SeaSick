using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **A hunter's spear and the carcass on his shoulders (2026-09-26).**
    ///
    /// Kevin, phone playtest: *"make a placeholder spear. make sure that the
    /// animal he kills dies. and that he carries the animal back to camp."*
    ///
    /// Pure show, like `VillagerActing`, and kept OUT of it on purpose (that
    /// file owns the work tools and the load stack): this owns the two
    /// things only a hunter has. `CampWorker.TickHunting` calls `Drive`
    /// every frame he is on a hunt; a frame without a call means he is not
    /// hunting any more, and both props go away on their own -- no order
    /// change, re-assignment or hand-grab can leave a spear floating or a
    /// goat stuck to a man who went off to saw planks.
    ///
    /// **Same frame rules as `VillagerActing`'s header.** Every length is in
    /// BODY metres: the props hang off the villager's ROOT (its own scale,
    /// ~1.7 m tall), never off a bone -- the deckhand's bones carry a ~92x
    /// scale (`astra-rig-scale-trap`) and anything parented to them explodes.
    /// The grip is READ off the live hand bone each frame and the spear is
    /// placed in world space along directions made from the character's
    /// own right/up/forward, never a bone's local axes.
    ///
    /// Runs after `VillagerActing` (execution order) so the grip it reads is
    /// the arm as it is actually posed this frame.
    [DefaultExecutionOrder(200)]
    public class HunterProps : MonoBehaviour
    {
        public enum Pose { Upright, Thrust }

        /// The props on this body, added if it has none.
        public static HunterProps On(GameObject body)
        {
            if (body == null) return null;
            var p = body.GetComponent<HunterProps>();
            return p != null ? p : body.AddComponent<HunterProps>();
        }

        // --- the spear, in BODY metres (grip at the origin, +Y to the tip) --

        /// Grip to the butt, and grip to the base of the head: a 1.8 m spear
        /// for a 1.7 m man, held a little below its middle.
        const float ButtBelow = 0.65f, HeadAbove = 1.15f;
        const float ShaftThick = 0.035f;
        /// A thrust: how far the spear drives forward and back, and how
        /// fast. Twice the `Bend` stoop's rate, so each stoop is two jabs.
        const float JabReach = 0.28f, JabPeriod = 0.6f;
        /// Wrist to the middle of the fist, as `VillagerActing.PalmReach`.
        const float PalmReach = 0.07f;

        // --- the carcass, in BODY metres -------------------------------------

        /// Where the middle of the beast sits: across the shoulders, a hand
        /// behind the neck.
        static readonly Vector3 CarcassAt = new Vector3(0f, 1.52f, -0.10f);
        /// Longest a carried beast may read. A boar bigger than this is
        /// shrunk to it -- this is the picture of a carcass, and one wider
        /// than a doorway on a 1.7 m man reads as a joke.
        const float CarcassMaxLength = 1.25f;

        Transform armR, handR;
        bool tried;

        GameObject spear;
        string spearFor;
        Animal carcass;
        int drivenFrame = -10;
        string wantSpear;
        Pose pose;
        Vector3 aim;
        float jabClock;

        public bool HasCarcass => carcass != null;

        /// **One frame of hunting.** `spearRes` is what the ledger says is
        /// in his hand (`OutpostLedger.SpearInHand`, null = none: no spear
        /// is drawn). `aimAt` is the world point a thrust drives at.
        public void Drive(string spearRes, Pose p, Vector3 aimAt = default)
        {
            drivenFrame = Time.frameCount;
            wantSpear = spearRes;
            if (p == Pose.Thrust && pose != Pose.Thrust) jabClock = 0f;
            pose = p;
            aim = aimAt;
        }

        /// **Lift a dead beast onto his shoulders.** The animal's own body,
        /// not a stand-in: re-parented to the villager root (world scale
        /// kept), laid across the shoulders on its side, legs forward and
        /// down. The carcass is his from here -- `PutDown` destroys it.
        public void Shoulder(Animal a)
        {
            if (a == null) return;
            PutDown();
            carcass = a;
            a.Shouldered = true;
            a.enabled = false;   // the flop is over; nothing left to think about

            var t = a.transform;
            Bounds local = LocalBounds(t);
            t.SetParent(transform, true);

            // Scale: keep the beast's own size unless it is too long to read.
            Vector3 ls = t.localScale;
            float len = Mathf.Max(local.size.x * ls.x, Mathf.Max(local.size.y * ls.y, local.size.z * ls.z));
            if (len > CarcassMaxLength && len > 1e-4f) { ls *= CarcassMaxLength / len; t.localScale = ls; }

            // On its side across the shoulders: its nose along his right
            // (+X), its back toward his back (-Z), then pitched so the legs
            // hang forward and down over his chest.
            Quaternion rot = Quaternion.Euler(35f, 0f, 0f)
                * Quaternion.LookRotation(Vector3.right, Vector3.back);
            t.localRotation = rot;
            t.localPosition = CarcassAt - rot * Vector3.Scale(local.center, ls);
        }

        /// The carcass is at the store: the books took it there already.
        public void PutDown()
        {
            if (carcass != null) Destroy(carcass.gameObject);
            carcass = null;
        }

        void LateUpdate()
        {
            bool driven = Time.frameCount - drivenFrame <= 1;
            if (!driven)
            {
                // Not hunting any more (re-ordered, hauling, lifted by the
                // Hand, or the camp stopped being watched).
                if (spear != null && spear.activeSelf) spear.SetActive(false);
                if (carcass != null) PutDown();
                return;
            }

            if (string.IsNullOrEmpty(wantSpear))
            {
                if (spear != null && spear.activeSelf) spear.SetActive(false);
                return;
            }
            EnsureSpear(wantSpear);
            if (!spear.activeSelf) spear.SetActive(true);
            PlaceSpear(Time.deltaTime);
        }

        void OnDisable()
        {
            if (spear != null) spear.SetActive(false);
            PutDown();
        }

        // --- the spear ---------------------------------------------------------

        void PlaceSpear(float dt)
        {
            Bind();
            Transform body = transform;
            float s = BodyScale;
            Vector3 grip = Grip(s);

            Vector3 dir;
            if (pose == Pose.Thrust)
            {
                dir = aim - grip;
                if (dir.sqrMagnitude < 1e-4f) dir = body.forward;
                dir.Normalize();
                // Never past level-and-a-bit upward: it is a downward jab
                // at an animal, not a salute.
                if (Vector3.Dot(dir, body.up) > 0.2f)
                    dir = (Vector3.ProjectOnPlane(dir, body.up).normalized + body.up * 0.2f).normalized;
                jabClock += dt;
                float k = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * jabClock / JabPeriod);
                grip += dir * (JabReach * s * k);
            }
            else
            {
                // A walking staff: point up, leaning a little forward and
                // out, butt a hand above the ground.
                dir = (body.up + body.forward * 0.22f + body.right * 0.06f).normalized;
            }

            Vector3 hint = Mathf.Abs(Vector3.Dot(dir, body.up)) > 0.7f ? body.forward : body.up;
            spear.transform.SetPositionAndRotation(grip,
                Quaternion.LookRotation(dir, hint) * Quaternion.Euler(90f, 0f, 0f));
        }

        Vector3 Grip(float s)
        {
            if (handR != null)
            {
                Vector3 wrist = handR.position;
                Transform fore = handR.parent;
                Vector3 along = fore != null && fore != transform ? wrist - fore.position : -transform.up;
                if (along.sqrMagnitude < 1e-10f) along = -transform.up;
                return wrist + along.normalized * (PalmReach * s);
            }
            if (armR != null) return armR.position - transform.up * 0.6f * s;
            return transform.TransformPoint(new Vector3(0.25f, 0.85f, 0.10f));
        }

        float BodyScale
        {
            get
            {
                float s = transform.lossyScale.y;
                return s > 1e-4f ? s : 1f;
            }
        }

        /// The arm on the body's +X side and its wrist. Read off the rig
        /// (which arm is REALLY on the right), as `VillagerActing.Bind` does.
        void Bind()
        {
            if (tried) return;
            tried = true;
            Transform a = null, b = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (a == null && (n == "arm_l" || n.Contains("upper_arm.l"))) a = t;
                else if (b == null && (n == "arm_r" || n.Contains("upper_arm.r"))) b = t;
            }
            if (a != null && b != null)
                armR = transform.InverseTransformPoint(a.position).x > transform.InverseTransformPoint(b.position).x ? a : b;
            else armR = a != null ? a : b;
            handR = HandUnder(armR);
        }

        static Transform HandUnder(Transform arm)
        {
            if (arm == null) return null;
            Transform best = null;
            int bestDepth = int.MaxValue;
            foreach (var t in arm.GetComponentsInChildren<Transform>(true))
            {
                if (t == arm || t.name.IndexOf("hand", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                int depth = 0;
                for (var p = t; p != null && p != arm; p = p.parent) depth++;
                if (depth < bestDepth) { best = t; bestDepth = depth; }
            }
            return best;
        }

        /// The stone or iron spear (`ToolKit` mesh; fallback: a shaft plus a
        /// knapped stone head bound on with sinew, or a longer, thinner iron
        /// head). Built once per kind, hung off the ROOT with local scale 1,
        /// so it is in body metres.
        void EnsureSpear(string res)
        {
            if (spear != null && spearFor == res) return;
            if (spear != null) Destroy(spear);
            spearFor = res;
            bool iron = res == Res.IronSpear;

            var root = new GameObject(iron ? "HunterSpear_Iron" : "HunterSpear_Stone");
            root.transform.SetParent(transform, false);

            // **Astra's worker tools v1, Kevin approved 2026-09-30:** her
            // stone / iron spear, true metres in the tool frame (grip at the
            // origin, .65 m of butt below and the haft to 1.15 m above, the
            // head on top of that -- `ButtBelow` / `HeadAbove` are the mesh's
            // own numbers), at identity so `PlaceSpear` poses it as it did
            // the primitives. The primitives below stay as the fallback if
            // the mesh fails to load.
            if (ToolKit.Attach(iron ? ToolKit.SpearIron : ToolKit.SpearStone, root.transform, false))
            {
                spear = root;
                return;
            }

            var wood = Mat("spear_shaft", new Color(0.47f, 0.33f, 0.20f));
            var shaft = Prim(PrimitiveType.Cylinder, root.transform,
                new Vector3(ShaftThick, (ButtBelow + HeadAbove) * 0.5f, ShaftThick), wood);
            shaft.transform.localPosition = new Vector3(0f, (HeadAbove - ButtBelow) * 0.5f, 0f);

            // The head: a cube turned 45 deg into a diamond, stretched along
            // the shaft by its pivot into a leaf blade.
            var headMat = iron ? Mat("spear_iron", new Color(0.55f, 0.58f, 0.62f))
                               : Mat("spear_stone", new Color(0.36f, 0.36f, 0.38f));
            float bladeW = iron ? 0.05f : 0.065f, bladeT = iron ? 0.014f : 0.024f, stretch = iron ? 3.4f : 2.5f;
            var pivot = new GameObject("Head").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, HeadAbove + bladeW * 0.707f * stretch * 0.9f, 0f);
            pivot.localScale = new Vector3(1f, stretch, 1f);
            var blade = Prim(PrimitiveType.Cube, pivot, new Vector3(bladeW, bladeW, bladeT), headMat);
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            // The binding where head meets shaft (stone only; iron has a socket).
            var bindMat = iron ? headMat : Mat("spear_sinew", new Color(0.25f, 0.18f, 0.12f));
            var band = Prim(PrimitiveType.Cylinder, root.transform,
                new Vector3(ShaftThick * 1.5f, 0.035f, ShaftThick * 1.5f), bindMat);
            band.transform.localPosition = new Vector3(0f, HeadAbove, 0f);

            spear = root;
        }

        // --- helpers -----------------------------------------------------------

        /// The beast's mesh bounds in its own root space (rotation-free, so
        /// a flopped animal measures the same as a standing one).
        static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) Encap(root, mf.transform, mf.sharedMesh.bounds, ref b, ref any);
            foreach (var sm in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sm.sharedMesh != null) Encap(root, sm.transform, sm.sharedMesh.bounds, ref b, ref any);
            if (!any) b = new Bounds(new Vector3(0f, 0.4f, 0f), new Vector3(0.4f, 0.8f, 1f));
            return b;
        }

        static void Encap(Transform root, Transform part, Bounds mb, ref Bounds b, ref bool any)
        {
            Vector3 c = mb.center, e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = root.InverseTransformPoint(part.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// Shared across every hunter, one per colour -- the same shader
        /// choice as `VillagerActing.Mat`, so it is in the phone build.
        static Material Mat(string key, Color c)
        {
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find(
                WorldArtStyle.Instance != null ? "SeaSick/Environment Toon" : "Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            mats[key] = m;
            return m;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }
    }
}
