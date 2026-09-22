using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **The one thing in the chart layer that needs a frame.**
    ///
    /// `ChartData` and `Discovery` are both pure reads of the world — except
    /// for two facts that only exist if somebody is watching for them: an
    /// island the ship has sailed past, and where the ship has been. Nothing
    /// in the scene has to be wired for either; this stands itself up after
    /// the first scene load, the same way `SheetHost` does.
    ///
    /// It polls rather than subscribing because `AnchorController` raises no
    /// event on its state (AnchorController.cs:32 is a plain auto-property),
    /// and a poll at 4 Hz against a state that changes once a voyage is not
    /// worth an event on somebody else's class — especially not one I am not
    /// the owner of this week.
    public class ChartWatch : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Statics outlive play mode (domain reload is off), so the wipe
            // happens BEFORE the scene comes up rather than in `Awake` —
            // by `Awake` the world build has already noted the start island.
            ChartData.ResetForPlay();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Stand()
        {
            if (FindFirstObjectByType<ChartWatch>() != null) return;
            var go = new GameObject("ChartWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<ChartWatch>();
        }

        AnchorController anchor;
        AnchorController.State was = AnchorController.State.Underway;
        float nextScan;

        void Update()
        {
            ChartData.TickTrack();

            if (anchor == null) anchor = FindFirstObjectByType<AnchorController>();
            if (anchor == null) return;

            // Landing. Read every frame, not on the scan timer: the state is
            // an edge, and a quarter-second window is long enough to miss
            // `Dropping → Anchored → Ashore` entirely.
            var now = anchor.CurrentState;
            if (now != was)
            {
                was = now;
                if ((now == AnchorController.State.Anchored
                     || now == AnchorController.State.Ashore)
                    && anchor.CurrentIsland != null)
                    Discovery.NoteLanding(anchor.CurrentIsland);
            }

            // Glimpses. A sweep of `Island.All` four times a second, which is
            // what it costs to have the chart fill in as she sails.
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.25f;

            Vector3 p = anchor.transform.position;
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                if (Discovery.Of(isle) != Seen.Never) continue;
                Vector3 d = isle.transform.position - p;
                d.y = 0f;
                // Off the island's OWN outline, not off its centre: a long
                // spit and a pebble would otherwise be seen from the same
                // distance, and the big one is the one you can actually see.
                if (d.magnitude <= isle.MaxRadius + Discovery.GlimpseMargin)
                    Discovery.NoteGlimpse(isle);
            }
        }
    }
}
