using System.Globalization;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **Ship-wide totals: GUNS n/ports, CREW n/beds, CARGO n/cap, DRAFT m.**
    /// (Mockup frame 1.) Four equal tiles, built once and re-texted by
    /// `Bind`. Crew over beds and cargo over cap turn ember on their own;
    /// draft takes the tone the adapter gives it.
    public sealed class TotalsStripView : VisualElement
    {
        readonly Label guns, gunsOf, crew, crewOf, cargo, cargoOf, draft, draftOf;

        public TotalsStripView()
        {
            AddToClassList("ys-tot");
            Tile("GUNS", out guns, out gunsOf);
            Tile("CREW", out crew, out crewOf);
            Tile("CARGO", out cargo, out cargoOf);
            Tile("DRAFT", out draft, out draftOf);
        }

        void Tile(string key, out Label v, out Label of)
        {
            var t = new VisualElement(); t.AddToClassList("ys-tot-tile");
            var k = new Label(key); k.AddToClassList("ys-tot-k"); t.Add(k);
            var row = new VisualElement(); row.AddToClassList("ys-tot-row");
            v = new Label(); v.AddToClassList("ys-tot-v");
            of = new Label(); of.AddToClassList("ys-tot-of");
            row.Add(v); row.Add(of); t.Add(row);
            Add(t);
        }

        public void Bind(YardTotalsVm t)
        {
            guns.text = t.guns.ToString(); gunsOf.text = $"/{t.gunPorts} ports";
            crew.text = t.crew.ToString(); crewOf.text = $"/{t.berths} beds";
            cargo.text = t.cargo.ToString(); cargoOf.text = $"/{t.cargoCap}";
            draft.text = t.draft.ToString("0.00", CultureInfo.InvariantCulture); draftOf.text = " m";
            Tone(guns, t.guns > t.gunPorts ? YardTone.Bad : YardTone.Plain);
            Tone(crew, t.crew > t.berths ? YardTone.Bad : YardTone.Plain);
            Tone(cargo, t.cargo > t.cargoCap ? YardTone.Bad : YardTone.Plain);
            Tone(draft, t.draftTone);
        }

        static void Tone(Label l, YardTone tone)
        {
            l.EnableInClassList("ys-tone-good", tone == YardTone.Good);
            l.EnableInClassList("ys-tone-warn", tone == YardTone.Warn);
            l.EnableInClassList("ys-tone-bad", tone == YardTone.Bad);
        }
    }
}
