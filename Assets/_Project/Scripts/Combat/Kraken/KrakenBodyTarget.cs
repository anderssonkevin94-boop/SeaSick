using UnityEngine;

namespace SeaSick.Combat
{
    /// **The kraken's head as THE hit target** (GDD §6 "The Kraken", step
    /// 3): what `CombatLock`'s candidate and the lock button pick, what the
    /// guns auto-fire at once locked, and what the HUD chip names
    /// (`Kraken.BodyTarget`). A sphere round the mantle, kept on a child
    /// transform so the chase camera's `LockTarget` has something to follow
    /// (the camera composes the kraken shot for it, see
    /// `ChaseCamera.SeaFocusLockAlias`). Damage goes to `Kraken.TakeDamage`.
    public class KrakenBodyTarget : MonoBehaviour, IHittable
    {
        Kraken kraken;

        public static KrakenBodyTarget Create(Kraken owner)
        {
            var go = new GameObject("KrakenHead");
            go.transform.SetParent(owner.transform, false);
            var t = go.AddComponent<KrakenBodyTarget>();
            t.kraken = owner;
            return t;
        }

        void OnEnable() => HitTargets.Register(this);
        void OnDisable() => HitTargets.Unregister(this);

        /// Kept on the mantle every frame: the body bone plus the mantle's
        /// measured centre (half the 16 m dome over the root at scale 49).
        void LateUpdate()
        {
            if (kraken == null) return;
            var body = kraken.Arms != null ? kraken.Arms.Body : null;
            Vector3 p = body != null ? body.position : kraken.transform.position;
            float k = kraken.transform.lossyScale.y / 49f;
            p.y = Mathf.Max(p.y, kraken.WaterY) + 6.5f * k;
            transform.position = p;
        }

        public Vector3 HitCentre => transform.position;
        public float HitRadius => KrakenTuning.bodyHitRadius;
        public Vector3 HitAxis => Vector3.zero;

        /// Shootable once it has broken the water and until it turns to go.
        public bool Alive => kraken != null && kraken.Breached && !kraken.Retreating;

        public float Health01 => kraken != null ? kraken.Health01 : 0f;
        public int HitPoints => kraken != null ? kraken.HitPoints : 1;
        public int DamageTaken => kraken != null ? kraken.DamageTaken : 0;

        public bool TakeHit(Vector3 point, float damage)
        {
            if (!Alive) return false;
            kraken.TakeDamage(point, damage);
            return true;
        }
    }
}
