using UnityEngine;
namespace SeaSick.Ship
{
    [DefaultExecutionOrder(-100)]
    public sealed class ShipWaterProbeFeeder : MonoBehaviour
    {
        public ShipWaterEffects Owner {get;set;}
        void FixedUpdate(){if(Owner!=null&&Owner.isActiveAndEnabled)Owner.WriteQueries();}
    }
}
