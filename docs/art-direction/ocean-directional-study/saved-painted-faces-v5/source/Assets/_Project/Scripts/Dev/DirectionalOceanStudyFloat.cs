using UnityEngine;
namespace SeaSick.Dev
{
    /// Visual study only: samples this study's analytic waves, not game buoyancy.
    public sealed class DirectionalOceanStudyFloat : MonoBehaviour
    {
        [SerializeField] Material water;
        public void Configure(Material material){water=material;}
        void Update(){if(water)Apply(4+Time.timeSinceLevelLoad);}
        public void Apply(float time)
        {
            if(!water)return;
            float front=Height(0,2.5f,time),back=Height(0,-2.5f,time);
            float left=Height(-1.2f,0,time),right=Height(1.2f,0,time);
            float dz=(front-back)/5,dx=(right-left)/2.4f;
            transform.position=new Vector3(0,(front+back+left+right)*.25f+.1f,0);
            transform.rotation=Quaternion.LookRotation(new Vector3(0,dz,1),new Vector3(-dx,1,-dz));
        }
        float Height(float x,float z,float t)
        {
            return (Wave(x,z,t,new Vector2(.2f,1),32,1.15f,0)+Wave(x,z,t,new Vector2(-.18f,1),14.5f,.35f,2.1f)
                +Wave(x,z,t,new Vector2(.45f,1),5.4f,.085f,4.3f)+Wave(x,z,t,new Vector2(-.55f,1),2.6f,.012f,1.7f))*water.GetFloat("_Amplitude");
        }
        static float Ridge(float p)=>1-2*Mathf.Acos(.985f*Mathf.Cos(p))/Mathf.PI;
        float Wave(float x,float z,float t,Vector2 d,float wavelength,float amplitude,float offset)
        {
            d.Normalize();float along=x*d.x+z*d.y,across=-x*d.y+z*d.x;
            float k=2*Mathf.PI/wavelength,omega=Mathf.Sqrt(9.81f*k),group=along-omega/k*.47f*t;
            float packet=1-water.GetFloat("_CrestVariation")*(.38f-.22f*Mathf.Sin(across*.105f+group*.13f+offset)-.16f*Mathf.Sin(across*.217f-group*.07f+offset*2.3f));
            float phase=k*along+.75f*Ridge(across*.15f+offset)+.25f*Ridge(across*.31f+along*.045f+offset*3)-omega*t+offset;
            float q=phase-Mathf.PI*.5f;
            return amplitude*packet*(Ridge(q)+.24f*Mathf.Sin(q));
        }
    }
}
