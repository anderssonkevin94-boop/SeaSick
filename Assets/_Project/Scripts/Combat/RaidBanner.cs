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
                // **Phase 12:** a spear breaking mid-fight folds in as a
                // third line for a few seconds, on top of the scoreline.
                string flash = SeaSick.Combat.RaidAlarm.FlashMessage(outpost);
                if (!string.IsNullOrEmpty(flash)) text += "\n" + flash;
                phase = 2;
            }
            else
            {
                string last = SeaSick.Combat.RaidDirector.LastResult(outpost);
                if (string.IsNullOrEmpty(last)) return;
                text = last;
                phase = 3;
            }

            // **Ashore, the land HUD says it all, 2026-09-27 / 2026-09-30**:
            // its RAID chip (tap -> `RaidSheet`) replaces this line while the
            // land HUD is up, and Kevin's rule (*"no control is ever cut off
            // or half-hidden"*) took the floating "Hide everyone" button off
            // the phone too -- it sat over the chip and over the camp above
            // a half-height sheet. The switch now lives in `RaidSheet`'s
            // thumb row. The after-raid verdict (phase 3) still shows here,
            // and at sea (or on the classic HUD) this banner and its button
            // are the only voice.
            if (phase != 3 && Sheets.AlertStrip.ShowsRaid && Sheets.MidnightLandHud.Active)
            {
                // Desktop keeps the H mirror of the switch.
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (live && kb != null && kb.hKey.wasPressedThisFrame)
                    SeaSick.Combat.RaidAlarm.HideAll(outpost, !SeaSick.Combat.RaidAlarm.IsHiding(outpost));
                return;
            }

            int stolen = SeaSick.Combat.RaidParty.Active != null ? SeaSick.Combat.RaidParty.Active.Stolen : 0;
            int ashore = SeaSick.Combat.RaidParty.Active != null ? SeaSick.Combat.RaidParty.Active.Ashore : 0;
            long key = HudLabel.Key(outpost.GetInstanceID(), stolen, ashore,
                phase * 10000 + defending * 100 + hiding * 10 + spearsLeft);
            if (label.Changed(key)) label.Set(text);

            var style = phase == 2 ? WrapStyle() : UITheme.Toast;
            float width = Mathf.Min(HudLayout.Safe.width - HudLayout.Unit * 2f, HudLayout.Unit * 30f);
            bool threeLines = phase == 2 && text.IndexOf('\n') != text.LastIndexOf('\n');
            float height = phase == 2 ? HudLayout.Unit * (threeLines ? 5.6f : 4.2f)
                : HudLayout.Unit * (text.IndexOf('\n') >= 0 ? 3.4f : 2.2f);

            var rect = HudLayout.ToastRow(height, width);
            UITheme.ToastCard(rect, UITheme.LedgerEmber);   // Ledger toast card, ember: a raid
            var inset = new Rect(rect.x + HudLayout.Pad, rect.y, rect.width - HudLayout.Pad * 2f, rect.height);
            GUI.Label(inset, label.Content, style);

            // The land HUD's raid switch is `RaidSheet`'s, not an IMGUI button.
            if (live && !Sheets.MidnightLandHud.Active) DrawSwitch(outpost);
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

    // Ashore, on the land HUD, the Fight / Hide-all switch is a button in
    // `RaidSheet`'s thumb row (2026-09-30); `Draw` never draws the IMGUI one
    // there. At sea and on the classic HUD it is still drawn above.
}
