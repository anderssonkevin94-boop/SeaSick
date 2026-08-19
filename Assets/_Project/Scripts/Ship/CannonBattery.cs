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

        readonly List<Cannon> port = new List<Cannon>();
        readonly List<Cannon> starboard = new List<Cannon>();
        ShipMotor motor;

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
            return fired;
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.qKey.wasPressedThisFrame) FireBroadside(false);
            if (kb.eKey.wasPressedThisFrame) FireBroadside(true);
        }

        void OnGUI()
        {
            int u = UITheme.Unit;
            float pad = u * 0.7f;
            float bw = u * 5.6f;
            float bh = u * 2.2f;
            float y = Screen.height - pad - u * 2.6f - bh;

            DrawSide(new Rect(pad, y, bw, bh), false, PortReady, "◀ port");
            DrawSide(new Rect(pad + bw + u * 0.4f, y, bw, bh), true, StarboardReady, "stbd ▶");
        }

        void DrawSide(Rect r, bool starboardSide, int ready, string label)
        {
            UIBlocker.Block(r);
            var style = new GUIStyle(UITheme.Button);
            GUI.enabled = ready > 0;
            var side = starboardSide ? starboard : port;
            if (GUI.Button(r, $"{label}  {ready}/{side.Count}", style)) FireBroadside(starboardSide);
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
