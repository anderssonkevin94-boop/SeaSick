using System;
using System.Collections.Generic;
using SeaSick.UI.Sheets;
using UnityEngine.UIElements;

namespace SeaSick.UI.Menus
{
    /// **The list of saves.** One builder, two callers: the Home screen's
    /// "Load" (five manual slots + three autosaves, tap to load, per-row
    /// delete/rename on a manual slot) and the Pause menu's "Save" / "Save &
    /// exit" (the five manual slots only, tap to save into one). Same rows,
    /// same formatting -- the difference is entirely in what a tap on a row
    /// does, which is why this takes the mode and two callbacks rather than
    /// being two files that would drift apart.
    internal static class SlotListScreen
    {
        public enum Mode { Load, SaveTarget }

        /// `pickLoad(slotId)` -- the row was confirmed for loading.
        /// `pickSave(slotId, existingNameOrNull)` -- the row was tapped to
        /// save into; the caller prompts for a name when it is null (empty
        /// slot) or confirms overwrite when it is not.
        public static VisualElement Build(Mode mode, VisualElement dialogHost,
                                          Action back, Action<string> pickLoad,
                                          Action<string, string> pickSave)
        {
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;

            var headRow = new VisualElement(); headRow.AddToClassList(SheetTheme.Row);
            headRow.Add(MenuKit.Back(back));
            col.Add(headRow);

            col.Add(MenuKit.Title(mode == Mode.Load ? "LOAD" : "SAVE"));
            col.Add(MenuKit.Subtitle(mode == Mode.Load
                ? "tap a save to load it"
                : "tap a slot to save there"));

            // A plain column, not a `ScrollView` -- this project's sheets
            // gave up scrolling on purpose (`Sheets.uss`: "there is no
            // scroll view in the frame any more") and paginate instead. The
            // list here never needs to: it is exactly 5 manual + 3 autosave
            // rows, always, which fits the card with room to spare at the
            // phone reference resolution this panel renders at.
            var list = new VisualElement();
            list.AddToClassList("menu-scroll");
            col.Add(list);

            void Rebuild()
            {
                list.Clear();
                List<SeaSick.Save.SaveSlotInfo> all;
                try { all = SaveSlotsAdapter.List(); }
                catch { all = new List<SeaSick.Save.SaveSlotInfo>(); }

                foreach (var info in all)
                {
                    if (info == null) continue;
                    if (mode == Mode.SaveTarget && info.isAuto) continue;   // never save over an autosave
                    list.Add(Row(info, mode, dialogHost, Rebuild, pickLoad, pickSave));
                }
            }
            Rebuild();

            return col;
        }

        static VisualElement Row(SeaSick.Save.SaveSlotInfo info, Mode mode, VisualElement dialogHost,
                                 Action refresh, Action<string> pickLoad, Action<string, string> pickSave)
        {
            // Only a real Load-mode row (not an autosave, not empty) carries
            // `Rename`/`Delete` in the row itself -- see the wrap-clipping
            // note below, where this decides both the row's CSS class and
            // how much of the subtitle it dares show on one line.
            bool hasRowActions = mode == Mode.Load && !info.isAuto && info.exists;

            var row = new VisualElement();
            row.AddToClassList("menu-slot-row");
            if (!info.exists) row.AddToClassList("menu-slot-row--empty");
            if (hasRowActions) row.AddToClassList("menu-slot-row--with-actions");

            var main = new VisualElement(); main.AddToClassList("menu-slot-main");
            row.Add(main);

            string name = info.exists ? (string.IsNullOrEmpty(info.displayName) ? "(unnamed)" : info.displayName)
                                       : "Empty slot";
            var nameRow = new VisualElement(); nameRow.AddToClassList(SheetTheme.Row);
            var nameLbl = new Label(name); nameLbl.AddToClassList("menu-slot-name");
            nameRow.Add(nameLbl);
            if (info.isAuto)
            {
                var tag = new Label("AUTOSAVE"); tag.AddToClassList("menu-slot-tag");
                nameRow.Add(tag);
            }
            main.Add(nameRow);

            // `Rename`/`Delete` (below) sit in the SAME row and narrow this
            // column enough that the full three-part line wraps to a second
            // line the row's own height does not account for -- the wrapped
            // remainder draws past the row's border, into the row below it
            // (2026-09-26 review, reproduced back to the coordinate-only
            // text this replaced, so it was never about the island name
            // being long). Rather than chase the Yoga/UI-Toolkit measure
            // order that under-sizes the row for wrapped text, the Load
            // row with buttons drops the play-time third: location + date
            // is enough to tell one save from another, and the full line
            // (still) shows on the Save-target list, which never carries
            // these buttons.
            string sub = info.exists
                ? (hasRowActions
                    ? $"{info.location}  ·  {SaveSlotsAdapter.FormatWhen(info.savedAtUtc)}"
                    : $"{info.location}  ·  {SaveSlotsAdapter.FormatWhen(info.savedAtUtc)}  ·  {SaveSlotsAdapter.FormatPlayTime(info.playSeconds)}")
                : (mode == Mode.SaveTarget ? "tap to save here" : "nothing saved yet");
            var subLbl = new Label(sub); subLbl.AddToClassList("menu-slot-sub");
            main.Add(subLbl);

            var actions = new VisualElement(); actions.AddToClassList("menu-slot-actions");

            if (mode == Mode.Load && !info.isAuto && info.exists)
            {
                var rename = SheetKit.Btn("Rename", () =>
                {
                    MenuKit.Prompt("Rename this save.", info.displayName, "Rename", newName =>
                    {
                        if (!SaveSlotsAdapter.Rename(info.id, newName, out string err))
                            MenuKit.Toast(dialogHost, string.IsNullOrEmpty(err) ? "could not rename" : err);
                        refresh();
                    }, null, dialogHost);
                }, quiet: true);
                actions.Add(rename);

                var del = SheetKit.Btn("Delete", () =>
                {
                    MenuKit.Confirm($"Delete '{name}'? This cannot be undone.", "Delete", () =>
                    {
                        if (!SaveSlotsAdapter.Delete(info.id, out string err))
                            MenuKit.Toast(dialogHost, string.IsNullOrEmpty(err) ? "could not delete" : err);
                        refresh();
                    }, null, dialogHost);
                }, quiet: true);
                actions.Add(del);
            }
            row.Add(actions);

            // Registered on `main`, not `row` -- `actions` (Delete/Rename)
            // sits beside it as a sibling, so a tap on one of THOSE buttons
            // never bubbles into this and fires the load/save confirm too.
            bool tappable = mode == Mode.Load ? info.exists : true;
            if (tappable)
            {
                main.RegisterCallback<ClickEvent>(_ =>
                {
                    if (mode == Mode.Load)
                    {
                        MenuKit.Confirm($"Load '{name}'?", "Load", () => pickLoad(info.id), null, dialogHost);
                    }
                    else if (!info.exists)
                    {
                        MenuKit.Prompt("Name this save.", SaveSlotsAdapter.DefaultSaveName(), "Save",
                            chosen => pickSave(info.id, chosen), null, dialogHost);
                    }
                    else
                    {
                        MenuKit.Confirm($"Overwrite '{name}'?", "Overwrite",
                            () => pickSave(info.id, info.displayName), null, dialogHost);
                    }
                });
                main.AddToClassList("sheet-clickable");
                main.pickingMode = PickingMode.Position;
            }

            return row;
        }
    }
}
