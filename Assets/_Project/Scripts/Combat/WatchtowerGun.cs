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
        }

        /// True only while someone is actually working the tower. A gun with
        /// nobody in it is a target, not a threat — same rule the player's
        /// own battery follows for an unmanned cannon.
        bool Manned => Camp != null && Camp.Ledger != null
            && Camp.Ledger.HandsOn(World.OutpostOrder.Work, World.OutpostLedger.WatchtowerId) > 0;

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
            pivot.transform.position = transform.position + Vector3.up * 7.2f;
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
