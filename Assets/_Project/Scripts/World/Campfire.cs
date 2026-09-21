using UnityEngine;

namespace SeaSick.World
{
    /// The flicker on a camp's fire, and — later — the gauge on its stores.
    ///
    /// Settled 2026-09-13: **the campfire is the provisions gauge.** When food
    /// is wired in Phase 2 the fire dims as stores run low, so a camp's health
    /// reads from the water before you anchor, with no interface at all. That
    /// is why this sits on the light rather than the flicker living in the
    /// factory: there is somewhere for `Health01` to go.
    ///
    /// Nothing feeds `Health01` yet. It is 1 and the fire burns bright.
    [RequireComponent(typeof(Light))]
    public class Campfire : MonoBehaviour
    {
        [Tooltip("How the camp is doing. Drives brightness. Nothing writes it yet — food lands in Phase 2.")]
        [Range(0f, 1f)] public float health01 = 1f;

        [SerializeField] float baseIntensity = 2.2f;
        public float flicker = 0.18f;

        [Tooltip("How much of the light is left in full daylight. A fire by day is embers; it still marks the camp.")]
        [SerializeField, Range(0f, 1f)] float byDay = 0.15f;

        Light lamp;
        float seed;

        void Awake()
        {
            lamp = GetComponent<Light>();
            if (lamp != null) baseIntensity = lamp.intensity;
            // Per-fire, so two camps in one shot do not pulse together.
            seed = Random.value * 100f;
        }

        void Update()
        {
            if (lamp == null) return;
            // Two offset waves rather than one, so it reads as a flame and not
            // as a dimmer being turned.
            float t = Time.time * 6.3f + seed;
            float f = 1f + flicker * (Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 2.37f + 1.1f) * 0.4f);
            // A neglected fire is dim, not out: going out entirely would hide
            // the very camp the player has to be told about.
            float dim = Mathf.Lerp(0.32f, 1f, Mathf.Clamp01(health01));
            // Firelight is for the night. By day the sun owns the ground and
            // a full-strength point light only reads as a bug in the shading.
            var sky = SkyDirector.Instance;
            float night = sky != null ? Mathf.Clamp01(sky.Night01) : 1f;
            float when = Mathf.Lerp(byDay, 1f, night);
            lamp.intensity = baseIntensity * dim * f * when;
        }
    }
}
