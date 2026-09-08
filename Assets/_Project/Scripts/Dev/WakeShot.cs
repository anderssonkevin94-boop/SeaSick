using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// **Does she leave a wake, and does the bow throw water?** Proof-of-life plus
/// a look. Sails the player ship at full throttle across a moderate sea long
/// enough for the diverging wake arms to develop and the bow-entry splash to
/// fire, reports that the new emitters are actually alive (particle counts,
/// bow-entry burst count) and the ripple field carries a wake, then shoots a
/// high stern-quarter frame through the MAIN camera into a RenderTexture (the
/// HUD is IMGUI and never reaches the texture).
///
/// Rendered through Camera.main on purpose: the clipmap follows Camera.main,
/// so a probe's own camera would look out through a ring seam (the documented
/// trap). Play mode, Sea.unity. Writes /tmp/seasick-wake-shot.png and .txt.
public class WakeShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WakeShot: not in play mode"); return; }
        WakeShot old = FindAnyObjectByType<WakeShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("WakeShot").AddComponent<WakeShot>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var juice = motor != null ? motor.GetComponent<SeaSick.Ship.SpeedJuice>() : null;
        if (motor == null || juice == null || SeaStateController.Instance == null
            || !OceanSampler.Ready)
        {
            Debug.LogError("WakeShot: missing pieces");
            yield break;
        }
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        // A gentle sea and a course ALONG it, not into it: a head-sea run lets
        // her surf the faces to twice hull speed, which strings the wake out
        // into dots. The wake is a cruising-speed look, so shoot it cruising.
        SeaStateController.Instance.ForceSeverity(0.30f);
        Vector3 spot = new Vector3(-1500f, 0f, 0f);
        yield return new WaitForSeconds(8f);
        float h0 = OceanSampler.SampleImmediate(spot).height;
        Vector2 wnd = SeaStateController.Instance.WindDirection;
        // Run across the seas (along the wind), where speed is steady.
        Vector3 course = new Vector3(wnd.x, 0f, wnd.y).normalized;
        rb.position = new Vector3(spot.x, h0 + 0.3f, spot.z);
        rb.rotation = Quaternion.LookRotation(course, Vector3.up);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        motor.ThrottleOrder = 1f;
        motor.AutopilotTarget = rb.position + course * 5000f;

        // Let her build way and lay a wake.
        yield return new WaitForSeconds(14f);

        // Proof of life: the wake arms and the running emitters carry
        // particles, and the ripple field is well above its parked level.
        int wakeParts = LiveCount(juice, "WakeLinePort") + LiveCount(juice, "WakeLineStar");
        int bowParts = LiveCount(juice, "BowSpray");
        int shoulderParts = LiveCount(juice, "ShoulderPort") + LiveCount(juice, "ShoulderStar");
        sb.AppendLine(string.Format("speed {0:F1} m/s of {1:F1}", motor.CurrentSpeed, motor.MaxSpeed));
        sb.AppendLine(string.Format(
            "live particles — wake arms {0}, bow {1}, shoulders {2}",
            wakeParts, bowParts, shoulderParts));
        sb.AppendLine(wakeParts > 0
            ? "wake arms ALIVE" : "wake arms EMPTY — emitter misconfigured or she is not moving");

        // The look: a high stern-quarter that shows both arms of the V and the
        // bow throwing spray ahead. Set FollowOverride so the clipmap centres
        // on the shot, then restore it.
        var cam = Camera.main;
        var clipmap = FindAnyObjectByType<OceanClipmap>();
        if (cam != null)
        {
            // High, behind and a little to one side, far enough back to hold
            // the whole V and the ship in one portrait frame.
            Vector3 fwd = motor.transform.forward;
            Vector3 eye = motor.transform.position
                        - fwd * 46f + Vector3.up * 26f - motor.transform.right * 6f;
            var prevPos = cam.transform.position;
            var prevRot = cam.transform.rotation;
            if (clipmap != null) clipmap.FollowOverride = motor.transform;
            // Aim a little astern of her so the wake fills the frame, not sky.
            Vector3 aim = motor.transform.position - fwd * 16f;
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation(
                (aim - eye).normalized, Vector3.up);
            yield return null;

            var rt = new RenderTexture(900, 1500, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(900, 1500, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 900, 1500), 0, 0);
            tex.Apply();
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            System.IO.File.WriteAllBytes("/tmp/seasick-wake-shot.png", tex.EncodeToPNG());

            if (clipmap != null) clipmap.FollowOverride = null;
            cam.transform.position = prevPos;
            cam.transform.rotation = prevRot;
        }

        SeaStateController.Instance.ReleaseForce();
        motor.AutopilotTarget = null;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-wake-shot.txt", sb.ToString());
        Debug.Log("WakeShot:\n" + sb);
        Destroy(gameObject);
    }

    static int LiveCount(Component juice, string childName)
    {
        Transform root = juice.transform.Find("FoamEmitters/" + childName);
        if (root == null) return -1;
        var ps = root.GetComponent<ParticleSystem>();
        return ps != null ? ps.particleCount : -1;
    }
}
