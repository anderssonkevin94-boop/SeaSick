using UnityEngine;

namespace SeaSick.Crew
{
    /// **A body that was never on the ship's books.**
    ///
    /// The camp recruits into its own ledger (`OutpostHand.born`), and a row
    /// with nobody to look at is a number in a panel -- so every born row gets
    /// one of these: the same authored, named figure the ship's hands wear,
    /// cloned from one of them rather than built from primitives, for the same
    /// reason `Shipyard.ManCrew` clones (one body, one scale, one material,
    /// one place to drift from `WorldScale.Person`).
    ///
    /// The component itself is a TAG. It says "this figure did not come out of
    /// the scene, so nothing in the ship's save will re-create it" -- which is
    /// exactly what `SaveGame` needs to know to write the name down and make
    /// him again on load.
    public class BornVillager : MonoBehaviour
    {
        /// The name he was recruited under. Kept beside `CrewMemberDef` so a
        /// save can read it off a body whose def is a runtime instance.
        public string bornName = "";

        /// **Clone a villager.** `parent` is where he lives -- an outpost
        /// while he is one of theirs, the hull once he is carried aboard.
        ///
        /// Returns null when there is no crew figure anywhere to copy, which
        /// is a scene with no ship in it and nothing this can invent.
        public static CrewAgent Make(string displayName, Transform parent)
        {
            if (string.IsNullOrEmpty(displayName)) return null;
            var template = Template();
            if (template == null) return null;

            var clone = Object.Instantiate(template.gameObject, parent);
            clone.name = "Villager_" + displayName;
            clone.SetActive(false);              // the caller decides; see Outpost.Watched

            var agent = clone.GetComponent<CrewAgent>();
            if (agent == null) { Object.Destroy(clone); return null; }

            // A worker is attached to a body, never authored into one; a clone
            // taken off a hand who happened to be chopping would arrive with
            // somebody else's job half-done.
            var stale = clone.GetComponent<SeaSick.World.CampWorker>();
            if (stale != null) Object.DestroyImmediate(stale);

            // His own def, not a shared asset: `Shipyard.Refit` writes
            // `ironStomach` straight into `CrewAgent.Def`, so a villager
            // sharing the template's asset would edit the whole crew.
            var src = template.Def;
            var def = ScriptableObject.CreateInstance<CrewMemberDef>();
            def.displayName = displayName;
            if (src != null) { def.role = src.role; def.ironStomach = src.ironStomach; }
            else def.role = CrewRole.Deckhand;
            agent.SetDef(def);

            var tag = clone.GetComponent<BornVillager>();
            if (tag == null) tag = clone.AddComponent<BornVillager>();
            tag.bornName = displayName;
            return agent;
        }

        /// Somebody to copy. A hand who is not currently working a camp is
        /// preferred -- see above -- but anybody is better than nobody.
        static CrewAgent Template()
        {
            CrewAgent any = null;
            foreach (var c in Object.FindObjectsByType<CrewAgent>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c == null) continue;
                if (any == null) any = c;
                if (c.GetComponent<SeaSick.World.CampWorker>() != null) continue;
                if (c.GetComponent<BornVillager>() != null) continue;
                return c;
            }
            return any;
        }

        /// **A deck post nobody is standing on**, ship-local: beside the hands
        /// she already has, stepping aft a little for each pair, so a villager
        /// carried aboard stands ON her rather than in the sea beside her.
        /// Deliberately arithmetic rather than a bay lookup -- `Shipyard`
        /// re-posts the whole crew on the next refit anyway.
        public static Vector3 FreeStation(Transform hull)
        {
            if (hull == null) return Vector3.zero;
            int n = 0;
            Vector3 beside = Vector3.zero;
            bool have = false;
            foreach (var c in hull.GetComponentsInChildren<CrewAgent>(true))
            {
                if (c == null || !c.gameObject.activeSelf) continue;
                if (!have) { beside = c.transform.localPosition; have = true; }
                n++;
            }
            if (!have) return Vector3.zero;
            float side = n % 2 == 0 ? 1f : -1f;
            return beside + new Vector3(side * 0.7f, 0f, -0.5f * (n / 2));
        }

        /// Make a villager who is already aboard -- what a save does on load
        /// for anybody the ship is carrying who was never in the scene to
        /// begin with. Returns the body, or one that was already there.
        public static CrewAgent Board(string displayName, Transform hull)
        {
            if (hull == null || string.IsNullOrEmpty(displayName)) return null;
            foreach (var c in hull.GetComponentsInChildren<CrewAgent>(true))
                if (c != null && c.DisplayName == displayName) return c;

            Vector3 post = FreeStation(hull);
            var a = Make(displayName, hull);
            if (a == null) return null;
            a.gameObject.SetActive(true);
            a.BoardShip(hull, post);
            return a;
        }

        /// Everybody in the scene who was born at a camp, wherever they now
        /// stand. Used by the save, which has to write down the ones the ship
        /// would not otherwise re-create.
        public static BornVillager[] All() => Object.FindObjectsByType<BornVillager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
    }
}
