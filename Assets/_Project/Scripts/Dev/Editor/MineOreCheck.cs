using System;
using UnityEditor;
using UnityEngine;
using SeaSick.World;
using SeaSick.World.Economy;
public static class MineOreCheck
{
    static void Gate(bool ok,string label){if(!ok)throw new Exception(label);Debug.Log("[MineOre] PASS "+label);}
    public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    static void Run()
    {
        Gate(MineSelfTest.Run(),"existing stone mining regression");
        Gate(RecipeGraph.Validate().Ok,"recipe graph");
        var l=new OutpostLedger{campfireLevel=1,ceilingPer=1000,stationsMigrated=true};
        l.SetCentre(Vector3.zero);l.raised.Add(new BuiltBuilding{planId="Mine",x=15,yaw=270});l.built.Add("Mine");
        l.hands.Add(new OutpostHand{name="Miner",order=OutpostOrder.Work,target="Mine"});
        l.Store(Res.Food,true).whole=1000;l.lastTicked=0;l.EnsureStations();var mine=l.StationOf("Mine");
        Gate(!l.SelectRecipe(mine,0,"mine-ore",out _),"ore locked at fire I");
        l.campfireLevel=2;Gate(l.SelectRecipe(mine,0,"mine-ore",out _),"ore available at fire II in level-one mine");
        double now=0;bool underground=false,carried=false;
        for(int i=0;i<2500 && l.StoreCountOf(Res.Ore)<2;i++){
            now+=.36;l.Tick(now+.001);var h=l.hands[0];underground|=l.Underground(h);carried|=h.Hauling && h.haulPicked && h.haulRes==Res.Ore;
        }
        Gate(underground && carried && l.StoreCountOf(Res.Ore)>=2,"ore dug underground, carried out and delivered to store");
        Gate(l.CountOf(Res.Stone)==0,"ore selection not overwritten by auto-start stone");
        var saved=JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(l));saved.EnsureStations();
        Gate(saved.StationOf("Mine").OrderRecipe?.id=="mine-ore" && saved.CountOf(Res.Ore)==l.CountOf(Res.Ore),"ore selection and stock survive save/load");
        l=saved;l.raised.Add(new BuiltBuilding{planId="Blacksmith",x=5});l.built.Add("Blacksmith");
        l.hands.Add(new OutpostHand{name="Smith",order=OutpostOrder.Work,target="Blacksmith",wHas=true,wx=5});l.EnsureStations();
        Gate(l.SelectRecipe(l.StationOf("Blacksmith"),StationSpots.IndexOf("Blacksmith","Smelter"),"iron",out _),"forge accepts iron order");
        for(int i=0;i<3000 && l.CountOf(Res.Iron)+l.CarriedOf(Res.Iron)<1;i++){now+=.36;l.Tick(now+.001);}
        Gate(l.CountOf(Res.Iron)+l.CarriedOf(Res.Iron)>=1,"mined ore hauled into forge and smelted into iron");
        Gate(l.SelectRecipe(l.StationOf("Mine"),0,"mine-stone",out _),"can switch back to stone");
        for(int i=0;i<1000 && l.CountOf(Res.Stone)<4;i++){now+=.36;l.Tick(now+.001);}
        Gate(l.CountOf(Res.Stone)+l.CarriedOf(Res.Stone)>=4,"stone production resumes after ore");
    }
}
