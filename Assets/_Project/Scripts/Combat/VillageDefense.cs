using UnityEngine;
using SeaSick.World;
namespace SeaSick.Combat
{
    public static class VillageDefense
    {
        public const float SightRange=24f, TowerSightRange=55f;
        public static bool ClearSight(Outpost camp,Vector3 from,Vector3 to) {
            float distance=Vector3.Distance(from,to);
            int steps=Mathf.Clamp(Mathf.CeilToInt(distance/.8f),1,128);
            for(int i=1;i<steps;i++) {
                var p=Vector3.Lerp(from,to,i/(float)steps);
                if(camp.GroundAt(p) > p.y) return false;
            }
            // Physical building/wall colliders block sight, characters do not.
            foreach(var hit in Physics.RaycastAll(from,(to-from).normalized,distance,~0,QueryTriggerInteraction.Ignore)) {
                if(hit.collider.GetComponentInParent<WallSegment>()!=null || hit.collider.GetComponentInParent<Building>()!=null) {
                    if(hit.distance>1.5f && hit.distance<distance-.5f) return false;
                }
            }
            return true;
        }
        public static void Detect(RaidParty party) {
            var camp=party.Camp; if(camp==null || RaidAlarm.IsActive(camp)) return;
            foreach(var body in CampWorker.Bodies) {
                if(body==null || body.Camp!=camp) continue;
                var h=body.HandRow;
                if(h==null || h.downed || h.recovering || h.hiddenInHut || h.sleepHutId!=0 || camp.Ledger.Underground(h)) continue;
                bool tower=RaidAlarm.IsPostedLookout(h) && body.transform.position.y-camp.GroundAt(body.transform.position)>2f;
                float range=tower ? TowerSightRange : SightRange;
                foreach(var foe in party.Walkers) {
                    if(foe==null || foe.Dead) continue;
                    var a=body.transform.position+Vector3.up*1.3f; var b=foe.transform.position+Vector3.up;
                    if((a-b).sqrMagnitude>range*range || !ClearSight(camp,a,b)) continue;
                    RaidAlarm.Begin(camp); RaidAlarm.Flash(camp,h.name+" spotted raiders!"); return;
                }
            }
        }
        // Watch the actual approaching ship too. Ground villagers can spot it,
        // but an elevated lookout has the larger sight radius.
        public static void DetectIncoming(Outpost camp, EnemyShip ship) {
            if (camp == null || ship == null || RaidAlarm.IsActive(camp)) return;
            foreach (var body in CampWorker.Bodies) {
                if (body == null || body.Camp != camp) continue;
                var h = body.HandRow;
                if (h == null || h.downed || h.recovering || h.hiddenInHut || h.sleepHutId != 0 || camp.Ledger.Underground(h)) continue;
                bool tower = RaidAlarm.IsPostedLookout(h) && body.transform.position.y - camp.GroundAt(body.transform.position) > 2f;
                float range = tower ? TowerSightRange : SightRange;
                var eye = body.transform.position + Vector3.up * 1.3f;
                var target = ship.transform.position + Vector3.up * 2f;
                if ((eye - target).sqrMagnitude > range * range || !ClearSight(camp, eye, target)) continue;
                RaidAlarm.Begin(camp);
                RaidAlarm.Flash(camp, h.name + " spotted the raider ship!");
                return;
            }
        }
        public static bool ThreatNear(OutpostLedger ledger,Vector3 at,float radius=7f) {
            var p=RaidParty.Active; if(p==null || p.Camp==null || p.Camp.Ledger!=ledger) return false;
            foreach(var w in p.Walkers) if(w!=null && !w.Dead && w.phase!=RaidWalker.Phase.Fleeing && w.phase!=RaidWalker.Phase.Recalled
                && (w.transform.position-at).sqrMagnitude<radius*radius) return true;
            return false;
        }
    }
}
