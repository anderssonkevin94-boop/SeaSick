using SeaSick.Ocean;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Steamer
{
    /// Puts the paddle steamer under the player, at runtime, without touching
    /// the scene.
    ///
    /// `Sea.unity` holds one PlayerShip and the yard dresses it as a rung of
    /// the sailing ladder. The steamer has to be sailable SIDE BY SIDE with
    /// that ship -- same water, same camera, same HUD, one switch -- while the
    /// scene file is carrying other uncommitted work, so she is not a second
    /// ship in the scene. She is a conversion of the one that is there: the
    /// yard is told to stand down before it builds anything, and this swaps
    /// the hull, the float model and the drive in its place. Everything that
    /// hangs off the PlayerShip by reference (camera rig, helm input, bilge,
    /// combat, crew) keeps the object it already had.
    ///
    /// Domain reload and scene reload are both OFF in this project, so a
    /// static survives from one play to the next. Both hooks below therefore
    /// decide from the preference EVERY play and assign unconditionally --
    /// "set it when the steamer is chosen" would leave the sailing ship with
    /// no hull on the play after the switch was turned back off.
    public static class SteamerBootstrap
    {
        /// 1 = the next Play sails the steamer. Toggled from
        /// SeaSick/Dev/Sail the Steamer.
        public const string PrefKey = "SeaSick.Steamer";

        /// Cargo cells. She is broad and full-bodied and carries her guns on
        /// an open deck, so she is a better trader than the side-wheeler was.
        const int HoldCells = 16;

        /// Hands aboard at a new game: four crew (Kevin, 2026-09-29: "you
        /// have 4 crew and the captain"). The captain is the helmsman figure
        /// at the wheel, not one of these -- he never leaves the helm.
        const int Hands = 4;

        const string HullResource = "Steamer/steamer_hull";
        const string WheelResource = "Steamer/steamer_wheel";
        /// The SAME painted timber the approved adventure brig is drawn with
        /// (`AdventureBrigVisual`), not the flatter fleet shader she used to
        /// wear. Two ships in one sea have to be lit the same way or the newer
        /// one reads as a different game.
        const string PaintShader = "SeaSick/Terrain Vertex Color";

        /// **She sails at 0.42 of her drawing for now.** Kevin, 2026-09-22:
        /// the 32 m stern-wheeler was a 430 t riverboat among 9-18 m fleet
        /// hulls, 1.8 m crew and islands a few hundred metres across. At 0.42
        /// she is a 13.5 m paddle launch of ~32 t, between the fleet's
        /// decked launch and coastal launch, and the mid raiders (18-22 m)
        /// read as bigger than her, which a raid needs. Uniform on purpose:
        /// the hull form scales with the mesh (`HullFormData.Scaled`), so she
        /// still floats on her marks and the wheel still bites, and her roll
        /// period drops as √k to ~5 s -- a small boat is lively. Regenerate
        /// at new proportions once the size has been played.
        public const float PlaytestScale = 0.42f;

        /// **Her rudder, as an area rather than as a picture.** Rudder area
        /// over L x T: 1.5 % is a lazy merchantman, 2.5 % a handy small
        /// craft. 1.8 % of 12.6 x 0.861 m is 0.195 m^2, which at full ahead
        /// makes about 13 kN of side force -- the right order for a 32 t
        /// launch, against the 225 kN the drawn 2.82 m^2 was making. Solved
        /// against the target below, not guessed:
        ///   full ahead  tactical diameter 58 m (4.6 L), 180 deg in 10.7 s
        ///   half ahead  56 m, 22 s        slow ahead  59 m, 49 s
        /// -- a circle that is the same size in METRES at any speed and a
        /// yaw rate that is not, which is what a real hull does.
        const float RudderAreaFraction = 0.018f;

        /// In the editor the default is the ladder ship, so a fresh checkout
        /// plays the scene as authored and the menu opts INTO the steamer. A
        /// player build has no menu and no way to set the preference, and
        /// the steamer is the ship being played (2026-09-22, first iPhone
        /// build came up with the brig), so there the default flips.
        public static bool Selected =>
            PlayerPrefs.GetInt(PrefKey, Application.isEditor ? 0 : 1) == 1;

        /// Before any Awake: the yard's `Start` must already know not to
        /// build a rung that the conversion would then have to find and undo.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ChooseShip()
        {
            Shipyard.SuppressApplyOnStart = Selected;
        }

        /// After every Awake and OnEnable, before any Start -- so the yard
        /// has not applied, `ShipMotor.Start` has not cached anything, and the
        /// components being disabled here never get a first frame.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ConvertPlayerShip()
        {
            if (!Selected) return;
            var yard = Object.FindFirstObjectByType<Shipyard>();
            // A lab scene with no player ship in it: nothing to convert and
            // nothing to say about it.
            if (yard == null) return;
            // Already converted this load (the first scene gets both the
            // attribute and the `sceneLoaded` hook below).
            if (yard.GetComponent<SeaSick.Ship.Modular.ShipyardService>() != null) return;

            // Everything that can fail is loaded BEFORE anything is switched
            // off. The yard has been told not to build, so bailing out half
            // way would leave a PlayerShip with no hull at all; without her
            // numbers there is no steamer, and the honest fallback is the
            // ship the scene was authored with.
            var data = ReferenceData();
            if (data == null)
            {
                Debug.LogError("[Steamer] no usable hull form at Resources/Steamer/hullform.json"
                    + " -- sailing the ladder ship instead.");
                Shipyard.SuppressApplyOnStart = false;
                return;
            }
            Convert(yard.gameObject, yard, data);
        }

        /// **Every scene load, not just the first** (2026-09-26, Kevin's
        /// phone: "the boat is not visible anymore"). Continue / Load / New
        /// reload `Sea.unity` mid-session (`GameMenus.ReloadForBoot`), and the
        /// attribute above fires once per session -- so the reloaded
        /// PlayerShip was never converted: `SuppressApplyOnStart` (still
        /// true) kept the ladder yard from building a hull, nothing else
        /// built one, and she sailed as crew and guns with no boat under
        /// them, with no `ShipyardService` either -- which is why every
        /// autosave after a load wrote `modular: ""` and forgot her refit.
        /// `sceneLoaded` runs after the new scene's Awake/OnEnable and before
        /// any Start, the same moment the attribute is documented for.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReconvertOnReload()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                  UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Shipyard.SuppressApplyOnStart = Selected;
            ConvertPlayerShip();
        }

        /// **Her hull form as she is built today**: the generator's tables at
        /// `PlaytestScale`, with the rudder cut to a real blade (below). The
        /// modular shipyard reshapes THIS for a refitted ship; the standard
        /// long steamer is this, unchanged. Null when there is no usable form.
        public static HullFormData ReferenceData()
        {
            var data = HullFormData.Load();
            if (data == null || data.stations == null || data.stations.Length == 0) return null;

            if (PlaytestScale > 0f && Mathf.Abs(PlaytestScale - 1f) > 0.001f)
                data = data.Scaled(PlaytestScale);
            // The blade the generator DREW is not the blade she steers with.
            // `hullform.json` carries 16 m^2 on a 30 m hull -- 26 % of L x T,
            // where a real rudder is 1.5 to 2.5 % -- and `Scaled` carried
            // that ratio faithfully down to 0.42, so the launch went to sea
            // with a barn door. Measured: 225 kN of side force at full ahead
            // on a 32 t boat, a 23 m turning circle (1.8 lengths) and 180
            // degrees in 4.6 s. Kevin, 2026-09-22, on the phone: *"the
            // turning radius is way too sharp on this ship. handling needs to
            // be improved so I have freedom to move without getting whiplash
            // from how fast it turns."* The HYDRODYNAMIC area is stated here
            // as the fraction of L x T it should have been; the drawing and
            // the mesh are untouched, because nothing but the rudder force
            // reads this field.
            data.rudderArea = RudderAreaFraction * data.lwl * data.draft;
            return data;
        }

        static void Convert(GameObject ship, Shipyard yard, HullFormData data)
        {
            Transform root = ship.transform;

            // --- 1. stand the ladder ship down --------------------------------
            yard.enabled = false;
            // The panel is on the PlayerShip in Sea.unity; looked for in the
            // scene as well because nothing forces it to stay there.
            var panel = ship.GetComponent<SeaSick.UI.ShipyardPanel>();
            if (panel == null) panel = Object.FindFirstObjectByType<SeaSick.UI.ShipyardPanel>();
            if (panel != null) panel.enabled = false;
            // Disabled, NOT removed: ShipMotor requires it, and Bilge,
            // Breakers and HullIntegrity read the hull's wetness from it.
            // Disabling unregisters it from the ocean driver, so it applies
            // nothing; HullFormBody publishes into it instead.
            var buoyant = ship.GetComponent<BuoyantBody>();
            if (buoyant != null) buoyant.enabled = false;

            // --- 2. the hull she is wearing ------------------------------------
            ClearVisuals(root);
            var approved = Resources.Load<GameObject>("AstraPlaytest/Steamer");
            GameObject hull;
            GameObject wheel;
            if (approved != null)
            {
                hull = Object.Instantiate(approved, root, false);
                hull.name = "HullVisual";
                var art = hull.GetComponent<AstraSteamerVisual>();
                art.enabled = false; // PaddleDrive remains the sole wheel animator.
                art.geometry.localScale = new Vector3(data.beam / 9.36f,
                    (data.depth - data.draft) / 1.76f, data.lwl / 23.95f);
                wheel = art.paddle.gameObject;
                AstraSteamerVisual.ReplaceCaptain(root);
            }
            else
            {
                hull = Spawn(HullResource, root, "HullVisual", Vector3.zero, PlaytestScale);
                wheel = Spawn(WheelResource, root, "PaddleWheel", data.wheelAxle, PlaytestScale);
            }
            // ONE wheel, on the centreline, inside her stern. The mesh is
            // modelled about its own axle so this is the only place its
            // position is stated.

            Material paint = null;
            var shader = Shader.Find(PaintShader);
            if (shader == null)
                Debug.LogError($"[Steamer] shader '{PaintShader}' not found -- she keeps her import materials.");
            else
            {
                // ONE material for hull and wheel: the colour is in the
                // vertices, so there is nothing per-part to vary and two
                // materials would be two draw-call breaks for nothing. The
                // detail terms are the terrain shader's and belong to ground;
                // on planking they read as dirt.
                paint = new Material(shader) { name = "SteamerPaint (runtime)" };
                paint.SetFloat("_DetailStrength", 0f);
                paint.SetFloat("_NormalStrength", 0f);
                paint.SetFloat("_StriationStrength", 0f);
                paint.SetFloat("_GraphicLight", 0.75f);
                Paint(hull, paint);
                Paint(wheel, paint);
            }

            Assemble(ship, data, hull, wheel != null ? wheel.transform : null, paint, BuildOptions.Standard);

            // The modular shipyard's handle on her (docs/SHIPYARD-API.md). It
            // does nothing until a refit is confirmed or a save carries one:
            // until then she is exactly the ship built above.
            var shipyard = ship.GetComponent<SeaSick.Ship.Modular.ShipyardService>();
            if (shipyard == null) shipyard = ship.AddComponent<SeaSick.Ship.Modular.ShipyardService>();
            shipyard.Bind(data, hull, wheel != null ? wheel.transform : null);
        }

        /// What differs between building her at start and rebuilding her in
        /// the modular shipyard. `Standard` is today's steamer, unchanged.
        public struct BuildOptions
        {
            /// Hold cells handed to the VoyageManager.
            public int holdCells;
            /// Hands to post (and, when `cloneHands`, to make up to).
            public int hands;
            /// Start: clone hands up to `hands` and stand down the rest.
            /// Refit: never -- post the hands already aboard, nobody made,
            /// nobody sent away.
            public bool cloneHands;
            /// Where the deck load goes; null = measure the funnel off `hull`.
            public SeaSick.Ship.Modular.DeckLoadPlan deckLoad;
            /// For the log line only.
            public bool refit;
            /// Her guns, explicit equipment (2026-09-25), each with its own
            /// side and position -- null = the untouched steamer, which still
            /// takes her battery from `data.gunSockets` (starboard, mirrored
            /// to port) exactly as she always has. A modular refit passes the
            /// plan's `fittedGuns` here instead, so a side missing a gun (one
            /// sent to the dry dock) does not get a mirrored phantom back.
            public System.Collections.Generic.List<SeaSick.Ship.Modular.FittedGun> fittedGuns;

            public static BuildOptions Standard => new BuildOptions
            {
                holdCells = HoldCells, hands = Hands, cloneHands = true, deckLoad = null, refit = false,
                fittedGuns = null,
            };
        }

        /// **Everything that follows from her hull form**, in the one order
        /// that works: collider, body, motor, clip and foam, people and hold,
        /// drive, target, marker. Run once at start (`Convert`) and again, on
        /// the SAME GameObject and Rigidbody, by `ShipyardService` for a
        /// refit -- so a refitted ship is built by the very path she was.
        /// `hull` is what she is drawn with (for the funnel), `wheel` the
        /// transform the drive spins.
        public static void Assemble(GameObject ship, HullFormData data, GameObject hull, Transform wheel,
                                    Material paint, BuildOptions o)
        {
            Transform root = ship.transform;

            // --- 3. collider, THEN mass and inertia ----------------------------
            //
            // Same box the yard fits, from her own numbers: keel at -draft,
            // deck at depth - draft, so its centre is half a depth up from the
            // keel in a frame whose origin is the waterline. Before the hull
            // model because a collider added to a rigidbody re-derives its
            // mass properties, and HullFormBody.Configure has to have the
            // last word on the centre of mass and the tensor.
            float railY = data.depth - data.draft;
            var box = ship.GetComponent<BoxCollider>();
            if (box == null) box = ship.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (railY - data.draft) * 0.5f, 0f);
            box.size = new Vector3(data.beam * 0.85f, data.depth, data.lwl * 0.9f);

            var body = ship.GetComponent<HullFormBody>();
            if (body == null) body = ship.AddComponent<HullFormBody>();
            body.Configure(data);

            // --- 4. the motor, as instruments rather than engine ---------------
            var motor = ship.GetComponent<ShipMotor>();
            if (motor != null)
            {
                motor.ConfigureForHull(data.lwl, railY, false, null);
                ApplyTopSpeed(motor, data, PaddleDrive.DefaultTopSpeed);
            }

            // --- 5. the sea kept out of her, and the foam she throws -----------
            // Both exactly as Shipyard.Refit sizes them, from this hull.
            var clip = ship.GetComponent<HullWaterClip>();
            if (clip == null) clip = ship.AddComponent<HullWaterClip>();
            {
                const float Below = 0.4f, AboveRail = 0.5f;
                float lo = -Below, hi = railY + AboveRail;
                // The ellipse has to stay inboard of the planking or it cuts a
                // notch in the open sea -- AND it has to stop short of the
                // paddle well, which is open water on purpose. Clipped, the
                // wheel would be seen turning in a dry hole in her stern. So
                // it is pushed forward until its after end is clear of the
                // well's bulkhead, and lengthened by as much at the bow.
                float aft = data.well != null ? data.well.fwdZ : -0.35f * data.lwl;
                float halfL = data.lwl * 0.42f;
                float centreZ = aft + halfL + 0.9f;
                clip.Configure(
                    new Vector3(0f, (lo + hi) * 0.5f, centreZ),
                    new Vector2(data.beam * 0.40f, halfL),
                    (hi - lo) * 0.5f);
            }
            var juice = ship.GetComponent<SpeedJuice>();
            if (juice != null) juice.ConfigureForHull(data.lwl, data.beam, railY);

            // --- 6. the people and the hold ------------------------------------
            var captain = root.Find("Helmsman");
            if (captain != null) captain.localPosition = data.helm;
            var voyage = Object.FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            if (voyage != null) voyage.SetHoldCapacity(o.holdCells);
            FitDeckLoad(ship, data, hull, o);
            Man(ship, data, o);

            // --- 7. the drive, and only then hand it the ship ------------------
            var paddle = ship.GetComponent<PaddleDrive>();
            if (paddle == null) paddle = ship.AddComponent<PaddleDrive>();
            // Froude scaling: a hull at k of the drawing tops out at √k of
            // the drawing's speed (15.5 → ~10 m/s at 0.42).
            paddle.SetTopSpeed(PaddleDrive.DefaultTopSpeed * Mathf.Sqrt(Mathf.Max(0.05f, PlaytestScale)));
            paddle.Configure(data, body, wheel);
            // The capsule the raiders' balls have to cross, from this hull.
            var target = ship.GetComponent<SeaSick.Combat.PlayerHull>();
            if (target != null) target.Configure(data.beam * 0.5f + 0.4f, data.lwl * 0.46f);
            // The gauges are scaled to MaxSpeed, so it has to be the speed the
            // wheels actually top out at -- whatever that was tuned to.
            if (motor != null)
            {
                ApplyTopSpeed(motor, data, paddle.TopSpeed);
                // Last: until the wheels exist the servo is the only thing
                // that could hold her, and after this line it holds nothing.
                motor.ExternalDrive = true;
            }

            var marker = ship.GetComponent<SteamerShip>();
            if (marker == null) marker = ship.AddComponent<SteamerShip>();
            // A refit keeps the paint the conversion made (and still owns).
            marker.Bind(data, body, paddle, paint != null ? paint : marker.PaintMaterial);

            var rb = ship.GetComponent<Rigidbody>();
            float tonnes = (rb != null ? rb.mass : data.massKg) / 1000f;
            Debug.Log($"[Steamer] {(o.refit ? "refitted" : "converted")} PlayerShip: L {data.lwl:F1} B {data.beam:F1} m {tonnes:F0} t");
        }

        /// Crew and guns, from the ship rather than from constants.
        ///
        /// The yard is standing down, so the two things it would normally do
        /// for a hull -- clone hands up to her berths and fit a battery to her
        /// gun stations -- have to be done here or she sails with an empty
        /// deck. The gun positions come out of `hullform.json`, where the hull
        /// generator solved them against her own planking; `CannonBattery`
        /// mirrors the starboard side and then posts a hand to each gun
        /// itself.
        static void Man(GameObject ship, HullFormData data, BuildOptions o)
        {
            int Hands = o.hands;
            // A body whose life has ended is stood down by
            // `CrewNames.RetireTakenNames` in `SaveGame.Restore` step 5a2,
            // after `DeathRepair` (2026-10-04), not here: this runs at the
            // scene-load conversion (the life registry is still the LAST
            // session's) and at a refit, which never switches anybody on.
            var hands = ship.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(true);
            if (!o.cloneHands)
            {
                // A refit: the same people, re-posted on the new deck. Nobody
                // is made and nobody is stood down (the shipyard refuses a
                // refit with more hands aboard than stations).
                var aboard = new System.Collections.Generic.List<SeaSick.Crew.CrewAgent>();
                foreach (var h in hands) if (h != null && h.gameObject.activeSelf) aboard.Add(h);
                for (int i = 0; i < aboard.Count; i++)
                {
                    DeckStation(data, i, Mathf.Max(Hands, aboard.Count), out Vector3 at, out Vector3 rail);
                    aboard[i].AssignStation(at, rail);
                }
                var crewList = ship.GetComponent<SeaSick.Crew.CrewRoster>();
                if (crewList != null) crewList.Refresh();
            }
            else if (hands.Length > 0)
            {
                var live = new System.Collections.Generic.List<SeaSick.Crew.CrewAgent>(hands);
                // A clone is a different person: `Instantiate` copies the
                // serialised `CrewMemberDef`, so without christening him the
                // new hand wears the template's NAME as well as his coat and
                // the crew list reads "Pip, Bo, Bo, Bo" (Kevin, 2026-09-22).
                // Same hat as `Shipyard.ManCrew`, same rule: no name that is
                // already worn aboard or written in a camp's ledger.
                System.Collections.Generic.HashSet<string> taken = null;
                for (int i = live.Count; i < Hands; i++)
                {
                    var clone = Object.Instantiate(hands[0].gameObject,
                                                   hands[0].transform.parent);
                    clone.SetActive(true);
                    var agent = clone.GetComponent<SeaSick.Crew.CrewAgent>();
                    if (taken == null) taken = SeaSick.Crew.CrewNames.InUse();
                    if (agent != null) SeaSick.Crew.CrewNames.Christen(agent, i, taken);
                    else clone.name = $"Hand{i:00}";
                    live.Add(agent);
                }
                for (int i = 0; i < live.Count; i++)
                    if (live[i] != null) live[i].gameObject.SetActive(i < Hands);
                // **Everybody stands inside her.** The scene's stations were
                // authored for the brig; on a 13.5 m launch two of them were
                // off the bow and the rest hovered above the deck. Kevin,
                // 2026-09-22: *"ensure no crew stands outside of it."* So the
                // stations come off the hull form: pairs down the deck from
                // the forecastle to the helm, each 0.7 m inside the bulwark
                // at that station, feet on that station's deck.
                int posted = 0;
                for (int i = 0; i < live.Count && posted < Hands; i++)
                {
                    if (live[i] == null || !live[i].gameObject.activeSelf) continue;
                    DeckStation(data, posted, Hands, out Vector3 at, out Vector3 rail);
                    live[i].AssignStation(at, rail);
                    posted++;
                }
                var roster = ship.GetComponent<SeaSick.Crew.CrewRoster>();
                if (roster != null) roster.Refresh();
            }

            var battery = ship.GetComponent<CannonBattery>();
            if (battery == null) return;

            if (o.fittedGuns != null)
            {
                // Guns as explicit equipment (2026-09-25): each one already
                // knows its own side and position (the slot it stands on),
                // so it is handed to the battery as-is -- no mirroring, so a
                // side missing a gun (one sent to the dry dock) stays short
                // instead of growing a phantom twin from the other side.
                var stations = new System.Collections.Generic.List<CannonBattery.GunStation>(o.fittedGuns.Count);
                foreach (var g in o.fittedGuns)
                {
                    // Same correction as the socket path below: stand the
                    // gunner on the deck at that station, not at the
                    // carriage's own height.
                    var pos = g.positionM;
                    stations.Add(new CannonBattery.GunStation(pos, g.side == "starboard"));
                }
                battery.Fit(stations);
                return;
            }

            // An EMPTY list is a real answer -- no guns -- and null means "the
            // authored pair", so a hull whose generator wrote no sockets must
            // pass the empty list, not null.
            var sockets = new System.Collections.Generic.List<Vector3>();
            if (data.gunSockets != null)
                foreach (var g in data.gunSockets)
                {
                    // The generator wrote the sockets at the carriage's
                    // height; the battery builds a gun UP from its socket
                    // and stands the gunner AT it, so both hung half a
                    // metre over the planks. Feet on the deck at that
                    // station instead.
                    sockets.Add(new Vector3(g.x, DeckYAt(data, g.z), g.z));
                }
            battery.Fit(sockets);
        }

        /// **Her cargo is a deck load, lashed low and wide just aft of
        /// amidships** (2026-09-24). Kevin, on the phone: *"the cargo gets
        /// HUGE on the ship ... it piles it in a very distracting and
        /// unreasonable way."* The serialized layout was the brig's (a 2 x 2
        /// tower at the stern); this lays it out from her own numbers.
        ///
        /// - Row 0 fills the open deck between the funnel and the helmsman,
        ///   centred in it; row 1 starts just forward of the funnel. The
        ///   funnel is MEASURED off the mesh she is wearing (the Astra
        ///   `Chimney` renderer), not typed; without one it is taken as a
        ///   0.8 m casing on the centreline amidships.
        /// - Two kinds abreast (fewer on a narrow deck): as many piles at
        ///   `Across` centres as fit inside the crew's stations, which stand
        ///   0.7 m in from the bulwark (`DeckStation`).
        /// - Feet on the deck at each row's own station.
        static void FitDeckLoad(GameObject ship, HullFormData data, GameObject hull, BuildOptions o)
        {
            var hold = ship.GetComponent<ShipHold>();
            if(hold!=null)hold.SetAbstractStorage(false);
            if (hold == null) return;
            if (o.deckLoad != null)
            {
                // A refit: the shipyard laid the rows out already (the SAME
                // `DeckLoadPlan.Lay`, from the assembly's own funnel), and
                // checked every pile still has a place.
                hold.Fit(o.deckLoad.rows, o.deckLoad.abreast, SeaSick.Ship.Modular.DeckLoadPlan.Across, o.holdCells);
                return;
            }

            // A pile by the fire is at most ~1.6 m long (a dozen logs laid
            // fore and aft) and ~1.55 m wide (a cairn of stone), about a
            // metre high: `CampPiles.DrawPile`.
            const float PileLength = 1.7f, Across = 1.5f, Clear = 0.15f;

            float funnelAft = -0.4f, funnelFwd = 0.4f;
            var chimney = FindDeep(hull != null ? hull.transform : null, "Chimney");
            var r = chimney != null ? chimney.GetComponent<Renderer>() : null;
            if (r != null)
            {
                Bounds b = r.bounds;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    float z = ship.transform.InverseTransformPoint(corner).z;
                    lo = Mathf.Min(lo, z);
                    hi = Mathf.Max(hi, z);
                }
                funnelAft = lo; funnelFwd = hi;
            }

            // The helmsman stands at `data.helm`; leave him a body's room.
            // (Same layout as `SeaSick.Ship.Modular.DeckLoadPlan.Lay`, which
            // the shipyard uses for a refit -- keep the two in step.)
            float aftLimit = data.helm.z + 0.45f;
            float room = (funnelAft - Clear) - aftLimit;
            float z0 = room >= PileLength
                ? (funnelAft - Clear + aftLimit) * 0.5f
                : funnelAft - Clear - PileLength * 0.5f;
            float z1 = funnelFwd + Clear + PileLength * 0.5f;

            // Across: inside the crew's stations at row 0's station.
            int si = NearestStation(data, z0);
            float half = data.HalfBreadthAt(si, data.stations[si].deckY);
            int abreast = Mathf.Clamp(Mathf.FloorToInt(2f * (half - 0.7f) / Across), 1, 3);

            var rows = new[]
            {
                new Vector3(0f, DeckYAt(data, z0) + 0.01f, z0),
                new Vector3(0f, DeckYAt(data, z1) + 0.01f, z1),
                new Vector3(0f, DeckYAt(data, z1 + PileLength + 0.1f) + 0.01f, z1 + PileLength + 0.1f),
            };
            hold.Fit(rows, abreast, Across, HoldCells);
            Debug.Log($"[Steamer] deck load: {abreast} abreast at {Across:F2} m, rows z "
                + $"{rows[0].z:F2} / {rows[1].z:F2} / {rows[2].z:F2}, deck y {rows[0].y:F2} "
                + $"(funnel z {funnelAft:F2}..{funnelFwd:F2}{(r != null ? "" : ", assumed")}, "
                + $"helm z {data.helm.z:F2})");
        }

        static int NearestStation(HullFormData data, float z)
        {
            int si = 0; float best = float.MaxValue;
            for (int i = 0; i < data.StationCount; i++)
            {
                float d = Mathf.Abs(data.stations[i].z - z);
                if (d < best) { best = d; si = i; }
            }
            return si;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        /// `ConfigureForHull` authors top speed as 15 * sqrt(L / TunedLoa) and
        /// `ApplyFit` multiplies it, so the multiplier that lands MaxSpeed on
        /// the wheels' top speed is the ratio of the two. Turn and
        /// acceleration stay at 1: with ExternalDrive nothing reads them but
        /// the panels.
        /// Deck height at z, from the nearest station of the form.
        static float DeckYAt(HullFormData data, float z)
        {
            int si = 0; float best = float.MaxValue;
            for (int i = 0; i < data.StationCount; i++)
            {
                float d = Mathf.Abs(data.stations[i].z - z);
                if (d < best) { best = d; si = i; }
            }
            return data.stations[si].deckY;
        }

        /// The n-th deck station: pairs port/starboard, walking aft from a
        /// third of the way forward to just ahead of the helm, x inside the
        /// bulwark by `Inboard` at that station's deck level. Nothing here
        /// is a typed coordinate, so it holds at any scale.
        static void DeckStation(HullFormData data, int n, int hands, out Vector3 at, out Vector3 rail)
        {
            const float Inboard = 0.7f;
            int pairs = Mathf.Max(1, (hands + 1) / 2);
            int pair = n / 2;
            float side = n % 2 == 0 ? 1f : -1f;
            // From +0.30 L to the helm, and never into the wheel well.
            float zFwd = data.lwl * 0.30f;
            float zAft = Mathf.Max(data.helm.z + 1.0f, data.well != null ? data.well.fwdZ + 1.0f : -data.lwl * 0.2f);
            float z = pairs > 1 ? Mathf.Lerp(zFwd, zAft, pair / (float)(pairs - 1)) : zFwd;
            // Nearest station of the form to that z.
            int si = 0; float best = float.MaxValue;
            for (int i = 0; i < data.StationCount; i++)
            {
                float d = Mathf.Abs(data.stations[i].z - z);
                if (d < best) { best = d; si = i; }
            }
            var st = data.stations[si];
            float deckY = st.deckY;
            float half = data.HalfBreadthAt(si, deckY);
            float x = Mathf.Max(0.3f, half - Inboard) * side;
            at = new Vector3(x, deckY, z);
            rail = new Vector3(Mathf.Max(0.3f, half - 0.25f) * side, deckY, z);
        }

        static void ApplyTopSpeed(ShipMotor motor, HullFormData data, float topSpeed)
        {
            float baseMax = 15f * Mathf.Sqrt(Mathf.Max(1f, data.lwl) / ShipMotor.TunedLoa);
            motor.ApplyFit(topSpeed / baseMax, 1f, 1f);
        }

        /// Every hull visual, whoever left it. `Destroy` is deferred to the
        /// end of the frame, so the corpse is renamed and hidden as well --
        /// otherwise the next lookup of "HullVisual" by name finds it instead
        /// of the hull that replaced it.
        static void ClearVisuals(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child.name != "HullVisual" && child.name != "FleetVisual") continue;
                child.name = "Discarded";
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
        }

        static GameObject Spawn(string resource, Transform parent, string name,
                                Vector3 localPosition, float scale = 1f)
        {
            var src = Resources.Load<GameObject>(resource);
            if (src == null)
            {
                // Not fatal: she floats and drives on her numbers, and a
                // physics probe does not need to see her.
                Debug.LogError($"[Steamer] no model at Resources/{resource}");
                return null;
            }
            var go = Object.Instantiate(src, parent);
            go.name = name;
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static void Paint(GameObject go, Material paint)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                // Every slot, not just the first: an FBX with two material
                // slots would otherwise keep its import grey on half of her.
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = paint;
                r.sharedMaterials = slots;
            }
        }
    }
}
