using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.CameraRig;

/// A contact sheet of sailing camera angles, rendered at the aspect the game
/// actually ships in.
///
/// Ten shots, taken the only way a framing question can honestly be answered:
/// by putting the camera there and photographing it. Each seat is expressed in
/// ACTUAL METRES behind and above her and then divided by the rig's per-hull
/// scale, because `SailOverride` sets the base numbers and the rig multiplies
/// them — asking for "18 m back" and getting 24 is how a sheet of angles ends
/// up comparing the wrong things.
///
/// The rig eases at 2.2/s, so each shot waits before it is taken. A sheet shot
/// on the frame the seat changed is a sheet of the previous angle.
public class SailShots : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SailShots: not in play mode"); return; }
        var old = FindAnyObjectByType<SailShots>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("SailShots").AddComponent<SailShots>();
    }

    struct Shot
    {
        public string name, note;
        public float back, up, ahead, fov;   // back/up/ahead in REAL metres
    }

    // Set A is the angle Kevin pointed at: close, over her stern, deck
    // readable, horizon still there. Set B is the spread worth comparing it
    // against — including the one we ship now, so the sheet has a control.
    static readonly Shot[] Sheet =
    {
        new Shot { name = "A1_low_astern",   back = 18f, up = 10f, ahead = 12f, fov = 58f,
                   note = "low and close, almost on the water" },
        new Shot { name = "A2_close_quarter",back = 18f, up = 14f, ahead = 10f, fov = 58f,
                   note = "the reference: close, eye just above the rail line" },
        new Shot { name = "A3_close_down",   back = 16f, up = 18f, ahead = 8f,  fov = 58f,
                   note = "same distance, looking down into the deck" },
        new Shot { name = "A4_tight",        back = 14f, up = 13f, ahead = 8f,  fov = 58f,
                   note = "tightest that still holds her whole length" },
        new Shot { name = "A5_reference",    back = 20f, up = 16f, ahead = 10f, fov = 58f,
                   note = "the rescue-boat shot: deck and surroundings together" },

        new Shot { name = "B1_high_plan",    back = 22f, up = 26f, ahead = 6f,  fov = 58f,
                   note = "the pirate-game angle: down onto the deck as a plan" },
        new Shot { name = "B2_sea_level",    back = 26f, up = 8f,  ahead = 18f, fov = 58f,
                   note = "cinematic, horizon dominant, deck mostly hidden" },
        new Shot { name = "B3_shipped_now",  back = 32f, up = 22f, ahead = 24f, fov = 60f,
                   note = "CONTROL: what the game ships right now" },
        new Shot { name = "B4_top_down",     back = 14f, up = 30f, ahead = 2f,  fov = 58f,
                   note = "near plan view, most readable deck, least sea" },
        new Shot { name = "B5_long_lens",    back = 40f, up = 26f, ahead = 16f, fov = 38f,
                   note = "far but narrow: flattens toward isometric" },
    };

    IEnumerator Start()
    {
        var chase = FindAnyObjectByType<ChaseCamera>();
        var anchor = FindAnyObjectByType<SeaSick.Ship.AnchorController>();
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var cam = chase != null ? chase.GetComponent<Camera>() : Camera.main;
        if (chase == null || cam == null) { Debug.LogError("SailShots: no rig"); yield break; }

        // GET HER OFF THE PIER, and prove it before shooting.
        //
        // The first run of this produced ten identical pictures. Calling
        // CastOff is not enough: while she is still alongside, a THIRD rig
        // drives the camera -- not the sail seat and not the island overview,
        // but the anchored framing, which is hard-coded 18 m back and 14 m up
        // and ignores SailOverride completely. Ten seats, one picture, and
        // nothing anywhere says so.
        //
        // So: cast off, put the helm ahead, and wait until she is actually
        // making way and clear of the berth before the first shot.
        if (anchor != null) anchor.CastOff();
        if (motor != null) motor.ThrottleOrder = 0.75f;
        float waited = 0f;
        while (waited < 25f)
        {
            waited += Time.deltaTime;
            bool underway = anchor == null
                || anchor.CurrentState == SeaSick.Ship.AnchorController.State.Underway;
            if (underway && motor != null && motor.CurrentSpeed > 3f) break;
            yield return null;
        }
        yield return new WaitForSeconds(4f);      // and some sea room behind her
        if (motor != null) motor.ThrottleOrder = 0.35f;

        string dir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "Temp", "SailShots");
        System.IO.Directory.CreateDirectory(dir);

        int w = 780, h = 1690;                    // 0.4615, the shipping shape
        var sb = new StringBuilder("SailShots — ten seats, 780x1690 portrait\n");
        sb.AppendLine("name              ASKED back/up/ahead  lens |  GOT back    up   tilt");

        foreach (var shot in Sheet)
        {
            float k = Mathf.Max(0.05f, chase.FramingScale);
            chase.SailOverride = new ChaseCamera.SailShot
            {
                distance = shot.back / k, height = shot.up / k,
                lookAhead = shot.ahead / k, lookHeight = 1.2f, fov = shot.fov,
            };
            yield return new WaitForSeconds(2.4f);   // the rig eases at 2.2/s

            var rt = new RenderTexture(w, h, 24) { antiAliasing = 2 };
            var prevT = cam.targetTexture; var prevA = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = prevT; RenderTexture.active = prevA;
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(dir, shot.name + ".png"), ImageConversion.EncodeToPNG(tex));
            Destroy(tex); rt.Release(); Destroy(rt);

            // Where the camera ACTUALLY ended up, not where it was asked to
            // go. The first sheet was ten copies of one seat and the asked-for
            // column looked perfect throughout.
            Vector3 rel = cam.transform.position - motor.transform.position;
            float realBack = new Vector2(rel.x, rel.z).magnitude, realUp = rel.y;
            float tilt = Mathf.Atan2(realUp - 1.2f, realBack) * Mathf.Rad2Deg;
            sb.AppendLine($"{shot.name,-17}{shot.back,5:F0}{shot.up,6:F0}{shot.ahead,7:F0}"
                        + $"{shot.fov,6:F0}   {realBack,6:F0}{realUp,6:F0}{tilt,6:F0}°  {shot.note}");
        }

        chase.SailOverride = null;
        sb.AppendLine("\nwritten to Temp/SailShots/");
        Debug.Log(sb.ToString());
        Destroy(gameObject);
    }
}
