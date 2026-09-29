using UnityEngine;
namespace SeaSick.Ship
{
    [CreateAssetMenu(menuName="SeaSick/Ship water effects")]
    public sealed class ShipWaterSettings : ScriptableObject
    {
        public bool effectsEnabled = true;
        [Range(0,2)] public float intensity = 1;
        [Range(.01f,.15f)] public float contactHysteresis = .035f;
        [Range(2,10)] public float wakeLifetime = 7;
        [Range(.1f,.8f)] public float wakeSpread = .42f;
        [Range(.05f,.5f)] public float surfaceLift = .16f;
        [Range(.1f,.8f)] public float bowWidth = .52f;
        [Range(.05f,.4f)] public float bowHeight = .20f;
        [Range(.1f,.6f)] public float bowOffset = .24f;
        public Color waterColor = new Color(.07f,.48f,.64f,1);
        public Color foamColor = new Color(.79f,.93f,.94f,1);
    }
}
