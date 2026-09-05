using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.CameraRig;

/// How much of the SCREEN does she cover, on every rung?
///
/// The complaint that started this was that a big ship "covers such a large
/// and awkward part of the screen". That is a measurable thing, so it gets
/// measured rather than eyeballed: the hull's own renderer bounds are
/// projected through the LIVE camera and reported as a fraction of the frame.
///
/// It drives the real path — `Shipyard.Apply(i)`, the same call the yard
/// buttons make — and then waits real seconds for the rig to ease onto the new
/// hull, because the framing is deliberately slow and a probe that measures on
/// the swap frame measures the old ship.
///
/// If `ChaseCamera.framingPower` is 1, the last two columns are flat across
/// twenty rungs and the tilt does not move. That is the whole claim.
public class FramingProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("FramingProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<FramingProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("FramingProbe").AddComponent<FramingProbe>();
    }

    /// The framing eases at 1.6/s and the rig at 2.2/s, so three seconds is
    /// about five time constants — settled to well under a percent.
    const float Settle = 3f;

    /// A 1080x2340 phone held upright — what the game actually ships on.
    const float PortraitAspect = 1080f / 2340f;

    IEnumerator Start()
    {
        var yard = FindFirstObjectByType<Shipyard>();
        var rig = FindFirstObjectByType<ChaseCamera>();
        var cam = rig != null ? rig.GetComponent<Camera>() : Camera.main;
        if (yard == null || cam == null)
        {
            Debug.LogError("FramingProbe: need a Shipyard and a camera");
            Destroy(gameObject);
            yield break;
        }

        int startedOn = yard.NodeIndex;

        // Get her off the pier first.
        //
        // The first run of this probe measured a docked ship, and a docked
        // ship is not looking through the chase camera at all — the ISLAND
        // OVERVIEW is, at its own 36-degree lens, framing the village. Every
        // number it printed was about the overview and none of it was about
        // the framing this probe exists to check.
        var anchor = yard.GetComponent<AnchorController>();
        if (anchor != null) anchor.CastOff();
        yield return null;

        // And well away from the land, so the terrain clearance is not what is
        // deciding where the camera sits.
        var rb = yard.GetComponent<Rigidbody>();
        Vector3 offshore = yard.transform.position + Vector3.forward * 1500f;
        offshore.y = 0f;
        yard.transform.position = offshore;
        if (rb != null) { rb.position = offshore; rb.linearVelocity = Vector3.zero; }
        yield return new WaitForSeconds(4f);

        var sb = new StringBuilder("FramingProbe — what the player actually sees:\n");
        sb.AppendLine("rung  hull                 L      k    back     up   tilt°"
                    + "     H%    W%   W% portrait");

        foreach (int i in new[] { 0, 4, 8, 12, 15, 18, 19 })
        {
            yard.Apply(i);
            yield return new WaitForSeconds(Settle);

            var n = ShipLadder.Node(i);
            var vis = yard.transform.Find("HullVisual");
            var rends = vis != null ? vis.GetComponentsInChildren<Renderer>() : null;
            if (rends == null || rends.Length == 0)
            {
                sb.AppendLine($"{i,4}  {n.label,-18}  no hull visual");
                continue;
            }

            // The ORIENTED box, not `Renderer.bounds`.
            //
            // `bounds` is axis-aligned in WORLD space, so a ship steering
            // north-east reports a box with her whole length folded into its
            // width — the first run of this measured a 7.8 m brig as 32 m
            // across. Her own local bounds put through her own transform is
            // the box she actually occupies.
            var local = rends[0].localBounds;
            var toShip = yard.transform.worldToLocalMatrix;
            for (int r = 0; r < rends.Length; r++)
            {
                var lb = rends[r].localBounds;
                var m = toShip * rends[r].localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? lb.min.x : lb.max.x,
                        (c & 2) == 0 ? lb.min.y : lb.max.y,
                        (c & 4) == 0 ? lb.min.z : lb.max.z);
                    var p = m.MultiplyPoint3x4(corner);
                    if (r == 0 && c == 0) local = new Bounds(p, Vector3.zero);
                    else local.Encapsulate(p);
                }
            }

            float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
            bool any = false;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) == 0 ? local.min.x : local.max.x,
                    (c & 2) == 0 ? local.min.y : local.max.y,
                    (c & 4) == 0 ? local.min.z : local.max.z);
                var v = cam.WorldToViewportPoint(yard.transform.TransformPoint(corner));
                if (v.z <= 0f) continue;     // behind the lens; not on screen
                any = true;
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            if (!any) { sb.AppendLine($"{i,4}  {n.label,-18}  off camera"); continue; }

            Vector3 rel = cam.transform.position - yard.transform.position;
            float back = new Vector2(rel.x, rel.z).magnitude;
            float tilt = Mathf.Atan2(rel.y, Mathf.Max(0.01f, back)) * Mathf.Rad2Deg;

            // The editor Game view is landscape and the game is not.
            //
            // Unity's fieldOfView is VERTICAL, so the height figure is already
            // the phone's. Width is not: a portrait frame is a third as wide
            // for the same lens, so the same ship covers three times as much
            // of it. Reported both ways, because the awkward one is the one
            // the player holds.
            float aspectNow = (float)Screen.width / Mathf.Max(1, Screen.height);
            float wPortrait = (maxX - minX) * 100f * (aspectNow / PortraitAspect);

            sb.AppendLine($"{i,4}  {n.label,-18} {n.length,5:F1} {rig.FramingScale,5:F2} "
                        + $"{back,7:F1} {rel.y,6:F1} {tilt,6:F1} "
                        + $"{(maxY - minY) * 100f,8:F1} {(maxX - minX) * 100f,8:F1} "
                        + $"{wPortrait,10:F1}");
        }

        sb.AppendLine($"\nframing scale now {rig.FramingScale:F2}; "
                    + $"viewport {Screen.width}x{Screen.height}, fov {cam.fieldOfView:F1}");
        sb.AppendLine("H% / W% are her ORIENTED bounding box as a percentage of the "
                    + "frame. Flat down a column = the same picture on every rung.");
        Debug.Log(sb.ToString());

        yard.Apply(startedOn);
        Destroy(gameObject);
    }
}
