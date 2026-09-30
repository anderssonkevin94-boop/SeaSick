using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The carry line, as a pill (2026-09-30, island UI phase 6).**
    /// With a villager in the Hand, the one line that says what letting go
    /// here would do -- "Bo -- cut timber", "put Bo at the smithy", "back
    /// aboard" -- or why it is refused, with an ember rim and a cross:
    /// "not in the sea". It replaces the IMGUI `GUI.Label` `Hand.OnGUI`
    /// drew into the prompt slot.
    ///
    /// Drawn in the thumb lane, directly above what stacks on the bar
    /// (`ThumbBar.ReservePanel`: the bar, then the Next card), so it is at
    /// the top of the thumb area and the world stays clear -- on a
    /// one-handed portrait screen the thumb is ON the thing being aimed at,
    /// so text beside the cursor cannot be read. Never takes a touch
    /// (`PickingMode.Ignore` all the way down): the drag belongs to
    /// `IslandInput`. House colours: the Next card's slate and ice; ember
    /// (`.carry-pill--no`) for a target that refuses.
    ///
    /// On a desktop the empty-handed hover labels ("build" over the
    /// campfire, "blueprint" over a drawing) come through the same pill
    /// (`Hand.CarryLine`). Owned by `PartyReportToast`, ticked from
    /// `SheetHost.LateUpdate`. Reads only.
    internal sealed class CarryPill
    {
        const float MaxWidth = 460f;

        readonly VisualElement holder;
        readonly VisualElement pill;
        readonly StationPage.Glyph cross;
        readonly Label text;
        bool shown = true;
        bool refusing;

        /// On screen this frame (the gesture hint stands aside for it).
        public bool Shown => shown;

        public CarryPill(VisualElement root)
        {
            holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.AddToClassList("carry-holder");

            pill = new VisualElement { pickingMode = PickingMode.Ignore };
            pill.AddToClassList("carry-pill");

            cross = new StationPage.Glyph("close", new Color32(246, 146, 128, 255), "carry-cross");
            cross.style.display = DisplayStyle.None;
            pill.Add(cross);

            text = new Label { pickingMode = PickingMode.Ignore };
            text.AddToClassList("carry-text");
            pill.Add(text);

            holder.Add(pill);
            root.Add(holder);
            Hide();
        }

        void Hide()
        {
            if (!shown) return;
            shown = false;
            holder.style.display = DisplayStyle.None;
        }

        public void Tick(VisualElement root)
        {
            var hand = Hand.Instance;
            if (hand == null || ThumbBar.PlacementActive
                || !SeaSick.CameraRig.IslandCam.Engaged
                || !hand.CarryLine(out string line, out bool allowed))
            {
                Hide();
                return;
            }

            if (text.text != line) text.text = line;
            if (refusing == allowed)
            {
                refusing = !allowed;
                pill.EnableInClassList("carry-pill--no", refusing);
                cross.style.display = refusing ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!shown)
            {
                shown = true;
                holder.style.display = DisplayStyle.Flex;
                holder.BringToFront();
            }

            ThumbBar.Lane(root, out float left, out float width, out float bottom);
            if (width > MaxWidth) { left += (width - MaxWidth) * 0.5f; width = MaxWidth; }
            holder.style.left = left;
            holder.style.width = width;
            // Above the food-draft notice too, when one is up (2026-09-30:
            // the pill sat across "Food low -- villagers gathering food").
            holder.style.bottom = Mathf.Max(bottom, Mathf.Max(ThumbBar.ReservePanel, CampStatusHud.TopPanel));
        }
    }
}
