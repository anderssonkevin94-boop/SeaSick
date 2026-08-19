using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Combat
{
    /// The player's ship as a target.
    ///
    /// Exists so that being shot at runs through exactly the same path as
    /// shooting: one hit test, one registry, no special case for "the enemy
    /// fires at you". Damage is handed straight to HullIntegrity, which
    /// already knows how to cost speed, add wallow and terrify the crew — so
    /// incoming fire lands on the sickness clock rather than on a health bar.
    [RequireComponent(typeof(HullIntegrity))]
    public class PlayerHull : MonoBehaviour, IHittable
    {
        [SerializeField] float hitRadius = 4.2f;
        [SerializeField] float halfLength = 9.5f;   // the hull is ~21m
        [SerializeField] int hitPoints = 10;        // for readouts only

        HullIntegrity hull;

        void Awake() { hull = GetComponent<HullIntegrity>(); }
        void OnEnable() { HitTargets.Register(this); }
        void OnDisable() { HitTargets.Unregister(this); }

        public Vector3 HitCentre => transform.position + Vector3.up * 2.2f;
        public float HitRadius => hitRadius;
        public Vector3 HitAxis => transform.forward * halfLength;

        /// Never false. Failure in this game is soft — a wrecked hull is
        /// crippled and terrifying, not destroyed, and the crew turning for
        /// home is the real fail state.
        public bool Alive => true;

        public int HitPoints => hitPoints;
        public int DamageTaken => hull != null
            ? Mathf.RoundToInt((1f - hull.Integrity01) * hitPoints) : 0;
        public float Health01 => hull != null ? hull.Integrity01 : 1f;

        public bool TakeHit(Vector3 point, float damage)
        {
            if (hull == null) return false;
            hull.TakeShot(point, damage);
            return true;
        }
    }
}
