using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// The sheet HUD's palette and its USS class names, in one place.
    ///
    /// Kevin will retune every one of these colours, so they are named for
    /// what they MEAN (ember, moss, timber) rather than for what they look
    /// like — a rename is then a value change and nothing else.
    ///
    /// The class-name constants exist for the same reason: the content agent
    /// composes cards out of `SheetKit`, and every string it would otherwise
    /// hand-type is a string that can drift out of step with `Sheets.uss`.
    public static class SheetTheme
    {
        static Color Hex(string rrggbb, float a = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + rrggbb, out var c);
            c.a = a;
            return c;
        }

        // --- the paper and the ink ---
        public static readonly Color Paper = Hex("F4E8CF");
        public static readonly Color Ink = Hex("1F2D38");
        /// Ink at 62 % — the second line on a row, a unit, a "of 30".
        public static readonly Color InkDim = new Color(31f / 255f, 45f / 255f, 56f / 255f, 0.62f);

        // --- the accents ---
        public static readonly Color Brass = Hex("C99339");
        public static readonly Color BrassBorder = Hex("8A6120");
        public static readonly Color Ember = Hex("E4623A");
        public static readonly Color Moss = Hex("5E9A48");
        public static readonly Color Sea = Hex("2E7EA3");
        public static readonly Color Timber = Hex("A8672F");
        public static readonly Color Stone = Hex("7C8894");

        // --- USS class names ---
        public const string Sheet = "sheet";
        public const string Title = "sheet-title";
        public const string Eyebrow = "sheet-eyebrow";
        public const string Rule = "sheet-rule";
        public const string Btn = "sheet-btn";
        public const string BtnPrimary = "sheet-btn--primary";
        public const string BtnQuiet = "sheet-btn--quiet";
        public const string Token = "sheet-token";
        public const string TokenAngry = "sheet-token--angry";
        public const string TokenContent = "sheet-token--content";
        public const string Store = "sheet-store";
        public const string Bar = "sheet-bar";
        public const string BarFill = "sheet-bar-fill";
        public const string Row = "sheet-row";
        public const string Col = "sheet-col";
        public const string Muted = "sheet-muted";

        // --- names the host and the kit use internally ---
        public const string Card = "sheet-card";
        public const string Body = "sheet-body";
        public const string Head = "sheet-head";
        public const string Badge = "sheet-badge";
        public const string Close = "sheet-close";
        public const string Tail = "sheet-tail";
        public const string Note = "sheet-note";
        public const string Seg = "sheet-seg";
        public const string SegBtn = "sheet-seg-btn";
        public const string SegOn = "sheet-seg-btn--on";
        public const string Rail = "sheet-rail";
        public const string Place = "sheet-place";
        public const string PlaceName = "sheet-place-name";
        public const string PlaceSub = "sheet-place-sub";
        public const string TokenJob = "sheet-token-job";
        public const string TokenLetter = "sheet-token-letter";
        public const string Docked = "sheet-card--docked";

        // --- the standard frame (2026-09-22, "one sheet, tabs") ---
        public const string Frame = "sheet-frame";
        public const string FrameHead = "sheet-frame-head";
        public const string Tabs = "sheet-tabs";
        public const string Tab = "sheet-tab";
        public const string TabOn = "sheet-tab--on";
        public const string TabMark = "sheet-tab-mark";
        public const string TabsCompact = "sheet-tabs--compact";
        public const string TabArrow = "sheet-tab-arrow";
        public const string TabNow = "sheet-tab-now";
        public const string Dots = "sheet-dots";
        public const string Dot = "sheet-dot";
        public const string ListRow = "sheet-list-row";
        public const string Actions = "sheet-actions";
        public const string Chip = "sheet-chip";
        public const string ChipStripe = "sheet-chip-stripe";
        public const string ChipValue = "sheet-chip-value";
    }
}
