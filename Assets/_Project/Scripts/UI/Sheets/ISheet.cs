using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **One object, one sheet, one decision.**
    ///
    /// A sheet is the parchment card that unfolds beside a thing in the world
    /// once the player taps it. Nothing is drawn over the world: the object
    /// itself is the button, and this is what it has to say.
    ///
    /// The contract is deliberately tiny, because the whole point of the
    /// redesign is that a sheet cannot grow into a menu of everything. It
    /// builds one card, it is asked to refresh that card four times a second,
    /// it says where in the world it belongs, and it says when its subject has
    /// stopped existing so the host can fold it away without being told.
    public interface ISheet
    {
        /// The heavy-serif line at the top of the card.
        string Title { get; }

        /// Build the card's body ONCE. The host owns the frame, the shadow and
        /// the placement; this is everything inside it.
        VisualElement Build();

        /// Called 4x/s while the sheet is open. Update the elements built
        /// above in place — never rebuild, or the card flickers and any
        /// scroll position is thrown away.
        void Refresh();

        /// The world point the card is anchored beside, and where the
        /// selection ring is drawn. Read every frame, so a sheet on something
        /// that walks (a hand) follows it.
        Vector3 AnchorWorld { get; }

        /// False the moment the subject is gone — the building finished, the
        /// hand went aboard, the ship weighed anchor. The host closes on it.
        bool StillValid { get; }
    }
}
