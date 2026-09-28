using System.Collections.Generic;
using UnityEngine;
using SeaSick.Voyage;
using SeaSick.World;
using SeaSick.World.Life;

namespace SeaSick.Ship.Overboard
{
    /// **A crate lost over the side** (phase 6, docs/PLAN-DEATH-RESCUE.md
    /// "Man overboard", last bullet). Spawned by `CargoLashing.TrySlideCargo`
    /// when the lashing meter empties; hauled back in by the same
    /// `CrewAgent`/`RescueHud` machinery as a swimmer, through
    /// `IOverboardTarget`. Drifts and rides the real wave exactly like
    /// `Swimmer` (same code, copied rather than shared through a base class
    /// — the two have almost nothing else in common and a shared base would
    /// buy nothing but indirection for two fields).
    ///
    /// **Visual**: `CargoVisual.Build` — the SAME drawing the deck load and
    /// the camp piles use, not a new box, so a crate looks like the thing it
    /// was a second ago on deck. A stack of up to four units, tilted a
    /// little off the wave's own normal so it reads as floating rather than
    /// standing.
    public class FloatingCargo : MonoBehaviour, IOverboardTarget
    {
        static readonly List<FloatingCargo> all = new List<FloatingCargo>();
        public static IReadOnlyList<FloatingCargo> All => all;

        public string Resource { get; private set; }
        public int Units { get; private set; }
        float timeLeft;
        float timeTotal = 1f;
        public bool Resolved { get; private set; }
        public Vector3 WorldPosition => transform.position;

        Transform ship;
        Vector3 driftVel;
        GameObject visual;

        const float SettleSeconds = 20f;

        // --- IOverboardTarget -------------------------------------------
        Transform IOverboardTarget.Transform => transform;
        public float TimeLeft01 => timeTotal > 0f ? Mathf.Clamp01(timeLeft / timeTotal) : 0f;
        string IOverboardTarget.Label => Units + " " + Resource.ToLowerInvariant();
        /// Cargo yields to a person (build brief item 1).
        int IOverboardTarget.RescuePriority => 1;
        bool IOverboardTarget.Boardable => true;
        public Transform Hull => ship;
        public bool BeingHauled { get; set; }
        public Vector3 HaulAnchor { get; set; }
        void IOverboardTarget.OnHauled(string rescuerName) => Recover(rescuerName);

        /// **Spawn one.** `hull` is the ship she fell from — used for the
        /// reach check and to find her again once recovered.
        public static FloatingCargo Spawn(string resource, int units, Transform hull, Vector3 worldPos)
        {
            if (units <= 0 || string.IsNullOrEmpty(resource)) return null;
            var go = new GameObject("FloatingCargo_" + resource);
            go.transform.position = worldPos;
            var c = go.AddComponent<FloatingCargo>();
            c.Resource = resource;
            c.Units = units;
            // The crew's own `ship` can be unset (e.g. docked/anchored
            // bookkeeping); fall back to the player's hull so reach checks
            // never measure a target against itself (2026-09-28 smoke).
            if (hull == null) { var m = Object.FindAnyObjectByType<SeaSick.Ship.ShipMotor>(); if (m != null) hull = m.transform; }
            c.ship = hull;
            c.timeTotal = Mathf.Max(10f, OverboardTuning.FloatSeconds);
            c.timeLeft = c.timeTotal;

            // Drift: same idea as `Swimmer` — a fixed slow heading, seeded
            // off the spawn moment so two crates don't drift identically.
            float ang = (Time.frameCount * 37 + units * 13) % 360 * Mathf.Deg2Rad;
            c.driftVel = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * OverboardTuning.DriftSpeed;

            c.BuildVisual();
            all.Add(c);
            return c;
        }

        void BuildVisual()
        {
            visual = new GameObject("Crate");
            visual.transform.SetParent(transform, false);
            int shown = Mathf.Clamp(Units, 1, 4);
            for (int i = 0; i < shown; i++)
            {
                var unit = CargoVisual.Build(Resource, visual.transform);
                unit.transform.localPosition = CargoVisual.StackSlot(i, 2, 0.55f, 0.4f);
            }
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            if (Resolved) return;
            float dt = Time.deltaTime;

            Vector3 pos = BeingHauled
                ? Vector3.MoveTowards(transform.position, HaulAnchor, OverboardTuning.HaulPullSpeed * dt)
                : transform.position + driftVel * dt;

            float bob = 0.15f;
            Vector3 normal = Vector3.up;
            if (Ocean.OceanSampler.Ready)
            {
                var sample = Ocean.OceanSampler.SampleImmediate(pos);
                pos.y = sample.height + bob;
                normal = sample.normal;
            }

            // In the last SettleSeconds she rides lower and lower before
            // sinking outright — "slowly settles lower... and then sinks".
            float settleT = Mathf.InverseLerp(SettleSeconds, 0f, timeLeft);
            pos.y -= settleT * (bob + 0.35f);
            transform.position = pos;

            // A slight tilt off the wave's own normal — enough to read as
            // floating, not so much a stacked crate looks capsized.
            Quaternion tiltTarget = Quaternion.FromToRotation(Vector3.up, normal);
            transform.rotation = Quaternion.Slerp(transform.rotation, tiltTarget, dt * 2f);

            timeLeft -= dt;
            if (timeLeft <= 0f) Sink();
        }

        void Sink()
        {
            if (Resolved) return;
            Resolved = true;
            Banner.Show("The " + Resource.ToLowerInvariant() + " sank.");
            Debug.Log("[FloatingCargo] " + Units + " " + Resource + " lost overboard (sank).");
            Destroy(gameObject);
        }

        /// **`CrewAgent.TickHaul` calls this once the haul finishes.** Units
        /// go back into the hold — through `VoyageManager.ReturnCargo`,
        /// which never drops a unit even past the marked line, because these
        /// were already aboard a moment ago.
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

            Banner.Show("Recovered " + Units + " " + Resource.ToLowerInvariant());
            Destroy(gameObject);
        }

        /// **Save (phase 6, build brief item 5).** A crate riding the sea is
        /// not written to disk — on capture, every one still afloat is
        /// pulled straight back into the hold it came from so nothing
        /// vanishes through a save/quit. Called by `SaveGame.Capture`
        /// BEFORE it reads `VoyageManager.HeldStores`.
        public static void RecallAllForSave()
        {
            if (all.Count == 0) return;
            // Copy first -- `Recover` mutates `all` through `OnDestroy`.
            var snapshot = new List<FloatingCargo>(all);
            foreach (var c in snapshot)
                if (c != null) c.Recover(null);
        }

        /// The nearest point on the hull's SIDE — same shape as
        /// `Swimmer.NearestHullSide`.
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
    }
}
