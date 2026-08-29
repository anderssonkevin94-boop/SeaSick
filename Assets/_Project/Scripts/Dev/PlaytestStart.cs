using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.Terrain;

namespace SeaSick.Dev
{
    /// Drops the ship straight into the weather you actually want to test.
    ///
    /// The voyage properly begins at home in the calm shelf, and sailing west
    /// to the storm under her own steam is minutes of nothing every time you
    /// press play. This spawns her out there instead.
    ///
    /// It does NOT take a distance on faith. WestProbe found that 1500 m due
    /// west — the obvious number — is forty metres ABOVE sea level: there is an
    /// island chain across the western approach. This is the same trap that
    /// left StallProbe warping into twelve metres of water and reporting
    /// nothing for weeks. So the spawn SEARCHES: it walks out along the bearing
    /// looking for the nearest pocket that is genuinely deep, genuinely clear
    /// for a radius around it, and genuinely inside the storm, and it asserts
    /// what it found in the log rather than assuming.
    ///
    /// "Deep" has a specific meaning. The envelope caps a wave at
    /// breakFraction x depth / Hs, so at the shipped 0.55 and the storm's
    /// nominal Hs the sea does not reach full height until roughly 120 m of
    /// water. Spawning shallower gives you a quietly shrunk sea that reads as
    /// an ocean tuning problem — which is the single most expensive way to
    /// waste a playtest.
    ///
    /// The ship moves in Awake, before the terrain streamer or the clipmap have
    /// looked at her, so the world streams around the real spawn from frame one
    /// instead of snapping across kilometres. HomePoint does not move: home
    /// stays put, so RegionField's envelope, the distance readout and the
    /// navigation aid all still measure from where the voyage started.
    ///
    /// Turn `active` off to get the real spawn back.
    [DefaultExecutionOrder(-200)]
    public class PlaytestStart : MonoBehaviour
    {
        [Tooltip("Off = the ship spawns where the scene puts her, at home.")]
        [SerializeField] bool active = true;

        [Tooltip("Which way out. Default is west, matching RegionField.stormBearing — west is the open ocean and the storms.")]
        [SerializeField] Vector2 bearing = new Vector2(-1f, 0f);

        [Tooltip("Don't bother searching closer than this; inside it the storm weight hasn't come up yet. RegionField reaches full storm at stormFar, 1250 m by default.")]
        [SerializeField] float minDistance = 1300f;
        [SerializeField] float maxDistance = 6000f;

        [Tooltip("Metres of water the spawn needs. The depth limit holds the sea to breakFraction x depth / Hs, so below about 120 m the storm is capped and you are testing a smaller sea than you think.")]
        [SerializeField] float requiredDepth = 130f;

        [Tooltip("That depth must hold everywhere within this radius, so the first stretch of sailing doesn't run onto a bank. At 20 m/s the ship covers 800 m in forty seconds, which is about how long a look at the water takes.")]
        [SerializeField] float clearRadius = 800f;

        [Tooltip("How far off the bearing the search may wander to find water. On-axis candidates are always tried first.")]
        [SerializeField] float maxLateral = 1600f;

        [Tooltip("Face the ship along the bearing so she starts bow-on to the swell rather than beam-on in a trough.")]
        [SerializeField] bool faceOutward = true;

        void Awake()
        {
            if (!active) return;

            var motor = FindFirstObjectByType<ShipMotor>();
            if (motor == null) { Debug.LogWarning("PlaytestStart: no ShipMotor in the scene"); return; }

            var voyage = FindFirstObjectByType<Voyage.VoyageManager>();
            Vector3 home = voyage != null && voyage.HomePoint != null
                ? voyage.HomePoint.position : Vector3.zero;
            float2 home2 = new float2(home.x, home.z);

            Vector2 dir2 = bearing.sqrMagnitude > 1e-6f ? bearing.normalized : Vector2.left;
            float2 dir = new float2(dir2.x, dir2.y);
            float2 side = new float2(-dir.y, dir.x);

            if (!TryFindWater(home2, dir, side, out float2 spot, out float depth, out float room))
            {
                Debug.LogWarning($"PlaytestStart: no water at least {requiredDepth:F0} m deep and " +
                                 $"{clearRadius:F0} m clear within {maxDistance:F0} m along {dir2}. " +
                                 "Spawn left at home — run WestProbe and look at the coastline.");
                return;
            }

            Vector3 p = motor.transform.position;
            p.x = spot.x; p.z = spot.y;

            motor.transform.position = p;
            if (faceOutward)
                motor.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);

            // The hull is a Rigidbody. Writing the transform alone leaves the
            // body's cached pose at the old spot until the next physics step,
            // which is a FixedUpdate of buoyancy solved kilometres from where
            // the ship is drawn.
            var rb = motor.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = p;
                if (faceOutward) rb.rotation = motor.transform.rotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            float dist = math.distance(spot, home2);
            float along = math.dot(spot - home2, dir);
            Debug.Log($"PlaytestStart: spawned at ({spot.x:F0}, {spot.y:F0}) — {depth:F0} m of water, " +
                      $"clear for {room:F0} m, {dist:F0} m from home ({along:F0} m along the bearing).");
        }

        /// On-axis first, nearest second. The lateral offset is the OUTER loop
        /// on purpose: searching by distance first finds the nearest water at
        /// any bearing, which out here means 1200 m off to one side, and a
        /// spawn that is nominally "west" but actually south-west of an island
        /// chain. Sailing west is the design; hold the axis and go further out
        /// for it.
        bool TryFindWater(float2 home, float2 dir, float2 side,
                          out float2 spot, out float depth, out float room)
        {
            spot = home; depth = 0f; room = 0f;

            var settings = FindTerrainSettings();
            if (settings == null)
            {
                Debug.LogWarning("PlaytestStart: no TerrainSettings found — cannot check depth, not moving the ship.");
                return false;
            }

            var prm = TerrainParams.From(settings);
            var lut = TerrainCurveLut.Bake(settings.profileCurve, Allocator.Temp);
            try
            {
                for (float lat = 0f; lat <= maxLateral; lat += 200f)
                    // +lat and -lat both, nearest-to-axis first.
                    for (int sgn = 0; sgn < (lat < 1f ? 1 : 2); sgn++)
                    {
                        float offset = sgn == 0 ? lat : -lat;
                        for (float d = minDistance; d <= maxDistance; d += 100f)
                        {
                            float2 p = home + dir * d + side * offset;
                            float worst = WorstDepth(p, prm, lut);
                            if (worst < requiredDepth) continue;
                            spot = p;
                            depth = -TerrainHeight.Height(p, prm, lut);
                            room = clearRadius;
                            return true;
                        }
                    }
            }
            finally { lut.Dispose(); }
            return false;
        }

        /// Shallowest water within clearRadius: centre, plus two rings so a
        /// bank between the samples has nowhere to hide.
        float WorstDepth(float2 p, in TerrainParams prm, in NativeArray<float> lut)
        {
            float worst = -TerrainHeight.Height(p, prm, lut);
            for (int ring = 1; ring <= 2; ring++)
            {
                float r = clearRadius * ring / 2f;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * (Mathf.PI * 2f / 12f);
                    float2 q = p + new float2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    worst = math.min(worst, -TerrainHeight.Height(q, prm, lut));
                    if (worst < requiredDepth) return worst;   // early out
                }
            }
            return worst;
        }

        static TerrainSettings FindTerrainSettings()
        {
            var streamer = FindFirstObjectByType<TerrainStreamer>(FindObjectsInactive.Include);
            if (streamer != null && streamer.settings != null) return streamer.settings;
            var shore = FindFirstObjectByType<TerrainShoreField>(FindObjectsInactive.Include);
            return shore != null ? shore.settings : null;
        }
    }
}
