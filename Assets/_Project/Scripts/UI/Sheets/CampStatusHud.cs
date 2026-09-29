using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    // Fixed-size, safe-area footer. Hidden behind sheets; notices remain undoable
    // until the draft ends. The ledger owns both the draft and the one-day veto.
    internal sealed class CampStatusHud
    {
        readonly VisualElement footer, tally, banner;
        readonly Label notice;
        readonly Label[] counts = new Label[8];
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
            tally = new VisualElement(); tally.AddToClassList("camp-tally");
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = new Label { pickingMode = PickingMode.Ignore };
                counts[i].AddToClassList("camp-tally-count"); tally.Add(counts[i]);
            }
            counts[5].AddToClassList("camp-status-muted");
            counts[6].AddToClassList("camp-status-danger");
            counts[7].AddToClassList("camp-status-danger");
            footer.Add(tally); root.Add(footer);
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

        public void Hide() { footer.style.display = DisplayStyle.None; }

        public Rect Tick(Outpost camp, bool active, float scale)
        {
            Bind(camp != null ? camp.Ledger : null);
            if (!active || ledger == null || SheetHost.FrameOpen) { Hide(); return Rect.zero; }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .25f;
                var t = ledger.Tally();
                int[] values = { t.building, t.hauling, t.working, t.gathering, t.reserve, t.noWork, t.stuck, t.downed };
                string[] words = { "building", "hauling", "working", "gathering", "reserve", "no work", "stuck", "downed" };
                for (int i = 0; i < counts.Length; i++)
                {
                    counts[i].text = values[i] + " " + words[i];
                    counts[i].style.display = values[i] > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
                draft.Clear(); draft.AddRange(ledger.FoodDrafted);
                if (draft.Count == 0) message = null;
                notice.text = message ?? "Food low — villagers gathering food";
                // The notice describes the last event; the button explicitly undoes
                // ALL current drafts, including an earlier still-active batch.
                undo.text = "Undo all (" + draft.Count + ")";
                banner.style.display = draft.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            footer.style.display = DisplayStyle.Flex;
            var safe = Screen.safeArea;
            footer.style.left = safe.xMin * scale + 8f;
            footer.style.right = (Screen.width - safe.xMax) * scale + 8f;
            footer.style.bottom = safe.yMin * scale + 8f;
            // Use actual wrapped height after layout, with a first-frame fallback.
            float height = footer.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0) height = draft.Count > 0 ? 150f : 64f;
            return new Rect(safe.xMin + 8f / scale, Screen.height - safe.yMin - (height + 8f) / scale,
                safe.width - 16f / scale, height / scale);
        }
    }
}
