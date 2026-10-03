using UnityEngine;
using SeaSick.Ship;
using SeaSick.Ship.Harpoon;
using SeaSick.Ship.Overboard;
using SeaSick.World.Life;

namespace SeaSick.Voyage
{
    /// **A `SalvageSpawner` wreckage cluster, as a thing the bow harpoon can
    /// hook.** The spawner still owns the cluster: it floats it, respawns it
    /// and pays the sail-over. This component only lets the harpoon reach it
    /// the way it reaches `FlotsamCrate` -- `IHarpoonable` for the hook,
    /// `IOverboardTarget` for the haul -- and `OnHauled` calls the spawner's
    /// own `Collect`, the very method the sail-over calls, so the timber and
    /// the banner are one code path. It is NOT listed by `RescueHud`
    /// (that only walks `FloatingCargo`, `FlotsamCrate` and friends), so the
    /// ring and tap-to-steer behave as before.
    ///
    /// `BeingHauled` is what keeps the two pickups apart: the spawner skips
    /// the sail-over and the distance respawn while the line holds it, and
    /// the harpoon clears it before it delivers. The cluster is never
    /// `Resolved`; collecting it recycles it into the sea elsewhere, same as
    /// a sail-over.
    public class WreckSalvage : MonoBehaviour, IOverboardTarget, IHarpoonable
    {
        [SerializeField] SalvageSpawner owner;
        [SerializeField] ShipMotor motor;
        [SerializeField] int units = 2;

        public void Init(SalvageSpawner spawner, ShipMotor hull, int timberUnits)
        {
            owner = spawner;
            motor = hull;
            units = timberUnits;
        }

        void OnEnable() => HarpoonRegistry.Add(this);
        void OnDisable() => HarpoonRegistry.Remove(this);

        // --- IOverboardTarget -------------------------------------------
        Transform IOverboardTarget.Transform => transform;
        public Vector3 WorldPosition => transform.position;
        public float TimeLeft01 => 1f;
        string IOverboardTarget.Label => "wreckage";
        public bool Resolved => false;
        public Transform Hull => motor != null ? motor.transform : null;
        public bool BeingHauled { get; set; }
        public Vector3 HaulAnchor { get; set; }
        int IOverboardTarget.RescuePriority => 2;
        bool IOverboardTarget.Boardable => true;

        public Vector3 NearestHullSide()
        {
            if (motor == null) return transform.position;
            Transform ship = motor.transform;
            Vector3 local = ship.InverseTransformPoint(transform.position);
            float halfLen = Mathf.Max(1f, motor.HullLength * 0.5f);
            float halfBeam = OverboardTuning.HullHalfBeamMetres;
            float side = Mathf.Sign(local.x != 0f ? local.x : 1f);
            float z = Mathf.Clamp(local.z, -halfLen, halfLen);
            return ship.TransformPoint(new Vector3(halfBeam * side, 0f, z));
        }

        void IOverboardTarget.OnHauled(string rescuerName)
        {
            BeingHauled = false;
            if (owner != null) owner.Collect(transform);
        }

        // --- IHarpoonable ------------------------------------------------
        Transform IHarpoonable.Transform => transform;
        public Vector3 HookPoint => transform.position + Vector3.up * 0.3f;
        string IHarpoonable.HarpoonLabel => "wreckage";
        public bool CanBeHarpooned => !BeingHauled && owner != null;
        public float HarpoonMass => 1.2f;
        public int HarpoonHoldUnits => units;
    }
}
