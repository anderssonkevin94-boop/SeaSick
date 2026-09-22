using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Where you are, and what day it is.** Two lines in the top-left
    /// corner, under the minimap, and nothing else.
    ///
    /// It is not a panel: no box, no border, just the island's name in the
    /// storybook serif over a line of small caps, dropped straight onto the
    /// world with a shadow to keep it legible over sand. The redesign's rule
    /// is that nothing floats over the world — a name is the one exception,
    /// because it is a caption on the place rather than a control over it.
    ///
    /// It does NOT rebuild the minimap. The minimap is IMGUI and stays IMGUI;
    /// this sits beneath whatever it draws.
    public class PlaceLabel
    {
        readonly VisualElement box;
        readonly Label name;
        readonly Label sub;

        string lastName;
        int lastDay = -1;
        string lastState;
        float nextEval;

        public PlaceLabel(VisualElement root)
        {
            box = new VisualElement();
            box.AddToClassList(SheetTheme.Place);
            box.pickingMode = PickingMode.Ignore;
            box.style.display = DisplayStyle.None;
            root.Add(box);

            name = new Label("");
            name.AddToClassList(SheetTheme.PlaceName);
            name.pickingMode = PickingMode.Ignore;
            box.Add(name);

            sub = new Label("");
            sub.AddToClassList(SheetTheme.PlaceSub);
            sub.pickingMode = PickingMode.Ignore;
            box.Add(sub);
        }

        public void Tick(bool on, VisualElement root)
        {
            if (!on) { box.style.display = DisplayStyle.None; return; }
            box.style.display = DisplayStyle.Flex;

            // Clear of the minimap: the map is a disc about 170 px across at
            // the reference width, and this is the caption beside it.
            float scale = root.resolvedStyle.width / Mathf.Max(1f, Screen.width);
            var safe = Screen.safeArea;
            // Clear of the chart instrument, which is 210 px of dial in the
            // same corner: 14 (its own margin) + 210 + 16 of air.
            box.style.left = safe.xMin * scale + 240f;
            box.style.top = (Screen.height - safe.yMax) * scale + 18f;

            if (Time.unscaledTime < nextEval) return;
            nextEval = Time.unscaledTime + 0.25f;

            var anchor = Sheets.Anchor;
            string place = Pretty(anchor != null && anchor.CurrentIsland != null
                ? anchor.CurrentIsland.name : "");
            string state = StateWord(anchor);
            int day = TimeOfDay.Day;

            // Keyed, because a `Label.text` assignment rebuilds a text mesh
            // whether or not the string changed — the same trap `HudLabel`
            // exists for on the IMGUI side.
            if (place != lastName) { lastName = place; name.text = place; }
            if (day != lastDay || state != lastState)
            {
                lastDay = day; lastState = state;
                sub.text = (state + " · day " + day).ToUpperInvariant();
            }
        }

        static string StateWord(AnchorController a)
        {
            if (a == null) return "at anchor";
            if (a.CurrentDock != null) return "alongside";
            return a.CurrentState == AnchorController.State.Ashore ? "ashore" : "at anchor";
        }

        /// Islands have no authored names yet — the world names its objects
        /// `Island_Home`, `Island_2`. Until there is a name table this makes
        /// the GameObject's name readable rather than inventing one, because
        /// an invented name would not survive being given a real one.
        ///
        /// **It keeps the word "Island".** The first version stripped it, so
        /// `Island_2` came out as a bare "2" sitting on its own in a 30 px
        /// serif — which reads as a number somebody left on the screen rather
        /// than as the name of a place.
        static string Pretty(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            raw = raw.Replace('_', ' ').Trim();
            while (raw.Contains("  ")) raw = raw.Replace("  ", " ");
            if (raw.Length == 0) return "The Island";
            return char.ToUpperInvariant(raw[0]) + raw.Substring(1);
        }
    }
}
