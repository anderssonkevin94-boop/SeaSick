using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// One physical unit of resource: one tree is one log. Resources stopped
    /// being an abstract number draining from an island — the crew walk to
    /// this object, work it, and carry the result back.
    public class ResourceNode : MonoBehaviour
    {
        public static readonly List<ResourceNode> All = new List<ResourceNode>();

        [SerializeField] string resource = "Timber";
        [SerializeField] int hitsToHarvest = 3;

        public string Resource => resource;
        public int HitsToHarvest => hitsToHarvest;
        public bool Harvested { get; private set; }
        /// Claimed by one crew member so three of them don't converge on the
        /// same tree and two come away empty-handed.
        public CrewClaim Claim { get; private set; }
        public Island Home { get; private set; }

        public struct CrewClaim
        {
            public Object owner;
            public bool Held => owner != null;
        }

        Vector3 baseScale;
        float shakeUntil;
        float shakeStrength;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Awake() { baseScale = transform.localScale; }

        public void Configure(string res, Island island, int hits)
        {
            resource = res;
            Home = island;
            hitsToHarvest = Mathf.Max(1, hits);
        }

        // --- trees that live in the baked scenery mesh --------------------

        Terrain.SceneryWood wood;
        int treeIndex = -1;

        /// A node standing on a real tree in the island's scenery mesh.
        ///
        /// It has no renderer of its own — the tree is already drawn, as part
        /// of the one welded mesh — so this is a transform, a claim and a hit
        /// count. Harvesting it tells the mesh to drop that tree.
        public void ConfigureScenery(Island island, Terrain.SceneryWood w, int index)
        {
            resource = "Timber";
            Home = island;
            hitsToHarvest = 3;
            wood = w;
            treeIndex = index;
        }

        public bool TryClaim(Object owner)
        {
            if (Harvested || Claim.Held) return false;
            Claim = new CrewClaim { owner = owner };
            return true;
        }

        public void Release(Object owner)
        {
            if (Claim.owner == owner) Claim = default;
        }

        /// A blow lands: shake so the hit reads without an animation rig.
        public void Strike()
        {
            shakeUntil = Time.time + 0.35f;
            shakeStrength = 1f;
        }

        public void Harvest()
        {
            Harvested = true;
            Claim = default;
            // A scenery tree has to come down in the MESH -- there is nothing
            // to deactivate, because this object was never what was drawn.
            if (wood != null && treeIndex >= 0)
            {
                wood.NodeHarvested(treeIndex);
                Destroy(gameObject);
                return;
            }
            gameObject.SetActive(false);
        }

        void Update()
        {
            // A scenery node draws nothing of its own, so there is no shake
            // to animate -- the strike reads on the crew, not on the tree.
            if (wood != null) return;
            if (Time.time > shakeUntil)
            {
                if (shakeStrength > 0f)
                {
                    shakeStrength = 0f;
                    transform.localScale = baseScale;
                    transform.localRotation = Quaternion.identity;
                }
                return;
            }

            float wob = Mathf.Sin(Time.time * 42f) * 5f * shakeStrength;
            transform.localRotation = Quaternion.Euler(wob, 0f, wob * 0.6f);
            transform.localScale = baseScale * (1f + Mathf.Sin(Time.time * 30f) * 0.03f);
        }

        /// Nearest unclaimed node on a given island.
        public static ResourceNode FindFree(Island island, Vector3 near, Object claimant)
        {
            ResourceNode best = null;
            float bestSq = float.MaxValue;
            foreach (var n in All)
            {
                if (n == null || n.Harvested || n.Claim.Held) continue;
                if (n.Home != island) continue;
                float sq = (n.transform.position - near).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = n; }
            }
            if (best != null && best.TryClaim(claimant)) return best;
            return null;
        }

        public static int CountFree(Island island)
        {
            int n = 0;
            foreach (var node in All)
                if (node != null && !node.Harvested && node.Home == island) n++;
            return n;
        }
    }
}
