using UnityEngine;

namespace SeaSick.World
{
    /// A place to tie up. One per island at most, and only home has one for
    /// now.
    ///
    /// The dock owns its own berth rather than letting the mooring code work
    /// one out. `AnchorController.MoorAlongside` berths a ship at
    /// `RadiusAt(bearing) + berthDistance` -- a radial offset from the island
    /// centre -- which is fine for running a boat onto a beach and useless
    /// here: a pier is at a fixed place with a fixed heading, and lying
    /// alongside it means matching both.
    public class Dock : MonoBehaviour
    {
        public static Dock Home { get; private set; }

        [SerializeField] Vector3 berth;
        [SerializeField] Vector3 head;
        [SerializeField] Vector3 root;
        [SerializeField] Vector2 seaward;
        [SerializeField] float deckY;
        [SerializeField] float berthDepth;

        /// Where her centre lies when she is tied up.
        public Vector3 Berth => berth;

        /// Which way she points at the berth: bow seaward, so she can leave
        /// without turning in her own length against the pier.
        public Quaternion Heading => Quaternion.LookRotation(
            new Vector3(seaward.x, 0f, seaward.y), Vector3.up);

        public Vector3 Head => head;
        public Vector3 Root => root;
        public float DeckY => deckY;
        public float BerthDepth => berthDepth;

        /// Where the gangway lands and where a shore party forms up.
        public Vector3 Landing => root;

        public void Configure(Terrain.HarbourSite.Site site, float deck)
        {
            berth = site.berth;
            head = site.head;
            root = site.root;
            seaward = site.seaward;
            berthDepth = site.berthDepth;
            deckY = deck;
        }

        void OnEnable() { if (Home == null) Home = this; }
        void OnDisable() { if (Home == this) Home = null; }

        /// How far off her berth she is, flat. The mooring code eases her in
        /// on this, and the camera uses it to decide she has arrived.
        public float DistanceFrom(Vector3 pos)
        {
            pos.y = 0f;
            var b = berth; b.y = 0f;
            return Vector3.Distance(pos, b);
        }
    }
}
