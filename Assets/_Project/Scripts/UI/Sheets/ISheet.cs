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

    /// **The standard frame: header, tabs, scrolling body, action row.**
    ///
    /// Kevin, 2026-09-22, after three mockups on the phone: *option A — one
    /// sheet, tabs*. Every sheet occupies the SAME fixed region of the screen
    /// and is divided the same way, so the card never grows, never jumps, and
    /// the thumb learns one geometry rather than five.
    ///
    /// A sheet that implements this hands the host four separate pieces
    /// instead of one tree:
    ///
    /// * `BuildHeader()` — the badge, the eyebrow, the title and the X. Built
    ///   once and pinned at the top of the frame, so it does not scroll away.
    /// * `TabLabels` — short, sentence-case, e.g. `camp`, `hands · 3`. Null,
    ///   empty or a single label means the sheet has one section and NO tab
    ///   strip is drawn: a strip with one tab is a label pretending to be a
    ///   control.
    /// * `ISheet.Build()` — the body of the CURRENT tab. It is called again
    ///   on every tab change, so a framed sheet's `Build` must be safe to
    ///   call repeatedly and must re-bind whatever `Refresh` mutates.
    /// * `BuildActions()` — the row pinned at the bottom of the frame for the
    ///   current tab, or null for a tab with no standing action.
    ///
    /// `Tab` starts at -1, meaning "no preference": the host then restores
    /// the last tab this sheet TYPE was left on this session, so reopening
    /// the fire lands where you were. A sheet opened at a particular tab (the
    /// watchtower asking about the watch) sets `Tab` in its constructor and
    /// the host leaves it alone.
    public interface ISheetFramed : ISheet
    {
        VisualElement BuildHeader();

        string[] TabLabels { get; }

        /// The live tab, or -1 before the host has chosen one.
        int Tab { get; }

        /// Called by the host only — on restore and on a tab press.
        void SetTab(int index);

        /// The colour the live tab is marked in: the sheet's own accent, so
        /// the fire's tabs are ember and the ship's are sea.
        Color Accent { get; }

        VisualElement BuildActions();

        /// **Opt-in, 2026-09-26.** True asks the host to give this sheet the
        /// full band between the top resource bar and the bottom nav instead
        /// of the standard third-of-screen card -- the Stores bank needs
        /// several rows of tiles plus a detail card, which the standard band
        /// only ever shows one row of (Kevin's mockup verdict: "give me
        /// exactly that"). Default false so every other framed sheet is
        /// unaffected; only `SheetHost.FrameSizeScreen` reads it, and only on
        /// the phone shape -- a desk sheet is already a near-full-height
        /// column.
        bool WantsTallSheet => false;
    }
}
