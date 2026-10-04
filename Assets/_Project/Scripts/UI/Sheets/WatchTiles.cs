using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The villager tiles the Lookout and Raid cards share (menu rework
    /// #1 / #2, 2026-09-27).** A 3-wide grid of the camp's hands in the hand
    /// sheet's tile look: whoever is on watch first (ice ring, a Relieve
    /// pill), then the idle (moss border), then everyone else with their job
    /// in a word. **A tile is the order**: tap a hand on watch and they come
    /// down (`Outpost.OrderIdle`); tap anyone else and they take the tower
    /// (`Outpost.Assign(h, WatchtowerId)`). With every tower already manned
    /// the tap SWAPS: the newcomer takes the post and the one they relieve
    /// takes the newcomer's old job, the same trade `HandSheet` makes.
    ///
    /// When nobody can be posted -- no hands ashore, or no tower standing --
    /// the grid is replaced by a card that says why, never a greyed button.
    ///
    /// Tiles are rebuilt only when the SET of hands changes and re-texted on
    /// the 0.25 s refresh after that (a rebuilt button loses the tap it is
    /// in the middle of). More than fit page with dots, never a scroll.
    internal sealed class WatchTiles
    {
        // --- styles, shared with the two sheets --------------------------------

        static StyleSheet handUss, lookoutUss;
        static bool loaded;

        /// `.st` + Hand.uss + Lookout.uss on a fresh root.
        public static VisualElement Root(string cls)
        {
            var e = StationPage.Root(cls);
            if (!loaded)
            {
                loaded = true;
                handUss = Resources.Load<StyleSheet>("UI/Hand");
                lookoutUss = Resources.Load<StyleSheet>("UI/Lookout");
                if (lookoutUss == null) Debug.LogWarning("[Sheets] Resources/UI/Lookout.uss is missing — the lookout card will be half styled.");
            }
            if (handUss != null) e.styleSheets.Add(handUss);
            if (lookoutUss != null) e.styleSheets.Add(lookoutUss);
            return e;
        }

        public static VisualElement Box(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            return e;
        }

        public static void Set(Label l, string s)
        {
            s = s ?? "";
            if (l != null && l.text != s) l.text = s;
        }

        public static void Show(VisualElement e, bool on)
        {
            if (e == null) return;
            var want = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != want) e.style.display = want;
        }

        /// 0 good, 1 warn, 2 bad -- Hand.uss's chip tones.
        public static void Tone(Label l, int tone)
        {
            l.EnableInClassList("hs-tone--good", tone == 0);
            l.EnableInClassList("hs-tone--warn", tone == 1);
            l.EnableInClassList("hs-tone--bad", tone == 2);
        }

        /// A chip in Hand.uss's row: KEY over value.
        public static Label Chip(VisualElement row, string key, bool first)
        {
            var c = Box("hs-chip");
            if (first) c.AddToClassList("hs-chip--first");
            c.Add(StationPage.Text(key, "hs-chip-k"));
            var v = StationPage.Text("", "hs-chip-v");
            c.Add(v);
            row.Add(c);
            return v;
        }

        /// The header both cards wear: glyph tile · title / sub · pill · ✕.
        public sealed class Head
        {
            public readonly VisualElement Root;
            readonly Label title, sub, pillText;
            readonly VisualElement pill;
            int pillKind = -1;

            public Head(string glyph, string titleText) : this(new Glyph(glyph), titleText) { }

            /// Any drawn glyph in the tile (`StationPage.Glyph` for the small
            /// world-object cards, 2026-09-27).
            public Head(VisualElement glyph, string titleText)
            {
                Root = WatchTiles.Root("st-head");
                var gl = Box("lk-glyph-box");
                gl.Add(glyph);
                Root.Add(gl);

                var words = Box("st-head-words");
                title = StationPage.Text(titleText, "st-title");
                sub = StationPage.Text("", "st-sub");
                sub.style.whiteSpace = WhiteSpace.Normal;
                words.Add(title);

                pill = Box("st-pill");
                pillText = StationPage.Text("", "st-pill-text");
                pillText.pickingMode = PickingMode.Ignore;
                pill.Add(pillText);
                // Tappable when the card says so (`PillTap`): the lookout's
                // pill opens his own sheet (2026-09-27).
                pill.RegisterCallback<ClickEvent>(_ => PillTap?.Invoke());

                // **Hugging, 2026-09-30**: the pill drops under the title,
                // beside the sub line. In the half-screen frame glyph + name
                // + pill + Move + ✕ left the NAME "Cam..." / "Watchto...".
                if (StationPage.Hugging)
                {
                    var line = new VisualElement { pickingMode = PickingMode.Ignore };
                    line.style.flexDirection = FlexDirection.Row;
                    line.style.alignItems = Align.Center;
                    line.style.marginTop = 2f;
                    pill.style.marginRight = 8f;
                    line.Add(pill);
                    sub.style.flexShrink = 1f;
                    sub.style.minWidth = 0f;
                    // Wraps, never cut (2026-10-02 rule: no ellipsis in UI text).
                    sub.style.whiteSpace = WhiteSpace.Normal;
                    line.style.minWidth = 0f;
                    line.Add(sub);
                    words.Add(line);
                    Root.Add(words);
                }
                else
                {
                    words.Add(sub);
                    Root.Add(words);
                    Root.Add(pill);
                }

                var close = new Button(() => Sheets.Close()) { text = "" };
                close.AddToClassList("st-square");
                close.tooltip = "Close";
                close.Add(new StationPage.Glyph("close", StationPage.Ink, "st-glyph"));
                Root.Add(close);
            }

            public void SetSub(string s) => Set(sub, s);

            /// What a tap on the pill does, or null for nothing.
            public System.Action PillTap;

            public void SetPill(string text, int kind)
            {
                Set(pillText, text);
                Show(pill, !string.IsNullOrEmpty(text));
                if (kind == pillKind) return;
                pillKind = kind;
                pill.EnableInClassList("st-pill--wait", kind == StationPage.PillWait);
                pill.EnableInClassList("st-pill--bad", kind == StationPage.PillBad);
            }
        }

        /// **A tower or a sword, drawn** -- the mockup's header glyphs, so no
        /// font glyph or texture is needed.
        public sealed class Glyph : VisualElement
        {
            readonly string kind;

            public Glyph(string kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("lk-glyph");
                generateVisualContent += Draw;
            }

            static readonly Color Wood = new Color32(168, 103, 47, 255);
            static readonly Color Brass = new Color32(201, 147, 57, 255);
            static readonly Color Straw = new Color32(224, 178, 122, 255);
            static readonly Color Door = new Color32(58, 36, 16, 255);
            static readonly Color Steel = new Color32(223, 231, 236, 255);

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float s = Mathf.Min(contentRect.width, contentRect.height) / 32f;
                if (s <= 0f) return;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                void Poly(Color c, params Vector2[] pts)
                {
                    p.fillColor = c;
                    p.BeginPath();
                    p.MoveTo(pts[0]);
                    for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                    p.ClosePath();
                    p.Fill();
                }
                if (kind == "sword")
                {
                    p.lineCap = LineCap.Round;
                    p.strokeColor = Steel;
                    p.lineWidth = 3f * s;
                    p.BeginPath(); p.MoveTo(V(8, 24)); p.LineTo(V(24, 6)); p.Stroke();
                    p.strokeColor = Brass;
                    p.BeginPath(); p.MoveTo(V(7, 19)); p.LineTo(V(13, 25)); p.Stroke();
                    p.BeginPath(); p.MoveTo(V(5, 27)); p.LineTo(V(9, 23)); p.Stroke();
                    return;
                }
                // the tower: legs, deck, roof, door
                Poly(Wood, V(10, 28), V(12, 12), V(20, 12), V(22, 28));
                Poly(Brass, V(8, 8), V(24, 8), V(24, 13), V(8, 13));
                Poly(Straw, V(8, 8), V(16, 3), V(24, 8));
                Poly(Door, V(14, 20), V(18, 20), V(18, 28), V(14, 28));
            }
        }

        // --- the grid ----------------------------------------------------------------

        sealed class Tile
        {
            public Button root;
            public Label initial, name, sub;
            public VisualElement pill;
            public string who;
        }

        readonly Outpost outpost;
        readonly System.Action<string> toast;
        readonly System.Action changed;
        readonly int perPage;
        public readonly VisualElement Grid, Dots, Note;
        readonly Label noteT;
        readonly List<Tile> tiles = new List<Tile>();
        string key;
        int page;

        /// `perPage` is worked out by the sheet from the band BEFORE building
        /// (a page never re-plans under a finger); `toast` shows a line over
        /// the card; `changed` asks the sheet to refresh after an order.
        public WatchTiles(Outpost camp, int perPage, System.Action<string> toast, System.Action changed)
        {
            outpost = camp;
            this.perPage = Mathf.Max(3, perPage);
            this.toast = toast;
            this.changed = changed;
            Grid = Box("hs-grid");
            Grid.pickingMode = PickingMode.Position;
            Dots = Box("st-dots");
            Dots.pickingMode = PickingMode.Position;
            Note = StationPage.Card();
            Note.AddToClassList("lk-note");
            Note.AddToClassList("lk-card--warn");
            Note.pickingMode = PickingMode.Ignore;
            noteT = StationPage.Text("", "lk-note-t");
            Note.Add(noteT);
            Show(Note, false);
        }

        /// Rows of tiles that fit the tall band under `rest` panel units of
        /// everything else on the page (same arithmetic as
        /// `HandSheet.TilesPerPage`).
        public static int PerPage(float rest)
        {
            float frame = SheetHost.FrameSizeScreen().y * SheetHost.PanelScale;
            float avail = frame - SheetHost.BorderPx - 82f - SheetHost.BodyPadPx - 22f;
            const float Row = 122f;
            int rows = Mathf.Clamp(Mathf.FloorToInt((avail - rest) / Row), 1, 3);
            return rows * 3;
        }

        /// Hands on watch first, then the idle, then everyone else -- in
        /// roster order inside each group.
        static List<OutpostHand> Ordered(OutpostLedger l)
        {
            var list = new List<OutpostHand>();
            foreach (var h in l.hands) if (h != null && OnWatch(h)) list.Add(h);
            foreach (var h in l.hands) if (h != null && !OnWatch(h) && h.order == OutpostOrder.Idle) list.Add(h);
            foreach (var h in l.hands) if (h != null && !OnWatch(h) && h.order != OutpostOrder.Idle) list.Add(h);
            return list;
        }

        public static bool OnWatch(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Work && h.target == OutpostLedger.WatchtowerId;

        /// Towers standing here (each takes one lookout).
        public int Towers => outpost != null ? outpost.CountOf(OutpostLedger.WatchtowerId) : 0;

        /// Why nobody can be posted, or null when a tap would take.
        public string Blocker(OutpostLedger l)
        {
            if (l == null) return "";
            if (Towers <= 0)
                return "No watchtower is standing here, so there is nowhere to post a lookout. Raise one from Build › Defence.";
            if (l.hands.Count == 0)
                return "Nobody is ashore to stand watch. Send hands ashore from the ship first.";
            return null;
        }

        public void Refresh(OutpostLedger l)
        {
            if (l == null) return;
            string why = Blocker(l);
            Set(noteT, why);
            Show(Note, why != null);
            Show(Grid, why == null);
            if (why != null) { Show(Dots, false); return; }

            var order = Ordered(l);
            var sb = new System.Text.StringBuilder();
            foreach (var h in order) sb.Append(h.name).Append('|');
            string k = sb.ToString();
            if (k != key)
            {
                key = k;
                Rebuild(order);
            }
            for (int i = 0; i < tiles.Count && i < order.Count; i++) Fill(l, tiles[i], order[i]);
        }

        void Rebuild(List<OutpostHand> order)
        {
            Grid.Clear();
            tiles.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                var t = new Tile();
                t.root = new Button(() => Press(t)) { text = "" };
                t.root.AddToClassList("hs-tile");
                if ((i % perPage) % 3 == 2) t.root.AddToClassList("hs-tile--col3");
                var av = Box("lk-av");
                t.initial = StationPage.Text("", "lk-av-t");
                av.Add(t.initial);
                t.root.Add(av);
                // **Name over job, beside the face (2026-09-30).** Added
                // straight to the row tile they ran together ("Pipidle"),
                // and the absolute "Relieve" pill sat on top of the name.
                // Now the words are one column that ellipsizes, and the
                // pill is gone: the tile IS the button, "on watch" is its
                // state and the eyebrow says "tap again to relieve".
                av.style.marginBottom = 0f;
                var words = Box("hs-tile-words");
                words.style.marginLeft = 6f;
                t.name = StationPage.Text("", "hs-tile-n");
                t.sub = StationPage.Text("", "hs-tile-s");
                words.Add(t.name);
                words.Add(t.sub);
                t.root.Add(words);
                t.pill = Box("lk-pill");
                t.pill.Add(StationPage.Text("Relieve", "lk-pill-t"));
                tiles.Add(t);
                Grid.Add(t.root);
            }

            Dots.Clear();
            int pages = Mathf.Max(1, Mathf.CeilToInt(order.Count / (float)perPage));
            if (page >= pages) page = pages - 1;
            if (pages > 1)
                for (int p = 0; p < pages; p++)
                {
                    int pg = p;
                    var b = new Button(() => { page = pg; ShowPage(); }) { text = "" };
                    b.AddToClassList("st-dot-btn");
                    b.Add(Box("st-dot"));
                    Dots.Add(b);
                }
            Show(Dots, pages > 1);
            ShowPage();
        }

        void ShowPage()
        {
            for (int i = 0; i < tiles.Count; i++)
                tiles[i].root.style.display = i / perPage == page ? DisplayStyle.Flex : DisplayStyle.None;
            for (int p = 0; p < Dots.childCount; p++)
                Dots[p][0].EnableInClassList("st-dot--on", p == page);
        }

        void Fill(OutpostLedger l, Tile t, OutpostHand h)
        {
            t.who = h.name;
            bool on = OnWatch(h);
            bool idle = h.order == OutpostOrder.Idle;
            Set(t.initial, SheetBits.Initial(h.name));
            Set(t.name, h.name);
            Set(t.sub, on ? "on watch" : h.walkingIn ? "walking up" : JobWord(h));
            t.root.EnableInClassList("hs-tile--on", on);
            t.root.EnableInClassList("lk-tile--idle", idle && !on);
            Show(t.pill, on);
        }

        /// The job in a word or two: "no job", "reserve", "timber", "hunting", "sawyer",
        /// "building".
        public static string JobWord(OutpostHand h)
        {
            switch (h.order)
            {
                // The player's reserve is not "no job" (2026-10-03).
                case OutpostOrder.Idle: return OutpostLedger.Reserve(h) ? "reserve" : "no job";
                case OutpostOrder.Build: return "building";
                case OutpostOrder.Gather:
                    if (h.target == Res.Game) return "hunting";
                    return ResDefs.Label(h.target);
                case OutpostOrder.Work:
                {
                    var plan = BuildPlans.Named(h.target);
                    if (!string.IsNullOrEmpty(plan.position)) return plan.position;
                    return string.IsNullOrEmpty(plan.label) ? "working" : plan.label;
                }
                default: return h.Doing;
            }
        }

        // --- the orders -----------------------------------------------------------

        void Press(Tile t)
        {
            var l = outpost != null ? outpost.Ledger : null;
            var h = outpost != null && t.who != null ? outpost.HandNamed(t.who) : null;
            if (l == null || h == null) return;
            string why = Blocker(l);
            if (why != null) { toast?.Invoke(why); return; }
            if (OnWatch(h)) Relieve(h);
            else Post(h);
            changed?.Invoke();
        }

        /// Come down off the tower; free hands help at the sites on their own.
        public void Relieve(OutpostHand h)
        {
            if (outpost == null || h == null) return;
            outpost.OrderIdle(h, reserve: false);
            toast?.Invoke($"{h.name} comes down from the tower.");
        }

        /// Up the tower. With every tower manned the first lookout swaps
        /// with the newcomer and takes the newcomer's old job.
        public void Post(OutpostHand h)
        {
            var l = outpost != null ? outpost.Ledger : null;
            if (l == null || h == null) return;
            OutpostHand other = null;
            int manned = 0;
            foreach (var o in l.hands)
                if (o != null && o != h && OnWatch(o)) { manned++; if (other == null) other = o; }
            bool swap = other != null && manned >= Towers;
            var oldOrder = h.order;
            string oldTarget = h.target;
            if (!outpost.Assign(h, OutpostLedger.WatchtowerId))
            {
                toast?.Invoke("That order didn't take.");
                return;
            }
            if (swap)
            {
                GiveJob(other, oldOrder, oldTarget);
                toast?.Invoke($"{h.name} takes the watch from {other.name}.");
            }
            else toast?.Invoke($"{h.name} takes the watch.");
        }

        void GiveJob(OutpostHand o, OutpostOrder order, string target)
        {
            switch (order)
            {
                case OutpostOrder.Work:
                    if (target != OutpostLedger.WatchtowerId && outpost.Assign(o, target)) return;
                    break;
                case OutpostOrder.Gather:
                    if (outpost.OrderGather(o, target)) return;
                    break;
                case OutpostOrder.Build:
                    if (outpost.OrderBuild(o)) return;
                    break;
            }
            outpost.OrderIdle(o, reserve: false);
        }

        /// The best hand for the primary "Post X" button: the first idle one
        /// not already up the tower, or null.
        public static OutpostHand FirstFree(OutpostLedger l)
        {
            if (l == null) return null;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Idle && !h.walkingIn) return h;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Idle) return h;
            return null;
        }

        /// The standing towers, in raised order ("Show tower" cycles them).
        public static List<Building> TowersOf(Outpost camp)
        {
            var list = new List<Building>();
            if (camp == null) return list;
            foreach (var b in camp.Built)
                if (b != null && b.Id == OutpostLedger.WatchtowerId) list.Add(b);
            return list;
        }

        /// Swing the island camera onto a building without locking it
        /// there: follow for one call (which re-centres the focus), then let
        /// go -- the view stays put and the next drag is the player's.
        public static void LookAt(Building b)
        {
            if (b == null) return;
            CameraRig.IslandCam.Follow(b.transform);
            CameraRig.IslandCam.StopFollow();
        }
    }
}
