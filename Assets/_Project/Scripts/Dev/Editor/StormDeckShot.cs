using System.Collections;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Four shots of the deck in a mountainous sea, which is the only condition
/// where the surface climbs above the planking. This is what says whether the
/// hull water clip works: before it, the sea rendered straight through the
/// deck. Play mode, Sea.unity. Writes /tmp/seasick-deck-0..3.png.
public class StormDeckShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("StormDeckShot: not in play mode"); return; }
        new GameObject("StormDeckShot").AddComponent<StormDeckShot>();
    }

    IEnumerator Start()
    {
        ShipMotor motor = FindAnyObjectByType<ShipMotor>();
        HelmInput helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;

        SeaStateController sea = SeaStateController.Instance;
        if (sea != null) sea.ForceSeverity(1f);

        // Out into the western deep, driving into it. Move the RIGIDBODY, not
        // the transform: the first run set transform.position and she never
        // left home, so the shots came back showing flat water in the lee of
        // the home island rather than a mountainous sea.
        Rigidbody rb = motor != null ? motor.GetComponent<Rigidbody>() : null;
        Vector3 spot = new Vector3(-2600f, 0f, 0f);
        yield return new WaitForSeconds(6f);
        float h0 = OceanSampler.Ready ? OceanSampler.SampleImmediate(spot).height : 0f;
        if (rb != null)
        {
            rb.position = new Vector3(spot.x, h0 + 0.5f, spot.z);
            rb.rotation = Quaternion.identity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (motor != null)
        {
            motor.SailOrder = 1f;
            motor.Rudder = 0f;
            // Straight into the seas, which is where water comes aboard.
            Vector2 w = sea != null ? sea.WindDirection : new Vector2(1f, 0f);
            motor.AutopilotTarget = new Vector3(spot.x, 0f, spot.z)
                + new Vector3(-w.x, 0f, -w.y).normalized * 5000f;
        }

        yield return new WaitForSeconds(20f);

        // A decisive rig rather than a hunt. The first attempt waited for a
        // natural crest and measured the water 0.47 m BELOW the deck at the
        // moment it fired — an A/B on a condition that never happened proves
        // nothing. So: freeze her, pin the wave, and sit her down until the
        // sea is unambiguously above the planking. This tests the RENDERER,
        // which is the thing being claimed, and nothing else.
        HullWaterClip clip = FindAnyObjectByType<HullWaterClip>();
        if (motor != null) motor.SailOrder = 0f;
        if (motor != null) motor.AutopilotTarget = null;

        OceanTime.Paused = true;
        OceanTime.Scrub(500.0);
        yield return new WaitForSeconds(1.5f);

        Vector3 hold = rb != null ? rb.position : Vector3.zero;
        float surface = OceanSampler.Ready ? OceanSampler.SampleImmediate(hold).height : 0f;
        // Deck sits 0.67 m above the origin; sit her so the sea is half a
        // metre over it.
        hold.y = surface - 0.67f - 0.5f;
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.position = hold;
            rb.rotation = Quaternion.identity;
        }
        yield return new WaitForSeconds(1.5f);

        float over = surface - (hold.y + 0.67f);
        Debug.Log("StormDeckShot: sea is " + over.ToString("F2") + " m over the deck");

        if (clip != null) clip.enabled = true;
        yield return new WaitForSeconds(0.8f);
        Shoot("/tmp/seasick-deck-clip-on.png");
        yield return new WaitForSeconds(2.5f);

        if (clip != null) clip.enabled = false;
        yield return new WaitForSeconds(0.8f);
        Shoot("/tmp/seasick-deck-clip-off.png");
        yield return new WaitForSeconds(2.5f);

        if (clip != null) clip.enabled = true;
        if (rb != null) rb.isKinematic = false;
        OceanTime.Paused = false;
        System.IO.File.WriteAllText("/tmp/seasick-deck.txt",
            "sea held " + over.ToString("F2") + " m over the deck, wave phase pinned\n"
            + "shots: /tmp/seasick-deck-clip-on.png and -off.png\n");

        if (sea != null) sea.ReleaseForce();
        Debug.Log("StormDeckShot done");
    }

    static void Shoot(string path)
    {
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("StormDeckShot: " + path);
    }
}
