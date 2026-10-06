#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using SeaSick.Ship;
using SeaSick.Ship.Harpoon;
using SeaSick.Ship.Overboard;
using SeaSick.Combat;
using SeaSick.UI.Sheets;
using UnityEngine;
using UnityEngine.UIElements;

public class SailingHudPlayProbe : MonoBehaviour
{
    public static string Result;
    public static Action<bool> SetPhone;
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object o,string f)=>o.GetType().GetField(f,F).GetValue(o);
    static void Set(object o,string f,object v)=>o.GetType().GetField(f,F).SetValue(o,v);
    static void Call(object o,string f,params object[] args)=>o.GetType().GetMethod(f,F).Invoke(o,args);
    static void Gate(bool b,string what) { if(!b) throw new Exception(what); Debug.Log("[SailingCheck] PASS "+what); }
    IEnumerator Start()
    {
        SeaSick.Save.GameBoot.Skip();
        yield return new WaitForSecondsRealtime(3);
        while (SeaSick.UI.Menus.LoadingScreen.Instance != null && !SeaSick.UI.Menus.LoadingScreen.Instance.Finished)
            yield return null;
        try { SetupAndCheck(); } catch(Exception e) { Result="FAIL "+e; yield break; }
        yield return new WaitForSecondsRealtime(1);
        try { CheckHud(); CheckHold(); CheckEnemyTap(); ScreenCapture.CaptureScreenshot("Logs/sailing-quiet-phone.png"); } catch(Exception e) { Result="FAIL "+e; yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        Button compass = null;
        try {
            var layer=Layer(); compass=layer.Query<Button>().ToList().Find(b=>b.tooltip=="Open live sea chart");
            Gate(compass!=null,"compass button exists");
            using(var e=NavigationSubmitEvent.GetPooled()) { e.target=compass; compass.SendEvent(e); }
        } catch(Exception e) { Result="FAIL "+e; yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        try {
            Gate(QuietSailingHud.MapOpen,"compass opens paper chart");
            var map=Layer().Q<PaperSeaMap>(); Gate(Mathf.Abs(map.worldBound.width-map.worldBound.height)<2,"chart is square");
            Gate(Time.timeScale>0 && SeaHud.HelmShowing,"chart leaves sailing active");
            CheckHud(); ScreenCapture.CaptureScreenshot("Logs/sailing-map-phone.png");
        } catch(Exception e) { Result="FAIL "+e; yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        try {
            using(var e=NavigationSubmitEvent.GetPooled()) { e.target=compass; compass.SendEvent(e); }
        } catch(Exception e) { Result="FAIL "+e; yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        SetPhone(false);
        yield return new WaitForSecondsRealtime(.7f);
        try { CheckHud(); ScreenCapture.CaptureScreenshot("Logs/sailing-quiet-desktop.png"); } catch(Exception e) { Result="FAIL "+e; yield break; }
        yield return new WaitForSecondsRealtime(.5f);
        Result="PASS forward shot, swept salvage/raider hits, reel-stop-resume-cut, miss return, no lamp/swivel, phone/desktop hit regions, live square map";
    }
    VisualElement Layer()
    {
        foreach(var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        { var e=doc.rootVisualElement.Q("quiet-sailing-hud"); if(e!=null)return e; }
        throw new Exception("quiet HUD missing");
    }
    void CheckHud()
    {
        Gate(QuietSailingHud.Active,"quiet HUD active");
        Gate(SailingCompass.InView(0,0) && !SailingCompass.InView(180,0),"compass shows only forward sector");
        Gate(Mathf.Abs(SailingCompass.BearingX(15,0,132)-SailingCompass.BearingX(0,0,132)-10)<.01f,"compass bearing spacing is 180-degree scale");
        Gate(Mathf.Approximately(SailingCompass.BearingX(0,0,132),66),"north home aligns with north at centre");
        Gate(SailingCompass.BearingX(0,90,132)<66 && SailingCompass.BearingX(0,270,132)>66,
            "north marker moves with north as ship turns east/west");
        var r=QuietSailingHud.SteeringRect;
        Gate(!r.Overlaps(SeaHud.HarpoonRect) && !r.Overlaps(SeaHud.BoostRect),"steering clear of right controls");
        Gate(SeaStick.CanStartAt(new Vector2(r.center.x,Screen.height-r.center.y)),"steering can start");
        foreach(var rect in new[]{SeaHud.HarpoonRect,SeaHud.BoostRect})
        {
            var p=new Vector2(rect.center.x,Screen.height-rect.center.y);
            Gate(!SeaStick.CanStartAt(p) && !CombatLock.TapAllowedAt(p),"action tap cannot steer or lock");
        }
        var drawn=Layer().Q("quiet-harpoon").worldBound;
        float s=SheetHost.PanelScale;
        Gate(Vector2.Distance(drawn.center/s,SeaHud.HarpoonRect.center)<3,"harpoon drawn and hit rect agree");
    }
    void CheckEnemyTap()
    {
        var lk=CombatHud.Source; var cam=Camera.main;
        Gate(lk!=null && cam!=null,"combat tap has camera and lock source");
        var enemy=new GameObject("Tap target fixture").AddComponent<EnemyShip>(); enemy.enabled=false;
        HitTargets.Register(enemy);
        enemy.transform.position=cam.ViewportToWorldPoint(new Vector3(.7f,.45f,40))-Vector3.up*2.2f;
        Vector2 at=cam.WorldToScreenPoint(enemy.HitCentre);
        Call(lk,"FinishEnemyTap",at,at,Time.unscaledTime);
        Gate(ReferenceEquals(lk.Locked,enemy),"tap visible enemy locks");
        Call(lk,"FinishEnemyTap",at,at,Time.unscaledTime);
        Gate(lk.Locked==null,"tap same enemy releases");
        enemy.transform.position=cam.ViewportToWorldPoint(new Vector3(1.01f,.45f,40))-Vector3.up*2.2f;
        at=cam.WorldToScreenPoint(enemy.HitCentre);
        Call(lk,"FinishEnemyTap",at,at,Time.unscaledTime);
        Gate(lk.Locked==null,"offscreen enemy cannot be tapped");
        HitTargets.Unregister(enemy);DestroyImmediate(enemy.gameObject);
    }
    void CheckHold()
    {
        var gun=HarpoonGun.Player;
        var muzzle=(Transform)Get(gun,"muzzle");
        var load=FloatingCargo.Spawn(SeaSick.World.Res.Timber,1,gun.transform,muzzle.position+gun.transform.forward*20);
        load.enabled=false;
        Set(gun,"hooked",load); Call(gun,"Bite");
        var button=Layer().Q("quiet-harpoon");
        var ev=new Event { type=EventType.MouseDown,button=0,mousePosition=button.worldBound.center };
        using(var e=PointerDownEvent.GetPooled(ev)) { e.target=button;button.SendEvent(e); }
        var q=typeof(QuietSailingHud).GetField("current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        Gate((int)Get(q,"pointer")>=0,"harpoon takes pointer down");
        ev.type=EventType.MouseUp;
        using(var e=PointerUpEvent.GetPooled(ev)) { e.target=button;button.SendEvent(e); }
        Gate(gun.IsReeling && gun.State==HarpoonState.Hooked,"short button tap reels without cutting");
        ev.type=EventType.MouseDown;
        using(var e=PointerDownEvent.GetPooled(ev)) { e.target=button;button.SendEvent(e); }
        Set(q,"pressedAt",Time.unscaledTime-1f); ((QuietSailingHud)q).Tick("");
        Gate(gun.State==HarpoonState.Reloading && !load.BeingHauled,"held button cuts attached line");
        ev.type=EventType.MouseUp;
        using(var e=PointerUpEvent.GetPooled(ev)) { e.target=button;button.SendEvent(e); }
        Gate(!(bool)Get(gun,"pendingShot"),"release after cut never fires again");
        DestroyImmediate(load.gameObject);Call(gun,"TickReloading",6f);
    }
    void SetupAndCheck()
    {
        var gun=HarpoonGun.Player; Gate(gun!=null,"real player harpoon exists");
        var motor=gun.GetComponent<ShipMotor>(); var anchor=gun.GetComponent<AnchorController>();
        anchor.CastOff(); motor.Anchored=false;
        // Keep the sea rendering and UI live; isolate the mechanics from world drift.
        motor.enabled=false; var body=motor.GetComponent<Rigidbody>(); if(body!=null)body.isKinematic=true;
        anchor.enabled=false;
        var voyage=FindFirstObjectByType<SeaSick.Voyage.VoyageManager>(); voyage.BeginVoyage(); voyage.enabled=false;
        gun.enabled=false;
        // OnDisable clears Player; tests drive Update by reflection, then restore it.
        Call(gun,"OnEnable");
        Set(gun,"<Available>k__BackingField",true);
        var muzzle=(Transform)Get(gun,"muzzle"); var start=muzzle.position;
        Vector3 f=Vector3.ProjectOnPlane(gun.transform.forward,Vector3.up).normalized;
        var cargo=FloatingCargo.Spawn(SeaSick.World.Res.Timber,2,gun.transform,start+f*18);
        cargo.enabled=false;
        Set(gun,"target",cargo);
        gun.FireOrCut(); Call(gun,"Launch");
        Vector3 destination=(Vector3)Get(gun,"flyTo");
        Gate(Vector3.Dot(Vector3.ProjectOnPlane(destination-start,Vector3.up).normalized,f)>.9999f,"shot is straight ahead");
        Call(gun,"TickFlying",.5f);
        Gate(gun.State==HarpoonState.Hooked && !gun.IsReeling,"swept shot hits salvage and waits");
        float len=(float)Get(gun,"lineLen"); Call(gun,"TickHooked",.016f);
        Gate(Mathf.Abs((float)Get(gun,"lineLen")-len)<.001f,"unreeled rope does not shorten");
        gun.FireOrCut(); Gate(gun.IsReeling,"tap begins reeling");
        for(int i=0;i<60;i++)Call(gun,"TickHooked",.016f);
        Gate((float)Get(gun,"lineLen")<len,"existing winch shortens rope");
        gun.FireOrCut(); len=(float)Get(gun,"lineLen"); Call(gun,"TickHooked",.016f);
        Gate(!gun.IsReeling && gun.State==HarpoonState.Hooked && Mathf.Abs((float)Get(gun,"lineLen")-len)<.001f,"stop retains rope without reeling");
        gun.FireOrCut(); Gate(gun.IsReeling,"tap resumes"); gun.CutLine();
        Gate(gun.State==HarpoonState.Reloading && !cargo.BeingHauled,"cut frees salvage");
        DestroyImmediate(cargo.gameObject);
        Call(gun,"TickReloading",6f);
        var enemy=new GameObject("Harpoon test raider").AddComponent<EnemyShip>(); enemy.enabled=false;
        enemy.transform.position=start+f*24; HarpoonRegistry.Add(enemy);
        Call(gun,"Launch"); Call(gun,"TickFlying",.7f);
        Gate(gun.State==HarpoonState.Hooked && enemy.HarpoonHeld,"raider can be hooked");
        gun.FireOrCut(); for(int i=0;i<30;i++)Call(gun,"TickHooked",.016f);
        Gate(enemy.Alive && gun.State==HarpoonState.Hooked,"raider stays a ship, never collected as cargo");
        gun.CutLine(); Gate(!enemy.HarpoonHeld,"cut releases raider");
        HarpoonRegistry.Remove(enemy); DestroyImmediate(enemy.gameObject); Call(gun,"TickReloading",6f);
        Call(gun,"Launch"); Call(gun,"TickFlying",2f);
        Gate(gun.State==HarpoonState.Returning,"empty forward shot misses and returns"); Call(gun,"TickReturning",3f);
        Set(gun,"target",null); Call(gun,"TickMount",.1f);
        var swivel=(Transform)Get(gun,"swivel"); Gate(Quaternion.Angle(swivel.localRotation,Quaternion.identity)<.1f,"barrel does not swivel");
        var mount=(Transform)Get(gun,"mount"); foreach(var l in mount.GetComponentsInChildren<Light>(true))Gate(!l.isActiveAndEnabled,"aim lamp disabled");
        gun.enabled=true;
    }
}
#endif
