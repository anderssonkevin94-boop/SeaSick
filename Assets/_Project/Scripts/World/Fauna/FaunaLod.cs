using System.Collections.Generic;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.Terrain;

namespace SeaSick.World
{
    /// **The island's fauna, switched off when nobody is near it.**
    ///
    /// There can be a dozen islands in the streamed world and every one of
    /// them wants a herd. A goat thinking, turning and walking is trivial;
    /// two hundred goats doing it across an archipelago the player cannot see
    /// is a frame budget spent on nothing. So the whole herd is gated once a
    /// second on one distance test against the island as a WHOLE -- islands
    /// are the unit the world already streams in, and an animal is never
    /// meaningfully nearer than its island is.
    ///
    /// It also owns the two things every animal on the island would otherwise
    /// look up for itself: the crew bodies to flee from and the ship. One
    /// `FindObjectsByType` a second for the island beats one per animal per
    /// scan -- there is no `CrewAgent` registry to ask (checked: `CrewAgent`
    /// keeps no static list), and the flee scan is the only reason an animal
    /// needs to know about anything outside itself.
    ///
    /// Re-activating does NOT reset anyone. An animal resumes mid-graze where
    /// it stood, because the alternative -- a herd that teleports to its
    /// anchor whenever you sail back -- is more noticeable than the pause.
    public class FaunaLod : MonoBehaviour
    {
        /// Metres from the island's SHORE, not its centre. Placeholder: a bit
        /// past `sceneryLod0Distance` (220 m), so the herd is already thinking
        /// by the time the trees around it are at full detail.
        public const float ThinkRange = 300f;

        public Island Island { get; private set; }
        public Vector3 Centre { get; private set; }
        public float SandTop { get; private set; }
        public Transform Root => transform;
        public bool Active { get; private set; } = true;

        /// The camp is a keep-out only where there IS a camp; `Outpost.Of` is
        /// null on an island nobody has landed on.
        public Vector3 CampAt { get; private set; }
        public float CampKeepOut { get; private set; }

        System.Func<float, float, float> height;
        public float Height(float x, float z) => height != null ? height(x, z) : 0f;

        readonly List<Animal> animals = new List<Animal>();
        readonly List<Gull> gulls = new List<Gull>();
        readonly List<Renderer[]> bodies = new List<Renderer[]>();
        /// The body each `bodies` entry was taken off, so `Remove` can find
        /// the renderers belonging to one animal. A parallel list rather than
        /// an index into `animals`: gulls go into `bodies` too, so the two
        /// have never lined up.
        readonly List<GameObject> owners = new List<GameObject>();
        public int Count => animals.Count + gulls.Count;

        /// **The live herd, for anyone who needs to pick one out of it** --
        /// the ledger choosing what a hunter's kill takes down, the cursor
        /// deciding whether the thing under it is a goat. Goats and boar
        /// only; the gulls are scenery and nobody hunts them.
        public IReadOnlyList<Animal> Animals => animals;

        // Shared threat state, refreshed on the same one-second tick.
        CrewAgent[] crew = System.Array.Empty<CrewAgent>();
        public IReadOnlyList<CrewAgent> Crew => crew;
        public SeaSick.Ship.ShipMotor Ship { get; private set; }
        public Vector3 ShipAt { get; private set; }
        public bool ShipMoving { get; private set; }

        float tick;
        float gateRange;

        public void Bind(Island isle, System.Func<float, float, float> h, float sandTop)
        {
            Island = isle;
            height = h;
            SandTop = sandTop;
            Centre = isle != null ? isle.transform.position : transform.position;
            gateRange = (isle != null ? isle.MaxRadius : 0f) + ThinkRange;

            var post = isle != null ? Outpost.Of(isle) : null;
            if (post != null && post.HasCamp)
            {
                CampAt = post.CampCentre;
                CampKeepOut = FaunaField.CampKeepOut;
            }
        }

        public void Add(GameObject body, Animal a, Gull g)
        {
            if (a != null) animals.Add(a);
            if (g != null) gulls.Add(g);
            bodies.Add(body.GetComponentsInChildren<Renderer>(true));
            owners.Add(body);
        }

        /// **One animal is gone.** Called by `Animal.Die` before the carcass
        /// is destroyed, and it must take the renderers with it: the gate
        /// sweep walks `bodies` a second later and a `Renderer[]` belonging
        /// to a destroyed object is a null-reference an island's width away
        /// from anything the player did.
        ///
        /// Taking it out of `animals` here is also what makes the flop safe:
        /// `Gate` never touches a component it cannot see, so a carcass goes
        /// on flopping even if the player sails out of range mid-fall.
        public void Remove(Animal a)
        {
            if (a == null) return;
            animals.Remove(a);
            int i = owners.IndexOf(a.gameObject);
            if (i >= 0) { owners.RemoveAt(i); bodies.RemoveAt(i); }
        }

        /// Called once the herds are placed: stagger the first tick so a world
        /// of twenty islands doesn't do all its scanning on the same frame.
        public void Finish() => tick = Random.value;

        void Update()
        {
            tick -= Time.deltaTime;
            if (tick > 0f) return;
            tick = 1f;

            var obs = Observer();
            Vector3 d = obs - Centre; d.y = 0f;
            bool want = d.magnitude < gateRange;
            if (want != Active) Gate(want);
            if (!Active) return;

            // Threats, once, for the whole island.
            crew = Object.FindObjectsByType<CrewAgent>(FindObjectsSortMode.None);
            if (Ship == null) Ship = Object.FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
            if (Ship != null)
            {
                ShipAt = Ship.transform.position;
                ShipMoving = Ship.CurrentSpeed > 1f;
            }
            else ShipMoving = false;
        }

        /// The camera is what the player actually judges from; the streamer's
        /// target (the ship) is the fallback for a frame where `Camera.main`
        /// is between assignments.
        Vector3 Observer()
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position;
            var st = Object.FindFirstObjectByType<TerrainStreamer>();
            if (st != null && st.target != null) return st.target.position;
            return Centre;
        }

        void Gate(bool on)
        {
            Active = on;
            for (int i = 0; i < animals.Count; i++)
                if (animals[i] != null) animals[i].enabled = on;
            for (int i = 0; i < gulls.Count; i++)
                if (gulls[i] != null) gulls[i].enabled = on;
            for (int i = 0; i < bodies.Count; i++)
            {
                var rs = bodies[i];
                for (int k = 0; k < rs.Length; k++)
                    if (rs[k] != null) rs[k].enabled = on;
            }
        }
    }
}
