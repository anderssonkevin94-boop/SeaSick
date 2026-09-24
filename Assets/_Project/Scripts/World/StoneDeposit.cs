using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **A rock you can quarry (2026-09-24).** Kevin, phone, after Astra's
    /// island makeover: *"the stone now exists on the island. so include
    /// them as a resource that can be gathered and stored. you should have
    /// the assets for those."*
    ///
    /// Sits on a Stone `ResourceNode` and wears one of Astra's stone-resource
    /// kit v2 deposits (`Resources/StoneKit/Stone_<Shape>`), replacing
    /// whatever the prop showed before (the populator's boulder, or on
    /// Island_2 her `IslandNatureProfile.DressResource` boulder -- hidden the
    /// way she hides the originals, `forceRenderingOff`, so her grounding
    /// component still owns the root and nothing here fights it).
    ///
    /// **Units by shape** (`Units`): Field 3, Broad 5, Split 4, Upright 6,
    /// LowRidge 6, Outcrop 8. The island's deposits between them size the
    /// camp's Stone seam once (`OutpostLedger.SizeStoneToDeposits`), and
    /// `GatherSync` spends the seam through them nearest the camp first.
    /// When one's share is gone it swaps IN PLACE to its `_Depleted`
    /// remnant -- same transform, stays in the world, no longer a target.
    ///
    /// **The shape is picked from where the node was made** (its
    /// `ResourceNode.SpawnPos`, which is the populator's seeded position or
    /// the seeded spot `StoneDeposits` chose), so a reload -- or a camp
    /// moving the rock into its ring -- gives the same rock the same shape
    /// and the same units. Frequencies follow the kit README: Field and
    /// Broad most, Split and LowRidge now and then, Upright and Outcrop rare.
    ///
    /// Material: the kit imports with none (vertex colours only), so every
    /// renderer is given ONE shared vertex-colour material: Astra's nature
    /// material where her profile dresses the island (Island_2), else the
    /// scenery material the whole island kit is drawn with
    /// (`IslandScenery.SceneryMaterial`, whose shader is kept in a player by
    /// `Resources/Shaders/Keepalive/Keep_TerrainVertexColor.mat`).
    public class StoneDeposit : MonoBehaviour
    {
        public enum Shape { Field, Broad, Split, Upright, LowRidge, Outcrop }

        static readonly string[] Names = { "Field", "Broad", "Split", "Upright", "LowRidge", "Outcrop" };
        static readonly int[] UnitsBy = { 3, 5, 4, 6, 6, 8 };
        /// Out of 100. README: "use Field and Broad most often, occasional
        /// Split/LowRidge ..., Upright/Outcrop as larger landmarks; do not
        /// scatter all six equally".
        static readonly int[] WeightBy = { 36, 30, 12, 11, 6, 5 };
        /// Half-footprint fallback (m) when the kit is missing: 2-4 m across.
        static readonly float[] RadiusBy = { 1.1f, 1.6f, 1.4f, 1.8f, 1.2f, 2.0f };

        public const string KitFolder = "StoneKit/Stone_";

        [SerializeField] Shape shape;
        public Shape Kind => shape;
        public int Units => UnitsBy[(int)shape];
        public static int UnitsOf(Shape s) => UnitsBy[(int)s];

        /// How far from the node's pivot a worker stands to swing at it: the
        /// footprint's radius plus an arm's length.
        public float StandOff { get; private set; } = 1.1f;
        public bool ShowsDepleted { get; private set; }

        GameObject full, depleted;
        Renderer[] fullR, depR = new Renderer[0];
        /// Kept for the remnant, made the first time it is needed: most rocks
        /// on most islands are never worked, so half the instances never exist.
        float yaw, size;
        Material mat;
        /// The renderers this deposit turned off (the prop's old look), to
        /// fall back to hiding them the old way if the kit is not there.
        readonly List<Renderer> hid = new List<Renderer>();

        // --- making one ---------------------------------------------------------

        /// Put a deposit on this Stone node (idempotent; returns the one it
        /// already has). Not for scenery trees or other resources.
        public static StoneDeposit Dress(ResourceNode n)
        {
            if (n == null || n.Resource != Res.Stone) return null;
            if (n.Deposit != null) return n.Deposit;
            var d = n.gameObject.AddComponent<StoneDeposit>();
            d.Build(n);
            n.Deposit = d;      // and `Deposit`'s setter shows the node's state
            return d;
        }

        /// The kit shape for a node made at `p`, from README frequencies.
        public static Shape ShapeAt(Vector3 p)
        {
            int roll = (int)(Hash(p, 0x5701) % 100u);
            for (int i = 0; i < WeightBy.Length; i++)
            {
                if (roll < WeightBy[i]) return (Shape)i;
                roll -= WeightBy[i];
            }
            return Shape.Field;
        }

        void Build(ResourceNode n)
        {
            Vector3 at = n.SpawnPos;
            shape = ShapeAt(at);
            yaw = Hash(at, 0x1a2b) % 3600u * 0.1f;
            size = 0.9f + (Hash(at, 0x77) % 1000u) * 0.00022f;    // 0.90..1.12

            // The old look goes, the way Astra's own dressing hides it:
            // `SetGathered` toggles `enabled`, which cannot undo this.
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                hid.Add(r);
            }

            mat = MaterialFor(n.Home);
            full = Make(Names[(int)shape], yaw, size, mat);
            fullR = full != null ? full.GetComponentsInChildren<Renderer>(true) : new Renderer[0];

            if (full == null)
            {
                // No kit in this build: keep the old rock rather than show
                // nothing. The units and the arithmetic still hold.
                foreach (var r in hid) if (r != null) r.forceRenderingOff = false;
                StandOff = RadiusBy[(int)shape] * size + 0.7f;
                return;
            }
            StandOff = Mathf.Max(1.1f, FootprintRadius(full) + 0.7f);
        }

        GameObject Make(string id, float yaw, float size, Material mat)
        {
            var prefab = Resources.Load<GameObject>(KitFolder + id);
            if (prefab == null) return null;
            var go = Instantiate(prefab, transform, false);
            go.name = "Deposit_" + id;
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
            // Metres whatever the prop's root was scaled to.
            Vector3 ls = transform.lossyScale;
            t.localScale = Vector3.Scale(prefab.transform.localScale,
                new Vector3(size / Safe(ls.x), size / Safe(ls.y), size / Safe(ls.z)));
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                int slots = Mathf.Max(1, r.sharedMaterials.Length);
                var arr = new Material[slots];
                for (int i = 0; i < slots; i++) arr[i] = mat;
                r.sharedMaterials = arr;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.enabled = true;
                r.forceRenderingOff = false;
            }
            return go;
        }

        static float Safe(float s) => Mathf.Abs(s) < 1e-4f ? 1f : s;

        /// Largest horizontal reach of the mesh from the node's pivot.
        float FootprintRadius(GameObject go)
        {
            float best = 0f;
            Vector3 o = transform.position;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                        (i & 2) == 0 ? b.min.y : b.max.y,
                                        (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 w = mf.transform.TransformPoint(c) - o;
                    w.y = 0f;
                    best = Mathf.Max(best, w.magnitude);
                }
            }
            // The AABB corner overstates a round rock by up to 1.41x.
            return best > 0f ? best * 0.8f : RadiusBy[(int)shape];
        }

        static Material MaterialFor(Island home)
        {
            if (home != null)
            {
                var p = Terrain.IslandNatureProfile.For(home.transform.position);
                if (p != null && p.Material != null) return p.Material;
            }
            return Terrain.IslandScenery.SceneryMaterial();
        }

        // --- full or worked out ---------------------------------------------------

        /// Full deposit or its `_Depleted` remnant, swapped in place; or
        /// nothing at all (`gone`) when a building plot's clearing has taken
        /// the rock (`ResourceNode.HeldBySite`) -- a hut does not stand on a
        /// remnant. Idempotent. Called through `ResourceNode`.
        public void Show(bool worked, bool gone)
        {
            ShowsDepleted = worked && !gone;
            if (full == null)
            {
                foreach (var r in hid) if (r != null) r.enabled = !(worked || gone);
                return;
            }
            if (worked && !gone && depleted == null)
            {
                depleted = Make(Names[(int)shape] + "_Depleted", yaw, size, mat);
                if (depleted != null) depR = depleted.GetComponentsInChildren<Renderer>(true);
            }
            foreach (var r in fullR) if (r != null) { r.enabled = true; r.forceRenderingOff = worked || gone; }
            foreach (var r in depR) if (r != null) { r.enabled = true; r.forceRenderingOff = !worked || gone; }
        }

        /// A stable 32-bit hash of a position at decimetre resolution.
        static uint Hash(Vector3 p, uint salt)
        {
            unchecked
            {
                uint h = 2166136261u ^ salt;
                h = (h ^ (uint)Mathf.RoundToInt(p.x * 10f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(p.z * 10f)) * 16777619u;
                h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
                return h;
            }
        }
    }
}
