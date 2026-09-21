using UnityEngine;

namespace SeaSick.Ship
{
    /// **What a rung of the ladder costs, and the only place that knows.**
    ///
    /// Until now the twenty-rung ladder and all fifteen fittings were FREE:
    /// `Shipyard` never read a store in its life, so the biggest thing in the
    /// game was bought by pressing a button twenty times. Kevin's call for
    /// Phase 2 of the island loop (GDD §6, 2026-09-20): **ship rungs are
    /// priced in camp-made goods that must be sailed home in the hold** —
    /// timber first, then boards (a sawmill on a wooded island), then tools
    /// (a smith on an ore island), then spice from far out. Home village
    /// growth is the SECOND sink and comes after the ship.
    ///
    /// That is why the table below climbs through four materials rather than
    /// asking for more and more of one. The bands are written against
    /// `WorldSettings.kinds`, which unlocks a resource by RING — Timber from
    /// the first island, Stone at 0.30 of the discovery radius, Ore at 0.55,
    /// Spice at 0.78 — and against `BuildPlans`, where a sawmill turns timber
    /// into boards and a forge turns ore into tools. A rung you cannot pay for
    /// is therefore a rung whose materials are further out than you have
    /// sailed, which is the pacing the loop is made of:
    ///
    ///   rungs 1-6    timber          — one wooded island and a hold
    ///   rungs 7-11   boards (+stone) — a camp with a sawmill standing in it
    ///   rungs 12-16  tools           — an ore island, a forge, a smith
    ///   rungs 17-19  spice           — the outer ring
    ///
    /// **Every number here is a guess and none of them has been played.**
    /// They are set against the hold she has at the rung BEFORE each one
    /// (`SinkProbe` prints that table) so that a rung is one or two voyages
    /// rather than ten, but nobody has sailed a single one of them yet.
    ///
    /// --- THE DESIGN RULE THIS FILE EXISTS TO HOLD --------------------------
    ///
    /// **Price `Shipyard.Move` and `Shipyard.Upgrade`. NEVER `Apply`, `Undo`
    /// or the yard panel's four dev buttons.** `Apply(i)` is how every probe
    /// and every ladder-walker in `Scripts/Dev` puts her on a known rung —
    /// `UpgradeProbe` alone applies eight of them in one run — and a charged
    /// `Apply` would turn every one of those into a test of the stores
    /// instead of a test of the ship. The free dev path is not an oversight
    /// to be closed later; it is the path the instruments run on. There is
    /// deliberately no `FreeForProbes` flag either: a probe that needs a free
    /// rung already has one, and a global "everything is free" switch is a
    /// switch that will one day be left on.
    public static class ShipPrices
    {
        /// One price: up to two resources, because "boards and stone" is as
        /// complicated as a shop button is allowed to get. The second is
        /// optional — most rungs ask for one thing.
        public struct Price
        {
            public string a;
            public int na;
            public string b;
            public int nb;

            public Price(string a, int na, string b = null, int nb = 0)
            { this.a = a; this.na = na; this.b = b; this.nb = nb; }

            /// **Is there anything to pay?** A rung with no price is free —
            /// rung 0 is where she starts, and a fitting past the top has no
            /// next level to sell.
            public bool Has => na > 0 && !string.IsNullOrEmpty(a);
            public bool HasSecond => nb > 0 && !string.IsNullOrEmpty(b);

            /// Everything it costs, as one number. Only for the pacing table,
            /// which asks "how many hold-loads is this" and does not care that
            /// half of them are tools.
            public int Units => (Has ? na : 0) + (HasSecond ? nb : 0);

            /// In the player's words: "24 boards and 8 tools". The resource
            /// names are the ones the world uses (`World.Res`), which are
            /// capitalised because they are keys; on a button they are not.
            public override string ToString()
            {
                if (!Has) return "free";
                string one = $"{na} {a.ToLowerInvariant()}";
                return HasSecond ? $"{one} and {nb} {b.ToLowerInvariant()}" : one;
            }
        }

        // --- the vocabulary ---------------------------------------------------

        /// Every resource that appears anywhere in the two tables below.
        ///
        /// The yard panel folds the banked count of each of these into its
        /// text cache key: without that the afford state goes stale the moment
        /// a voyage lands, which is a bug this project has already had once
        /// and written into the trap log.
        public static readonly string[] Priced =
        {
            World.Res.Timber, World.Res.Stone,
            World.Res.Boards, World.Res.Tools, World.Res.Spice,
        };

        // --- the hull ladder ---------------------------------------------------

        /// Index is the rung being BOUGHT, so `Rungs[7]` is what it costs to
        /// become rung 7. Rung 0 is where she starts and is free; the slot is
        /// kept so the index needs no arithmetic done to it.
        ///
        /// **All guesses, none played.** See the header for the bands.
        static readonly Price[] Rungs =
        {
            /*  0 start          */ default,
            /*  1 lengthened skiff*/ new Price(World.Res.Timber, 8),
            /*  2 girdled skiff  */ new Price(World.Res.Timber, 12),
            /*  3 decked boat    */ new Price(World.Res.Timber, 16),
            /*  4 long boat      */ new Price(World.Res.Timber, 20),
            /*  5 beamy boat     */ new Price(World.Res.Timber, 24),
            /*  6 coastal sloop  */ new Price(World.Res.Timber, 28),
            // The first rung that wants something MADE. A sawmill has to be
            // standing on a wooded island and a sawyer assigned to it before
            // this one can be paid for at all.
            /*  7 long sloop     */ new Price(World.Res.Boards, 12, World.Res.Timber, 20),
            /*  8 beamy sloop    */ new Price(World.Res.Boards, 18),
            /*  9 first battery  */ new Price(World.Res.Boards, 24),
            /* 10 long battery   */ new Price(World.Res.Boards, 30, World.Res.Stone, 10),
            /* 11 beamy battery  */ new Price(World.Res.Boards, 36, World.Res.Stone, 12),
            // And the first that wants a forge. Tools are slow (1.5 a day to
            // boards' 3), so the counts step down as they come in.
            /* 12 brig           */ new Price(World.Res.Boards, 40, World.Res.Tools, 8),
            /* 13 long brig      */ new Price(World.Res.Boards, 30, World.Res.Tools, 14),
            /* 14 beamy brig     */ new Price(World.Res.Boards, 36, World.Res.Tools, 18),
            /* 15 two-decker     */ new Price(World.Res.Boards, 40, World.Res.Tools, 24),
            /* 16 long two-decker*/ new Price(World.Res.Boards, 48, World.Res.Tools, 30),
            // The outer ring. Spice is the only thing out there that is worth
            // the passage, and the last three rungs are what it is for.
            /* 17 beamy two-decker*/ new Price(World.Res.Tools, 40, World.Res.Spice, 12),
            /* 18 full length    */ new Price(World.Res.Tools, 48, World.Res.Spice, 20),
            /* 19 three-decker   */ new Price(World.Res.Tools, 60, World.Res.Spice, 30),
        };

        /// What it costs to become `toRung`. Free off both ends of the table:
        /// rung 0 is the start, and there is nothing above the top.
        public static Price ForRung(int toRung)
            => toRung > 0 && toRung < Rungs.Length ? Rungs[toRung] : default;

        // --- the fittings --------------------------------------------------------

        /// **The same three prices for every track, for now.**
        ///
        /// A rudder, a suit of sails and a drilled crew are not the same
        /// purchase and will not stay the same price — but the tracks are
        /// already gated by something PHYSICAL (`ShipFit.Blocked`: masts for
        /// sail tiers, beam for sail area and for gun calibre, length for a
        /// wheel), so what the table has to do first is make a fitting cost
        /// ANYTHING at all. Differentiating it before the shape has been
        /// played would be tuning a number nobody has felt.
        ///
        /// Level 1 is timber, 2 is boards, 3 is tools — the same three bands
        /// the hull ladder walks, so a fitting is always reachable a little
        /// before the rung that shares its material.
        static readonly Price[] Fits =
        {
            /* 0 (already owned) */ default,
            /* 1 */ new Price(World.Res.Timber, 10),
            /* 2 */ new Price(World.Res.Boards, 12),
            /* 3 */ new Price(World.Res.Tools, 12),
        };

        public static Price ForFit(FitTrack track, int toLevel)
            => toLevel > 0 && toLevel < Fits.Length ? Fits[toLevel] : default;

        // --- can she pay for it? ----------------------------------------------

        /// Why this cannot be bought, in one sentence, or null if it can.
        ///
        /// The same idiom as `ShipFit.Blocked` and `ShipLadder.Blocked`: the
        /// refusal is a SENTENCE, because a greyed-out button with no reason
        /// leaves the player to guess, and the guess is always "the game is
        /// broken".
        ///
        /// **No purse means no price.** The ocean and ladder lab scenes have a
        /// `Shipyard` and no `VoyageManager`, and a yard that threw — or
        /// refused — in a scene with nowhere to keep stores would take every
        /// hull instrument in `Scripts/Dev` down with it.
        public static string CannotAfford(Price p, Voyage.VoyageManager v)
        {
            if (!p.Has) return null;
            if (v == null) return null;
            // Stores are ashore, in a pile, at home. You cannot re-loft a hull
            // from the middle of the sea however full the hold is.
            if (!v.AtHome) return "she has to be at her own pier";
            return Shortfall(p.a, p.na, v) ?? Shortfall(p.b, p.nb, v);
        }

        static string Shortfall(string res, int n, Voyage.VoyageManager v)
        {
            if (n <= 0 || string.IsNullOrEmpty(res)) return null;
            int have = v.Banked(res);
            if (have >= n) return null;
            return $"needs {n} {res.ToLowerInvariant()} — home has {have}";
        }

        /// Pay for it. Re-checks first: the panel's answer is cached, and a
        /// cached answer must never be what takes the stores down.
        ///
        /// Spent through `VoyageManager.SpendBanked`, which also withdraws
        /// from the visible `Stockpile` — otherwise the number in the panel
        /// falls and the pile of logs two metres away does not, which is the
        /// same lie the build button was fixed for.
        public static bool TrySpend(Price p, Voyage.VoyageManager v, out string why)
        {
            why = CannotAfford(p, v);
            if (why != null) return false;
            if (!p.Has || v == null) return true;
            v.SpendBanked(p.a, p.na);
            if (p.HasSecond) v.SpendBanked(p.b, p.nb);
            return true;
        }

        /// "home has 9 boards, 31 timber" — what is in store OF THE THINGS
        /// THIS PRICE ASKS FOR, in the order it asks for them. Null when there
        /// is no purse or nothing to pay.
        public static string InStore(Price p, Voyage.VoyageManager v)
        {
            if (!p.Has || v == null) return null;
            string one = $"{v.Banked(p.a)} {p.a.ToLowerInvariant()}";
            return p.HasSecond
                ? $"home has {one}, {v.Banked(p.b)} {p.b.ToLowerInvariant()}"
                : $"home has {one}";
        }
    }
}
