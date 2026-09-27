using System.Collections.Generic;

namespace SeaSick.World.Life
{
    /// One thing that happened to somebody, worth remembering for the story
    /// their tombstone tells. `kind` is one of the `LifeEvents` constants.
    /// `camp` is the camp label it happened at (may be empty for an at-sea
    /// event nothing has built yet in phase 1). `other` is another person's
    /// name, for events that are about a relationship (`RescuedOther`,
    /// `DraggedOther`).
    ///
    /// **Repeats collapse.** `Lives.Log` looks for an existing event with
    /// the same kind+camp+other and bumps `count` instead of appending a
    /// second row -- "went over the rail twice" is one event with count 2,
    /// not two events. `day` holds the LAST day it happened.
    [System.Serializable]
    public class LifeEvent
    {
        public string kind = "";
        public string camp = "";
        public int day;
        public int count = 1;
        public string other = "";
    }

    /// **One person's life, while they are alive.** Keyed by name (the same
    /// identity `OutpostHand` and `CrewAgent` use), global rather than
    /// per-camp because a hand moves between the ship and any number of
    /// camps over a life.
    [System.Serializable]
    public class LifeRecord
    {
        public string name = "";
        /// Game day they were first seen (recruited, born, or the manifest's
        /// own crew at world start). -1 = unknown -- an old save's cast, or
        /// anybody whose first event predates this system.
        public int bornDay = -1;
        /// The camp they were recruited at, or first logged an event at.
        /// Used by the story generator's fallback sentence.
        public string homeCamp = "";
        public List<LifeEvent> events = new List<LifeEvent>();
    }

    /// **One dead person**, as much as the tombstone/graveyard flow needs.
    /// Global (like `LifeRecord`), saved forever -- a graveyard should
    /// survive as long as the save does.
    [System.Serializable]
    public class GraveRecord
    {
        public string name = "";
        public string camp = "";
        public int bornDay = -1;
        public int diedDay;
        /// One of the `LifeEvents` death kinds (`KilledInRaid`, `LostAtSea`,
        /// `HuntingAccident`, `Shipwreck`, or empty for "downed, nobody
        /// came").
        public string cause = "";
        public float x, z;
        /// Exactly 3 sentences, built once at death by `LifeStory.Build` and
        /// saved rather than regenerated, so a story never changes under a
        /// tombstone the player already read.
        public string[] story = new string[3];
        /// **Phase 3's flag**, carried here from the start so the save
        /// format does not have to change when the placement flow lands:
        /// true once the player has sited this grave's tombstone. Phase 1
        /// never sets it and never reads it.
        public bool placed;
    }
}
