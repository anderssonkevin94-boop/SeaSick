using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    // Compact food-draft notice + "Undo all", shown ONLY while a food draft is
    // active (the tally line was removed 2026-09-30). It sits just ABOVE the
    // thumb bar (`ThumbBar.ReservePanel`), or above the safe-area bottom edge
    // when the bar is hidden; keep it one short row. Hidden behind sheets; notices remain undoable
    // until the draft ends. The ledger owns both the draft and the veto (saved
    // on the hand until the draft ends, 2026-10-03: OutpostLedger.FoodDraft.cs).
    internal sealed class CampStatusHud
    {
        readonly VisualElement footer, banner;
        readonly Label notice;
        readonly Button undo;
        OutpostLedger ledger;
        readonly List<OutpostHand> draft = new List<OutpostHand>();
        string message;
        float nextRefresh;

        public CampStatusHud(VisualElement root)
        {
            footer = new VisualElement(); footer.AddToClassList("camp-status-footer");
            banner = new VisualElement(); banner.AddToClassList("camp-food-draft");
            notice = new Label(); notice.AddToClassList("camp-food-notice");
            undo = new Button(Undo) { text = "Undo all" };
            undo.AddToClassList("camp-food-undo");
            banner.Add(notice); banner.Add(undo); footer.Add(banner);
            root.Add(footer);
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == root) Bind(null); });
            Hide();
        }

        void Bind(OutpostLedger next)
        {
            if (ledger == next) return;
            if (ledger != null) ledger.FoodDraftNotice -= OnDraft;
            ledger = next; message = null; draft.Clear(); nextRefresh = 0f;
            if (ledger != null) ledger.FoodDraftNotice += OnDraft;
        }

        void OnDraft(string text) { message = text; nextRefresh = 0f; }

        void Undo()
        {
            if (ledger == null) return;
            // Copy: FoodDrafted is a reused ledger buffer, not a stable snapshot.
            draft.Clear(); draft.AddRange(ledger.FoodDrafted);
            foreach (var hand in draft) ledger.UndoFoodDraft(hand);
            message = null; nextRefresh = 0f;
        }

        /// "Food low — Pip cooking, Gale foraging": who is on food and at
        /// what, for a reload (the draft's own sentence is not saved).
        static string DraftLine(List<OutpostHand> hands)
        {
            var parts = new List<string>(hands.Count);
            foreach (var h in hands)
            {
                if (h == null) continue;
                string doing = h.target == BuildPlans.Farm.id ? "farming"
                    : h.target == BuildPlans.Kitchen.id ? "cooking"
                    : h.target == Res.Game ? "hunting" : "foraging";
                parts.Add(h.name + " " + doing);
            }
            return parts.Count == 0 ? "Food low" : "Food low — " + string.Join(", ", parts);
        }

        public void Hide() { footer.style.display = DisplayStyle.None; TopPanel = 0f; }

        /// **Top of the food-draft notice, in panel units up from the panel's
        /// bottom; 0 while it is hidden (2026-09-30).** `GestureHintPill`
        /// stacks above it rather than hiding for as long as a draft lasts
        /// (one can last days). Written by the last `Tick`, one frame stale
        /// for anything ticked before the land HUD.
        public static float TopPanel { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTop() => TopPanel = 0f;

        public Rect Tick(Outpost camp, bool active, float scale)
        {
            Bind(camp != null ? camp.Ledger : null);
            if (!active || ledger == null || SheetHost.FrameOpen) { Hide(); return Rect.zero; }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .25f;
                draft.Clear();
                // **Only while food really is short (2026-10-02 play check):**
                // the banner read "Food low — villagers gathering food" over a
                // store at its ceiling of potatoes, baked potatoes, fish and
                // forage, because the emergency's cook stays on above `FedDays`
                // (hysteresis). `FoodShort` is the top bar's own count (ice
                // under 3 days); above it the hand's own line says why he is
                // still on food, and nothing says "low".
                if (ledger.FoodShort) draft.AddRange(ledger.FoodDrafted);
                if (draft.Count == 0) message = null;
                notice.text = message ?? DraftLine(draft);
                // The notice describes the last event; the button explicitly undoes
                // ALL current drafts, including an earlier still-active batch.
                undo.text = "Undo all (" + draft.Count + ")";
                banner.style.display = draft.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // No draft, nothing to show: the footer must not reserve space.
            if (draft.Count == 0) { Hide(); return Rect.zero; }
            footer.style.display = DisplayStyle.Flex;
            var safe = Screen.safeArea;
            footer.style.left = safe.xMin * scale + 8f;
            footer.style.right = (Screen.width - safe.xMax) * scale + 8f;
            float bottom = Mathf.Max(safe.yMin * scale + 8f, ThumbBar.ReservePanel);
            footer.style.bottom = bottom;
            // Use actual wrapped height after layout, with a first-frame fallback.
            float height = footer.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0) height = 64f;
            TopPanel = bottom + height;
            return new Rect(safe.xMin + 8f / scale, Screen.height - (bottom + height) / scale,
                safe.width - 16f / scale, height / scale);
        }
    }
}
