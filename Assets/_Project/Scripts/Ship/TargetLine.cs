using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using SeaSick.Voyage;

namespace SeaSick.Ship
{
    /// **How far off the next rung actually is, once camps are counted.**
    ///
    /// The yard panel already says what the next hull costs and what home has
    /// in store (`ShipPrices.InStore`); this adds the piece neither of those
    /// know about — what is sitting made-but-not-banked (aboard the ship, or
    /// piled in a camp waiting on a voyage) and how fast the camps still owe
    /// are closing that gap. A tail, not a replacement.
    public static class TargetLine
    {
        // --- the tail for the yard's own "next: ..." line --------------------

        static readonly StringBuilder outlookSb = new StringBuilder(96);
        static long outlookKey = long.MinValue;
        static string outlookCached;
        static bool outlookEverBuilt;

        /// The TAIL the yard appends after its own price line, or null when
        /// there is nothing worth adding — the yard's "next: ... — 12 boards"
        /// already covers a rung the bank alone can pay for.
        public static string Outlook(VoyageManager v, Shipyard yard)
        {
            if (v == null || yard == null || yard.Node == null) return null;
            var next = ShipLadder.Node(yard.Node.node + 1);
            if (next == null) return null;

            var price = ShipPrices.ForRung(yard.Node.node + 1);
            if (!price.Has) return null;

            long key = KeyFor(v, yard, price);
            if (outlookEverBuilt && key == outlookKey) return outlookCached;
            outlookKey = key;
            outlookEverBuilt = true;
            outlookCached = BuildOutlook(v, price);
            return outlookCached;
        }

        // --- the full line for the camp sheet ---------------------------------

        static readonly StringBuilder sheetSb = new StringBuilder(128);
        static long sheetKey = long.MinValue;
        static string sheetCached;
        static bool sheetEverBuilt;

        /// "next: Long sloop — 24 boards and 8 stone   ·   still to make 15
        /// boards ..." — the whole line the camp sheet prints, price and
        /// outlook together so the sheet never has to know how to join them.
        public static string ForSheet(VoyageManager v, Shipyard yard)
        {
            if (v == null || yard == null) return null;
            if (yard.Node == null) return null;

            var next = ShipLadder.Node(yard.Node.node + 1);
            if (next == null) return "she is as big as the yard can build";

            var price = ShipPrices.ForRung(yard.Node.node + 1);
            long key = KeyFor(v, yard, price) * 31 + next.node;
            if (sheetEverBuilt && key == sheetKey) return sheetCached;
            sheetKey = key;
            sheetEverBuilt = true;

            sheetSb.Length = 0;
            if (!price.Has)
            {
                sheetSb.Append("next: ").Append(next.label).Append(" — free");
            }
            else
            {
                sheetSb.Append("next: ").Append(next.label).Append(" — ").Append(price.ToString());
                string outlook = BuildOutlook(v, price);
                sheetSb.Append(outlook != null
                    ? "   ·   " + outlook
                    : "   ·   in the store at home");
            }
            sheetCached = sheetSb.ToString();
            return sheetCached;
        }

        // --- the shared arithmetic --------------------------------------------

        /// Built fresh each time the key moves — never called more than once
        /// per rebuild, so it does not need its own cache.
        static string BuildOutlook(VoyageManager v, ShipPrices.Price price)
        {
            // Home's own bank covers it whole: the yard's own line already
            // says so, nothing to add.
            if (Covered(v, price)) return null;

            Span2 shortfall = default;
            bool anyToMake = false;
            float days = 0f;
            string noOneMaking = null;

            AccumulateResource(v, price.a, price.na, ref shortfall, ref anyToMake, ref days, ref noOneMaking);
            if (price.HasSecond)
                AccumulateResource(v, price.b, price.nb, ref shortfall, ref anyToMake, ref days, ref noOneMaking);

            if (!anyToMake) return "gathered — sail it home";

            outlookSb.Length = 0;
            outlookSb.Append("still to make ");
            shortfall.AppendTo(outlookSb);
            if (noOneMaking != null)
                outlookSb.Append("   ·   nobody is making ").Append(noOneMaking);
            else
                outlookSb.Append("   ·   ~").Append(days.ToString("0.#")).Append(" days across your camps");
            return outlookSb.ToString();
        }

        static bool Covered(VoyageManager v, ShipPrices.Price price)
        {
            if (!price.Has) return true;
            if (v.Banked(price.a) < price.na) return false;
            if (price.HasSecond && v.Banked(price.b) < price.nb) return false;
            return true;
        }

        /// A tiny two-slot builder for "15 boards, 8 stone" without allocating
        /// a List for what is at most two resources.
        struct Span2
        {
            string first, second;
            public void Add(string text) { if (first == null) first = text; else second = text; }
            public void AppendTo(StringBuilder sb)
            {
                if (first != null) sb.Append(first);
                if (second != null) sb.Append(", ").Append(second);
            }
        }

        static void AccumulateResource(VoyageManager v, string res, int n,
            ref Span2 shortfall, ref bool anyToMake, ref float days, ref string noOneMaking)
        {
            if (n <= 0 || string.IsNullOrEmpty(res)) return;

            int aboard = v.AmountOf(res);
            int inHand = v.Banked(res) + aboard + Outpost.PiledAcrossCamps(res);
            int toMake = System.Math.Max(0, n - inHand);
            if (toMake <= 0) return;

            anyToMake = true;
            shortfall.Add($"{toMake} {Lower(res)}");

            float rate = Outpost.MakeRateAcrossCamps(res);
            if (rate <= 0f)
            {
                if (noOneMaking == null) noOneMaking = Lower(res);
                return;
            }
            float need = toMake / rate;
            if (need > days) days = need;
        }

        // --- lower-cased resource names, cached ------------------------------
        //
        // Same idiom `UI/ReturnSummary.cs` keeps for itself: a handful of ids,
        // not worth reaching into another feature's cache for.
        static readonly Dictionary<string, string> loweredCache = new Dictionary<string, string>();

        static string Lower(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (loweredCache.TryGetValue(s, out string lower)) return lower;
            lower = s.ToLowerInvariant();
            loweredCache[s] = lower;
            return lower;
        }

        // --- the cache key ------------------------------------------------------

        static long KeyFor(VoyageManager v, Shipyard yard, ShipPrices.Price price)
        {
            long k = yard.Node.node;
            // A free price has a null resource, and `Banked`/`AmountOf` are
            // dictionary lookups that throw on null rather than answer 0.
            if (!price.Has) return k;
            k = k * 31 + v.Banked(price.a);
            k = k * 31 + v.AmountOf(price.a);
            k = k * 31 + Outpost.PiledAcrossCamps(price.a);
            k = k * 31 + (int)(Outpost.MakeRateAcrossCamps(price.a) * 10f);
            if (price.HasSecond)
            {
                k = k * 31 + v.Banked(price.b);
                k = k * 31 + v.AmountOf(price.b);
                k = k * 31 + Outpost.PiledAcrossCamps(price.b);
                k = k * 31 + (int)(Outpost.MakeRateAcrossCamps(price.b) * 10f);
            }
            k = k * 31 + (v.AtHome ? 1 : 0);
            return k;
        }
    }
}
