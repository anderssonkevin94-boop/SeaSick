using UnityEngine;

namespace SeaSick.Combat
{
    /// **One raised arm as a hit target, the skill shot** (GDD §6 "The
    /// Kraken", step 3). Registered in `HitTargets` only while that arm is
    /// winding up (`KrakenSwat` adds and removes it); a capsule along the
    /// outer six bones. A hit cancels the swat (`KrakenSwat.TryInterrupt`:
    /// the arm flinches back, the ring fades) and wears the beast down a
    /// little (`KrakenTuning.armHitDamage` of a body hit). Never locked
    /// (`ILockExempt`): the lock stays on the head and the guns hit the arm
    /// because it is in the line of fire, or because the player turned to
    /// put it there.
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

        /// Thicker than the arm itself (~1.5 m at scale 49): a ball that
        /// grazes the raised arm should count, this is the one shot the
        /// player is rewarded for taking.
        public float HitRadius => KrakenTuning.armHitRadius;

        public bool Alive => kraken != null && kraken.Swat != null && kraken.Swat.IsWindingUp(arm)
                             && !kraken.Retreating;

        public float Health01 => kraken != null ? kraken.Health01 : 0f;
        public int HitPoints => kraken != null ? kraken.HitPoints : 1;
        public int DamageTaken => kraken != null ? kraken.DamageTaken : 0;

        public bool TakeHit(Vector3 point, float damage)
        {
            if (!Alive) return false;
            kraken.Swat.TryInterrupt(arm);
            kraken.TakeDamage(point, damage * KrakenTuning.armHitDamage);
            return true;
        }
    }
}
