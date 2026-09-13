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
        [SerializeField] float flicker = 0.18f;

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
            lamp.intensity = baseIntensity * dim * f;
        }
    }
}
