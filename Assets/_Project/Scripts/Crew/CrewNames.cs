using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Crew
{
    /// **One pool of names for everybody who did not come off the manifest.**
    ///
    /// The authored crew (`Assets/_Project/Settings/Crew_*.asset`) are named
    /// people: Pip, Bo, Mara, Tam, Ola. Everybody else -- a hand `Shipyard`
    /// clones to fill a new berth, a villager a camp recruits -- is a body
    /// with no name of its own, and until 2026-09-22 they took the name of
    /// whoever they were cloned from. The ship's crew list read
    /// "Pip, Bo, Bo, Bo", which Kevin flagged: they are different people, in
    /// name only the same.
    ///
    /// The fix is a single pool and a single rule -- **a name in use anywhere
    /// is not free** -- so the ship, the camps and the ledger draw from the
    /// same hat. `World.OutpostLedger.VillagerNames` is the camp's
    /// deterministic front door onto this; `Shipyard.ManCrew` and
    /// `BornVillager` come in through `Christen`.
    public static class CrewNames
    {
        /// ~24 short storybook names, the register the manifest's own crew
        /// names are already in. (Lived in `VillagerNames` until the ship
        /// needed the same hat; that class now reads this array.)
        public static readonly string[] Pool =
        {
            "Ash", "Bram", "Cass", "Dorrit", "Edda", "Finch", "Gale", "Hollis",
            "Ivo", "Jory", "Kess", "Lark", "Mabel", "Nye", "Orin", "Pell",
            "Quill", "Rowan", "Sable", "Tam", "Ursa", "Vane", "Wren", "Yara",
        };

        /// **The mark of a def nobody authored.** An asset's `name` is its
        /// file name (`Crew_Bo`); every def made at runtime gets this one, so
        /// a body wearing an authored, named person can be told apart from a
        /// body wearing a name the pool handed out -- which is what decides
        /// who keeps their name when two collide.
        public const string RuntimeDefName = "CrewMember (runtime)";

        /// A def of this body's own, copying the role and stomach of `like`.
        ///
        /// Never the shared asset: `Shipyard.Apply` writes `ironStomach`
        /// straight into `CrewAgent.Def`, so a clone left holding the
        /// template's asset edits the whole crew -- and, the reason this
        /// class exists, wears the template's name.
        public static CrewMemberDef MakeDef(string displayName, CrewMemberDef like)
        {
            var def = ScriptableObject.CreateInstance<CrewMemberDef>();
            def.name = RuntimeDefName;
            def.displayName = displayName;
            if (like != null) { def.role = like.role; def.ironStomach = like.ironStomach; }
            else def.role = CrewRole.Deckhand;
            return def;
        }

        /// Is this body one of the authored cast, rather than a clone the
        /// yard or a camp made?
        public static bool IsAuthored(CrewAgent agent) =>
            agent != null && agent.Def != null && agent.Def.name != RuntimeDefName;

        /// **Every name spoken for right now** -- bodies standing anywhere in
        /// the scene, and rows in every camp ledger, because a hand away at a
        /// camp is a name in use whether or not his body is loaded.
        public static HashSet<string> InUse()
        {
            var used = new HashSet<string>();
            foreach (var c in Object.FindObjectsByType<CrewAgent>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c == null) continue;
                string n = c.DisplayName;
                if (!string.IsNullOrEmpty(n)) used.Add(n);
            }
            foreach (var o in World.Outpost.All)
            {
                if (o == null || o.Ledger == null || o.Ledger.hands == null) continue;
                foreach (var h in o.Ledger.hands)
                    if (h != null && !string.IsNullOrEmpty(h.name)) used.Add(h.name);
            }
            return used;
        }

        /// **A body whose life has ended does not walk the deck** (2026-10-04).
        ///
        /// Root cause of Kevin's "Bo": the authored cast are fixed bodies in
        /// `Sea.unity`, so every load brings ALL of them back, whoever died
        /// or washed up; the steamer's `SteamerBootstrap.Man` then switches
        /// the first four on before the save's lives are even known, and
        /// nothing afterwards checked them (the ladder brig's `ManCrew`
        /// renamed them; the steamer path never did). Bo was lost at sea on
        /// day 649, the scene's Bo body came back on the next load, and went
        /// on living as Bo beside his own grave.
        ///
        /// Closed here: every SWITCHED-ON body under `ship` wearing a name a
        /// grave or a beach castaway holds is switched off -- the person is
        /// dead or on that beach, and the ship lost that hand when it
        /// happened, so a reload must not hand it back. Not renamed (no
        /// stranger appears to fill the gap), not counted
        /// (`CrewRoster.AboardCount` is switched-on only). Bodies already off
        /// are left exactly as they are (Ola). Runs after
        /// `DeathRepair`/`CastawayRepair` on load, so a death or a castaway
        /// copy those take back keeps its living body. Returns how many.
        public static int RetireTakenNames(Transform ship)
        {
            if (ship == null) return 0;
            int retired = 0;
            foreach (var c in ship.GetComponentsInChildren<CrewAgent>(false))
            {
                if (c == null || !c.gameObject.activeSelf) continue;
                if (c.GetComponent<SeaSick.Combat.RaidWalker>() != null) continue;
                string dn = c.DisplayName;
                if (string.IsNullOrEmpty(dn) || !World.Life.Lives.IsTaken(dn)) continue;
                c.gameObject.SetActive(false);
                retired++;
                Debug.Log("CrewNames: \"" + dn + "\" is dead or a castaway; the body aboard is stood down");
            }
            return retired;
        }

        /// The first free name in the pool, starting the walk at `seed`.
        ///
        /// Deterministic in `seed` and `taken`: the same berth on the same
        /// ship draws the same name every session, which is why the hands the
        /// yard clones need no row in the save file to keep their names.
        /// Falls back to a numbered hand rather than stalling when a very
        /// large crew has emptied the hat.
        public static string Free(int seed, ICollection<string> taken)
        {
            uint h = unchecked((uint)seed);
            for (int i = 0; i < Pool.Length; i++)
            {
                string candidate = Pool[(int)((h + (uint)i) % (uint)Pool.Length)];
                if (taken == null || !taken.Contains(candidate)) return candidate;
            }
            for (int i = 1; i < 999; i++)
            {
                string candidate = "Hand " + i;
                if (taken == null || !taken.Contains(candidate)) return candidate;
            }
            return "Hand";
        }

        /// **Give this body a name of its own.** Draws a free name, builds it
        /// a def, and books the name in `taken` so the next body in the same
        /// loop cannot take it again. Returns the name.
        public static string Christen(CrewAgent agent, int seed, HashSet<string> taken)
        {
            if (agent == null) return null;
            if (taken == null) taken = InUse();
            string name = Free(seed, taken);
            agent.SetDef(MakeDef(name, agent.Def));
            var born = agent.GetComponent<BornVillager>();
            if (born != null) { born.bornName = name; agent.gameObject.name = "Villager_" + name; }
            else agent.gameObject.name = "Hand_" + name;
            taken.Add(name);
            return name;
        }

        /// **Nobody aboard answers to the same name as anybody else.**
        ///
        /// Run after a load, where two naming paths meet: the yard clones her
        /// berths full before the save boards the hands she was carrying, so
        /// a villager recruited as "Rowan" can walk up the plank to find a
        /// clone already called Rowan. The authored cast keep their names,
        /// then the camp-born (their name is written in the save and in a
        /// ledger row), and only the yard's anonymous clones are re-drawn.
        ///
        /// Returns how many were renamed.
        public static int Deduplicate()
        {
            var agents = Object.FindObjectsByType<CrewAgent>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var taken = InUse();
            var kept = new HashSet<string>();
            var later = new List<CrewAgent>();

            // Names a ledger row is holding. A body whose name is written
            // down somewhere outranks one whose name was only drawn from the
            // hat, because renaming him would orphan the row.
            var rows = new HashSet<string>();
            foreach (var o in World.Outpost.All)
            {
                if (o == null || o.Ledger == null || o.Ledger.hands == null) continue;
                foreach (var h in o.Ledger.hands)
                    if (h != null && !string.IsNullOrEmpty(h.name)) rows.Add(h.name);
            }

            // First claim: the named cast, then anybody whose name a save or
            // a ledger row is holding, then the rest.
            foreach (var a in agents) Claim(a, 0, rows, kept, later);
            foreach (var a in agents) Claim(a, 1, rows, kept, later);
            foreach (var a in agents) Claim(a, 2, rows, kept, later);

            int renamed = 0;
            for (int i = 0; i < later.Count; i++)
            {
                string was = later[i].DisplayName;
                string now = Christen(later[i], i + kept.Count, taken);
                kept.Add(now);
                renamed++;
                Debug.Log("CrewNames: two hands answered to \"" + was
                          + "\"; the later one is now " + now);
            }
            return renamed;
        }

        /// One pass of the claim. `rank` 0 is the authored cast, 1 the
        /// camp-born, 2 everybody else; a body only claims on its own rank,
        /// and a body whose name is already claimed joins the re-draw.
        static void Claim(CrewAgent a, int rank, HashSet<string> rows,
            HashSet<string> kept, List<CrewAgent> later)
        {
            if (a == null) return;
            // A landing party is five bodies all called "raider" on purpose.
            // They are not crew and they are not anybody; leave them be.
            if (a.GetComponent<SeaSick.Combat.RaidWalker>() != null) return;
            string n = a.DisplayName;
            int mine = IsAuthored(a) ? 0
                : a.GetComponent<BornVillager>() != null
                  || (!string.IsNullOrEmpty(n) && rows.Contains(n)) ? 1 : 2;
            if (mine != rank) return;

            if (string.IsNullOrEmpty(n)) { later.Add(a); return; }
            if (kept.Add(n)) return;   // first body with this name keeps it
            later.Add(a);
        }
    }
}
