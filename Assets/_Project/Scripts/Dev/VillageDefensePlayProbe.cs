#if UNITY_EDITOR
using System;
using System.Reflection;
using SeaSick.World;
using SeaSick.Combat;
using SeaSick.Crew;
using UnityEngine;
using UnityEngine.UIElements;
using SeaSick.UI.Sheets;

public class VillageDefensePlayProbe : MonoBehaviour
{
    public static string Result;
    public static Action<bool> SetPhone;
    EquipmentSheet equipmentPage;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    Outpost camp; OutpostLedger ledger; OutpostHand hand; CampWorker worker; RaidWalker foe;
    Camera cameraView; float time; int stage; float health;
    static void Set(object o,string name,object value) => o.GetType().GetField(name,Flags).SetValue(o,value);
    static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name,Flags).Invoke(o,args);
    static void Gate(bool ok,string label) { if(!ok) throw new Exception(label); Debug.Log("[DefensePlay] PASS "+label); }
    float Hp => (float)typeof(RaidWalker).GetField("hp",Flags).GetValue(foe);
    void Start() { try { Setup(); } catch(Exception e) { Result="FAIL "+e; enabled=false; } }
    void Setup() {
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale=Vector3.one*5f;
        ground.GetComponent<Renderer>().material.color=new Color(.34f,.43f,.3f);
        var light=new GameObject("Sun").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.4f; light.transform.rotation=Quaternion.Euler(45,-30,0);
        RenderSettings.ambientLight=new Color(.55f,.55f,.6f);
        camp=new GameObject("Defense fixture").AddComponent<Outpost>(); camp.enabled=false;
        ledger=new OutpostLedger(); Set(camp,"ledger",ledger); Set(camp,"height",new Func<float,float,float>((x,z)=>0f));
        hand=new OutpostHand{name="Defender",mood=1f}; ledger.hands.Add(hand);
        var prefab=Resources.Load<GameObject>("AstraPlaytest/DeckhandVisual"); Gate(prefab!=null,"actual crew visual available");
        var body=Instantiate(prefab); body.name="Defender"; body.transform.SetParent(camp.transform);
        var agent=body.GetComponent<CrewAgent>() ?? body.AddComponent<CrewAgent>();
        agent.SetDef(CrewNames.MakeDef(hand.name,null)); agent.enabled=false;
        CampWorker.Attach(camp,agent); worker=body.GetComponent<CampWorker>(); worker.enabled=false;
        hand.equipment.mainHand="IronSword";
        hand.equipment.helmet="IronHelmet";hand.equipment.upperBody="IronArmor";hand.equipment.pants="IronPants";hand.equipment.shoes="IronBoots";
        hand.equipment.offHand="IronShield";
        var enemy=Instantiate(prefab); enemy.name="Raider"; enemy.transform.position=new Vector3(0,0,1.1f);
        var ea=enemy.GetComponent<CrewAgent>() ?? enemy.AddComponent<CrewAgent>(); ea.enabled=false;
        foe=enemy.AddComponent<RaidWalker>(); foe.enabled=false; foe.camp=camp;
        VillagerActing.On(ea); Set(foe,"hp",30f);
        var party=new GameObject("Raid fixture").AddComponent<RaidParty>(); party.enabled=false;
        typeof(RaidParty).GetProperty("Camp").SetValue(party,camp);
        var walkers=(System.Collections.Generic.List<RaidWalker>)typeof(RaidParty).GetField("walkers",Flags).GetValue(party); walkers.Add(foe);
        typeof(RaidParty).GetProperty("Active").SetValue(null,party);
        // Use the actual sight detector with a distant and then close target.
        CampWorker.Bodies.Add(worker);
        foe.transform.position=new Vector3(0,0,80f);
        VillageDefense.Detect(party);
        Gate(!RaidAlarm.IsActive(camp),"distant raider does not trigger alarm");
        foe.transform.position=new Vector3(0,0,1.1f);
        VillageDefense.Detect(party);
        Gate(RaidAlarm.IsActive(camp),"one visible raider raises shared alarm");
        cameraView=new GameObject("Preview camera").AddComponent<Camera>(); cameraView.clearFlags=CameraClearFlags.SolidColor; cameraView.backgroundColor=new Color(.13f,.18f,.22f);
        cameraView.tag="MainCamera";
        cameraView.orthographic=true; cameraView.orthographicSize=3.8f;
        cameraView.transform.position=new Vector3(5,4,-6); cameraView.transform.LookAt(new Vector3(0,1,1));
        health=Hp;
    }
    void Update() {
        if(Result!=null) return;
        try {
            time+=Time.deltaTime;
            if(stage==0) {
                Call(worker,"TickDefend",hand,Time.deltaTime);
                if(time>.15f) { Gate(Hp==health,"melee wind-up causes no early damage"); stage=1; }
            } else if(stage==1) {
                Call(worker,"TickDefend",hand,Time.deltaTime);
                if(time>.65f) {
                    Gate(Hp<health,"melee contact damages actual raid walker");
                    Gate(worker.transform.Find("Equipment_IronSword")!=null,"sword placeholder in actual combat");
                    var props=(GameObject[])typeof(VillagerActing).GetField("tools",Flags).GetValue(worker.GetComponent<VillagerActing>());
                    Gate(props[(int)VillagerActing.Mode.MeleeAttack]==null || !props[(int)VillagerActing.Mode.MeleeAttack].activeSelf,"sword does not spawn a second hammer");
                    Capture("melee-phone",720,1560); Capture("melee-desktop",1280,720);
                    hand.equipment.helmet="LeatherHelmet";hand.equipment.upperBody="LeatherVest";hand.equipment.pants="LeatherPants";hand.equipment.shoes="LeatherBoots";
                    hand.defending=false; hand.equipment.mainHand=Res.Bow; hand.equipment.offHand=null; hand.equipment.arrows=6;
                    foe.transform.position=new Vector3(0,0,6); Set(foe,"hp",30f);
                    UnityEngine.Random.InitState(4);
                    Call(worker,"Shoot",hand,foe,worker.transform.position+Vector3.up*1.35f,hand);
                    Gate(hand.equipment.arrows==5 && Hp==30f,"bow spends one arrow before damage");
                    Gate(FindObjectsByType<ArrowFlight>(FindObjectsSortMode.None).Length>0,"visible projectile spawned");
                    stage=2; time=0f;
                }
            } else if(stage==2) {
                worker.GetComponent<VillagerActing>().CombatPose(VillagerActing.Mode.BowAttack,.55f);
                HunterProps.On(worker.gameObject).Drive(Res.Bow,HunterProps.Pose.Thrust,foe.transform.position+Vector3.up);
                if(time>.8f) {
                    Gate(Hp<30f,"arrow damage arrives after flight"); Capture("archer-phone",720,1560); Capture("archer-desktop",1280,720);
                    health=Hp; UnityEngine.Random.InitState(4);
                    Call(worker,"Shoot",hand,foe,worker.transform.position+Vector3.up*1.35f,hand);
                    foe.transform.position+=Vector3.right*4f;
                    stage=3; time=0;
                }
            } else if(stage==3 && time>.8f) {
                Gate(Hp==health,"moving off ballistic endpoint dodges arrow");
                Gate(hand.equipment.arrows==4,"miss still consumes ammunition");
                CheckRaiderStrike();
                CheckRescue();
                hand.defending=false; hand.rescuing="";
                equipmentPage = new EquipmentSheet(camp, hand.name);
                Sheets.Open(equipmentPage);
                stage=4; time=0;
            } else if(stage==4 && time>.8f) {
                CheckEquipmentLayout();
                ScreenCapture.CaptureScreenshot("Logs/DefensePreview/equipment-phone.png");
                stage=5; time=0;
            } else if(stage==5 && time>.4f) {
                var root=SheetHost.Instance.GetComponent<UIDocument>().rootVisualElement;
                var scroll=root.Q<ScrollView>("villager-equipment-scroll");
                scroll.verticalScroller.value=scroll.verticalScroller.highValue;
                stage=6; time=0;
            } else if(stage==6 && time>.4f) {
                ScreenCapture.CaptureScreenshot("Logs/DefensePreview/equipment-phone-bottom.png");
                stage=7; time=0;
            } else if(stage==7 && time>.4f) {
                SetPhone(false); stage=8; time=0;
            } else if(stage==8 && time>.8f) {
                CheckEquipmentLayout();
                ScreenCapture.CaptureScreenshot("Logs/DefensePreview/equipment-desktop.png");
                stage=9; time=0;
            } else if(stage==9 && time>.4f) {
                Result="PASS: sight alarm, melee contact, ballistic arrows, ammo, dodging, rescue arrival and missing hut, equipment phone/desktop layout";
            }
            if(time>12f) throw new Exception("probe timed out");
        } catch(Exception e) { Result="FAIL: "+e; }
    }
    void CheckRaiderStrike() {
        worker.transform.position=Vector3.zero;
        ledger.BodyAt(hand,worker.transform.position);
        foe.transform.position=new Vector3(0,0,1.1f);
        hand.equipment.offHand=null;
        Set(foe,"fightTarget",hand); Set(foe,"hitClock",0f); Set(foe,"strikeLanded",false);
        float wounds=hand.combatDamage;
        Call(foe,"TickFight",RaidFightTuning.RaiderHitSeconds*.2f);
        Gate(hand.combatDamage==wounds,"raider wind-up does not deal early damage");
        Call(foe,"TickFight",RaidFightTuning.RaiderHitSeconds*.35f);
        Gate(hand.combatDamage>wounds,"raider strike damages actual villager at contact");
        var actor=foe.GetComponent<VillagerActing>();
        actor.CombatPose(VillagerActing.Mode.MeleeAttack,.15f); Call(actor,"Step",.25f); Call(actor,"Step",.25f);
        var arm=(Transform)typeof(VillagerActing).GetProperty("ToolArm",Flags).GetValue(actor);
        Quaternion before=arm.localRotation;
        actor.CombatPose(VillagerActing.Mode.MeleeAttack,.3f); Call(actor,"Step",.1f);
        Gate(Quaternion.Angle(before,arm.localRotation)>10f,"raider weapon arm actually animates on the mirrored rig");
        var props=(GameObject[])typeof(VillagerActing).GetField("tools",Flags).GetValue(actor);
        var hammer=props[(int)VillagerActing.Mode.MeleeAttack];
        Gate(hammer!=null && hammer.activeSelf,"raider holds visible weapon during swing");
        Capture("raider-swing-phone",720,1560);
        hand.lastCombatHit=-100f;
    }
    void CheckRescue() {
        RaidAlarm.End(camp);
        var casualty=new OutpostHand { name="Casualty" }; ledger.hands.Add(casualty);
        ledger.Down(casualty,"Raid"); ledger.BodyAt(casualty,worker.transform.position);
        hand.rescuing=casualty.name;
        Call(worker,"TickRescue",hand,.01f);
        Gate(casualty.reached && casualty.downed,"physical pickup leaves casualty bleeding");
        Call(worker,"TickRescue",hand,.01f);
        Gate(casualty.downed && !casualty.recovering,"no hut cannot complete rescue");
        var hut=new GameObject("Rescue hut").AddComponent<Building>(); hut.Configure(BuildPlans.Hut);
        hut.transform.position=new Vector3(20,0,0);
        ((System.Collections.Generic.List<Building>)typeof(Outpost).GetField("built",Flags).GetValue(camp)).Add(hut);
        Call(worker,"TickRescue",hand,.01f);
        Gate(casualty.downed,"far or unreachable hut cannot revive remotely");
        worker.transform.position=CampWorker.WorkSpot(camp,hut);
        Call(worker,"TickRescue",hand,.01f);
        Gate(!casualty.downed && casualty.recovering && casualty.recoverAtHut,"actual arrival at hut starts recovery");
    }
    void CheckEquipmentLayout() {
        var root=SheetHost.Instance.GetComponent<UIDocument>().rootVisualElement;
        var scroll=root.Q<ScrollView>("villager-equipment-scroll");
        Debug.Log($"[DefenseLayout] screen={Screen.width}x{Screen.height} scroll={scroll?.layout} viewport={scroll?.contentViewport.layout} root={root.layout}");
        Gate(scroll!=null && scroll.contentViewport.layout.height>100f,"equipment has bounded scroll viewport");
        Gate(scroll.verticalScroller.highValue>0f,"all six equipment slots reachable by scrolling");
        foreach(var button in scroll.Query<Button>().ToList())
            Gate(button.resolvedStyle.height>=43f,"equipment touch target: "+button.text);
    }
    void Capture(string name,int width,int height) {
        var rt=new RenderTexture(width,height,24); cameraView.targetTexture=rt; cameraView.Render();
        var previous=RenderTexture.active; RenderTexture.active=rt;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
        System.IO.Directory.CreateDirectory("Logs/DefensePreview"); System.IO.File.WriteAllBytes("Logs/DefensePreview/"+name+".png",image.EncodeToPNG());
        RenderTexture.active=previous; cameraView.targetTexture=null; Destroy(image); Destroy(rt);
    }
}
#endif
