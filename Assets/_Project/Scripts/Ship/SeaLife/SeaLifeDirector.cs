using UnityEngine;
using SeaSick.Ship.Overboard;

namespace SeaSick.Ship.SeaLife
{
    /// **The heartbeat of "things to find at sea"** (2026-09-28): rolls the
    /// dice for flotsam/bottles and fish shoals on their own jittered
    /// timers, and watches for the smooth-sailing streak that earns
    /// dolphins. Only ever while `Sailing.IsLive` — never offline catch-up,
    /// never paused, never at anchor — same guard every overboard system
    /// checks (`Sailing.cs`).
    ///
    /// Self-installing singleton, same shape as `RescueHud`/`Banner`: one
    /// per boot, `DontDestroyOnLoad`, no scene wiring needed.
    public class SeaLifeDirector : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<SeaLifeDirector>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("SeaLifeDirector");
            go.AddComponent<SeaLifeDirector>();
            DontDestroyOnLoad(go);
        }

        ShipMotor motor;
        AnchorController anchor;
        SmoothnessMeter meter;
        float nextLookup;

        float nextFlotsamAt = -1f;
        float nextShoalAt = -1f;
        float dolphinSmoothFor;
        float dolphinCooldownLeft;

        void Update()
        {
            if (Time.time >= nextLookup)
            {
                if (motor == null) motor = FindAnyObjectByType<ShipMotor>();
                if (anchor == null) anchor = FindAnyObjectByType<AnchorController>();
                if (motor != null && meter == null) meter = motor.GetComponent<SmoothnessMeter>();
                nextLookup = Time.time + 1f;
            }
            if (motor == null) return;
            if (!Sailing.IsLive(anchor)) return;

            float dt = Time.deltaTime;
            TickFlotsam(dt);
            TickShoal(dt);
            TickDolphins(dt);
        }

        // --- flotsam + bottle ---------------------------------------------

        void TickFlotsam(float dt)
        {
            if (nextFlotsamAt < 0f) nextFlotsamAt = Time.time + JitteredSeconds(SeaLifeTuning.FlotsamEverySeconds);
            if (Time.time < nextFlotsamAt) return;
            nextFlotsamAt = Time.time + JitteredSeconds(SeaLifeTuning.FlotsamEverySeconds);

            if (FlotsamCrate.All.Count >= 2) return; // "if fewer than 2 exist" -- build brief item 1

            if (!SeaLifeSpawn.TryFindSpot(motor.transform.position, motor.transform.forward,
                    SeaLifeTuning.FlotsamAheadMin, SeaLifeTuning.FlotsamAheadMax, SeaLifeTuning.FlotsamConeDeg, out var spot))
                return;

            if (Random.value < SeaLifeTuning.BottleChance)
                MessageBottle.Spawn(motor.transform, spot);
            else
                FlotsamCrate.Spawn(motor.transform, spot);
        }

        // --- fish shoal -----------------------------------------------------

        void TickShoal(float dt)
        {
            if (nextShoalAt < 0f) nextShoalAt = Time.time + JitteredSeconds(SeaLifeTuning.ShoalEverySeconds);
            if (Time.time < nextShoalAt) return;
            nextShoalAt = Time.time + JitteredSeconds(SeaLifeTuning.ShoalEverySeconds);

            if (FishShoal.All.Count >= 1) return; // one at a time is plenty of "alive sea"

            if (!SeaLifeSpawn.TryFindSpot(motor.transform.position, motor.transform.forward,
                    SeaLifeTuning.ShoalAheadMin, SeaLifeTuning.ShoalAheadMax, 60f, out var spot))
                return;

            FishShoal.Spawn(motor.transform, spot);
        }

        // --- dolphins -------------------------------------------------------

        void TickDolphins(float dt)
        {
            if (dolphinCooldownLeft > 0f) dolphinCooldownLeft -= dt;
            if (DolphinPod.Present) { dolphinSmoothFor = 0f; return; }

            float rough = meter != null ? meter.Roughness01 : 1f;
            bool smoothAndFast = motor.CurrentSpeed > SeaLifeTuning.DolphinMinSpeed
                && rough < SeaLifeTuning.DolphinMaxRoughness01;

            dolphinSmoothFor = smoothAndFast ? dolphinSmoothFor + dt : 0f;

            if (dolphinCooldownLeft <= 0f && dolphinSmoothFor >= SeaLifeTuning.DolphinBuildupSeconds)
            {
                DolphinPod.Spawn(motor.transform);
                dolphinCooldownLeft = SeaLifeTuning.DolphinCooldownSeconds;
                dolphinSmoothFor = 0f;
            }
        }

        static float JitteredSeconds(float baseSeconds) =>
            Mathf.Max(5f, baseSeconds * Random.Range(0.7f, 1.3f));

        // --- dev hooks (LifeDevPanel) ---------------------------------------

        public static void DebugSpawnFlotsam()
        {
            var motor = FindAnyObjectByType<ShipMotor>();
            if (motor == null) return;
            Vector3 spot = motor.transform.position + motor.transform.forward * 60f;
            FlotsamCrate.Spawn(motor.transform, spot);
        }

        public static void DebugSpawnBottle()
        {
            var motor = FindAnyObjectByType<ShipMotor>();
            if (motor == null) return;
            Vector3 spot = motor.transform.position + motor.transform.forward * 60f;
            MessageBottle.Spawn(motor.transform, spot);
        }

        public static void DebugSpawnShoal()
        {
            var motor = FindAnyObjectByType<ShipMotor>();
            if (motor == null) return;
            Vector3 spot = motor.transform.position + motor.transform.forward * 60f;
            FishShoal.Spawn(motor.transform, spot);
        }

        public static void DebugDolphinsNow()
        {
            var motor = FindAnyObjectByType<ShipMotor>();
            if (motor == null) return;
            DolphinPod.Spawn(motor.transform);
        }
    }
}
