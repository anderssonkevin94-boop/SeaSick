using System;
using System.IO;
using System.Reflection;
using SeaSick.World;
using SeaSick.World.Economy;
using SeaSick.Combat;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class VillageDefenseCheck
{
    static int count;
    static void Gate(bool yes,string name) { if(!yes) throw new Exception("FAIL: "+name); count++; Debug.Log("[DefenseCheck] PASS "+name); }
    public static void RunBatch() {
        try { Run(); EditorApplication.Exit(0); }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Run() {
        count=0;
        var l=new OutpostLedger(); var h=new OutpostHand { name="Defense test" }; l.hands.Add(h);
        l.Store(Res.Spear,true).whole=1; l.Store(Res.Bow,true).whole=1;
        l.Store("WoodShield",true).whole=1; l.Store(Res.Arrows,true).whole=20;
        Gate(l.Equip(h,EquipmentSlot.MainHand,Res.Spear,out _) && l.StoreCountOf(Res.Spear)==0,"spear removed from stock");
        Gate(l.Equip(h,EquipmentSlot.OffHand,"WoodShield",out _),"one-hand spear permits shield");
        Gate(l.Equip(h,EquipmentSlot.MainHand,Res.Bow,out _) && h.equipment.offHand==null && l.StoreCountOf("WoodShield")==1 && l.StoreCountOf(Res.Spear)==1,"bow returns shield and old spear atomically");
        Gate(h.equipment.arrows==12 && l.StoreCountOf(Res.Arrows)==8,"quiver conserves arrows");
        Gate(!l.Equip(h,EquipmentSlot.OffHand,"WoodShield",out _) && l.StoreCountOf("WoodShield")==1,"bow rejects off-hand without spending it");
        Gate(!l.Equip(h,EquipmentSlot.MainHand,Res.IronSpear,out _) && h.equipment.mainHand==Res.Bow,"missing stock leaves current gear unchanged");
        Gate(!l.Equip(h,EquipmentSlot.Shoes,Res.Spear,out _),"wrong slot rejected");
        var saved=JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(l));
        Gate(saved.hands[0].equipment.mainHand==Res.Bow && saved.hands[0].equipment.arrows==12 && saved.StoreCountOf(Res.Bow)==0,"save/load has exactly one owner");
        var old=JsonUtility.FromJson<OutpostHand>("{\"name\":\"Old save\"}");
        Gate(old.equipment==null || !old.equipment.Armed,"old save has no invented equipment");
        Gate(l.Equip(h,EquipmentSlot.MainHand,null,out _) && l.StoreCountOf(Res.Arrows)==20 && l.StoreCountOf(Res.Bow)==1,"unequip returns bow and arrows");
        foreach(var slot in new[]{EquipmentSlot.Helmet,EquipmentSlot.UpperBody,EquipmentSlot.Pants,EquipmentSlot.Shoes}) {
            string id=slot==EquipmentSlot.Helmet ? "LeatherHelmet" : slot==EquipmentSlot.UpperBody ? "LeatherVest" : slot==EquipmentSlot.Pants ? "LeatherPants" : "LeatherBoots";
            l.Store(id,true).whole=1; Gate(l.Equip(h,slot,id,out _),"equip "+slot);
        }
        Gate(Mathf.Abs(h.equipment.Protection-.4f)<.001f,"armor combines to forty percent reduction");
        for(int i=0;i<4;i++) l.HitDefender(h,3);
        Gate(!h.downed,"armor survives four ordinary hits"); l.HitDefender(h,3); Gate(h.downed,"fifth hit downs armored hand");
        Gate(!l.Equip(h,EquipmentSlot.MainHand,Res.Spear,out _),"downed villager cannot swap equipment");
        h.reached=true; h.dragged=true; float before=h.downedLeft; l.TickDowned(1f);
        Gate(h.downedLeft<before,"bleeding continues during dragging");
        var copy=JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(l));
        Gate(copy.hands[0].downed && copy.hands[0].dragged && copy.hands[0].downedLeft==h.downedLeft,"mid-drag state survives save/load");
        l.Revive(h); l.Equip(h,EquipmentSlot.MainHand,Res.Spear,out _);
        l.RemoveHand(h); Gate(l.StoreCountOf(Res.Spear)==1 && h.equipment.mainHand==null,"boarding returns personal equipment to camp stock");
        var casualty=new OutpostHand{name="Casualty test"}; var rescuer=new OutpostHand{name="Rescuer test"};
        l.hands.Add(casualty); l.hands.Add(rescuer); l.Down(casualty,"Raid");
        Gate(l.OrderRescue(casualty) && rescuer.rescuing==casualty.name,"manual rescue assigns one person");
        casualty.reached=true; casualty.dragged=true; l.Down(rescuer,"Raid");
        Gate(string.IsNullOrEmpty(rescuer.rescuing),"downed rescuer releases casualty");
        casualty.downedLeft=.1f; l.TickDowned(.2f);
        Gate(!l.hands.Contains(casualty),"bleed-out permanently removes casualty even during drag");
        var recipe=RecipeGraph.Validate(); Gate(recipe.Ok,"resource and recipe graph: "+recipe);
        // A synthetic terrain ridge is sufficient to distinguish blocked sight
        // without relying on the user's live settlement or save file.
        var go=new GameObject("Sight fixture"); var camp=go.AddComponent<Outpost>();
        var height=typeof(Outpost).GetField("height",BindingFlags.NonPublic|BindingFlags.Instance);
        height.SetValue(camp,new Func<float,float,float>((x,z)=>0f));
        Gate(VillageDefense.ClearSight(camp,new Vector3(0,1.3f,0),new Vector3(10,1.3f,0)),"clear ground visible");
        height.SetValue(camp,new Func<float,float,float>((x,z)=>x>4f && x<6f ? 3f : 0f));
        Gate(!VillageDefense.ClearSight(camp,new Vector3(0,1.3f,0),new Vector3(10,1.3f,0)),"ridge blocks ground observer");
        Gate(VillageDefense.ClearSight(camp,new Vector3(0,8f,0),new Vector3(10,1.3f,0)),"tower height sees over ridge");
        UnityEngine.Object.DestroyImmediate(go);
        var islandObject = new GameObject("Landing fixture");
        var landingCamp = islandObject.AddComponent<Outpost>();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(Outpost).GetField("site", flags).SetValue(landingCamp, islandObject.AddComponent<Settlement>());
        typeof(Outpost).GetField("ledger", flags).SetValue(landingCamp, new OutpostLedger());
        height.SetValue(landingCamp, new Func<float,float,float>((x,z)=>Mathf.Clamp((40f-Mathf.Sqrt(x*x+z*z))*.2f,-8f,3f)));
        float prior = 0;
        for (int i=0; i<6; i++) {
            Gate(landingCamp.BestLanding(new Vector3(0,3,0),new Vector3(5,3,0),out var shore,out var water,out float bearing,out _,out _),"valid shore approach " + i);
            Gate(landingCamp.GroundAt(shore)>=0f && landingCamp.GroundAt(water)<=-5f,"landing stays on land with hull in deep water");
            if (i>0) Gate(Mathf.Abs(Mathf.DeltaAngle(prior,bearing))>=65f,"successive raids vary approach sector");
            prior=bearing;
        }
        UnityEngine.Object.DestroyImmediate(islandObject);
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/village-defense-check.txt",count+" defense gates passed");
        Debug.Log("[DefenseCheck] "+count+" gates passed");
    }
}
