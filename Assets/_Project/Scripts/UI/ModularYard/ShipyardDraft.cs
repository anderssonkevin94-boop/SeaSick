using System;
using System.Collections.Generic;
using SeaSick.Ship.Modular;

namespace SeaSick.UI.ModularYard
{
    // Adapter boundary: implementation belongs to the gameplay integration.
    // TryApply must compare expected with live state and commit atomically.
    public interface IShipyardRefit
    {
        ShipConfiguration ReadCurrent();
        string Validate(ShipConfiguration draft);
        bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason);
    }

    public sealed class ShipyardDraft
    {
        readonly ModuleLibrary library;
        readonly IShipyardRefit backend;
        readonly Func<ShipConfiguration, int, string> removalBlocker;
        readonly Func<string, string, bool> allowed;
        readonly ShipConfiguration baseline;
        ShipConfiguration draft;
        readonly Stack<ShipConfiguration> undo = new Stack<ShipConfiguration>();
        public AssemblyResult Assembly { get; private set; }
        public string Message { get; private set; } = "";
        public string Highlight { get; private set; }
        public bool Committed { get; private set; }
        public event Action Changed;
        public int Count => draft.middleIds.Count;
        public int Maximum => Math.Min(3, library.MaxMiddles);
        public string Rotor => draft.rotorId;
        public bool Dirty => !draft.ValueEquals(baseline);
        public bool CanUndo => undo.Count > 0 && !Committed;
        public bool HasBackend => backend != null;
        public ShipConfiguration Snapshot() => draft.Clone();
        public float OriginalLength { get; }

        public ShipyardDraft(ModuleLibrary library, ShipConfiguration current, IShipyardRefit backend = null,
            Func<ShipConfiguration, int, string> removalBlocker = null,
            Func<string, string, bool> allowed = null)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.backend = backend;
            this.removalBlocker = removalBlocker;
            this.allowed = allowed;
            baseline = (current ?? throw new ArgumentNullException(nameof(current))).Clone();
            draft = baseline.Clone();
            Assembly = ShipAssembler.Assemble(draft, library);
            if (!Assembly.ok) throw new ArgumentException(Reason(Assembly));
            OriginalLength = Assembly.overallLengthM;
        }

        static string Reason(AssemblyResult result) => result.rejections.Count == 0
            ? "This configuration is unavailable." : result.rejections[0].message;

        bool Refuse(string reason) { Message = reason; Changed?.Invoke(); return false; }

        bool Set(ShipConfiguration next, string highlight)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            var result = ShipAssembler.Assemble(next, library);
            if (!result.ok) return Refuse(Reason(result));
            if (next.ValueEquals(draft)) return false;
            undo.Push(draft.Clone()); draft = next; Assembly = result;
            Highlight = highlight; Message = ""; Changed?.Invoke(); return true;
        }

        // First prototype appends/removes the bay immediately behind the bow.
        // Existing bay indices, and therefore equipment references, never shift.
        public bool AddMiddle()
        {
            if (!CanSelect(ModuleKind.Middle, ShipConfiguration.V3Middle)) return Refuse("This section is unavailable.");
            if (Count >= Maximum) return Refuse("Maximum length for this hull.");
            var next = Snapshot(); next.middleIds.Add(ShipConfiguration.V3Middle);
            return Set(next, ShipAssembler.MiddleKey(Count));
        }

        public bool RemoveMiddle()
        {
            if (Count == 0) return Refuse("The bow and stern must remain.");
            if (backend != null && removalBlocker == null)
                return Refuse("Section availability is not connected yet.");
            string reason = RemovalReason();
            if (!string.IsNullOrEmpty(reason)) return Refuse(reason);
            var next = Snapshot(); next.middleIds.RemoveAt(Count - 1);
            return Set(next, "bow");
        }

        public bool ChooseWheel(string id)
        {
            if (!CanSelect(ModuleKind.Rotor, id)) return Refuse("This wheel is unavailable.");
            if (id != ShipConfiguration.TimberRotor && id != ShipConfiguration.ReinforcedRotor)
                return Refuse("That wheel is not available in this shipyard yet.");
            var next = Snapshot(); next.rotorId = id;
            return Set(next, ShipAssembler.StdKeyRotor);
        }

        public void Undo()
        {
            if (!CanUndo) return;
            draft = undo.Pop(); Assembly = ShipAssembler.Assemble(draft, library);
            Highlight = null; Message = ""; Changed?.Invoke();
        }

        public bool CanSelect(string kind, string id) => allowed == null || allowed(kind, id);
        public string RemovalReason() => Count == 0 ? "The bow and stern must remain." :
            backend != null && removalBlocker == null ? "Section availability is not connected yet." :
            removalBlocker?.Invoke(Snapshot(), Count - 1);

        public string CannotConfirm()
        {
            if (Committed) return "This refit is already confirmed.";
            if (!Dirty) return "No changes yet.";
            if (backend == null) return "Preview only. Live refitting is not connected.";
            return backend.Validate(Snapshot());
        }

        public bool Confirm()
        {
            string reason = CannotConfirm();
            if (!string.IsNullOrEmpty(reason)) return Refuse(reason);
            if (!backend.TryApply(baseline.Clone(), Snapshot(), out reason))
                return Refuse(string.IsNullOrEmpty(reason) ? "Refit could not be applied. Your ship is unchanged." : reason);
            Committed = true; undo.Clear(); Message = "Refit confirmed.";
            Changed?.Invoke(); return true;
        }
    }
}
