using UnityEngine;

namespace SeaSick.World
{
    /// **One raised piece of wall: two posts, a line between them, and how
    /// much of it is left.**
    ///
    /// `docs/PLAN-fortress-harbour.md` Phase 1, and Kevin's own siting
    /// design (D5): a run of palisade is not one object, it is a chain of
    /// segments, each of which was its own site in the queue and each of
    /// which can be broken on its own. That is the whole reason this is a
    /// component per segment rather than a mesh per run -- *"raiders
    /// hitting a wall is the moment the vision becomes real"*, and what
    /// they hit has to be a thing with hit points, not a decoration.
    ///
    /// **It is a `Building`.** Not a cousin of one: it is in `Outpost.Built`,
    /// it carries a plan id and a footprint, it is tapped and opens a sheet
    /// like anything else standing in the camp. What it adds is two posts
    /// instead of a centre, a state (whole / breached) and the one thing a
    /// hut never has to do -- tell the pathing grid it is there.
    ///
    /// **The ledger is the truth, as everywhere else.** `Outpost` writes a
    /// `BuiltWall` row when the segment is raised and this keeps that row's
    /// `hp` in step; the object on the ground is destroyed and re-created
    /// freely (an island unloading, a save coming back) and nothing about
    /// the wall is lost by it.
    public class WallSegment : Building
    {
        [SerializeField] Vector3 a, b;
        [SerializeField] bool isGate;
        [SerializeField] float maxHp = 100f;
        [SerializeField] float hp = 100f;

        /// The two posts, world metres, at ground height.
        public Vector3 A => a;
        public Vector3 B => b;
        public bool IsGate => isGate;
        public float MaxHp => maxHp;
        public float Hp => hp;

        /// **Breached, not destroyed.** A broken segment goes on standing
        /// there with its middle third gone: it is the thing the hands
        /// come and repair, and it is the hole the raiders came through.
        /// Removing the object would lose both facts.
        public bool Breached => hp <= 0f;

        /// The camp this belongs to. Set by `Outpost.RaiseWall`; the parent
        /// chain would answer too, but a raid's per-frame code should not be
        /// walking transforms to find out who it is hitting.
        public Outpost Camp { get; set; }

        /// The saved row this keeps in step. Never the only copy of
        /// anything -- see the class note.
        public BuiltWall Row { get; set; }

        /// The two halves of the drawing: which one is showing says whether
        /// this is whole. Swapped rather than rebuilt, because a segment can
        /// be broken and mended several times in one raid.
        Transform whole, broken;

        /// The camp's chain of posts (`WallChain`), which redraws this when
        /// a neighbour changes how its ends fit -- and the fit it was last
        /// drawn with, so it only does when something did change. Null for
        /// the extruded fallback and for a segment with no camp object.
        WallChain chain;
        int fitKey = -1;

        internal int FitKey => fitKey;

        /// Metres from the line within which a point counts as "on" this
        /// segment. Half the post step, so two parallel runs a step apart
        /// are still two runs.
        public const float Reach = 1.0f;

        /// Length of one segment's own hit points. A palisade holds N
        /// raider-seconds (Phase 1); this is the N, in the units
        /// `Damage` is handed. **A guess, never played** -- 40 hp a metre
        /// means a 6 m segment at 240 takes four raiders about twenty
        /// seconds at the 3 hp/s a raider's axe is likely to be worth.
        public const float HpPerMetre = 40f;

        /// A gate is a heavier thing than the fence beside it: it is the
        /// obvious way in and it has to be worth NOT being the obvious way
        /// in. Half again.
        public const float GateHpMultiplier = 1.5f;

        public static float HpFor(float length, bool gate)
        {
            float h = Mathf.Max(1f, length) * HpPerMetre;
            return gate ? h * GateHpMultiplier : h;
        }

        /// Stand one up. Called only by `Outpost.RaiseWall`, which owns the
        /// ledger row and the grid marking that go with it.
        public void Configure(Outpost camp, Vector3 postA, Vector3 postB, bool gate,
            float hitPoints, float maxHitPoints, Transform wholeVisual, Transform brokenVisual)
        {
            Camp = camp;
            a = postA;
            b = postB;
            isGate = gate;
            maxHp = Mathf.Max(1f, maxHitPoints);
            hp = Mathf.Clamp(hitPoints, 0f, maxHp);
            whole = wholeVisual;
            broken = brokenVisual;
            chain = WallChain.Of(transform.parent);
            if (chain != null)
            {
                // The factory drew it a moment ago from this same question.
                fitKey = chain.FitFor(a, b, isGate, this).Key;
                chain.Add(this);
            }
            ShowState();
        }

        /// A new drawing from the chain: the old one goes, the state it
        /// shows carries over.
        internal void Redraw(Transform newWhole, Transform newBroken, int key)
        {
            if (whole != null && whole != newWhole) WallVisual.Kill(whole.gameObject);
            if (broken != null && broken != newBroken) WallVisual.Kill(broken.gameObject);
            whole = newWhole;
            broken = newBroken;
            fitKey = key;
            ShowState();
        }

        void OnDestroy()
        {
            if (chain != null) chain.Remove(this);
        }

        /// The midpoint at ground height -- where a hauler walks to, where
        /// the sheet hangs, and what the row's `x`/`z` are.
        public Vector3 Midpoint => 0.5f * (a + b);

        public float Length => (b - a).magnitude;

        /// How far this point is from the segment, flat. The one test
        /// "is that tap on this wall" and "does this hut overlap it" both
        /// ask.
        public float FlatDistanceTo(Vector3 p) => FlatDistance(a, b, p);

        /// Flat distance from a point to a segment. Static and public
        /// because `Outpost.CanPlaceWall` needs it about lines that are not
        /// standing yet.
        public static float FlatDistance(Vector3 p0, Vector3 p1, Vector3 p)
        {
            Vector2 s = new Vector2(p0.x, p0.z);
            Vector2 e = new Vector2(p1.x, p1.z);
            Vector2 q = new Vector2(p.x, p.z);
            Vector2 d = e - s;
            float len2 = d.sqrMagnitude;
            if (len2 < 1e-6f) return (q - s).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(q - s, d) / len2);
            return (q - (s + t * d)).magnitude;
        }

        /// **The shortest distance between two segments, flat.** What tells
        /// a new wall run it is crossing one that is already there: two
        /// segments that cross have distance zero, two that merely run
        /// beside each other do not.
        public static float FlatDistance(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            if (Crosses(a0, a1, b0, b1)) return 0f;
            float d = FlatDistance(a0, a1, b0);
            d = Mathf.Min(d, FlatDistance(a0, a1, b1));
            d = Mathf.Min(d, FlatDistance(b0, b1, a0));
            d = Mathf.Min(d, FlatDistance(b0, b1, a1));
            return d;
        }

        static float Cross(Vector2 o, Vector2 p, Vector2 q)
            => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);

        static bool Crosses(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            Vector2 p1 = new Vector2(a0.x, a0.z), p2 = new Vector2(a1.x, a1.z);
            Vector2 p3 = new Vector2(b0.x, b0.z), p4 = new Vector2(b1.x, b1.z);
            float d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2);
            float d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
            return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
        }

        /// **Hit it.** Clamps at zero, and the crossing is a one-shot: the
        /// visual breaks, the pathing cells reopen and the camp queues the
        /// repair, once, however many axes land in the same frame.
        public void Damage(float amount)
        {
            if (amount <= 0f || Breached) return;
            hp = Mathf.Max(0f, hp - amount);
            if (Row != null) Row.hp = hp;
            if (hp > 0f) return;

            ShowState();
            // The hole is a hole for everybody: the raiders walk through it
            // and so do the hands going out to mend it.
            var map = Camp != null ? CampPath.For(Camp) : null;
            if (map != null) map.MarkWall(this, false);
            if (Camp != null) Camp.QueueRepair(this);
        }

        /// Put it back up whole. `Outpost.RaiseWall` calls this when a
        /// repair site on these posts is finished, rather than making a
        /// second segment on the same line.
        public void Mend()
        {
            hp = maxHp;
            if (Row != null) Row.hp = hp;
            ShowState();
            var map = Camp != null ? CampPath.For(Camp) : null;
            if (map != null) map.MarkWall(this, true);
        }

        /// **Turn this segment into a gate, or back.** The posts do not
        /// move -- a gate replaces the piece of wall that was there (D5) --
        /// so only the drawing, the hit points and who the cells block
        /// change hands.
        public void BecomeGate(Transform gateWhole, Transform gateBroken)
        {
            var map = Camp != null ? CampPath.For(Camp) : null;
            if (map != null) map.MarkWall(this, false);   // off the map as a WALL

            isGate = true;
            maxHp = HpFor(Length, true);
            hp = maxHp;
            if (Row != null) { Row.isGate = true; Row.hp = hp; Row.maxHp = maxHp; }
            Redraw(gateWhole, gateBroken,
                chain != null ? chain.FitFor(a, b, true, this).Key : -1);
            // The gate's own posts stand on its nodes now (when it fits
            // them), so the chain's posts there come down.
            if (chain != null) chain.MarkDirty();

            if (map != null) map.MarkWall(this, true);    // back on, as a GATE
        }

        /// Take it down for good. The ledger row goes with it and the cells
        /// reopen; nothing is refunded (`WallSheet`'s "Tear down" says so).
        public void TearDown()
        {
            var map = Camp != null ? CampPath.For(Camp) : null;
            if (map != null) map.MarkWall(this, false);
            if (Camp != null) Camp.ForgetWall(this);
            Destroy(gameObject);
        }

        void ShowState()
        {
            if (whole != null) whole.gameObject.SetActive(!Breached);
            if (broken != null) broken.gameObject.SetActive(Breached);
        }
    }
}
