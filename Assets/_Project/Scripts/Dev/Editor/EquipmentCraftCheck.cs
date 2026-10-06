using System;
using UnityEngine;
using SeaSick.World;
using SeaSick.World.Economy;
public static class EquipmentCraftCheck
{
    static void Gate(bool ok,string label) { if(!ok)throw new Exception(label);Debug.Log("[EquipmentCraft] PASS "+label); }
    public static void Run()
    {
        foreach(string id in new[]{"LeatherHelmet","LeatherVest","LeatherPants","LeatherBoots","WoodShield","IronHelmet","IronArmor","IronPants","IronBoots","IronSword","IronShield"})
        {
            var r=Recipes.Named(id);bool iron=id.StartsWith("Iron");
            Gate(r!=null && r.station==(iron?"Blacksmith":"Fletcher"),id+" station");
            var l=new OutpostLedger{ceilingPer=1000,stationsMigrated=true,campfireLevel=2};
            l.SetCentre(Vector3.zero);l.raised.Add(new BuiltBuilding{planId=r.station,x=15});l.built.Add(r.station);
            l.hands.Add(new OutpostHand{name="Crafter",order=OutpostOrder.Work,target=r.station,wHas=true,wx=15});
            l.Store(Res.Food,true).whole=1000;l.lastTicked=0;l.EnsureStations();var st=l.StationOf(r.station);
            foreach(var cost in r.takes)st.Bay(cost.res,true).whole=cost.n;
            int spot=iron?StationSpots.IndexOf(r.station,"Forge"):0;
            Gate(l.SelectRecipe(st,spot,id,out var why),id+" selectable: "+why);
            for(int i=1;i<=100;i++)l.Tick(i*.05*TimeOfDay.WorkDaySeconds+.001);
            Gate(l.CountOf(r.makes)+l.CarriedOf(r.makes)==1,id+" actually produced exactly once");
            foreach(var cost in r.takes)Gate(l.CountOf(cost.res)+l.CarriedOf(cost.res)==0,id+" consumed "+cost.res);
            var saved=JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(l));
            Gate(saved.CountOf(r.makes)+saved.CarriedOf(r.makes)==1,id+" output saved");
            if(iron){l.campfireLevel=1;Gate(!l.SelectRecipe(st,spot,id,out _),id+" requires fire II");}
        }
        foreach(var state in new[]{BenchState.Working,BenchState.Finished})
        {
            var st=new StationStock{planId="Blacksmith"};st.EnsureSpotRows();var sp=st.Spots[StationSpots.IndexOf(st.planId,"Forge")];
            sp.recipeId="LeatherVest";sp.benchRecipe="LeatherVest";sp.benchState=state;sp.benchOut=state==BenchState.Finished?1:0;
            st.EnsureSpotRows();st.EnsureSpotRows();
            Gate(!sp.Selected && sp.benchState==BenchState.Empty,"old leather order cleared");
            Gate(state==BenchState.Working?st.Bay(Res.Hide,true).whole==3:st.Rack("LeatherVest",true).whole==1,"migration preserves stock exactly once");
        }
        var gear=new VillagerEquipment{mainHand="IronSword",offHand="IronShield",helmet="IronHelmet",upperBody="IronArmor",pants="IronPants",shoes="IronBoots"};
        Gate(gear.Armed && Mathf.Abs(gear.Protection-.6f)<.001f && gear.ShieldBlockChance==.55f,"iron equipment combat stats");
        var ledger=new OutpostLedger();var h=new OutpostHand{name="Knight"};ledger.hands.Add(h);
        foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot))) {var id=gear.Get(slot);ledger.Store(id,true).whole=1;Gate(ledger.Equip(h,slot,id,out _),"equip "+id);}
        ledger.Store(Res.Bow,true).whole=1;Gate(ledger.Equip(h,EquipmentSlot.MainHand,Res.Bow,out _) && ledger.StoreCountOf("IronSword")==1 && ledger.StoreCountOf("IronShield")==1,"bow returns sword and iron shield");
    }
}
