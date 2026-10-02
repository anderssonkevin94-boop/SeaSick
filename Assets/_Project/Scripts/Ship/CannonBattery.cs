using System.Collections.Generic;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Four guns on the rail — two a side, one forward and one aft of the mast,
    /// set inboard of the bulwark and toed in to cross on the beam.
    ///
    /// Aiming is done with the tiller: you steer to bring a side to bear and
    /// fire that broadside. That keeps combat inside the sailing rather than
    /// competing with it for the player's thumb.
    public class CannonBattery : MonoBehaviour
    {
        // The hull tumbles home above the waterline, so a gun sitting at the
        // full beam ends up buried in the planking. These are inboard of the
        // bulwark with only the muzzle looking over it.
        [SerializeField] Vector2 forePosition = new Vector2(1.45f, 4.8f);  // x offset, z
        [SerializeField] Vector2 aftPosition = new Vector2(1.45f, -1.2f);
        [SerializeField] float deckHeight = 2.05f;

        // How much of the ship's own motion the shot carries away with it.
        //
        // 1 is the honest simulation — but at 21 m/s against a 42 m/s muzzle
        // that deflects the shot ~27° forward of the beam, which flatly
        // contradicts "steer to bring a side to bear and fire". 0 means the
        // ball goes exactly where the barrel points, so the aiming premise
        // holds at any speed.
        //
        // Held at 0 while the broadside mechanic itself is being proved out.
        // Raising it is a live design question, not a fix: see GDD 2026-08-19.
        [Range(0f, 1f)] [SerializeField] float velocityInheritance = 0f;

        // The two guns on a side sit 6m apart along the hull. Trained square
        // out they fire parallel, so with the ship's centre laid on the target
        // the fore gun passes 4.8m ahead of it and cannot hit — measured, not
        // guessed. Toeing them in to cross at one point abeam means "the side
        // bears" is a single answer instead of two different ones.
        [SerializeField] float convergeRange = 45f;

        // How far off the beam a target can sit and still have the guns laid on
        // it. Wider than the traverse limit on purpose: outside the limit the
        // guns hold hard over, so you can see them reaching for a beast before
        // the side properly bears.
        [SerializeField] float trainWithinDeg = 60f;

        // Roll kick per gun that speaks. Two guns give ~5 degrees of heel at
        // the peak — enough to feel the ship answer the broadside, not enough
        // to spoil the shot that is already in the air.
        /// **Recoil is an impulse now, not an angle.**
        ///
        /// It used to be `AddRecoilRoll(14 x guns)` — the same formula for a
        /// sloop's two guns and a three-decker's twelve, with no reference to
        /// what the ship weighs or how hard she is to heel. Now each gun that
        /// speaks returns real momentum: shot mass x muzzle speed, plus about
        /// half again for the propellant gas, applied outboard at the gun's own
        /// position. The heel that follows is whatever her mass and her righting
        /// moment allow, which is the point.
        ///
        /// Shot weights are the calibres they are named for: a 4-pounder throws
        /// 1.8 kg, an 18-pounder 8.2.
        static readonly float[] ShotKg = { 0.5f, 1.8f, 4.1f, 8.2f };
        const float MuzzleSpeed = 400f;
        const float GasFactor = 1.5f;

        [Tooltip("Multiplies the real recoil impulse. 1 is physics. Real ships barely heel to a broadside, so this exists to be turned up deliberately rather than by tuning something else until it looks right.")]
        [SerializeField] float recoilExaggeration = 1f;

        /// Which calibre she is fitted with, set by the yard from ShipFit.
        public int CalibreLevel { get; set; }

        readonly List<Cannon> port = new List<Cannon>();
        readonly List<Cannon> starboard = new List<Cannon>();
        // Build order, and therefore the crew assignment order: each gun is
        // worked by one named hand. A gun goes silent because a person walked
        // away from it, which is a thing you can watch happen on deck.
        readonly List<Cannon> allGuns = new List<Cannon>();
        ShipMotor motor;
        Crew.CrewRoster roster;
        Combat.IHittable self;

        public int PortReady => CountReady(port);
        public int StarboardReady => CountReady(starboard);
        public int GunsPerSide => Mathf.Max(port.Count, starboard.Count);
        /// A side's REAL count, unlike `GunsPerSide` (the symmetric max) --
        /// the two differ once a ship can carry an unpaired gun.
        public int PortCount => port.Count;
        public int StarboardCount => starboard.Count;
        public int TotalGuns => allGuns.Count;
        public IReadOnlyList<Cannon> Guns => allGuns;

        /// Flat-water reach of these guns, for the gunnery readout.
        public float GunRange => starboard.Count > 0 && starboard[0] != null
            ? starboard[0].FlatRange : 0f;

        public float VelocityInheritance => velocityInheritance;

        /// How far forward of the beam the ship's motion throws the shot at the
        /// current speed. Shown in the readout so the cost of raising
        /// `velocityInheritance` is a measured number rather than a guess.
        public float DeflectionDeg
        {
            get
            {
                if (motor == null || starboard.Count == 0 || starboard[0] == null) return 0f;
                float muzzle = starboard[0].MuzzleSpeed;
                if (muzzle <= 0.01f) return 0f;
                return Mathf.Atan2(motor.CurrentSpeed * velocityInheritance, muzzle) * Mathf.Rad2Deg;
            }
        }

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            // The arcs on the water belong to the battery: added here so no
            // bootstrap has to know about them (2026-10-02).
            if (GetComponent<Combat.FiringArcs>() == null)
                gameObject.AddComponent<Combat.FiringArcs>();
            // Only the authored four. A ship with a Shipyard on her calls
            // `Fit` instead, from the bays the player assigned to a battery,
            // and this default never runs.
            if (built) return;
            Fit((IList<Vector3>)null);
        }

        bool built;
        bool authoredBattery;
        Crew.CrewAgent AssignedCrew(int index)
        {
            var hand = roster == null ? null : roster.GunCrew(authoredBattery ? index / 2 : index);
            return hand != null && hand.gameObject.activeInHierarchy ? hand : null;
        }

        Material MakeWood()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.38f, 0.24f, 0.14f));
            m.SetFloat("_Smoothness", 0.12f);
            return m;
        }

        Material MakeIron()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.17f));
            m.SetFloat("_Smoothness", 0.45f);
            return m;
        }

        /// Fit the battery to a set of measured gun positions, in ship-local
        /// space, one per gun on the STARBOARD side; the port side is mirrored.
        ///
        /// This is what makes a battery a consequence of the ship rather than a
        /// constant: the positions come from the bays the player gave to guns,
        /// which come from the gun-port stations the hull was lofted with. Pass
        /// null to get the authored four, which is what a ship with no
        /// Shipyard on her still wants.
        public void Fit(IList<Vector3> starboardLocal)
        {
            authoredBattery = false;
            foreach (var c in allGuns)
                if (c != null) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            port.Clear(); starboard.Clear(); allGuns.Clear();

            var wood = MakeWood();
            var iron = MakeIron();

            // NULL means "use the authored pair fore and aft" — a ship with no
            // Shipyard on her. An EMPTY LIST means she has no battery at all,
            // which is a real answer and not the same thing: a 9 m open fishing
            // skiff came out of the yard carrying two guns because the two
            // cases were folded together.
            if (starboardLocal == null)
            {
                Make("CannonPortFore", -forePosition.x, forePosition.y, -1f, port, wood, iron);
                Make("CannonPortAft", -aftPosition.x, aftPosition.y, -1f, port, wood, iron);
                Make("CannonStarFore", forePosition.x, forePosition.y, 1f, starboard, wood, iron);
                Make("CannonStarAft", aftPosition.x, aftPosition.y, 1f, starboard, wood, iron);
            }
            else
            {
                hasFittedMid = false;
                for (int i = 0; i < starboardLocal.Count; i++)
                {
                    Vector3 g = starboardLocal[i];
                    MakeAt($"CannonStar{i}", new Vector3(Mathf.Abs(g.x), g.y, g.z),
                           1f, starboard, wood, iron);
                    MakeAt($"CannonPort{i}", new Vector3(-Mathf.Abs(g.x), g.y, g.z),
                           -1f, port, wood, iron);
                }
                // The convergence mark is the middle of the battery she
                // actually has, not of the two positions in the inspector.
                if (starboardLocal.Count > 0)
                {
                    float zSum = 0f;
                    foreach (var g in starboardLocal) zSum += g.z;
                    fittedMidZ = zSum / starboardLocal.Count;
                    hasFittedMid = true;
                }
            }

            built = true;
            PostGunCrews();
        }

        /// One physical gun to stand, ship-local space, for `Fit(IList&lt;GunStation&gt;)`.
        /// Unlike the starboard-only list `Fit(IList&lt;Vector3&gt;)` mirrors, each
        /// entry carries its OWN side, so a ship can be missing a gun on one
        /// side and keep the other exactly where she stood.
        public struct GunStation
        {
            public Vector3 position;
            public bool starboard;
            public GunStation(Vector3 position, bool starboard)
            {
                this.position = position;
                this.starboard = starboard;
            }
        }

        /// Fit the battery to an EXPLICIT set of guns, each already carrying
        /// its own side and position -- no mirroring. This is what a ship
        /// with guns as equipment (2026-09-25) uses: her port and starboard
        /// counts and positions come straight from what is actually fitted,
        /// so removing one gun from one side leaves the other side untouched
        /// and does not draw a phantom twin. Guns are added in list order,
        /// which is also the crew-assignment order (`CrewRoster.GunCrew`).
        /// An empty or null list is a real "no guns" battery.
        public void Fit(IList<GunStation> guns)
        {
            authoredBattery = false;
            foreach (var c in allGuns)
                if (c != null) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            port.Clear(); starboard.Clear(); allGuns.Clear();

            var wood = MakeWood();
            var iron = MakeIron();

            hasFittedMid = false;
            if (guns != null && guns.Count > 0)
            {
                float zSum = 0f;
                foreach (var g in guns) zSum += g.position.z;
                fittedMidZ = zSum / guns.Count;
                hasFittedMid = true;
            }

            if (guns != null)
            {
                int si = 0, pi = 0;
                foreach (var g in guns)
                {
                    var side = g.starboard ? starboard : port;
                    string name = g.starboard ? $"CannonStar{si++}" : $"CannonPort{pi++}";
                    MakeAt(name, g.position, g.starboard ? 1f : -1f, side, wood, iron);
                }
            }

            built = true;
            PostGunCrews();
        }

        public void FitAuthored(FleetVisual visual)
        {
            authoredBattery = true;
            foreach (var c in allGuns) if (c != null) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            port.Clear(); starboard.Clear(); allGuns.Clear();
            motor = GetComponent<ShipMotor>();
            fittedMidZ = 0;
            foreach (var template in visual.gunTemplates) fittedMidZ += template.localPosition.z;
            if (visual.gunTemplates.Length > 0) fittedMidZ /= visual.gunTemplates.Length;
            hasFittedMid = visual.gunTemplates.Length > 0;
            foreach (var template in visual.gunTemplates)
            {
                var go = Instantiate(template.gameObject, transform, false);
                go.name = "Fitted" + template.name;
                go.SetActive(true);
                var cannon = go.AddComponent<Cannon>();
                cannon.BuildAuthored(go.GetComponent<FleetGun>());
                (go.transform.localPosition.x > 0 ? starboard : port).Add(cannon);
                allGuns.Add(cannon);
            }
            built = true;
            PostGunCrews();
        }

        float fittedMidZ;
        bool hasFittedMid;

        [Header("Gun crews")]
        [Tooltip("How far inboard of their gun the gunner stands.")]
        [SerializeField] float gunnerInboard = 0.72f;
        [Tooltip("How far outboard of the gun the rail is — a gunner heaves out their own port.")]
        [SerializeField] float gunportOutboard = 0.55f;

        /// Stand each assigned hand at the gun they work. This is what makes
        /// the mechanic legible: a silent gun has a visibly empty place behind
        /// it, and you can watch the person who should be there walk away.
        public void SetGunnerClearance(float inboard) { gunnerInboard=inboard;PostGunCrews(); }

        void PostGunCrews()
        {
            if (roster == null) roster = GetComponent<Crew.CrewRoster>();
            if (roster == null) return;

            for (int i = 0; i < allGuns.Count; i++)
            {
                var hand = AssignedCrew(i);
                if (hand == null || allGuns[i] == null) continue;

                Vector3 gun = allGuns[i].transform.localPosition;
                float side = Mathf.Sign(gun.x);
                Vector3 station = new Vector3(gun.x - side * gunnerInboard, gun.y, gun.z);
                // **The v15 gunner (GunRam / GunFire, 2026-10-01)** was
                // authored at his station beside the BACK of the gun, the gun
                // on his right, facing outboard with it: 1.05 m inboard of
                // the gun's origin and 0.98 m to its left (gun frame), clear
                // of the recoil. A body with those clips stands there.
                if (hand.HasGunnerClips && allGuns[i].transform.parent == transform)
                {
                    var g = allGuns[i].transform;
                    Vector3 fwd = g.localRotation * Vector3.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 1e-4f) fwd = new Vector3(side, 0f, 0f);
                    fwd.Normalize();
                    // Outboard along the gun; its left is up x forward.
                    if (Vector3.Dot(fwd, new Vector3(side, 0f, 0f)) < 0f) fwd = -fwd;
                    Vector3 left = Vector3.Cross(fwd, Vector3.up);
                    station = gun - fwd * Crew.CrewAgent.GunnerBehind + left * Crew.CrewAgent.GunnerBeside;
                    station.y = gun.y;
                }
                hand.AssignStation(station,
                    new Vector3(gun.x + side * gunportOutboard, gun.y, gun.z));
                hand.AssignGun(allGuns[i]);
            }
        }

        /// Where a side's guns are trained to cross, in ship-local space:
        /// straight out on the beam, level with the middle of the battery.
        float BatteryMidZ => hasFittedMid
            ? fittedMidZ : (forePosition.y + aftPosition.y) * 0.5f;

        void Make(string name, float x, float z, float sideSign, List<Cannon> side,
            Material wood, Material iron)
            => MakeAt(name, new Vector3(x, deckHeight, z), sideSign, side, wood, iron);

        void MakeAt(string name, Vector3 local, float sideSign, List<Cannon> side,
            Material wood, Material iron)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            float deckHeight = local.y;

            // Point the gun at the convergence mark rather than square out.
            Vector3 aim = new Vector3(sideSign * convergeRange, deckHeight, BatteryMidZ);
            Vector3 dir = aim - go.transform.localPosition;
            go.transform.localRotation = Quaternion.Euler(
                0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);

            var cannon = go.AddComponent<Cannon>();
            cannon.Build(wood, iron);
            side.Add(cannon);
            allGuns.Add(cannon);
        }

        static int CountReady(List<Cannon> side)
        {
            int n = 0;
            foreach (var c in side) if (c != null && c.Ready) n++;
            return n;
        }

        /// Fire every loaded gun on one side. Returns how many spoke.
        public int FireBroadside(bool starboardSide)
        {
            var side = starboardSide ? starboard : port;
            Vector3 carried = motor != null ? motor.Velocity * velocityInheritance : Vector3.zero;
            int fired = 0;
            foreach (var c in side) if (c != null && c.Fire(carried)) fired++;

            // Lean the view out along the side that spoke, so you can watch
            // where the shot lands instead of guessing.
            if (fired > 0)
            {
                // The hull answers: firing to starboard heels her to port.
                // Applied at each gun, so the lever arm is the real height of
                // that gun above her centre of gravity — an upper-deck battery
                // heels her far harder than the same guns in the hold would.
                var rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    float shot = ShotKg[Mathf.Clamp(CalibreLevel, 0, 3)];
                    float perGun = shot * MuzzleSpeed * GasFactor * recoilExaggeration;
                    var firing = starboardSide ? starboard : port;
                    int spoke = 0;
                    foreach (var c in firing)
                    {
                        if (c == null || spoke >= fired) continue;
                        spoke++;
                        // Reaction is opposite the shot: outboard becomes inboard.
                        Vector3 outward = transform.TransformDirection(
                            new Vector3(starboardSide ? 1f : -1f, 0f, 0f));
                        rb.AddForceAtPosition(-outward * perGun,
                                              c.transform.position,
                                              ForceMode.Impulse);
                    }
                }
            }

            return fired;
        }

        /// Hand each gun to its crew member. A queasy gunner reloads slowly,
        /// a gunner at the rail doesn't reload at all — the half-worked charge
        /// is still sitting there when they stagger back.
        void ServiceGuns()
        {
            if (roster == null) roster = GetComponent<Crew.CrewRoster>();
            for (int i = 0; i < allGuns.Count; i++)
            {
                var gun = allGuns[i];
                if (gun == null) continue;
                if (roster == null) { gun.Manned = true; gun.ReloadScale = 1f; continue; }

                var hand = AssignedCrew(i);
                gun.Manned = hand != null && hand.Available;
                gun.ReloadScale = hand != null ? hand.WorkRate01 : 0f;
            }
        }

        /// How many of a side's guns have someone standing behind them.
        int MannedOn(List<Cannon> side)
        {
            int n = 0;
            foreach (var c in side) if (c != null && c.Manned) n++;
            return n;
        }

        // ---- auto-fire (2026-09-27) -------------------------------------

        /// The target the guns work on their own, or null for manual only.
        ///
        /// Set every frame by `CombatLock` while a lock holds and cleared the
        /// moment it breaks. Kevin, on the phone, 2026-09-27: "cannons should
        /// auto fire when engaged". Locking IS the engagement: once you have
        /// said who the enemy is, steering to bring a side to bear is the
        /// whole of the gunnery, and a second thumb on a fire button while
        /// the first one is on the helm is exactly what a one-thumb phone
        /// cannot do.
        public Combat.IHittable AutoFireTarget { get; set; }

        /// Shots the guns have fired on their own this session, for the
        /// play-mode check (`LockOnCheck`).
        public int AutoShots { get; private set; }

        [Header("Auto-fire")]
        [Tooltip("Fire out to this fraction of the gun's flat-water reach. Below 1 so a shot that falls just short is not the first thing the lock does.")]
        [Range(0.5f, 1.2f)] [SerializeField] float autoFireReach01 = 1f;
        [Tooltip("How much of a raider's motion the crews lay ahead of her, 0..1. At 15 m/s and ~1.3 s of flight an unled ball lands ~20 m astern of her centre -- past her whole hull -- so a lock on a circling raider missed every time it fired.")]
        [Range(0f, 1f)] [SerializeField] float crewLead01 = 1f;

        // Muzzle speed over the water: the guns are laid ~9 deg up.
        const float FlatSpeedOfMuzzle = 0.985f;

        /// **Where a crew lays on a target** (2026-10-02): its hit centre,
        /// plus where a raider will have sailed to by the time the ball
        /// arrives. The handspike traverse already lays the gun on the
        /// target; this only says WHERE on the target, so a ship still has
        /// to bring a side to bear. Two passes, since the lead lengthens
        /// the flight. Beasts carry no velocity and are laid on directly.
        Vector3 AimPointFor(Combat.IHittable t, Vector3 from, float muzzleSpeed)
        {
            Vector3 aim = t.HitCentre;
            if (crewLead01 <= 0f || !(t is Combat.EnemyShip raider)) return aim;
            Vector3 v = raider.Velocity * crewLead01;
            float flat = Mathf.Max(1f, muzzleSpeed * FlatSpeedOfMuzzle);
            for (int i = 0; i < 2; i++)
            {
                Vector3 d = aim - from;
                d.y = 0f;
                aim = t.HitCentre + v * (d.magnitude / flat);
            }
            return aim;
        }

        /// **Can this gun reach the target at all?** Inside its flat range
        /// and inside its traverse arc (rest bearing +- `MaxTraverseDeg`,
        /// widened by the target's own half-width at that range). This is
        /// the gate auto-fire uses and the firing-arc wedge lights on, so
        /// what the wedge shows and what the guns do cannot disagree.
        /// `to` is the flat line from the muzzle to the laid aim point.
        bool InReach(Cannon c, Combat.IHittable t, out Vector3 to, out float dist)
        {
            Vector3 from = c.MuzzlePoint;
            to = AimPointFor(t, from, c.MuzzleSpeed) - from;
            to.y = 0f;
            dist = to.magnitude;
            if (dist < 0.5f || dist > c.FlatRange * autoFireReach01) return false;
            float half = Mathf.Atan2(t.HitRadius, dist) * Mathf.Rad2Deg;
            return Vector3.Angle(c.RestDirection, to) <= c.MaxTraverseDeg + half;
        }

        /// **Each gun decides for itself.** A gun speaks when it is loaded,
        /// manned, the target is inside what it can reach (`InReach`: range
        /// and traverse arc), its crew has finished training onto the laid
        /// aim point (the muzzle assist takes up the last metres), and the
        /// first thing along the line of fire is not a friendly tower.
        ///
        /// 2026-10-02: the old gate was "barrel within the hull's width +
        /// 2.5 m" against the target's CENTRE, with an 18 deg traverse and
        /// no lead -- in practice a near-perfect beam on a raider that was
        /// crossing too fast to hold, and a ball laid astern of her when it
        /// did fire. The window is now the whole 25 deg arc the wedges draw. So nothing new is said about reload,
        /// crew or traverse: those still decide WHEN a gun is ready, and the
        /// helm still decides whether a side bears. What goes away is only
        /// the button press.
        ///
        /// Per gun rather than per side: guns reload on their own crew's
        /// clock, so a side fires as a ripple when it swings onto the target
        /// and then each gun again as its hand gets it loaded.
        void AutoFire()
        {
            var t = AutoFireTarget;
            if (t == null || !t.Alive || t is Combat.IFriendly) return;
            AutoFireSide(starboard, true, t);
            AutoFireSide(port, false, t);
        }

        void AutoFireSide(List<Cannon> side, bool starboardSide, Combat.IHittable t)
        {
            Vector3 carried = motor != null ? motor.Velocity * velocityInheritance : Vector3.zero;
            foreach (var c in side)
            {
                if (c == null || !c.Ready) continue;
                if (!InReach(c, t, out Vector3 to, out float dist)) continue;
                Vector3 from = c.MuzzlePoint;

                // Still swinging onto it (55 deg/s): wait the few frames
                // until the barrel is on, rather than throw the ball wide.
                Vector3 bore = c.FireDirection;
                bore.y = 0f;
                if (bore.sqrMagnitude < 1e-6f) continue;
                float off = Vector3.Angle(bore, to);
                float allow = Mathf.Atan2(t.HitRadius + c.AimAssistCap, dist) * Mathf.Rad2Deg;
                if (off > Mathf.Max(1.5f, allow)) continue;

                // Never through a friend. The first thing along the line
                // from this muzzle to the target must not be one of ours.
                var first = Combat.HitTargets.SweepFirst(from, t.HitCentre, 0.3f,
                                                         out _, self);
                if (first != null && first is Combat.IFriendly) continue;

                if (!c.Fire(carried)) continue;
                AutoShots++;
                Recoil(c, starboardSide);
            }
        }

        /// One gun's reaction on the hull, as in `FireBroadside`.
        void Recoil(Cannon c, bool starboardSide)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null) return;
            float shot = ShotKg[Mathf.Clamp(CalibreLevel, 0, 3)];
            float perGun = shot * MuzzleSpeed * GasFactor * recoilExaggeration;
            Vector3 outward = transform.TransformDirection(
                new Vector3(starboardSide ? 1f : -1f, 0f, 0f));
            rb.AddForceAtPosition(-outward * perGun, c.transform.position, ForceMode.Impulse);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            ServiceGuns();
            TrainSide(starboard, true, dt);
            TrainSide(port, false, dt);
            AutoFire();

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (kb.qKey.wasPressedThisFrame) FireBroadside(false);
            if (kb.eKey.wasPressedThisFrame) FireBroadside(true);
        }

        /// Lay a side's guns on the nearest beast, if one is anywhere near that
        /// beam. The gun clamps to its own traverse limit, so this widens the
        /// window rather than removing the need to steer.
        void TrainSide(List<Cannon> side, bool starboardSide, float dt)
        {
            Vector3? aim = null;

            // The player's hull is a target now, so every lookup has to say
            // who is asking or the guns train on their own ship.
            if (self == null) self = GetComponent<Combat.PlayerHull>();
            // A locked target is the one the guns lay on, even with another
            // raider nearer: the lock is the player saying which one.
            Combat.IHittable target;
            float dist;
            var locked = AutoFireTarget;
            if (locked != null && locked.Alive)
            {
                target = locked;
                Vector3 d = locked.HitCentre - transform.position;
                d.y = 0f;
                dist = d.magnitude;
            }
            else target = NearestHostile(transform.position, out dist, self);
            if (target != null && dist <= GunRange * 1.4f)
            {
                Vector3 toTarget = target.HitCentre - transform.position;
                toTarget.y = 0f;
                float rel = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
                if (Mathf.Abs(Mathf.DeltaAngle(rel, starboardSide ? 90f : -90f)) <= trainWithinDeg)
                    aim = side.Count > 0 && side[0] != null
                        ? AimPointFor(target, side[0].MuzzlePoint, side[0].MuzzleSpeed)
                        : target.HitCentre;
            }

            foreach (var c in side) if (c != null) c.TrainOn(aim, dt);
        }

        /// `HitTargets.Nearest` with friendlies (watchtowers) excluded — the
        /// player's own guns train on raiders, never on the towers helping
        /// them fight. Local rather than a change to `HitTargets`, since a
        /// raider's own guns are meant to see everything.
        static Combat.IHittable NearestHostile(Vector3 pos, out float distance, Combat.IHittable ignore)
        {
            Combat.IHittable best = null;
            float bestSq = float.MaxValue;
            foreach (var t in Combat.HitTargets.All)
            {
                if (t == null || !t.Alive || ReferenceEquals(t, ignore) || t is Combat.IFriendly) continue;
                Vector3 d = t.HitCentre - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = t; }
            }
            distance = best != null ? Mathf.Sqrt(bestSq) : float.PositiveInfinity;
            return best;
        }

        /// **The fire buttons only show with an enemy in range (2026-09-30).**
        /// A lock that holds, or the nearest hostile (`NearestHostile`: an
        /// enemy ship or a sea beast, never a friendly tower) within 1.5x the
        /// guns' flat reach (at least 60 m). Looked up 4x a second, not per
        /// IMGUI event.
        bool EnemyInRange()
        {
            if (Time.unscaledTime < nextEnemyLook) return enemyInRange;
            nextEnemyLook = Time.unscaledTime + 0.25f;
            if (self == null) self = GetComponent<Combat.PlayerHull>();
            var locked = AutoFireTarget;
            if (locked != null && locked.Alive) { nearestHostile = null; return enemyInRange = true; }
            var t = NearestHostile(transform.position, out float dist, self);
            enemyInRange = t != null && dist <= Mathf.Max(GunRange, 40f) * 1.5f;
            nearestHostile = enemyInRange ? t : null;
            return enemyInRange;
        }
        bool enemyInRange;
        float nextEnemyLook;
        Combat.IHittable nearestHostile;

        // ---- what the firing arcs draw (`Combat.FiringArcs`, 2026-10-02) ----

        /// The target the arcs light up for: the lock, else the nearest
        /// hostile that made the combat row show. Null when neither.
        public Combat.IHittable ArcTarget
        {
            get
            {
                if (!EnemyInRange()) return null;
                var locked = AutoFireTarget;
                if (locked != null && locked.Alive) return locked;
                return nearestHostile != null && nearestHostile.Alive ? nearestHostile : null;
            }
        }

        /// Half-angle of a side's arc: the guns' own traverse limit.
        public float ArcHalfDeg => allGuns.Count > 0 && allGuns[0] != null ? allGuns[0].MaxTraverseDeg : 0f;

        /// How far out auto-fire reaches, which is how far the arc is drawn.
        public float ArcRange => GunRange * autoFireReach01;

        /// The middle of a side's guns in ship-local space (y zeroed): the
        /// apex of that side's arc. False for a side with no guns.
        public bool ArcApex(bool starboardSide, out Vector3 local)
        {
            var side = starboardSide ? starboard : port;
            local = Vector3.zero;
            int n = 0;
            foreach (var c in side)
            {
                if (c == null) continue;
                local += transform.InverseTransformPoint(c.transform.position);
                n++;
            }
            if (n == 0) return false;
            local /= n;
            local.y = 0f;
            return true;
        }

        /// Any gun on this side can reach `t` (`InReach`), loaded or not.
        public bool SideReaches(bool starboardSide, Combat.IHittable t)
        {
            if (t == null || !t.Alive) return false;
            foreach (var c in starboardSide ? starboard : port)
                if (c != null && InReach(c, t, out _, out _)) return true;
            return false;
        }

        // ---- the fire controls live in `UI/Sheets/CombatHud` (2026-09-30) ----
        //
        // **The IMGUI "◀ port 1/1 ready" / "stbd ▶" chips are gone** (island
        // UI phase 6, mockup "8b · Sea: combat"): `CombatHud` draws one
        // combat row -- Fire port, Lock / Release, Fire stbd -- above the helm
        // row, in UI Toolkit, and calls `FireBroadside` from its buttons. It
        // reads the readouts below. Q / E (Update) and auto-fire are untouched.

        /// Should the combat row show? Public for `CombatHud`; the same
        /// 4x-a-second lookup the IMGUI chips used.
        public bool EnemyInRangeNow => EnemyInRange();

        /// Hands standing behind a side's guns (0 = "No crew on the guns").
        public int PortManned => MannedOn(port);
        public int StarboardManned => MannedOn(starboard);

        /// A side's reload progress, 0..1, averaged over its guns (the bar
        /// inside the Fire button).
        public float PortLoaded01 => LoadedOn(port);
        public float StarboardLoaded01 => LoadedOn(starboard);

        static float LoadedOn(List<Cannon> side)
        {
            float loaded = 0f;
            foreach (var c in side) if (c != null) loaded += c.ReloadFraction;
            return loaded / Mathf.Max(1, side.Count);
        }
    }
}
