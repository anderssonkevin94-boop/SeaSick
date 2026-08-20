using UnityEngine;

namespace SeaSick.Ocean
{
    /// Ship-following ripple/wake simulation. API-stable STUB for the cutover:
    /// callers that used the old WakeTexture (hull wake stamps, cannonball and
    /// monster splashes) compile and run against this; the actual 2D
    /// wave-equation sim lands at the dynamic-interaction milestone.
    public class DynamicWaterSim : MonoBehaviour
    {
        public static DynamicWaterSim Instance { get; private set; }

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        /// Direct impulse into the sim (hull footprints, off-screen actors).
        public void Stamp(Vector2 worldPos, float radius, float foam, float displace)
        {
            // Stub until the ripple sim milestone.
        }

        /// One-off splash ring (cannonballs, jettison, monster breach).
        public static void Splash(Vector3 worldPos, float radius, float strength)
        {
            // Stub until the ripple sim milestone.
        }
    }
}
