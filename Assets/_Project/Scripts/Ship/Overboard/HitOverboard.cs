using System.Collections.Generic;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.World.Life;

namespace SeaSick.Ship.Overboard
{
    /// **What actually sends crew and cargo into the sea** (2026-09-30
    /// Kevin, playtest: *"crew and cargo fall off waaaay too easily when
    /// sailing ... It should be collisions or getting hit by enemies that
    /// cause things to go overboard."*).
    ///
    /// Sailing alone no longer does it: the grip and lashing drains from
    /// roughness, ordinary heel and wave slams are off (`OverboardTuning`),
    /// leaving only a near-capsize extreme. Instead `HullIntegrity` calls
    /// here from the two places a hull is really hit:
    ///
    ///   * `FromCollision` -- a hard contact with rocks, a reef, the shore
    ///     or another hull (`HullIntegrity.Bill`), scaled by closing speed
    ///     between `impactMinSpeed` and `impactFullSpeed`.
    ///   * `FromShot` -- an enemy cannonball landing (`HullIntegrity.TakeShot`).
    ///
    /// Each rolls a chance to throw one hand over (through the existing
    /// `CrewAgent.ThrownOverboard` -> `GoOverboardAt` swimmer routine) and a
    /// chance to knock one crate loose (`CargoLashing.FromImpact`, the same
    /// floating-crate routine). Bulwarks and safety lines cut the crew
    /// chance the same way they cut grip loss. Never while the game is
    /// paused or catching up offline.
    public static class HitOverboard
    {
        static float lastRoll = -99f;
        static readonly List<CrewAgent> scratch = new List<CrewAgent>();

        /// `closingSpeed` in m/s, `point` the contact (or the hull centre if
        /// unknown -- then a side is picked at random).
        public static void FromCollision(Transform ship, Vector3 point, float closingSpeed)
        {
            float min = OverboardTuning.ImpactMinSpeed;
            if (closingSpeed <= min) return;
            float full = Mathf.Max(min + 0.1f, OverboardTuning.ImpactFullSpeed);
            float strength = Mathf.Clamp01((closingSpeed - min) / (full - min));

            float crewChance = Mathf.Lerp(OverboardTuning.ImpactCrewChanceMin, OverboardTuning.ImpactCrewChanceFull, strength);
            float cargoChance = Mathf.Lerp(OverboardTuning.ImpactCargoChanceMin, OverboardTuning.ImpactCargoChanceFull, strength);
            Roll(ship, point, strength, crewChance, cargoChance, twoHands: strength >= 0.75f);
        }

        /// `amount` is `HullIntegrity.TakeShot`'s multiplier on a standard
        /// shot (1 = one ordinary cannonball).
        public static void FromShot(Transform ship, Vector3 point, float amount)
        {
            float k = Mathf.Clamp(amount, 0.25f, 2f);
            Roll(ship, point, Mathf.Clamp01(0.5f * k),
                Mathf.Clamp01(OverboardTuning.ShotCrewChance * k),
                Mathf.Clamp01(OverboardTuning.ShotCargoChance * k), twoHands: false);
        }

        static void Roll(Transform ship, Vector3 point, float strength01,
            float crewChance, float cargoChance, bool twoHands)
        {
            if (ship == null) return;
            if (Time.timeScale <= 0f || SeaSick.Save.AwayProgress.Running) return;
            if (Time.time - lastRoll < OverboardTuning.HitCooldownSeconds) return;
            lastRoll = Time.time;

            // Which side of the ship took it: positive = starboard. Zero (a
            // point at the centre line, or none) lets the callee choose.
            float side = 0f;
            float lx = ship.InverseTransformPoint(point).x;
            if (Mathf.Abs(lx) > 0.5f) side = Mathf.Sign(lx);

            crewChance *= OverboardModules.GripDrainMultiplier();
            if (Random.value < crewChance)
            {
                var roster = ship.GetComponent<CrewRoster>();
                if (roster != null)
                {
                    scratch.Clear();
                    foreach (var c in roster.All)
                        if (c != null && c.gameObject.activeInHierarchy && c.IsAboard) scratch.Add(c);
                    int n = twoHands ? 2 : 1;
                    for (int i = 0; i < n && scratch.Count > 0; i++)
                    {
                        int pick = Random.Range(0, scratch.Count);
                        var who = scratch[pick];
                        scratch.RemoveAt(pick);
                        who.ThrownOverboard(side);
                    }
                }
            }

            if (Random.value < cargoChance)
                CargoLashing.FromImpact(ship.GetComponent<ShipMotor>(), strength01, side);
        }
    }
}
