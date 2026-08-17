using SeaSick.Ship;
using SeaSick.Voyage;
using UnityEngine;

namespace SeaSick.Crew
{
    /// The escalation ladder. Average crew anger drives four visible stages —
    /// the player must always see a mutiny coming:
    ///   1 grumbling (warning banner, red flushes on bodies)
    ///   2 refusing stations (sail capped at 60%)
    ///   3 mutiny (crew seize the helm and steer for home)
    ///   4 ditching cargo (loot goes overboard to get home faster)
    public class MutinyController : MonoBehaviour
    {
        [SerializeField] float grumbleAt = 0.25f;
        [SerializeField] float refuseAt = 0.5f;
        [SerializeField] float mutinyAt = 0.75f;
        [SerializeField] float ditchAt = 0.9f;
        [SerializeField] float ditchInterval = 6f;
        [SerializeField] int ditchAmount = 3;

        public int Stage { get; private set; }
        public float CrewAnger { get; private set; }

        ShipMotor motor;
        CrewAgent[] crew;
        VoyageManager voyage;
        float ditchTimer;
        GUIStyle style;

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            crew = GetComponentsInChildren<CrewAgent>();
            voyage = FindFirstObjectByType<VoyageManager>();
        }

        void Update()
        {
            if (crew.Length == 0) return;

            float sum = 0f;
            foreach (var c in crew) sum += c.Anger01;
            CrewAnger = sum / crew.Length;

            Stage = CrewAnger >= ditchAt ? 4
                : CrewAnger >= mutinyAt ? 3
                : CrewAnger >= refuseAt ? 2
                : CrewAnger >= grumbleAt ? 1 : 0;

            motor.SailCap = Stage >= 2 ? 0.6f : 1f;
            motor.AutopilotTarget = Stage >= 3 && voyage != null
                ? voyage.HomePoint.position
                : (Vector3?)null;

            if (Stage >= 4 && voyage != null)
            {
                ditchTimer -= Time.deltaTime;
                if (ditchTimer <= 0f)
                {
                    voyage.DitchCargo(ditchAmount);
                    ditchTimer = ditchInterval;
                }
            }
            else
            {
                ditchTimer = 1.5f; // first splash shortly after stage 4 begins
            }
        }

        void OnGUI()
        {
            if (Stage == 0) return;
            if (style == null)
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 17,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };

            string msg = Stage switch
            {
                1 => "The crew grumbles about the rough ride…",
                2 => "The crew refuses full sail!",
                3 => "MUTINY — they've seized the helm and turned for home!",
                _ => "They're throwing the cargo overboard!",
            };

            var r = new Rect(0f, Screen.height * 0.235f, Screen.width, 32f);
            GUI.color = new Color(0.55f, 0.08f, 0.05f, Stage >= 3 ? 0.8f : 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, msg, style);
        }
    }
}
