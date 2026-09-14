using UnityEngine;

namespace SeaSick.World
{
    /// Identity carried by each standalone island prefab. The island economy
    /// binds ResourceNode when the asset is placed or a landing party approaches.
    public class IslandAsset : MonoBehaviour
    {
        public string assetId;
        public string resource;
        public float authoredHeight;
    }
}
