using UnityEngine;
namespace SeaSick.Ship.Modular
{
    /// Daylight restraint, warm pools at dusk; no extra shadow maps on mobile.
    public sealed class CoasterLantern : MonoBehaviour
    {
        Light lamp; SeaSick.World.SkyDirector sky;
        void Awake(){ lamp=GetComponent<Light>(); sky=FindFirstObjectByType<SeaSick.World.SkyDirector>(); }
        void Update(){ if(lamp) lamp.intensity=Mathf.Lerp(.18f,1.6f,sky ? sky.Night01 : 0f); }
    }
}
