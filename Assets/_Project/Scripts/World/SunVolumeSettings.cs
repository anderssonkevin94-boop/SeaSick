using UnityEngine;
namespace SeaSick.World
{
    [CreateAssetMenu(menuName = "SeaSick/Volumetric sunlight")]
    public sealed class SunVolumeSettings : ScriptableObject
    {
        public enum Quality { Off, Low, Medium, High }
        public Quality desktopQuality = Quality.Medium;
        public Quality mobileQuality = Quality.Off;
        [Range(30,250)] public float distance = 160;
        [Range(0,.01f)] public float density = .0015f;
        [Range(0,2)] public float strength = .7f;
    }
}
