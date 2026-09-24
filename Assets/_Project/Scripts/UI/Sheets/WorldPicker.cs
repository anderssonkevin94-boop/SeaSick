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
            if (SeaSick.Ship.SailingPilot.OwnsWorldInput)
            {
                armed = false;
                return;
            }
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

            if (!TrySelect(at) && Sheets.IsOpen) Sheets.Close();
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

        public static bool TrySelect(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return false;

            // **The crew are asked FIRST, and they are asked in screen space.**
            //
            // Kevin, on the phone, 2026-09-22: *"when pressing on the villager
            // his information / commands I can give him should be the 1/3
            // screen UI"* -- and tapping one did nothing, while tapping his
            // token in the crew list opened the sheet. The reason is that a
            // villager has NO COLLIDER: `Hand.PickAt` is deliberately
            // projection rather than physics ("giving twenty of them one so
            // that a cursor can be aimed would be paying the physics engine
            // for an interface", Hand.cs:228). So the raycast below could
            // never hit a hand -- it hit the beach he was standing on, found
            // no sheet for the terrain, and closed whatever was open.
            //
            // Asking `Hand` is also the only way the two agree: the cursor
            // that highlights a villager and the tap that opens his sheet now
            // resolve the same man, with the same generous follow radius
            // (`Feel.FollowRadius01` of the screen height, which is the
            // thumb-sized target a 1.8 m body at island zoom does not have).
            var hand = Hand.Instance;
            var who = hand != null ? hand.PickAt(screen, forPickup: false) : null;
            if (who != null)
            {
                var hers = Sheets.TryCreateFor(who);
                if (hers != null) { Sheets.Open(hers); return true; }
            }

            // Triggers included on purpose: `Pickable` hangs a trigger sphere
            // on anything tappable that has no collider of its own.
            //
            // **Every hit, nearest first -- not just the first, 2026-09-23.**
            // Kevin, on the phone: *"nothing happens when I press on the
            // forge."* Every scenery tree carries a ~15 m trigger box
            // (`Broad*`/`Spruce` under Scenery), and a single `Raycast` that
            // includes triggers returned the tree standing in front of the
            // forge -- no sheet for a tree, so the tap was read as ground.
            // Measured at a camp in the woods: 4 of 36 island-cam rays at
            // the forge reached it. A trigger with no sheet is an invisible
            // volume, so the ray goes on through it; a SOLID collider with no
            // sheet (the ground, a rock) is something the eye sees in front,
            // and the tap stops there.
            var ray = cam.ScreenPointToRay(screen);
            int n = Physics.RaycastNonAlloc(ray, Hits, 6000f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(Hits, 0, n, NearestFirst.Instance);
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                var sheet = Sheets.TryCreateFor(col);
                if (sheet != null) { Sheets.Open(sheet); return true; }
                if (!col.isTrigger) break;
            }

            // Ground, water, or nothing at all: whatever was open is done.
            return false;
        }

        /// Room for a ray through a wood: every tree's trigger box is a hit.
        static readonly RaycastHit[] Hits = new RaycastHit[64];

        sealed class NearestFirst : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly NearestFirst Instance = new NearestFirst();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
