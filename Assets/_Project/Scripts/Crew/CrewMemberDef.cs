using UnityEngine;

namespace SeaSick.Crew
{
    public enum CrewRole { Deckhand, Steerer, Cook, Doctor }

    /// A villager as data. The roster layer (recruitment, traits, breeding,
    /// sea legs) will grow around this asset type without touching scenes.
    [CreateAssetMenu(menuName = "SeaSick/Crew Member", fileName = "CrewMember")]
    public class CrewMemberDef : ScriptableObject
    {
        public string displayName = "Unnamed";
        public CrewRole role = CrewRole.Deckhand;

        [Tooltip("0 = weak-kneed landlubber, 1 = iron stomach. Halves sickness rate at max.")]
        [Range(0f, 1f)] public float ironStomach = 0.2f;
    }
}
