using System;
using System.Collections.Generic;
using SeaSick.World.Economy;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **What is wrong with the camp, as things you can tap, 2026-09-27.**
    ///
    /// The same facts `FireSheet.CampWarning` joins into one line (builders
    /// short, angry hands, nobody on watch, idle hands), plus the raid and
    /// the food clock, each carrying WHERE it gets fixed. `AlertStrip` shows
    /// the first three under the resource bar; the Camp sheet lists them all
    /// (NEEDS YOU), each with its fix button. Ordered by how soon it hurts: a raid on the sand,
    /// then an empty food pile, then the watch, then single hands, then
    /// the camp's missing places (no beds, store full), then angry hands.
    public static class CampAlerts
    {
        public enum Tone { Raid, Bad, Warn }

        public struct Alert
        {
            public string text;
            public Tone tone;
            public Func<ISheet> open;
            /// **What the fix button says (2026-09-30, Camp sheet):** "Assign Bo",
            /// "Build a hut", "Make gate", "Open". Null or empty = the sheet
            /// labels it "Fix".
            public string fixLabel;
        }

        /// True while `alerts` holds a live raid line for this camp (phase
        /// "standing off" or "on the sand"). `RaidBanner` reads it through
        /// `AlertStrip.ShowsRaid` to stay quiet on land.
        public static bool RaidLive(Outpost camp, out string text)
        {
            text = null;
            if (camp == null) return false;
            var party = SeaSick.Combat.RaidParty.Active;
            if (party != null && party.Camp == camp)
            {
                text = $"RAID · {party.Ashore} ashore";
                return true;
            }
            var incoming = SeaSick.Combat.RaidDirector.Incoming(camp);
            if (incoming != null && !incoming.Beached && SeaSick.Combat.RaidDirector.WarnedOf(camp))
            {
                text = "RAIDERS · making for the beach";
                return true;
            }
            return false;
        }

        public static void Collect(Outpost camp, List<Alert> into)
        {
            into.Clear();
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return;

            if (RaidLive(camp, out string raid))
                into.Add(new Alert { text = raid, tone = Tone.Raid, open = () => new RaidSheet(camp), fixLabel = "Open" });

            if (l.hands.Count > 0)
            {
                // **The alert names the fix (2026-10-02).** The food draft no
                // longer pulls a hand the player assigned (Kevin: *"you
                // assign someone somewhere, thats what they do"*), so this
                // line is how a hungry camp asks for one.
                float days = SheetBits.FoodDays(l);
                if (l.Hungry)
                    into.Add(new Alert { text = "Out of food · assign a gatherer or cook", tone = Tone.Bad, open = () => Larder(camp), fixLabel = "Food" });
                else if (days >= 0f && days < 1f)
                    into.Add(new Alert { text = "Food low · assign a gatherer or cook", tone = Tone.Bad, open = () => Larder(camp), fixLabel = "Food" });
            }

            // **Walled off (2026-09-30)**: Kevin's catch-up closed a palisade
            // ring with no gate -- store inside, pier and three hands outside --
            // and every hand starved with 30 fish in the store while the strip
            // showed "Nobody on watch". One camp chip, not one per hand; the
            // fix is the wall piece nearest the first stuck hand, whose sheet
            // has Make gate.
            OutpostHand walled = null;
            foreach (var h in l.hands)
                if (h != null && IsWalledOff(h)) { walled = h; break; }
            // **And a station the store's runners cannot reach for a wall
            // (2026-10-05, the store front runner loop):** the runners no
            // longer walk into it, so no hand may be stuck to raise this --
            // the chip stays while the station is walled off, and opens the
            // wall piece nearest it.
            if (walled == null && l.stations != null)
                foreach (var st in l.stations)
                {
                    if (st == null || st.removed || !l.Manned(st)) continue;
                    if (l.StationCutOffWords(st) != MissingWords.WalledOffFromStore) continue;
                    var b = BuildingOf(camp, st);
                    if (b == null) continue;
                    Vector3 stAt = b.transform.position;
                    into.Add(new Alert
                    {
                        text = "Walled off · needs a gate", tone = Tone.Bad, fixLabel = "Make gate",
                        open = () =>
                        {
                            var seg = camp.NearestWall(stAt);
                            return seg != null ? new WallSheet(camp, seg) : Sheets.TryCreateFor(b) ?? new StationSheet(camp, b);
                        },
                    });
                    break;
                }
            if (walled != null)
            {
                var at = l.HandAt(walled);
                into.Add(new Alert
                {
                    text = "Walled off · needs a gate", tone = Tone.Bad, fixLabel = "Make gate",
                    open = () =>
                    {
                        var seg = camp.NearestWall(at);
                        return seg != null ? new WallSheet(camp, seg) : (ISheet)new HandSheet(camp, walled.name);
                    },
                });
            }

            // Nobody ASSIGNED (2026-09-27), not "guard below full": a posted
            // lookout in low spirits guards at under full strength
            // (`OutpostLedger.Guard`), and the alert said "Nobody on watch"
            // while he stood at the tower -- his sheet names the slowness.
            if (l.HasWatchtower && SheetBits.Lookout(l) == null)
                into.Add(new Alert { text = "Nobody on watch", tone = Tone.Warn, open = () => new LookoutSheet(camp), fixLabel = "Assign" });

            // **One "Store full of boards" chip for every hand a full pile
            // holds up (2026-10-02)** -- Kevin's sawyer read "Yara · rack and
            // store are full…" while the fix (more store) sat in a lower
            // chip. When it has stopped somebody it goes up here, ahead of
            // the single hands, and those hands get no chip of their own.
            bool storeFull = StoreFullText(l, out string full, out bool stops);
            if (storeFull && stops)
                into.Add(new Alert { text = full, tone = Tone.Bad, open = () => BuildList(camp, BuildPlans.Storage.id), fixLabel = "Build storage" });

            // One chip per stuck hand -- the hand is where the fix is (his
            // orders). A hunter with no spear gets the forge instead: the
            // spear is made there, not on his sheet.
            int idle = 0;
            string firstIdle = null;
            foreach (var h in l.hands)
            {
                if (h == null || IsWalledOff(h)) continue;
                // **"No job" only (2026-10-03):** the player's reserve is his
                // own choice and not a problem to nag about.
                if (h.order == OutpostOrder.Idle)
                {
                    if (!OutpostLedger.Reserve(h)) { idle++; if (firstIdle == null) firstIdle = h.name; }
                    continue;
                }
                if (h.walkingIn) continue;
                if (h.order == OutpostOrder.Gather && h.target == Res.Game && l.HunterBlocker() != null)
                {
                    into.Add(new Alert { text = "No spear or bow · no hunting", tone = Tone.Bad, open = () => Forge(camp), fixLabel = "Make spear" });
                    continue;
                }
                if (!l.Stalled(h) && string.IsNullOrEmpty(h.bodyBlocked)) continue;
                if (string.IsNullOrEmpty(h.bodyBlocked) && l.StoreFullFor(h) != null) continue;   // the store chip
                string why = l.StallReason(h);
                if (string.IsNullOrEmpty(why)) continue;
                // **The held mill (2026-10-04)** is not a problem of the
                // miller's: "Flour waiting · Kitchen II bakes bread"
                // (`FlourAlert`, the same test) is its one chip, with the fix.
                if (why == OutpostLedger.FlourHoldWords) continue;
                // **A station cut off from the store (2026-10-05)** has its
                // own line below ("Fishing hut · walled off from the store").
                if (MissingWords.IsStationCutOff(why)) continue;
                string who = h.name;
                // **No recipe chosen (2026-10-02):** "Edda · no recipe chosen
                // · open the hunting lodge and pick one" -- the fix is at the
                // station, so the chip opens it, not his sheet.
                if (l.BenchUnordered(h))
                {
                    var post = camp.WorkplaceOf(h);
                    if (post != null)
                    {
                        into.Add(new Alert
                        {
                            text = who + " · " + why, tone = Tone.Warn, fixLabel = "Pick a recipe",
                            open = () => Sheets.TryCreateFor(post) ?? new StationSheet(camp, post),
                        });
                        continue;
                    }
                }
                // **Station first (2026-10-03, group 4, the Problems list):**
                // "Sawmill · needs logs (Yara)" -- the building is what is
                // stuck, and its sheet (stock, inputs, his row) is where
                // the player looks; the tap opens it and the camera frames
                // the building (`BuildingSheetFocus`).
                var stuckAt = l.StationOfHand(h);
                var stuckPost = stuckAt != null ? camp.WorkplaceOf(h) : null;
                if (stuckPost != null)
                {
                    into.Add(new Alert
                    {
                        text = StationName(l, stuckAt) + " · " + Short(why) + " (" + who + ")",
                        tone = Tone.Bad, fixLabel = "Open " + StationName(l, stuckAt),
                        open = () => Sheets.TryCreateFor(stuckPost) ?? new StationSheet(camp, stuckPost),
                    });
                    continue;
                }
                into.Add(new Alert { text = who + " · " + Short(why), tone = Tone.Bad, open = () => new HandSheet(camp, who), fixLabel = "Open" });
            }
            // **The station problems (2026-10-03, villager review group 4:
            // one place that says what is stuck).** A station with a recipe
            // chosen and nobody on it ("Kitchen · no cook"), and a bench that
            // has waited on the runners past `RunnerSlowQuanta` ("Sawmill ·
            // waiting on the runners for logs" -- the fix is more runners,
            // at the store hut). Read from the ledger; nothing decided here.
            if (l.stations != null)
                for (int i = 0; i < l.stations.Count; i++)
                {
                    var st = l.stations[i];
                    if (st == null || st.removed) continue;
                    var b = BuildingOf(camp, st);
                    if (b == null) continue;
                    if (!l.Manned(st))
                    {
                        if (!st.HasOrder || PostLostAt(l, camp, st.planId)) continue;
                        var plan = BuildPlans.Named(st.planId);
                        string noOne = string.IsNullOrEmpty(plan.position) ? "no worker" : "no " + plan.position;
                        into.Add(new Alert
                        {
                            text = StationName(l, st) + " · " + noOne, tone = Tone.Warn, fixLabel = "Assign",
                            open = () => Sheets.TryCreateFor(b) ?? new StationSheet(camp, b),
                        });
                        continue;
                    }
                    // **Cut off from the store (2026-10-05, Kevin's fishing
                    // hut outside the palisade):** no runner is booked there
                    // any more, and this says why, beside the camp's
                    // "Walled off · needs a gate" chip. Walled: the tap
                    // opens the wall piece nearest it (Make gate).
                    string cut = l.StationCutOffWords(st);
                    if (cut != null)
                    {
                        bool byWall = cut == MissingWords.WalledOffFromStore;
                        Vector3 stAt = b.transform.position;
                        var stB = b;
                        into.Add(new Alert
                        {
                            text = StationName(l, st) + " · " + cut, tone = Tone.Bad,
                            fixLabel = byWall ? "Make gate" : "Open " + StationName(l, st),
                            open = () =>
                            {
                                var seg = byWall ? camp.NearestWall(stAt) : null;
                                return seg != null ? new WallSheet(camp, seg) : Sheets.TryCreateFor(stB) ?? new StationSheet(camp, stB);
                            },
                        });
                        continue;
                    }
                    string slow = l.RunnersSlowFor(st);
                    if (slow == null) continue;
                    into.Add(new Alert
                    {
                        text = StationName(l, st) + " · waiting on the runners for " + ResDefs.Label(slow).ToLowerInvariant(),
                        tone = Tone.Warn, fixLabel = "Runners", open = () => StoreHut(camp),
                    });
                }
            FarmAlerts(camp, l, into);
            FlourAlert(camp, l, into);
            // **A post the station cap took (2026-10-03,
            // `OutpostLedger.EnforceStationCaps`)**: never silently -- named
            // until the player taps it, which opens the hand to re-assign.
            if (l.postsLost != null)
                for (int i = 0; i < l.postsLost.Count; i++)
                {
                    var p = l.postsLost[i];
                    var lostHand = p != null ? camp.HandNamed(p.name) : null;
                    // Gone, or given a post again since: nothing to say.
                    if (lostHand == null || lostHand.order == OutpostOrder.Work) continue;
                    string who = p.name;
                    string place = BuildPlans.Named(p.planId).label;
                    if (string.IsNullOrEmpty(place)) place = "station";
                    into.Add(new Alert
                    {
                        text = $"{who} lost the {place} post · {OutpostLedger.OneWorkerReason.ToLowerInvariant()}",
                        tone = Tone.Warn, fixLabel = "Open " + who,
                        open = () => { l.DismissPostLost(who); return new HandSheet(camp, who); },
                    });
                }
            if (idle > 0)
            {
                string who = firstIdle;
                into.Add(new Alert
                {
                    text = idle == 1 ? who + " · no job" : $"{idle} hands with no job",
                    tone = Tone.Warn,
                    fixLabel = idle == 1 ? "Assign " + who : "Assign",
                    open = () => idle == 1 ? new HandSheet(camp, who) : People(camp, WorkersSheet.Filter.Stuck),
                });
            }
            if (l.TimberStarved || l.StoneStarved)
                into.Add(new Alert
                {
                    text = "Builders short of " + (l.TimberStarved ? "timber" : "stone"),
                    tone = Tone.Warn, open = () => ShortSite(camp), fixLabel = "Open",
                });
            // Two more "the camp is short of a place" chips (2026-09-30, island
            // UI phase 3, rule 2: every problem carries its fix). Both open the
            // Build list on the plan that fixes them.
            int bedless = BedlessHands(l);
            if (bedless > 0)
                into.Add(new Alert
                {
                    text = $"{bedless} hand{(bedless == 1 ? "" : "s")} {(bedless == 1 ? "has" : "have")} no bed",
                    tone = Tone.Warn, open = () => BuildList(camp, BuildPlans.Hut.id), fixLabel = "Build a hut",
                });
            if (storeFull && !stops)
                into.Add(new Alert { text = full, tone = Tone.Warn, open = () => BuildList(camp, BuildPlans.Storage.id), fixLabel = "Build storage" });
            int angry = l.AngryCount;
            if (angry > 0)
                into.Add(new Alert { text = $"{angry} hand{(angry == 1 ? "" : "s")} angry", tone = Tone.Warn, open = () => People(camp, WorkersSheet.Filter.Unhappy), fixLabel = "Open" });
        }

        /// **The farm's lines (2026-10-04, Kevin's phone: nine ripe plots
        /// and "no worker", with nothing saying so or offering the fix).**
        /// Per farm: "Farm · 9 plots ripe, nobody picking" (Assign) while no
        /// farmhand works it -- or "Farm · 3 plots to plant, no farmer" --
        /// and "Farm · 2 plots empty" (Choose crop) for plots left with no
        /// crop at all. Each opens that farm's sheet, whose plots page has
        /// the Assign button and the crop picker.
        static void FarmAlerts(Outpost camp, OutpostLedger l, List<Alert> into)
        {
            string farmId = BuildPlans.Farm.id;
            int farms = l.CountBuilt(farmId);
            if (farms <= 0) return;
            foreach (var b in camp.Built)
            {
                if (b == null || b.Id != farmId) continue;
                int ord = camp.OrdinalOf(b);
                if (ord < 0) continue;
                int ripe = 0, toPlant = 0, bare = 0;
                foreach (var p in l.PlotsOf(ord))
                {
                    if (p == null) continue;
                    if (p.state == PlotState.Ripe) ripe++;
                    else if (p.state == PlotState.Empty)
                    {
                        if (string.IsNullOrEmpty(p.crop)) bare++;
                        else toPlant++;
                    }
                }
                bool farmer = false;
                foreach (var h in l.hands)
                    if (h != null && h.order == OutpostOrder.Work && h.target == farmId && l.OrdinalOfHand(h) == ord)
                    { farmer = true; break; }
                string name = farms > 1 ? $"Farm {ord + 1}" : "Farm";
                var farm = b;
                Func<ISheet> open = () => Sheets.TryCreateFor(farm) ?? new FarmSheet(camp, farm);
                if (!farmer && ripe > 0)
                    into.Add(new Alert
                    {
                        text = $"{name} · {ripe} plot{(ripe == 1 ? "" : "s")} ripe, nobody picking",
                        tone = Tone.Warn, fixLabel = "Assign", open = open,
                    });
                else if (!farmer && toPlant > 0)
                    into.Add(new Alert
                    {
                        text = $"{name} · {toPlant} plot{(toPlant == 1 ? "" : "s")} to plant, no farmer",
                        tone = Tone.Warn, fixLabel = "Assign", open = open,
                    });
                if (bare > 0)
                    into.Add(new Alert
                    {
                        text = $"{name} · {bare} plot{(bare == 1 ? "" : "s")} empty",
                        tone = Tone.Warn, fixLabel = "Choose crop", open = open,
                    });
            }
        }

        /// Flour in the store before this the line is worth a word: one batch.
        const int FlourWaitingAt = 2;

        /// **"Flour waiting · Kitchen II bakes bread" (2026-10-04).** The
        /// mill runs from fire II, but everything that takes flour (bread,
        /// ship's biscuit, fish pie) wants a Kitchen at level II or more, so
        /// flour piled up in Kevin's store with nothing able to use it and
        /// nothing saying why. Shown while flour is in the store and NO
        /// recipe that takes it can run here (`RecipeAvailable` and its
        /// station standing); names the cheapest one that would, and opens
        /// that station (its level page) or the build list.
        ///
        /// **With the flour hold (2026-10-04)** "nothing can use it" is
        /// `OutpostLedger.FlourHasUse`, the one test the mill's hold reads
        /// too: from `FlourHoldAt` flour this chip stands beside a mill that
        /// says "Mill paused · nothing bakes flour yet", and is the fix-it
        /// pointer for both (the miller's own stall chip stands aside).
        static void FlourAlert(Outpost camp, OutpostLedger l, List<Alert> into)
        {
            if (l.StoreCountOf(Res.Flour) < FlourWaitingAt) return;
            if (l.FlourHasUse()) return;   // something can use it
            Recipe best = null;
            foreach (var r in Recipes.All)
            {
                if (r == null || !Takes(r, Res.Flour)) continue;
                if (best == null || r.stationLevel < best.stationLevel) best = r;
            }
            if (best == null) return;
            string station = BuildPlans.Named(best.station).label;
            if (string.IsNullOrEmpty(station)) station = best.station;
            station = char.ToUpperInvariant(station[0]) + station.Substring(1);
            string lv = best.stationLevel > 1 ? " " + RecipeGraph.Roman(best.stationLevel) : "";
            bool standing = l.CountBuilt(best.station) > 0;
            string stationId = best.station;
            into.Add(new Alert
            {
                text = $"Flour waiting · {station}{lv} bakes {ResDefs.Label(best.makes)}",
                tone = Tone.Warn,
                fixLabel = standing ? $"Raise {station}" : $"Build {station.ToLowerInvariant()}",
                open = () =>
                {
                    if (camp != null)
                        foreach (var b in camp.Built)
                            if (b != null && b.Id == stationId)
                                return Sheets.TryCreateFor(b) ?? new StationSheet(camp, b);
                    return BuildList(camp, stationId);
                },
            });
        }

        static bool Takes(Recipe r, string res)
        {
            if (r.takes == null) return false;
            foreach (var line in r.takes) if (line.res == res) return true;
            return false;
        }

        /// Living hands past the camp's beds (`HousingCapacity`); 0 when
        /// everyone has one. A downed hand needs no bed until he is up.
        internal static int BedlessHands(OutpostLedger l)
        {
            if (l == null) return 0;
            int living = 0;
            foreach (var h in l.hands) if (h != null && !h.downed) living++;
            return Mathf.Max(0, living - l.HousingCapacity);
        }

        /// **"Store full" (2026-09-30; RETIRED 2026-10-03, infinite stacking
        /// -- always false, see the first line of the body)**: a pile sat at
        /// the store's ceiling (`ceilingPer`, the same of EACH thing) AND a
        /// hand was trying to add to it -- gathering it, a station whose
        /// rack can go nowhere, a load waiting at the store
        /// (`OutpostLedger.StoreFullFor`). A full pile nobody is feeding is
        /// a saving, not a problem, so it is not an alert. Since 2026-10-02
        /// the chip names the piles AND the fix, whole (it wraps, never
        /// cut): "Store full of boards · build or upgrade a store hut". The
        /// fix was a store hut (Build).
        /// `stops`: somebody's work has stopped on it (not only a
        /// gatherer's pile topped out).
        internal static bool StoreFullText(OutpostLedger l, out string text, out bool stops)
        {
            text = null;
            stops = false;
            // **Retired 2026-10-03** (Kevin: infinite stacking): the island
            // store is never full, so there is no "Store full" line here or
            // in the Problems list. Body kept for the day a cap returns.
            if (l == null || !OutpostLedger.IslandStoreCapped) return false;
            var piles = fullPiles;
            piles.Clear();
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                string res = l.StoreFullFor(h);
                if (string.IsNullOrEmpty(res)) continue;
                if (h.order != OutpostOrder.Gather) stops = true;
                if (!piles.Contains(res)) piles.Add(res);
            }
            if (piles.Count == 0)
            {
                // Any other stall that says "full" (a farm's harvest).
                foreach (var h in l.hands)
                {
                    if (h == null || h.walkingIn || !l.Stalled(h)) continue;
                    string why = l.StallReason(h);
                    if (!string.IsNullOrEmpty(why) && why.IndexOf("full", StringComparison.Ordinal) >= 0)
                    {
                        text = "Store full · build or upgrade a store hut";
                        stops = true;
                        return true;
                    }
                }
                return false;
            }
            string what = ResDefs.Label(piles[0]);
            if (piles.Count == 2) what += " and " + ResDefs.Label(piles[1]);
            else if (piles.Count == 3) what += ", " + ResDefs.Label(piles[1]) + " and " + ResDefs.Label(piles[2]);
            else if (piles.Count > 3) what += ", " + ResDefs.Label(piles[1]) + " and " + (piles.Count - 2) + " more";
            text = "Store full of " + what + " · build or upgrade a store hut";
            return true;
        }

        static readonly List<string> fullPiles = new List<string>(4);

        /// "Sawmill", or "Sawmill 2" when the camp has more than one.
        internal static string StationName(OutpostLedger l, StationStock s)
        {
            string label = BuildPlans.Named(s.planId).label;
            if (string.IsNullOrEmpty(label)) label = "station";
            label = char.ToUpperInvariant(label[0]) + label.Substring(1);
            return l != null && l.CountBuilt(s.planId) > 1 ? label + " " + (s.ordinal + 1) : label;
        }

        /// The building a station row stands for: the `ordinal`-th built
        /// one of its plan (the deal `Outpost.WorkplaceOf` makes).
        internal static Building BuildingOf(Outpost camp, StationStock s)
        {
            if (camp == null || s == null) return null;
            int k = 0;
            foreach (var b in camp.Built)
                if (b != null && b.Id == s.planId && k++ == s.ordinal) return b;
            return null;
        }

        /// A live "X lost the sawmill post" line for this plan already says
        /// the station is unmanned.
        static bool PostLostAt(OutpostLedger l, Outpost camp, string planId)
        {
            if (l.postsLost == null) return false;
            foreach (var p in l.postsLost)
            {
                if (p == null || p.planId != planId) continue;
                var hand = camp.HandNamed(p.name);
                if (hand != null && hand.order != OutpostOrder.Work) return true;
            }
            return false;
        }

        /// The store hut's own sheet (its Runners tab), else the build list
        /// on the store hut.
        internal static ISheet StoreHut(Outpost camp)
        {
            if (camp != null)
                foreach (var b in camp.Built)
                    if (b != null && b.Id == BuildPlans.Storage.id)
                        return Sheets.TryCreateFor(b) ?? new StationSheet(camp, b);
            return BuildList(camp, BuildPlans.Storage.id);
        }

        /// The body's "walled off — no way round, needs a gate"
        /// (`CampWorker`), which the walled-off camp chip covers.
        /// Since 2026-10-05 also a station worker's "cut off by the wall ·
        /// needs a gate" (`MissingWords.IsWalledOff`).
        static bool IsWalledOff(OutpostHand h) => MissingWords.IsWalledOff(h.bodyBlocked);

        /// A stall reason in chip words. "waiting for stone: none
        /// left here" -> "no stone left"; anything else keeps its head.
        internal static string Short(string why)
        {
            if (string.IsNullOrEmpty(why)) return "";
            const string waiting = "waiting for ";
            if (why.StartsWith(waiting, StringComparison.Ordinal))
            {
                string rest = why.Substring(waiting.Length);
                int colon = rest.IndexOf(':');
                if (colon > 0 && rest.IndexOf("none left", StringComparison.Ordinal) > 0)
                    return "no " + rest.Substring(0, colon) + " left";
                int paren = rest.IndexOf(" (", StringComparison.Ordinal);
                return "needs " + (paren > 0 ? rest.Substring(0, paren) : colon > 0 ? rest.Substring(0, colon) : rest);
            }
            int dash = why.IndexOf(" — ", StringComparison.Ordinal);
            if (dash > 0) why = why.Substring(0, dash);
            // Never cut mid-word with an ellipsis (Kevin, 2026-10-02): the chip wraps.
            return why;
        }

        /// Camp › People (2026-09-27; was the campfire sheet's hands tab),
        /// opened on a filter (idle → Stuck, angry → Unhappy; menu rework #4).
        internal static ISheet People(Outpost camp, WorkersSheet.Filter filter = WorkersSheet.Filter.All) =>
            camp != null ? new WorkersSheet(camp, filter) : null;

        /// The food chips (menu rework #4): the Larder, not the roster.
        internal static ISheet Larder(Outpost camp) => camp != null ? new FoodSheet(camp) : null;

        /// "Builders short of X" (menu rework #5): the blueprint the
        /// builders are serving (`OutpostLedger.Focus`), on its own site
        /// sheet; the build list only when that drawing is not in the world.
        internal static ISheet ShortSite(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            var focus = l != null ? l.Focus : null;
            if (focus != null)
            {
                foreach (var s in UnityEngine.Object.FindObjectsByType<BuildSite>(UnityEngine.FindObjectsSortMode.None))
                    if (s != null && s.Row == focus && SheetBits.OutpostOf(s) == camp)
                        return new SiteSheet(camp, s);
            }
            return BuildList(camp);
        }

        /// Camp › Build (2026-09-27; was the campfire sheet's build tab).
        internal static ISheet BuildList(Outpost camp, string focusPlanId = null) =>
            camp != null ? new BuildSheet(camp, focusPlanId) : null;

        /// The kitchen's own page if the camp has one, else the build list
        /// (where the kitchen is put up). The mood sheet's "Cook at the
        /// kitchen" (2026-09-30).
        internal static ISheet Kitchen(Outpost camp)
        {
            if (camp != null)
                foreach (var b in camp.Built)
                    if (b != null && b.Id == BuildPlans.Kitchen.id)
                        return Sheets.TryCreateFor(b) ?? new StationSheet(camp, b);
            return BuildList(camp, BuildPlans.Kitchen.id);
        }

        /// The forge's own page if the camp has one, else the build list
        /// (where the forge is put up).
        internal static ISheet Forge(Outpost camp)
        {
            if (camp != null)
                foreach (var b in camp.Built)
                    if (b != null && b.Id == BuildPlans.Blacksmith.id)
                        return Sheets.TryCreateFor(b) ?? new StationSheet(camp, b);
            return BuildList(camp, BuildPlans.Blacksmith.id);
        }
    }
}
