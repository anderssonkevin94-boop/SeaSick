using System.Collections;
using UnityEngine;
using SeaSick.World;

/// A wide shot of a CLUSTER of islets, because "throw in a few more of
/// those" is a question about the archipelago, not about one island. The
/// ship has to be parked in the middle of them: the streamer only loads
/// chunks around the ship, so a free camera parked anywhere else
/// photographs open water.
public class IsletShot : MonoBehaviour
{
    public static void Execute() => new GameObject("IsletShot").AddComponent<IsletShot>();

    IEnumerator Start()
    {
        var ship = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var anchor = ship != null ? ship.GetComponent<SeaSick.Ship.AnchorController>() : null;
        if (ship == null) { Debug.LogError("IsletShot: no ship"); yield break; }

        // The tightest cluster of small islands in the world: for each one,
        // how far to its two nearest small neighbours.
        Island best = null; float bestScore = float.MaxValue;
        var small = new System.Collections.Generic.List<Island>();
        foreach (var i in Island.All)
            if (i != null && !i.IsHome && i.MaxRadius < 220f) small.Add(i);
        foreach (var a in small)
        {
            float d1 = float.MaxValue, d2 = float.MaxValue;
            foreach (var b in small)
            {
                if (b == a) continue;
                float d = Island.FlatDistance(a.transform.position, b.transform.position);
                if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
            }
            if (d2 < bestScore) { bestScore = d2; best = a; }
        }
        if (best == null) { Debug.LogError("IsletShot: no islets"); yield break; }
        Debug.Log($"IsletShot: {best.name}, two nearest islets within {bestScore:F0} m");

        // Mid-morning, pinned. A shot taken at whatever hour the world
        // happened to reach is not comparable to the last one -- the first
        // attempt at this came back a black rectangle. `IslandLook` pins the
        // same 0.36 for the same reason.
        SeaSick.World.TimeOfDay.SetTime01(0.36f);
        if (anchor != null) anchor.CastOff();
        yield return null;
        Vector3 at = best.transform.position + new Vector3(360f, 0f, -360f);
        at.y = ship.transform.position.y;
        var rb = ship.GetComponent<Rigidbody>();
        ship.transform.SetPositionAndRotation(at, Quaternion.LookRotation(best.transform.position - at));
        if (rb != null) { rb.position = at; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        ship.AnchorPoint = at;
        yield return new WaitForSeconds(8f);
        SeaSick.World.TimeOfDay.SetTime01(0.36f);

        var go = new GameObject("IsletCam");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(Camera.main);
        cam.fieldOfView = 45f;
        cam.farClipPlane = 8000f;
        go.transform.position = at + Vector3.up * 210f;
        go.transform.LookAt(best.transform.position);

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        cam.aspect = (float)W / H;
        cam.ResetProjectionMatrix();
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes("/tmp/islets.png", tex.EncodeToPNG());
        cam.targetTexture = null;
        Destroy(go);
        Destroy(gameObject);
    }
}
