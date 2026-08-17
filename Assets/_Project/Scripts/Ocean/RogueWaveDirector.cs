using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Schedules rogue wave sets: every so often a big swell rolls through.
    /// Seamanship applies automatically via the seating model — take it on the
    /// bow (pitch over the long hull) and you're fine; take it on the beam
    /// (roll over the narrow one) and the crew's stomachs pay.
    [RequireComponent(typeof(WaveField))]
    public class RogueWaveDirector : MonoBehaviour
    {
        [SerializeField] Vector2 intervalRange = new Vector2(28f, 50f);
        [SerializeField] float swellDuration = 18f;
        [SerializeField] float swellSteepness = 0.55f;
        [SerializeField] float swellWavelength = 68f;
        [SerializeField] float warningSeconds = 8f;

        WaveField field;
        ShipMotor ship;
        float nextAt;
        float activeStart = -999f;
        Vector2 activeDir;
        GUIStyle style;

        void Start()
        {
            field = GetComponent<WaveField>();
            ship = FindFirstObjectByType<ShipMotor>();
            nextAt = Time.time + Random.Range(14f, 24f); // first set comes early
        }

        void Update()
        {
            if (Time.time >= nextAt)
            {
                float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                activeDir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                field.TriggerSwell(activeDir, swellDuration, swellSteepness, swellWavelength);
                activeStart = Time.time;
                nextAt = Time.time + swellDuration + Random.Range(intervalRange.x, intervalRange.y);
            }
        }

        void OnGUI()
        {
            float sinceStart = Time.time - activeStart;
            if (sinceStart < 0f || sinceStart > warningSeconds) return;
            if (style == null)
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 17,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };

            var r = new Rect(0f, Screen.height * 0.30f, Screen.width, 30f);
            GUI.color = new Color(0.9f, 0.55f, 0.1f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, $"Big swell rolling in — {RelativeCall()}!", style);
        }

        /// Where the swell hits relative to the bow, in sailor-speak.
        string RelativeCall()
        {
            if (ship == null) return "brace";
            Vector3 fwd = ship.transform.forward;
            // The swell travels toward activeDir, so it arrives FROM -activeDir.
            Vector3 from = new Vector3(-activeDir.x, 0f, -activeDir.y);
            float signed = Vector3.SignedAngle(fwd, from, Vector3.up);
            string side = signed < 0f ? "port" : "starboard";
            float a = Mathf.Abs(signed);
            if (a < 25f) return "dead ahead";
            if (a < 70f) return $"off the {side} bow";
            if (a < 110f) return $"on the {side} beam — turn into it";
            if (a < 155f) return $"on the {side} quarter";
            return "from astern";
        }
    }
}
