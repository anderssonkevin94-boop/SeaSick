using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Menus
{
    /// **Resume / Save / Save &amp; exit / Settings / Exit.**
    ///
    /// Opened by the pause chip (a thumb-reachable corner of the rail) or by
    /// Esc on a desk. `GameMenus` has already frozen time and blocked world
    /// input (`ShipyardSession.WorldInputBlocked`, the same switch the
    /// shipyard modal uses) before this builds -- this file only draws the
    /// choices and calls back into it.
    internal static class PauseMenu
    {
        public static VisualElement Build(VisualElement dialogHost, Action onResume,
                                          Action onSettings, Action onExitToHome)
        {
            var root = MenuKit.Root();
            var card = MenuKit.Card();
            root.Add(card);

            card.Add(MenuKit.Title("PAUSED"));

            card.Add(MenuKit.Btn("RESUME", onResume, primary: true));

            card.Add(MenuKit.Btn("SAVE", () => GameMenus.ShowSaveTarget(saveAndExit: false)));

            card.Add(MenuKit.Btn("SAVE & EXIT", () => GameMenus.ShowSaveTarget(saveAndExit: true)));

            card.Add(MenuKit.Btn("SETTINGS", onSettings));

            card.Add(MenuKit.Btn("EXIT", () =>
            {
                float since = 999f;
                try { since = SaveSlotsAdapter.SecondsSinceLastSave; } catch { since = 999f; }
                if (since > 60f)
                {
                    int mins = Mathf.Max(1, Mathf.RoundToInt(since / 60f));
                    MenuKit.Confirm($"Last saved {mins} min ago. Exit to the home screen anyway?",
                        "Exit", onExitToHome, null, dialogHost);
                }
                else onExitToHome();
            }));

            return root;
        }
    }
}
