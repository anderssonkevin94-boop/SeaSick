using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.World;

/// Matched-camera look sheet for the islands, so a terrain change can be
/// judged as a BEFORE and an AFTER of the same island from the same three
/// eyes rather than from wherever the ship happened to drift.
///
/// The vantages are the three that Kevin's complaints live at:
///   0  offshore, at the height of a man on deck — the silhouette. This is
///      the "islands feel small" shot; the ship is left in frame at a known
///      24.2 m so the eye has its ruler.
///   1  close in on the shore, low — the "no beach to land on" shot.
///   2  high and back — the massing and the skyline, which is where a
///      terrace staircase gives itself away.
///
/// The camera FRAMES the island rather than sitting at fixed coordinates:
/// the world is procedural and a hardcoded vantage eventually looks at open
/// water (this project has shipped that bug before). It picks the island
/// nearest the ship, backs off by its measured radius, and looks at the
/// highest ground it can find.
///
/// NOT in an Editor folder: a MonoBehaviour there cannot be AddComponent-ed
/// and comes back null. Writes /tmp/seasick-look-N.png and a text sheet.
public class IslandLook : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("IslandLook: not in play mode"); return; }
        var old = FindAnyObjectByType<IslandLook>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("IslandLook").AddComponent<IslandLook>();
    }

    /// Written next to the shots so a picture can never be confused about
    /// which terrain built it -- the whole point of an A/B.
    public static string Tag = "run";

    IEnumerator Start()
    {
        // Mid-morning, held still. A look sheet shot at whatever hour the
        // world happened to be at is not comparable to the next one -- the
        // first baseline came back at dusk, a grey silhouette against a grey
        // sky, which judges the lighting rather than the landform. 0.36 is
        // high enough to light the ground and low enough to keep slopes
        // reading through shadow.
        SeaSick.World.TimeOfDay.SetTime01(0.36f);
        SeaSick.World.TimeOfDay.Paused = true;

        // A calm sea, for two reasons that are both about judging the LAND.
        // The sky's overcast is driven by storminess, so a storm sheet is
        // shot under flat grey light that hides every slope; and a low
        // camera near the shore is simply submerged by storm crests, which
        // is what swallowed the first shore shot whole.
        var sea = SeaSick.Ocean.SeaStateController.Instance;
        if (sea != null) sea.ForceSeverity(0.12f);
        yield return new WaitForSeconds(2f);

        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var streamer = FindAnyObjectByType<SeaSick.Terrain.TerrainStreamer>();
        var sb = new StringBuilder();

        if (Island.All.Count == 0)
        {
            System.IO.File.WriteAllText("/tmp/seasick-look.txt", "IslandLook: no islands registered\n");
            Debug.LogError("IslandLook: no islands registered");
            yield break;
        }

        // The island nearest the ship, so the streamer already has it.
        Vector3 shipAt = motor != null ? motor.transform.position : Vector3.zero;
        Island target = null;
        float best = float.MaxValue;
        foreach (var isl in Island.All)
        {
            float d = Vector3.Distance(isl.transform.position, shipAt);
            if (d < best) { best = d; target = isl; }
        }
        Vector3 c = target.transform.position;
        float r = Mathf.Max(target.Radius, 60f);
        sb.AppendLine($"island at {c:F0}, radius {r:F0} m, {best:F0} m from the ship");

        Vector3 approach = shipAt - c; approach.y = 0f;
        if (approach.sqrMagnitude < 1f) approach = Vector3.forward;
        approach.Normalize();

        // Highest ground within the island, sampled on a coarse polar grid.
        // The camera aims here so a peak, if there is one, is in frame -- and
        // its height is the number the "too small" complaint is really about.
        Vector3 peak = c; float peakY = -999f;
        for (int ring = 1; ring <= 6; ring++)
        {
            for (int a = 0; a < 24; a++)
            {
                float ang = a / 24f * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * (r * ring / 6f);
                float y = Ground(p);
                if (y > peakY) { peakY = y; peak = new Vector3(p.x, y, p.z); }
            }
        }
        sb.AppendLine($"highest ground found: {peakY:F1} m at {peak:F0}  "
            + $"({peakY / 24.24f:F2} ship-lengths; she is 24.2 m overall)");

        // Look from the sea, on the bearing from the ship, so the shot is
        // reproducible whichever island it picks.
        Vector3 seaward = approach;

        var main = Camera.main;
        var cam = new GameObject("LookCam").AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.depth = 100f;
        cam.farClipPlane = 6000f;

        // Point the STREAMER at the look camera, not at the ship.
        //
        // This is what made the first three attempts photograph open water
        // with trees hanging in the air over it. The chunk radius follows the
        // streamer's target, the target is the ship, and SHE SAILS HERSELF --
        // parking her and zeroing her velocity buys a few seconds before she
        // is under way again. By the time the sheet was shot she had made
        // 1790 m from the island, taking every loaded chunk with her, while
        // the props stayed where the height field had put them. The terrain
        // was never wrong; nothing was built where the camera was looking.
        var streamerTarget = streamer != null ? streamer.target : null;
        if (streamer != null) streamer.target = cam.transform;

        Vector3[] from =
        {
            c + seaward * (r + 260f) + Vector3.up * 9f,     // offshore, deck height
            c + seaward * (r + 55f) + Vector3.up * 16f,     // close in on the shore
            c + seaward * (r + 380f) + Vector3.up * 260f,   // high and back
        };
        Vector3[] at =
        {
            new Vector3(peak.x, Mathf.Max(peakY * 0.55f, 12f), peak.z),
            c + seaward * r * 0.86f + Vector3.up * 3f,
            new Vector3(c.x, peakY * 0.4f, c.z),
        };

        for (int i = 0; i < from.Length; i++)
        {
            cam.transform.position = from[i];
            cam.transform.LookAt(at[i]);
            // Wait for the chunks THIS vantage needs, every time -- moving
            // the camera moves the streamer's centre, so each shot has its
            // own build to wait out.
            float waited = 0f;
            yield return new WaitForSeconds(i == 0 ? 4f : 1f);
            while (streamer != null && streamer.PendingCount > 0 && waited < 40f)
            {
                waited += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(1f);
            string path = $"/tmp/seasick-look-{Tag}-{i}.png";
            Shoot(cam, path);
            sb.AppendLine($"shot {i} -> {path}  from {from[i]:F0} looking at {at[i]:F0}"
                + (streamer != null ? $"  waited={waited:F1}s loaded={streamer.LoadedCount} pending={streamer.PendingCount}" : ""));
            yield return new WaitForSeconds(0.7f);
        }

        if (streamer != null) streamer.target = streamerTarget;
        Destroy(cam.gameObject);
        Unpin();
        System.IO.File.WriteAllText("/tmp/seasick-look.txt", sb.ToString());
        Debug.Log("IslandLook done\n" + sb);
        Destroy(gameObject);
    }

    /// Statics survive leaving play mode here (domain reload is disabled),
    /// so a sheet that is stopped early must not leave the world's clock
    /// frozen for every later run.
    void OnDestroy() => Unpin();

    static void Unpin()
    {
        SeaSick.World.TimeOfDay.Paused = false;
        if (SeaSick.Ocean.SeaStateController.Instance != null)
            SeaSick.Ocean.SeaStateController.Instance.ReleaseForce();
    }

    /// Renders the camera to a RenderTexture and writes a PNG.
    ///
    /// NOT ScreenCapture.CaptureScreenshot, for two reasons. It does not
    /// resolve from the runtime assembly here (its module is only reachable
    /// from the editor one, which is why every existing shot tool lives in
    /// Dev/Editor and is therefore a MonoBehaviour that cannot be added). And
    /// it would bring the HUD with it -- fine for reviewing the HUD, wrong
    /// for judging a landscape.
    static void Shoot(Camera cam, string path)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4
        };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0);
        tex.Apply();
        cam.targetTexture = prev;
        RenderTexture.active = prevActive;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);
    }

    /// Ground height under a world point, from the world height FUNCTION.
    ///
    /// Not a raycast: colliderRadius is 1 chunk, so colliders exist within
    /// 128 m of the SHIP and nowhere else, and the first version of this
    /// probe duly reported every peak as -999 m and aimed the camera at
    /// y = -400. Island.TerrainHeight is the populator's own sampler and
    /// answers for any coordinate whether or not it is streamed in.
    static float Ground(Vector3 p)
        => Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : -999f;
}
