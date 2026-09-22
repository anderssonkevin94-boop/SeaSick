using System.Collections.Generic;
using SeaSick.UI.Sheets;
using UnityEngine;

namespace SeaSick.World
{
    /// **What the player has actually seen of the archipelago.**
    ///
    /// The chart is not a map of the world; it is a map of the voyage. An
    /// island the ship has never been near is not on it at all, an island
    /// she has sailed past is a hatched blob with no name, and an island she
    /// has anchored off is drawn and named. That is the whole state machine,
    /// and it lives here rather than on `Island` because it belongs to the
    /// SAVE — the world is deterministic from its seed, what you know about
    /// it is not.
    ///
    /// **Monotonic.** A state never goes down: `NoteGlimpse` on a landed
    /// island is a no-op. Everything that records discovery is a proximity
    /// test firing several times a second, so the only safe rule is that a
    /// note can raise the state and nothing can lower it.
    ///
    /// Keyed on the `Island` component at runtime and on the island's centre
    /// in the save, for exactly the reason `OutpostSave.isleX/isleZ` gives:
    /// centres are as stable as the seed, an index or a scene name is not.
    public static class Discovery
    {
        static readonly Dictionary<Island, Seen> state = new Dictionary<Island, Seen>();

        /// How far off an island's own outline counts as having laid eyes on
        /// it. Generous on purpose — the point of a glimpse is that the chart
        /// fills in as you sail, so it wants to trigger from the water you
        /// pass through rather than from the water you stop in.
        public const float GlimpseMargin = 700f;

        /// Statics outlive play mode (domain reload is off), so a new voyage
        /// has to be told to forget the last one. Called by `GameBoot`'s
        /// reset path through `ChartData.ResetForPlay`.
        public static void Clear() => state.Clear();

        public static Seen Of(Island i)
        {
            if (i == null) return Seen.Never;
            if (state.TryGetValue(i, out var s)) return s;

            // **Two islands are landed without anybody having noted it.**
            // Home is where she starts, so it is not a discovery; and an
            // island carrying a camp was self-evidently landed on, whatever
            // the save happens to remember — a restored camp is proof.
            if (i.IsHome || Outpost.Of(i) != null)
            {
                state[i] = Seen.Landed;
                return Seen.Landed;
            }
            return Seen.Never;
        }

        public static void NoteGlimpse(Island i) => Raise(i, Seen.Glimpsed);
        public static void NoteLanding(Island i) => Raise(i, Seen.Landed);

        static void Raise(Island i, Seen to)
        {
            if (i == null) return;
            if (Of(i) >= to) return;
            state[i] = to;
        }

        /// How many islands are on the chart at all — the title line's count.
        public static int SeenCount
        {
            get
            {
                int n = 0;
                foreach (var isle in Island.All)
                    if (isle != null && Of(isle) != Seen.Never) n++;
                return n;
            }
        }

        // --- the save ---------------------------------------------------------

        /// Every island that is more than `Never`, as rows a `SaveData` can
        /// carry. `Never` is the default, so it is never written: a save
        /// lists what you know, not what you do not.
        public static List<SeenSave> Capture()
        {
            var rows = new List<SeenSave>();
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                var s = Of(isle);
                if (s == Seen.Never) continue;
                var p = isle.transform.position;
                rows.Add(new SeenSave { island = isle.name, x = p.x, z = p.z, state = (int)s });
            }
            return rows;
        }

        /// Put a save's rows back. Additive and monotonic like everything
        /// else here, so restoring into a world that has already noted a
        /// glimpse cannot un-see it.
        public static void Apply(List<SeenSave> rows)
        {
            if (rows == null) return;
            foreach (var r in rows)
            {
                if (r == null) continue;
                var isle = Save.SaveGame.IslandCentred(r.x, r.z);
                // The name is the fallback, not the key: a hand-edited save
                // or a centre that moved by more than the 25 m tolerance is
                // still recoverable while the scene names hold.
                if (isle == null) isle = ByName(r.island);
                if (isle == null) continue;
                Raise(isle, (Seen)Mathf.Clamp(r.state, 0, (int)Seen.Landed));
            }
        }

        static Island ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var isle in Island.All)
                if (isle != null && isle.name == name) return isle;
            return null;
        }
    }

    /// One island's discovery state, as `JsonUtility` can write it: public
    /// fields, no dictionary, no enum on the wire (an int, so a reordered
    /// `Seen` cannot silently re-label an old save).
    [System.Serializable]
    public class SeenSave
    {
        public string island;
        public float x, z;
        public int state;
    }
}
