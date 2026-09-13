using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// What has been built on ONE island, where the next thing goes, and how
    /// much that place can keep.
    ///
    /// Was `Village`, and was home-only. It is per-island now because a
    /// campfire on any shore is the same object as the home settlement at an
    /// earlier stage -- **home is outpost zero**. The alternative was two
    /// parallel building systems, and every feature after this one would have
    /// been written twice.
    ///
    /// `Settlement` measures the GROUND -- the largest contiguous piece of
    /// buildable land on the island. This owns what stands on it. They are
    /// separate because the ground is measured once and never changes, while
    /// what is built on it grows across a session.
    ///
    /// **Nothing here flattens anything.** `TerrainHeight.Height` is a pure
    /// function of world position running in Burst jobs on streamed chunks,
    /// so there is no per-building data it could consult and no pad to cut.
    /// A site is CHOSEN on ground that is already flat enough, its corners
    /// are measured, and the building sits at its highest corner with a
    /// footing deep enough to bridge down to its lowest.
    public class Outpost : MonoBehaviour
    {
        /// The home settlement. Kept as a static because the voyage loop is
        /// still berth-to-berth and the panel asks about home specifically --
        /// but home is now one outpost among many, not the only one there can
        /// be. Anything asking about the island the ship is AT must use `Of`,
        /// never this.
        public static Outpost Home { get; private set; }

        static readonly List<Outpost> all = new List<Outpost>();
        public static IReadOnlyList<Outpost> All => all;

        /// Islands whose ground has been surveyed and found wanting.
        ///
        /// A refusal has to be remembered or it is not lazy at all: `Of`
        /// returns null for a refused island exactly as it does for one nobody
        /// has looked at, so without this every anchoring at a barren rock
        /// re-runs the full raster. Cleared with the world -- see
        /// `ForgetSurveys`.
        static readonly HashSet<Island> refused = new HashSet<Island>();

        /// True once the ground here has been looked at, whatever the answer.
        public static bool Surveyed(Island isle)
            => isle != null && (Of(isle) != null || refused.Contains(isle));

        /// Drop what was learned about the old world. Statics outlive play mode
        /// here (domain reload is off), and an island reference from a previous
        /// session is a destroyed object that still hashes.
        public static void ForgetSurveys()
        {
            refused.Clear();
            surveying.Clear();
        }

        [Tooltip("What open ground keeps before anything is built. 30 at home — that is the beach, and the reason to build the first storehouse. ZERO anywhere else: an island with nobody on it keeps nothing, and the campfire is what first gives a place a ceiling at all.")]
        [SerializeField] int openCapacity = 30;

        [Tooltip("Metres between buildings -- room to walk round one, which is what a village looks like from above.")]
        [SerializeField] float spacing = 6f;

        readonly List<Building> built = new List<Building>();
        readonly List<Vector4> reserved = new List<Vector4>();   // xyz = point, w = radius

        Settlement site;
        System.Func<float, float, float> height;
        float minHeight;
        Island island;

        /// Which island this outpost is on. Every lookup keys off this rather
        /// than off an index: islands are discovered by flood-fill in streamer
        /// order, so an index is not an identity.
        public Island Island => island != null ? island : (island = GetComponent<Island>());

        public bool IsHome => Island != null && Island.IsHome;

        /// Surveyed and ready to build on. False on an island whose ground has
        /// been looked at and found wanting -- a crag with no flat.
        public bool Sited => site != null && height != null;

        /// The cleared ground this outpost stands in.
        ///
        /// At home it is handed to the scenery bake as a keep-out BEFORE the
        /// trees go in, because the scenery is several hundred trees welded
        /// into one mesh and there is no taking one out afterwards. Anywhere
        /// else the trees are already standing when the player chooses to
        /// build, so the clearing gets cut at runtime through the harvest path
        /// instead -- which is the better story anyway: making camp fells the
        /// wood it stands on.
        public Vector3 ClearingCentre { get; private set; }
        public float ClearingRadius { get; private set; }

        /// **What this place actually is.** The buildings and the crew you can
        /// see are a rendering of this; see OutpostLedger.
        ///
        /// Seeded when the ground is surveyed, because the worked area is what
        /// sets how much timber is standing within reach.
        [SerializeField] OutpostLedger ledger;
        public OutpostLedger Ledger => ledger;

        /// Bring the ledger up to now. Free to call as often as you like --
        /// the tick advances on a fixed grid of game time, so asking twice in
        /// a frame does nothing the second time.
        public void CatchUp()
        {
            if (ledger == null) return;
            // **One definition of the ceiling, and it is what stands on the
            // ground.** The ledger could have carried its own and drifted from
            // the buildings the moment a storehouse went up; instead the
            // buildings ARE the ledger's ceiling, pushed in before every tick.
            ledger.ceiling = StoreCapacity;
            ledger.Tick(TimeOfDay.Seconds);
        }

        /// Is there a camp here at all, or only ground that would take one?
        /// The fire is the difference.
        public bool HasCamp => CountOf(BuildPlans.Campfire.id) > 0;

        /// Light the fire.
        ///
        /// Returns the logs that came out of the clearing, or -1 if there is
        /// nowhere here to put a camp. **Making camp fells the wood it stands
        /// on** — at home the village clearing is reserved before the scenery
        /// is baked, but on any other island the trees are already standing
        /// when the player decides, so they come down through the same path
        /// the crew fell them by, and the logs go straight into the pile.
        public int MakeCamp() => MakeCamp(out _);

        /// As above, and says WHY when it refuses.
        ///
        /// The first version folded "no ground here", "already a camp" and
        /// "nowhere inside the clearing will take it" into a single -1, which
        /// is a number you cannot debug from. Three refusals that mean
        /// different things must not share a return value.
        public int MakeCamp(out string why)
        {
            if (!Sited) { why = "the ground here was never surveyed"; return -1; }
            if (HasCamp) { why = "there is already a camp here"; return -1; }
            var fire = Raise(BuildPlans.Campfire);
            if (fire == null)
            {
                why = $"nowhere in the {ClearingRadius:F0} m clearing stands level enough "
                    + $"or high enough (needs {minHeight:F1} m)";
                return -1;
            }
            why = "";

            int felled = 0;
            var wood = GetComponentInChildren<Terrain.SceneryWood>();
            if (wood != null)
            {
                // The fire's own footprint plus room to stand round it, not
                // the whole clearing: a camp is a gap in the wood, and
                // stripping thirty metres on the first tap would read as the
                // island being deleted rather than settled.
                felled = wood.FellWithin(ClearingCentre, CampClearingRadius);
            }

            if (ledger != null)
            {
                ledger.SetKey(ClearingCentre);
                ledger.ceiling = StoreCapacity;
                // The clearing's timber goes in the pile, capped by what the
                // fire can keep — the rest is left where it fell.
                ledger.timber = Mathf.Min(ledger.ceiling, ledger.timber + felled);
                ledger.lastTicked = TimeOfDay.Seconds;
            }
            return felled;
        }

        // --- the crew who stay ------------------------------------------------

        /// Leave this hand here.
        ///
        /// **The body is PARKED, not destroyed.** The ledger is what makes the
        /// hand real — it produces whether or not anything is drawn — but
        /// rebuilding a crewman from nothing on the way back would throw away
        /// an authored, named, tinted character to save a deactivated
        /// GameObject. So the body is unparented from the ship, stood at the
        /// camp, and switched off: no Update, no renderer, no physics, and
        /// nothing under it to fall through when the terrain streams out.
        ///
        /// The ship must be told to recount afterwards — see `CrewRoster`.
        public bool Station(Crew.CrewAgent hand)
        {
            if (hand == null || !HasCamp) return false;
            string who = hand.DisplayName;
            if (HandNamed(who) != null) return false;

            // They may be mid-errand ashore with a tree claimed. Drop it
            // first, or the node stays claimed by a body nobody can see and
            // no other hand will ever work it.
            hand.ReturnAboard();

            ledger?.hands.Add(new OutpostHand { name = who, order = OutpostOrder.Cut });

            var t = hand.transform;
            t.SetParent(transform, true);
            Vector3 at = ClearingCentre;
            if (height != null) at.y = height(at.x, at.z);
            // Scatter them a couple of metres round the fire, on the ground
            // plane only -- a sphere would put somebody underneath it.
            Vector2 off = Random.insideUnitCircle * 2.2f;
            at += new Vector3(off.x, 0f, off.y);
            if (height != null) at.y = height(at.x, at.z);
            t.position = at;
            t.rotation = Quaternion.identity;
            hand.gameObject.SetActive(false);
            return true;
        }

        /// Take this hand back aboard. The ledger stops counting them here.
        public bool Recall(Crew.CrewAgent hand, Transform ship)
        {
            if (hand == null || ship == null) return false;
            var row = HandNamed(hand.DisplayName);
            if (row == null) return false;
            ledger.hands.Remove(row);
            hand.transform.SetParent(ship, true);
            hand.gameObject.SetActive(true);
            // The body was MOVED, not walked -- the state machine has to agree
            // with where the transform is rather than try to path to it.
            hand.PutBackOnStation();
            hand.Rest();
            return true;
        }

        public OutpostHand HandNamed(string who)
        {
            if (ledger == null || string.IsNullOrEmpty(who)) return null;
            foreach (var h in ledger.hands) if (h != null && h.name == who) return h;
            return null;
        }

        /// The parked bodies belonging to this outpost, active or not.
        public Crew.CrewAgent[] Parked() => GetComponentsInChildren<Crew.CrewAgent>(true);

        /// Show the hands who live here, or put them away again.
        ///
        /// Called when the ship arrives and when she leaves: a camp you are
        /// standing in front of should have people in it, and a camp three
        /// kilometres astern should cost nothing at all.
        public void ShowHands(bool visible)
        {
            foreach (var a in Parked())
            {
                if (a == null) continue;
                // Only the ones this outpost actually owns. A crewman walking
                // ashore from the ship is parented elsewhere and is not ours
                // to switch off.
                if (HandNamed(a.DisplayName) == null) continue;
                if (a.gameObject.activeSelf != visible) a.gameObject.SetActive(visible);
            }
        }

        /// How much wood a camp clears when it is founded. Room to walk round
        /// the fire and stack what came down, nothing more.
        public const float CampClearingRadius = 7.5f;

        /// Everything this place can keep. Land more than this on one voyage
        /// and the surplus stays on the ground and is not there when you get
        /// back.
        public int StoreCapacity
        {
            get
            {
                int n = openCapacity;
                foreach (var b in built) if (b != null) n += b.StoreCapacity;
                return n;
            }
        }

        public IReadOnlyList<Building> Built => built;

        public int CountOf(string planId)
        {
            int n = 0;
            foreach (var b in built) if (b != null && b.Id == planId) n++;
            return n;
        }

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            if (Home == null && IsHome) Home = this;
        }

        void OnDisable()
        {
            all.Remove(this);
            if (Home == this) Home = null;
        }

        /// The outpost on this island, or null if there is not one yet. Does
        /// NOT survey -- see `Establish`.
        public static Outpost Of(Island isle)
        {
            if (isle == null) return null;
            foreach (var o in all) if (o != null && o.Island == isle) return o;
            return null;
        }

        // --- siting ---------------------------------------------------------

        /// Attach an outpost to ground that has already been surveyed. This is
        /// the home path: the populator measures before the scenery is baked,
        /// so the clearing can be reserved, and hands the result straight in.
        ///
        /// `viewHalfWidth` is passed rather than read off `Dock` because a camp
        /// has no pier. Home passes the docked shot's half-width and gets
        /// exactly the clearing it always had; anywhere else passes its own
        /// overview, so the clearing is sized to the shot the player will
        /// actually be looking at. Zero falls back to the dock's, which keeps
        /// every existing caller behaving identically.
        public void Configure(Settlement settlement, System.Func<float, float, float> terrainHeight,
            float minGroundHeight, float viewHalfWidth = 0f)
        {
            site = settlement;
            height = terrainHeight;
            minHeight = minGroundHeight;

            // Bare ground keeps nothing. Home is the exception and keeps its
            // beach, which is the whole reason its first storehouse exists.
            if (!IsHome) openCapacity = 0;

            // Centre the clearing on a circle that FITS in the buildable
            // patch, not on its centroid: a lobed patch has a centroid that
            // need not be on it at all, and the whole point of this disc is
            // that everything inside it is ground you can build on.
            //
            // And not on the island's BIGGEST such circle either. At home that
            // one is 130 m from the head of the pier, well outside the frame
            // the docked camera holds, so everything raised in it would be
            // invisible at the one moment the player is standing still looking
            // at the place. `VillageAt` is the best clearing inside that frame
            // -- see SettlementSite.Find.
            ClearingCentre = settlement.VillageAt;

            // The clearing is NOT the inscribed circle. That circle guarantees
            // every point in it is buildable, which sounds right and costs the
            // settlement most of its ground: home's best in-frame one is
            // 12.6 m, which holds exactly one storehouse. The guarantee was
            // never needed -- `Corners` tests each building against the actual
            // rectangle it stands on, so a candidate on bad ground is refused
            // whatever the clearing says.
            //
            // So the clearing is sized by what the SHOT holds instead: the
            // measured half-width of the view, less the room a building needs
            // to stand at its edge and still be in it.
            float widest = 0f;
            foreach (var plan in BuildPlans.All)
                widest = Mathf.Max(widest,
                    0.5f * Mathf.Sqrt(plan.footprint.x * plan.footprint.x
                                    + plan.footprint.y * plan.footprint.y));
            float reach = viewHalfWidth > 0.01f ? viewHalfWidth : Dock.ViewHalfWidth;
            ClearingRadius = Mathf.Clamp(reach - widest - 2f, 14f, 30f);

            // A CAMP's clearing is sized by the ground, not by the shot.
            //
            // Home's 30 m comes from what the docked camera holds, and home
            // has the broad flat ground to fill it. An island need not: the
            // measured circle that actually fits inside its buildable patch
            // can be a third of that, and a spiral searching 30 m of a 10 m
            // patch puts every candidate off the good ground and finds
            // nowhere to stand a fire. The floor drops to 4 m for the same
            // reason -- 14 m is a village's minimum, and a camp is a fire.
            if (!IsHome && settlement.VillageClearing > 0.01f)
                ClearingRadius = Mathf.Clamp(
                    Mathf.Min(ClearingRadius, settlement.VillageClearing), 4f, 30f);

            // Seed the ledger off the ground that was just surveyed: how much
            // timber stands within reach is a property of the place, so it
            // belongs to the survey rather than to a constant.
            if (ledger == null)
                ledger = OutpostLedger.For(ClearingCentre, settlement.AreaHectares);
        }

        /// What a NEW outpost is sited under, published once by the world
        /// build.
        ///
        /// Same pattern as `Island.TerrainHeight`, and for the same reason:
        /// the populator is the only thing that knows the terrain's sand
        /// height, and an outpost has to be establishable from wherever the
        /// player decides to make camp. One writer, many readers.
        public struct SiteRules
        {
            public System.Func<float, float, float> height;

            /// Floor the SURVEY works to. Keeps a settlement off the beach:
            /// sand is flat, so it passes a slope test with room to spare and
            /// drags the patch onto the foreshore.
            ///
            /// **Not the same number as `buildFloor`, and conflating them cost
            /// a run.** Home surveys at `sandHeight + 0.5` and refuses
            /// individual buildings at `sandHeight + 1.2`; passing the stricter
            /// one to the survey found no ground at all on five islands out of
            /// five. A survey floor rejects the whole PLACE; a build floor
            /// rejects one shed.
            public float surveyFloor;

            /// Floor an individual building's corners are tested against.
            public float buildFloor;

            /// Half-width of the shot the player will judge the place in.
            public float viewHalfWidth;
            public bool Valid => height != null;
        }
        public static SiteRules Rules;

        /// Survey the island the ship is at and make camp possible there.
        /// Null when the ground will not take a settlement, or when the world
        /// has not published its rules yet.
        public static Outpost Establish(Island isle)
        {
            if (isle == null || !Rules.Valid) return null;
            var existing = Of(isle);
            if (existing != null) return existing;
            if (refused.Contains(isle)) return null;
            return Establish(isle, Rules.height, Rules.surveyFloor, Rules.buildFloor,
                SettlementRadiusFor(isle.Radius),
                Rules.viewHalfWidth > 0.01f ? Rules.viewHalfWidth : Dock.ViewHalfWidth);
        }

        /// Survey an island that was never surveyed at world build, and put an
        /// outpost on it.
        ///
        /// **Lazy on purpose.** `SettlementSite.Find` rasterises the island and
        /// then searches that raster for the largest inscribed circle; doing it
        /// for every island in the discovery radius at world build would be
        /// paid by every player on every launch, for islands most of them will
        /// never land on. It runs when the ship anchors instead -- the player
        /// is stationary and the camera is rising, which is the one moment in
        /// the game with room in the frame budget.
        ///
        /// Returns null when the island has no ground worth building on. That
        /// is a real answer, not a failure: a sandbank that honestly holds
        /// nothing is a better story than a global rule about what may be
        /// settled.
        public static Outpost Establish(Island isle, System.Func<float, float, float> terrainHeight,
            float surveyFloor, float buildFloor, float searchRadius, float viewHalfWidth)
        {
            if (isle == null || terrainHeight == null) return null;

            var existing = Of(isle);
            if (existing != null) return existing;

            // No `preferNear` here, unlike home.
            //
            // Home is sited into the homecoming SHOT -- the best clearing
            // within reach of the pier head, because a village the player
            // never stands still in front of may as well not be there. A camp
            // has no pier and no arrival composition to be sited into, so it
            // takes the island's own best ground and the overview goes to it
            // instead. Passing the island CENTRE as the preferred point was
            // the first attempt and it is worse than nothing: on a lobed
            // island the centre is as likely to be a summit as a field.
            var flat = Terrain.SettlementSite.Find(isle.transform.position, searchRadius,
                terrainHeight, 4f, surveyFloor);
            // Remember the "no". Otherwise a barren rock is re-rastered every
            // time she anchors there, and the laziness buys nothing.
            if (!flat.found) { refused.Add(isle); return null; }

            var settlement = isle.gameObject.AddComponent<Settlement>();
            settlement.Configure(flat);

            var outpost = isle.gameObject.AddComponent<Outpost>();
            outpost.Configure(settlement, terrainHeight, buildFloor, viewHalfWidth);
            return outpost;
        }

        // --- surveying over frames ------------------------------------------

        /// Survey this island WITHOUT spending a frame on it.
        ///
        /// The synchronous `Establish` costs 129 ms on an average island and
        /// **443 ms on the biggest** -- measured, not guessed, and far past the
        /// 16.7 ms a frame has. Almost all of it is the 32 k calls to the
        /// height function that fill the raster; the analysis on top is array
        /// passes and costs a millisecond or two.
        ///
        /// So the raster is filled a band of rows at a time. The moment this
        /// runs is the moment the anchor goes down, and the camera spends
        /// about 0.7 s rising into the overview before the player can act on
        /// anything -- which is more than enough, and means the survey is
        /// finished before there is a question for it to answer.
        ///
        /// Safe to call every frame: a survey already running or already done
        /// is left alone.
        public static void BeginSurvey(Island isle, MonoBehaviour host)
        {
            if (isle == null || host == null || !Rules.Valid) return;
            if (Surveyed(isle) || surveying.Contains(isle)) return;
            surveying.Add(isle);
            host.StartCoroutine(Survey(isle));
        }

        static readonly HashSet<Island> surveying = new HashSet<Island>();

        /// What the last frame-spread survey actually cost, recorded BY the
        /// survey rather than recomputed from its rules. A gate that re-ran the
        /// arithmetic would agree with itself whatever the frames did.
        public static float LastSurveyWorstFrameMs { get; private set; }
        public static int LastSurveyFrames { get; private set; }
        public static float WorstSurveyFrameMs { get; private set; }
        public static void ResetSurveyCost() { WorstSurveyFrameMs = 0f; }

        /// True while the ground here is being looked at but has not answered.
        public static bool Surveying(Island isle) => isle != null && surveying.Contains(isle);

        /// How far out to look for a camp site.
        ///
        /// NOT `HarbourSite.SearchRadiusFor`, which home uses: that one is
        /// sized to find a HARBOUR, so it reaches well past the island to take
        /// in the water and the neighbouring shores, and on a big island it
        /// hits its 900 m ceiling. A camp only cares about the island's own
        /// ground, and the difference is twenty-five times the raster.
        ///
        /// Home is deliberately left on the harbour radius so this change
        /// cannot move the village.
        public static float SettlementRadiusFor(float meanRadius)
            => Mathf.Clamp(meanRadius * 1.35f + 40f, 120f, 420f);

        static System.Collections.IEnumerator Survey(Island isle)
        {
            // Everything Unity-side is read up front: the rest of this runs
            // across frames, and a transform read later would be a different
            // question than the one asked.
            Vector3 centre = isle.transform.position;
            float searchRadius = SettlementRadiusFor(isle.Radius);
            float viewHalfWidth = Rules.viewHalfWidth > 0.01f ? Rules.viewHalfWidth : Dock.ViewHalfWidth;
            var height = Rules.height;
            float surveyFloor = Rules.surveyFloor, buildFloor = Rules.buildFloor;

            const float Cell = 4f;
            int n = Terrain.SettlementSite.Grid(searchRadius, Cell);
            float x0 = centre.x - searchRadius, z0 = centre.z - searchRadius;
            var h = new float[n * n];

            // **The band size is measured, not chosen.**
            //
            // Two guesses at the height function's cost were wrong in opposite
            // directions -- 13 us a sample from one reading, 0.7 us from
            // another, and the truth moves with where you sample, because the
            // erosion fBm does different work over ocean and over land. A
            // constant tuned against either would be wrong on the other, and
            // wrong again on a phone. So: time the first row, then size the
            // band from what it actually cost.
            const float TargetMs = 4f;
            var clock = new System.Diagnostics.Stopwatch();
            int rowsPerFrame = 1;
            float worst = 0f; int frames = 1;

            clock.Restart();
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                    h[j * n + i] = height(x0 + i * Cell, z0 + j * Cell);

                if (j == 0)
                {
                    float rowMs = Mathf.Max(0.001f, (float)clock.Elapsed.TotalMilliseconds);
                    rowsPerFrame = Mathf.Clamp(Mathf.FloorToInt(TargetMs / rowMs), 1, n);
                }

                if ((j + 1) % rowsPerFrame == 0)
                {
                    float ms = (float)clock.Elapsed.TotalMilliseconds;
                    if (ms > worst) worst = ms;
                    yield return null;
                    frames++;
                    clock.Restart();
                }
                // The island can be destroyed mid-survey -- play mode ending,
                // or a world rebuild. Answering about a place that is gone is
                // worse than not answering.
                if (isle == null) { surveying.Remove(isle); yield break; }
            }

            var flat = Terrain.SettlementSite.Solve(h, n, x0, z0, centre, height,
                Cell, surveyFloor, default, 0f);
            float tail = (float)clock.Elapsed.TotalMilliseconds;
            if (tail > worst) worst = tail;

            LastSurveyWorstFrameMs = worst;
            LastSurveyFrames = frames;
            if (worst > WorstSurveyFrameMs) WorstSurveyFrameMs = worst;

            surveying.Remove(isle);
            if (isle == null) yield break;
            if (!flat.found) { refused.Add(isle); yield break; }
            if (Of(isle) != null) yield break;      // something beat us to it

            var settlement = isle.gameObject.AddComponent<Settlement>();
            settlement.Configure(flat);
            var outpost = isle.gameObject.AddComponent<Outpost>();
            outpost.Configure(settlement, height, buildFloor, viewHalfWidth);
        }

        /// Keep something out of the way -- the beacon, the head of the pier.
        /// Reserved before anything is built, so the first storehouse is not
        /// raised on top of them.
        public void Reserve(Vector3 point, float radius)
        {
            reserved.Add(new Vector4(point.x, point.y, point.z, radius));
        }

        /// True where the scenery must not put a tree. Flat distance only --
        /// the clearing is a disc on the map, and the trees it excludes stand
        /// on whatever height the ground has there.
        public bool KeepOut(float x, float z)
        {
            float dx = x - ClearingCentre.x, dz = z - ClearingCentre.z;
            return dx * dx + dz * dz < ClearingRadius * ClearingRadius;
        }

        /// Put the plan up. Null if there is nowhere in the clearing left to
        /// stand it -- the caller pays only if this returns something.
        public Building Raise(BuildPlan plan)
        {
            if (!Sited) return null;

            float len = plan.footprint.x, wid = plan.footprint.y;
            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            float room = ClearingRadius - halfDiag - 1.5f;
            if (room <= 0f) return null;

            // Golden-angle spiral out from the middle: the candidates come in
            // roughly increasing distance from the centre, so the settlement
            // fills from the inside out and reads as one place rather than a
            // ring of sheds.
            const int Tries = 220;
            const float Golden = 2.39996323f;
            for (int i = 0; i < Tries; i++)
            {
                float t = (i + 0.5f) / Tries;
                float r = room * Mathf.Sqrt(t);
                float a = i * Golden;
                Vector3 p = ClearingCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                if (!Clear(p, halfDiag)) continue;

                Vector3 toCentre = ClearingCentre - p;
                toCentre.y = 0f;
                // Door toward the middle of the clearing. A building whose
                // back is to the village is the tell that nobody chose where
                // it went. The plan's ridge runs along local X and the door is
                // in a gable end, so local -X is what has to face in.
                Quaternion facing = toCentre.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(toCentre.normalized, Vector3.up) * Quaternion.Euler(0f, 90f, 0f)
                    : Quaternion.identity;

                if (!Corners(p, facing, len, wid, out float lo, out float hi)) continue;

                p.y = hi;
                var go = BuildingFactory.Raise(plan, transform, p, facing, hi - lo);
                var b = go.GetComponent<Building>();
                built.Add(b);
                reserved.Add(new Vector4(p.x, p.y, p.z, halfDiag + spacing * 0.5f));
                return b;
            }
            return null;
        }

        /// Nothing already claimed within reach of this footprint.
        bool Clear(Vector3 p, float halfDiag)
        {
            foreach (var r in reserved)
            {
                float dx = p.x - r.x, dz = p.z - r.z;
                float need = halfDiag + r.w;
                if (dx * dx + dz * dz < need * need) return false;
            }
            return true;
        }

        /// The four corners of the footprint, measured off the height field.
        /// Rejects ground that is too steep to stand a building on or low
        /// enough to be beach -- the same two tests SettlementSite used to find
        /// the patch, applied to this actual rectangle rather than to the patch
        /// as a whole.
        bool Corners(Vector3 p, Quaternion facing, float len, float wid,
            out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 c = p + facing * new Vector3(sx * len * 0.5f, 0f, sz * wid * 0.5f);
                    float h = height(c.x, c.z);
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
            if (lo < minHeight) return false;
            float span = Mathf.Sqrt(len * len + wid * wid);
            return (hi - lo) / span <= Terrain.SettlementSite.BuildableSlope;
        }
    }
}
