using System.Collections.Generic;
using SeaSick.Combat;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// How much of an island the player knows.
    public enum Seen
    {
        /// Not on the chart at all.
        Never,
        /// Sailed past: a shape and nothing else — no name, no ledger.
        Glimpsed,
        /// Anchored off it. Drawn, named, and anything on it is known.
        Landed,
    }

    /// What a camp's fire says about it from a chart's distance.
    public enum FlameState
    {
        /// Not a camp.
        None,
        /// Eating.
        Fed,
        /// Out of food, or in food debt.
        Hungry,
        /// Raided since you sailed, and you have not been back to see it.
        Raided,
    }

    /// One island as the chart wants it: a shape, a state, and — when it is
    /// a camp — the one line of ledger that fits under it.
    public struct ChartIsland
    {
        public Island island;
        /// World XZ. Every `Vector2` in this file is world XZ; there is no
        /// screen space here at all, and no y.
        public Vector2 centre;
        public float meanRadius;
        public Seen seen;
        public string name;
        public Color tint;
        public FlameState flame;
        public Outpost outpost;
        /// "23 timber · 4 hands · fed", or empty when this is not a camp.
        public string ledgerLine;
    }

    /// One raider, and the water it is guarding.
    public struct ChartRaider
    {
        public Vector2 pos;
        public float headingDeg;
        public float patrolRadius;
        /// **Additive to the agreed contract** (2026-09-22): the patrol ring
        /// is drawn around the island the raider is guarding, not around the
        /// raider, exactly as `MiniMap` draws it — *"the patrol is the
        /// information, not the ship's current position"* (MiniMap.cs:254).
        /// Without this the radius has no centre to be a radius of. Falls
        /// back to `pos` when a raider has no home.
        public Vector2 patrolCentre;
        public bool hasTarget;
        public Vector2 target;
    }

    /// **Everything the chart draws, in world XZ, read once and cached.**
    ///
    /// The round instrument and the full sheet are two drawings of the same
    /// facts, so the facts live in neither of them. This is that layer: it
    /// asks the world, it never changes it, and the one thing it owns
    /// outright is the player's course and the ship's track.
    ///
    /// **Rebuilt at most twice a second.** Both consumers redraw far more
    /// often than that, and a rebuild walks `Island.All`, `Outpost.All` and
    /// `EnemyShip.All` and settles every ledger's books on the way past
    /// (`Outpost.CatchUp`). At 60 fps on two instruments that would be a
    /// hundred and twenty scene walks a second for numbers that move on a
    /// clock measured in game days.
    public static class ChartData
    {
        const float RebuildInterval = 0.5f;

        static readonly List<ChartIsland> islands = new List<ChartIsland>();
        static readonly List<ChartRaider> raiders = new List<ChartRaider>();
        static float nextRebuild;

        public static IReadOnlyList<ChartIsland> Islands() { Rebuild(); return islands; }
        public static IReadOnlyList<ChartRaider> Raiders() { Rebuild(); return raiders; }

        // --- the ship ---------------------------------------------------------

        public static Vector2 ShipPos
        {
            get
            {
                var m = SheetBits.Motor;
                if (m == null) return Vector2.zero;
                var p = m.transform.position;
                return new Vector2(p.x, p.z);
            }
        }

        public static float ShipHeadingDeg
        {
            get
            {
                var m = SheetBits.Motor;
                return m != null ? m.Heading : 0f;
            }
        }

        /// Not lying at anything. A dropped anchor, a berth and a shore party
        /// all read as stopped, which is what the track sampler and the
        /// instrument's "course" line both mean by it.
        public static bool UnderWay
        {
            get
            {
                var a = SheetBits.Anchor;
                if (a == null) return true;
                return a.CurrentState == Ship.AnchorController.State.Underway
                       && a.CurrentDock == null;
            }
        }

        // --- the islands -------------------------------------------------------

        /// **`MiniMap`'s palette, copied rather than shared.** `MiniMap`
        /// keeps its own `ResourceColour` private and that file is not mine
        /// to open (2026-09-22 split). These are the same six values
        /// (MiniMap.cs:159-167); if one of them is retuned, both have to move.
        public static Color TintFor(Island isle)
        {
            if (isle == null) return new Color(0.86f, 0.79f, 0.60f);
            if (isle.IsHome) return new Color(1f, 0.58f, 0.25f);
            if (!isle.HasResources) return new Color(0.86f, 0.79f, 0.60f);
            switch (isle.ResourceName)
            {
                case Res.Timber: return new Color(0.45f, 0.78f, 0.38f);
                case Res.Stone: return new Color(0.72f, 0.75f, 0.80f);
                case Res.Ore: return new Color(1f, 0.82f, 0.32f);
                case Res.Spice: return new Color(0.93f, 0.45f, 0.72f);
                default: return new Color(0.86f, 0.79f, 0.60f);
            }
        }

        static void Rebuild()
        {
            if (Time.unscaledTime < nextRebuild) return;
            nextRebuild = Time.unscaledTime + RebuildInterval;

            islands.Clear();
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                var seen = Discovery.Of(isle);
                if (seen == Seen.Never) continue;

                var p = isle.transform.position;
                var camp = Outpost.Of(isle);
                if (camp != null && !camp.HasCamp && !camp.Building) camp = null;

                islands.Add(new ChartIsland
                {
                    island = isle,
                    centre = new Vector2(p.x, p.z),
                    meanRadius = isle.Radius,
                    seen = seen,
                    // **A glimpse has no name.** The shape is what you saw;
                    // knowing what it is called is what landing buys.
                    name = seen == Seen.Landed ? PrettyName(isle) : "",
                    tint = TintFor(isle),
                    flame = Flame(camp),
                    outpost = camp,
                    ledgerLine = LedgerLine(camp),
                });
            }

            raiders.Clear();
            foreach (var r in EnemyShip.All)
            {
                if (r == null || !r.Alive) continue;
                var p = r.transform.position;
                var home = r.Home != null ? r.Home.transform.position : p;

                // `Duty.Raid` is a raider that has BEEN given a site
                // (EnemyShip.BeginRaid), so the camp is the thing on the far
                // end of the dotted line and never a guess.
                bool raiding = r.Current == EnemyShip.Duty.Raid && r.Site.camp != null;
                var aim = raiding ? r.Site.camp.CampCentre : Vector3.zero;

                raiders.Add(new ChartRaider
                {
                    pos = new Vector2(p.x, p.z),
                    headingDeg = r.transform.eulerAngles.y,
                    patrolRadius = r.PatrolRadius,
                    patrolCentre = new Vector2(home.x, home.z),
                    hasTarget = raiding,
                    target = new Vector2(aim.x, aim.z),
                });
            }
        }

        /// "Island_3" is a scene name, not a place. The chart prints places.
        public static string PrettyName(Island isle)
        {
            if (isle == null) return "";
            string n = isle.name;
            if (string.IsNullOrEmpty(n)) return "";
            n = n.Replace('_', ' ');
            int paren = n.IndexOf(" (");
            if (paren > 0) n = n.Substring(0, paren);
            return n.Trim();
        }

        /// **Where the flame states are decided, and the only place.**
        ///
        /// `Raided` beats `Hungry`: a camp that has just been robbed of four
        /// days' food is both, and the one the player has to sail to is the
        /// one that was robbed.
        ///
        /// "Since the ship last left and the player has not been back" is two
        /// records, because there are two moments it can be true in:
        ///   * `ledger.away` is the OPEN absence — the ship is away right now
        ///     and the raid landed while she was (OutpostLedger.Absence, set
        ///     by `Outpost.ShowHands(false)`);
        ///   * `outpost.LastReturn` is the absence the last arrival CLOSED,
        ///     and it is nulled by `Outpost.DismissReturn` the moment the
        ///     player reads the "while you were gone" card (Outpost.cs:890,
        ///     ReturnSummary.cs:50). An undismissed one is a raid she has
        ///     sailed back to but not yet looked at.
        static FlameState Flame(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return FlameState.None;

            if (l.away != null && l.away.Open && l.away.raids > 0) return FlameState.Raided;
            if (camp.LastReturn != null && camp.LastReturn.raids > 0) return FlameState.Raided;

            // Hungry is NOW: the pile is empty with mouths to feed (`Hungry`),
            // or under a day of food left (the warning AHEAD). Not
            // `hungerDays`: that is a lifetime tally that never goes back to
            // 0, so one lean week long ago read "hungry" forever (Kevin's home
            // camp, 15 days of food in the top bar, 2026-10-04).
            if (l.hands.Count > 0)
            {
                if (l.Hungry) return FlameState.Hungry;
                float days = SheetBits.FoodDays(l);
                if (days >= 0f && days < 1f) return FlameState.Hungry;
            }
            return FlameState.Fed;
        }

        /// "23 timber · 4 hands · 15 days of food" — the biggest pile, the roster, and the
        /// word the flame is already saying, for a player reading the chart
        /// rather than the mark.
        static string LedgerLine(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return "";

            string big = "";
            int most = 0;
            foreach (var s in l.stores)
            {
                if (s == null || s.whole <= most) continue;
                most = s.whole;
                big = s.resource;
            }

            var bits = new List<string>(3);
            if (most > 0) bits.Add(most + " " + CampLoading.Lower(big));
            bits.Add(l.hands.Count == 1 ? "1 hand" : l.hands.Count + " hands");
            // The food as the top bar counts it ("15 days of food"), not a
            // bare "fed"/"hungry" the player has to decode.
            if (Flame(camp) == FlameState.Raided) bits.Add("raided");
            bits.Add(FoodWords(l));
            return string.Join(" · ", bits);
        }

        /// "15 days of food", "under a day of food", "no food", or
        /// "nobody to feed": `SheetBits.FoodDays`, the top bar's own number.
        static string FoodWords(OutpostLedger l)
        {
            if (l == null || l.hands.Count == 0) return "nobody to feed";
            if (l.Hungry) return "no food";
            float days = SheetBits.FoodDays(l);
            if (days < 0f) return "eating nothing";
            if (days < 1f) return "under a day of food";
            int d = Mathf.FloorToInt(days);
            return d == 1 ? "1 day of food" : d + " days of food";
        }

        // --- the track ----------------------------------------------------------

        /// Where she has been, sampled on the game clock and aged out after a
        /// game day. A ring buffer rather than a growing list: a long session
        /// is a long voyage, and the chart only ever draws the last day of it.
        const int TrackCapacity = 256;
        static readonly Vector2[] trackPos = new Vector2[TrackCapacity];
        static readonly double[] trackAt = new double[TrackCapacity];
        static int trackHead;      // next slot to write
        static int trackCount;
        static double nextSample;
        static readonly List<Vector2> trackOut = new List<Vector2>(TrackCapacity);

        /// **~20 s of game time, or a thirty-sixth of a day, whichever is
        /// shorter.** The contract asked for 20 s and that is the cap; the
        /// floor exists because `TimeOfDay.DayLength` is 180 s in this build,
        /// and nine points is not a track, it is a dogleg. On a long day this
        /// is 20 s exactly.
        static float SampleInterval =>
            Mathf.Clamp(TimeOfDay.DayLength / 36f, 2f, 20f);

        /// Called every frame by `ChartWatch`. Cheap: a clock compare, and a
        /// transform read once every interval.
        public static void TickTrack()
        {
            double now = TimeOfDay.Seconds;
            // A scrub backwards (a load) leaves the next sample in the far
            // future; treat any backward jump as a reason to sample now.
            if (now < nextSample - SampleInterval) nextSample = 0.0;
            if (now < nextSample) return;
            nextSample = now + SampleInterval;

            var m = SheetBits.Motor;
            if (m == null) return;
            var p = m.transform.position;
            Push(new Vector2(p.x, p.z), now);
        }

        static void Push(Vector2 p, double at)
        {
            trackPos[trackHead] = p;
            trackAt[trackHead] = at;
            trackHead = (trackHead + 1) % TrackCapacity;
            if (trackCount < TrackCapacity) trackCount++;
        }

        /// The last game day of her wake, oldest first. Rebuilt into one
        /// reused list so a per-frame draw does not allocate.
        public static IReadOnlyList<Vector2> Track()
        {
            trackOut.Clear();
            double cutoff = TimeOfDay.Seconds - TimeOfDay.DayLength;
            int start = (trackHead - trackCount + TrackCapacity) % TrackCapacity;
            for (int i = 0; i < trackCount; i++)
            {
                int k = (start + i) % TrackCapacity;
                if (trackAt[k] < cutoff) continue;
                trackOut.Add(trackPos[k]);
            }
            return trackOut;
        }

        /// The buffer, flattened for `JsonUtility` (parallel lists, the same
        /// shape `OutpostLedger.Absence` uses and for the same reason).
        public static void CaptureTrack(List<float> x, List<float> z, List<double> at)
        {
            if (x == null || z == null || at == null) return;
            x.Clear(); z.Clear(); at.Clear();
            int start = (trackHead - trackCount + TrackCapacity) % TrackCapacity;
            for (int i = 0; i < trackCount; i++)
            {
                int k = (start + i) % TrackCapacity;
                x.Add(trackPos[k].x);
                z.Add(trackPos[k].y);
                at.Add(trackAt[k]);
            }
        }

        public static void RestoreTrack(List<float> x, List<float> z, List<double> at)
        {
            trackHead = 0; trackCount = 0; nextSample = 0.0;
            if (x == null || z == null || at == null) return;
            int n = Mathf.Min(x.Count, Mathf.Min(z.Count, at.Count));
            for (int i = 0; i < n; i++) Push(new Vector2(x[i], z[i]), at[i]);
        }

        // --- the course -----------------------------------------------------------

        static Outpost course;

        /// **The set course, or the way home.**
        ///
        /// A chart with nothing on it is still allowed to point at something:
        /// with no course set this answers with the nearest pier the player
        /// owns, which is the one destination that is always true. False only
        /// when there is nowhere at all — no camp, no home pier, no ship.
        public static bool TryCourse(out Vector2 target, out string label, out float distance)
        {
            target = Vector2.zero; label = ""; distance = 0f;
            var m = SheetBits.Motor;
            if (m == null) return false;
            Vector2 from = ShipPos;

            if (course != null && course.Ledger != null && (course.HasCamp || course.Building))
            {
                var c = course.CampCentre;
                target = new Vector2(c.x, c.z);
                label = course.Island != null ? PrettyName(course.Island) : "the camp";
                distance = Vector2.Distance(from, target);
                return true;
            }
            course = null;

            // `Dock.All` is every berth that exists — the start pier and any
            // pier a camp has raised (Dock.cs:24) — so "my nearest pier" is
            // one call and never a scene walk.
            var dock = Dock.Nearest(m.transform.position);
            if (dock == null) return false;
            var b = dock.Berth;
            target = new Vector2(b.x, b.z);
            var isle = dock.GetComponentInParent<Island>();
            label = dock.IsHome ? "home" : (isle != null ? PrettyName(isle) : "the pier");
            distance = Vector2.Distance(from, target);
            return true;
        }

        /// True when the player has set one — as opposed to `TryCourse`
        /// answering with the fallback pier, which is not a course and must
        /// not offer a "clear" button.
        public static bool HasCourse => course != null;

        public static Outpost Course => course;

        public static void SetCourse(Outpost camp) => course = camp;

        public static void ClearCourse() => course = null;

        // --- shape -------------------------------------------------------------------

        /// **Bearing in DEGREES to shoreline distance in metres.**
        ///
        /// `Island.RadiusAt` takes radians, and its convention is the
        /// project's compass one: `Atan2(d.x, d.z)` of the vector from the
        /// island's centre toward the point (Island.cs:75-83), so 0 is north
        /// (+z), 90° is east (+x), and it grows clockwise. A point on the
        /// outline is therefore `centre + (sin b, cos b) * OutlineOf(isle)(b)`.
        ///
        /// Returns a closure so a caller can sample forty-eight bearings
        /// without holding on to the island, and a flat mean radius for an
        /// island whose profile has not been measured yet.
        public static System.Func<float, float> OutlineOf(Island isle)
        {
            if (isle == null) return _ => 0f;
            return deg => isle.RadiusAt(deg * Mathf.Deg2Rad);
        }

        // --- play sessions -------------------------------------------------------------

        /// Domain reload is off, so every static above outlives play mode.
        /// `ChartWatch` calls this as the first scene comes up.
        public static void ResetForPlay()
        {
            islands.Clear();
            raiders.Clear();
            nextRebuild = 0f;
            trackHead = 0; trackCount = 0; nextSample = 0.0;
            course = null;
            Discovery.Clear();
        }
    }
}
