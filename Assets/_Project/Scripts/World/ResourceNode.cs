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
        /// How many ledger units one prop stands for. A rock disappears every
        /// four stone -- see `GatherSync`, which hides `floor(taken / this)`
        /// props nearest the camp. Timber does not use it (trees are felled
        /// one log each by `Outpost.SyncFelling`).
        [SerializeField] int unitsPerProp = DefaultUnitsPerProp;

        public const int DefaultUnitsPerProp = 4;

        public string Resource => resource;
        public int HitsToHarvest => hitsToHarvest;
        public int UnitsPerProp => Mathf.Max(1, unitsPerProp);

        /// Gone, as far as anyone looking for something to work is concerned:
        /// either a crew member took it (`Harvest`) or the ledger's arithmetic
        /// has already used it up (`SetGathered`).
        ///
        /// **Or it stands on a building plot (`HeldBySite`, 2026-09-23)**:
        /// that rock is the plot's, taken down by the builders' clearing and
        /// not by anybody gathering, so every finder that skips a spent node
        /// skips it too.
        public bool Harvested => harvested || Gathered || HeldBySite;
        bool harvested;

        /// **Owned by a building plot's CLEAR phase.** Set and cleared only by
        /// `Outpost`'s clearing registry, which also decides whether it is
        /// shown (`SetGathered`). `GatherSync` leaves a held rock out of the
        /// seam's order, so the seam's prefix and the plot's never fight over
        /// the same prop. Not saved: re-derived from the rows on every load.
        public bool HeldBySite
        {
            get => heldBySite;
            set { heldBySite = value; ShowDeposit(); }
        }
        bool heldBySite;

        // --- stone deposits (2026-09-24) ---------------------------------------

        /// Astra's kit rock this Stone node wears (`StoneDeposit.Dress`), or
        /// null. With one, being gathered SWAPS the rock to its `_Depleted`
        /// remnant in place instead of hiding it, and a plot's clearing hides
        /// it outright.
        public StoneDeposit Deposit
        {
            get => deposit;
            set { deposit = value; ShowDeposit(); }
        }
        StoneDeposit deposit;

        /// Where this node stood when it was made -- before any camp moved
        /// it into its ring -- so what is seeded off it (a deposit's shape
        /// and units) is the same on every load.
        public Vector3 SpawnPos { get; private set; }

        /// How far from the pivot a worker stands to swing at it: outside the
        /// deposit's footprint, or a pace off a plain prop.
        public float StandOff => deposit != null ? deposit.StandOff : 1.1f;

        void ShowDeposit()
        {
            if (deposit == null) return;
            bool gone = heldBySite && Gathered;
            deposit.Show(Gathered || harvested, gone);
        }

        /// **Hidden by `GatherSync` to match the ledger**, renderers off, the
        /// object still active. It stays in `All` on purpose: the ledger's
        /// `taken` can come back down (spice regrows), and then the same prop
        /// has to come back -- a deactivated object would have left the list
        /// and the sync could never find it again.
        public bool Gathered { get; private set; }
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

        void Awake() { baseScale = transform.localScale; SpawnPos = transform.position; }

        /// Every Stone node wears a kit deposit, on every island, whoever
        /// made it (`Configure` has set the kind by now). `StoneDeposits`
        /// dresses a camp's own earlier, when it sizes the seam.
        void Start()
        {
            if (wood == null && resource == Res.Stone && deposit == null) StoneDeposit.Dress(this);
        }

        public void Configure(string res, Island island, int hits)
        {
            resource = res;
            Home = island;
            hitsToHarvest = Mathf.Max(1, hits);
        }

        // --- trees that live in the baked scenery mesh --------------------

        Terrain.SceneryWood wood;
        int treeIndex = -1;

        /// The scenery tree this node stands on, or -1 (not a scenery tree).
        public int TreeIndex => wood != null ? treeIndex : -1;

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

        // --- berry bushes in the baked scenery mesh (2026-10-03) ----------

        Terrain.SceneryCrops crops;
        int bedIndex = -1;

        /// The scenery berry bed this node stands on, or -1.
        public int BedIndex => crops != null ? bedIndex : -1;

        /// **A landing party's node on a wild berry bush** (`SceneryCrops`,
        /// `BedKind.Berry`). Like a scenery tree it draws nothing of its own:
        /// a transform, a claim and a hit count. `Home` stays null (the
        /// party's own, like its rock nodes), so no camp system sees it.
        /// Harvesting it strips the bush in the mesh (`SceneryCrops.Harvest`;
        /// the party's `Cut` holds it for the ledger first).
        public void ConfigureBed(Terrain.SceneryCrops c, int index)
        {
            resource = Res.Food;
            Home = null;
            hitsToHarvest = 2;
            crops = c;
            bedIndex = index;
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

        /// Show or hide this prop to agree with the outpost ledger. Idempotent.
        /// Toggles the renderers and colliders rather than the GameObject so
        /// the node keeps its place in `All` (see `Gathered`). A gathered prop
        /// is not a valid target: `Harvested` reads true, so `FindFree`,
        /// `HandTargets.NearestNode` and `CampWorker` all skip it.
        public void SetGathered(bool gathered)
        {
            if (Gathered == gathered) return;
            Gathered = gathered;
            if (gathered) Claim = default;
            if (colliders == null) colliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in colliders) if (c != null) c.enabled = !gathered;
            // A deposit swaps to its remnant rather than vanishing.
            if (deposit != null) { ShowDeposit(); return; }
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) if (r != null) r.enabled = !gathered;
        }

        Renderer[] renderers;
        Collider[] colliders;

        public void Harvest()
        {
            harvested = true;
            Claim = default;
            // A quarried-out deposit stays where it was, as its remnant.
            if (deposit != null) { ShowDeposit(); return; }
            // A scenery tree has to come down in the MESH -- there is nothing
            // to deactivate, because this object was never what was drawn.
            if (wood != null && treeIndex >= 0)
            {
                wood.NodeHarvested(treeIndex);
                Destroy(gameObject);
                return;
            }
            // A picked bush is stripped in the mesh (the party has already
            // held it for the ledger, `GatherParty.Cut`).
            if (crops != null && bedIndex >= 0)
            {
                crops.Harvest(bedIndex);
                Destroy(gameObject);
                return;
            }
            gameObject.SetActive(false);
        }

        /// **Taken off the ground by name** (`OutpostLedger.GroundTaken`,
        /// the gather party's takes): no longer a target, and drawn gone --
        /// a kit deposit as its remnant, a scenery rock hidden, a plain prop's
        /// renderers off. Unlike `Harvest` it never destroys or deactivates
        /// the object, so it is safe from inside a pass over `All`.
        public void MarkTaken()
        {
            if (harvested) return;
            harvested = true;
            Claim = default;
            if (deposit != null) { ShowDeposit(); return; }
            if (wood != null) return;     // the tree is felled in the mesh by `GroundTaken`
            if (crops != null) return;    // the bush is stripped in the mesh by `GroundTaken`
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) if (r != null) r.enabled = false;
            if (colliders == null) colliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in colliders) if (c != null) c.enabled = false;
        }

        void Update()
        {
            // A scenery node draws nothing of its own, so there is no shake
            // to animate -- the strike reads on the crew, not on the tree.
            // Nor does a baked scenery rock (`StoneDeposit.IsScenery`).
            if (wood != null || crops != null || (deposit != null && deposit.IsScenery)) return;
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
