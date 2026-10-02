using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The island top bar's three numbers, 2026-09-29** (Kevin: *"i want
    /// to have mood. just a percentage ... a n/n showing working people
    /// compared to total people ... food ... how many days i have food for.
    /// give it a up or down arrow"*). One place computes them so the bar and
    /// the sheets they open (`MoodSheet`, `WorkersSheet`, `FoodSheet`) can
    /// never disagree.
    ///
    /// **It reads; it never decides.** Every term mirrors the ledger code
    /// that actually moves the number (`EatStep` for mood and eating,
    /// `RatePerDay` for benches and gatherers, `FarmDay` for the plots).
    ///
    /// **Units: sky days.** The top bar says "Day N" (`TimeOfDay.Day`, the
    /// sun's 480 s day), so everything here is per SKY day. The ledger's
    /// production rates are per WORK day (a fixed 180 s,
    /// `TimeOfDay.WorkDaySeconds`) and are divided by
    /// `TimeOfDay.SkyDaysPerWorkDay` on the way out; eating and mood already
    /// run per sky day inside `EatStep` (`days = workDays * SkyDaysPerWorkDay`),
    /// so `FillPerDay`-style terms are used as they are.
    public static class CampReadouts
    {
        // --- mood ---------------------------------------------------------------

        /// **Mean mood 0..1 of the hands who are up** (a downed hand's mood is
        /// frozen in `EatStep` and he is not the camp's temper right now);
        /// every hand when all are down; **-1 when nobody lives here**.
        public static float Mood01(OutpostLedger l)
        {
            if (l == null || l.hands == null || l.hands.Count == 0) return -1f;
            float sum = 0f, all = 0f;
            int n = 0, nAll = 0;
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                all += h.mood; nAll++;
                if (h.downed) continue;
                sum += h.mood; n++;
            }
            if (n > 0) return Mathf.Clamp01(sum / n);
            return nAll > 0 ? Mathf.Clamp01(all / nAll) : -1f;
        }

        /// "content" (95 %+), "uneasy", "angry" (under 50 %, the line
        /// `OutpostHand.Angry` and `WorkFactor` start docking labour at).
        public static string MoodWordFor(float mood01) =>
            mood01 < 0f ? "" : mood01 < 0.5f ? "angry" : mood01 < 0.95f ? "uneasy" : "content";

        /// Mood change per sky day for one hand, as `EatStep` applies it
        /// (0 for a downed hand, whom `EatStep` skips). Not clamped.
        public static float MoodDriftPerDay(OutpostLedger l, int index)
        {
            if (l == null || index < 0 || index >= l.hands.Count) return 0f;
            var h = l.hands[index];
            if (h == null || h.downed) return 0f;
            float d = 0f;
            if (l.rations == Rations.None || h.full <= 0f) d -= OutpostLedger.MoodDropPerHungryDay;
            else
            {
                d += l.rations == Rations.Half
                    ? -OutpostLedger.MoodDropPerHungryDay * 0.5f
                    : OutpostLedger.MoodRecoverPerFedDay;
                d += FoodBook.MoodPerDay(h.lastMeal);
            }
            if (l.IsHandWarm(index)) d += OutpostLedger.WarmMoodBonusPerDay;
            return d;
        }

        /// The camp's mean mood drift per sky day, a hand at 100 % not
        /// counting a rise it cannot have.
        public static float MoodTrendPerDay(OutpostLedger l)
        {
            if (l == null || l.hands.Count == 0) return 0f;
            float sum = 0f; int n = 0;
            for (int i = 0; i < l.hands.Count; i++)
            {
                var h = l.hands[i];
                if (h == null || h.downed) continue;
                float d = MoodDriftPerDay(l, i);
                if (d > 0f && h.mood >= 1f) d = 0f;
                if (d < 0f && h.mood <= 0f) d = 0f;
                sum += d; n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        // --- working / total -----------------------------------------------------

        /// Everyone on the roster, up or down.
        public static int Total(OutpostLedger l) => l != null && l.hands != null ? l.hands.Count : 0;

        /// **Hands actually at a job**: `Tally`'s building + hauling + working
        /// + gathering (sleep and evening read as the job underneath, as the
        /// tally reads them), **minus anyone held up** -- a sawyer with no
        /// order or a gatherer facing a full store is on a job but is not
        /// working, and a count that calls him working is the count Kevin
        /// asked to see through. `WorkersSheet` lists every hand not counted
        /// here, with why.
        public static int Working(OutpostLedger l)
        {
            if (l == null || l.hands == null) return 0;
            int n = 0;
            foreach (var h in l.hands)
                if (h != null && KindOf(l, h, out _) == HandKind.Working) n++;
            return n;
        }

        public enum HandKind { Working, Unassigned, HeldUp, Down, Busy }

        /// **Which section of `WorkersSheet` this hand is in, and why.**
        /// Mirrors `OutpostLedger.Word(h, routine: false)` (the tally's view):
        /// `StatusWord` with sleep/evening read as the job underneath.
        public static HandKind KindOf(OutpostLedger l, OutpostHand h, out string why)
        {
            why = "";
            if (l == null || h == null) return HandKind.Busy;
            string w = l.StatusWord(h);
            if (w == "Sleeping" || w == "Evening") w = UnderWord(l, h);
            switch (w)
            {
                case "Downed":
                    why = h.Doing;
                    if (h.downed && !string.IsNullOrEmpty(h.downedCause)) why += " · " + h.downedCause;
                    return HandKind.Down;
                case "Stuck":
                    why = string.IsNullOrEmpty(h.bodyBlocked) ? "stuck" : h.bodyBlocked;
                    return HandKind.HeldUp;
                case "Reserve":
                    why = "held in reserve";
                    return HandKind.Unassigned;
                case "No work":
                    why = h.order == OutpostOrder.Build ? "builder · nothing to build"
                        : h.order == OutpostOrder.Gather ? "gatherer · nothing picked"
                        : "no job";
                    return HandKind.Unassigned;
                case "Building":
                case "Hauling":
                case "Working":
                case "Gathering":
                case "Hunting":
                    if (!h.Hauling && l.Stalled(h))
                    {
                        why = JobOf(l, h) + " · " + l.StallReason(h);
                        return HandKind.HeldUp;
                    }
                    why = JobOf(l, h);
                    return HandKind.Working;
                // **Runners (2026-10-02).** A runner's words ("Runner,
                // waiting", "Running 6 boards to Sawmill") are working --
                // waiting on call is his job; "Waiting for a runner" is a
                // station worker held up for want of one, and "Idle" is the
                // ledger's word for a hand with no job.
                case "Waiting for a runner":
                    why = JobOf(l, h) + " · waiting for a runner";
                    return HandKind.HeldUp;
                case "Idle":
                    why = "no job";
                    return HandKind.Unassigned;
                default:
                    if (OutpostLedger.IsRunner(h) && (w.StartsWith("Runner") || w.StartsWith("Running")))
                    {
                        why = JobOf(l, h);
                        return HandKind.Working;
                    }
                    // Pouting, rescuing, a raid's fighting/hiding: busy with
                    // something that is not his job, and not the tally's.
                    why = h.Doing;
                    return HandKind.Busy;
            }
        }

        /// `OutpostLedger.Word`'s tail (after the raid/downed/stuck states,
        /// which `StatusWord` already answered before any "Sleeping").
        static string UnderWord(OutpostLedger l, OutpostHand h)
        {
            if (OutpostLedger.Reserve(h)) return h.Hauling ? "Hauling" : "Reserve";
            if (h.TopUpTrip) return "Gathering";
            switch (h.order)
            {
                case OutpostOrder.Gather:
                    if (string.IsNullOrEmpty(h.target)) return "No work";
                    return h.target == Res.Game ? "Hunting" : "Gathering";
                case OutpostOrder.Work:
                    return "Working";
                case OutpostOrder.Build:
                    if (l.BuildSiteFor(h) != null) return "Building";
                    return h.Hauling ? "Hauling" : "No work";
                default:
                    return h.Hauling ? "Hauling" : "No work";
            }
        }

        /// "sawyer at the sawmill", "gathering stone", "builder · shelter" --
        /// `PeopleSheet.JobOf`'s wording.
        public static string JobOf(OutpostLedger l, OutpostHand h)
        {
            if (l == null || h == null) return "";
            if (h.TopUpTrip) return h.Doing;
            switch (h.order)
            {
                case OutpostOrder.Work:
                {
                    if (OutpostLedger.IsRunner(h)) return "runner · pushes goods around the camp";
                    string label = BuildPlans.Named(h.target).label;
                    string post = BuildPlans.PositionAt(h.target);
                    if (string.IsNullOrEmpty(post)) post = "working";
                    return string.IsNullOrEmpty(label) ? post : $"{post} at the {label}";
                }
                case OutpostOrder.Build:
                {
                    var site = l.BuildSiteFor(h);
                    string label = site == null ? null
                        : site.isWall ? "wall" : BuildPlans.Named(site.planId).label;
                    return string.IsNullOrEmpty(label) ? "builder" : "builder · " + label;
                }
                case OutpostOrder.Gather:
                    if (h.target == Res.Game) return "hunting";
                    return string.IsNullOrEmpty(h.target) ? "gathering" : "gathering " + h.target.ToLowerInvariant();
                default:
                    return h.Hauling ? "hauling" : "no job";
            }
        }

        // --- food ------------------------------------------------------------------

        /// Hands who eat: everyone not downed (`EatStep` skips the downed).
        public static int Eaters(OutpostLedger l)
        {
            if (l == null || l.hands == null) return 0;
            int n = 0;
            foreach (var h in l.hands) if (h != null && !h.downed) n++;
            return n;
        }

        /// **Fill the camp eats per sky day** at the current rations: one
        /// fill per hand per sky day (`EatStep` converts its work-day step to
        /// sky days before draining `EatPerHandPerDay`), halved on half
        /// rations, nothing on none.
        public static float FoodEatenPerDay(OutpostLedger l) =>
            l == null ? 0f : Eaters(l) * OutpostLedger.EatPerHandPerDay * l.EatMultiplier;

        /// **Sky days the store feeds everybody** (`FoodFill`, saved dishes
        /// left out, as `DaysOfFood`); **-1 when nobody eats** (no hands, all
        /// down, or rations off -- the store never runs down then).
        public static float FoodDays(OutpostLedger l)
        {
            float eat = FoodEatenPerDay(l);
            if (l == null || eat <= 1e-4f) return -1f;
            return l.FoodFill() / eat;
        }

        /// **Net food fill per sky day: coming in minus eaten.** >0 rising.
        /// See `FoodInPerDay` for what "coming in" counts.
        public static float FoodTrendPerDay(OutpostLedger l) =>
            l == null ? 0f : FoodInPerDay(l) - FoodEatenPerDay(l);

        /// **Fill per sky day arriving in the store, day and night averaged.**
        ///
        /// Counted as the change in EATABLE fill (`FoodBook.Fill`, saved
        /// dishes out, the same basis `FoodFill` and so `FoodDays` use):
        /// `sum over edibles of NetPerDay(res) x Fill(res)`. Cooking is a
        /// conversion and nets itself out: a kitchen making veg stew adds the
        /// stew's fill and subtracts the raw potatoes' fill it eats (raw
        /// potato is edible at `RawFill`); meat, flour, wheat and onion have
        /// no fill of their own, so a hunter's meat shows up as fill only
        /// once the kitchen turns it into grilled meat. Nothing is counted
        /// twice.
        public static float FoodInPerDay(OutpostLedger l)
        {
            if (l == null) return 0f;
            Cache(l);
            return cacheIn;
        }

        /// **One resource's net change per sky day**, day/night averaged:
        /// the ledger's `RatePerDay` (benches, gatherers, hunters; per WORK
        /// day, so divided by `SkyDaysPerWorkDay`) with the farm's legacy
        /// potato term swapped for the real plot model (`PlotUnitsPerDay`).
        public static float NetPerDay(OutpostLedger l, string res)
        {
            if (l == null || string.IsNullOrEmpty(res)) return 0f;
            Cache(l);
            return cacheNet.TryGetValue(res, out float v) ? v : ComputeNet(l, res);
        }

        /// The resources the food sheet lists: every edible plus the
        /// kitchen's fill-less inputs.
        public static readonly string[] FoodLines = BuildFoodLines();

        static string[] BuildFoodLines()
        {
            var list = new List<string>();
            foreach (var e in FoodBook.Edibles) list.Add(e.res);
            foreach (var r in new[] { Res.Meat, Res.Flour, Res.Wheat, Res.Onion })
                if (!list.Contains(r)) list.Add(r);
            return list.ToArray();
        }

        // Four refreshes a second from up to two readers (bar + sheet): the
        // per-resource pass is cached for a quarter second per ledger.
        static OutpostLedger cacheOf;
        static float cacheAt = -1f;
        static float cacheIn;
        static readonly Dictionary<string, float> cacheNet = new Dictionary<string, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cacheOf = null; cacheAt = -1f; cacheNet.Clear(); }

        static void Cache(OutpostLedger l)
        {
            float now = Time.unscaledTime;
            if (ReferenceEquals(cacheOf, l) && now - cacheAt < 0.25f && now >= cacheAt) return;
            cacheOf = l;
            cacheAt = now;
            cacheNet.Clear();
            float fill = 0f;
            foreach (var res in FoodLines)
            {
                float net = ComputeNet(l, res);
                cacheNet[res] = net;
                if (!l.DishSaved(res)) fill += net * FoodBook.Fill(res);
            }
            cacheIn = fill;
        }

        static float ComputeNet(OutpostLedger l, string res)
        {
            float perWorkDay;
            // `RatePerDay` reads `DayNightWorkScale`, which is 0 all night:
            // read it at full pace and apply the day's average pace instead,
            // so the arrow does not flip to "falling" every sunset.
            bool known = OutpostLedger.ActiveHourKnown;
            try
            {
                OutpostLedger.ActiveHourKnown = false;
                perWorkDay = l.RatePerDay(res) - FarmLegacy(l, res);
            }
            finally { OutpostLedger.ActiveHourKnown = known; }
            float bench = perWorkDay * DayPace() / Mathf.Max(1e-4f, TimeOfDay.SkyDaysPerWorkDay);
            return bench + PlotUnitsPerDay(l, res, out _, out _);
        }

        /// `RatePerDay` still prices a farmhand at the Farm plan's old
        /// flat potato rate (`BuildPlans.Farm.makes/rate`, no recipes), but
        /// `FarmDay` has grown plots since the food rework -- the term is
        /// taken back out here and the plots put in instead. Mirrors the
        /// Work branch of `RatePerDay` term for term.
        static float FarmLegacy(OutpostLedger l, string res)
        {
            var farm = BuildPlans.Farm;
            if (res != farm.makes || !l.built.Contains(farm.id)) return 0f;
            float r = 0f;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work || h.target != farm.id) continue;
                if (l.Stalled(h)) continue;
                int lv = l.LevelOf(farm.id, l.OrdinalOfHand(h));
                float rate = farm.rate * Techs.RateMul(farm.id, lv);
                if (rate <= 0f) continue;
                r += rate * OutpostLedger.WorkFactorOn(h, res) * l.PriorityMultiplier(res);
            }
            return r;
        }

        /// Share of a whole day the camp is awake and at work
        /// (`DayNightWorkScale` averaged round the clock).
        public static float DayPace()
        {
            float s = 0f;
            for (int k = 0; k < 48; k++)
                if (SeaSick.World.Life.CampLifeTuning.IsAwakeHour(k * 0.5f)) s += SeaSick.World.Life.CampLifeTuning.AwakeWorkScale;
            return s / 48f;
        }

        /// **Units of `crop` a sky day from the farm plots.** A plot with a
        /// crop on it at a farm that has a farmhand yields `yield` every
        /// grow + plant + harvest seconds (real time, `FoodBook.Crops`);
        /// plots at a farm with nobody on it only ripen and wait. The
        /// farmhand's walk to the store and the night's wait for a ripe plot
        /// are left out, so this is the plots' pace, a little generous.
        public static float PlotUnitsPerDay(OutpostLedger l, string crop, out int plots, out bool unmanned)
        {
            plots = 0; unmanned = false;
            if (l == null || l.plots == null || FoodBook.Crop(crop) == null) return 0f;
            float units = 0f;
            foreach (var p in l.plots)
            {
                if (p == null || p.crop != crop) continue;
                if (!l.CropUnlocked(p, crop, out _)) continue;
                plots++;
                if (!FarmManned(l, p.farm)) { unmanned = true; continue; }
                var def = FoodBook.Crop(crop);
                float cycle = def.growSeconds + EconomyTuning.PlantSeconds + EconomyTuning.HarvestSeconds;
                if (cycle <= 0f) continue;
                units += def.yield * TimeOfDay.DayLength / cycle;
            }
            return units;
        }

        static bool FarmManned(OutpostLedger l, int farm)
        {
            foreach (var h in l.hands)
                if (h != null && !h.Busy && h.order == OutpostOrder.Work
                    && h.target == BuildPlans.Farm.id && Mathf.Max(0, l.OrdinalOfHand(h)) == farm)
                    return true;
            return false;
        }

        // --- to a hand ---------------------------------------------------------------

        /// **Tap a person: go to where he is now, and open his sheet.** The
        /// same find/pan as `AshoreRail.OpenHand` (body among the camp's
        /// parked agents; else the scene-wide search found him aboard, and
        /// the camera goes to the ship), but for a given camp. A ledger row
        /// with no body yet still opens his `HandSheet`.
        public static void GoToHand(Outpost camp, string who)
        {
            if (string.IsNullOrEmpty(who)) return;
            CrewAgent found = camp != null ? camp.BodyNamed(who) : null;
            bool aboard = false;
            if (found == null)
                foreach (var a in Object.FindObjectsByType<CrewAgent>(
                             FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (a != null && a.DisplayName == who) { found = a; aboard = true; break; }

            var isleCam = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            if (isleCam != null && found != null)
            {
                Vector3 point = found.transform.position;
                if (aboard && Sheets.Anchor != null) point = Sheets.Anchor.transform.position;
                isleCam.PanToWorld(point, CameraRig.IslandCam.BlueprintHeight, 0.6f);
            }

            ISheet sheet = found != null && !aboard ? Sheets.TryCreateFor(found) : null;
            if (sheet == null && camp != null && camp.Ledger != null) sheet = new HandSheet(camp, who);
            if (sheet != null) Sheets.Open(sheet);
        }

        // --- words -------------------------------------------------------------------

        public static string Label(string res)
        {
            string s = ResDefs.Label(res);
            return string.IsNullOrEmpty(s) ? "" : s.ToLowerInvariant();
        }

        public static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// "+1.5", "−0.3" (a real minus), "0" under 0.05.
        public static string Signed(float v, string fmt = "0.#")
        {
            if (Mathf.Abs(v) < 0.05f) return "0";
            return (v > 0f ? "+" : "−") + Mathf.Abs(v).ToString(fmt);
        }

        /// Mood drift as whole percent: "+25%", "−50%".
        public static string SignedPct(float v01)
        {
            int p = Mathf.RoundToInt(v01 * 100f);
            if (p == 0) return "0%";
            return (p > 0 ? "+" : "−") + Mathf.Abs(p) + "%";
        }
    }

    /// **The compact rows the three readout sheets share** (MoodSheet,
    /// WorkersSheet, FoodSheet). Built once, pooled, re-texted on refresh --
    /// never rebuilt, so a tap in flight is never thrown away. Styled with
    /// `CampPages.uss` (the People page's rows, tightened for a portrait
    /// phone: Kevin dislikes scrolling).
    internal static class ReadoutUi
    {
        /// The body: a styled `cp-root` with a clamped, scroller-less scroll
        /// view (only a big camp overflows it). Content goes in `col`.
        public static VisualElement Root(out VisualElement col)
        {
            var root = new VisualElement();
            root.AddToClassList("cp-root");
            CampPages.Styled(root);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cp-scroll");
            scroll.style.marginTop = 0f;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            scroll.Add(col);
            return root;
        }

        /// The big number line: "72%" + "uneasy · falling".
        public static VisualElement Headline(out Label big, out Label small)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;
            row.style.flexShrink = 0f;
            row.style.marginBottom = 2f;
            big = CampPages.Classed(new Label(), "cp-hand-name");
            big.AddToClassList("cp-black");
            big.style.fontSize = 30f;
            small = CampPages.Classed(new Label(), "cp-mood");
            small.style.fontSize = 15f;
            small.style.marginLeft = 8f;
            small.style.marginBottom = 5f;
            small.style.flexShrink = 1f;
            small.style.whiteSpace = WhiteSpace.Normal;
            row.Add(big);
            row.Add(small);
            return row;
        }

        public static Label Eyebrow(VisualElement parent)
        {
            var e = CampPages.Classed(new Label(), "cp-eyebrow");
            e.style.marginTop = 8f;
            e.style.marginBottom = 4f;
            parent.Add(e);
            return e;
        }

        public static void Tone(Label l, string tone)
        {
            l.EnableInClassList("cp-mood--bad", tone == "bad");
            l.EnableInClassList("cp-mood--warm", tone == "warm");
            l.EnableInClassList("cp-tag--ok", tone == "ok");
        }

        public static void SetText(Label l, string s)
        {
            s ??= "";
            if (l.text != s) l.text = s;
        }

        public static void Show(VisualElement e, bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        /// **A reason line**: a short value on the left ("−50%", "+2.5",
        /// "4 d") and the words beside it, with an optional muted hint under
        /// them ("build a hut near the fire"). Since 2026-09-30 (island UI
        /// phase 3, rule 2: every problem shows its fix as a button) a row can
        /// also carry a fix button on its right, 44 px tall, that runs an
        /// action; without one the row is plain text and not tappable.
        public sealed class Lines
        {
            sealed class Row
            {
                public VisualElement row;
                public Label val, text, hint;
                public Button fixBtn;
                public System.Action fix;
                public readonly string[] last = new string[5];
            }

            public readonly VisualElement root;
            readonly List<Row> rows = new List<Row>();
            int used;

            public Lines(VisualElement parent)
            {
                root = new VisualElement();
                root.style.flexDirection = FlexDirection.Column;
                root.style.flexShrink = 0f;
                parent.Add(root);
            }

            public void Begin() { used = 0; }

            /// `fixLabel` + `fix` together make the button; either missing,
            /// none. The row keeps ONE button and swaps its label and action,
            /// so a refresh never rebuilds it (a tap in flight lands).
            public void Add(string value, string tone, string text, string hint = null,
                            string fixLabel = null, System.Action fix = null)
            {
                if (used >= rows.Count) rows.Add(Make());
                var r = rows[used++];
                Show(r.row, true);
                string t = tone ?? "";
                if (r.last[0] != value) { r.last[0] = value; r.val.text = value ?? ""; }
                if (r.last[1] != t) { r.last[1] = t; Tone(r.val, t); }
                if (r.last[2] != text) { r.last[2] = text; r.text.text = text ?? ""; }
                if (r.last[3] != hint)
                {
                    r.last[3] = hint;
                    r.hint.text = hint ?? "";
                    Show(r.hint, !string.IsNullOrEmpty(hint));
                }
                bool has = fix != null && !string.IsNullOrEmpty(fixLabel);
                r.fix = has ? fix : null;
                string label = has ? fixLabel : null;
                if (r.last[4] != label)
                {
                    r.last[4] = label;
                    r.fixBtn.text = label ?? "";
                    Show(r.fixBtn, has);
                }
            }

            public void End()
            {
                for (int i = used; i < rows.Count; i++) { Show(rows[i].row, false); rows[i].fix = null; }
            }

            public int Count => used;

            Row Make()
            {
                var r = new Row();
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.FlexStart;
                row.style.flexShrink = 0f;
                row.style.paddingLeft = 4f;
                row.style.paddingRight = 4f;
                row.style.marginBottom = 5f;
                var val = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-tag");
                val.style.width = 64f;
                val.style.marginLeft = 0f;
                val.style.fontSize = 14f;
                val.style.unityTextAlign = TextAnchor.UpperLeft;
                var words = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-words");
                var text = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-hand-name");
                text.style.fontSize = 14f;
                text.style.whiteSpace = WhiteSpace.Normal;
                var hint = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-job");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.display = DisplayStyle.None;
                words.Add(text);
                words.Add(hint);
                var btn = new Button(() => r.fix?.Invoke()) { text = "" };
                btn.AddToClassList("cp-fix");
                btn.style.display = DisplayStyle.None;
                row.Add(val);
                row.Add(words);
                row.Add(btn);
                root.Add(row);
                r.row = row; r.val = val; r.text = text; r.hint = hint; r.fixBtn = btn;
                return r;
            }
        }

        /// **A tappable person row** (initial disc, name, a tag on the
        /// right, one line under): 48 px tall, the thumb's minimum. The tap
        /// hands back the name the row currently shows.
        public sealed class People
        {
            public readonly VisualElement root;
            readonly System.Action<string> onTap;
            sealed class Row
            {
                public Button btn;
                public Label initial, name, tag, sub;
                public string who;
                public readonly string[] last = new string[5];
            }
            readonly List<Row> rows = new List<Row>();
            int used;

            public People(VisualElement parent, System.Action<string> onTap)
            {
                this.onTap = onTap;
                root = new VisualElement();
                root.style.flexDirection = FlexDirection.Column;
                root.style.flexShrink = 0f;
                parent.Add(root);
            }

            public void Begin() { used = 0; }

            public void Add(string who, string tag, string tone, string sub, bool bad = false)
            {
                if (used >= rows.Count) rows.Add(Make());
                var r = rows[used++];
                Show(r.btn, true);
                r.who = who;
                if (r.last[0] != who)
                {
                    r.last[0] = who;
                    r.name.text = who ?? "";
                    r.initial.text = SheetBits.Initial(who);
                }
                if (r.last[1] != tag) { r.last[1] = tag; r.tag.text = tag ?? ""; }
                string t = tone ?? "";
                if (r.last[2] != t) { r.last[2] = t; Tone(r.tag, t); }
                if (r.last[3] != sub) { r.last[3] = sub; r.sub.text = sub ?? ""; }
                string b = bad ? "1" : "0";
                if (r.last[4] != b) { r.last[4] = b; r.btn.EnableInClassList("cp-hand--angry", bad); }
            }

            public void End()
            {
                for (int i = used; i < rows.Count; i++) Show(rows[i].btn, false);
            }

            public int Count => used;

            Row Make()
            {
                var r = new Row();
                r.btn = new Button(() => onTap?.Invoke(r.who)) { text = "" };
                r.btn.AddToClassList("cp-hand");
                r.btn.style.minHeight = 48f;
                r.btn.style.paddingTop = 4f;
                r.btn.style.paddingBottom = 4f;
                r.btn.style.marginBottom = 4f;
                var avatar = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-avatar");
                avatar.style.width = 30f;
                avatar.style.height = 30f;
                avatar.style.borderTopLeftRadius = 15f;
                avatar.style.borderTopRightRadius = 15f;
                avatar.style.borderBottomLeftRadius = 15f;
                avatar.style.borderBottomRightRadius = 15f;
                r.initial = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-avatar-l");
                r.initial.style.fontSize = 14f;
                avatar.Add(r.initial);
                r.btn.Add(avatar);
                var words = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-words");
                var top = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-top");
                r.name = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-hand-name");
                r.tag = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-mood");
                top.Add(r.name);
                top.Add(r.tag);
                words.Add(top);
                r.sub = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-job");
                r.sub.style.whiteSpace = WhiteSpace.Normal;
                words.Add(r.sub);
                r.btn.Add(words);
                root.Add(r.btn);
                return r;
            }
        }
    }
}
