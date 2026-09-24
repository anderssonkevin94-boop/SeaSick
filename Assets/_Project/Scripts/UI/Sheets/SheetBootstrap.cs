using System.Reflection;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Which object opens which sheet.**
    ///
    /// The parchment sheets are content; this is the one place that says what
    /// a tap on a thing in the world MEANS. It runs once, after the first
    /// scene is up, and registers a factory per component type with
    /// `Sheets`. Nothing here holds state: a factory is handed the component
    /// that was tapped and answers with a sheet or with null, and a null is
    /// simply "this thing has nothing to say right now" -- a campfire on an
    /// island the ship has already left, a build site whose row has been
    /// cancelled.
    ///
    /// **Every sheet is a view.** Not one of them owns a number or a rule;
    /// each button calls the same `Outpost` / `CampLoading` / `AnchorController`
    /// method the legacy IMGUI panel called, so the two can coexist until the
    /// old one is deleted.
    public static class SheetBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            // The fire IS the camp. Kevin, 2026-09-21: "press on the campfire
            // to get the options menu for building" -- that menu is now the
            // camp sheet, and the build list is a block inside it.
            Sheets.Register<Campfire>(f => FireFor(SheetBits.OutpostOf(f)));

            // **A length of wall is its own thing, 2026-09-23.** Before
            // the `Building` rule below, because a `WallSegment` IS a
            // `Building` and `Sheets.Register` resolves by the component
            // that was hit -- a wall that opened the camp sheet would have
            // no door for the gate (D5: "tap a built segment -> its
            // sheet -> make this a gate").
            Sheets.Register<WallSegment>(w =>
            {
                if (w == null) return null;
                var camp = w.Camp != null ? w.Camp : SheetBits.OutpostOf(w);
                if (camp == null) return null;
                return new WallSheet(camp, w);
            });

            // A finished building opens its own crafting menu now, 2026-09-23
            // (Kevin: *"a crafting menu in the appropriate buildings"*) --
            // except the fire, which IS the camp, and the watchtower, whose
            // only decision (who stands the watch) already lives on the camp
            // sheet's lookout row. A building with nothing to make and
            // nowhere to go (the pier) opens nothing at all.
            Sheets.Register<Building>(b =>
            {
                if (b == null) return null;
                // A wall has its own sheet, registered above; which one
                // `Sheets` reaches for depends on how it looks the
                // component up, so this says it outright.
                if (b is WallSegment seg)
                {
                    var wcamp = seg.Camp != null ? seg.Camp : SheetBits.OutpostOf(seg);
                    return wcamp != null ? new WallSheet(wcamp, seg) : null;
                }
                var camp = SheetBits.OutpostOf(b);
                if (b.Kind == BuildKind.Fire) return FireFor(camp);
                if (b.Id == OutpostLedger.WatchtowerId) return FireFor(camp, FireSheet.FocusLookout);
                if (camp == null || camp.Ledger == null) return null;
                // The farm has its own sheet since 2026-09-23 (the building
                // template, farm rows); it went to the camp sheet before.
                if (b.Id == BuildPlans.Farm.id) return new FarmSheet(camp, b);
                bool hasRecipes = Recipes.StationHasRecipes(b.Id);
                bool hasUpgrade = Techs.MaxLevel(b.Id) > 1;
                if (!hasRecipes && !hasUpgrade) return FireFor(camp);   // a pier: the fire, as before
                return new StationSheet(camp, b);
            });

            // A drawing on the ground is its own thing: it is not the camp,
            // it is one promise the camp has made.
            Sheets.Register<BuildSite>(s =>
            {
                if (s == null) return null;
                var camp = SheetBits.OutpostOf(s);
                // The drawing carries its own queue row since 2026-09-22;
                // a `BuildSite` with none is one frame from being retired.
                if (camp == null || camp.Ledger == null || s.Row == null) return null;
                return new SiteSheet(camp, s);
            });

            // A villager. Their own orders, not the camp's.
            Sheets.Register<CrewAgent>(a =>
            {
                if (a == null) return null;
                var camp = SheetBits.OutpostOf(a);
                if (camp == null) return null;
                var row = camp.HandNamed(a.DisplayName);
                if (row == null) return null;         // aboard, or somebody else's
                return new HandSheet(camp, row.name);
            });

            // Three ways to point at the same ship.
            Sheets.Register<ShipMotor>(m => ShipFor());
            Sheets.Register<AnchorController>(a => ShipFor());
            Sheets.Register<Dock>(d => ShipFor());
        }

        static ISheet FireFor(Outpost camp, string focus = null)
        {
            if (camp == null || camp.Ledger == null) return null;
            if (!camp.HasCamp && !camp.Building) return null;
            return new FireSheet(camp, focus);
        }

        internal static ISheet ShipFor()
        {
            var anchor = SheetBits.Anchor;
            if (anchor == null) return null;
            return new ShipSheet();
        }
    }

    /// Odds and ends every sheet needs: the scene singletons, the little
    /// string and colour maps, and the one-child swap that keeps `Refresh`
    /// from rebuilding a whole tree.
    internal static class SheetBits
    {
        // --- the scene's singletons, found once -------------------------------
        //
        // The `!= null` tests are real: a cached reference from a previous
        // play session is a destroyed object and Unity's fake null is what
        // catches it. Same pattern as `CampLoading.Anchor`.

        static AnchorController anchor;
        public static AnchorController Anchor =>
            anchor != null ? anchor : (anchor = Object.FindFirstObjectByType<AnchorController>());

        static VoyageManager voyage;
        public static VoyageManager Voyage =>
            voyage != null ? voyage : (voyage = Object.FindFirstObjectByType<VoyageManager>());

        static ShipHold hold;
        public static ShipHold Hold
        {
            get
            {
                if (hold != null) return hold;
                var a = Anchor;
                hold = a != null ? a.GetComponent<ShipHold>() : null;
                if (hold == null) hold = Object.FindFirstObjectByType<ShipHold>();
                return hold;
            }
        }

        static CrewRoster roster;
        public static CrewRoster Roster =>
            roster != null ? roster : (roster = Object.FindFirstObjectByType<CrewRoster>());

        static ShipMotor motor;
        public static ShipMotor Motor =>
            motor != null ? motor : (motor = Object.FindFirstObjectByType<ShipMotor>());

        /// The ship's transform, for `CampSiting.Begin` (which uses it to
        /// decide which way a new building faces) and for `Outpost.Recall`.
        public static Transform ShipTransform
        {
            get
            {
                var m = Motor;
                if (m != null) return m.transform;
                var a = Anchor;
                return a != null ? a.transform : null;
            }
        }

        /// The camp this component belongs to. Every parked villager,
        /// building and build site is parented under the `Outpost`, so the
        /// parent chain is the answer and nothing has to be registered.
        public static Outpost OutpostOf(Component c)
        {
            if (c == null) return null;
            var o = c.GetComponentInParent<Outpost>();
            if (o != null) return o;
            var isle = c.GetComponentInParent<Island>();
            return isle != null ? Outpost.Of(isle) : null;
        }

        /// The camp the ship is lying at, or null.
        public static Outpost CampAlongside()
        {
            var a = Anchor;
            if (a == null) return null;
            if (a.CurrentState != AnchorController.State.Anchored
                && a.CurrentState != AnchorController.State.Ashore) return null;
            var isle = a.CurrentIsland;
            return isle != null ? Outpost.Of(isle) : null;
        }

        // --- writing ----------------------------------------------------------

        /// Put one child in a holder, throwing away whatever was there. The
        /// holders are small -- a row, a bar, a pill group -- so this is what
        /// "re-bind the live numbers" means for anything that is not a label.
        public static void Swap(VisualElement holder, VisualElement child)
        {
            if (holder == null) return;
            holder.Clear();
            if (child != null) holder.Add(child);
        }

        /// An empty holder with no layout opinions of its own.
        public static VisualElement Holder()
        {
            var v = new VisualElement();
            v.style.flexDirection = FlexDirection.Column;
            return v;
        }

        public static string Initial(string who) =>
            string.IsNullOrEmpty(who) ? "?" : who.Substring(0, 1).ToUpperInvariant();

        /// One mark for what a hand is doing. Deliberately coarse: the row
        /// says the job in words beside it, and this is only so a column of
        /// tokens reads as a camp at a glance.
        public static string JobGlyph(OutpostHand h)
        {
            if (h == null) return "·";
            switch (h.order)
            {
                case OutpostOrder.Build: return "⚒";
                case OutpostOrder.Gather:
                    if (h.target == Res.Game) return "🏹";
                    if (h.target == Res.Stone) return "⛏";
                    if (h.target == Res.Food) return "🌾";
                    return "🪓";
                case OutpostOrder.Work:
                    return h.target == OutpostLedger.WatchtowerId ? "👁" : "⚙";
                default: return "·";
            }
        }

        /// The sheet palette's colour for a resource. `Res.Colour` is the
        /// world's own map and is tuned for piles on the ground; parchment
        /// wants the ink colours instead.
        public static Color Colour(string res)
        {
            switch (res)
            {
                case Res.Timber: return SheetTheme.Timber;
                case Res.Boards: return SheetTheme.Timber;
                case Res.Stone: return SheetTheme.Stone;
                case Res.Ore: return SheetTheme.Brass;
                case Res.Tools: return SheetTheme.Brass;
                case Res.Spice: return SheetTheme.Ember;
                case Res.Game: return SheetTheme.Ember;
                case Res.Food: return SheetTheme.Moss;
                case Res.Meals: return SheetTheme.Moss;
                default: return SheetTheme.InkDim;
            }
        }

        /// "+4/day", "−1.5/day", or **the empty string** when it is too small
        /// to print -- one rule, so every line that prints a rate agrees.
        /// Empty rather than "steady": the caller folds this into a line
        /// that reads perfectly well without it, and a pile that
        /// is not moving does not need a word to say so.
        public static string RateLine(OutpostLedger l, string res)
        {
            if (l == null) return "";
            float perDay = l.RatePerDay(res);
            if (Mathf.Abs(perDay) < 0.05f) return "";
            return (perDay > 0f ? "+" : "−") + Mathf.Abs(perDay).ToString("0.#") + "/day";
        }

        /// **How long the food lasts at the ration they are on.**
        ///
        /// The ledger owns the multiplier (`EatMultiplier`); this is only the
        /// division, and a camp eating nothing at all never runs out.
        public static float FoodDays(OutpostLedger l)
        {
            if (l == null || l.hands.Count == 0) return -1f;
            float perDay = l.hands.Count * OutpostLedger.EatPerHandPerDay * l.EatMultiplier;
            if (perDay <= 0.0001f) return -1f;
            // Upkeep eats from the store, not food reserved in a kitchen input bay.
            return (l.StoreCountOf(Res.Food) + (l.Store(Res.Food)?.part ?? 0f)) / perDay;
        }

        public static string FoodDaysLine(OutpostLedger l)
        {
            float d = FoodDays(l);
            if (l == null || l.hands.Count == 0) return "nobody to feed";
            if (d < 0f) return "they eat nothing — and work like it";
            return $"food lasts {d:0.#} days";
        }

        /// The first hand with nothing to do, or null. What "post a lookout"
        /// and "add a hand" both reach for.
        public static OutpostHand FirstIdle(OutpostLedger l)
        {
            if (l == null) return null;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Idle) return h;
            return null;
        }

        /// Whoever is standing the watch, or null.
        public static OutpostHand Lookout(OutpostLedger l)
        {
            if (l == null) return null;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Work
                    && h.target == OutpostLedger.WatchtowerId) return h;
            return null;
        }

        // --- the hull's repair switch -----------------------------------------
        //
        // The same switch the anchor prompt throws (AnchorController.cs:987),
        // through the public `Repairing` / `ToggleRepair` the controller now
        // exposes: the sheet sets the intention and `Repair(dt)` in `Update`
        // remains the only thing that spends a log or mends a plank.

        public static bool Repairing(AnchorController a) => a != null && a.Repairing;
        public static void ToggleRepair(AnchorController a) { if (a != null) a.ToggleRepair(); }
    }
}
