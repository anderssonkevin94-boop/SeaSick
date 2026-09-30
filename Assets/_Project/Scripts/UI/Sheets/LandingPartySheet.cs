using System.Collections.Generic;
using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The landing party sheet (2026-09-30).** Kevin, at Island_3 with the
    /// old gather party up: *"horrible ui, it doesn't fit the rest of the
    /// game and it's structured in a way that makes no sense. only room for
    /// two resources ... what if i want to hunt, or explore?"* He approved
    /// the mockups "7 · Landing party: Explore (fog)" and "7b · Landing
    /// party: Gather". This replaces `GatherPartySheet` (its own panel, its
    /// own USS) with an ordinary framed sheet in the house look (`CardKit`,
    /// `.st` + Hand.uss + Lookout.uss), hugging its content on the phone so
    /// the island stays visible above it (`SheetHost.HugsContent`).
    ///
    /// * **header** -- landing glyph · "Landing party" · "Anchored off
    ///   Island_3 · 4 aboard" (Gather: "hold 23/34") · pill "35% explored"
    ///   (`IslandFog.Revealed01`) · ☰ (the pause menu, since the IMGUI
    ///   Menu/Ledger chips stand down off a fresh island) · ✕;
    /// * **three order cards** -- Explore · Gather · Hunt;
    /// * **Explore** -- FOUND SO FAR chips (`IslandFog.FoundResources`,
    ///   `FoundHerds`, `FoundIslandFinds`), one line of what happens, WHO
    ///   GOES portraits ("spear" / "bow · 8" / "unarmed" / "at the helm");
    /// * **Gather** -- every found kind as a 4-wide icon tile, HOW MUCH
    ///   "5 / 10 / All N" (N = what the hold and the reachable sources
    ///   allow), WHO GOES name pills;
    /// * **Hunt** -- the found herds as tiles, WHO GOES portraits (only the
    ///   armed can be picked);
    /// * **thumb row** -- ONE primary that says exactly what happens ("Send
    ///   Bo and Ma to explore", "Send 2 to gather 10 ore", "Send Pip to hunt
    ///   goats"), or the fix: a button when there is one ("Explore first"),
    ///   plain text when there is not ("Nobody is armed ..."). Never a
    ///   disabled primary.
    ///
    /// While a party is ashore the page is its progress (what they are
    /// doing, a bar, what has been found so far) and "Call them back". The
    /// result of the last trip (`GatherParty.LastReport`) shows in a card at
    /// the top of the page for a few seconds after they are back.
    ///
    /// Opened from the anchor prompt's "Landing party" row
    /// (`AnchorController`), where "⛏ Send gather party" used to be.
    /// The trip logic is `Ship.GatherParty` (explore, gather, hunt).
    public sealed class LandingPartySheet : ISheetFramed
    {
        public static bool IsOpen => Sheets.Current is LandingPartySheet;

        public static void Open(AnchorController a)
        {
            if (a == null || a.CurrentIsland == null) return;
            Sheets.Open(new LandingPartySheet(a));
        }

        /// **Stopped off an island with no camp** (anchored or ashore, no
        /// fire and no blueprint) -- where a landing party is offered, and
        /// where the IMGUI Menu/Ledger chips stand down (`PauseChip`).
        public static bool Offered(AnchorController a) =>
            a != null && a.CurrentIsland != null
            && (a.CurrentState == AnchorController.State.Anchored || a.CurrentState == AnchorController.State.Ashore)
            && !Sheets.SuppressLegacy;

        public enum Tab3 { Explore, Gather, Hunt }
        static Tab3 lastOrder = Tab3.Explore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => lastOrder = Tab3.Explore;

        readonly AnchorController anchor;
        readonly GatherParty party;
        readonly Island island;
        readonly Voyage.VoyageManager voyage;
        readonly Transform helmsman;
        Tab3 order;

        // choices
        readonly List<CrewAgent> picked = new List<CrewAgent>();
        string gatherRes;
        int amount = 10;
        Animal.Kind? herd;
        string note;
        /// Why the last Send was refused, shown once on the next build.
        string lastWhy;

        static readonly int[] Amounts = { 5, 10 };
        const int AllAmount = GatherParty.FillHold;

        public LandingPartySheet(AnchorController a)
        {
            anchor = a;
            party = GatherParty.For(a);
            island = a != null ? a.CurrentIsland : null;
            voyage = Object.FindAnyObjectByType<Voyage.VoyageManager>();
            order = lastOrder;
            if (a != null)
                foreach (var t in a.GetComponentsInChildren<Transform>(false))
                    if (t.name == "Helmsman") { helmsman = t; break; }
            // Two hands by default (the mockup's Bo and Ma), fewer if fewer.
            foreach (var c in GatherParty.Available(a))
            {
                if (picked.Count >= 2) break;
                picked.Add(c);
            }
        }

        // --- ISheet / ISheetFramed ------------------------------------------

        public string Title => "Landing party";
        public Color Accent => MidnightLandHud.Ice;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public Vector3 AnchorWorld => anchor != null ? anchor.PartyLanding() : Vector3.zero;

        public bool StillValid =>
            anchor != null && island != null && anchor.CurrentIsland == island
            && (anchor.CurrentState == AnchorController.State.Anchored
                || anchor.CurrentState == AnchorController.State.Ashore);

        IslandFog Fog => IslandFog.For(island);
        bool PartyOut => party != null && party.Out && party.Island == island;

        // --- header -----------------------------------------------------------

        WatchTiles.Head head;

        public VisualElement BuildHeader()
        {
            head = new WatchTiles.Head(new LandGlyph("party", MidnightLandHud.Ice) { name = "landing-glyph" }, "Landing party");
            // ☰ -- the pause menu (save, settings, home). Off a fresh island
            // the IMGUI Menu chip stands down; this is its way in.
            var menu = new Button(OpenMenu) { text = "" };
            menu.AddToClassList("st-square");
            menu.tooltip = "Menu";
            menu.style.marginRight = 6f;
            menu.Add(new StationPage.Glyph("menu", StationPage.Ink, "st-glyph"));
            head.Root.Insert(Mathf.Max(0, head.Root.childCount - 1), menu);
            FillHeader();
            return head.Root;
        }

        static void OpenMenu()
        {
            Sheets.Close();
            SeaSick.UI.Menus.GameMenus.TogglePause();
        }

        void FillHeader()
        {
            if (head == null || island == null) return;
            string tail;
            if (order == Tab3.Gather && voyage != null)
                tail = $"hold {voyage.TotalHeld}/{voyage.HoldCapacity}";
            else
                tail = $"{GatherParty.Company(anchor).Count} aboard";
            head.SetSub($"Anchored off {island.name} · {tail}");
            var fog = Fog;
            int pct = fog != null ? Mathf.RoundToInt(fog.Revealed01 * 100f) : 100;
            head.SetPill($"{pct}% explored", pct >= 100 ? StationPage.PillGood : StationPage.PillWait);
        }

        // --- the page -----------------------------------------------------------

        VisualElement root, col, acts;
        string builtKey;

        public VisualElement Build()
        {
            root = CardKit.Page(out col);
            builtKey = null;
            Refresh();
            return root;
        }

        public void Refresh()
        {
            FillHeader();
            if (root == null) return;
            string key = Key();
            if (key != builtKey)
            {
                builtKey = key;
                Rebuild();
            }
            else if (PartyOut) FillProgress();
        }

        /// Everything the page shows that can change without a tap. The page
        /// is rebuilt only when this moves (a tap rebuilds at once).
        string Key()
        {
            var sb = new StringBuilder(128);
            sb.Append(order).Append('|').Append(PartyOut ? (party.Recalling ? "R" : "O") : "-").Append('|');
            sb.Append(GatherParty.ReportFresh ? GatherParty.LastReport : "").Append('|');
            var fog = Fog;
            if (fog != null)
            {
                found.Clear();
                fog.FoundResources(found);
                foreach (var kv in found) sb.Append(kv.Key).Append(kv.Value).Append(',');
                herds.Clear();
                fog.FoundHerds(herds);
                sb.Append(herds.Count).Append(',');
                finds.Clear();
                fog.FoundIslandFinds(finds);
                sb.Append(finds.Count).Append('|');
            }
            if (!PartyOut)
            {
                foreach (var c in GatherParty.Company(anchor))
                    sb.Append(c.GetInstanceID()).Append(c.Available ? 'a' : 'b').Append(picked.Contains(c) ? '+' : '.');
                if (voyage != null)
                    sb.Append('|').Append(voyage.TotalHeld).Append(voyage.HeldOf(Res.Spear) + voyage.HeldOf(Res.IronSpear))
                      .Append(voyage.HeldOf(Res.Bow)).Append(voyage.HeldOf(Res.Arrows));
                sb.Append('|').Append(anchor != null && anchor.CanSendParty(out _) ? 1 : 0)
                  .Append(Outpost.Surveying(island) ? 1 : 0);
            }
            return sb.ToString();
        }

        readonly Dictionary<string, int> found = new Dictionary<string, int>();
        readonly List<Animal> herds = new List<Animal>();
        readonly List<IslandFind> finds = new List<IslandFind>();
        readonly Dictionary<CrewAgent, string> dealt = new Dictionary<CrewAgent, string>();
        List<GatherParty.Option> survey = new List<GatherParty.Option>();

        void Rebuild()
        {
            col.Clear();
            if (acts != null) acts.RemoveFromHierarchy();
            acts = null;
            FillHeader();

            if (GatherParty.ReportFresh)
            {
                var back = new CardKit.Now(col, new LandGlyph(OrderGlyph(PartyMode()), MidnightLandHud.Ice));
                back.Set("Back aboard", GatherParty.LastReport);
                back.S.style.whiteSpace = WhiteSpace.Normal;
                back.Tone(GatherParty.LastReport.Contains("hurt") ? 1 : 0);
                back.Root.style.marginBottom = 8f;
            }

            if (PartyOut) { BuildProgress(); return; }

            BuildOrderCards();
            note = null;
            switch (order)
            {
                case Tab3.Explore: BuildExplore(); break;
                case Tab3.Gather: BuildGather(); break;
                default: BuildHunt(); break;
            }
            if (!string.IsNullOrEmpty(lastWhy))
            {
                var l = StationPage.Text(StationPage.Cap(lastWhy), "hs-now-s");
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.color = (Color)new Color32(242, 196, 109, 255);
                l.style.marginTop = 6f;
                col.Add(l);
                lastWhy = null;
            }
        }

        Tab3 PartyMode() => party == null ? order
            : party.Mode == GatherParty.Order.Explore ? Tab3.Explore
            : party.Mode == GatherParty.Order.Hunt ? Tab3.Hunt : Tab3.Gather;

        static string OrderGlyph(Tab3 t) => t == Tab3.Explore ? "explore" : t == Tab3.Gather ? "gather" : "hunt";

        void Tap(System.Action change)
        {
            change?.Invoke();
            builtKey = Key();
            Rebuild();
        }

        // --- the three order cards ---------------------------------------------

        void BuildOrderCards()
        {
            var row = CardKit.Grid(col);
            string[] names = { "Explore", "Gather", "Hunt" };
            for (int i = 0; i < 3; i++)
            {
                var t = (Tab3)i;
                var b = new Button(() => Tap(() => { order = t; lastOrder = t; })) { text = "" };
                b.AddToClassList("hs-tile");
                if (i == 2) b.AddToClassList("hs-tile--col3");
                if (t == order) b.AddToClassList("hs-tile--on");
                b.style.flexDirection = FlexDirection.Column;
                b.style.justifyContent = Justify.Center;
                b.style.alignItems = Align.Center;
                b.style.height = 60f;
                var g = new LandGlyph(OrderGlyph(t), t == order ? MidnightLandHud.Ice : new Color32(201, 216, 224, 255));
                g.style.width = 22f; g.style.height = 22f;
                b.Add(g);
                var n = StationPage.Text(names[i], "hs-tile-n");
                n.style.marginTop = 2f;
                b.Add(n);
                row.Add(b);
            }
        }

        // --- explore -------------------------------------------------------------

        void BuildExplore()
        {
            CardKit.Eye(col, "FOUND SO FAR");
            FoundChips(col);
            var why = StationPage.Text(
                $"The party walks into the fog for about {Mathf.RoundToInt(GatherParty.ExploreSeconds / 60f)} minutes and reports what they find. Something could bite; armed hands are safer.",
                "hs-now-s");
            why.style.whiteSpace = WhiteSpace.Normal;
            why.style.marginTop = 6f;
            col.Add(why);
            WhoGoes(col, false);

            var who = PickedInOrder();
            if (!CanSendAtAll(out string fix)) { Thumb(null, fix); return; }
            if (who.Count == 0) { Thumb(null, "Tap who goes."); return; }
            var fog = Fog;
            if (fog != null && fog.Revealed01 >= 0.999f && order == Tab3.Explore)
                note = "The whole island is already seen.";
            Thumb($"Send {Names(who)} to explore", note, () =>
            {
                if (party.SendExplore(who, out string why2)) Sheets.Close();
                else Tap(() => lastWhy = why2);
            });
        }

        /// Resources, herds and island finds seen so far, as small chips.
        void FoundChips(VisualElement into)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.flexShrink = 0f;
            int n = 0;
            foreach (var kv in found)
            {
                if (kv.Value <= 0) continue;
                row.Add(Chip($"{World.Economy.ResDefs.Label(kv.Key)} · {kv.Value}", false)); n++;
            }
            int goats = 0, boar = 0;
            foreach (var a in herds) if (a != null) { if (a.kind == Animal.Kind.Goat) goats++; else boar++; }
            if (goats > 0) { row.Add(Chip($"Goats · {goats}", false)); n++; }
            if (boar > 0) { row.Add(Chip($"Boar · {boar}", false)); n++; }
            foreach (var f in finds)
            {
                if (f == null) continue;
                row.Add(Chip(f.Kind == IslandFind.FindKind.Cairn ? "A cairn" : "A cache", true)); n++;
            }
            if (n == 0)
            {
                var none = StationPage.Text("Nothing yet. The fog hides the rest of the island.", "hs-now-s");
                none.style.whiteSpace = WhiteSpace.Normal;
                row.Add(none);
            }
            into.Add(row);
        }

        static Label Chip(string text, bool find)
        {
            var l = StationPage.Text(text, "hs-tile-s");
            l.style.paddingLeft = 9f; l.style.paddingRight = 9f;
            l.style.paddingTop = 4f; l.style.paddingBottom = 4f;
            l.style.marginRight = 6f; l.style.marginBottom = 6f;
            l.style.borderTopLeftRadius = 10f; l.style.borderTopRightRadius = 10f;
            l.style.borderBottomLeftRadius = 10f; l.style.borderBottomRightRadius = 10f;
            l.style.borderTopWidth = 1f; l.style.borderBottomWidth = 1f;
            l.style.borderLeftWidth = 1f; l.style.borderRightWidth = 1f;
            Color edge = find ? (Color)new Color32(242, 196, 109, 255) : StationPage.Edge;
            l.style.borderTopColor = edge; l.style.borderBottomColor = edge;
            l.style.borderLeftColor = edge; l.style.borderRightColor = edge;
            l.style.backgroundColor = find ? new Color(242f / 255f, 196f / 255f, 109f / 255f, 0.14f) : (Color)new Color32(28, 52, 70, 255);
            l.style.color = find ? (Color)new Color32(242, 196, 109, 255) : StationPage.Ink;
            l.style.maxWidth = StyleKeyword.None;
            return l;
        }

        // --- gather ----------------------------------------------------------------

        void BuildGather()
        {
            survey = GatherParty.Survey(island, anchor.PartyLanding(), party);
            // Every kind found on the island (the fog's list), plus anything
            // in reach the fog has not counted -- any number of kinds.
            var kinds = new List<string>();
            foreach (var kv in found) if (kv.Value > 0 && Res.IsGatherable(kv.Key) && !kinds.Contains(kv.Key)) kinds.Add(kv.Key);
            foreach (var o in survey) if (!kinds.Contains(o.resource)) kinds.Add(o.resource);
            if (gatherRes == null || !kinds.Contains(gatherRes))
            {
                gatherRes = null;
                foreach (var o in survey) { gatherRes = o.resource; break; }
                if (gatherRes == null && kinds.Count > 0) gatherRes = kinds[0];
            }

            CardKit.Eye(col, "WHAT", "found on this island");
            if (kinds.Count == 0)
            {
                var none = StationPage.Text("Nothing found yet.", "hs-now-s");
                col.Add(none);
            }
            var grid = CardKit.Grid(col);
            for (int i = 0; i < kinds.Count; i++)
            {
                string r = kinds[i];
                int reach = InReach(r);
                found.TryGetValue(r, out int seen);
                var b = new Button(() => Tap(() => gatherRes = r)) { text = "" };
                b.AddToClassList("hs-tile");
                if (r == gatherRes) b.AddToClassList("hs-tile--on");
                if (reach <= 0) b.AddToClassList("ck-tile--short");
                b.style.width = Length.Percent(23.5f);
                b.style.marginRight = i % 4 == 3 ? 0f : (StyleLength)Length.Percent(2f);
                b.style.height = 64f;
                b.style.flexDirection = FlexDirection.Column;
                b.style.justifyContent = Justify.Center;
                b.style.alignItems = Align.Center;
                b.style.paddingLeft = 2f; b.style.paddingRight = 2f;
                var ico = StationPage.Icon(r, "hs-tile-ico");
                ico.style.marginRight = 0f;
                b.Add(ico);
                var n = StationPage.Text(World.Economy.ResDefs.Label(r), "hs-tile-n");
                n.style.fontSize = 12f;
                b.Add(n);
                b.Add(StationPage.Text(reach > 0 ? Mathf.Max(seen, reach).ToString() : "far", "hs-tile-s"));
                grid.Add(b);
            }

            // HOW MUCH: 5 / 10 / All N (N = the hold's room, as far as the
            // sources in reach go).
            int room = party.Room;
            int all = Mathf.Min(room, gatherRes != null ? InReach(gatherRes) : 0);
            CardKit.Eye(col, "HOW MUCH");
            var pills = WatchTiles.Box("ck-pills");
            pills.pickingMode = PickingMode.Position;
            pills.style.marginTop = 0f;
            foreach (int a in Amounts)
            {
                int v = a;
                var p = CardKit.Pill(pills, v.ToString(), () => Tap(() => amount = v));
                p.style.flexGrow = 1f;
                if (amount == v) CardKit.PillTone(p, "ice");
            }
            var allB = CardKit.Pill(pills, $"All {Mathf.Max(0, all)}", () => Tap(() => amount = AllAmount));
            allB.style.flexGrow = 1f;
            if (amount == AllAmount) CardKit.PillTone(allB, "ice");
            col.Add(pills);

            WhoGoesPills(col);

            var who = PickedInOrder();
            if (!CanSendAtAll(out string fix)) { Thumb(null, fix); return; }
            if (kinds.Count == 0) { Thumb("Explore first", "Nothing found yet.", () => Tap(() => { order = Tab3.Explore; lastOrder = order; })); return; }
            if (room <= 0) { Thumb(null, "The hold is full. Unload it at a camp."); return; }
            if (gatherRes == null || InReach(gatherRes) <= 0)
            {
                Thumb("Explore first", $"No {Lower(gatherRes)} within reach of the landing yet.",
                    () => Tap(() => { order = Tab3.Explore; lastOrder = order; }));
                return;
            }
            if (who.Count == 0) { Thumb(null, "Tap who goes."); return; }
            int want = amount == AllAmount ? all : Mathf.Min(amount, room);
            string whoWords = who.Count == 1 ? who[0].DisplayName : who.Count.ToString();
            string res = gatherRes;
            int amt = amount;
            Thumb($"Send {whoWords} to gather {want} {Lower(res)}", null, () =>
            {
                if (party.SendGather(res, amt, who, out string why)) Sheets.Close();
                else Tap(() => lastWhy = why);
            });
        }

        int InReach(string res)
        {
            foreach (var o in survey) if (o.resource == res) return o.units;
            return 0;
        }

        static string Lower(string res) =>
            string.IsNullOrEmpty(res) ? "that" : World.Economy.ResDefs.Label(res).ToLowerInvariant();

        // --- hunt --------------------------------------------------------------------

        void BuildHunt()
        {
            int goats = 0, boar = 0;
            foreach (var a in herds) if (a != null && !a.Dead) { if (a.kind == Animal.Kind.Goat) goats++; else boar++; }
            if (herd == Animal.Kind.Goat && goats == 0) herd = null;
            if (herd == Animal.Kind.Boar && boar == 0) herd = null;
            if (herd == null) herd = goats > 0 ? Animal.Kind.Goat : boar > 0 ? Animal.Kind.Boar : (Animal.Kind?)null;

            CardKit.Eye(col, "WHAT", "herds found");
            var grid = CardKit.Grid(col);
            int i = 0;
            if (goats > 0) HerdTile(grid, Animal.Kind.Goat, goats, i++);
            if (boar > 0) HerdTile(grid, Animal.Kind.Boar, boar, i++);
            if (i == 0)
            {
                var none = StationPage.Text("No herds found yet.", "hs-now-s");
                col.Add(none);
            }

            WhoGoes(col, true);

            var hunters = new List<CrewAgent>();
            foreach (var c in PickedInOrder()) if (dealt.TryGetValue(c, out var w) && w != null) hunters.Add(c);
            bool anyArmed = false;
            foreach (var kv in dealt) if (kv.Value != null && kv.Key.Available) { anyArmed = true; break; }

            if (!CanSendAtAll(out string fix)) { Thumb(null, fix); return; }
            if (herd == null) { Thumb("Explore first", "No herds found yet.", () => Tap(() => { order = Tab3.Explore; lastOrder = order; })); return; }
            if (!anyArmed) { Thumb(null, "Nobody is armed. Take a spear or a bow and arrows aboard at a camp."); return; }
            if (party.Room <= 0) { Thumb(null, "The hold is full. Unload it at a camp."); return; }
            if (hunters.Count == 0) { Thumb(null, "Tap an armed hand to hunt."); return; }
            var kind = herd.Value;
            Thumb($"Send {Names(hunters)} to hunt {GatherParty.HerdWord(kind, 2)}", null, () =>
            {
                if (party.SendHunt(kind, hunters, out string why)) Sheets.Close();
                else Tap(() => lastWhy = why);
            });
        }

        void HerdTile(VisualElement grid, Animal.Kind kind, int count, int index)
        {
            var t = new CardKit.Tile(_ => Tap(() => herd = kind), false).Col3(index);
            t.Ico.Clear();
            var g = new LandGlyph(kind == Animal.Kind.Goat ? "goat" : "boar", StationPage.Ink);
            g.style.width = 26f; g.style.height = 26f;
            t.Ico.Add(g);
            t.Set(kind == Animal.Kind.Goat ? "Goats" : "Boar", $"{count} seen");
            t.State(herd == kind);
            grid.Add(t.Root);
        }

        // --- who goes -------------------------------------------------------------------

        /// The chosen hands in the ship's own crew order.
        List<CrewAgent> PickedInOrder()
        {
            var list = new List<CrewAgent>();
            foreach (var c in GatherParty.Company(anchor))
                if (picked.Contains(c) && c.Available) list.Add(c);
            return list;
        }

        void Deal()
        {
            var order2 = PickedInOrder();
            foreach (var c in GatherParty.Company(anchor)) if (!order2.Contains(c)) order2.Add(c);
            GatherParty.DealWeapons(voyage, order2, dealt);
        }

        string WeaponWord(CrewAgent c)
        {
            dealt.TryGetValue(c, out var w);
            if (w == Res.IronSpear) return "iron spear";
            if (w == Res.Spear) return "spear";
            if (w == Res.Bow) return $"bow · {(voyage != null ? voyage.HeldOf(Res.Arrows) : 0)}";
            return "unarmed";
        }

        /// Portrait tiles: tap to include. `armedOnly` (hunt) locks the
        /// unarmed. The helmsman is shown, "at the helm", and cannot go.
        void WhoGoes(VisualElement into, bool armedOnly)
        {
            Deal();
            CardKit.Eye(into, "WHO GOES", "tap to pick");
            var grid = CardKit.Grid(into);
            int i = 0;
            foreach (var c in GatherParty.Company(anchor))
            {
                var hand = c;
                bool armed = dealt.TryGetValue(c, out var w) && w != null;
                bool can = c.Available && (!armedOnly || armed);
                var t = new CardKit.Tile(_ =>
                {
                    if (!can) return;
                    Tap(() => { if (!picked.Remove(hand)) picked.Add(hand); });
                }, true).Col3(i++);
                t.SetPerson(c.DisplayName);
                t.Set(c.DisplayName, !c.Available ? "busy" : WeaponWord(c));
                bool on = picked.Contains(c) && can;
                t.State(on, !can);
                if (armed && can) t.Root.AddToClassList("ck-tile--ok");
                grid.Add(t.Root);
            }
            if (helmsman != null)
            {
                var t = new CardKit.Tile(null, true).Col3(i++);
                t.SetPerson("Captain");
                t.Set("Captain", "at the helm");
                t.State(false, true);
                grid.Add(t.Root);
            }
        }

        /// Gather's compact row: "Who goes:" and a name pill per hand.
        void WhoGoesPills(VisualElement into)
        {
            var row = WatchTiles.Box("ck-pills");
            row.pickingMode = PickingMode.Position;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.Wrap;
            var k = StationPage.Text("Who goes:", "hs-eye-em");
            k.style.marginLeft = 0f;
            k.style.marginRight = 8f;
            row.Add(k);
            foreach (var c in GatherParty.Company(anchor))
            {
                var hand = c;
                if (!c.Available) continue;
                var p = CardKit.Pill(row, c.DisplayName, () => Tap(() => { if (!picked.Remove(hand)) picked.Add(hand); }));
                p.style.marginBottom = 4f;
                if (picked.Contains(c)) CardKit.PillTone(p, "ice");
            }
            into.Add(row);
        }

        static string Names(List<CrewAgent> who)
        {
            if (who.Count == 0) return "";
            if (who.Count == 1) return who[0].DisplayName;
            if (who.Count == 2) return $"{who[0].DisplayName} and {who[1].DisplayName}";
            return who.Count.ToString();
        }

        /// The blockers every order shares, as a line to show instead of a
        /// button (none of them has a button that fixes it).
        bool CanSendAtAll(out string fix)
        {
            fix = null;
            if (anchor == null || !anchor.CanSendParty(out string why)) { fix = "Wait until she is lying at anchor."; return false; }
            if (Outpost.Surveying(island)) { fix = "Still looking the island over…"; return false; }
            if (Combat.EnemyShip.CountAt(island) > 0) { fix = "Raiders on this island. Nobody goes ashore."; return false; }
            if (GatherParty.Available(anchor).Count == 0) { fix = "Everyone aboard is busy."; return false; }
            return true;
        }

        // --- the thumb row ------------------------------------------------------------------

        /// ONE primary (`text` + `tap`), or plain text when `text` is null.
        /// `line` is a short note shown above the row.
        void Thumb(string text, string line, System.Action tap = null)
        {
            if (!string.IsNullOrEmpty(line))
            {
                var l = StationPage.Text(line, "hs-now-s");
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.marginTop = 8f;
                col.Add(l);
            }
            if (text == null) return;
            acts = CardKit.Acts(root);
            CardKit.Act(acts, text, () => tap?.Invoke(), 1);
        }

        // --- while a party is ashore ---------------------------------------------------------

        CardKit.Now progress;
        CardKit.Bar bar;

        void BuildProgress()
        {
            progress = new CardKit.Now(col, new LandGlyph(OrderGlyph(PartyMode()), MidnightLandHud.Ice));
            progress.S.style.whiteSpace = WhiteSpace.Normal;
            bar = new CardKit.Bar(col);
            bar.Root.style.marginTop = 8f;
            if (party.Mode != GatherParty.Order.Gather)
            {
                CardKit.Eye(col, "FOUND SO FAR");
                FoundChips(col);
            }
            FillProgress();
            acts = CardKit.Acts(root);
            if (party.Recalling)
            {
                var l = StationPage.Text("Coming back aboard…", "hs-now-s");
                l.style.marginTop = 4f;
                acts.Add(l);
            }
            else CardKit.Act(acts, "Call them back", () => { party.Recall("called back"); Tap(() => { }); }, 1);
        }

        void FillProgress()
        {
            if (progress == null || party == null) return;
            string who = Names(new List<CrewAgent>(party.Hands));
            if (party.Hands.Count > 2) who = $"{party.Hands.Count} hands";
            string t, s;
            float t01;
            switch (party.Mode)
            {
                case GatherParty.Order.Explore:
                {
                    int left = Mathf.CeilToInt(Mathf.Max(0f, GatherParty.ExploreSeconds - party.Elapsed));
                    t = party.Recalling ? "Coming back" : left > 0 ? $"Exploring · {left / 60}:{left % 60:00} left" : "Walking back";
                    s = party.HurtName != null ? $"{party.HurtName} is hurt; they are bringing him home"
                        : $"{who} {(party.Hands.Count == 1 ? "is" : "are")} in the fog";
                    t01 = party.Explore01;
                    break;
                }
                case GatherParty.Order.Hunt:
                {
                    int target = Mathf.Max(1, party.Hands.Count) * GatherParty.BeastsPerHunter;
                    t = $"Hunting {GatherParty.HerdWord(party.HuntKind, 2)} · {party.Kills}/{target}";
                    s = party.MeatAboard > 0 ? $"{party.MeatAboard} meat aboard so far" : $"{who} {(party.Hands.Count == 1 ? "is" : "are")} stalking";
                    t01 = party.Kills / (float)target;
                    break;
                }
                default:
                {
                    string label = World.Economy.ResDefs.Label(party.Resource);
                    t = $"Gathering {label.ToLowerInvariant()} · {party.DeliveredUnits}/{party.TargetLabel}";
                    s = $"{party.Ashore} ashore · {party.InHand} in hand";
                    t01 = party.Target > 0 ? party.DeliveredUnits / (float)party.Target
                        : party.Room > 0 ? party.DeliveredUnits / (float)(party.DeliveredUnits + party.Room) : 1f;
                    break;
                }
            }
            if (party.Recalling && !string.IsNullOrEmpty(party.StopReason) && party.HurtName == null)
                s = StationPage.Cap(party.StopReason);
            progress.Set(t, s);
            progress.Tone(party.HurtName != null ? 1 : -1);
            bar.Set(t01, MidnightLandHud.Ice);
        }

        // --- drawn glyphs -------------------------------------------------------------------

        /// **The order cards' and the header's drawn icons**: a compass
        /// (explore), a pick (gather), a spear (hunt), a pennant on a post
        /// (the party), a goat and a boar. Drawn, so no font glyph or
        /// texture is needed (the `StationPage.Glyph` approach).
        sealed class LandGlyph : VisualElement
        {
            readonly string kind;
            readonly Color color;

            public LandGlyph(string kind, Color color)
            {
                this.kind = kind;
                this.color = color;
                pickingMode = PickingMode.Ignore;
                AddToClassList("lk-glyph");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                float s = Mathf.Min(r.width, r.height) / 24f;
                if (s <= 0f) return;
                var p = ctx.painter2D;
                p.strokeColor = color;
                p.lineWidth = 2.2f * s;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                void Line(float x0, float y0, float x1, float y1)
                {
                    p.BeginPath(); p.MoveTo(V(x0, y0)); p.LineTo(V(x1, y1)); p.Stroke();
                }
                switch (kind)
                {
                    case "explore":
                        p.BeginPath(); p.Arc(V(12, 12), 9f * s, 0f, 360f); p.Stroke();
                        p.BeginPath(); p.MoveTo(V(15.5f, 8.5f)); p.LineTo(V(13.5f, 13.5f));
                        p.LineTo(V(8.5f, 15.5f)); p.LineTo(V(10.5f, 10.5f)); p.ClosePath(); p.Stroke();
                        break;
                    case "gather":
                        Line(4, 20, 11, 13);
                        p.BeginPath(); p.MoveTo(V(11, 13)); p.LineTo(V(14, 5)); p.LineTo(V(19, 10)); p.ClosePath(); p.Stroke();
                        break;
                    case "hunt":
                        Line(4, 20, 20, 4); Line(14, 4, 20, 4); Line(20, 4, 20, 10); Line(6, 14, 10, 18);
                        break;
                    case "goat":
                        Line(5, 11, 17, 11); Line(5, 11, 5, 18); Line(8, 11, 8, 18); Line(14, 11, 14, 18); Line(17, 11, 17, 18);
                        Line(17, 11, 20, 7); Line(20, 7, 18, 4);
                        break;
                    case "boar":
                        p.BeginPath(); p.MoveTo(V(4, 16)); p.LineTo(V(6, 9)); p.LineTo(V(17, 9)); p.LineTo(V(21, 13));
                        p.LineTo(V(17, 16)); p.ClosePath(); p.Stroke();
                        Line(7, 16, 7, 20); Line(15, 16, 15, 20); Line(20, 13, 22, 11);
                        break;
                    default: // "party": a pennant on a post over a shore line
                        Line(8, 4, 8, 20); Line(3, 20, 21, 20);
                        p.BeginPath(); p.MoveTo(V(8, 4)); p.LineTo(V(18, 7)); p.LineTo(V(8, 10)); p.Stroke();
                        break;
                }
            }
        }
    }
}
