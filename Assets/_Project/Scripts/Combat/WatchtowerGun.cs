using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Combat
{
    /// The gun standing in a raised watchtower. A target as much as a
    /// weapon: raiders shoot back at it (see `EnemyShip`'s tower-targeting),
    /// so a camp that builds one is trading a hut's worth of wood for a shot
    /// that can also be knocked down.
    ///
    /// Built the same way `EnemyShip.MakeGun` builds a raider's guns — one
    /// `Cannon` component, the same primitives — because the gun itself has
    /// no opinion about who is standing behind it.
    [RequireComponent(typeof(World.Building))]
    public class WatchtowerGun : MonoBehaviour, IHittable, IFriendly
    {
        public static readonly List<WatchtowerGun> All = new List<WatchtowerGun>();

        /// The kit's own walking-surface height, local to the tower's root
        /// (`watchtower-astra-lvl1-v2`'s README: "walking surface remains
        /// 4.61 m high"; unchanged in the chunky V5 kit). Not `plan.ridge` (5.37, the OVERALL height to the parapet tips) --
        /// the gun stands on the deck, not on the rail post above it.
        const float DeckHeight = 4.61f;

        [SerializeField] int hitPoints = 6;
        [SerializeField] float reload = 4f;
        [SerializeField] float range = 90f;
        [SerializeField] float muzzleSpeed = 38f;
        [SerializeField] float spreadDeg = 3f;
        [SerializeField] float traverseSpeed = 90f;   // deg/sec

        public World.Outpost Camp { get; private set; }
        public World.Building Building { get; private set; }

        Ship.Cannon gun;
        Transform gunPivot;
        int damage;
        float reloadLeft;
        float yaw;

        // ---------------------------------------------------------------- IHittable

        public Vector3 HitCentre => transform.position + Vector3.up * 5f;
        public float HitRadius => 2f;
        /// Along the tower, not across it — a capsule standing up rather than
        /// lying down, same idea as a ship's own HitAxis but vertical.
        public Vector3 HitAxis => Vector3.up * 3f;
        public bool Alive => damage < hitPoints;
        public float Health01 => Mathf.Clamp01(1f - (float)damage / Mathf.Max(1, hitPoints));
        public int HitPoints => hitPoints;
        public int DamageTaken => damage;

        public bool TakeHit(Vector3 point, float dmg)
        {
            if (!Alive) return false;
            damage += Mathf.Max(1, Mathf.RoundToInt(dmg));
            if (!Alive)
            {
                HitTargets.Unregister(this);
                All.Remove(this);
                // Demolish destroys the GameObject, so nothing below this call
                // may touch `this` again.
                Camp?.Demolish(Building);
            }
            return true;
        }

        // ---------------------------------------------------------------- lifecycle

        void Awake()
        {
            Building = GetComponent<World.Building>();
            Camp = GetComponentInParent<World.Outpost>();
            if (Camp == null)
                Camp = World.Outpost.Of(GetComponentInParent<World.Island>());
        }

        void OnEnable()
        {
            HitTargets.Register(this);
            All.Add(this);
        }

        void OnDisable()
        {
            HitTargets.Unregister(this);
            All.Remove(this);
        }

        void Start()
        {
            BuildGun();
            // A tower raised on a wall node is part of the wall: the post
            // there comes down and the node blocks raiders (2026-09-27,
            // `Outpost.WallTowers.cs`). Start, not OnEnable: by now the
            // building stands where it will stay. A ghost's gun is destroyed
            // before it ever starts, so it never touches the wall.
            started = true;
            if (Camp != null) Camp.TouchWallTowers();
        }

        bool started;

        /// Knocked down (`Outpost.Demolish`, which has already taken it out
        /// of `Built`) or unloaded: the post goes back up on its node and the
        /// node's cell reopens to whatever the runs say.
        void OnDestroy()
        {
            if (started && Camp != null) Camp.TouchWallTowers();
        }

        /// True only while someone is actually working the tower. A gun with
        /// nobody in it is a target, not a threat — same rule the player's
        /// own battery follows for an unmanned cannon.
        ///
        /// **Per tower since 2026-09-27** (several towers on a wall): Work
        /// hands are dealt round the towers (`OutpostLedger.OrdinalOfHand`),
        /// so THIS tower -- the nth -- is manned when more than n hands are
        /// on watch.
        bool Manned => Camp != null && Camp.Ledger != null
            && Camp.Ledger.HandsOn(World.OutpostOrder.Work, World.OutpostLedger.WatchtowerId)
               > Mathf.Max(0, Camp.OrdinalOf(Building));

        void Update()
        {
            if (!Alive || gun == null) return;

            EnemyShip target = null;
            float bestSq = range * range;
            foreach (var r in EnemyShip.All)
            {
                if (r == null || !r.Alive) continue;
                Vector3 d = r.HitCentre - transform.position;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; target = r; }
            }

            if (target != null)
            {
                Vector3 to = target.HitCentre - gunPivot.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    float wantedYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    yaw = Mathf.MoveTowardsAngle(yaw, wantedYaw, traverseSpeed * Time.deltaTime);
                    gunPivot.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }

            if (reloadLeft > 0f) reloadLeft -= Time.deltaTime;
            if (!Manned || target == null || reloadLeft > 0f) return;

            reloadLeft = reload;
            Vector3 aim = target.HitCentre + Vector3.up * 1.6f;
            Ship.CannonBall.FireAt(gun.MuzzlePoint, aim, muzzleSpeed, spreadDeg, this);
            gun.RecoilOnly();
        }

        void BuildGun()
        {
            var wood = Mat(new Color(0.34f, 0.23f, 0.14f), 0.12f);
            var iron = Mat(new Color(0.30f, 0.31f, 0.34f), 0.55f);

            var pivot = new GameObject("GunPivot");
            pivot.transform.SetParent(transform, false);
            // **On the platform, not floating above it (2026-09-27).** The
            // kit's own deck sits at local y 4.61 regardless of any slope
            // sink applied to the root (`BuildingFactory`'s per-corner
            // sink moves the whole transform, so the model's own local
            // measurements never change) -- see
            // `art-staging/watchtower-astra-lvl1-v2/README.md`. `7.2f` was
            // the old 7.5 m extruded tower's height; nobody moved the gun
            // down when the kit shrank it to 4.65 m, so it hovered well
            // clear of the platform (or, on a steep wall node, well BELOW
            // it once the root sank into the slope).
            pivot.transform.position = transform.position + Vector3.up * (DeckHeight + 0.15f);
            gunPivot = pivot.transform;

            var go = new GameObject("TowerGun");
            go.transform.SetParent(gunPivot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * 1.1f;

            gun = go.AddComponent<Ship.Cannon>();
            gun.Build(wood, iron);
        }

        static Material Mat(Color c, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }
    }
}
