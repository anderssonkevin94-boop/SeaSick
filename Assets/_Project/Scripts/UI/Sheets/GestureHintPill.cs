using SeaSick.CameraRig;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **One-time gesture hints, camp and sea (2026-09-30, island UI
    /// restructure phase 6).** Rule 7, teach by doing: the gestures that no
    /// button shows -- drag / pinch the camp view, press-and-hold a hand,
    /// double-tap to fly, tap an enemy hull to lock -- each get ONE calm line
    /// the first time the gesture matters, and never again
    /// (`GestureHints`, a PlayerPrefs flag per hint). In order of teaching:
    /// <list type="bullet">
    /// <item>`Look`, at the player's own camp, the first time the camp view
    ///   shows: "Drag to look around · pinch to zoom" (desktop: "Drag to look
    ///   around · scroll to zoom"). Done by a pan, zoom or turn
    ///   (`IslandCam.LookedAt`), or by the first world tap that opens a
    ///   sheet (`IslandInput.WorldTapAt`).</item>
    /// <item>`FlyTo`, right after the first pan at camp: "Double-tap the
    ///   ground to fly there". Done by a fly-to (`IslandCam.FlewAt`).</item>
    /// <item>`Carry`, the first time a villager stands in view at camp:
    ///   "Press and hold a hand to carry them to a job". Done by a pick-up
    ///   (`Hand.Holding`).</item>
    /// <item>`Lock`, at sea, the first time an enemy is in lock reach with
    ///   nothing locked: "Tap the enemy ship to lock your guns". Done by a
    ///   lock by any path (tap, the row's Lock button, Space).</item>
    /// </list>
    /// (The Backpack's "Hold a tile to keep some ashore" lives on the sheet's
    /// own info line, `BackpackSheet.RefreshInfo`; the stick's "drag here to
    /// sail" ring is `SeaHud`'s.)
    ///
    /// **Where**: the carry pill's spot, in the thumb lane just above the bar
    /// and Next card (camp), or just above the combat row (sea) -- where the
    /// thumb already is; never over the world, never over a building (Kevin's
    /// rule). One hint at a time, 1.5 s apart. **Never takes a touch**
    /// (`PickingMode.Ignore` all the way down). Each retires after
    /// `GestureHints.ShowSeconds` on screen if the gesture is never done.
    /// Hidden (its clock paused) under a sheet, while placing, while the
    /// carry pill holds the same spot; stacked above the food-draft notice
    /// (`CampStatusHud.TopPanel`) while one is up.
    ///
    /// Owned by `PartyReportToast` next to the carry pill, ticked from
    /// `SheetHost.LateUpdate`. Reads only.
    internal sealed class GestureHintPill
    {
        const float MaxWidth = 460f;
        const float Gap = 8f;
        /// Quiet time between one hint going and the next appearing.
        const float Between = 1.5f;

        readonly VisualElement holder;
        readonly Label text;
        bool shown = true;
        string current;
        float quietUntil;
        float nextViewCheck;
        bool villagerInView;
        float lastK = -1f;

        /// The hint on screen right now (null when none); for the probe/log.
        public static string Showing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Showing = null;

        public GestureHintPill(VisualElement root)
        {
            holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.AddToClassList("carry-holder");
            holder.AddToClassList("hint-holder");

            var pill = new VisualElement { pickingMode = PickingMode.Ignore };
            pill.AddToClassList("carry-pill");
            pill.AddToClassList("hint-pill");

            text = new Label { pickingMode = PickingMode.Ignore };
            text.AddToClassList("carry-text");
            text.AddToClassList("hint-text");
            pill.Add(text);

            holder.Add(pill);
            root.Add(holder);
            Hide();
        }

        void Hide()
        {
            Showing = null;
            if (!shown) return;
            shown = false;
            holder.style.display = DisplayStyle.None;
        }

        /// `carryShowing`: the carry pill holds the spot this frame.
        public void Tick(VisualElement root, bool carryShowing)
        {
            float now = Time.unscaledTime;
            string want = null, line = null;
            bool atSea = false;

            bool campPending = !GestureHints.IsDone(GestureHints.Look) || !GestureHints.IsDone(GestureHints.FlyTo)
                               || !GestureHints.IsDone(GestureHints.Carry);
            if (campPending && MidnightLandHud.Active && IslandCam.Engaged && OwnCamp(out _))
            {
                Retire(now);
                bool blocked = carryShowing || Sheets.Current != null || ThumbBar.PlacementActive
                               || LandingPartySheet.IsOpen || World.Life.GraveGate.Blocking
                               || (Hand.Instance != null && Hand.Instance.Holding);
                if (!blocked && now >= quietUntil)
                {
                    if (!GestureHints.IsDone(GestureHints.Look))
                    {
                        want = GestureHints.Look;
                        line = Touchscreen() ? "Drag to look around · pinch to zoom"
                                             : "Drag to look around · scroll to zoom";
                    }
                    else if (IslandCam.PannedAt >= 0f && !GestureHints.IsDone(GestureHints.FlyTo))
                    {
                        want = GestureHints.FlyTo;
                        line = Touchscreen() ? "Double-tap the ground to fly there"
                                             : "Double-click the ground to fly there";
                    }
                    else if (!GestureHints.IsDone(GestureHints.Carry) && VillagerInView(now))
                    {
                        want = GestureHints.Carry;
                        line = "Press and hold a hand to carry them to a job";
                    }
                }
            }
            else if (!MidnightLandHud.Active && CombatHud.Visible && CombatHud.Source != null)
            {
                var lk = CombatHud.Source;
                if (lk.Locked != null) GestureHints.MarkDone(GestureHints.Lock);
                // Only while her hull is in view: "tap the enemy ship" with the
                // raider astern of the camera is a riddle, not a hint.
                else if (lk.CurrentCandidate != null && !GestureHints.IsDone(GestureHints.Lock)
                         && Sheets.Current == null && InView(lk.CurrentCandidate.HitCentre))
                {
                    want = GestureHints.Lock;
                    line = "Tap the enemy ship to lock your guns";
                    atSea = true;
                }
            }

            if (want == null)
            {
                if (current != null && GestureHints.IsDone(current)) { quietUntil = now + Between; current = null; }
                Hide();
                return;
            }

            // Counts on-screen time; true once it has stood its six seconds.
            if (GestureHints.Shown(want, Time.unscaledDeltaTime))
            {
                quietUntil = now + Between;
                current = null;
                Hide();
                return;
            }

            current = want;
            Showing = want;
            if (text.text != line) text.text = line;
            if (!shown)
            {
                shown = true;
                holder.style.display = DisplayStyle.Flex;
                holder.BringToFront();
            }
            Place(root, atSea);
        }

        /// Marks the camp hints whose gesture has now been done.
        void Retire(float now)
        {
            if (!GestureHints.IsDone(GestureHints.Look))
            {
                if (IslandCam.LookedAt >= 0f) GestureHints.MarkDone(GestureHints.Look);
                // The first building tap: nothing more to say about looking.
                // Only a sheet a world tap opened counts (the Welcome back
                // sheet on load must not retire it).
                else if (Sheets.Current != null && IslandInput.WorldTapAt >= 0f
                         && now - IslandInput.WorldTapAt < 1f) GestureHints.MarkDone(GestureHints.Look);
            }
            if (IslandCam.FlewAt >= 0f) GestureHints.MarkDone(GestureHints.FlyTo);
            if (Hand.Instance != null && Hand.Instance.Holding) GestureHints.MarkDone(GestureHints.Carry);
            if (current != null && GestureHints.IsDone(current)) { current = null; quietUntil = now + Between; }
        }

        static bool OwnCamp(out Outpost camp)
        {
            camp = CampToasts.Here();
            return camp != null && camp.HasCamp;
        }

        static bool Touchscreen() =>
            Application.isMobilePlatform || UnityEngine.InputSystem.Touchscreen.current != null;

        /// A villager body stands in the middle 80 % of the view (checked
        /// four times a second).
        bool VillagerInView(float now)
        {
            if (now < nextViewCheck) return villagerInView;
            nextViewCheck = now + .25f;
            villagerInView = false;
            var cam = Camera.main;
            if (cam == null) return false;
            var bodies = CampWorker.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b == null || !b.isActiveAndEnabled) continue;
                Vector3 v = cam.WorldToViewportPoint(b.transform.position);
                if (v.z > 0f && v.x > .1f && v.x < .9f && v.y > .15f && v.y < .9f) { villagerInView = true; break; }
            }
            return villagerInView;
        }

        static bool InView(Vector3 world)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(world);
            return v.z > 0f && v.x > .05f && v.x < .95f && v.y > .3f && v.y < .95f;
        }

        void Place(VisualElement root, bool atSea)
        {
            ThumbBar.Lane(root, out float left, out float width, out float bottom);
            float k = 1f;
            if (atSea)
            {
                // The combat row's design-px scale (390 wide upright, one
                // panel unit per design px on a desk), so the pill matches it.
                var safe = Screen.safeArea;
                if (safe.width < 1f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
                float s = Mathf.Max(1e-4f, SheetHost.PanelScale);
                k = HudLayout.Wide ? 1f : safe.width / CombatHud.DesignWidth * s;
                bottom = (Screen.height - CombatHud.Rect.yMin) * s + Gap;
            }
            else bottom = Mathf.Max(bottom, Mathf.Max(ThumbBar.ReservePanel, CampStatusHud.TopPanel)) + Gap;

            if (width > MaxWidth * k) { left += (width - MaxWidth * k) * 0.5f; width = MaxWidth * k; }
            if (!Mathf.Approximately(k, lastK))
            {
                lastK = k;
                holder.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(100f), 0f);
                holder.style.scale = new Scale(new Vector3(k, k, 1f));
            }
            // Scaled about its bottom centre: lay it out 1/k as wide so the
            // drawn width is the lane's.
            float w = width / k;
            holder.style.left = left + (width - w) * .5f;
            holder.style.width = w;
            holder.style.bottom = bottom;
        }
    }
}
