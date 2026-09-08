using System.Collections.Generic;
using UnityEngine;
using SeaSick.Ocean;

namespace SeaSick.Ship
{
    /// What a bay is being used for. One decision per cell, changed at the
    /// yard, and the only place cargo, guns and crew compete for anything.
    /// What a bay is for. **Ballast is dead weight you WANT** — pig iron and
    /// shingle, bought to be heavy and stowed as low as she'll take it — and
    /// it is the counterweight that turns a high battery from a trap into a
    /// decision.
    public enum BayUse { Empty, Hold, Battery, Quarters, Ballast }

    /// The ship as a growing thing.
    ///
    /// She is never replaced. Every upgrade is a re-loft at the yard -- the
    /// hull mesh changes, her name, her cargo, her crew and her cell
    /// assignments do not. That is the whole point of the modular ladder and
    /// the reason the five-hull swap was thrown out: four moments of progress
    /// across a whole game, three of which asked the player to abandon the
    /// ship they had just earned.
    ///
    /// Nothing here is authored. The rungs come from `ShipLadder`, which is
    /// written by the same script that lofts the meshes.
    [RequireComponent(typeof(Rigidbody))]
    public class Shipyard : MonoBehaviour
    {
        [SerializeField] int nodeIndex = 12;         // the brig, mid-ladder
        [SerializeField] bool applyOnStart = true;
        [SerializeField] bool furnish = true;
        [SerializeField] ShipFit fit = new ShipFit();

        /// The fittings, bought separately from the hull and carried across
        /// every rung of the ladder.
        public ShipFit Fit => fit;

        /// What is aboard and where it sits. Rebuilt whenever the hull, the
        /// bays or the cargo change, because all three move her mass.
        public ShipLoad Load { get; private set; }
        int lastCargo = -1;

        /// Children that belong to the paddle steamer's HULL rather than to the
        /// ship, and must step aside with her. Her lanterns are the ones that
        /// caught us out; the ladder hulls carry no lamps of their own and get
        /// them from the kit, at the bays the player gave to quarters.
        static readonly string[] PaddleOnly =
        {
            "PaddleBoatVisual", "LanternBow_Pivot", "LanternStern_Pivot",
        };

        // Fallback only: `HydrostaticLayout` computes the real one per rung
        // from the rig it builds (0.567 across the ladder, by construction).
        const float FloatRatio = 0.60f;   // mass / (rho * total volume)
        const float TunedMass = 19200f;   // what the drag coefficients were tuned at
        // One copy of the anchor hull's length, owned by ShipMotor. Three
        // tables of the same numbers is how the steamer ended up at a sixth of
        // her displacement; this is not going to be a fourth.
        const float TunedLoa = ShipMotor.TunedLoa;

        // --- what the player has decided ------------------------------------
        readonly Dictionary<string, BayUse> cells = new Dictionary<string, BayUse>();
        Transform visual, furniture;

        public int NodeIndex => nodeIndex;
        public LadderNode Node => ShipLadder.Node(nodeIndex);
        public string Status { get; private set; } = "";

        // --- the readouts the player actually plays against -------------------
        // A cell of hold carries 3; a cell of battery is a gun position each
        // side worked by ONE crew of two, which is how a real ship fought --
        // you rarely engage both broadsides at once; a cell of quarters berths
        // one hand. Crew lands on 20 at the three-decker by itself, because
        // guns eat berths and stores burn as crew x days.
        public const int CargoPerHold = 3;
        public const int CrewPerGunCell = 2;
        public const int CrewPerQuarters = 1;

        /// Hands a battery cell needs, by how well drilled the crew is.
        ///
        /// **This is the reason a first-rate can be a first-rate.** An untrained
        /// ship costs two hands a gun cell, and against a roster that stops at
        /// twenty that caps her at ten guns a side — twenty cannon, on a hull
        /// with thirty-six ports. The cast of twenty is not negotiable; what a
        /// real ship did instead was DRILL, until one crew served two guns by
        /// crossing the deck between broadsides, which is exactly why you rarely
        /// engaged both sides at once.
        ///
        /// Tied to CREW training and not to the gun track on purpose: the gun
        /// track is calibre, and a heavier gun needs MORE men, not fewer.
        /// Reading it off calibre would have said the opposite of the truth.
        public static float CrewPerGun(int crewLevel) => crewLevel switch
        {
            <= 0 => 2f,
            1 => 1.5f,
            2 => 1f,
            _ => 0.5f,
        };

        public float CrewPerGunNow => CrewPerGun(fit.Level(FitTrack.Crew));

        public int Cargo => Count(BayUse.Hold) * CargoPerHold;
        public int Guns => Count(BayUse.Battery);
        public int Berths => Count(BayUse.Quarters) * CrewPerQuarters;
        public int CrewNeeded => Mathf.CeilToInt(Count(BayUse.Battery)
                                                 * CrewPerGunNow);
        public int Crew => Mathf.Min(Berths, int.MaxValue);
        public bool Undermanned => CrewNeeded > Berths;

        /// The biggest battery she could carry FULLY MANNED — a gun and the
        /// berths to work it, inside her cells, her ports and the roster.
        /// Nothing enforces it: the yard will happily fit guns she cannot man
        /// and say so. It is what the panel quotes and what the ladder is
        /// balanced against.
        public int MaxGunsManned
        {
            get
            {
                var n = Node;
                if (n == null || n.gun_rows <= 0) return 0;
                float c = CrewPerGunNow;
                int byCrew = Mathf.FloorToInt(CrewCeiling / c);
                int byCells = Mathf.FloorToInt(n.cells / (1f + c));
                return Mathf.Max(0, Mathf.Min(byCrew, byCells,
                                              n.ports_per_side));
            }
        }

        /// The roster ceiling, and it is a CASTING decision: every hand has a
        /// name, a role and an iron stomach, so twenty is as many people as
        /// this game can afford to have be someone.
        public const int CrewCeiling = 20;

        public int Count(BayUse u)
        {
            // Empty is the ABSENCE of a decision, so it cannot be counted the
            // same way: a cell nobody has touched is not in the dictionary at
            // all, and asking for Empty here honestly answers "none of the
            // decisions made were Empty". Use `EmptyCells`.
            if (u == BayUse.Empty) return EmptyCells;
            int n = 0;
            foreach (var kv in cells) if (kv.Value == u) n++;
            return n;
        }

        /// Bays with nothing decided for them — what the "+" buttons have left
        /// to spend, and the whole reason to buy a bigger hull.
        public int EmptyCells
        {
            get
            {
                var n = Node;
                if (n == null) return 0;
                int used = 0;
                foreach (var kv in cells) if (kv.Value != BayUse.Empty) used++;
                return Mathf.Max(0, n.cells - used);
            }
        }

        public static string Key(string bay, string tier) => bay + "_" + tier;

        public BayUse Use(string bay, string tier)
        {
            cells.TryGetValue(Key(bay, tier), out var u);
            return u;
        }

        public void SetUse(string bay, string tier, BayUse u)
        {
            cells[Key(bay, tier)] = u;
            if (furnish) Furnish();
            PushToGame();
        }

        /// Cycle a cell: empty -> hold -> battery -> quarters -> ballast ->
        /// empty.
        ///
        /// Battery is skipped where there is no gun port. The free board lets
        /// the player put any cell to any use, but "any use" cannot include a
        /// gun in a bay with solid planking outside it — she carries ten ports
        /// a side and forty-eight cells.
        public void CycleUse(string bay, string tier)
        {
            var next = (BayUse)(((int)Use(bay, tier) + 1) % 5);
            if (next == BayUse.Battery && !CanBearGun(bay, tier))
                next = BayUse.Quarters;
            SetUse(bay, tier, next);
        }

        /// Can this cell carry a gun — is it on a gun deck, at a port?
        public bool CanBearGun(string bay, string tier)
        {
            var n = Node;
            if (n == null || n.gun_rows <= 0) return false;
            int bi = System.Array.IndexOf(n.bay_labels, bay);
            int ti = System.Array.IndexOf(n.tier_names, tier);
            if (bi < 0 || ti < 0) return false;
            return HasPort(n, bi, ti);
        }

        // --- buying space by the bay -----------------------------------------
        //
        // The board below lets the player put any cell to any use, and that
        // stays: it is the detailed view. These are the BUTTONS — "one more
        // gun", "one more hand" — and they exist because the ask is "how many
        // guns has she", not "which of the thirty-six boxes is a gun".
        //
        // They also know something the free board does not: WHERE a use
        // belongs. A gun goes on a gun deck, low, amidships. A berth goes on
        // the weather deck, so the hand standing in it is a hand you can see.
        // Everything below follows from those two sentences.

        /// Tiers that carry gun ports, lowest first.
        ///
        /// `gun_rows` is stacked DOWN from the weather deck, so the gun decks
        /// are the top `gun_rows` tiers and anything under them is hold. A
        /// single-tier hull has `gun_rows == 0` and no battery at all — which
        /// is the honest answer to why a fishing skiff has no cannon, and the
        /// same fact `CannonBattery` learned the hard way.
        IEnumerable<int> GunTiers(LadderNode n)
        {
            for (int t = n.tiers - n.gun_rows; t < n.tiers; t++)
                if (t >= 0) yield return t;
        }

        /// How many guns this tier has ports for.
        ///
        /// **A gun deck is not a row of bays, it is a row of PORTS**, and the
        /// two stopped being the same thing when the generator began cutting
        /// one port per gun she can man rather than one per bay. A first-rate
        /// has twelve bays on each of three gun decks and ten ports in total;
        /// fitting an eleventh gun would run a carriage out at solid planking.
        int PortsOn(LadderNode n, int ti)
        {
            int r = ti - (n.tiers - n.gun_rows);
            if (r < 0) return 0;
            if (n.ports_per_row != null && r < n.ports_per_row.Length)
                return n.ports_per_row[r];
            // An older manifest has no per-row list. Fall back to the total
            // rather than to the bay count, so the cap is never too generous.
            return n.gun_rows > 0 ? n.ports_per_side / n.gun_rows : 0;
        }

        /// Is there a gun port at this bay on this tier?
        ///
        /// The generator cuts a tier's ports amidships out — the `k` bays with
        /// the smallest `abs(bay_x)`, ties by index — and that is this
        /// expression, on these numbers. Both sides compute it from `bay_x`
        /// rather than one of them shipping a list, so there is no list to
        /// fall out of date with the mesh.
        bool HasPort(LadderNode n, int bi, int ti)
        {
            int ports = PortsOn(n, ti);
            if (ports <= 0) return false;
            if (ports >= n.bay_x.Length) return true;
            int ahead = 0;
            float mine = Mathf.Abs(n.bay_x[bi]);
            for (int k = 0; k < n.bay_x.Length; k++)
            {
                if (k == bi) continue;
                float d = Mathf.Abs(n.bay_x[k]);
                if (d < mine || (d == mine && k < bi)) ahead++;
            }
            return ahead < ports;
        }

        /// Where a use would go next, best first. Empty when there is nowhere.
        ///
        /// Guns: the LOWEST gun deck, and amidships before the ends — a gun is
        /// the heaviest thing aboard and the two places you least want it are
        /// high and over an end.
        ///
        /// Berths: the weather deck first, working down. Hammocks belong on
        /// the gun deck historically, but a hand stands at his bay's FLOOR and
        /// a floor below the weather deck puts him inside the hull; until the
        /// hatches are cut, up is where he can be seen.
        List<(string bay, string tier)> Candidates(LadderNode n, BayUse use)
        {
            var outp = new List<(string, string, float)>();
            bool guns = use == BayUse.Battery;
            // Ballast is the one other thing that wants to be as far DOWN as
            // she will take it — that is the entire reason to buy it. Ranking
            // it with cargo would have put pig iron on the weather deck.
            bool low = guns || use == BayUse.Ballast;
            var tiers = guns ? new List<int>(GunTiers(n)) : null;

            for (int ti = 0; ti < n.tier_names.Length; ti++)
            {
                if (guns && !tiers.Contains(ti)) continue;
                if (guns && PortsOn(n, ti) <= 0) continue;
                // Rank: guns and ballast want the lowest tier, everything
                // else the highest.
                float tierRank = low ? ti : (n.tier_names.Length - 1 - ti);
                for (int bi = 0; bi < n.bay_labels.Length; bi++)
                {
                    if (Use(n.bay_labels[bi], n.tier_names[ti]) != BayUse.Empty) continue;
                    // A gun goes where there is a hole to run it out of, and
                    // she has far fewer of those than she has bays.
                    if (guns && !HasPort(n, bi, ti)) continue;
                    // Amidships first for weight; the ends fill last.
                    float fromMidships = Mathf.Abs(n.bay_x[bi]);
                    outp.Add((n.bay_labels[bi], n.tier_names[ti],
                              tierRank * 1000f + fromMidships));
                }
            }
            outp.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            return outp.ConvertAll(e => (e.Item1, e.Item2));
        }

        /// Why one more of this cannot be fitted, or null. The reason is
        /// always a fact about the ship, never "you may not".
        public string WhyNotAdd(BayUse use)
        {
            var n = Node;
            if (n == null) return "no hull";
            if (use == BayUse.Battery && n.gun_rows <= 0)
                return "she has no gun deck to run a carriage out on. Raise her.";
            if (Candidates(n, use).Count == 0)
                return use == BayUse.Battery
                    ? $"she has {n.ports_per_side} gun ports a side and a gun "
                      + "in every one. Lengthen or raise her."
                    : "she has no empty bay left. Lengthen or raise her.";
            return null;
        }

        /// Fit one more bay of this use. Returns false with a reason on
        /// `Status` when the hull cannot take it.
        public bool AddCell(BayUse use)
        {
            string why = WhyNotAdd(use);
            if (why != null) { Status = why; return false; }
            var pick = Candidates(Node, use)[0];
            SetUse(pick.bay, pick.tier, use);
            Status = $"{pick.bay} {pick.tier} → {use}";
            return true;
        }

        /// Give one back. Takes the WORST-placed cell first — the reverse of
        /// the order they were fitted in — so stripping a gun leaves the
        /// battery low and amidships instead of scattered.
        public bool RemoveCell(BayUse use)
        {
            var n = Node;
            if (n == null || Count(use) == 0)
            { Status = $"she carries no {use} to give up"; return false; }

            string bestBay = null, bestTier = null;
            float worst = float.NegativeInfinity;
            bool low = use == BayUse.Battery || use == BayUse.Ballast;
            for (int ti = 0; ti < n.tier_names.Length; ti++)
            {
                float tierRank = low ? ti : (n.tier_names.Length - 1 - ti);
                for (int bi = 0; bi < n.bay_labels.Length; bi++)
                {
                    if (Use(n.bay_labels[bi], n.tier_names[ti]) != use) continue;
                    float rank = tierRank * 1000f + Mathf.Abs(n.bay_x[bi]);
                    if (rank <= worst) continue;
                    worst = rank;
                    bestBay = n.bay_labels[bi];
                    bestTier = n.tier_names[ti];
                }
            }
            if (bestBay == null) return false;
            SetUse(bestBay, bestTier, BayUse.Empty);
            Status = $"{bestBay} {bestTier} cleared";
            return true;
        }

        void Start()
        {
            // `visual == null` guards against clobbering a rung something else
            // already applied. AddComponent runs Start at the END of the frame,
            // so a probe that adds the yard and immediately calls Apply(3) had
            // its hull quietly replaced by the serialized default a moment
            // later, and every rig in the row came out the same ship.
            if (applyOnStart && visual == null) Apply(nodeIndex);
        }

        // --- the three moves ---------------------------------------------------

        public bool CanMove(string move) => ShipLadder.Blocked(nodeIndex, move) == null;
        public string WhyNot(string move) => ShipLadder.Blocked(nodeIndex, move);

        public bool Move(string move)
        {
            if (!CanMove(move)) { Status = WhyNot(move); return false; }
            Apply(nodeIndex + 1);
            return true;
        }

        public void Lengthen() => Move("lengthen");
        public void Girdle() => Move("girdle");
        public void Raise() => Move("raise");

        /// Step back down the ladder. A dev convenience, not a game action --
        /// no yard ever shortened a ship.
        public void Undo()
        {
            if (nodeIndex > 0) Apply(nodeIndex - 1);
        }

        // --- applying a rung ----------------------------------------------------

        public void Apply(int index)
        {
            var n = ShipLadder.Node(index);
            if (n == null) { Status = "no such rung"; return; }
            nodeIndex = index;

            SwapVisual(n);
            Refit(n);
            PruneCells(n);
            if (furnish) Furnish();
            PushToGame();

            Status = $"{n.label} — {n.length:F1} x {n.beam:F1} m, "
                   + $"{n.mass_kg / 1000f:F0} t, {n.cells} cells";
        }

        void SwapVisual(LadderNode n)
        {
            // Clear EVERY hull visual, not just the one this component made.
            //
            // `SetupFleetShip.Wear()` leaves a child called "FleetVisual" in the
            // scene, and it is serialised — so a ship that had a T1 raft worn in
            // the editor kept that raft under every rung the yard applied, and
            // the skiff came out looking like a hull sitting on a raft. Owning
            // only your own leftovers is not the same as starting clean.
            if (visual != null) Discard(visual.gameObject);
            foreach (var stale in new[] { "FleetVisual", "HullVisual" })
            {
                var t = transform.Find(stale);
                while (t != null)
                {
                    Discard(t.gameObject);
                    t = transform.Find(stale);
                }
            }
            // The steamer's own fittings are SIBLINGS of her visual, not
            // children of it, so hiding the visual left her two lanterns
            // hanging in mid-air beside whatever hull replaced her. Anything
            // that belongs to her hull and not to the ship goes with it.
            foreach (var only in PaddleOnly)
            {
                var t = transform.Find(only);
                if (t != null) t.gameObject.SetActive(false);
            }
            var drive = GetComponent<PaddleDrive>();
            if (drive != null) drive.enabled = false;

            var src = Resources.Load<GameObject>(n.ResourcePath);
            if (src == null)
            {
                Debug.LogError($"Shipyard: no hull at Resources/{n.ResourcePath}");
                return;
            }
            var go = Instantiate(src, transform);
            go.name = "HullVisual";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            visual = go.transform;
        }

        void Refit(LadderNode n)
        {
            var rb = GetComponent<Rigidbody>();
            RebuildLoad(n);
            rb.mass = Load.TotalKg;
            float k = rb.mass / TunedMass;

            // The rig is SOLVED from her hydrostatics, so both of the
            // constants this used to need are gone: `probe_lift` was a
            // once-solved ratio that made her float right, and `FloatRatio`
            // was the rig's volume expressed as a guess. Reproduce her
            // measured volume, waterplane, second moment and centre of
            // buoyancy and both fall out -- see `HydrostaticLayout`.
            var probes = GetComponent<BuoyancyProbeSet>();
            float floatRatio = FloatRatio;
            if (probes != null)
            {
                probes.SetProbes(BuoyancyProbeSet.HydrostaticLayout(
                    n.length, n.beam, n.draft, n.RailY,
                    n.volume_m3, n.waterplane_m2, n.bm_m, n.kb_above_keel_m,
                    out float capacity));
                floatRatio = n.volume_m3 / Mathf.Max(0.01f, capacity);
            }

            var buoy = GetComponent<BuoyantBody>();
            if (buoy != null)
            {
                buoy.ConfigureForHull(
                    n.volume_m3, floatRatio, k, n.length / TunedLoa,
                    new Vector3(n.beam, n.depth, n.length * 0.9f),
                    // The centre of gravity is COMPUTED from what is aboard,
                    // not a fixed fraction of her depth. Guns on an upper deck
                    // raise it and ballast pulls it down, so how the player
                    // fills the bays decides how stiff she is.
                    Load.CentreOfMassLocal);
                // Burial clamp, reserve, planing lift and plow — the
                // metre-denominated thresholds, rescaled to HER freeboard and
                // draft. The legacy fleet hulls and the steamer never come
                // through here and keep their scene-tuned values.
                buoy.ConfigureWaveResponse(n.RailY, n.draft,
                    Mathf.Sqrt(n.length / TunedLoa));
            }

            var motor = GetComponent<ShipMotor>();
            if (motor != null)
            {
                // No mastPivot: SailRig owns every sail, because one Transform
                // field cannot turn three masts about three different points.
                motor.ConfigureForHull(n.length, n.RailY, true, null);
                // A smaller hull cannot carry every fitting a bigger one could.
                fit.ClampTo(n);
                motor.ApplyFit(fit.SpeedMultiplier, fit.TurnMultiplier,
                               fit.AccelMultiplier);
            }

            var rig = GetComponent<SailRig>();
            if (rig == null) rig = gameObject.AddComponent<SailRig>();
            rig.Fit(visual);
            rig.SetArea(fit.SailAreaScale);

            // Every port her decks allow is cut in the mesh and every lid is
            // baked shut; this is what decides how many of them are open. Fit
            // it here, with the hull it belongs to, and re-Open it wherever the
            // battery changes.
            var hatches = GetComponent<PortLids>();
            if (hatches == null) hatches = gameObject.AddComponent<PortLids>();
            hatches.Fit(visual, GunPositions(Node));
        }

        /// A raise adds a tier and a lengthening adds bays amidships. Cells
        /// that no longer exist are dropped; NEW cells arrive unassigned, so
        /// every upgrade ends with the player standing in new empty space
        /// deciding what it is for.
        void PruneCells(LadderNode n)
        {
            var live = new HashSet<string>();
            foreach (var b in n.bay_labels)
                foreach (var t in n.tier_names)
                    live.Add(Key(b, t));
            var drop = new List<string>();
            foreach (var kv in cells) if (!live.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var k in drop) cells.Remove(k);

            // A battery cell has to keep its PORT as well as its bay. A
            // lengthening inserts bays amidships and pushes the run of ports
            // outward, so a gun that was at a port before the upgrade can find
            // itself behind solid planking after it. It becomes empty space
            // rather than an invisible gun: the player is told what she is by
            // the panel, and a gun that is not there must not be counted, fed
            // or weighed.
            for (int bi = 0; bi < n.bay_labels.Length; bi++)
                for (int ti = 0; ti < n.tier_names.Length; ti++)
                {
                    if (Use(n.bay_labels[bi], n.tier_names[ti]) != BayUse.Battery)
                        continue;
                    if (!HasPort(n, bi, ti))
                        cells[Key(n.bay_labels[bi], n.tier_names[ti])] = BayUse.Empty;
                }
        }

        /// Recount what is aboard. Cargo comes from the voyage, crew and guns
        /// from the bays, ballast from what is left over.
        public void RebuildLoad(LadderNode n)
        {
            // A gun stands on its deck, a hand stands on his, and pig iron
            // lies on the deck below whatever is on top of it.
            var gunAt = CentreOf(n, BayUse.Battery, 0.47f);
            var berthAt = CentreOf(n, BayUse.Quarters, 0.90f);
            var ironAt = CentreOf(n, BayUse.Ballast, 0.35f);

            Load = new ShipLoad(n)
            {
                Crew = Mathf.Max(1, Berths),
                Guns = Guns * 2,          // a battery cell is a gun each side
                GunLevel = fit.Level(FitTrack.Guns),
                GunKGm = MeanGunHeight(n),
                GunLCGm = gunAt.z,
                CrewKGm = berthAt.h,
                CrewLCGm = berthAt.z,
                BallastCells = Count(BayUse.Ballast),
                BallastKGm = ironAt.h,
                BallastLCGm = ironAt.z,
                FullCargoUnits = Cargo,
            };
            var voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            Load.CargoUnits = voyage != null ? voyage.TotalHeld : 0;
            var stow = StowCargo(n, Load.CargoUnits);
            Load.CargoKGm = stow.h;
            Load.CargoLCGm = stow.z;
            lastCargo = Load.CargoUnits;
            var bilge = GetComponent<Bilge>();
            if (bilge != null) Load.FloodTonnes = bilge.Bilge01 * n.volume_m3 * 0.08f;
        }

        /// Mean height of the battery above the keel. A gun on an upper deck is
        /// the most destabilising thing aboard, so this is what carries that
        /// decision into the physics.
        float MeanGunHeight(LadderNode n)
        {
            float sum = 0f; int c = 0;
            for (int bi = 0; bi < n.bay_labels.Length; bi++)
                for (int ti = 0; ti < n.tier_names.Length; ti++)
                    if (Use(n.bay_labels[bi], n.tier_names[ti]) == BayUse.Battery)
                    { sum += n.tier_floor[ti] + n.draft + 0.47f; c++; }
            return c > 0 ? sum / c : 0f;
        }

        /// Every cell put to this use, LOWEST first and then from amidships
        /// outward — the order anything heavy actually gets stowed in, and the
        /// order that makes "cargo in the bottom centre" the well-balanced
        /// answer it ought to be.
        List<(int bi, int ti)> CellsLowestFirst(LadderNode n, BayUse use)
        {
            var outp = new List<(int, int, float)>();
            for (int ti = 0; ti < n.tier_names.Length; ti++)
                for (int bi = 0; bi < n.bay_labels.Length; bi++)
                    if (Use(n.bay_labels[bi], n.tier_names[ti]) == use)
                        outp.Add((bi, ti, ti * 1000f + Mathf.Abs(n.bay_x[bi])));
            outp.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            return outp.ConvertAll(e => (e.Item1, e.Item2));
        }

        /// The centre of a use's cells: height above the keel, and station.
        (float h, float z) CentreOf(LadderNode n, BayUse use, float sitting)
        {
            var list = CellsLowestFirst(n, use);
            if (list.Count == 0) return (0f, 0f);
            float h = 0f, z = 0f;
            foreach (var (bi, ti) in list)
            { h += n.TierHeightAboveKeel(ti) + sitting; z += n.bay_x[bi]; }
            return (h / list.Count, z / list.Count);
        }

        /// Where the cargo actually aboard ends up.
        ///
        /// **Not the mean of every hold cell she owns.** Six barrels in an
        /// eighteen-cell hold are stowed in the six lowest of them, and a hold
        /// that is a third full therefore sits lower than one that is brim
        /// full — which is the difference between a ship that is stiff and one
        /// that is merely deep. Averaging the cells she OWNS would have thrown
        /// that away and made the hold a single number again.
        ///
        /// Anything over what her hold will take goes ON DECK, at the height
        /// of her topmost tier, because that is where it goes in life and it
        /// is exactly where it hurts most.
        (float h, float z) StowCargo(LadderNode n, int units)
        {
            if (units <= 0) return (0f, 0f);
            float m = 0f, mh = 0f, mz = 0f;
            int left = units;
            foreach (var (bi, ti) in CellsLowestFirst(n, BayUse.Hold))
            {
                int put = Mathf.Min(left, CargoPerHold);
                float h = n.TierHeightAboveKeel(ti) + 0.60f;
                m += put; mh += put * h; mz += put * n.bay_x[bi];
                left -= put;
                if (left <= 0) break;
            }
            if (left > 0)
            {
                int top = n.tier_names.Length - 1;
                float h = n.TierHeightAboveKeel(top) + 0.60f;
                m += left; mh += left * h; mz += left * n.BayMeanX;
            }
            return m > 0f ? (mh / m, mz / m) : (0f, 0f);
        }

        void Update()
        {
            // Cargo is loaded a unit at a time by the shore party, so her mass
            // changes without anything calling into the yard. Watch it.
            var voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            if (voyage == null || Node == null) return;
            if (voyage.TotalHeld == lastCargo) return;
            ApplyLoad();
        }

        /// Push the loading into the physics. Mass, centre of gravity, and the
        /// sinkage readout — nothing else, because everything else follows.
        public void ApplyLoad()
        {
            var n = Node;
            if (n == null) return;
            RebuildLoad(n);
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = Load.TotalKg;
                rb.centerOfMass = Load.CentreOfMassLocal;
            }
            var motor = GetComponent<ShipMotor>();
            if (motor != null) motor.SinkDepth = Load.SinkageM;
        }

        /// Hand the bay decisions to the systems that already exist.
        ///
        /// Without this the panel is a viewer: you can move the ship up twenty
        /// rungs and the voyage loop still thinks she holds the 40 somebody
        /// typed into the scene, and her battery is still the four guns
        /// `CannonBattery` builds by default. The numbers only become a game
        /// when something consumes them.
        /// Buy the next level of a fitting. Returns false, with a reason on
        /// `Status`, when the hull cannot carry it.
        public bool Upgrade(FitTrack track)
        {
            var n = Node;
            string why = fit.Blocked(track, n);
            if (why != null) { Status = why; return false; }
            fit.SetLevel(track, fit.Level(track) + 1);
            Status = $"{track}: {ShipFit.Name(track, fit.Level(track))}";
            PushToGame();
            return true;
        }

        public string WhyNotFit(FitTrack t) => fit.Blocked(t, Node);

        public void PushToGame()
        {
            var n = Node;
            if (n == null) return;

            var motor = GetComponent<ShipMotor>();
            if (motor != null)
                motor.ApplyFit(fit.SpeedMultiplier, fit.TurnMultiplier,
                               fit.AccelMultiplier);

            // A size upgrade nobody can see is a number in a panel.
            var rig = GetComponent<SailRig>();
            if (rig != null) rig.SetArea(fit.SailAreaScale);

            // Crew training is a real hook, not a new stat: CrewMemberDef's
            // ironStomach already halves the sickness rate at maximum, and
            // CrewRoster.Labour01 is what sail changes and the oars scale by.
            foreach (var c in GetComponentsInChildren<Crew.CrewAgent>(true))
                if (c != null && c.Def != null) c.Def.ironStomach = fit.IronStomach;

            var voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            if (voyage != null) voyage.SetHoldCapacity(Cargo);

            ManCrew(n);

            // Guns AFTER crew: the battery posts its gunners from the roster as
            // it fits, so a gun fitted before its hand exists stands unmanned
            // until something else happens to refit it.
            var battery = GetComponent<CannonBattery>();
            if (battery != null)
            {
                battery.CalibreLevel = fit.Level(FitTrack.Guns);
                battery.Fit(GunPositions(n));
            }
            // The ports she is showing are the guns she has. Same list the
            // battery was just fitted from, so the two cannot disagree.
            var lids = GetComponent<PortLids>();
            if (lids != null) lids.Open(GunPositions(n));

            ApplyLoad();
        }

        /// Bring the crew up to the number of berths she has, and stand them
        /// in the bays that hold those berths.
        ///
        /// New hands are CLONES of one already aboard rather than built from
        /// primitives here. The authored body carries a material, a scale that
        /// `ApplyCrewScale` has already put right, and whatever a CrewAgent
        /// needs wired; a second construction of the same figure is a second
        /// place for it to drift from WorldScale.Person.
        void ManCrew(LadderNode n)
        {
            var roster = GetComponent<Crew.CrewRoster>();
            if (roster == null) return;
            var have = GetComponentsInChildren<Crew.CrewAgent>(true);
            if (have.Length == 0) return;          // nobody to clone from

            int want = Mathf.Max(1, Berths);       // she always has a helmsman
            var live = new List<Crew.CrewAgent>(have);

            for (int i = live.Count; i < want; i++)
            {
                var clone = Instantiate(have[0].gameObject, have[0].transform.parent);
                clone.name = $"Hand{i:00}";
                clone.SetActive(true);
                live.Add(clone.GetComponent<Crew.CrewAgent>());
            }
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null) live[i].gameObject.SetActive(i < want);

            roster.Refresh();

            // Stand them in the quarters bays, so where the crew ARE is where
            // the player put their berths.
            var posts = QuartersPosts(n);
            if (posts.Count == 0) return;
            for (int i = 0; i < want && i < live.Count; i++)
            {
                Vector3 at = posts[i % posts.Count];
                float side = i % 2 == 0 ? 1f : -1f;
                at.x *= side;
                live[i].transform.localPosition = at;
                live[i].AssignStation(at, new Vector3(at.x + side * 0.6f, at.y, at.z));
            }
        }

        List<Vector3> QuartersPosts(LadderNode n)
        {
            var outp = new List<Vector3>();
            for (int bi = 0; bi < n.bay_labels.Length; bi++)
                for (int ti = 0; ti < n.tier_names.Length; ti++)
                    if (Use(n.bay_labels[bi], n.tier_names[ti]) == BayUse.Quarters)
                        outp.Add(new Vector3(n.beam * 0.20f,
                                             n.tier_floor[ti], n.bay_x[bi]));
            return outp;
        }

        /// Ship-local positions for the starboard guns, one per battery cell.
        /// Port is mirrored by the battery.
        List<Vector3> GunPositions(LadderNode n)
        {
            var outp = new List<Vector3>();
            for (int bi = 0; bi < n.bay_labels.Length; bi++)
                for (int ti = 0; ti < n.tier_names.Length; ti++)
                {
                    if (Use(n.bay_labels[bi], n.tier_names[ti]) != BayUse.Battery)
                        continue;
                    // Blender bow is +X and Unity's is +Z; the exporter yaws
                    // -90 so a bay's x is Unity's z. The gun stands just inboard
                    // of the side, on the deck of its own tier.
                    outp.Add(new Vector3(n.beam * 0.34f - 0.6f,
                                         n.tier_floor[ti] + 0.47f,
                                         n.bay_x[bi]));
                }
            return outp;
        }

        // --- furniture ------------------------------------------------------
        // Placed as instances of ONE kit mesh each: ten guns are one mesh and
        // ten transforms. The whole kit is 524 triangles.

        // The kit exports as ONE FBX, so it imports as one prefab with a child
        // per piece -- not as eleven prefabs. That is the right shape for the
        // asset (one import, one material, one place to improve a barrel) and
        // the wrong shape for `Resources.LoadAll`, which returns exactly one
        // object. Instantiating a CHILD of a prefab clones just that subtree,
        // so the lookup walks the children once and caches them.
        Dictionary<string, Transform> pieces;

        void LoadKit()
        {
            if (pieces != null) return;
            pieces = new Dictionary<string, Transform>();
            var root = Resources.Load<GameObject>("Kit/seasick_kit");
            if (root == null)
            {
                Debug.LogError("Shipyard: no kit at Resources/Kit/seasick_kit");
                return;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t != root.transform) pieces[t.name] = t;
        }

        Transform KitPiece(string name)
        {
            LoadKit();
            if (pieces == null) return null;
            foreach (var kv in pieces)
                if (kv.Key.Contains(name)) return kv.Value;
            return null;
        }

        public void Furnish()
        {
            if (furniture != null) Discard(furniture.gameObject);
            var n = Node;
            if (n == null) return;
            var root = new GameObject("Furniture");
            root.transform.SetParent(transform, false);
            furniture = root.transform;

            for (int bi = 0; bi < n.bay_labels.Length; bi++)
            {
                for (int ti = 0; ti < n.tier_names.Length; ti++)
                {
                    var use = Use(n.bay_labels[bi], n.tier_names[ti]);
                    if (use == BayUse.Empty) continue;
                    // Blender bow is +X and Unity's is +Z; the exporter yaws
                    // -90 so the MESH is right, and a bay's x therefore lands
                    // on Unity's z. Reading bay_x into Unity x puts the whole
                    // cargo across the beam instead of along the keel.
                    float z = n.bay_x[bi];
                    float y = 0.5f * (n.tier_floor[ti] + n.tier_ceiling[ti]);
                    float floorY = n.tier_floor[ti];
                    float halfBeam = n.beam * 0.34f;
                    Place(use, root.transform, z, floorY, y, halfBeam);
                }
            }
        }

        void Place(BayUse use, Transform root, float z, float floorY,
                   float midY, float halfBeam)
        {
            if (use == BayUse.Hold)
            {
                Transform barrel = KitPiece("Barrel");
                Transform crate = KitPiece("Crate");
                for (int i = 0; i < 3; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Transform src = (i % 2 == 0) ? barrel : crate;
                        if (src == null) continue;
                        Spawn(src, root, new Vector3(s * halfBeam * 0.45f,
                                                     floorY, z - 0.9f + i * 0.9f), 0f);
                    }
            }
            else if (use == BayUse.Battery)
            {
                Transform gun = KitPiece("Gun");
                for (int s = -1; s <= 1; s += 2)
                    if (gun != null)
                        Spawn(gun, root, new Vector3(s * (halfBeam - 0.6f),
                                                     floorY + 0.47f, z), s * 90f);
            }
            else if (use == BayUse.Quarters)
            {
                Transform ham = KitPiece("Hammock");
                Transform chest = KitPiece("SeaChest");
                for (int s = -1; s <= 1; s += 2)
                {
                    if (ham != null)
                        Spawn(ham, root, new Vector3(s * halfBeam * 0.5f,
                                                     midY + 0.3f, z), 90f);
                    if (chest != null)
                        Spawn(chest, root, new Vector3(s * halfBeam * 0.7f,
                                                       floorY, z + 0.8f), 0f);
                }
            }
        }

        /// `Destroy` THROWS in edit mode, and the throw aborts whatever was
        /// half way through calling it -- which is why the first ladder walk
        /// reported "furniture spawned: 0" while every hull swap looked fine.
        /// The yard is driven from editor tooling as well as from the running
        /// game, so it has to be able to clear up in both.
        static void Discard(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying)
            {
                // `Destroy` is deferred to the end of the frame, so the object
                // being replaced is STILL THERE, still named "HullVisual",
                // while its replacement is being built. Anything that looks the
                // visual up by name gets the corpse -- which is exactly how the
                // first in-game upgrade reported the old hull's mesh after a
                // swap that had actually worked. Rename and deactivate it now,
                // so it is neither found nor drawn for the rest of the frame.
                go.name = "~discarded";
                go.SetActive(false);
                Destroy(go);
            }
            else DestroyImmediate(go);
        }

        void Spawn(Transform src, Transform root, Vector3 local, float yaw)
        {
            var go = Instantiate(src.gameObject, root);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
