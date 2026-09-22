using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The tap that opens a sheet.**
    ///
    /// The sheet HUD has no buttons of its own out in the world: the player
    /// touches the campfire, the hut, the ship or a hand, and that thing's
    /// card unfolds. This is the one place that turns a press on the screen
    /// into that.
    ///
    /// ## What it has to refuse, and why each one bit somewhere else first
    ///
    /// * **A press that started on the HUD.** The panel is tested at the
    ///   press, not the release, because a drag that begins on a card and ends
    ///   over the world is still the card's.
    /// * **A drag.** Twelve pixels. The camera is dragged with the same finger
    ///   that taps, and without this every pan ends by opening whatever the
    ///   finger happened to stop on.
    /// * **A press while something else owns the finger** — siting a building
    ///   (`CampSiting.Placing`) or carrying a villager (`Hand.Holding`). Both
    ///   are modal by design and both end on a tap in the world, which is
    ///   exactly the press this would otherwise also read.
    ///
    /// Nothing here knows what a sheet says. It walks up from the collider and
    /// asks the registry; if the registry has nothing, and a sheet is open,
    /// the tap was on the ground and the sheet folds away.
    public class WorldPicker : MonoBehaviour
    {
        /// A press further than this from where it started is a drag on the
        /// camera, not a tap on a thing.
        public const float DragSlop = 12f;

        Vector2 pressAt;
        bool armed;

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;

            if (pointer.press.wasPressedThisFrame)
            {
                pressAt = pointer.position.ReadValue();
                armed = !OverUI(pressAt) && !Busy();
                return;
            }

            if (!pointer.press.wasReleasedThisFrame) return;
            if (!armed) return;
            armed = false;

            var at = pointer.position.ReadValue();
            if ((at - pressAt).sqrMagnitude > DragSlop * DragSlop) return;
            // Re-checked at the release: a villager picked up DURING the press
            // is dropped by this same release, and that drop is not a tap.
            if (Busy() || OverUI(at)) return;

            Tap(at);
        }

        static bool Busy()
        {
            if (CampSiting.Placing) return true;
            var hand = Hand.Instance;
            return hand != null && hand.Holding;
        }

        /// Is the finger on the sheet HUD's own panel? `Pick` returns the
        /// element under a panel-space point, and anything but null means the
        /// press belongs to the card. The root itself is `PickingMode.Ignore`,
        /// so the empty screen around a card is not the HUD.
        static bool OverUI(Vector2 screen)
        {
            var host = SheetHost.Instance;
            var panel = host != null ? host.Panel : null;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(p) != null;
        }

        void Tap(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Triggers included on purpose: `Pickable` hangs a trigger sphere
            // on anything tappable that has no collider of its own.
            if (Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 6000f,
                                ~0, QueryTriggerInteraction.Collide))
            {
                var sheet = Sheets.TryCreateFor(hit.collider);
                if (sheet != null) { Sheets.Open(sheet); return; }
            }

            // Ground, water, or nothing at all: whatever was open is done.
            if (Sheets.IsOpen) Sheets.Close();
        }
    }
}
