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
        [SerializeField] float recoilRollPerGun = 14f;

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

            var wood = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            wood.SetColor("_BaseColor", new Color(0.38f, 0.24f, 0.14f));
            wood.SetFloat("_Smoothness", 0.12f);

            var iron = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            iron.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.17f));
            iron.SetFloat("_Smoothness", 0.45f);

            Make("CannonPortFore", -forePosition.x, forePosition.y, -1f, port, wood, iron);
            Make("CannonPortAft", -aftPosition.x, aftPosition.y, -1f, port, wood, iron);
            Make("CannonStarFore", forePosition.x, forePosition.y, 1f, starboard, wood, iron);
            Make("CannonStarAft", aftPosition.x, aftPosition.y, 1f, starboard, wood, iron);

            PostGunCrews();
        }

        [Header("Gun crews")]
        [Tooltip("How far inboard of their gun the gunner stands.")]
        [SerializeField] float gunnerInboard = 0.72f;
        [Tooltip("How far outboard of the gun the rail is — a gunner heaves out their own port.")]
        [SerializeField] float gunportOutboard = 0.55f;

        /// Stand each assigned hand at the gun they work. This is what makes
        /// the mechanic legible: a silent gun has a visibly empty place behind
        /// it, and you can watch the person who should be there walk away.
        void PostGunCrews()
        {
            if (roster == null) roster = GetComponent<Crew.CrewRoster>();
            if (roster == null) return;

            for (int i = 0; i < allGuns.Count; i++)
            {
                var hand = roster.GunCrew(i);
                if (hand == null || allGuns[i] == null) continue;

                Vector3 gun = allGuns[i].transform.localPosition;
                float side = Mathf.Sign(gun.x);
                hand.AssignStation(
                    new Vector3(gun.x - side * gunnerInboard, gun.y - 0.05f, gun.z),
                    new Vector3(gun.x + side * gunportOutboard, gun.y - 0.05f, gun.z));
            }
        }

        /// Where a side's guns are trained to cross, in ship-local space:
        /// straight out on the beam, level with the middle of the battery.
        float BatteryMidZ => (forePosition.y + aftPosition.y) * 0.5f;

        void Make(string name, float x, float z, float sideSign, List<Cannon> side,
            Material wood, Material iron)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, deckHeight, z);

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
                if (motor != null)
                    motor.AddRecoilRoll(recoilRollPerGun * fired * (starboardSide ? 1f : -1f));

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

                var hand = roster.GunCrew(i);
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

        void Update()
        {
            float dt = Time.deltaTime;
            ServiceGuns();
            TrainSide(starboard, true, dt);
            TrainSide(port, false, dt);

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
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
            var target = Combat.HitTargets.Nearest(transform.position, out float dist, self);
            if (target != null && dist <= GunRange * 1.4f)
            {
                Vector3 toTarget = target.HitCentre - transform.position;
                toTarget.y = 0f;
                float rel = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
                if (Mathf.Abs(Mathf.DeltaAngle(rel, starboardSide ? 90f : -90f)) <= trainWithinDeg)
                    aim = target.HitCentre;
            }

            foreach (var c in side) if (c != null) c.TrainOn(aim, dt);
        }

        void OnGUI()
        {
            int u = UITheme.Unit;
            float pad = u * 0.7f;
            // Wide enough for "stbd ▶ 2/2" and for "no crew" — at 5.6 both
            // clipped to "port 2/".
            float bw = u * 7.4f;
            float bh = u * 2.2f;
            float y = Screen.height - pad - u * 2.6f - bh;

            DrawSide(new Rect(pad, y, bw, bh), false, PortReady, "◀ port");
            DrawSide(new Rect(pad + bw + u * 0.4f, y, bw, bh), true, StarboardReady, "stbd ▶");
        }

        void DrawSide(Rect r, bool starboardSide, int ready, string label)
        {
            UIBlocker.Block(r);
            var style = UITheme.Button;   // cached; copying it per frame bought nothing
            GUI.enabled = ready > 0;
            var side = starboardSide ? starboard : port;
            // Say WHY the side is silent. "0/2" reads as a reload; "no crew"
            // reads as the two people who are supposed to be there being
            // somewhere else, which is the actual situation.
            int manned = MannedOn(side);
            string text = manned == 0
                ? $"{label}  no crew"
                : $"{label}  {ready}/{side.Count}";
            if (GUI.Button(r, text, style)) FireBroadside(starboardSide);
            GUI.enabled = true;

            // Reload progress under the button.
            float loaded = 0f;
            foreach (var c in side) if (c != null) loaded += c.ReloadFraction;
            loaded /= Mathf.Max(1, side.Count);
            UITheme.Bar(new Rect(r.x, r.yMax + 2f, r.width, UITheme.Unit * 0.35f), loaded,
                ready > 0 ? UITheme.Good : UITheme.Warn);
        }
    }
}
