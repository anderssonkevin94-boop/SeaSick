using UnityEngine;

namespace SeaSick.Combat
{
    /// **One raised arm as a hit target** (GDD §6 "The Kraken"). Registered
    /// in `HitTargets` only while that arm is winding up (`KrakenSwat` adds
    /// and removes it); a capsule along the outer six bones, the arm's own
    /// thickness. A ball that physically hits it wears the beast down a
    /// little (`KrakenTuning.armHitDamage` of a body hit) and does nothing
    /// else: the swat is never cancelled. Never locked (`ILockExempt`): the
    /// lock stays on the head.
    public class KrakenArmTarget : IHittable, ILockExempt
    {
        readonly Kraken kraken;
        readonly int arm;

        public KrakenArmTarget(Kraken owner, int armIndex)
        {
            kraken = owner;
            arm = armIndex;
        }

        public int Arm => arm;

        Transform Bone(int j) => kraken != null && kraken.Arms != null ? kraken.Arms.ArmBone(arm, j) : null;

        public Vector3 HitCentre
        {
            get
            {
                var a = Bone(4); var b = Bone(9);
                return a != null && b != null ? (a.position + b.position) * 0.5f : Vector3.zero;
            }
        }

        public Vector3 HitAxis
        {
            get
            {
                var a = Bone(4); var b = Bone(9);
                return a != null && b != null ? (b.position - a.position) * 0.5f : Vector3.zero;
            }
        }

        /// The arm's own thickness, no padding: the shots land where they land.
        public float HitRadius => KrakenTuning.armHitRadius;

        public bool Alive => kraken != null && kraken.Swat != null && kraken.Swat.IsWindingUp(arm)
                             && !kraken.Retreating;

        public float Health01 => kraken != null ? kraken.Health01 : 0f;
        public int HitPoints => kraken != null ? kraken.HitPoints : 1;
        public int DamageTaken => kraken != null ? kraken.DamageTaken : 0;

        public bool TakeHit(Vector3 point, float damage)
        {
            if (!Alive) return false;
            kraken.TakeDamage(point, damage * KrakenTuning.armHitDamage);
            return true;
        }
    }
}
