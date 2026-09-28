using UnityEngine;

namespace SeaSick.UI
{
    /// The one line a watched camp shows about the raid it's living through
    /// (or just finished) -- a warning while a raider is standing off, a
    /// scoreline while her party is on the sand, and a verdict for a few
    /// seconds after the ship has gone. Kept as a static drawer beside
    /// `ReturnSummary`, same `OnGUI`/`HudLayout.ToastRow` idiom, so the two
    /// toasts never fight for the same row.
    public static class RaidBanner
    {
        static readonly HudLabel label = new HudLabel();

        /// Called every OnGUI event from `CampToasts.OnGUI`. Costs nothing
        /// when there's nothing to say.
        public static void Draw(World.Outpost outpost)
        {
            if (outpost == null) return;

            string text;
            int phase;

            var incoming = SeaSick.Combat.RaidDirector.Incoming(outpost);
            if (incoming != null && !incoming.Beached)
            {
                if (!SeaSick.Combat.RaidDirector.WarnedOf(outpost)) return;
                text = "the lookout: RAIDERS making for the beach";
                phase = 1;
            }
            else if (SeaSick.Combat.RaidParty.Active != null && SeaSick.Combat.RaidParty.Active.Camp == outpost)
            {
                var party = SeaSick.Combat.RaidParty.Active;
                // **Phase 9 (village defence):** half the landing party
                // down and the rest are running for their boat -- a
                // different line than the ordinary scoreline while that
                // lasts.
                text = party.MoraleBroken
                    ? $"the raiders are running — {party.Ashore} still on the sand"
                    : $"RAID — {party.Ashore} ashore · {party.Stolen} taken · sink the ship";
                phase = 2;
            }
            else
            {
                string last = SeaSick.Combat.RaidDirector.LastResult(outpost);
                if (string.IsNullOrEmpty(last)) return;
                text = last;
                phase = 3;
            }

            // **Ashore, the alert strip says it, 2026-09-27**: its RAID chip
            // (tap -> the lookout) replaces this line while the land HUD is
            // up. The after-raid verdict (phase 3) still shows here, and at
            // sea this banner is the only voice.
            if (phase != 3 && Sheets.AlertStrip.ShowsRaid && Sheets.MidnightLandHud.Active) return;

            int stolen = SeaSick.Combat.RaidParty.Active != null ? SeaSick.Combat.RaidParty.Active.Stolen : 0;
            int ashore = SeaSick.Combat.RaidParty.Active != null ? SeaSick.Combat.RaidParty.Active.Ashore : 0;
            long key = HudLabel.Key(outpost.GetInstanceID(), stolen, ashore, phase);
            if (label.Changed(key)) label.Set(text);

            var style = UITheme.Toast;
            float width = Mathf.Min(HudLayout.Safe.width - HudLayout.Unit * 2f, HudLayout.Unit * 30f);
            float height = HudLayout.Unit * 2.2f;

            var rect = HudLayout.ToastRow(height, width);
            UITheme.ToastCard(rect, UITheme.LedgerEmber);   // Ledger toast card, ember: a raid
            var inset = new Rect(rect.x + HudLayout.Pad, rect.y, rect.width - HudLayout.Pad * 2f, rect.height);
            GUI.Label(inset, label.Content, style);
        }
    }
}
