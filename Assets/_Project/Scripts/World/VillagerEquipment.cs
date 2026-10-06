using System;
using UnityEngine;

namespace SeaSick.World
{
    public enum EquipmentSlot { MainHand, OffHand, Helmet, UpperBody, Pants, Shoes }

    [Serializable]
    public class VillagerEquipment
    {
        public string mainHand, offHand, helmet, upperBody, pants, shoes;
        public int arrows;
        public const int QuiverCapacity = 12;
        public string Get(EquipmentSlot slot) => slot switch {
            EquipmentSlot.MainHand => mainHand, EquipmentSlot.OffHand => offHand,
            EquipmentSlot.Helmet => helmet, EquipmentSlot.UpperBody => upperBody,
            EquipmentSlot.Pants => pants, _ => shoes };
        public void Set(EquipmentSlot slot, string id) {
            switch(slot) {
                case EquipmentSlot.MainHand: mainHand=id; break;
                case EquipmentSlot.OffHand: offHand=id; break;
                case EquipmentSlot.Helmet: helmet=id; break;
                case EquipmentSlot.UpperBody: upperBody=id; break;
                case EquipmentSlot.Pants: pants=id; break;
                case EquipmentSlot.Shoes: shoes=id; break;
            }
        }
        public bool Armed => Fits(EquipmentSlot.MainHand, mainHand);
        public static bool Fits(EquipmentSlot slot, string id) {
            if (string.IsNullOrEmpty(id)) return false;
            return slot switch {
                EquipmentSlot.MainHand => id == Res.Spear || id == Res.IronSpear || id == Res.Bow,
                EquipmentSlot.OffHand => id == "WoodShield",
                EquipmentSlot.Helmet => id == "LeatherHelmet",
                EquipmentSlot.UpperBody => id == "LeatherVest",
                EquipmentSlot.Pants => id == "LeatherPants",
                EquipmentSlot.Shoes => id == "LeatherBoots", _ => false };
        }
        public float Protection => (helmet == "LeatherHelmet" ? .08f : 0f)
            + (upperBody == "LeatherVest" ? .18f : 0f)
            + (pants == "LeatherPants" ? .09f : 0f)
            + (shoes == "LeatherBoots" ? .05f : 0f);
    }

    public partial class OutpostLedger
    {
        // Atomic stock ownership: one equipped item is removed from stock once.
        // Returns bypass the storage ceiling like recalled carried loads.
        public bool Equip(OutpostHand h, EquipmentSlot slot, string id, out string why)
        {
            why = null;
            if (h == null || !hands.Contains(h)) { why="Villager unavailable"; return false; }
            if (h.Busy || h.huntArmed) { why="Wait until this villager is free"; return false; }
            var e = h.equipment ?? (h.equipment = new VillagerEquipment());
            if (string.IsNullOrEmpty(id)) {
                ReturnEquipment(e.Get(slot)); e.Set(slot,null);
                if(slot == EquipmentSlot.MainHand) { ReturnEquipment(Res.Arrows,e.arrows); e.arrows=0; }
                return true;
            }
            if (!VillagerEquipment.Fits(slot,id)) { why="Wrong equipment slot"; return false; }
            if (slot == EquipmentSlot.OffHand && e.mainHand == Res.Bow) { why="Bow uses both hands"; return false; }
            if (e.Get(slot) == id) return true;
            if (TakeFromStore(id,1) != 1) { why="None in the store"; return false; }
            ReturnEquipment(e.Get(slot)); e.Set(slot,id);
            if (slot == EquipmentSlot.MainHand) {
                if (id == Res.Bow) { ReturnEquipment(e.offHand); e.offHand=null; ReloadQuiver(h); }
                else { ReturnEquipment(Res.Arrows,e.arrows); e.arrows=0; }
            }
            return true;
        }
        public bool OrderRescue(OutpostHand down) {
            if(down==null || !hands.Contains(down) || !down.downed) return false;
            foreach(var h in hands) if(h!=null && h.rescuing==down.name) return true;
            OutpostHand best=null; float d=float.MaxValue;
            foreach(var h in hands) {
                if(h==null || h==down || h.downed || h.recovering || h.dragged || !string.IsNullOrEmpty(h.rescuing)) continue;
                if (Underground(h) || SeaSick.Combat.RaidAlarm.IsPostedLookout(h)) continue;
                float n=(HandAt(h)-HandAt(down)).sqrMagnitude;
                if(n<d) { best=h; d=n; }
            }
            if(best==null) return false;
            DropCarriedLoadNow(best);
            best.rescuing=down.name; best.defending=false; best.alarmed=false;
            best.fetchingSpear=best.hidingHut=best.hidingCrouch=false;
            down.reached=false; down.dragged=false;
            return true;
        }
        void ReturnEquipment(string id, int n=1) { if(!string.IsNullOrEmpty(id) && n>0) Store(id,true).whole += n; }
        public int ReloadQuiver(OutpostHand h) {
            var e=h?.equipment;
            if(e==null || e.mainHand!=Res.Bow) return 0;
            int n=TakeFromStore(Res.Arrows,Mathf.Max(0,VillagerEquipment.QuiverCapacity-e.arrows));
            e.arrows+=n; return n;
        }
        public void ReleaseEquipment(OutpostHand h, bool onGround) {
            var e=h?.equipment; if(e==null) return;
            foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot))) {
                string id=e.Get(slot);
                if(!string.IsNullOrEmpty(id)) {
                    if(onGround) DropRaiderLoot(id,1,HandAt(h)); else ReturnEquipment(id);
                    e.Set(slot,null);
                }
            }
            if(onGround) DropRaiderLoot(Res.Arrows,e.arrows,HandAt(h)); else ReturnEquipment(Res.Arrows,e.arrows);
            e.arrows=0;
        }
    }
}
