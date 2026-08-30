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

        // --- the docked shot, fitted from the framing Kevin flew by hand ---
        //
        // These live on the DOCK because the shot is expressed in the dock's
        // own frame -- the same shot at any pier rather than one island's
        // coordinates -- and because more than one thing needs them now. The
        // camera aims at `ViewCentre`; the village is SITED against it, so
        // that what you come home to is in the frame you come home to. Two
        // copies of 41 m would be a gate that silently stops gating the day
        // one of them is retuned.

        /// How far inland of the pier root the frame is centred.
        const float CentreInlandOfRoot = 41f;
        /// And how far to starboard of the pier's axis.
        const float CentreOffPierAxis = 22f;
        /// Where the camera sits, as a bearing off the pier's seaward
        /// direction. Near enough zero: it looks straight back down the pier.
        const float CameraOffSeaward = 5f;

        /// How far to either side of `ViewCentre` the docked shot actually
        /// holds ground, in PORTRAIT.
        ///
        /// Measured, not derived: ground points projected through the live
        /// camera at the 900x1500 the game ships at came back in frame to
        /// 42 m starboard and 44 m to port, against 252 m inland and 86 m
        /// seaward. **The shot is a long narrow wedge running up the beach,
        /// not a disc** -- and the editor's landscape Game view says
        /// otherwise, which is how the first village got sited 92 m out and
        /// certified as visible. Anything the player is meant to SEE from the
        /// berth has to live inside this number.
        public const float ViewHalfWidth = 42f;

        /// Unit direction, land to water, along the pier.
        public Vector3 Seaward
        {
            get
            {
                var s = new Vector3(seaward.x, 0f, seaward.y);
                return s.sqrMagnitude < 1e-4f ? Vector3.forward : s.normalized;
            }
        }

        /// What the docked camera holds in the middle of the frame: a little
        /// inland of the pier root and off to starboard of its axis, so the
        /// pier runs into the shot from the near edge with the ship on it and
        /// the land lies beyond.
        public Vector3 ViewCentre
        {
            get
            {
                Vector3 sea = Seaward;
                Vector3 starboard = new Vector3(sea.z, 0f, -sea.x);
                Vector3 c = root - sea * CentreInlandOfRoot + starboard * CentreOffPierAxis;
                c.y = 0f;
                return c;
            }
        }

        /// Which way the camera lies off that centre. Essentially straight
        /// out to sea from the pier, looking back down it at the land.
        public Vector3 ViewFrom
        {
            get
            {
                Vector3 sea = Seaward;
                float az = Mathf.Atan2(sea.x, sea.z) * Mathf.Rad2Deg + CameraOffSeaward;
                return new Vector3(Mathf.Sin(az * Mathf.Deg2Rad), 0f, Mathf.Cos(az * Mathf.Deg2Rad));
            }
        }

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
