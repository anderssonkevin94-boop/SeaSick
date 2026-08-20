using System.Collections.Generic;
using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Sends mountain seas rolling across the western deep, and tells the
    /// player one is coming.
    ///
    /// They are a *region*, not a global weather event: the folklore says the
    /// waves out west are the size of mountains, and out west is where they
    /// are. Close to home the tribe's stories are just stories.
    ///
    /// They roam rather than ambush — one is visible on the horizon long
    /// before it arrives, so being caught by one is a decision you made or a
    /// watch you failed to keep, never something the game did to you.
    public class MountainSeaDirector : MonoBehaviour
    {
        [Header("Where they live")]
        [Tooltip("Bearing of the deep, in degrees. 270 = due west.")]
        [SerializeField] float regionBearingDeg = 270f;
        [Tooltip("How wide the region is, either side of that bearing.")]
        [SerializeField] float regionHalfAngle = 62f;
        [Tooltip("No mountain seas closer to home than this.")]
        [SerializeField] float minDistanceFromHome = 700f;

        [Header("Cadence")]
        [SerializeField] float spawnInterval = 55f;
        [SerializeField] int maxAlive = 2;
        [Tooltip("How far ahead of the ship one is launched — far enough to see it coming.")]
        [SerializeField] float spawnLead = 900f;
        [SerializeField] float despawnBehind = 700f;

        /// True while a warning plate is on screen, so the swell readout can
        /// stand aside — a mountain sea is the more urgent thing by far.
        public static bool WarningShowing { get; private set; }

        readonly List<MountainSea> alive = new List<MountainSea>();
        Ship.ShipMotor ship;
        Transform home;
        Material water;
        float timer;

        void Start()
        {
            ship = FindAnyObjectByType<Ship.ShipMotor>();
            var voyage = FindAnyObjectByType<Voyage.VoyageManager>();
            if (voyage != null) home = voyage.HomePoint;

            water = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            water.SetColor("_BaseColor", new Color(0.07f, 0.24f, 0.38f, 1f));
            water.SetFloat("_Smoothness", 0.72f);
            timer = spawnInterval * 0.4f;
        }

        void Update()
        {
            if (ship == null) return;
            alive.RemoveAll(m => m == null);

            Cull();

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = spawnInterval;
                if (alive.Count < maxAlive && ShipIsInTheDeep()) Launch();
            }
        }

        bool ShipIsInTheDeep()
        {
            if (home == null) return false;
            Vector3 fromHome = ship.transform.position - home.position;
            fromHome.y = 0f;
            if (fromHome.magnitude < minDistanceFromHome) return false;

            float bearing = Mathf.Atan2(fromHome.x, fromHome.z) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(bearing, regionBearingDeg)) <= regionHalfAngle;
        }

        /// Launch one ahead of the ship, running across her. Given a heading to
        /// cross rather than chase, so it can always be dodged by turning.
        void Launch()
        {
            float bearing = regionBearingDeg + Random.Range(-40f, 40f);
            Vector2 travel = new Vector2(
                Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));

            // Put it out along its own back-bearing, so it comes at her.
            Vector3 origin = ship.transform.position
                - new Vector3(travel.x, 0f, travel.y) * spawnLead
                + new Vector3(-travel.y, 0f, travel.x) * Random.Range(-160f, 160f);

            alive.Add(MountainSea.Spawn(origin, travel, water));
        }

        void Cull()
        {
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                var m = alive[i];
                if (m == null) { alive.RemoveAt(i); continue; }
                if (m.SignedDistanceTo(ship.transform.position) < -despawnBehind)
                {
                    Destroy(m.gameObject);
                    alive.RemoveAt(i);
                }
            }
        }

        /// The one that matters: closest still to arrive.
        public MountainSea Threat
        {
            get
            {
                MountainSea best = null;
                float soonest = float.MaxValue;
                foreach (var m in alive)
                {
                    if (m == null || ship == null) continue;
                    if (m.AlongCrest(ship.transform.position) > m.CrestLength * 0.5f) continue;
                    float s = m.SecondsTo(ship.transform.position);
                    if (s > 0f && s < soonest) { soonest = s; best = m; }
                }
                return best;
            }
        }

        void OnGUI()
        {
            WarningShowing = false;
            var threat = Threat;
            if (threat == null || ship == null) return;

            float secs = threat.SecondsTo(ship.transform.position);
            if (secs < 0f || secs > 75f) return;

            WarningShowing = true;

            // How she's lying relative to it right now — this is the entire
            // skill of surviving one, so it has to be readable at a glance.
            Vector3 comingFrom = new Vector3(-threat.Travel.x, 0f, -threat.Travel.y);
            float angle = Vector3.Angle(ship.transform.forward, comingFrom);
            float beam = Mathf.Clamp01(Mathf.Sin(angle * Mathf.Deg2Rad));

            int u = UITheme.Unit;
            float w = u * 13f;
            float x = (Screen.width - w) * 0.5f;
            float y = u * 5.2f;

            UITheme.Rect(new Rect(x, y, w, u * 4.4f), UITheme.PanelSolid);
            UITheme.Rect(new Rect(x, y, w, 2f), beam > 0.5f ? UITheme.Bad : UITheme.Warn);

            GUI.Label(new Rect(x, y + u * 0.25f, w, u * 1.5f),
                $"mountain sea  ·  {secs:F0}s", UITheme.Small2Centered);

            // Not "turn left" — just how square she's lying. The player works
            // out the rest, which is the part worth learning.
            GUI.Label(new Rect(x, y + u * 1.6f, w, u * 1.4f),
                beam > 0.5f ? "she's lying across it" : "bow into it", UITheme.Small2Centered);

            var bar = new Rect(x + u * 0.7f, y + u * 3.2f, w - u * 1.4f, u * 0.5f);
            UITheme.Bar(bar, 1f - beam, UITheme.Ramp(beam));
        }
    }
}
