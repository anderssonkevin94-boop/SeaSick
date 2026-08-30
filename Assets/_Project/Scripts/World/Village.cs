using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// What has been built at home, where the next thing goes, and how much
    /// the place can keep.
    ///
    /// `Settlement` measures the GROUND -- the largest contiguous piece of
    /// buildable land on the island. This owns what stands on it. They are
    /// separate because the ground is measured once at world build and never
    /// changes, while the village grows across a session.
    ///
    /// **Nothing here flattens anything.** `TerrainHeight.Height` is a pure
    /// function of world position running in Burst jobs on streamed chunks,
    /// so there is no per-building data it could consult and no pad to cut.
    /// A site is CHOSEN on ground that is already flat enough, its corners
    /// are measured, and the building sits at its highest corner with a
    /// footing deep enough to bridge down to its lowest.
    public class Village : MonoBehaviour
    {
        public static Village Home { get; private set; }

        [Tooltip("What the open beach keeps before the weather has it. The reason to build the first storehouse.")]
        [SerializeField] int beachCapacity = 30;

        [Tooltip("Metres between buildings -- room to walk round one, which is what a village looks like from above.")]
        [SerializeField] float spacing = 6f;

        readonly List<Building> built = new List<Building>();
        readonly List<Vector4> reserved = new List<Vector4>();   // xyz = point, w = radius

        Settlement site;
        System.Func<float, float, float> height;
        float minHeight;

        /// The cleared ground the village stands in. Handed to the scenery
        /// bake as a keep-out BEFORE the trees go in, because the scenery is
        /// several hundred trees welded into one mesh and there is no taking
        /// one out afterwards.
        public Vector3 ClearingCentre { get; private set; }
        public float ClearingRadius { get; private set; }

        /// Everything home can keep. Land more than this on one voyage and
        /// the surplus stays on the sand and is not there when you get back.
        public int StoreCapacity
        {
            get
            {
                int n = beachCapacity;
                foreach (var b in built) if (b != null) n += b.StoreCapacity;
                return n;
            }
        }

        public IReadOnlyList<Building> Built => built;

        public int CountOf(string planId)
        {
            int n = 0;
            foreach (var b in built) if (b != null && b.Id == planId) n++;
            return n;
        }

        void OnEnable() { if (Home == null) Home = this; }
        void OnDisable() { if (Home == this) Home = null; }

        public void Configure(Settlement settlement, System.Func<float, float, float> terrainHeight,
            float minGroundHeight)
        {
            site = settlement;
            height = terrainHeight;
            minHeight = minGroundHeight;

            // Centre the clearing on a circle that FITS in the buildable
            // patch, not on its centroid: a lobed patch has a centroid that
            // need not be on it at all, and the whole point of this disc is
            // that everything inside it is ground you can build on.
            //
            // And not on the island's BIGGEST such circle either. That one is
            // 130 m from the head of the pier here, well outside the frame
            // the docked camera holds, so everything raised in it would be
            // invisible at the one moment the player is standing still
            // looking at home. `VillageAt` is the best clearing inside that
            // frame -- see SettlementSite.Find.
            ClearingCentre = settlement.VillageAt;

            // The clearing is NOT the inscribed circle. That circle
            // guarantees every point in it is buildable, which sounds right
            // and costs the village most of its ground: home's best in-frame
            // one is 12.6 m, which holds exactly one storehouse. The
            // guarantee was never needed -- `Corners` tests each building
            // against the actual rectangle it stands on, so a candidate on
            // bad ground is refused whatever the clearing says.
            //
            // So the clearing is sized by what the SHOT holds instead: the
            // measured half-width of the docked view, less the room a
            // building needs to stand at its edge and still be in it.
            float widest = 0f;
            foreach (var plan in BuildPlans.All)
                widest = Mathf.Max(widest,
                    0.5f * Mathf.Sqrt(plan.footprint.x * plan.footprint.x
                                    + plan.footprint.y * plan.footprint.y));
            ClearingRadius = Mathf.Clamp(Dock.ViewHalfWidth - widest - 2f, 14f, 30f);
        }

        /// Keep something out of the village's way -- the beacon, the head of
        /// the pier. Reserved before anything is built, so the first
        /// storehouse is not raised on top of them.
        public void Reserve(Vector3 point, float radius)
        {
            reserved.Add(new Vector4(point.x, point.y, point.z, radius));
        }

        /// True where the scenery must not put a tree. Flat distance only --
        /// the clearing is a disc on the map, and the trees it excludes stand
        /// on whatever height the ground has there.
        public bool KeepOut(float x, float z)
        {
            float dx = x - ClearingCentre.x, dz = z - ClearingCentre.z;
            return dx * dx + dz * dz < ClearingRadius * ClearingRadius;
        }

        /// Put the plan up. Null if there is nowhere in the clearing left to
        /// stand it -- the caller pays only if this returns something.
        public Building Raise(BuildPlan plan)
        {
            if (site == null || height == null) return null;

            float len = plan.footprint.x, wid = plan.footprint.y;
            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            float room = ClearingRadius - halfDiag - 1.5f;
            if (room <= 0f) return null;

            // Golden-angle spiral out from the middle: the candidates come in
            // roughly increasing distance from the centre, so the village
            // fills from the inside out and reads as one place rather than a
            // ring of sheds.
            const int Tries = 220;
            const float Golden = 2.39996323f;
            for (int i = 0; i < Tries; i++)
            {
                float t = (i + 0.5f) / Tries;
                float r = room * Mathf.Sqrt(t);
                float a = i * Golden;
                Vector3 p = ClearingCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                if (!Clear(p, halfDiag)) continue;

                Vector3 toCentre = ClearingCentre - p;
                toCentre.y = 0f;
                // Door toward the middle of the clearing. A building whose
                // back is to the village is the tell that nobody chose where
                // it went. The plan's ridge runs along local X and the door
                // is in a gable end, so local -X is what has to face in.
                Quaternion facing = toCentre.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(toCentre.normalized, Vector3.up) * Quaternion.Euler(0f, 90f, 0f)
                    : Quaternion.identity;

                if (!Corners(p, facing, len, wid, out float lo, out float hi)) continue;

                p.y = hi;
                var go = BuildingFactory.Raise(plan, transform, p, facing, hi - lo);
                var b = go.GetComponent<Building>();
                built.Add(b);
                reserved.Add(new Vector4(p.x, p.y, p.z, halfDiag + spacing * 0.5f));
                return b;
            }
            return null;
        }

        /// Nothing already claimed within reach of this footprint.
        bool Clear(Vector3 p, float halfDiag)
        {
            foreach (var r in reserved)
            {
                float dx = p.x - r.x, dz = p.z - r.z;
                float need = halfDiag + r.w;
                if (dx * dx + dz * dz < need * need) return false;
            }
            return true;
        }

        /// The four corners of the footprint, measured off the height field.
        /// Rejects ground that is too steep to stand a building on or low
        /// enough to be beach -- the same two tests SettlementSite used to
        /// find the patch, applied to this actual rectangle rather than to
        /// the patch as a whole.
        bool Corners(Vector3 p, Quaternion facing, float len, float wid,
            out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 c = p + facing * new Vector3(sx * len * 0.5f, 0f, sz * wid * 0.5f);
                    float h = height(c.x, c.z);
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
            if (lo < minHeight) return false;
            float span = Mathf.Sqrt(len * len + wid * wid);
            return (hi - lo) / span <= Terrain.SettlementSite.BuildableSlope;
        }
    }
}
