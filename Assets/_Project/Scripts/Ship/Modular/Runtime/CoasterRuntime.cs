using UnityEngine;
using SeaSick.Crew;
namespace SeaSick.Ship.Modular
{
    public sealed class CoasterRuntime : MonoBehaviour
    {
        public ShipConfiguration Configuration {get;private set;}
        public static void Install(GameObject ship,ModularShipView view,ShipyardPlan plan)
        {
            var runtime=view.GetComponent<CoasterRuntime>();if(runtime==null)runtime=view.gameObject.AddComponent<CoasterRuntime>();runtime.Configuration=plan.config.Clone();
            // Battery owns projectiles/traverse/recoil; the modular view supplies its approved art.
            var guns=new System.Collections.Generic.List<Cannon>(ship.GetComponent<CannonBattery>().Guns).ToArray();
            foreach(var gun in guns)
            {
                Transform art=null;float best=.05f;
                foreach(Transform part in view.transform)
                {
                    if(!part.name.StartsWith("equipment:"))continue;
                    float d=(ship.transform.InverseTransformPoint(part.position)-gun.transform.localPosition).sqrMagnitude;
                    if(d<best){best=d;art=part;}
                }
                if(art==null)continue;
                foreach(var r in gun.GetComponentsInChildren<Renderer>())r.enabled=false;
                // Keep the mesh's verified axis, then let the cannon carry it when traversing.
                var drawn=Object.Instantiate(art,gun.transform,true);drawn.gameObject.SetActive(true);art.gameObject.SetActive(false);
                gun.BindCoasterArt(drawn);
                var body=gun.gameObject.AddComponent<BoxCollider>();body.center=new Vector3(0,.38f,.05f);body.size=new Vector3(.82f,.76f,1.30f);
            }
            ship.GetComponent<CannonBattery>().SetGunnerClearance(.95f);
            var helm=ship.transform.Find("Helmsman");if(helm!=null){helm.localPosition=plan.data.helm;helm.localRotation=Quaternion.identity;}
            // Tap target only (WorldPicker's ray includes triggers and SelectionRing's overlap does too).
            // A TRIGGER: a solid capsule with no rigidbody, moved by transform as a hand walks ashore and
            // back, overlaps the hull box and PhysX depenetrates the ship at deck height -> capsize.
            foreach(var hand in ship.GetComponentsInChildren<CrewAgent>())
            {var c=hand.GetComponent<CapsuleCollider>();if(c==null)c=hand.gameObject.AddComponent<CapsuleCollider>();c.isTrigger=true;c.radius=.22f;c.height=1.65f;c.center=Vector3.up*.825f;}
            var nav=view.GetComponent<CoasterNavigation>();if(nav==null)nav=view.gameObject.AddComponent<CoasterNavigation>();nav.Build(ship.transform,plan, guns);
            var roster=ship.GetComponent<CrewRoster>();
            var posted=new System.Collections.Generic.HashSet<CrewAgent>();
            var occupied=new System.Collections.Generic.List<Vector3>();
            for(int i=0;i<guns.Length;i++){var hand=roster?.GunCrew(i);if(hand!=null){posted.Add(hand);occupied.Add(hand.transform.localPosition);}}
            foreach(var hand in ship.GetComponentsInChildren<CrewAgent>())
            {
                if(!hand.IsAboard)continue;
                if(!posted.Contains(hand))
                {
                    var at=nav.SpareStation(occupied,plan.viewOffset.z+4.9f);
                    var rail=nav.ClosestWalkable(at+Vector3.right*(at.x<0?-1.3f:1.3f));hand.AssignStation(at,rail);
                }
                hand.transform.localPosition=nav.ClosestWalkable(hand.transform.localPosition);
            }
            // Hide the legacy fallback battery meshes only after the imported meshes are attached.
            runtime.nav=nav;
            float accessZ=plan.viewOffset.z+4.9f+(plan.config.middleIds.Count>0?1.5f:2f);
            ship.GetComponent<ShipHold>()?.SetAbstractStorage(true,nav.ClosestWalkable(new Vector3(0,.88f,accessZ)));
            // Gun boxes, deck-ramp / navy-wall boxes and crew capsules were all added after HullIntegrity.Start:
            // they must ignore the Land layer like the hull box does, or a sloped beach levers her over.
            HullIntegrity.ExcludeLand(ship);
        }
        CoasterNavigation nav;
        void OnDestroy(){if(nav!=null)nav.Clear();}
    }
}
