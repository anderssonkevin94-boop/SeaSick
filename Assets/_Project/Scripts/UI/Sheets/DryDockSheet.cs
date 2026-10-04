using SeaSick.Ship.Modular;
using SeaSick.UI.ModularYard;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **What tapping a built dry dock says when the shipyard cannot open
    /// straight away.**
    ///
    /// Kevin, 2026-09-26: *"pressing on the building should be the same as
    /// pressing the dry dock button."* When a refit is possible the tap never
    /// gets this far -- `SheetBootstrap`'s `DryDockSlip` factory opens
    /// `ShipyardLiveBridge` itself, as a side effect, and answers with no
    /// sheet at all, exactly as if the player had pressed the Manifest
    /// sheet's own Shipyard button. This card is only what appears the other
    /// half of the time: the same blockers `ShipSheet.ShipyardBlock` prints
    /// under that button, unfolded beside the building the finger actually
    /// landed on instead of a sheet about the ship.
    ///
    /// One section, no tabs -- a blocked refit is one fact and one door back
    /// into it once the blocker clears, not a menu.
    public class DryDockSheet : ISheetFramed
    {
        readonly DryDockSlip slip;

        public DryDockSheet(DryDockSlip s) { slip = s; }

        public string Title => "Dry dock";
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Sea;

        public VisualElement BuildHeader() =>
            SheetKit.Header("shipyard", Title, SheetTheme.Sea, "anchor", () => Sheets.Close());

        public Vector3 AnchorWorld => slip != null ? slip.ShipCenter : Vector3.zero;

        /// The building itself must still be standing; `ShipyardService`
        /// coming and going with the player's ship is `Refresh`'s problem,
        /// not a reason to fold the card away out from under a still-valid
        /// tap on a still-standing dry dock.
        public bool StillValid => slip != null;

        Label note;
        Button openBtn;
        Label reason;

        public VisualElement Build()
        {
            note = null; openBtn = null; reason = null;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            note = SheetKit.Text("", false, true, 12f);
            note.style.whiteSpace = WhiteSpace.Normal;
            root.Add(note);
            Refresh();
            return root;
        }

        public VisualElement BuildActions()
        {
            openBtn = SheetKit.Btn("Open shipyard", OpenPressed, true);
            reason = SheetKit.Text("", false, true, 11f);
            reason.style.whiteSpace = WhiteSpace.Normal;
            reason.style.marginBottom = 5f;
            reason.style.display = DisplayStyle.None;

            var col = new VisualElement();
            col.AddToClassList(SheetTheme.Actions);
            col.style.flexDirection = FlexDirection.Column;
            col.style.alignItems = Align.Stretch;
            col.style.flexWrap = Wrap.NoWrap;
            col.Add(reason);
            col.Add(openBtn);
            Refresh();
            return col;
        }

        /// Refreshed 4x/s so a blocker that clears while the card is open --
        /// she finishes drifting to rest, a save finishes loading -- turns
        /// the button live without the player having to close and retap the
        /// dock, same as `ShipSheet.ShipyardBlock`.
        public void Refresh()
        {
            var yard = ShipyardService.Player;
            if (note != null)
                note.text = "Refit the ship berthed here.";
            if (openBtn == null) return;
            if (yard == null)
            {
                openBtn.SetEnabled(false);
                if (reason != null)
                {
                    reason.text = "There is no ship to refit.";
                    reason.style.display = DisplayStyle.Flex;
                }
                return;
            }
            var blockers = yard.RefitBlockers();
            openBtn.SetEnabled(blockers.Count == 0);
            if (reason == null) return;
            reason.text = blockers.Count == 0 ? "" : string.Join("\n", blockers);
            reason.style.display = blockers.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void OpenPressed() => ShipyardLiveBridge.Open();
    }
}
