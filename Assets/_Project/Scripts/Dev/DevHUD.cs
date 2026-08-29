using SeaSick.Crew;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Dev
{
    /// Development overlay: speed, sail, wind angle, and the roughness meter.
    /// IMGUI on purpose — zero setup, deleted before release. Toggle with F1.
    [RequireComponent(typeof(ShipMotor), typeof(SmoothnessMeter))]
    public class DevHUD : MonoBehaviour
    {
        // Off by default now that StatusHUD owns the permanent readouts.
        // F1 brings back the full diagnostic wall when tuning.
        [SerializeField] bool visible = false;

        ShipMotor motor;
        SmoothnessMeter meter;
        CrewAgent[] crew;
        GUIStyle label;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            meter = GetComponent<SmoothnessMeter>();
        }

        void Start() { crew = GetComponentsInChildren<CrewAgent>(); }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible) return;
            const int FontSize = 16; // constant: view-size-derived values proved unreliable
            if (label == null || label.fontSize != FontSize)
                label = new GUIStyle(GUI.skin.label)
                {
                    fontSize = FontSize,
                    fontStyle = FontStyle.Bold,
                };

            float w = Screen.width * 0.6f;
            float x = 12f, y = 12f;
            const float line = FontSize * 1.7f;

            int crewCount = crew != null ? crew.Length : 0;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(6f, 6f, w + 12f, line * (4.4f + crewCount * 1.75f)), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Vector3 fwd = motor.transform.forward;
            float windAngle = Vector3.SignedAngle(
                new Vector3(fwd.x, 0f, fwd.z),
                new Vector3(motor.WindDirection.x, 0f, motor.WindDirection.y),
                Vector3.up);

            GUI.Label(new Rect(x, y, w, line),
                $"speed {motor.CurrentSpeed:F1} m/s  ({motor.CurrentSpeed / motor.MaxSpeed:P0})   drift {motor.DriftAngleDeg:F0}°", label);
            string gust = motor.GustFactor01 > 0.1f ? $"   GUST ×{motor.WindStrength:F2}" : "";
            GUI.Label(new Rect(x, y += line, w, line),
                $"engine {motor.Throttle:P0}   wind {windAngle:F0}° off bow{gust}", label);
            var hull = motor.GetComponent<HullIntegrity>();
            string hullText = hull != null ? $"   hull {hull.Integrity01:P0}" : "";
            GUI.Label(new Rect(x, y += line, w, line),
                $"roughness {meter.Roughness01:F2}{hullText}", label);

            // Roughness bar: green calm, red rough.
            var barRect = new Rect(x, y + line * 1.1f, w, line * 0.5f);
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.3f, 0.85f, 0.35f), new Color(0.9f, 0.25f, 0.2f), meter.Roughness01);
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * meter.Roughness01, barRect.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (crew == null) return;
            y = barRect.yMax + line * 0.2f;
            foreach (var c in crew)
            {
                if (c == null) continue;
                string who = c.Def != null ? c.Def.displayName : "crew";
                GUI.Label(new Rect(x, y, w, line),
                    $"{who}: sick {c.Sickness01:F2}  work {c.WorkRate01:F2}  [{c.StateName}]", label);
                var sickRect = new Rect(x, y + line, w, line * 0.4f);
                GUI.color = new Color(0f, 0f, 0f, 0.4f);
                GUI.DrawTexture(sickRect, Texture2D.whiteTexture);
                GUI.color = Color.Lerp(new Color(0.85f, 0.75f, 0.4f), new Color(0.45f, 0.8f, 0.3f), c.Sickness01);
                GUI.DrawTexture(new Rect(sickRect.x, sickRect.y, sickRect.width * c.Sickness01, sickRect.height),
                    Texture2D.whiteTexture);
                GUI.color = Color.white;
                y += line * 1.75f;
            }
        }
    }
}
