using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.UI
{
    /// A thing with knobs on it, that the settings drawer owns.
    ///
    /// Every tuner in this project used to be its own island: a `[SerializeField]
    /// bool active` you ticked in the Inspector, and an `OnGUI` that drew
    /// wherever its author felt like. `SeaFoamTuner` took a 440x560 box out of
    /// the top right and sat on the minimap and the ship panel;
    /// `WaterClarityTuner` took 430x470 out of the top left and sat on the crew
    /// pips, as did `IslandTuner` and `DevHUD`; `DockCamTuner` and
    /// `SailCamTuner` both drew a readout at 0.28-0.30 of screen height, on the
    /// Yard and Home tabs and on each other.
    ///
    /// Implementing this interface and registering gives up the right to
    /// choose a place on screen, and gets in exchange: a line in the settings
    /// drawer, a body rect that is guaranteed to be clear, and the guarantee
    /// that no second tuner is open behind you.
    public interface IDevTool
    {
        /// The line in the drawer's list.
        string ToolName { get; }

        /// One short line under the name saying what it is for. Keep it to
        /// what the tool DOES; the controls belong on the tool itself.
        string ToolBlurb { get; }

        /// Open or closed. The drawer is the only thing that sets this — a
        /// tool that opens itself is back to being an island.
        bool ToolActive { get; set; }

        /// Draw into this rect and nowhere else.
        void DrawTool(Rect body);
    }

    /// The list of tools the settings drawer offers.
    ///
    /// Tools add themselves in `OnEnable` and drop out in `OnDisable`, so the
    /// drawer never searches the scene and a tool that is not in the scene
    /// simply is not offered. Registration order is the order they list in,
    /// which in practice is scene order — stable enough that the entry you
    /// want stays where you last saw it.
    public static class DevTools
    {
        static readonly List<IDevTool> tools = new List<IDevTool>();

        public static IReadOnlyList<IDevTool> All => tools;

        public static void Register(IDevTool t)
        {
            if (t == null || tools.Contains(t)) return;
            tools.Add(t);
        }

        public static void Unregister(IDevTool t)
        {
            if (t == null) return;
            tools.Remove(t);
            if (Open == t) Open = null;
        }

        /// The one tool currently drawing, or null. Assigning closes whatever
        /// was open — two tuners at once is how they ended up on top of each
        /// other in the first place, and it is also how you tune the foam
        /// while the island tuner is quietly rebuilding the terrain under you.
        public static IDevTool Open
        {
            get => open;
            set
            {
                if (open == value) return;
                if (open != null) open.ToolActive = false;
                open = value;
                if (open != null) open.ToolActive = true;
            }
        }
        static IDevTool open;

        /// Close everything. Called when the drawer shuts, so a tuner cannot
        /// keep running behind a closed panel.
        public static void CloseAll()
        {
            Open = null;
            foreach (var t in tools) if (t != null) t.ToolActive = false;
        }
    }
}
