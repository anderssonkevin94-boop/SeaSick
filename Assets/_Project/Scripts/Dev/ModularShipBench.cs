using System.Collections.Generic;
using SeaSick.Ship.Modular;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Dev
{
    /// Test bench for the modular-ship milestone (scene
    /// Scenes/Tests/ModularShipTest.unity, made by "SeaSick/Modular/Create
    /// Test Scene"). Loads the module library from Resources, assembles a
    /// configuration, draws it with ModularShipView and spins the rotor.
    /// Isolated from the sailing game: no ShipMotor, no physics, no save.
    ///
    /// Phone first: thumb-sized IMGUI buttons across the top half, drag
    /// anywhere below them to orbit. A rejected configuration never replaces
    /// the ship on screen -- the last valid one stays, and the reasons show
    /// in a red box.
    public class ModularShipBench : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] float orbitDegreesPerScreen = 360f;

        ModuleLibrary lib;
        ModularShipView view;
        ShipConfiguration valid;
        AssemblyResult validResult;
        List<Rejection> lastRejections = new List<Rejection>();
        string lastAction = "Short";
        string validJson = "";

        float yaw = 215f, pitch = 22f, distance = 18f;
        bool dragging;
        Vector2 lastPointer;
        Vector2 jsonScroll;
        GUIStyle button, label, red, area;
        float guiBottom;

        const string Cannon = "equipment.cannon.placeholder";

        void Start()
        {
            if (cam == null) cam = Camera.main;
            var host = new GameObject("ModularShip");
            host.transform.SetParent(transform, false);
            view = host.AddComponent<ModularShipView>();
            host.AddComponent<RotorSpin>();
            lib = ModuleLibrary.LoadFromResources();
            if (!lib.Ok) Debug.LogWarning("[ModularShipBench] library: " + string.Join("\n", lib.errors));
            Try("Short", ShipConfiguration.Short());
        }

        void Try(string action, ShipConfiguration cfg)
        {
            lastAction = action;
            var r = ShipAssembler.Assemble(cfg, lib);
            lastRejections = r.rejections;
            if (!r.ok) return;
            valid = cfg;
            validResult = r;
            validJson = cfg.ToJson(true);
            view.Build(r);
            distance = Mathf.Max(8f, r.overallLengthM * 1.35f);
        }

        ShipConfiguration Base() => valid != null ? valid.Clone() : ShipConfiguration.Short();

        void Actions()
        {
            Button("Short", () => Try("Short", Keep(ShipConfiguration.Short())));
            Button("Long", () => Try("Long", Keep(ShipConfiguration.Long())));
            Button("Long + 2 bays", () => Try("Long + 2 bays", Keep(ShipConfiguration.WithMiddles(3))));
            Button("Timber wheel", () => { var c = Base(); c.rotorId = ShipConfiguration.TimberRotor; Try("Timber wheel", c); });
            Button("Reinforced wheel", () => { var c = Base(); c.rotorId = ShipConfiguration.ReinforcedRotor; Try("Reinforced wheel", c); });
            Button("Oversized wheel (M1-L)", () => { var c = Base(); c.rotorId = ShipConfiguration.OversizedRotor; Try("Oversized wheel (M1-L)", c); });
            Button("V1 middle (W1)", () => { var c = Base(); c.middleIds.Clear(); c.middleIds.Add("hull.middle.w1.v1"); Try("V1 middle (W1)", c); });
            Button("Cannon on slot", () =>
            {
                var c = Base();
                string slot = c.middleIds.Count > 0 ? "middle[0]/DeckSlot_0_1" : "stern/DeckSlot_2_1";
                c.equipment.RemoveAll(e => e.slotId == slot);
                c.equipment.Add(new EquipmentChoice { slotId = slot, moduleId = Cannon });
                Try("Cannon on slot", c);
            });
            Button("Cannon in passage", () =>
            {
                var c = Base();
                if (c.middleIds.Count == 0) c.middleIds.Add(ShipConfiguration.V3Middle);
                c.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckArea", moduleId = Cannon });
                Try("Cannon in passage", c);
            });
            Button("Raised deck (placeholder)", () =>
            {
                var c = Base();
                c.fittings.RemoveAll(f => f.socketId == "stern/UpperDeckMount");
                c.fittings.Add(new FittingChoice { socketId = "stern/UpperDeckMount", moduleId = "deck.upper.partial.placeholder" });
                Try("Raised deck (placeholder)", c);
            });
        }

        /// Hull presets keep the current wheel choice.
        ShipConfiguration Keep(ShipConfiguration c)
        {
            if (valid != null) { c.rotorId = valid.rotorId; c.carrierId = valid.carrierId; }
            return c;
        }

        // ---- IMGUI ---------------------------------------------------------

        int col, cols;
        float bx, by, bw, bh, pad;
        readonly List<(string, System.Action)> pending = new List<(string, System.Action)>();

        void Button(string text, System.Action act) => pending.Add((text, act));

        void OnGUI()
        {
            float u = Mathf.Max(44f, Mathf.Min(Screen.width, Screen.height) * 0.075f); // >= 44 px, thumb-sized on a phone
            if (button == null)
            {
                button = new GUIStyle(GUI.skin.button) { wordWrap = true };
                label = new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = Color.white } };
                red = new GUIStyle(GUI.skin.box) { wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } };
                area = new GUIStyle(GUI.skin.textArea) { wordWrap = false };
            }
            button.fontSize = Mathf.RoundToInt(u * 0.3f);
            label.fontSize = red.fontSize = Mathf.RoundToInt(u * 0.26f);
            area.fontSize = Mathf.RoundToInt(u * 0.2f);

            pad = u * 0.12f;
            cols = Screen.width > Screen.height ? 5 : 2;
            bw = (Screen.width - pad * (cols + 1)) / cols;
            bh = u;
            bx = pad; by = pad + SafeTop(); col = 0;

            pending.Clear();
            Actions();
            foreach (var (text, act) in pending)
            {
                var rect = new Rect(pad + col * (bw + pad), by, bw, bh);
                if (GUI.Button(rect, text, button)) act();
                if (++col == cols) { col = 0; by += bh + pad; }
            }
            if (col != 0) by += bh + pad;

            float w = Screen.width - 2 * pad;
            string status = validResult == null ? "No valid ship yet." :
                $"Showing: {DescribeValid()}  |  {validResult.overallLengthM:0.00} m, wheel overhang {validResult.wheelOverhangAftM:0.00} m";
            if (validResult != null && validResult.placeholders.Count > 0) status += "\nPLACEHOLDERS: " + string.Join(", ", validResult.placeholders);
            if (view != null && view.missingParts.Count > 0) status += $"\nMissing meshes (import pending?): {view.missingParts.Count}";
            if (!lib.Ok) status += "\nLibrary: " + string.Join(" / ", lib.errors);
            float h = label.CalcHeight(new GUIContent(status), w);
            GUI.Label(new Rect(pad, by, w, h), status, label);
            by += h + pad;

            if (lastRejections.Count > 0)
            {
                var sb = new System.Text.StringBuilder($"REJECTED \"{lastAction}\" -- still showing the last valid ship:");
                foreach (var r in lastRejections) sb.Append("\n• ").Append(r.message).Append("  [").Append(r.code).Append(']');
                string msg = sb.ToString();
                float rh = red.CalcHeight(new GUIContent(msg), w) + pad;
                var old = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.1f, 0.1f, 1f);
                GUI.Box(new Rect(pad, by, w, rh), msg, red);
                GUI.backgroundColor = old;
                by += rh + pad;
            }

            float jh = Mathf.Min(u * 3f, Screen.height * 0.5f - by);
            if (jh > u)
            {
                var outer = new Rect(pad, by, w, jh);
                var inner = new Rect(0, 0, w * 1.6f, area.CalcHeight(new GUIContent(validJson), w * 1.6f));
                jsonScroll = GUI.BeginScrollView(outer, jsonScroll, inner);
                GUI.TextArea(inner, validJson, area);
                GUI.EndScrollView();
                by += jh + pad;
            }
            guiBottom = by;
        }

        string DescribeValid()
        {
            if (valid == null) return "";
            string wheel = valid.rotorId == ShipConfiguration.TimberRotor ? "timber" : valid.rotorId == ShipConfiguration.ReinforcedRotor ? "reinforced" : valid.rotorId;
            return $"{valid.middleIds.Count} middle(s), {wheel} wheel, {valid.equipment.Count} equipment";
        }

        static float SafeTop() => Screen.height - (Screen.safeArea.y + Screen.safeArea.height);

        // ---- orbit camera -------------------------------------------------

        void LateUpdate()
        {
            if (cam == null) return;
            var p = Pointer.current;
            if (p != null)
            {
                Vector2 pos = p.position.ReadValue();
                bool down = p.press.isPressed;
                float guiY = Screen.height - pos.y; // IMGUI y runs downwards
                if (down && !dragging && guiY > guiBottom) { dragging = true; lastPointer = pos; }
                else if (!down) dragging = false;
                if (dragging)
                {
                    var d = pos - lastPointer;
                    lastPointer = pos;
                    float perPx = orbitDegreesPerScreen / Mathf.Max(1, Screen.width);
                    yaw += d.x * perPx;
                    pitch = Mathf.Clamp(pitch - d.y * perPx, -10f, 80f);
                }
            }
            var m = Mouse.current;
            if (m != null) distance = Mathf.Clamp(distance * (1f - m.scroll.ReadValue().y * 0.001f), 4f, 60f);

            var target = new Vector3(0f, 0.6f, validResult != null ? validResult.overallLengthM * 0.5f : 5f);
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
        }
    }
}
