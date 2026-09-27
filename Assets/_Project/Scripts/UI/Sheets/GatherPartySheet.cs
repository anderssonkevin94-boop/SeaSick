using System.Collections.Generic;
using SeaSick.Ship;
using SeaSick.Ship.Modular;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **"⛏ Send gather party" (2026-09-27).** A small bottom sheet, opened
    /// from the anchor prompt off an island with no camp: WHAT (the raw goods
    /// in reach of the landing, with how much), HOW MUCH (5 / 10 / 20 / fill
    /// hold), HOW MANY hands (1..aboard), then Send. Big thumb buttons, the
    /// bottom of a portrait screen; its own panel (the `ShipyardModal`
    /// pattern) because the sheet HUD is not up on an island without a fire.
    /// Style: `Resources/UI/GatherParty.uss`.
    public sealed class GatherPartySheet : MonoBehaviour
    {
        static GatherPartySheet active;
        public static bool IsOpen => active != null;

        AnchorController anchor;
        GatherParty party;
        PanelSettings settings;
        UIDocument document;

        List<GatherParty.Option> options = new List<GatherParty.Option>();
        string res;
        int amount = 10;
        int hands = 1;
        int aboard;

        VisualElement whatRow, howMuchRow, howManyRow;
        Label title, note;
        Button send;

        static readonly int[] Amounts = { 5, 10, 20, GatherParty.FillHold };

        public static void Open(AnchorController a, GatherParty p)
        {
            if (active != null || a == null || p == null) return;
            var template = Resources.Load<PanelSettings>("UI/SheetPanel");
            var go = new GameObject("Gather party sheet");
            var s = go.AddComponent<GatherPartySheet>();
            active = s;
            s.anchor = a;
            s.party = p;
            s.settings = template != null ? Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            s.settings.sortingOrder = 900;
            s.settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            s.settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            s.settings.match = 0;
            s.document = go.AddComponent<UIDocument>();
            s.document.panelSettings = s.settings;
            ShipyardSession.SetWorldInputBlocked(true);
            s.Build();
        }

        public static void Close()
        {
            if (active != null) Destroy(active.gameObject);
        }

        void Build()
        {
            var root = document.rootVisualElement;
            var style = Resources.Load<StyleSheet>("UI/GatherParty");
            if (style != null) root.styleSheets.Add(style);
            root.AddToClassList("gp-root");

            var scrim = new VisualElement();
            scrim.AddToClassList("gp-scrim");
            scrim.RegisterCallback<PointerDownEvent>(_ => Close());
            root.Add(scrim);

            var sheet = new VisualElement();
            sheet.AddToClassList("gp-sheet");
            root.Add(sheet);

            title = new Label("⛏  Gather party");
            title.AddToClassList("gp-title");
            sheet.Add(title);

            sheet.Add(Heading("What"));
            whatRow = Row(sheet);
            sheet.Add(Heading("How much"));
            howMuchRow = Row(sheet);
            sheet.Add(Heading("Hands"));
            howManyRow = Row(sheet);

            note = new Label("");
            note.AddToClassList("gp-note");
            sheet.Add(note);

            var actions = Row(sheet);
            var cancel = new Button(Close) { text = "Cancel" };
            cancel.AddToClassList("gp-btn");
            cancel.AddToClassList("gp-secondary");
            actions.Add(cancel);
            send = new Button(DoSend) { text = "Send" };
            send.AddToClassList("gp-btn");
            send.AddToClassList("gp-primary");
            actions.Add(send);

            Refresh();
        }

        static Label Heading(string text)
        {
            var l = new Label(text);
            l.AddToClassList("gp-heading");
            return l;
        }

        static VisualElement Row(VisualElement parent)
        {
            var r = new VisualElement();
            r.AddToClassList("gp-row");
            parent.Add(r);
            return r;
        }

        Button Chip(VisualElement row, string text, bool on, System.Action tap)
        {
            var b = new Button(() => { tap(); Refresh(); }) { text = text };
            b.AddToClassList("gp-btn");
            b.AddToClassList("gp-chip");
            if (on) b.AddToClassList("gp-on");
            row.Add(b);
            return b;
        }

        /// Rebuilt on every choice: a handful of buttons, only on a tap.
        void Refresh()
        {
            if (anchor == null || anchor.CurrentIsland == null) { Close(); return; }
            options = GatherParty.Survey(anchor.CurrentIsland, anchor.PartyLanding(), party);
            aboard = GatherParty.Available(anchor).Count;
            if (res == null || !options.Exists(o => o.resource == res))
                res = options.Count > 0 ? options[0].resource : null;
            hands = Mathf.Clamp(hands, 1, Mathf.Max(1, aboard));

            title.text = $"⛏  Gather party — {anchor.CurrentIsland.name}";

            whatRow.Clear();
            if (options.Count == 0)
            {
                var none = new Label("nothing to gather within reach of the landing");
                none.AddToClassList("gp-note");
                whatRow.Add(none);
            }
            foreach (var o in options)
            {
                string r = o.resource;
                Chip(whatRow, $"{r}\n{o.units} in reach", r == res, () => res = r);
            }

            howMuchRow.Clear();
            foreach (int a in Amounts)
            {
                int v = a;
                Chip(howMuchRow, v == GatherParty.FillHold ? "Fill hold" : v.ToString(), v == amount, () => amount = v);
            }

            howManyRow.Clear();
            int shown = Mathf.Min(aboard, 6);
            for (int i = 1; i <= shown; i++)
            {
                int v = i;
                Chip(howManyRow, v.ToString(), v == hands, () => hands = v);
            }
            if (aboard == 0)
            {
                var none = new Label("no hands aboard");
                none.AddToClassList("gp-note");
                howManyRow.Add(none);
            }

            int room = party.Room;
            string want = amount == GatherParty.FillHold ? $"up to {room}" : $"{Mathf.Min(amount, room)}";
            note.text = res == null ? "" : $"{want} {res.ToLowerInvariant()} into the hold ({room} room)";
            send.SetEnabled(res != null && aboard > 0 && room > 0);
        }

        void DoSend()
        {
            if (party.Send(res, amount, hands, out string why)) { Close(); return; }
            note.text = why;
        }

        void Update()
        {
            if (document == null || settings == null) return;
            bool wide = Screen.width > Screen.height * 1.15f;
            settings.referenceResolution = wide ? new Vector2Int(844, 390) : new Vector2Int(390, 844);
            var root = document.rootVisualElement;
            float scale = settings.referenceResolution.x / (float)Mathf.Max(1, Screen.width);
            var safe = Screen.safeArea;
            root.style.paddingBottom = safe.yMin * scale;
            // She weighed anchor or the island went away under the sheet.
            if (anchor == null || anchor.CurrentIsland == null
                || anchor.CurrentState != AnchorController.State.Anchored) Close();
            // Desktop override: Escape closes.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        void OnDestroy()
        {
            if (active == this) active = null;
            ShipyardSession.SetWorldInputBlocked(false);
            if (settings != null) Destroy(settings);
        }
    }
}
