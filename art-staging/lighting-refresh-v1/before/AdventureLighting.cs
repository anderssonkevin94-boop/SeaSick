using UnityEngine;
namespace SeaSick.World
{
    [CreateAssetMenu(menuName="SeaSick/Adventure lighting")]
    public sealed class AdventureLighting : ScriptableObject
    {
        public bool applyToGame=true;
        [Range(.4f,1f)] public float sunHeightScale=.72f;
        public Color sunlight=new Color(1f,.89f,.73f);
        public float sunIntensity=1.12f;
        public Color ambientEquator=new Color(.36f,.43f,.60f);
        public Color ambientGround=new Color(.19f,.24f,.33f);
        [Range(0,1)] public float skyFill=.68f;
        [Header("Golden hour")]
        [Range(.17f,.5f)] public float goldenHourElevation=.32f;
        public Color sunriseHorizon=new Color(1f,.58f,.34f);
        public Color sunsetHorizon=new Color(.98f,.38f,.23f);
        public Color sunriseLight=new Color(1f,.76f,.51f);
        public Color sunsetLight=new Color(1f,.61f,.36f);
        public float fogStart=950f, fogEnd=1500f;
    }
}
