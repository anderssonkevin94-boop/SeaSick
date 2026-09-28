using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **The hunger draft, made visible and undoable (2026-09-28, designer
    /// call).** `FeedFirst` (OutpostLedger.cs) drafts free hands onto food
    /// when the pile is under a day's eating: hunting when a hunt can start,
    /// else foraging (`Res.Food`, the wild-forage Gather order). Before this
    /// the player only noticed when his builders were suddenly "hunting";
    /// now each draft batch fires `FoodDraftNotice` with a ready sentence
    /// ("Food low — Finch and Gale sent hunting"), `FoodDrafted` lists who
    /// is out on the draft, and `UndoFoodDraft` sends one back, with a
    /// one-game-day veto so the very next quantum does not re-draft him.
    /// </summary>
    public partial class OutpostLedger
    {
        /// **Fired once per draft batch** with the sentence for a banner.
        /// Raised from `Step` (the ledger tick), on the main thread.
        [field: System.NonSerialized]
        public event System.Action<string> FoodDraftNotice;

        /// Names drafted in the batch `FeedFirst` is making.
        [System.NonSerialized] readonly List<string> draftNames = new List<string>();
        [System.NonSerialized] readonly List<OutpostHand> foodDrafted = new List<OutpostHand>();

        /// Hands the player sent back from a draft -> game-days left before
        /// `FeedFirst` may draft them again. Not saved: a reload forgets it,
        /// which only means one early re-draft.
        [System.NonSerialized] readonly Dictionary<OutpostHand, float> foodVeto =
            new Dictionary<OutpostHand, float>();
        [System.NonSerialized] readonly List<OutpostHand> foodVetoScratch = new List<OutpostHand>();

        /// Game-days an undone draft keeps a hand off the next one.
        public const float FoodVetoDays = 1f;

        /// **The hands out on the hunger draft right now** (`autoFood`):
        /// hunting or foraging on their own initiative. Rebuilt per call.
        public IReadOnlyList<OutpostHand> FoodDrafted
        {
            get
            {
                foodDrafted.Clear();
                if (hands != null)
                    foreach (var h in hands)
                        if (h != null && h.autoFood && FoodDraftOrder(h)) foodDrafted.Add(h);
                return foodDrafted;
            }
        }

        /// **The player says "not him"**: back to "no job" (system Idle,
        /// NOT the player's reserve, so the idle ladder takes him on), and
        /// `FeedFirst` leaves him alone for `FoodVetoDays`. A beast he was
        /// only stalking is let be; a carcass already on his shoulders, or
        /// an armful of forage, is still carried home (goods count on
        /// arrival, and the hauler pass walks a load in any idle hand's arms).
        public void UndoFoodDraft(OutpostHand h)
        {
            if (h == null || !h.autoFood) return;
            h.autoFood = false;
            if (FoodDraftOrder(h))
            {
                if (h.HuntTrip && !h.huntKilled) ClearHaul(h);
                h.order = OutpostOrder.Idle;
                h.target = "";
                h.playerIdle = false;
            }
            foodVeto[h] = FoodVetoDays;
        }

        /// A food-draft order: Gather on the hunt or on wild forage.
        static bool FoodDraftOrder(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Gather && (h.target == Res.Game || h.target == Res.Food);

        bool FoodVetoed(OutpostHand h) => h != null && foodVeto.ContainsKey(h);

        /// One `Step` quantum off every veto; spent ones go.
        void AgeFoodVetoes(float days)
        {
            if (foodVeto.Count == 0) return;
            foodVetoScratch.Clear();
            foodVetoScratch.AddRange(foodVeto.Keys);
            foreach (var h in foodVetoScratch)
            {
                float left = foodVeto[h] - days;
                if (left <= 0f || hands == null || !hands.Contains(h)) foodVeto.Remove(h);
                else foodVeto[h] = left;
            }
        }

        /// **Could a forage trip start right now** -- `StartGatherTrip`'s
        /// own arithmetic for `Res.Food`: something standing nobody is
        /// already walking to pick, and room in the store for it.
        bool ForageCanStart() =>
            Res.IsGatherable(Res.Food) && FieldFree(Res.Food) >= 1 && RoomFor(Res.Food) >= 1;

        void AnnounceFoodDraft(bool hunting)
        {
            if (draftNames.Count == 0) return;
            string who;
            int n = draftNames.Count;
            if (n == 1) who = draftNames[0];
            else if (n == 2) who = draftNames[0] + " and " + draftNames[1];
            else who = string.Join(", ", draftNames.GetRange(0, n - 1)) + " and " + draftNames[n - 1];
            string msg = "Food low — " + who + " sent " + (hunting ? "hunting" : "foraging");
            try { FoodDraftNotice?.Invoke(msg); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }
}
