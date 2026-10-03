using System.Collections.Generic;
using UnityEngine;
using SeaSick.Voyage;
using SeaSick.World;
using SeaSick.World.Life;
using SeaSick.Ship.Overboard;
using SeaSick.Ship.Harpoon;

namespace SeaSick.Ship.SeaLife
{
    /// **Wreckage on the water** — build item 1 of "things to find at sea"
    /// (2026-09-28). A small cluster (a crate/barrel drawn with the same
    /// `CargoVisual` the deck load and camp piles use, plus a couple of
    /// plank primitives) holding a few units of a common resource. Rides the
    /// real wave and drifts, same shape as `FloatingCargo`, and plugs into
    /// the exact same `RescueHud` sail-over pickup through `IOverboardTarget`
    /// — no new UI, no new haul code.
    public class FlotsamCrate : MonoBehaviour, IOverboardTarget, IHarpoonable
    {
        static readonly List<FlotsamCrate> all = new List<FlotsamCrate>();
        public static IReadOnlyList<FlotsamCrate> All => all;

        public string Resource { get; private set; }
        public int Units { get; private set; }
        float timeLeft;
        float timeTotal = 1f;
        public bool Resolved { get; private set; }
        public Vector3 WorldPosition => transform.position;

        Transform ship;
        Vector3 driftVel;

        static readonly string[] CommonResources = { Res.Timber, Res.Stone, Res.Food, Res.Boards, Res.Tools };

        // --- IOverboardTarget -------------------------------------------
        Transform IOverboardTarget.Transform => transform;
        public float TimeLeft01 => timeTotal > 0f ? Mathf.Clamp01(timeLeft / timeTotal) : 0f;
        string IOverboardTarget.Label => Units + " " + Resource.ToLowerInvariant();
        int IOverboardTarget.RescuePriority => 2; // people, then lost cargo, then wreckage
        bool IOverboardTarget.Boardable => true;
        public Transform Hull => ship;
        public bool BeingHauled { get; set; }
        public Vector3 HaulAnchor { get; set; }
        void IOverboardTarget.OnHauled(string rescuerName) => Recover(rescuerName);

        // --- IHarpoonable (hooked and reeled, then the same `OnHauled`) -------
        Transform IHarpoonable.Transform => transform;
        public Vector3 HookPoint => transform.position + Vector3.up * 0.3f;
        string IHarpoonable.HarpoonLabel => "flotsam";
        public bool CanBeHarpooned => !Resolved && !BeingHauled;
        public float HarpoonMass => Mathf.Clamp(0.5f + 0.25f * Units, 0.7f, 2f);
        public int HarpoonHoldUnits => Units;

        public static FlotsamCrate Spawn(Transform hull, Vector3 worldPos)
        {
            if (hull == null) return null;
            string resource = CommonResources[Random.Range(0, CommonResources.Length)];
            int units = Random.Range(SeaLifeTuning.FlotsamUnitsMin, SeaLifeTuning.FlotsamUnitsMax + 1);

            var go = new GameObject("Flotsam_" + resource);
            go.transform.position = worldPos;
            var c = go.AddComponent<FlotsamCrate>();
            c.Resource = resource;
            c.Units = units;
            c.ship = hull;
            c.timeTotal = Mathf.Max(20f, SeaLifeTuning.FlotsamLifeSeconds);
            c.timeLeft = c.timeTotal;

            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            c.driftVel = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * SeaLifeTuning.FlotsamDriftSpeed;

            c.BuildVisual();
            all.Add(c);
            HarpoonRegistry.Add(c);
            return c;
        }

        void BuildVisual()
        {
            var visualRoot = new GameObject("Wreckage");
            visualRoot.transform.SetParent(transform, false);

            int shown = Mathf.Clamp(Units, 1, 3);
            for (int i = 0; i < shown; i++)
            {
                var unit = CargoVisual.Build(Resource, visualRoot.transform);
                unit.transform.localPosition = CargoVisual.StackSlot(i, 2, 0.55f, 0.35f);
            }

            // A couple of broken boards scattered around the crate — the
            // "wreckage" read, not just a tidy stack of goods. Astra's board
            // pieces (sea discovery kit v1, Kevin approved 2026-09-30) replace
            // the old cube planks: short (1.25 m) or long (2.15 m), authored
            // surface-centred, so they sit a hair under the crate root's +.1 m
            // bob offset to lie in the water. The cargo units above are the
            // resource's own and are not touched. Same random draws as before
            // (count, angle, radius, yaw) plus one for which board.
            int planks = Random.Range(1, 3);
            for (int i = 0; i < planks; i++)
            {
                float ang = Random.Range(0f, 360f);
                float r = Random.Range(0.5f, 1.1f);
                float yaw = Random.Range(0f, 360f);
                bool longBoard = Random.value < 0.5f;
                var pos = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad) * r, -BoardDrop, Mathf.Sin(ang * Mathf.Deg2Rad) * r);

                var board = SeaKit.Spawn(longBoard ? SeaKit.BrokenBoardLong : SeaKit.BrokenBoardShort,
                    visualRoot.transform, pos);
                if (board != null)
                {
                    board.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    continue;
                }

                var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = "Plank";
                var col = plank.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                plank.transform.SetParent(visualRoot.transform, false);
                plank.transform.localScale = new Vector3(1.6f, 0.06f, 0.22f);
                plank.transform.localPosition = new Vector3(pos.x, 0f, pos.z);
                plank.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                var mr = plank.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = PlankMat();
            }
        }

        /// How far the surface-centred board art is lowered from the crate
        /// root (which floats .1 m above the wave), putting the board's
        /// centre .04 m above the water rather than hovering.
        const float BoardDrop = 0.06f;

        static Material plankMat;
        static Material PlankMat()
        {
            if (plankMat == null)
            {
                plankMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                plankMat.SetColor("_BaseColor", new Color(0.42f, 0.3f, 0.2f));
            }
            return plankMat;
        }

        void OnDestroy() { all.Remove(this); HarpoonRegistry.Remove(this); }

        void Update()
        {
            if (Resolved) return;
            float dt = Time.deltaTime;

            Vector3 pos = BeingHauled
                ? Vector3.MoveTowards(transform.position, HaulAnchor, OverboardTuning.HaulPullSpeed * dt)
                : transform.position + driftVel * dt;

            float bob = 0.1f;
            Vector3 normal = Vector3.up;
            if (Ocean.OceanSampler.Ready)
            {
                var sample = Ocean.OceanSampler.SampleImmediate(pos);
                pos.y = sample.height + bob;
                normal = sample.normal;
            }
            transform.position = pos;
            Quaternion tiltTarget = Quaternion.FromToRotation(Vector3.up, normal);
            transform.rotation = Quaternion.Slerp(transform.rotation, tiltTarget, dt * 1.5f);

            // Quietly gone if she drifts too far astern of the ship that
            // could have picked her up, or her clock runs out — either way,
            // no banner: this one was never seen, so nothing to mourn.
            if (ship != null)
            {
                Vector3 toHer = pos - ship.position;
                toHer.y = 0f;
                Vector3 fwd = ship.forward; fwd.y = 0f;
                if (Vector3.Dot(toHer.normalized, fwd.normalized) < -0.2f
                    && toHer.magnitude > SeaLifeTuning.FlotsamCullAsternMetres)
                {
                    Cull();
                    return;
                }
            }

            if (!BeingHauled) timeLeft -= dt;
            if (timeLeft <= 0f) Cull();
        }

        void Cull()
        {
            if (Resolved) return;
            Resolved = true;
            Destroy(gameObject);
        }

        void Recover(string rescuerName)
        {
            if (Resolved) return;
            Resolved = true;

            var voyage = ship != null ? ship.GetComponentInParent<VoyageManager>() : null;
            if (voyage == null) voyage = Object.FindFirstObjectByType<VoyageManager>();
            if (voyage != null)
            {
                voyage.ReturnCargo(Units, Resource);
                var shipHold = ship != null ? ship.GetComponent<ShipHold>() : null;
                if (shipHold != null)
                    for (int i = 0; i < Units; i++) shipHold.AddVisual(Resource);
            }

            Banner.Show("Picked up " + Units + " " + Resource.ToLowerInvariant() + " from the wreckage");
            Destroy(gameObject);
        }

        public Vector3 NearestHullSide()
        {
            if (ship == null) return transform.position;
            Vector3 local = ship.InverseTransformPoint(transform.position);
            var motor = ship.GetComponent<ShipMotor>();
            float halfLen = motor != null ? Mathf.Max(1f, motor.HullLength * 0.5f) : 12f;
            float halfBeam = OverboardTuning.HullHalfBeamMetres;
            float side = Mathf.Sign(local.x != 0f ? local.x : 1f);
            float z = Mathf.Clamp(local.z, -halfLen, halfLen);
            return ship.TransformPoint(new Vector3(halfBeam * side, 0f, z));
        }

        /// Everything still afloat is quietly gone on save/quit — same rule
        /// as `FloatingCargo.RecallAllForSave`, but this cargo was never
        /// aboard, so it is lost rather than recovered (nothing to return).
        public static void ClearForSave()
        {
            var snapshot = new List<FlotsamCrate>(all);
            foreach (var c in snapshot)
                if (c != null) c.Cull();
        }
    }
}
