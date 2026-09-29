using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// The approved fixed-beam, two-level boat. Art revisions have their own IDs.
    public static class CoasterFamily
    {
        public static bool Is(string id) => id != null && id.Contains(".f30.");
        public static bool Is(ShipConfiguration c) => c != null && Is(c.sternId);
        public static bool Raised(string id) => Is(id) && id.Contains(".raised.");
        public static string Hull(string kind, bool raised) => "hull." + kind.ToLowerInvariant() + ".f30." + (raised ? "raised" : "low") + ".v1";
        public static void Wheel(ShipConfiguration c)
        {
            string level = Raised(c.sternId) ? "raised" : "low";
            c.rotorId = "wheel.rotor.f30." + level; c.carrierId = "wheel.carrier.f30." + level;
        }
        public static ShipConfiguration Default()
        {
            var c = new ShipConfiguration { sternId = Hull("stern", true), bowId = Hull("bow", false) };
            c.middleIds.Add(Hull("middle", false)); Wheel(c);
            foreach (string key in SlotModel.SectionKeys(c))
                foreach (int side in new[] { -1, 1 }) c.equipment.Add(new EquipmentChoice { slotId = key + "/Gun_0_" + side, moduleId = ShipConfiguration.EquipmentCannon });
            return c;
        }
        /// **The new-game boat** (Kevin, 2026-09-29: "the base version of the
        /// ship (nose and butt of ship, no middle section and only one
        /// floor)"). Low stern + low bow, no middle, one deck. The low stern
        /// has no gun ports, so her one pair sits on the bow.
        public static ShipConfiguration Base()
        {
            var c = new ShipConfiguration { sternId = Hull("stern", false), bowId = Hull("bow", false) };
            Wheel(c);
            foreach (int side in new[] { -1, 1 })
                c.equipment.Add(new EquipmentChoice { slotId = ShipAssembler.StdKeyBow + "/Gun_0_" + side, moduleId = ShipConfiguration.EquipmentCannon });
            return c;
        }
        public static (string sternId, string[] middleIds, string bowId) ToIds(DeckLevel stern, IList<DeckLevel> middles, DeckLevel bow)
        {
            var ids = new string[middles.Count];
            for (int i=0;i<ids.Length;i++) ids[i]=Hull("middle",middles[i]==DeckLevel.Raised);
            return (Hull("stern",stern==DeckLevel.Raised),ids,Hull("bow",bow==DeckLevel.Raised));
        }
        public static ShipConfiguration Upgrade(ShipConfiguration old, ModuleLibrary lib, out List<string> overflow)
        {
            overflow = new List<string>();
            if (Is(old)) return old.Clone();
            if (old == null) return Base();
            if (old.UsesSlots) old = SlotModel.Normalized(old,lib);
            var c = new ShipConfiguration { sternId=Hull("stern",true), bowId=Hull("bow",RaisedSections.LevelOf(old.bowId)==DeckLevel.Raised) };
            foreach (var id in old.middleIds) c.middleIds.Add(Hull("middle",RaisedSections.LevelOf(id)==DeckLevel.Raised));
            Wheel(c);
            foreach(var key in SlotModel.SectionKeys(c))
            {
                var guns = old.equipment.FindAll(e=>e?.slotId != null && (e.slotId.StartsWith(key+"/") || e.slotId.StartsWith("fitting:"+key+"/")));
                int capacity = key=="stern" ? 2 : Raised(SlotModel.HullIdOf(c,key)) ? 4 : 2;
                for(int i=0;i<guns.Count;i++)
                    if(i<capacity) c.equipment.Add(new EquipmentChoice {slotId=key+"/Gun_"+(i/2)+"_"+(i%2==0?-1:1),moduleId=guns[i].moduleId});
                    else overflow.Add(guns[i].moduleId);
            }
            if(old.UsesSlots && old.fits!=null && lib.Catalog!=null)
                foreach(var fit in old.fits) if(lib.Catalog.TryGet(fit.moduleId,out var module) && !module.IsCannon) overflow.Add(fit.moduleId);
            return c;
        }
        public static void ConfigurePlan(ShipyardPlan p)
        {
            if(!Is(p.config)) return;
            bool high=Raised(p.config.sternId);
            var d=p.data; float offset=p.viewOffset.z;
            d.wheelRadius=(high?3.35f:2.03f)*.5f; d.wheelWidth=2.54f;
            d.wheelAxle=new Vector3(0,(high?1.65f:.33f)*.5f,offset+.06f);
            d.helm=new Vector3(0,(high?7.36f:2.96f)*.5f,offset+1.10f);
            d.funnelTopY=d.helm.y+2f;
            foreach(var station in d.stations)
            {
                float x=(station.z-offset)*2;
                station.deckY=DeckY(p.config,x);
            }
        }
        public static float DeckY(ShipConfiguration c,float x)
        {
            if(x<7.2f) return (Raised(c.sternId)?7.36f:2.96f)*.5f;
            if(x<9.8f)return .88f;
            x-=9.8f;
            foreach(var id in c.middleIds) { if(x<6)return Raised(id)?3.08f:.88f;x-=6; }
            return Raised(c.bowId)?3.08f:(x<1.8f?.88f:1.055f);
        }
    }
}
