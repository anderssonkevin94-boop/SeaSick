using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Menus
{
    /// **CONTINUE / NEW VOYAGE / LOAD / SETTINGS**, and Quit on a desk.
    ///
    /// Drawn over the boot overlay's frozen world (`GameBoot` sets
    /// `Time.timeScale = 0` before calling `GameMenus.ShowHome`), so the
    /// buttons here never touch a half-built world directly -- they only ever
    /// ask `SaveSlots` to remember what was chosen and then reload the scene,
    /// which is what `GameBoot.Awake` reads back on the way up.
    internal static class HomeScreen
    {
        public static VisualElement Build(VisualElement dialogHost, Action onSettings)
        {
            var root = MenuKit.Root();
            var card = MenuKit.Card();
            root.Add(card);

            card.Add(MenuKit.Title("SEASICK"));

            SeaSick.Save.SaveSlotInfo mostRecent = null;
            try { mostRecent = SaveSlotsAdapter.MostRecent(); } catch { mostRecent = null; }

            if (mostRecent != null)
                card.Add(MenuKit.Subtitle(mostRecent.displayName + "  ·  " +
                    SaveSlotsAdapter.FormatWhen(mostRecent.savedAtUtc)));
            else
                card.Add(MenuKit.Subtitle("no voyages yet"));

            card.Add(MenuKit.Btn("CONTINUE", () =>
            {
                if (mostRecent == null) return;
                GameMenus.LoadSlotAndReload(mostRecent.id);
            }, primary: true, enabled: mostRecent != null));

            card.Add(MenuKit.Btn("NEW VOYAGE", () =>
            {
                MenuKit.Confirm("Start a new voyage? Your saves stay.", "New voyage",
                    GameMenus.NewGameAndReload, null, dialogHost);
            }));

            card.Add(MenuKit.Btn("LOAD", () => GameMenus.ShowLoad()));

            card.Add(MenuKit.Btn("SETTINGS", onSettings));

            if (!Application.isMobilePlatform)
                card.Add(MenuKit.Btn("QUIT", () =>
                {
                    MenuKit.Confirm("Quit SeaSick?", "Quit", Application.Quit, null, dialogHost);
                }));

            return root;
        }
    }
}
