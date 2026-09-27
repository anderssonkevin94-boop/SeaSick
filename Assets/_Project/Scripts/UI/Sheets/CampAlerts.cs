using System;
using System.Collections.Generic;
using SeaSick.World.Economy;
using SeaSick.World;

namespace SeaSick.UI.Sheets
{
    /// **What is wrong with the camp, as things you can tap, 2026-09-27.**
    ///
    /// The same facts `FireSheet.CampWarning` joins into one line (builders
    /// short, angry hands, nobody on watch, idle hands), plus the raid and
    /// the food clock, each carrying WHERE it gets fixed. `AlertStrip` shows
    /// the first three under the resource bar; the ledger drawer's Overview
    /// row counts them. Ordered by how soon it hurts: a raid on the sand,
    /// then an empty food pile, then the watch, then single hands.
    public static class CampAlerts
    {
        public enum Tone { Raid, Bad, Warn }

        public struct Alert
        {
            public string text;
            public Tone tone;
            public Func<ISheet> open;
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
                into.Add(new Alert { text = raid, tone = Tone.Raid, open = () => new FireSheet(camp, FireSheet.FocusLookout) });

            if (l.hands.Count > 0)
            {
                float days = SheetBits.FoodDays(l);
                if (l.Hungry)
                    into.Add(new Alert { text = "Out of food · hands hungry", tone = Tone.Bad, open = () => People(camp) });
                else if (days >= 0f && days < 1f)
                    into.Add(new Alert { text = "Food · under a day", tone = Tone.Bad, open = () => People(camp) });
            }

            if (l.HasWatchtower && !l.LookoutPosted)
                into.Add(new Alert { text = "Nobody on watch", tone = Tone.Warn, open = () => new FireSheet(camp, FireSheet.FocusLookout) });

            // One chip per stuck hand -- the hand is where the fix is (his
            // orders). A hunter with no spear gets the forge instead: the
            // spear is made there, not on his sheet.
            int idle = 0;
            string firstIdle = null;
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                if (h.order == OutpostOrder.Idle) { idle++; if (firstIdle == null) firstIdle = h.name; continue; }
                if (h.walkingIn) continue;
                if (h.order == OutpostOrder.Gather && h.target == Res.Game && l.HunterBlocker() != null)
                {
                    into.Add(new Alert { text = "No spear · no hunting", tone = Tone.Bad, open = () => Forge(camp) });
                    continue;
                }
                if (!l.Stalled(h) && string.IsNullOrEmpty(h.bodyBlocked)) continue;
                string why = l.StallReason(h);
                if (string.IsNullOrEmpty(why)) continue;
                string who = h.name;
                into.Add(new Alert { text = who + " · " + Short(why), tone = Tone.Bad, open = () => new HandSheet(camp, who) });
            }
            if (idle > 0)
            {
                string who = firstIdle;
                into.Add(new Alert
                {
                    text = idle == 1 ? who + " · idle" : $"{idle} hands idle",
                    tone = Tone.Warn,
                    open = () => idle == 1 ? new HandSheet(camp, who) : People(camp),
                });
            }
            if (l.TimberStarved || l.StoneStarved)
                into.Add(new Alert
                {
                    text = "Builders short of " + (l.TimberStarved ? "timber" : "stone"),
                    tone = Tone.Warn, open = () => BuildList(camp),
                });
            int angry = l.AngryCount;
            if (angry > 0)
                into.Add(new Alert { text = $"{angry} hand{(angry == 1 ? "" : "s")} angry", tone = Tone.Warn, open = () => People(camp) });
        }

        /// A stall reason cut down to chip length. "waiting for stone: none
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
            return why.Length > 24 ? why.Substring(0, 23) + "…" : why;
        }

        /// Camp › People (2026-09-27; was the campfire sheet's hands tab).
        internal static ISheet People(Outpost camp) => camp != null ? new PeopleSheet(camp) : null;

        /// Camp › Build (2026-09-27; was the campfire sheet's build tab).
        internal static ISheet BuildList(Outpost camp, string focusPlanId = null) =>
            camp != null ? new BuildSheet(camp, focusPlanId) : null;

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
