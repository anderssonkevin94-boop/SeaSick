using UnityEngine;
namespace SeaSick.Dev
{
    /// Animation only for the isolated art study; not a gameplay water clock.
    [RequireComponent(typeof(Renderer))]
    public sealed class DirectionalOceanStudyMotion : MonoBehaviour
    {
        Renderer surface;
        MaterialPropertyBlock properties;
        void Awake(){surface=GetComponent<Renderer>();properties=new MaterialPropertyBlock();}
        void Update(){surface.GetPropertyBlock(properties);properties.SetFloat("_StudyTime",4+Time.timeSinceLevelLoad);surface.SetPropertyBlock(properties);}
    }
}
