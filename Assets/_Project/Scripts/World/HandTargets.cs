using UnityEngine;

namespace SeaSick.World
{
    /// What the Hand is over, and what letting go there would mean.
    ///
    /// One of these is the answer to both "what do I show" and "what do I
    /// write", which is the point: see `HandTargets.Resolve`.
    public struct HandTarget
    {
        public enum Kind { None, Ground, Tree, Node, Workplace, Blueprint, Fire, Ship, Water }

        public Kind kind;
        /// Where on the ground (or the thing) the drop lands.
        public Vector3 point;
        /// For `Tree` / `Node`: what would be gathered.
        public string resource;
        /// For `Workplace` / `Blueprint`: the plan.
        public string planId;
        /// For `Workplace` / `Fire`: the building itself, so a hand dropped
        /// on the SECOND sawmill walks to the second sawmill.
        public Building building;
        /// What letting go here does, in the player's words. Empty if nothing.
        public string verb;
        /// Why letting go here is refused. Empty means it is allowed.
        public string refusal;

        public bool Allowed => kind != Kind.None && string.IsNullOrEmpty(refusal);

        // --- what the cursor draws round, filled in by `Resolve` -------------

        /// The thing's own centre on the ground, for the highlight ring. Equal
        /// to `point` for bare ground and water.
        public Vector3 centre;
        /// How big the thing is, metres. A circle's radius for a tree, a node
        /// or the ship; half the footprint's diagonal for a building.
        public float extent;
        /// Which way the thing is turned, world degrees, for the buildings
        /// whose highlight is a rectangle rather than a circle.
        public float yaw;
        /// True when the highlight should be that building's own footprint
        /// rather than a circle.
        public bool boxed;
        /// The footprint, metres, when `boxed`.
        public Vector2 footprint;
    }

    /// **The one place that decides what is under the Hand.**
    ///
    /// The preview and the drop both come here, for the reason `Outpost.
    /// CanPlace` serves both the ghost and the raise: a cursor that promises
    /// "gather timber" on one test and a drop that writes something else on
    /// another is the bug this shape exists to prevent.
    ///
    /// Nothing in the world the Hand touches has a collider — not the crew,
    /// not the kit buildings (`BuildingFactory` strips them), not the welded
    /// trees, and the terrain only within a chunk of the ship — so every test
    /// here is geometry against positions the game already knows.
    ///
    /// **Called every frame the cursor moves, so it allocates nothing.** Every
    /// list it would otherwise build is memoised against the thing it was
    /// built from, and every string it hands back is rebuilt only when the
    /// words in it actually change — see `Verb` and `Refuse`. A resolver that
    /// formatted "Tobias — cut timber" sixty times a second would be the most
    /// expensive thing on the island.
    public static class HandTargets
    {
        /// Tunables. Static because the Hand is a runtime-added component and
        /// a serialised field on one of those is never editable.
        public static class Feel
        {
            /// How near the ray has to pass the hull's centre-line, on top of
            /// half her beam, to mean "back aboard". Two metres of slack is
            /// about a stride either side of the rail.
            public static float ShipSlack = 2f;

            /// Nearest a tree or a prop may be to the drop point and still be
            /// what you meant. The floor is a person's reach; the share of the
            /// view is what keeps a wide shot usable, where a tree is four
            /// pixels across and nobody can put a cursor on it.
            public static float PropReachMin = 2.5f;
            public static float PropReachOfView = 0.02f;

            /// How much room a footprint is given before a drop counts as
            /// being "on" the building. A metre: close enough to read as the
            /// same object, far enough that the door is not a pixel hunt.
            public static float FootprintSlack = 1f;
        }

        /// Metres of ground up the frame, as the view is currently set. The
        /// Hand pushes it in; the reach above scales with it, because a target
        /// radius that is honest at 18 m of ground is unusable at 520.
        public static float ViewGround = 165f;

        /// Anything at or under this is sea. The height field goes on
        /// returning sea bed out past every shore, so "the ray hit ground" is
        /// not on its own an answer.
        public const float SeaLevel = 0f;

        public static HandTarget Resolve(Outpost outpost, Crew.CrewAgent held, Ray ray, Transform ship)
        {
            var t = default(HandTarget);
            string who = NameOf(held);

            // **Where the ground is, first — even though the ship is tested
            // first.** The ship test needs to know whether she is in FRONT of
            // the ground along this ray: from a god's-eye view a ray aimed at
            // a tree well inland can still pass within a few metres of her
            // masthead on the way, and "back aboard" appearing while the
            // cursor is over the wood is the one failure this ordering would
            // otherwise buy.
            bool onGround = CameraRig.GroundPick.Along(ray, out Vector3 p);
            float tGround = onGround
                ? Vector3.Dot(p - ray.origin, ray.direction) : float.MaxValue;

            // --- her deck ----------------------------------------------------
            if (Hull(ship, out Vector3 bowEnd, out Vector3 sternEnd, out float hullRadius))
            {
                float d = RayToSegment(ray, bowEnd, sternEnd, out float tShip, out Vector3 onHull);
                if (d <= hullRadius && tShip <= tGround + hullRadius)
                {
                    t.kind = HandTarget.Kind.Ship;
                    t.point = onHull;
                    t.centre = onHull;
                    t.extent = hullRadius;
                    t.verb = Verb(HandTarget.Kind.Ship, who, null);
                    // A villager born at the camp has no berth waiting for
                    // him, so this is the one drop on her deck that can be
                    // refused -- and the refusal is on the cursor before the
                    // player lets go, with the number that explains it.
                    var born = outpost != null ? outpost.HandNamed(who) : null;
                    if (born != null && born.born)
                        t.refusal = Outpost.BerthRefusal(ship);
                    return t;
                }
            }

            // --- the sea -----------------------------------------------------
            if (!onGround || p.y <= SeaLevel)
            {
                t.kind = HandTarget.Kind.Water;
                t.point = onGround ? p : ray.origin + ray.direction * 100f;
                t.point.y = SeaLevel;
                t.centre = t.point;
                t.extent = 1.5f;
                t.refusal = "not in the sea";
                return t;
            }

            t.point = p;
            t.centre = p;
            t.extent = 1.2f;

            // Somebody still on the ship's books can only be put ashore where
            // `Outpost.Station` will take them, and it wants a fire or at
            // least a drawing of one. Worked out once here rather than at each
            // branch, because it refuses every land target alike.
            bool aboard = held != null && outpost != null
                          && outpost.HandNamed(who) == null;
            string cannotLand = null;
            if (outpost == null)
                cannotLand = "no ground here will take a camp";
            else if (aboard && !(outpost.HasCamp || outpost.Building))
                cannotLand = "site a camp first";

            if (outpost == null)
            {
                t.kind = HandTarget.Kind.Ground;
                t.verb = Verb(HandTarget.Kind.Ground, who, null);
                t.refusal = cannotLand;
                return t;
            }

            var ledger = outpost.Ledger;
            float slack = Feel.FootprintSlack;

            // --- the drawing -------------------------------------------------
            //
            // Before the finished buildings, because a blueprint can overlap
            // the ground beside one and the thing you are pointing at is
            // whichever one the player can still do something about.
            if (ledger != null && ledger.pending != null)
            {
                var plan = BuildPlans.Named(ledger.pending.planId);
                Vector3 at = ledger.pending.At;
                at.y = outpost.GroundAt(at);
                if (InFootprint(p, at, ledger.pending.yaw, plan.footprint, slack))
                {
                    t.kind = HandTarget.Kind.Blueprint;
                    t.planId = ledger.pending.planId;
                    t.centre = at;
                    t.yaw = ledger.pending.yaw;
                    t.boxed = true;
                    t.footprint = plan.footprint;
                    t.extent = HalfDiagonal(plan.footprint);
                    t.verb = Verb(HandTarget.Kind.Blueprint, who, plan.label);
                    t.refusal = cannotLand;
                    return t;
                }
            }

            // --- what is standing --------------------------------------------
            var built = outpost.Built;
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null) continue;
                var plan = BuildPlans.Named(b.Id);
                if (plan.footprint.x <= 0f) continue;      // an id nothing knows
                float bYaw = b.transform.eulerAngles.y;
                if (!InFootprint(p, b.transform.position, bYaw, plan.footprint, slack)) continue;

                t.building = b;
                t.planId = b.Id;
                t.centre = b.transform.position;
                t.yaw = bYaw;
                t.boxed = true;
                t.footprint = plan.footprint;
                t.extent = HalfDiagonal(plan.footprint);

                if (plan.kind == BuildKind.Fire)
                {
                    t.kind = HandTarget.Kind.Fire;
                    t.verb = Verb(HandTarget.Kind.Fire, who, null);
                    t.refusal = cannotLand;
                    return t;
                }
                if (BuildPlans.HasPosition(b.Id))
                {
                    // **A mill with nothing to saw is still a place to put a
                    // sawyer.** Refusing here would hide the state the player
                    // most needs to see: a stalled hand standing at a stalled
                    // building is the truth about this camp, and the fix for
                    // it is somewhere else.
                    t.kind = HandTarget.Kind.Workplace;
                    t.verb = Verb(HandTarget.Kind.Workplace, who, plan.label);
                    t.refusal = cannotLand;
                    return t;
                }

                // A hut or a store hut. Nobody works in one.
                t.kind = HandTarget.Kind.Workplace;
                t.verb = Verb(HandTarget.Kind.Workplace, who, plan.label);
                t.refusal = "nothing to work at here";
                return t;
            }

            float reach = Mathf.Max(Feel.PropReachMin, Feel.PropReachOfView * ViewGround);

            // --- a tree --------------------------------------------------------
            var wood = TreeIndex.For(outpost);
            if (wood != null && wood.NearestStanding(p, reach, out Vector3 treeAt))
            {
                t.kind = HandTarget.Kind.Tree;
                t.resource = Res.Timber;
                t.centre = treeAt;
                t.extent = Mathf.Max(1.4f, reach * 0.5f);
                t.verb = Verb(HandTarget.Kind.Tree, who, Res.Timber);
                t.refusal = cannotLand ?? (Gatherable(outpost, Res.Timber)
                    ? null : "nobody can bring timber in here");
                return t;
            }

            // --- a bed of wheat ------------------------------------------------
            // Resolved as a Node carrying Food, so the drop is the same
            // `OrderGather` a stone or a spice prop gets.
            var crops = outpost != null ? outpost.GetComponentInChildren<Terrain.SceneryCrops>() : null;
            if (crops != null)
            {
                int bed = crops.NearestStanding(p, Mathf.Min(reach, 3f));
                if (bed >= 0)
                {
                    t.kind = HandTarget.Kind.Node;
                    t.resource = Res.Food;
                    t.centre = crops.BedAt(bed).at;
                    t.extent = Mathf.Max(1.4f, reach * 0.5f);
                    t.verb = Verb(HandTarget.Kind.Node, who, Res.Food);
                    t.refusal = cannotLand ?? (Gatherable(outpost, Res.Food)
                        ? null : Refuse(Res.Food));
                    return t;
                }
            }

            // --- a goat or a boar ------------------------------------------------
            // Resolved as a Node carrying Game, exactly as a wheat bed is a
            // Node carrying Food: the drop is the same `OrderGather` every
            // other prop gets, and `CampWorker` and the ledger between them
            // turn the claim into meat on the pile.
            //
            // **Before the props, and deliberately.** A goat standing on a
            // boulder is a goat you can put somebody on; the boulder is not
            // going anywhere, and it is the animal the player is pointing at.
            var herd = outpost != null ? outpost.FaunaHere() : null;
            if (herd != null)
            {
                var live = herd.Animals;
                Animal beast = null;
                float bestSq = reach * reach;
                for (int i = 0; i < live.Count; i++)
                {
                    var a = live[i];
                    if (a == null || a.Dead) continue;
                    Vector3 d = a.transform.position - p;
                    d.y = 0f;
                    float m = d.sqrMagnitude;
                    if (m < bestSq) { bestSq = m; beast = a; }
                }
                if (beast != null)
                {
                    t.kind = HandTarget.Kind.Node;
                    t.resource = Res.Game;
                    t.centre = beast.transform.position;
                    t.extent = Mathf.Max(1.4f, reach * 0.5f);
                    t.verb = Verb(HandTarget.Kind.Node, who, Res.Game);
                    t.refusal = cannotLand ?? (Gatherable(outpost, Res.Game)
                        ? null : Refuse(Res.Game));
                    return t;
                }
            }

            // --- ore, stone, spice ---------------------------------------------
            var node = NearestNode(outpost, p, reach);
            if (node != null)
            {
                t.kind = HandTarget.Kind.Node;
                t.resource = node.Resource;
                t.centre = node.transform.position;
                t.extent = Mathf.Max(1.4f, reach * 0.5f);
                t.verb = Verb(HandTarget.Kind.Node, who, node.Resource);
                t.refusal = cannotLand ?? (Gatherable(outpost, node.Resource)
                    ? null : Refuse(node.Resource));
                return t;
            }

            // --- bare ground ---------------------------------------------------
            t.kind = HandTarget.Kind.Ground;
            t.verb = Verb(HandTarget.Kind.Ground, who, null);
            t.refusal = cannotLand;
            return t;
        }

        // =====================================================================
        // Geometry
        // =====================================================================

        /// Is this point inside a footprint standing at `centre`, turned by
        /// `yaw`, grown by `slack` all round?
        ///
        /// The same rectangle `Outpost.Corners` measures the ground at: local
        /// X is the ridge (`footprint.x`) and local Z is across it, so a test
        /// written the other way round would refuse the long sides of every
        /// shed in the camp.
        public static bool InFootprint(Vector3 p, Vector3 centre, float yaw,
            Vector2 footprint, float slack)
        {
            float r = yaw * Mathf.Deg2Rad;
            float s = Mathf.Sin(r), c = Mathf.Cos(r);
            float dx = p.x - centre.x, dz = p.z - centre.z;
            float lx = dx * c - dz * s;
            float lz = dx * s + dz * c;
            return Mathf.Abs(lx) <= footprint.x * 0.5f + slack
                && Mathf.Abs(lz) <= footprint.y * 0.5f + slack;
        }

        public static float HalfDiagonal(Vector2 footprint) =>
            0.5f * Mathf.Sqrt(footprint.x * footprint.x + footprint.y * footprint.y);

        /// Distance from a ray to a segment, with where along each it happened.
        /// One clamp rather than an iterative solve: the segment is a hull and
        /// the tolerance round it is metres, so the exact answer at the ends is
        /// worth nothing.
        public static float RayToSegment(Ray ray, Vector3 p0, Vector3 p1,
            out float tRay, out Vector3 onSegment)
        {
            Vector3 u = ray.direction;
            Vector3 v = p1 - p0;
            Vector3 w = ray.origin - p0;
            float a = Vector3.Dot(u, u);
            float b = Vector3.Dot(u, v);
            float cc = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w);
            float e = Vector3.Dot(v, w);
            float denom = a * cc - b * b;

            float sc;
            if (denom < 1e-6f) sc = 0f;                       // parallel
            else sc = Mathf.Clamp01((a * e - b * d) / denom);

            onSegment = p0 + v * sc;
            tRay = Mathf.Max(0f, Vector3.Dot(u, onSegment - ray.origin) / Mathf.Max(1e-6f, a));
            return Vector3.Distance(ray.origin + u * tRay, onSegment);
        }

        // --- her hull, measured once -----------------------------------------
        //
        // `Shipyard.Refit` already fits a BoxCollider to the rung she is on --
        // beam x depth x length, in her own axes -- so the hull is a fact the
        // ship already knows and this must not invent a second one. The
        // renderer fallback is for a ship that has not been refitted (a probe
        // scene, a stripped test rig), never for the game.

        static Transform hullFor;
        static BoxCollider hullBox;
        static Vector3 hullCentre, hullSize;

        static bool Hull(Transform ship, out Vector3 aft, out Vector3 fwd, out float radius)
        {
            aft = fwd = Vector3.zero;
            radius = 0f;
            if (ship == null) return false;

            if (!ReferenceEquals(ship, hullFor))
            {
                hullFor = ship;
                hullBox = ship.GetComponent<BoxCollider>();
                hullCentre = Vector3.zero;
                hullSize = Vector3.zero;
                if (hullBox == null) MeasureRenderers(ship);
            }
            // `Shipyard.Refit` ADDS the collider, and it may not have run the
            // first time this was asked -- so keep looking rather than living
            // on the renderer fallback for the rest of the session.
            else if (hullBox == null) hullBox = ship.GetComponent<BoxCollider>();

            Vector3 c, s;
            if (hullBox != null) { c = hullBox.center; s = hullBox.size; }
            else { c = hullCentre; s = hullSize; }
            if (s.z <= 0.01f) return false;

            // The centre-line at rail height: a body dropped "on her" is
            // dropped on the deck, not into the bilge.
            float deckY = c.y + s.y * 0.5f;
            float half = s.z * 0.5f;
            aft = ship.TransformPoint(new Vector3(c.x, deckY, c.z - half));
            fwd = ship.TransformPoint(new Vector3(c.x, deckY, c.z + half));
            radius = s.x * 0.5f + Feel.ShipSlack;
            return true;
        }

        static void MeasureRenderers(Transform ship)
        {
            var rs = ship.GetComponentsInChildren<MeshRenderer>(false);
            if (rs == null || rs.Length == 0) return;
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int i = 0; i < rs.Length; i++)
            {
                var b = rs[i].bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3(
                        (k & 1) == 0 ? b.min.x : b.max.x,
                        (k & 2) == 0 ? b.min.y : b.max.y,
                        (k & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 l = ship.InverseTransformPoint(corner);
                    lo = Vector3.Min(lo, l);
                    hi = Vector3.Max(hi, l);
                }
            }
            hullCentre = (lo + hi) * 0.5f;
            hullSize = hi - lo;
            // Yards and a masthead are wider and far taller than the hull.
            // Clamp both against her length, which nothing else stretches.
            hullSize.x = Mathf.Min(hullSize.x, hullSize.z * 0.5f);
            hullSize.y = Mathf.Min(hullSize.y, hullSize.z * 0.35f);
        }

        // =====================================================================
        // The lists, memoised
        // =====================================================================

        static Outpost gatherFor;
        static int gatherStocks = -1;
        static readonly System.Collections.Generic.List<string> gatherable
            = new System.Collections.Generic.List<string>(6);

        /// Can this camp bring this resource in at all? `Outpost.Gatherable`
        /// builds a fresh list every call, which is fine for a menu and not
        /// fine for a cursor, so the answer is cached against the stock list
        /// it came from.
        public static bool Gatherable(Outpost outpost, string resource)
        {
            if (outpost == null || outpost.Ledger == null) return false;
            int n = outpost.Ledger.stocks.Count;
            if (!ReferenceEquals(outpost, gatherFor) || n != gatherStocks)
            {
                gatherFor = outpost;
                gatherStocks = n;
                gatherable.Clear();
                var stocks = outpost.Ledger.stocks;
                for (int i = 0; i < stocks.Count; i++)
                {
                    var st = stocks[i];
                    if (st != null && Res.IsGatherable(st.resource)) gatherable.Add(st.resource);
                }
            }
            for (int i = 0; i < gatherable.Count; i++)
                if (gatherable[i] == resource) return true;
            return false;
        }

        /// Nearest unharvested prop belonging to this island, within reach.
        /// An index loop rather than `foreach`: `ResourceNode.All` is a
        /// `List<T>` and this runs every frame the cursor moves.
        static ResourceNode NearestNode(Outpost outpost, Vector3 at, float reach)
        {
            var isle = outpost != null ? outpost.Island : null;
            var all = ResourceNode.All;
            ResourceNode best = null;
            float bestSq = reach * reach;
            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                if (n == null || n.Harvested) continue;
                if (isle != null && n.Home != isle) continue;
                Vector3 d = n.transform.position - at;
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < bestSq) { bestSq = m; best = n; }
            }
            return best;
        }

        // =====================================================================
        // Words, built only when they change
        // =====================================================================

        /// `CrewAgent.DisplayName` falls back to `UnityEngine.Object.name`,
        /// which allocates a fresh string on every read. One entry is enough:
        /// there is one hand in the Hand.
        static Crew.CrewAgent namedAgent;
        static string namedIs;

        public static string NameOf(Crew.CrewAgent a)
        {
            if (a == null) return null;
            if (ReferenceEquals(a, namedAgent)) return namedIs;
            namedAgent = a;
            namedIs = a.DisplayName;
            return namedIs;
        }

        static HandTarget.Kind verbKind = (HandTarget.Kind)(-1);
        static string verbWho, verbArg, verbIs;

        static string Verb(HandTarget.Kind kind, string who, string arg)
        {
            if (kind == verbKind && Same(who, verbWho) && Same(arg, verbArg)) return verbIs;
            verbKind = kind;
            verbWho = who;
            verbArg = arg;
            bool named = !string.IsNullOrEmpty(who);
            switch (kind)
            {
                case HandTarget.Kind.Tree:
                    verbIs = named ? who + " — cut timber" : "cut timber";
                    break;
                case HandTarget.Kind.Node:
                {
                    // Nobody gathers a goat. Game is the only id in the set
                    // that needs its own verb.
                    string doing = Same(arg, Res.Game) ? "hunt" : "gather " + Lower(arg);
                    verbIs = named ? who + " — " + doing : doing;
                    break;
                }
                case HandTarget.Kind.Workplace:
                    verbIs = named ? "put " + who + " at the " + arg : "the " + arg;
                    break;
                case HandTarget.Kind.Blueprint:
                    // Held: dropping somebody here sets him building it.
                    // Empty-handed: the drawing is tappable, and what the
                    // tap opens is the blueprint's own panel -- what it
                    // still wants, and cancel / move (Kevin, 2026-09-21).
                    // Same shape as the fire's "build", one line below.
                    verbIs = named ? "build the " + arg : "blueprint";
                    break;
                case HandTarget.Kind.Fire:
                    // Held: dropping somebody here idles them by the fire.
                    // Empty-handed: nothing to drop, so the same spot's
                    // affordance is the tap that opens the build menu --
                    // see `BuildMenuRequest` and `Hand.OnGUI`'s hover case.
                    verbIs = named ? who + " — rest by the fire" : "build";
                    break;
                case HandTarget.Kind.Ship:
                    verbIs = "back aboard";
                    break;
                default:
                    verbIs = "set down";
                    break;
            }
            return verbIs;
        }

        static string refuseFor, refuseIs;

        static string Refuse(string resource)
        {
            if (Same(resource, refuseFor)) return refuseIs;
            refuseFor = resource;
            // Game is the one id the general line is not English about: a
            // herd is not something there is "none of worth working". The
            // refusal it needs is about the camp, not about the crag --
            // `Gatherable` only says no here once the stock exists, and the
            // stock exists the moment a herd was ever loaded.
            refuseIs = Same(resource, Res.Game)
                ? "nobody can hunt here yet"
                : "there is no " + Lower(resource) + " worth working here";
            return refuseIs;
        }

        static string lowerOf, lowerIs;

        static string Lower(string s)
        {
            if (s == null) return "";
            if (Same(s, lowerOf)) return lowerIs;
            lowerOf = s;
            lowerIs = s.ToLowerInvariant();
            return lowerIs;
        }

        static bool Same(string a, string b) =>
            ReferenceEquals(a, b) || string.Equals(a, b, System.StringComparison.Ordinal);
    }
}
