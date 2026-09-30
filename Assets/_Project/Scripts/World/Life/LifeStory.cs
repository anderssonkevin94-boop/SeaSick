using System.Collections.Generic;

namespace SeaSick.World.Life
{
    /// **Builds a dead person's 3-sentence story** (docs/PLAN-DEATH-RESCUE.md,
    /// "The tombstone"): how they lived, a second life detail, how they
    /// died. Hand-written templates with slots, picked deterministically off
    /// a stable hash of the name -- never `string.GetHashCode`, which is
    /// randomized per process on some runtimes (Mono/IL2CPP included), so
    /// the SAME grave would read a different story every launch.
    public static class LifeStory
    {
        /// FNV-1a, 32-bit. Stable across processes and platforms, which is
        /// the one property `GetHashCode` does not promise.
        public static uint Fnv32(string s)
        {
            uint h = 2166136261u;
            if (s != null)
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
            return h;
        }

        static string Ordinal(int n)
        {
            if (n <= 0) return "first";
            switch (n)
            {
                case 1: return "first";
                case 2: return "second";
                case 3: return "third";
                case 4: return "fourth";
                case 5: return "fifth";
                default: return n + "th";
            }
        }

        static string NumberWord(int n)
        {
            switch (n)
            {
                case 1: return "one";
                case 2: return "two";
                case 3: return "three";
                case 4: return "four";
                case 5: return "five";
                default: return n.ToString();
            }
        }

        static string CountWord(int n)
        {
            switch (n)
            {
                case 1: return "once";
                case 2: return "twice";
                case 3: return "three times";
                default: return n + " times";
            }
        }

        static string Pick(uint h, int salt, string[] variants)
        {
            if (variants == null || variants.Length == 0) return "";
            uint i = (h + (uint)salt * 2654435761u) % (uint)variants.Length;
            return variants[i];
        }

        /// The event on `r` with the highest `count` (ties broken by the
        /// latest `day`), or null if there are none.
        static LifeEvent Strongest(LifeRecord r)
        {
            if (r?.events == null || r.events.Count == 0) return null;
            LifeEvent best = null;
            foreach (var e in r.events)
            {
                if (e == null) continue;
                if (best == null || e.count > best.count
                    || (e.count == best.count && e.day > best.day)) best = e;
            }
            return best;
        }

        /// A second event, different from `already`, for sentence 2.
        static LifeEvent SecondBest(LifeRecord r, LifeEvent already)
        {
            LifeEvent best = null;
            if (r?.events == null) return null;
            foreach (var e in r.events)
            {
                if (e == null || e == already) continue;
                if (best == null || e.count > best.count
                    || (e.count == best.count && e.day > best.day)) best = e;
            }
            return best;
        }

        static string CampOr(string camp, string fallback = "the camp") =>
            string.IsNullOrEmpty(camp) ? fallback : camp;

        /// Sentence for one life event, whichever slot it lands in.
        static string Sentence(LifeEvent e, uint h, int salt, string homeCamp)
        {
            if (e == null) return "";
            string camp = CampOr(!string.IsNullOrEmpty(e.camp) ? e.camp : homeCamp);
            switch (e.kind)
            {
                case LifeEvents.Overboard:
                    return Pick(h, salt, new[]
                    {
                        "Went over the rail " + CountWord(e.count) + " and was hauled back "
                            + CountWord(e.count) + ".",
                        e.count > 1
                            ? "The sea came for them " + CountWord(e.count) + ", and lost every time."
                            : "The sea came for them once, and lost.",
                        "Went over the side more than once, and came back every time.",
                    });
                case LifeEvents.RescuedOther:
                    return Pick(h, salt, new[]
                    {
                        "Pulled " + (string.IsNullOrEmpty(e.other) ? "somebody" : e.other) + " from the sea.",
                        "Hauled " + (string.IsNullOrEmpty(e.other) ? "a shipmate" : e.other) + " back aboard when the water nearly had them.",
                        "Went over the side after " + (string.IsNullOrEmpty(e.other) ? "a shipmate" : e.other) + " and brought them home.",
                    });
                case LifeEvents.DraggedOther:
                    return Pick(h, salt, new[]
                    {
                        "Carried " + (string.IsNullOrEmpty(e.other) ? "somebody" : e.other) + " back to a hut, hurt but standing.",
                        "Dragged " + (string.IsNullOrEmpty(e.other) ? "a friend" : e.other) + " home when they went down.",
                        "Went out for " + (string.IsNullOrEmpty(e.other) ? "a fallen shipmate" : e.other) + " and brought them home on their feet.",
                    });
                case LifeEvents.Downed:
                    return Pick(h, salt, new[]
                    {
                        "Was carried home once, and walked again.",
                        "Went down at " + camp + ", and got back up.",
                        "Fell " + CountWord(e.count) + " and was carried home every time.",
                    });
                case LifeEvents.Hungry:
                    return Pick(h, salt, new[]
                    {
                        "Went hungry through the long winter at " + camp + ".",
                        "Went short more than once at " + camp + ", and kept working anyway.",
                        "Knew what it was to go without, at " + camp + ".",
                    });
                case LifeEvents.Pouted:
                    return Pick(h, salt, new[]
                    {
                        "Sulked by the fire more than once, and always came back to work.",
                        "Had his black moods at " + camp + ", and everyone learned to let them pass.",
                        "Stood by the fire and stewed " + CountWord(e.count) + ", and picked his tools back up every time.",
                    });
                case LifeEvents.SurvivedRaid:
                    return Pick(h, salt, new[]
                    {
                        "Stood through " + (e.count > 1 ? NumberWord(e.count) + " raids on " + camp : "a raid on " + camp) + " and walked away.",
                        "Was still standing when the raiders left " + camp + ".",
                    });
                case LifeEvents.KilledRaider:
                    return Pick(h, salt, new[]
                    {
                        "Put " + (e.count > 1 ? NumberWord(e.count) + " raiders" : "a raider") + " in the sand at " + camp + ".",
                        "Sent " + CountWord(e.count) + " raider" + (e.count > 1 ? "s" : "") + " back to the boats at " + camp + ", the hard way.",
                        "Was the one the raiders learned to go around, at " + camp + ".",
                    });
                case LifeEvents.DefendedCamp:
                    return Pick(h, salt, new[]
                    {
                        "Stood at the gate with a stone spear when the boats came.",
                        "Turned out for every raid on " + camp + " that ever came, spear in hand.",
                        "Held the line at " + camp + " more than once, and it held.",
                    });
                case LifeEvents.SpearBroke:
                    return Pick(h, salt, new[]
                    {
                        "Broke " + (e.count > 1 ? CountWord(e.count) + " spears" : "a spear")
                            + " on the raiders at " + camp + ".",
                    });
                case LifeEvents.HuntingKill:
                    return Pick(h, salt, new[]
                    {
                        "Brought down more game than anyone else at " + camp + ".",
                        "Fed " + camp + " off the herd, " + CountWord(e.count) + " over.",
                        "Was the surest hand at " + camp + " with a spear at a running animal.",
                    });
                case LifeEvents.Recruited:
                case LifeEvents.Born:
                    return Pick(h, salt, new[]
                    {
                        "Came up at " + camp + " and made a life of it.",
                        "Was raised at " + camp + ", and never left it for long.",
                        "Started at " + camp + " with nothing and stayed anyway.",
                    });
                case LifeEvents.WentAshore:
                    return Pick(h, salt, new[]
                    {
                        "Put down roots at " + camp + ".",
                        "Made " + camp + " a home, of sorts.",
                    });
                case LifeEvents.WashedAshore:
                    return Pick(h, salt, new[]
                    {
                        "Swam for an island and waited there to be fetched.",
                        "Went over the side and made land on " + camp + " alone.",
                        "Washed up on " + camp + " and waited out the days until a sail came.",
                    });
                case LifeEvents.FoundCastaway:
                    return Pick(h, salt, new[]
                    {
                        "Was picked up from " + camp + " and never looked back.",
                        "Waited on " + camp + " for a ship, and one finally came.",
                        "Was found on " + camp + " with nothing, and taken aboard anyway.",
                    });
                case LifeEvents.PulledFromSea:
                {
                    // Kevin, 2026-09-30: castaways on a board at sea.
                    string by = string.IsNullOrEmpty(e.other) ? "the ship's crew" : e.other;
                    return Pick(h, salt, new[]
                    {
                        "Pulled from the sea off " + camp + " by " + by + ".",
                        "Was found clinging to a board off " + camp + ", and " + by + " hauled them in.",
                        "Came out of the sea off " + camp + " with nothing but a plank and a name.",
                    });
                }
                case LifeEvents.Ferried:
                    return Pick(h, salt, new[]
                    {
                        "Was carried from " + (string.IsNullOrEmpty(e.other) ? "another island" : e.other)
                            + " to " + camp + " to start again.",
                        "Left " + (string.IsNullOrEmpty(e.other) ? "one camp" : e.other)
                            + " behind and made a new start at " + camp + ".",
                    });
                default:
                    return "";
            }
        }

        /// The 3-sentence story: lived, a second life detail, died. Never
        /// empty -- a name with no events at all still gets a generic
        /// fallback so a tombstone is never blank.
        public static string[] Build(LifeRecord r, GraveRecord g)
        {
            string homeCamp = r?.homeCamp;
            if (string.IsNullOrEmpty(homeCamp)) homeCamp = g?.camp;
            uint h = Fnv32(g?.name ?? r?.name ?? "");

            var first = Strongest(r);
            var second = SecondBest(r, first);

            string s1 = Sentence(first, h, 1, homeCamp);
            if (string.IsNullOrEmpty(s1))
                s1 = Pick(h, 11, new[]
                {
                    "Lived out their days at " + CampOr(homeCamp) + ", one of the quiet ones.",
                    "Kept to their work at " + CampOr(homeCamp) + " and never made trouble.",
                    "Was just another pair of hands at " + CampOr(homeCamp) + ", and none the worse for it.",
                });

            string s2 = Sentence(second, h, 2, homeCamp);
            if (string.IsNullOrEmpty(s2))
                s2 = Pick(h, 12, new[]
                {
                    "Nobody remembers them saying much.",
                    "Never once asked for more than their share.",
                    "Was there before most of the others, and after some of them too.",
                });

            string cause = g?.cause ?? "";
            string s3 = Pick(h, 3, CauseLines(cause));

            return new[] { s1, s2, s3 };
        }

        static string[] CauseLines(string cause)
        {
            switch (cause)
            {
                case LifeEvents.KilledInRaid:
                    return new[]
                    {
                        "Fell defending the palisade when the raiders came.",
                        "Went down fighting when the raiders came, and did not get back up.",
                        "Died with a spear in hand, holding the line.",
                    };
                case LifeEvents.LostAtSea:
                    return new[]
                    {
                        "Lost to the grey swell within sight of the ship.",
                        "Went under before the line could reach them.",
                        "The sea took them and gave nothing back.",
                    };
                case LifeEvents.HuntingAccident:
                    return new[]
                    {
                        "Went out after the herd once too often, and did not come back.",
                        "The hunt turned on them, and that was the end of it.",
                        "Died the way they lived: out after the next meal.",
                    };
                case LifeEvents.Shipwreck:
                    return new[]
                    {
                        "Went down with the ship.",
                        "Never made it off the wreck.",
                        "The hull broke, and so did their luck.",
                    };
                default:
                    return new[]
                    {
                        "Went down, and nobody came in time.",
                        "Fell where they stood, and was not carried home.",
                        "Their time simply ran out.",
                    };
            }
        }
    }
}
