using System;
using System.Collections.Generic;
using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **The one open sheet, and the table of who can make one.**
    ///
    /// There is exactly one sheet open at a time by construction — that is the
    /// whole design rule ("one object, one sheet, one decision") expressed as
    /// a static rather than as a convention four panels are expected to keep.
    /// `Open` on a second thing closes the first.
    ///
    /// The registry is the seam between this foundation and the cards
    /// themselves: the foundation never knows what a campfire's sheet says,
    /// and the content never knows where a card is placed or when it is
    /// refreshed. Content registers a factory per component type from a
    /// `[RuntimeInitializeOnLoadMethod]`, and `TryCreateFor` walks up from
    /// whatever collider the player's finger landed on until one of those
    /// types answers.
    public static class Sheets
    {
        // --- the open sheet ---

        static ISheet current;

        public static ISheet Current => current;
        public static bool IsOpen => current != null;

        /// Raised whenever the open sheet changes — opened, swapped or closed.
        /// `SheetHost` rebuilds the card on it; `SelectionRing` re-targets.
        public static event Action Changed;

        public static void Open(ISheet sheet)
        {
            if (sheet == null) { Close(); return; }
            if (ReferenceEquals(sheet, current)) return;
            current = sheet;
            Changed?.Invoke();
        }

        public static void Close()
        {
            if (current == null) return;
            current = null;
            Changed?.Invoke();
        }

        // --- the registry ---

        /// Type -> "make a sheet for this component". Keyed on the EXACT
        /// component type the factory was registered for; `TryCreateFor` walks
        /// the base chain, so registering a base type still catches a
        /// subclass without every subclass needing a row.
        static readonly Dictionary<Type, Func<Component, ISheet>> factories =
            new Dictionary<Type, Func<Component, ISheet>>();

        /// Content registers here, once, from a `[RuntimeInitializeOnLoadMethod]`.
        ///
        /// A domain reload clears the dictionary along with everything else
        /// static, and `RuntimeInitializeOnLoadMethod` runs again after one, so
        /// the two stay in step without this class having to clear anything.
        public static void Register<T>(Func<T, ISheet> factory) where T : Component
        {
            if (factory == null) return;
            factories[typeof(T)] = c => factory(c as T);
        }

        /// True once anything has registered — the host uses it to keep quiet
        /// until the content agent's bootstrap has run.
        public static bool AnyRegistered => factories.Count > 0;

        /// Walk up from what the finger hit and ask each registered type in
        /// turn. Returns null when nothing up the chain has a sheet.
        ///
        /// `GetComponentsInParent` rather than a hand-written parent walk
        /// because a pickable's collider is routinely a grandchild of the
        /// thing that owns the behaviour — the ship's hull collider, a
        /// building's mesh child — and the walk has to cross those.
        public static ISheet TryCreateFor(Component c)
        {
            if (c == null || factories.Count == 0) return null;
            var chain = c.GetComponentsInParent<Component>(true);
            if (chain == null) return null;
            for (int i = 0; i < chain.Length; i++)
            {
                var comp = chain[i];
                if (comp == null) continue;
                for (var t = comp.GetType(); t != null && t != typeof(Component); t = t.BaseType)
                {
                    if (!factories.TryGetValue(t, out var make)) continue;
                    var sheet = make(comp);
                    if (sheet != null) return sheet;
                }
            }
            return null;
        }

        // --- the chart ---

        /// The chart's own sheet, registered the same way the object sheets
        /// are. It is separate from `Register<T>` because the chart is not
        /// opened by tapping a thing in the world — it is opened by tapping
        /// the instrument, which is HUD, so there is no component to key on.
        static Func<ISheet> chartFactory;

        public static void RegisterChart(Func<ISheet> factory) => chartFactory = factory;

        /// Open the chart if anything has registered one. Returns false when
        /// nothing has, so the instrument can be built and shipped before the
        /// sheet behind it exists rather than waiting on it.
        public static bool TryOpenChart()
        {
            if (chartFactory == null) return false;
            var s = chartFactory();
            if (s == null) return false;
            Open(s);
            return true;
        }

        /// True while the chart instrument is the thing drawing the compass,
        /// the map and the nav line. Unlike `SuppressLegacy` this is NOT about
        /// lying at a camp: the instrument is up at sea as well, which is
        /// exactly where the old minimap and compass tape used to be the only
        /// instruments. `ChartInstrument` owns this flag.
        public static bool ChartActive { get; internal set; }

        // --- is the sheet HUD the HUD right now? ---

        /// True while she is lying at an island that has a camp — the one
        /// situation the sheet HUD is built for. The legacy IMGUI panels read
        /// it and stand down, so the two never draw over each other, and
        /// neither one has to know the other exists beyond this line.
        ///
        /// Evaluated four times a second rather than per call: `OnGUI` runs
        /// once per IMGUI EVENT and several panels ask, so a `FindFirstObject`
        /// behind this property would be dozens of scene walks a frame.
        public static bool SuppressLegacy
        {
            get { Evaluate(); return suppress; }
        }

        static bool suppress;
        static float nextEval;
        static AnchorController anchor;

        /// **Statics outlive play mode here — the clock does not.**
        ///
        /// Domain reload is off in this project, so everything above survives
        /// a play session while `Time.unscaledTime` restarts at zero. A
        /// session that ran for five minutes therefore left `nextEval` at
        /// ~300, and the NEXT session spent its first five minutes with the
        /// throttle permanently closed, serving whatever `suppress` happened
        /// to be when the last one stopped. The symptom is the worst kind:
        /// the sheet HUD and the legacy IMGUI panels both draw, on top of
        /// each other, and only on the second and later runs — so it looks
        /// like a guard that does not work rather than a clock that moved.
        ///
        /// `GameBoot` documents the same trap for its own statics. Anything
        /// static that stores a TIME has to be reset here.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            nextEval = 0f;
            suppress = false;
            anchor = null;
            current = null;
            ChartActive = false;
            factories.Clear();
            chartFactory = null;
        }

        static void Evaluate()
        {
            // **The lookup is OUTSIDE the throttle, the verdict is inside it.**
            // They were both inside it first, and that is a bug with a very
            // quiet failure: the ship is not up on the frame the HUD first
            // asks, so `anchor` is null, and every call for the next quarter
            // second then returns before the line that would have found her.
            // `SuppressLegacy` is asked several times a frame by the legacy
            // panels, so the throttle is nearly always closed and the cache
            // never fills -- the place label sat there with its island name
            // blank while the sub-line underneath it read correctly, because
            // that line reads the same way whether the anchor is found or not.
            //
            // Finding her costs a scene walk ONCE, on the frames where the
            // field is empty; after that this is a null check.
            if (anchor == null) anchor = UnityEngine.Object.FindFirstObjectByType<AnchorController>();

            if (Time.unscaledTime < nextEval) return;
            nextEval = Time.unscaledTime + 0.25f;
            if (anchor == null) { suppress = false; return; }

            bool stopped = anchor.CurrentState == AnchorController.State.Anchored
                        || anchor.CurrentState == AnchorController.State.Ashore
                        || anchor.CurrentDock != null;
            if (!stopped) { suppress = false; return; }

            // **A surveyed island is not a camp.** This read `Outpost.Of(isle)
            // != null`, which is true the moment the SURVEY finishes -- the
            // survey adds the `Outpost` component itself (Outpost.cs:2600),
            // and it runs as the anchor goes down, before anything is built.
            // So the legacy bar stood down the instant she stopped, and with
            // it went the only "🔥 Make camp" button in the game
            // (CampSheet.cs:310) -- while the sheet HUD had nothing to put in
            // its place, because `SheetBootstrap.FireFor` refuses a camp with
            // no fire and no blueprint, and there is no campfire in the world
            // to tap. Kevin, on the phone, 2026-09-22: "I see no option at all
            // to build the campfire."
            //
            // The test is the same one `FireFor` uses, so the handover is
            // exact: the legacy bar owns the island until the fire is sited,
            // the sheets own it from the frame the drawing goes down.
            var isle = anchor.CurrentIsland;
            var camp = isle != null ? Outpost.Of(isle) : null;
            suppress = camp != null && (camp.HasCamp || camp.Building);
        }

        /// The ship the sheet HUD is hung off, for anything that needs her —
        /// the place label, the ashore rail. Cached by `Evaluate`.
        public static AnchorController Anchor
        {
            get { Evaluate(); return anchor; }
        }
    }
}
