using System;
using System.IO;
using System.Linq;
using SeaSick.Ship.Modular;
using SeaSick.UI.ModularYard;

static class DraftTests
{
    static int count;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    sealed class Backend : IShipyardRefit
    {
        public ShipConfiguration current = ShipConfiguration.Long();
        public string rejection;
        public int applied;
        public ShipConfiguration ReadCurrent() => current.Clone();
        public string Validate(ShipConfiguration draft) => rejection;
        public bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason)
        {
            reason = !expected.ValueEquals(current) ? "Ship changed during preview." : rejection;
            if (reason != null) return false;
            current = draft.Clone(); applied++; return true;
        }
    }
    static int Main(string[] args)
    {
        var dir = Path.Combine(args[0], "Assets/_Project/Resources/ShipModules");
        var lib = ModuleLibrary.FromJson(File.ReadAllText(Path.Combine(dir,"standards.json")),
            Directory.GetFiles(Path.Combine(dir,"Modules"),"*.json").Select(File.ReadAllText));
        Check(lib.Ok,"library loads");
        var restricted = new ShipyardDraft(lib, ShipConfiguration.Long(), allowed: (kind,id) => false);
        Check(!restricted.AddMiddle() && restricted.Count == 1, "backend allowlist blocks unavailable section");
        Check(!restricted.ChooseWheel(ShipConfiguration.TimberRotor), "backend allowlist blocks unavailable wheel");
        var backend = new Backend(); var initial = backend.ReadCurrent();
        var d = new ShipyardDraft(lib,initial,backend,(config,index)=>null);
        Check(!d.Dirty && !d.CanUndo,"clean initial draft");
        Check(d.AddMiddle() && d.Count == 2,"add section");
        Check(d.Highlight == "middle[1]","highlight inserted section");
        Check(Math.Abs(d.Assembly.overallLengthM-d.OriginalLength-3f)<.001f,"one bay adds three metres");
        Check(initial.middleIds.Count==1 && backend.current.middleIds.Count==1,"preview does not mutate input or live ship");
        var copy=d.Snapshot(); copy.middleIds.Clear(); Check(d.Count==2,"snapshot is defensive");
        d.Undo(); Check(!d.Dirty && d.Count==1,"undo restores original");
        Check(d.RemoveMiddle() && d.Count==0,"shortest ship");
        Check(!d.RemoveMiddle() && d.Count==0,"cannot remove bow or stern");
        d.AddMiddle(); d.AddMiddle(); d.AddMiddle();
        Check(!d.AddMiddle() && d.Count==3,"maximum length");
        Check(d.ChooseWheel(ShipConfiguration.TimberRotor),"compatible wheel selection");
        Check(!d.ChooseWheel(ShipConfiguration.OversizedRotor),"oversized wheel unavailable");
        Check(d.Rotor==ShipConfiguration.TimberRotor,"rejection preserves valid draft");
        backend.rejection="Not in port";
        Check(!d.Confirm() && backend.applied==0,"backend rejection does not commit");
        backend.rejection=null; backend.current=ShipConfiguration.Short();
        Check(!d.Confirm() && backend.applied==0,"stale baseline refused");
        backend.current=initial.Clone();
        Check(d.Confirm() && backend.applied==1,"confirmation commits once");
        Check(!d.Confirm() && backend.applied==1,"repeat confirmation refused");
        Check(!d.RemoveMiddle() && !d.CanUndo,"committed draft is locked");
        var isolated=new ShipyardDraft(lib,ShipConfiguration.Long()); isolated.AddMiddle();
        Check(!isolated.Confirm(),"preview without backend cannot confirm");
        var occupied=ShipConfiguration.Long();
        occupied.equipment.Add(new EquipmentChoice { slotId="middle[0]/DeckSlot_0_1", moduleId="equipment.cannon.placeholder" });
        var occupiedDraft=new ShipyardDraft(lib,occupied);
        Check(!occupiedDraft.RemoveMiddle() && occupiedDraft.Count==1,"occupied bay cannot be deleted");
        Check(occupiedDraft.AddMiddle(),"can extend equipped ship");
        Check(occupiedDraft.Snapshot().equipment[0].slotId=="middle[0]/DeckSlot_0_1","extension preserves equipment reference");
        var noReport=new ShipyardDraft(lib,initial,backend);
        Check(!noReport.RemoveMiddle(),"live removal requires authoritative report");
        int reportedIndex=-1;
        var cargoBlocked=new ShipyardDraft(lib,initial,backend,(config,index)=>{
            reportedIndex=index; config.middleIds.Clear(); return "Cargo occupies this section.";
        });
        Check(!cargoBlocked.RemoveMiddle() && cargoBlocked.Count==1 && reportedIndex==0,"backend occupancy controls removal with defensive copy");
        Console.WriteLine("Shipyard draft: " + count + " checks passed"); return 0;
    }
}
