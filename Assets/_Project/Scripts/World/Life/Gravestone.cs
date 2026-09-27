using UnityEngine;

namespace SeaSick.World.Life
{
    /// Tags a tombstone (ghost or planted) with whose grave it is, so a tap
    /// on it (`GravePlacementFlow.TryOpenStoryAt`, through the ashore tap
    /// router in `IslandInput.RegisterTap`) can find the story to show.
    public class Gravestone : MonoBehaviour
    {
        public string graveName = "";
    }
}
