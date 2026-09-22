using System.Text;
using SeaSick.Crew;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Everyone ashore, as four discs in the top corner.**
    ///
    /// The one piece of the sheet HUD that is not attached to an object,
    /// because the thing it is about — "who is on this island and is anyone
    /// unhappy" — has no single object to hang off. It earns the corner by
    /// being answerable at a glance and by carrying no words: a ring is ember
    /// or it is moss, and a badge says what the hand is doing.
    ///
    /// Tapping a token opens that hand's own sheet, which is the same sheet a
    /// tap on the villager in the world opens. The rail is a shortcut to the
    /// world, never a second interface onto it.
    ///
    /// A hand that exists only as a ledger row — recruited this morning, no
    /// body raised for them yet — is shown and does nothing when tapped.
    /// Hiding them would make the count on the rail disagree with the count on
    /// the camp sheet, and a number that disagrees with another number is
    /// worse than a token that does not answer.
    public class AshoreRail
    {
        readonly VisualElement bar;
        string signature;
        float nextEval;

        public AshoreRail(VisualElement root)
        {
            bar = new VisualElement();
            bar.AddToClassList(SheetTheme.Rail);
            bar.style.display = DisplayStyle.None;
            root.Add(bar);

            var label = new Label("ASHORE");
            label.AddToClassList("sheet-rail-label");
            label.pickingMode = PickingMode.Ignore;
            bar.Add(label);
        }

        /// Where the rail ends, in panel units, or 0 when it is not showing.
        /// The card hangs off this: the two share the right-hand corner and
        /// the rail is the one that stays on top, so it has to be able to say
        /// how much of the corner it is using.
        public float BottomPanelY =>
            bar != null && bar.resolvedStyle.display != DisplayStyle.None
                ? bar.layout.yMax : 0f;

        public void Tick(bool on, VisualElement root)
        {
            if (!on)
            {
                if (bar.style.display != DisplayStyle.None) Clear();
                bar.style.display = DisplayStyle.None;
                return;
            }

            // **Top right, UNDER the minimap.** The rail and the minimap are
            // different toolkits drawing into the same corner, so neither can
            // see the other by laying out — the rail sat across the map and,
            // worse, across the open card's own close button.
            //
            // `HudLayout.HeightOf` is the IMGUI column's own answer to "how
            // tall is the map right now", reported by the panel that drew it,
            // so this stacks under whatever it actually was rather than under
            // a copy of its height kept here. Zero when the map is not
            // drawing, and then the rail takes the corner itself.
            float scale = root.resolvedStyle.width / Mathf.Max(1f, Screen.width);
            var safe = Screen.safeArea;
            float mapH = HudLayout.HeightOf(HudLayout.Slot.Map);
            float top = (Screen.height - safe.yMax) * scale + 14f;
            if (mapH > 0f) top += (mapH + HudLayout.Gap) * scale;
            bar.style.right = (Screen.width - safe.xMax) * scale + 14f;
            bar.style.top = top;

            if (Time.unscaledTime < nextEval) return;
            nextEval = Time.unscaledTime + 0.25f;

            var camp = Camp();
            if (camp == null || camp.Ledger == null || camp.Ledger.hands.Count == 0)
            {
                if (signature != null) Clear();
                bar.style.display = DisplayStyle.None;
                return;
            }

            bar.style.display = DisplayStyle.Flex;

            // Rebuilt only when the ROW SET changes — names, moods, orders.
            // A rail rebuilt four times a second throws away the hover state
            // and allocates a token per hand per quarter second for a picture
            // that has not changed.
            var sb = new StringBuilder();
            var hands = camp.Ledger.hands;
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null) continue;
                sb.Append(h.name).Append(h.Angry ? '!' : '.').Append((int)h.order).Append('|');
            }
            string sig = sb.ToString();
            if (sig == signature) return;
            signature = sig;

            Clear();
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null) continue;
                string who = h.name;
                bar.Add(SheetKit.Token(who, h.Angry, Glyph(h.order), () => OpenHand(who)));
            }
        }

        void Clear()
        {
            // The rotated "ASHORE" caption is child 0 and stays.
            for (int i = bar.childCount - 1; i >= 1; i--) bar.RemoveAt(i);
            signature = null;
        }

        static Outpost Camp()
        {
            var anchor = Sheets.Anchor;
            if (anchor == null) return null;
            var isle = anchor.CurrentIsland;
            return isle != null ? Outpost.Of(isle) : null;
        }

        /// Open the hand's sheet — by finding their BODY, the same way
        /// `CampWorker` and `Outpost` pair a ledger row to an agent: on
        /// `CrewAgent.DisplayName`. A row with no body yet does nothing.
        ///
        /// Also swings the island camera to wherever that body actually is,
        /// so the tap answers "where are they" and not only "what are they
        /// doing". Found among `camp.Parked()` means ashore; found only by
        /// the fallback scene-wide search means the search had to reach past
        /// the island to find them — the ship.
        static void OpenHand(string who)
        {
            var camp = Camp();
            CrewAgent found = null;
            bool aboard = false;
            if (camp != null)
            {
                var parked = camp.Parked();
                if (parked != null)
                    foreach (var a in parked)
                        if (a != null && a.DisplayName == who) { found = a; break; }
            }
            if (found == null)
                foreach (var a in Object.FindObjectsByType<CrewAgent>(
                             FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (a != null && a.DisplayName == who) { found = a; aboard = true; break; }

            if (found == null) return;

            PanCameraTo(found, aboard);

            var sheet = Sheets.TryCreateFor(found);
            if (sheet != null) Sheets.Open(sheet);
        }

        /// Ease the island view onto the hand — their own body ashore, or
        /// the ship if `aboard`, since a body still parked below decks has
        /// nothing ashore worth looking at. `PanToWorld`'s own clamp keeps
        /// the height no closer than the camera's minimum; the player's next
        /// drag takes over from wherever the ease has gotten to, the same
        /// way any other gesture cancels a fling.
        static void PanCameraTo(CrewAgent found, bool aboard)
        {
            var isleCam = Object.FindFirstObjectByType<CameraRig.IslandCam>();
            if (isleCam == null) return;

            Vector3 point;
            if (aboard)
            {
                var anchor = Sheets.Anchor;
                if (anchor == null) return;
                point = anchor.transform.position;
            }
            else
            {
                point = found.transform.position;
            }

            isleCam.PanToWorld(point, CameraRig.IslandCam.BlueprintHeight, 0.6f);
        }

        /// Short text glyphs, never emoji — the HUD's type is a serif and a
        /// sans, and an emoji is neither and renders at whatever size the
        /// platform font feels like.
        static string Glyph(OutpostOrder order) => order switch
        {
            OutpostOrder.Gather => "⚒",
            OutpostOrder.Build => "⚑",
            OutpostOrder.Work => "◆",
            _ => "·",
        };
    }
}
