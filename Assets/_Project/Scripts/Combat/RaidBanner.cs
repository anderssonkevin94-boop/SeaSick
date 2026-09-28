using UnityEngine;

namespace SeaSick.UI
{
    /// The one line a watched camp shows about the raid it's living through
    /// (or just finished) -- a warning while a raider is standing off, a
    /// scoreline while her party is on the sand, and a verdict for a few
    /// seconds after the ship has gone. Kept as a static drawer beside
    /// `ReturnSummary`, same `OnGUI`/`HudLayout.ToastRow` idiom, so the two
    /// toasts never fight for the same row.
    ///
    /// **Phase 11 (2026-09-28), docs/PLAN-DEATH-RESCUE.md "What you see" +
    /// "The Fight / Hide-all switch":** while the party is on the sand the
    /// toast also carries the alarm's own counts (defending / hiding /
    /// spears left in the store), and a big bottom button -- protected from
    /// `IslandInput`'s tap router the same way `Ship.Overboard.CastawayHud`'s
    /// "Take X aboard" already is -- sends everyone into hiding or calls the
    /// armed back out (`Combat.RaidAlarm.HideAll`). This file lives in
    /// `Combat/`, not `Scripts/UI` -- it is gameplay (a raid decision), the
    /// same reasoning that already put `RescueHud`/`CastawayHud` outside the
    /// menu tree.
    public static class RaidBanner
    {
        static readonly HudLabel label = new HudLabel();
        static GUIStyle wrapStyle;

        /// Called every OnGUI event from `CampToasts.OnGUI`. Costs nothing
        /// when there's nothing to say.
        public static void Draw(World.Outpost outpost)
        {
            if (outpost == null) return;

            string text;
            int phase;
            bool live = false;
            int defending = 0, hiding = 0, spearsLeft = 0;

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
                live = true;
                (defending, hiding, spearsLeft) = SeaSick.Combat.RaidAlarm.Counts(outpost);
                string spearWord = spearsLeft == 0 ? "no spears left"
                    : spearsLeft == 1 ? "1 spear left" : $"{spearsLeft} spears left";
                // **Phase 9 (village defence):** half the landing party
                // down and the rest are running for their boat -- a
                // different line than the ordinary scoreline while that
                // lasts. Two short lines either way: the raiders/loot/goal
                // line, then who's doing what about it.
                text = party.MoraleBroken
                    ? $"the raiders are running — {party.Ashore} still on the sand\n"
                      + $"{defending} defending · {hiding} hiding · {spearWord}"
                    : $"RAID — {party.Ashore} ashore · {party.Stolen} taken · sink the ship\n"
                      + $"{defending} defending · {hiding} hiding · {spearWord}";
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
            //
            // The Fight / Hide-all switch is still drawn ashore (it is the
            // only place the player can reach it until Astra's RaidSheet
            // carries one -- see the write-up at the bottom of this file);
            // only the text line gives way to the alert strip.
            if (phase != 3 && Sheets.AlertStrip.ShowsRaid && Sheets.MidnightLandHud.Active)
            {
                if (live) DrawSwitch(outpost);
                return;
            }

            int stolen = SeaSick.Combat.RaidParty.Active != null ? SeaSick.Combat.RaidParty.Active.Stolen : 0;
            int ashore = SeaSick.Combat.RaidParty.Active != null ? SeaSick.Combat.RaidParty.Active.Ashore : 0;
            long key = HudLabel.Key(outpost.GetInstanceID(), stolen, ashore,
                phase * 10000 + defending * 100 + hiding * 10 + spearsLeft);
            if (label.Changed(key)) label.Set(text);

            var style = phase == 2 ? WrapStyle() : UITheme.Toast;
            float width = Mathf.Min(HudLayout.Safe.width - HudLayout.Unit * 2f, HudLayout.Unit * 30f);
            float height = phase == 2 ? HudLayout.Unit * 4.2f : HudLayout.Unit * 2.2f;

            var rect = HudLayout.ToastRow(height, width);
            UITheme.ToastCard(rect, UITheme.LedgerEmber);   // Ledger toast card, ember: a raid
            var inset = new Rect(rect.x + HudLayout.Pad, rect.y, rect.width - HudLayout.Pad * 2f, rect.height);
            GUI.Label(inset, label.Content, style);

            if (live) DrawSwitch(outpost);
        }

        /// The phase-3 style, unwrapped, is `UITheme.Toast` itself -- a live
        /// raid's two lines need `wordWrap` on top of it, which is the only
        /// difference, kept as one small style built once rather than a
        /// `new GUIStyle` every OnGUI event (IMGUI keys its cached text mesh
        /// on the style instance).
        static GUIStyle WrapStyle()
        {
            if (wrapStyle == null) wrapStyle = new GUIStyle(UITheme.Toast) { wordWrap = true };
            wrapStyle.fontSize = UITheme.Toast.fontSize;   // tracks Unit across a resize
            return wrapStyle;
        }

        /// **The Fight / Hide-all switch (phase 11).** A big bottom-centre
        /// button, same slot `Ship.Overboard.CastawayHud` uses for "Take X
        /// aboard" (`HudLayout.BottomClustersTop`, `UIBlocker.Block` on its
        /// rect so a tap here never reaches `IslandInput`/the helm
        /// underneath). Desktop keeps a keyboard mirror (H) beside it --
        /// never the only path, same rule the cast-off/recall buttons keep
        /// for space.
        static void DrawSwitch(World.Outpost outpost)
        {
            bool hiding = SeaSick.Combat.RaidAlarm.IsHiding(outpost);

            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool pressed = kb != null && kb.hKey.wasPressedThisFrame;

            int u = HudLayout.Unit;
            float btnH = Mathf.Max(u * 3.4f, 56f);
            float btnW = Mathf.Min(HudLayout.Safe.width - HudLayout.Pad * 2f, u * 26f);
            var safe = HudLayout.Safe;
            var rect = new Rect(safe.x + (safe.width - btnW) * 0.5f,
                HudLayout.BottomClustersTop - HudLayout.Gap - btnH, btnW, btnH);
            UIBlocker.Block(rect);

            string text = hiding ? "Send the armed out" : "Hide everyone";
            if (HudLayout.Wide) text += "   (H)";   // desktop-only keyboard hint, never the only path

            if (GUI.Button(rect, text, UITheme.Button) || pressed)
                SeaSick.Combat.RaidAlarm.HideAll(outpost, !hiding);
        }
    }

    // --- for Astra (Scripts/UI) -------------------------------------------
    //
    // `RaidSheet` (Scripts/UI/Sheets/RaidSheet.cs) is the land-side raid
    // card -- it currently shows raiders/wall/gate chips and the lookout,
    // but nothing about who's defending, who's hiding, or spears left, and
    // it has no Hide-all button. Two things the land HUD is missing that
    // this file now has at sea:
    //   1. The counts -- `Combat.RaidAlarm.Counts(outpost)` returns
    //      (defending, hiding, spearsLeftInStore); a fourth chip or a line
    //      under the existing three would match.
    //   2. The switch -- `Combat.RaidAlarm.HideAll(outpost, hide)` and
    //      `Combat.RaidAlarm.IsHiding(outpost)` for the label; a UI Toolkit
    //      button in `RaidSheet`'s "hs-acts" thumb row (beside "Walls") is
    //      the natural spot, same shape as `mainBtn` there already.
    // The Hide-all button ALSO does not appear at all while
    // `Sheets.MidnightLandHud.Active` is true (this file returns before
    // drawing it), so ashore, on the land HUD, players have NO way to hit
    // the switch today -- RaidSheet is the only place that can fix that.
}
