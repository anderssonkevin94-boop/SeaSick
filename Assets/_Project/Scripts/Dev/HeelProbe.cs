using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// How far does a broadside actually lay her over?
///
/// Recoil is a real impulse now — shot mass times muzzle speed, applied at
/// each gun — and the heel that follows is whatever her mass and her righting
/// moment allow. Nobody authored an angle, so nobody knows the answer until it
/// is measured. This fires each rung's full broadside in flat water and
/// records the peak roll.
///
/// It also reports the factor that would be needed to reach a chosen visible
/// angle, so exaggeration is a decision taken against a number rather than a
/// constant nudged until it looks right.
public class HeelProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HeelProbe: play mode only"); return; }
        var old = FindAnyObjectByType<HeelProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("HeelProbe").AddComponent<HeelProbe>();
    }

    const float WantDegrees = 3f;     // what "a reasonable tilt" might mean

    IEnumerator Start()
    {
        var yard = FindAnyObjectByType<Shipyard>();
        var battery = yard != null ? yard.GetComponent<CannonBattery>() : null;
        var sea = SeaSick.Ocean.SeaStateController.Instance;
        if (yard == null || battery == null)
        { Debug.LogError("HeelProbe: need a Shipyard with a CannonBattery"); yield break; }
        if (sea != null) sea.ForceHs(0.15f);      // flat, so roll is the guns

        var sb = new StringBuilder("=== HeelProbe ===\n");
        sb.AppendLine("rung  label              guns/side  mass t    GM     peak heel   x for "
                      + WantDegrees.ToString("F0") + "deg");

        foreach (int node in new[] { 6, 12, 15, 19 })
        {
            yard.Apply(node);
            var n = yard.Node;

            // Give her every cell she has to guns on the topmost tier, so this
            // is a full broadside and not a token one.
            string top = n.tier_names[n.tier_names.Length - 1];
            foreach (var b in n.bay_labels) yard.SetUse(b, top, BayUse.Battery);
            // and berths below, or the guns have nobody to work them
            if (n.tier_names.Length > 1)
                foreach (var b in n.bay_labels)
                    yard.SetUse(b, n.tier_names[0], BayUse.Quarters);

            var rb = yard.GetComponent<Rigidbody>();
            yard.transform.rotation = Quaternion.identity;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(3f);   // let her settle upright

            float before = SignedRoll(yard.transform);
            battery.FireBroadside(true);

            float peak = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < 4f)
            {
                yield return new WaitForFixedUpdate();
                peak = Mathf.Max(peak, Mathf.Abs(SignedRoll(yard.transform) - before));
            }

            float need = peak > 0.001f ? WantDegrees / peak : 0f;
            sb.AppendLine($"{node,4}  {n.label,-17} {battery.GunsPerSide,9} "
                        + $"{rb.mass / 1000f,7:F0} {yard.Load.GMm,6:F2} "
                        + $"{peak,10:F3}deg {need,10:F0}");
        }

        sb.AppendLine("\n  Real ships barely heel to a broadside — this is what the physics");
        sb.AppendLine("  gives with no exaggeration. The last column is the multiplier that");
        sb.AppendLine("  would reach " + WantDegrees.ToString("F0") + " degrees, if that is the feel we want.");
        Debug.Log(sb.ToString());
    }

    static float SignedRoll(Transform t)
    {
        float r = t.eulerAngles.z;
        return r > 180f ? r - 360f : r;
    }
}
