using System;
using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;
namespace SeaSick.UI.Sheets
{
    public sealed class EquipmentSheet : ISheetFramed
    {
        readonly Outpost camp; readonly string who;
        OutpostHand Hand => camp != null ? camp.HandNamed(who) : null;
        readonly Label[] labels=new Label[6];
        Label notice, ammo;
        Button reload;
        readonly List<(Button button, EquipmentSlot slot, string item)> choices = new();
        public EquipmentSheet(Outpost camp,string who) { this.camp=camp; this.who=who; }
        public string Title => who + " · Equipment";
        public Vector3 AnchorWorld => camp != null ? camp.CampCentre : Vector3.zero;
        public Color Accent => MidnightLandHud.Ice;
        public bool WantsTallSheet => true;
        public bool StillValid => Hand != null;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int i) {}
        public VisualElement BuildHeader() {
            var header = StationPage.Root("equipment-header");
            var title = StationPage.Text(Title,"st-title");
            title.style.fontSize=20;
            header.Add(title);
            return header;
        }
        public VisualElement BuildActions() {
            var back = new Button(()=>Sheets.Open(new HandSheet(camp,who))) { text="Back to villager" };
            back.AddToClassList("st-btn"); back.style.minHeight=48;
            var actions = StationPage.Root("equipment-actions");
            actions.Add(back);
            return actions;
        }
        public VisualElement Build() {
            choices.Clear();
            var page = StationPage.Root("st-page");
            StationPage.FitToParent(page);
            var root = new ScrollView(ScrollViewMode.Vertical) { name="villager-equipment-scroll" };
            root.AddToClassList("st-scroll");
            root.horizontalScrollerVisibility=ScrollerVisibility.Hidden;
            root.verticalScrollerVisibility=ScrollerVisibility.Hidden;
            root.touchScrollBehavior=ScrollView.TouchScrollBehavior.Clamped;
            page.Add(root);
            notice=StationPage.Text("Equipped items come from your store. Bows use both hands.","st-sub"); root.Add(notice);
            string[] names={"Main hand","Off hand","Helmet","Upper body","Pants","Shoes"};
            for(int i=0;i<6;i++) {
                var slot=(EquipmentSlot)i;
                labels[i]=StationPage.Text(names[i],"st-title");
                labels[i].style.fontSize=20; labels[i].style.marginTop=12;
                root.Add(labels[i]);
                foreach(var d in ResDefs.All) if(VillagerEquipment.Fits(slot,d.id)) {
                    string id=d.id;
                    var button=new Button(()=>Change(slot,id)) {text=d.label};
                    button.AddToClassList("st-btn"); button.style.minHeight=48; root.Add(button);
                    choices.Add((button,slot,id));
                }
                var clear=new Button(()=>Change(slot,null)) {text="Unequip"}; clear.AddToClassList("st-btn"); clear.style.minHeight=44; root.Add(clear);
                choices.Add((clear,slot,null));
            }
            ammo=StationPage.Text("","st-sub"); root.Add(ammo);
            reload=new Button(()=> { if(Hand!=null && !Hand.Busy) camp.Ledger.ReloadQuiver(Hand); Refresh(); }) {text="Fill quiver from store"};
            reload.AddToClassList("st-btn"); reload.style.minHeight=48; root.Add(reload);
            foreach (var child in root.Children()) child.style.flexShrink=0f;
            Refresh(); return page;
        }
        void Change(EquipmentSlot slot,string id) {
            if(!camp.Ledger.Equip(Hand,slot,id,out var why)) notice.text=why;
            else notice.text="Equipment updated";
            Refresh();
        }
        public void Refresh() {
            var h=Hand; if(h==null) return;
            var e=h.equipment ?? new VillagerEquipment();
            bool free=!h.Busy && !h.huntArmed;
            foreach(var choice in choices) {
                bool equipped=!string.IsNullOrEmpty(choice.item) && e.Get(choice.slot)==choice.item;
                int stock=string.IsNullOrEmpty(choice.item) ? 0 : camp.Ledger.StoreCountOf(choice.item);
                choice.button.text=string.IsNullOrEmpty(choice.item) ? "Unequip"
                    : ResDefs.Label(choice.item)+(equipped ? " · equipped" : " · "+stock+" in store");
                bool allowed=string.IsNullOrEmpty(choice.item) ? !string.IsNullOrEmpty(e.Get(choice.slot))
                    : !equipped && stock>0 && !(choice.slot==EquipmentSlot.OffHand && e.mainHand==Res.Bow);
                choice.button.SetEnabled(free && allowed);
                choice.button.style.opacity=free && allowed ? 1f : .55f;
            }
            reload?.SetEnabled(free && e.mainHand==Res.Bow && e.arrows<VillagerEquipment.QuiverCapacity && camp.Ledger.ArrowsHeld);
            string[] names={"Main hand","Off hand","Helmet","Upper body","Pants","Shoes"};
            for(int i=0;i<6;i++) if(labels[i]!=null) {
                string id=e.Get((EquipmentSlot)i);
                labels[i].text=names[i]+" · "+(i==1 && e.mainHand==Res.Bow ? "Bow · two-handed" : string.IsNullOrEmpty(id) ? "Empty" : ResDefs.Label(id));
            }
            if(ammo!=null) ammo.text=$"Arrows {e.arrows}/{VillagerEquipment.QuiverCapacity} · Store {camp.Ledger.StoreCountOf(Res.Arrows)}";
        }
    }
}
