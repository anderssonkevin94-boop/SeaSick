using System.Collections.Generic;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Four guns on the rail — two a side, one forward and one aft of the mast,
    /// set inboard of the bulwark and trained square out over the beam.
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

        readonly List<Cannon> port = new List<Cannon>();
        readonly List<Cannon> starboard = new List<Cannon>();

        public int PortReady => CountReady(port);
        public int StarboardReady => CountReady(starboard);

        void Start()
        {
            var wood = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            wood.SetColor("_BaseColor", new Color(0.38f, 0.24f, 0.14f));
            wood.SetFloat("_Smoothness", 0.12f);

            var iron = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            iron.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.17f));
            iron.SetFloat("_Smoothness", 0.45f);

            Make("CannonPortFore", -forePosition.x, forePosition.y, -90f, port, wood, iron);
            Make("CannonPortAft", -aftPosition.x, aftPosition.y, -90f, port, wood, iron);
            Make("CannonStarFore", forePosition.x, forePosition.y, 90f, starboard, wood, iron);
            Make("CannonStarAft", aftPosition.x, aftPosition.y, 90f, starboard, wood, iron);
        }

        void Make(string name, float x, float z, float yaw, List<Cannon> side,
            Material wood, Material iron)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, deckHeight, z);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
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
            int fired = 0;
            foreach (var c in side) if (c != null && c.Fire()) fired++;
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
            if (GUI.Button(r, $"{label}  {ready}/2", style)) FireBroadside(starboardSide);
            GUI.enabled = true;

            // Reload progress under the button.
            var side = starboardSide ? starboard : port;
            float loaded = 0f;
            foreach (var c in side) if (c != null) loaded += c.ReloadFraction;
            loaded /= Mathf.Max(1, side.Count);
            UITheme.Bar(new Rect(r.x, r.yMax + 2f, r.width, UITheme.Unit * 0.35f), loaded,
                ready > 0 ? UITheme.Good : UITheme.Warn);
        }
    }
}
