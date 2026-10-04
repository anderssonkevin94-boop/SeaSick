using System;

namespace SeaSick.UI.ModularYard
{
    /// **The shipyard slot screen's view model -- plain data, no backend.**
    ///
    /// Every view in `ModularYard/Slots/` is fed from these structs and talks
    /// back only through callbacks (`Action<string>` and friends), so the
    /// screen can be built, captured and tuned before the Slots API lands.
    /// An adapter (written later, against docs/SHIPYARD-API.md) fills a
    /// `YardVm` from the live draft and turns the callbacks into draft edits.
    ///
    /// Mockup: scratchpad shipyard/index.html, frames 1-6 (Kevin approved
    /// 2026-09-27). Ids are opaque strings the adapter chooses; the views
    /// only hand them back.
    [Serializable]
    public struct YardVm
    {
        public YardSectionVm[] sections;     // stern first, bow last
        public YardPlusChipVm[] plusChips;   // null/empty = no '+' chips on the strip
        public string stripNote;             // small green line across the top of the strip, or null
        public YardSectionCardVm card;       // the selected section
        public YardTotalsVm totals;
        public YardBlockerVm[] blockers;     // shown above the pinned row, first one only on the phone
        public YardActionsVm actions;
        public YardDrawerVm drawer;          // drawer.open false = hidden
    }

    public enum YardSectionKind { Stern, Mid, Bow, New }

    [Serializable]
    public struct YardSectionVm
    {
        public string key;
        public string label;          // "Stern", "Mid 1", "New"
        public YardSectionKind kind;
        public int decksUsed;         // 1 = hold only, 2 = hold + deck, 3 = + upper, 4 = + top
        public int decksMax;
        public bool selected;
        public bool canRemove;
        public bool raisePreview;     // dashed "+ Upper" box drawn above her top deck
        public string raiseLabel;     // "+ Upper" / "+ Top"
    }

    [Serializable]
    public struct YardPlusChipVm
    {
        public int index;             // insert BEFORE sections[index] (1..sections.Length-1)
        public bool locked;
        public string lockReason;
    }

    // ------------------------------------------------------------------
    // The section card
    // ------------------------------------------------------------------

    [Serializable]
    public struct YardSectionCardVm
    {
        public string sectionKey;
        public string title;          // "Mid 1"
        public string sub;            // "3 of 4 decks"
        public string hint;           // replaces `sub` while dragging ("hover a deck tab to switch"), or null
        public int guns, bunks, crates;
        public YardDeckTabVm[] deckTabs;
        public YardDeckVm deck;       // the live tab's plan
    }

    public enum YardDeckTabKind { Deck, Raise, Locked }

    [Serializable]
    public struct YardDeckTabVm
    {
        public string id;             // handed back by onDeckTab
        public string label;          // "Hold", "Deck", "+ Top", "Top"
        public YardDeckTabKind kind;
        public int used, cap;         // "5/6" under the label (Deck kind only)
        public string note;           // replaces used/cap when set ("no guns", "1 port free")
        public bool selected;
        public bool hot;              // highlighted (raise tab under consideration, or a drag target)
        public string lockReason;     // "Dry dock IV"
    }

    public enum YardRow { Port, Mid, Stbd }

    [Serializable]
    public struct YardDeckVm
    {
        public string name;           // "Deck"
        public YardCellVm[] cells;    // any order; laid out by row then col
        public string sternLabel, bowLabel;   // null = "‹ STERN" / "BOW ›"
    }

    [Serializable]
    public struct YardCellVm
    {
        public string cellId;
        public YardRow row;
        public int col;               // 0 = sternmost
        public bool isGunPort;
        public bool hasModule;        // struct field can't be null; false = empty cell
        public YardModuleVm module;
        public string lockedReason;   // non-null = dimmed, can't take anything ("not a port")
        public bool isNew;            // amber "NEW" tag
        public bool selected;         // the slot the drawer is filling
    }

    [Serializable]
    public struct YardModuleVm
    {
        public string id;
        public string label;          // "Cannon"
        public string sub;            // "1 hand"
        public string iconKey;        // "cannon", "bunk", "crate", "pump", "lookout", "bench" (or an ItemIconSet id)
    }

    // ------------------------------------------------------------------
    // Totals, blockers, the pinned row
    // ------------------------------------------------------------------

    public enum YardTone { Plain, Good, Warn, Bad }

    [Serializable]
    public struct YardTotalsVm
    {
        public int guns, gunPorts;
        public int crew, berths;
        public int cargo, cargoCap;
        public float draft;           // metres
        public YardTone draftTone;    // Good = moss, Warn = amber, Bad = ember
    }

    [Serializable]
    public struct YardBlockerVm
    {
        public string text;           // rich text allowed: "<b>10 crew aboard, 8 beds.</b> Add a bunk..."
        public string fixLabel;       // null = no fix button
        public string fixId;          // handed back by onFix
        public bool warnOnly;         // amber (advice) instead of ember (blocks Confirm)
    }

    [Serializable]
    public struct YardActionsVm
    {
        public string cancelLabel;    // null = no cancel button (the header X is the way out)
        public string confirmLabel;   // "Confirm", "Add section", "Fix 1 problem to confirm"
        public bool confirmEnabled;
    }

    // ------------------------------------------------------------------
    // The drawer
    // ------------------------------------------------------------------

    [Serializable]
    public struct YardDrawerVm
    {
        public bool open;
        public string title;          // "Add to this slot"
        public string sub;            // "Mid 1 · Deck · port gun port"
        public YardDrawerItemVm[] items;
        public string footnote;       // rich text; "Build costs are <b>free and instant</b>..."
    }

    [Serializable]
    public struct YardDrawerItemVm
    {
        public string moduleId;
        public string label;
        public string stats;          // "needs 1 hand · 500 kg"
        public string iconKey;
        public int inStore;
        public string aboardAt;       // one-per-ship module already fitted: "stern" -> "Move here"
        public bool canPlace;
        public bool lockedByDock;     // dashed "Locked" card; `reason` names the dock level
        public string reason;         // why not ("Not on this deck", "Dry dock IV")
        public bool highlighted;      // ice border -- the suggested pick
        public string buildLabel;     // null = "Build + place · free"
    }
}
