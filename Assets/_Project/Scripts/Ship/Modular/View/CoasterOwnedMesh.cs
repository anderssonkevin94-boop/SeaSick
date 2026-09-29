using UnityEngine;
namespace SeaSick.Ship.Modular
{
    public sealed class CoasterOwnedMesh : MonoBehaviour
    {
        public Mesh mesh;
        void OnDestroy(){if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}}
    }
}
