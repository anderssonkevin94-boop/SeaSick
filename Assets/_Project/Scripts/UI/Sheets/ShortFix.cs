using System;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **"Every problem shows its fix as a button" (island UI rule 2),
    /// 2026-09-30, phase 3.** One place that answers "this sheet says X is
    /// SHORT -- what does the player press?", so `BuildSheet`, `SiteSheet`,
    /// `CampfireSheet` and `StationSheet` cannot answer it four ways.
    ///
    /// - **A gatherable** (`Res.IsGatherable`: timber, stone, ore, spice,
    ///   food, game) -> **"Gather X"**: the first idle hand goes after it
    ///   (`Outpost.OrderGather`, the call `GatherSheet`'s send button makes);
    ///   with nobody idle, or the order refused, `GatherSheet` opens and says
    ///   who is on it and why. A resource that is WORKED OUT (`standing < 1`,
    ///   never Game) skips the order and goes straight to that page.
    /// - **Game** needs a spear first: while `HunterBlocker()` says so, the fix
    ///   is the forge (`CampAlerts.Forge`, the same door the alert chip uses).
    /// - **A made or treated good** (boards, brick, a spear...) -> the recipe
    ///   system finds the station: built -> **"Make X"** opens its page; the
    ///   station is built but the fire is too low for the recipe -> **"Raise
    ///   Campfire to N"**; no station of that kind yet -> **"Build <station>"**
    ///   opens the Build sheet on its plan (or the fire, when the plan itself
    ///   is locked by the fire).
    /// - Anything else (a hunt drop, an unknown id) has no fix; `Fix.Valid`
    ///   is false and the caller hides its button.
    ///
    /// **The resolved `Fix` is cheap** (no closures, no lists), because
    /// sheets ask it on every 0.25 s refresh; the sheet-opening happens only
    /// in `Run`, at the tap. `Slot` is the one-button widget for sheets that
    /// have no fix row of their own: built once, then only shown, hidden and
    /// re-labelled by `Bind`.
    public static class ShortFix
    {
        public enum Kind { None, Gather, Station, Build, Raise, Forge }

        public struct Fix
        {
            public Kind kind;
            public string label;
            /// Gather: the resource. Everything else: the resource being fixed.
            public string res;
            /// Station: the built station to open.
            public Building station;
            /// Build: the plan id.
            public string planId;

            public bool Valid => kind != Kind.None;

            /// Do the fix. True when it acted without opening a sheet (a hand
            /// was sent), so the caller can refresh in place.
            public bool Run(Outpost camp)
            {
                if (camp == null) return false;
                switch (kind)
                {
                    case Kind.Gather:
                    {
                        var l = camp.Ledger;
                        if (l == null) return false;
                        var idle = SheetBits.FirstIdle(l);
                        var stock = l.Stock(res);
                        bool workedOut = res != Res.Game && stock != null && stock.standing < 1f;
                        if (idle != null && !workedOut && camp.OrderGather(idle, res)) return true;
                        Sheets.Open(new GatherSheet(camp, res));
                        return false;
                    }
                    case Kind.Station:
                        if (station != null) Sheets.Open(Sheets.TryCreateFor(station) ?? new StationSheet(camp, station));
                        return false;
                    case Kind.Build:
                        Sheets.Open(new BuildSheet(camp, planId));
                        return false;
                    case Kind.Raise:
                        Sheets.Open(new CampfireSheet(camp));
                        return false;
                    case Kind.Forge:
                    {
                        var s = CampAlerts.Forge(camp);
                        if (s != null) Sheets.Open(s);
                        return false;
                    }
                }
                return false;
            }
        }

        /// **The most-missing resource of several**, without allocating:
        /// `Add(res, n)` for each shortfall; a gatherable wins over a made
        /// good (a hand can simply go and fetch it), then the bigger gap wins.
        /// This is the rule `BuildSheet`'s fix row has always used.
        public struct Most
        {
            string res;
            int n;
            bool gatherable;

            public string Res => res;

            public void Add(string r, int missing)
            {
                if (string.IsNullOrEmpty(r) || missing <= 0) return;
                bool g = SeaSick.World.Res.IsGatherable(r);
                if (res == null || (g && !gatherable) || (g == gatherable && missing > n))
                { res = r; n = missing; gatherable = g; }
            }
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// The fix for `res` being short at `camp`; `Valid` is false when
        /// there is none.
        public static Fix For(Outpost camp, string res)
        {
            var none = new Fix();
            var l = camp != null ? camp.Ledger : null;
            if (l == null || string.IsNullOrEmpty(res)) return none;
            string name = Cap(ResDefs.Label(res));

            if (SeaSick.World.Res.IsGatherable(res))
            {
                // Game needs a spear in hand before any hunter can go.
                if (res == SeaSick.World.Res.Game && l.HunterBlocker() != null)
                {
                    var spear = ForMade(camp, l, SeaSick.World.Res.Spear);
                    if (spear.kind == Kind.Station || spear.kind == Kind.Build)
                        spear.kind = Kind.Forge;
                    if (spear.Valid) { spear.res = res; return spear; }
                }
                return new Fix { kind = Kind.Gather, res = res, label = "Gather " + name };
            }
            return ForMade(camp, l, res);
        }

        /// **"Raise Campfire to N"**: the fix for anything gated by the fire's
        /// level (a locked recipe, a locked upgrade). Not valid at the top
        /// level, where there is nothing to press.
        public static Fix RaiseFire(Outpost camp, string res = null)
        {
            var l = camp != null ? camp.Ledger : null;
            var next = l != null ? l.NextCampfire : null;
            if (next == null) return default;
            return new Fix { kind = Kind.Raise, res = res, label = "Raise Campfire to " + RecipeGraph.Roman(next.level) };
        }

        /// A made or treated good: the station that produces it (see the type
        /// comment for the order of preference).
        static Fix ForMade(Outpost camp, OutpostLedger l, string res)
        {
            string name = Cap(ResDefs.Label(res));
            Fix best = default;
            int bestScore = 0;   // 3 make, 2 raise, 1 build
            foreach (var r in Recipes.All)
            {
                if (r == null || r.makes != res) continue;
                Building built = null;
                foreach (var b in camp.Built)
                    if (b != null && b.Id == r.station) { built = b; break; }

                if (built != null && l.CampfireLevel >= r.campfireLevel)
                    return new Fix { kind = Kind.Station, res = res, station = built, label = "Make " + name };

                var next = l.NextCampfire;
                if (built != null && next != null)
                {
                    if (bestScore < 2)
                    {
                        bestScore = 2;
                        best = new Fix { kind = Kind.Raise, res = res, label = "Raise Campfire to " + RecipeGraph.Roman(next.level) };
                    }
                    continue;
                }
                if (built != null) continue;   // top fire and still locked: nothing to press

                if (bestScore < 1)
                {
                    var plan = BuildPlans.Named(r.station);
                    if (string.IsNullOrEmpty(plan.id)) continue;
                    if (Techs.PlanLevel(plan.id) > l.CampfireLevel && next != null)
                        best = new Fix { kind = Kind.Raise, res = res, label = "Raise Campfire to " + RecipeGraph.Roman(next.level) };
                    else
                        best = new Fix { kind = Kind.Build, res = res, planId = plan.id, label = "Build " + Cap(plan.label) };
                    bestScore = 1;
                }
            }
            return best;
        }

        // --- the button ---------------------------------------------------------------

        /// **One fix button, kept between refreshes.** `button` is added to
        /// the sheet once; `Bind` shows, hides and re-labels it (touching a
        /// style or the text only when it changed). Styled inline, so it looks
        /// the same in the `.st` pages, the `sheet-` frame and the Build strip:
        /// ice fill, dark ink, 44 px high.
        public sealed class Slot
        {
            public readonly Button button;
            Outpost camp;
            Fix fix;
            readonly Action after;

            /// `after` runs when the fix acted in place (a hand was sent), so
            /// the sheet can refresh at once.
            public Slot(Action after = null)
            {
                this.after = after;
                button = new Button(OnClick) { text = "" };
                button.AddToClassList("sf-fix");
                var s = button.style;
                s.minHeight = 44; s.height = StyleKeyword.Auto;
                s.marginTop = 8; s.marginBottom = 0; s.marginLeft = 0; s.marginRight = 0;
                s.paddingLeft = 14; s.paddingRight = 14; s.paddingTop = 0; s.paddingBottom = 0;
                s.flexShrink = 0;
                s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 0;
                s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 10;
                s.backgroundColor = (Color)new Color32(164, 210, 232, 255);
                s.color = (Color)new Color32(11, 23, 32, 255);
                s.fontSize = 15;
                s.unityFontStyleAndWeight = FontStyle.Bold;
                s.unityTextAlign = TextAnchor.MiddleCenter;
                s.whiteSpace = WhiteSpace.Normal;
                s.display = DisplayStyle.None;
            }

            public bool Visible => button.style.display != DisplayStyle.None;

            /// Point the button at the fix for `res` (null = nothing short:
            /// hidden).
            public void Bind(Outpost camp, string res) => Bind(camp, For(camp, res));

            public void Bind(Outpost camp, Fix f)
            {
                this.camp = camp;
                fix = f;
                var want = f.Valid ? DisplayStyle.Flex : DisplayStyle.None;
                if (button.style.display != want) button.style.display = want;
                if (f.Valid && button.text != f.label) button.text = f.label;
            }

            void OnClick()
            {
                if (fix.Run(camp)) after?.Invoke();
            }
        }
    }
}
