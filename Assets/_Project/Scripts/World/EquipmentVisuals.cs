using System.Collections.Generic;
using UnityEngine;
namespace SeaSick.World
{
    /// Gear is presentation only. Instances stay under the body root, avoiding
    /// the imported rig's enlarged bone scale; live bone poses drive attachments.
    [DefaultExecutionOrder(210)]
    public sealed class EquipmentVisuals : MonoBehaviour
    {
        CampWorker worker;
        readonly GameObject[] worn=new GameObject[5];
        readonly string[] ids=new string[5];
        readonly List<Attachment> attachments=new();
        Transform wrist,head,chest,leftLeg,rightLeg,leftFoot,rightFoot;
        bool bound;
        sealed class Attachment
        {
            public Transform prop,bone;
            public Quaternion bind;
            public Vector3 offset,fallback;
        }
        void LateUpdate()
        {
            if(worker==null)worker=GetComponent<CampWorker>();
            var hand=worker!=null?worker.HandRow:null;
            bool visible=hand?.equipment!=null && !hand.hiddenInHut && !hand.recovering;
            if(!visible) { foreach(var go in worn)if(go!=null)go.SetActive(false);return; }
            if(!bound)Bind();
            var gear=hand.equipment;
            for(int i=0;i<5;i++)
            {
                string id=gear.Get((EquipmentSlot)(i+1));
                if(i==0 && (gear.mainHand==Res.Bow || !VillagerEquipment.IsShield(id)))id=null;
                if(ids[i]!=id)Rebuild(i,id);
                if(worn[i]!=null)worn[i].SetActive(true);
            }
            float scale=transform.lossyScale.y;
            foreach(var a in attachments)
            {
                if(a.prop==null)continue;
                Quaternion rot=a.bone!=null?a.bone.rotation*Quaternion.Inverse(a.bind):transform.rotation;
                Vector3 at=a.bone!=null?a.bone.position:transform.TransformPoint(a.fallback);
                a.prop.SetPositionAndRotation(at+rot*a.offset*scale,rot);
            }
            if(worn[0]!=null)
            {
                Vector3 at=hand.defending && wrist!=null?wrist.position+transform.forward*.12f*scale
                    :transform.TransformPoint(new Vector3(0,.65f,-.20f));
                worn[0].transform.SetPositionAndRotation(at,transform.rotation);
            }
        }
        void Rebuild(int slot,string id)
        {
            if(worn[slot]!=null)Destroy(worn[slot]);
            attachments.RemoveAll(a=>a.prop==null || (worn[slot]!=null && a.prop.IsChildOf(worn[slot].transform)));
            worn[slot]=null;ids[slot]=id;if(string.IsNullOrEmpty(id))return;
            var root=EquipmentPlaceholder.Create(id,transform);worn[slot]=root;
            if(slot==1)Attach(root.transform,head,new Vector3(0,.26f,0),new Vector3(0,1.4f,0));
            if(slot==2)Attach(root.transform,chest,new Vector3(0,.13f,0),new Vector3(0,.6f,0));
            if(slot==3 || slot==4)
            {
                bool boots=slot==4;
                var left=root.transform.Find("Left");var right=root.transform.Find("Right");
                if(left!=null && right!=null)
                {
                    Attach(left,boots?leftFoot:leftLeg,boots?new Vector3(0,.015f,.04f):new Vector3(0,-.10f,0),new Vector3(-.15f,boots?.1f:.65f,0));
                    Attach(right,boots?rightFoot:rightLeg,boots?new Vector3(0,.015f,.04f):new Vector3(0,-.10f,0),new Vector3(.15f,boots?.1f:.65f,0));
                }
                else Attach(root.transform,chest,Vector3.zero,new Vector3(0,1,0));
            }
        }
        void Attach(Transform prop,Transform bone,Vector3 offset,Vector3 fallback)
        {
            attachments.Add(new Attachment { prop=prop,bone=bone,offset=offset,fallback=fallback,
                bind=bone!=null?Quaternion.Inverse(transform.rotation)*bone.rotation:Quaternion.identity });
        }
        void Bind()
        {
            bound=true;float min=float.MaxValue;
            foreach(var t in GetComponentsInChildren<Transform>(true))
            {
                string n=t.name.ToLowerInvariant();
                if(n=="head")head=t;
                if(n=="chest" || n=="spine")chest=t;
                if(n=="leg_l" || n.Contains("thigh.l"))leftLeg=t;
                if(n=="leg_r" || n.Contains("thigh.r"))rightLeg=t;
                if(n=="foot_l" || n.Contains("foot.l"))leftFoot=t;
                if(n=="foot_r" || n.Contains("foot.r"))rightFoot=t;
                if(n.StartsWith("hand") && !n.Contains("finger"))
                { float x=transform.InverseTransformPoint(t.position).x;if(x<min){min=x;wrist=t;} }
            }
        }
        void OnDisable(){foreach(var go in worn)if(go!=null)go.SetActive(false);}
        void OnDestroy(){foreach(var go in worn)if(go!=null)Destroy(go);}
    }
}
