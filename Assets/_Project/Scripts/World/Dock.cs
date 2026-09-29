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
    ///
    /// **T-berth (2026-09-26, Kevin: "pull up to the pier so we're making a
    /// T shape").** She used to lie ALONG the pier, bow seaward -- which
    /// meant swinging round and backing in toward the island on the way
    /// home. Now her centreline runs PERPENDICULAR to the pier's seaward
    /// axis, her side against the pier's sea end (`head`), midship on the
    /// pier's own axis: the pier is the stem of the T, she is the crossbar.
    /// `Berth`/`Heading` stay as the deterministic default (no ship, no
    /// approach to judge by -- a probe, a save's display point, a cold
    /// spawn); `BerthFor`/`HeadingFor` take what a live ship actually knows
    /// (her beam, her heading on the approach) when there is one to ask.
    public class Dock : MonoBehaviour
    {
        /// The home pier's dock: the one the voyage closes on, and the one
        /// the spawn berths at. Null until the world build raises it.
        /// **Switchable** (2026-09-25, Kevin: "make my home berth the pier I
        /// built at island_2") -- see `SetHome`. Everything that reads
        /// `Home`/`IsHome` (`AnchorController.AtHomeDock`/`BerthAtHome`,
        /// `VoyageManager.AtBerth`, `ShipyardService`) follows a move without
        /// its own code changing.
        public static Dock Home
        {
            get => home;
            private set
            {
                home = value;
                // Home is an ISLAND as well as a berth: the island this pier
                // stands on (`Island.IsHome`, `Outpost.Home`, `Stockpile.Instance`
                // all follow it). There is no island that is home by birth any
                // more (2026-09-29): until a pier is named, nothing is.
                Island.SetHome(value != null ? Island.Nearest(value.Berth) : null);
            }
        }
        static Dock home;

        /// **The original harbour's dock**, kept aside once so a chosen pier
        /// that later gets demolished has somewhere to fall back to. Set
        /// once, by the first `Configure` (the world build), and never
        /// itself torn down by `Remove` -- only a runtime pier is.
        static Dock originalHarbour;

        /// True for the one dock the world itself built, never a player's
        /// pier.
        public bool IsOriginalHarbour => this == originalHarbour;

        /// **A label for wherever `Home` is right now**, for a refusal
        /// sentence or a confirmation toast: "the harbour", or "<island> pier".
        public static string HomeLabel
        {
            get
            {
                var d = Home;
                if (d == null) return "her home berth";
                if (d.IsOriginalHarbour) return "the harbour";
                var isle = Island.Nearest(d.Berth);
                return (isle != null ? isle.name : "her home berth") + " pier";
            }
        }

        /// **Move the home berth to `d`.** The old home dock becomes an
        /// ordinary dock; exactly one dock is ever home. A no-op for `null`
        /// or the current home.
        public static void SetHome(Dock d)
        {
            if (d == null || d == Home) return;
            if (Home != null) Home.isHome = false;
            d.isHome = true;
            Home = d;
        }

        /// **Every dock that exists right now**, home included. A pier a
        /// camp raises registers here on enable and leaves on disable, so
        /// tearing the pier down (a load re-raising a ledger, a probe
        /// cleaning up) removes its berth with it.
        public static readonly System.Collections.Generic.List<Dock> All =
            new System.Collections.Generic.List<Dock>();

        /// The dock whose berth is nearest `pos`, flat, or null with none.
        public static Dock Nearest(Vector3 pos)
        {
            Dock best = null;
            float bestD = float.MaxValue;
            foreach (var d in All)
            {
                if (d == null) continue;
                float dd = d.DistanceFrom(pos);
                if (dd < bestD) { bestD = dd; best = d; }
            }
            return best;
        }

        /// Is this the home pier? Set by the world build through `Configure`;
        /// a runtime pier (`Create`) never is, so `Home` cannot wander onto a
        /// camp's jetty however early it is raised.
        public bool IsHome => isHome;
        [SerializeField] bool isHome;

        /// **Stand a dock up at runtime**, for a pier a camp has built.
        ///
        /// `root` is the land end of the pier (where the gangway lands and a
        /// shore party forms up), `head` its sea end, `berth` where the
        /// ship's centre lies when tied up, `seaward` the unit direction
        /// land-to-water along the pier (her heading at the berth: bow out).
        /// `deckY` is the deck's world height and `berthDepth` the water
        /// under the berth. The component goes on `host` -- normally the
        /// pier's own GameObject, so destroying the pier destroys the dock
        /// and unregisters it -- or on a new GameObject under `host == null`.
        /// Tear one down with `Remove`.
        public static Dock Create(GameObject host, Vector3 root, Vector3 head, Vector3 berth,
            Vector3 seaward, float deckY, float berthDepth)
        {
            if (host == null) host = new GameObject("Dock");
            var d = host.AddComponent<Dock>();
            d.isHome = false;
            d.root = root;
            d.head = head;
            d.berth = berth;
            seaward.y = 0f;
            d.seaward = seaward.sqrMagnitude > 1e-6f
                ? new Vector2(seaward.normalized.x, seaward.normalized.z)
                : new Vector2(0f, 1f);
            d.deckY = deckY;
            d.berthDepth = berthDepth;
            return d;
        }

        /// Take a runtime dock down without taking its host with it.
        /// **A home pier may now be demolished** (2026-09-25): `OnDisable`
        /// below returns `Home` to the original harbour when that happens,
        /// the same as any other teardown of the live home dock.
        public static void Remove(Dock d)
        {
            if (d == null) return;
            Destroy(d);
        }

        [SerializeField] Vector3 berth;
        [SerializeField] Vector3 head;
        [SerializeField] Vector3 root;
        [SerializeField] Vector2 seaward;
        [SerializeField] float deckY;
        [SerializeField] float berthDepth;

        /// Where her centre lies when she is tied up, at the default beam
        /// (`WorldScale.ShipBeam`) -- for whoever has no live ship to ask.
        /// `AnchorController` asks `BerthFor` with the real one instead.
        public Vector3 Berth => berth;

        /// Half a beam plus a fender's worth of water: how far beyond the
        /// head her centreline sits so her side can lie against it without
        /// touching.
        const float Fender = 0.9f;

        /// Where her centre lies when tied up, for a hull of this beam:
        /// seaward of the head by half that beam plus a fender, on the
        /// pier's own axis.
        public Vector3 BerthFor(float beamMeters)
        {
            Vector3 sea = Seaward;
            float off = beamMeters * 0.5f + Fender;
            return new Vector3(head.x + sea.x * off, 0f, head.z + sea.y * off);
        }

        /// The two headings she could take at this berth -- her long axis
        /// perpendicular to the pier, bow to one side or the other. Which
        /// one is "A" is arbitrary; it only has to be the same answer every
        /// time, since `Heading` (no ship, no approach) and a stale save's
        /// display point both lean on it.
        Vector3 PerpA
        {
            get
            {
                Vector3 sea = Seaward;
                return new Vector3(sea.z, 0f, -sea.x);
            }
        }
        Vector3 PerpB => -PerpA;

        /// Which way she points at the berth with nothing to judge by: the
        /// deterministic default, `PerpA`. Bow seaward (the old alongside
        /// heading) would sail her straight into the pier head from here,
        /// which is exactly the fault this berth exists to fix.
        public Quaternion Heading => Quaternion.LookRotation(PerpA, Vector3.up);

        /// Which of the two perpendicular headings to take, given her
        /// heading on the approach (flat; zero/degenerate for "no approach
        /// to judge by" -- a spawn, a save restore, the home teleport) --
        /// whichever needs the smaller turn, so she glides in and stops
        /// rather than spinning on the spot.
        public Quaternion HeadingFor(Vector3 approachHeadingFlat)
        {
            approachHeadingFlat.y = 0f;
            if (approachHeadingFlat.sqrMagnitude < 1e-6f) return Heading;
            float da = Vector3.Angle(approachHeadingFlat, PerpA);
            float db = Vector3.Angle(approachHeadingFlat, PerpB);
            return Quaternion.LookRotation(da <= db ? PerpA : PerpB, Vector3.up);
        }

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
            isHome = true;
            if (Home == null) Home = this;
            if (originalHarbour == null) originalHarbour = this;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            if (isHome && Home == null) Home = this;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (Home == this)
            {
                Home = null;
                // The chosen home berth just went away (demolished, or a
                // load tearing down a re-raised ledger mid-rebuild): fall
                // back to the harbour rather than leave `Home` null for
                // whoever asks next frame. Not for the harbour's OWN
                // teardown (a scene unload) -- `originalHarbour` dies with
                // it too, and there is nothing to fall back to.
                if (originalHarbour != null && originalHarbour != this)
                {
                    originalHarbour.isHome = true;
                    Home = originalHarbour;
                }
            }
        }

        /// **How far `pos` is from this pier at all**, flat: the nearer of
        /// the berth and the pier's own root-to-head line. A save written
        /// before the T-berth (2026-09-26) holds the OLD alongside berth,
        /// which lay beside the pier rather than beyond its head; matching
        /// against the pier itself finds her chosen home either way.
        public float DistanceFromPier(Vector3 pos)
        {
            pos.y = 0f;
            Vector3 a = root, b = head; a.y = 0f; b.y = 0f;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f
                ? Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude) : 0f;
            return Mathf.Min(DistanceFrom(pos), Vector3.Distance(pos, a + ab * t));
        }

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
