using System.Collections;
using UnityEngine;
using SeaSick.World;

/// Stand the ship off the biggest island in the world, then run the normal
/// look sheet. The streamer only loads chunks around the SHIP, so a free
/// camera five kilometres out photographs empty sky -- which is what the
/// first attempt did.
public class BigLook : MonoBehaviour
{
    public static void Execute() => new GameObject("BigLook").AddComponent<BigLook>();

    IEnumerator Start()
    {
        var motor = FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
        var anchor = motor != null ? motor.GetComponent<SeaSick.Ship.AnchorController>() : null;
        // The TALLEST island, not the widest. Measured across the world, the
        // two are anti-correlated here -- the widest island in the game is a
        // pancake and the tall ones are small -- so "biggest" photographs
        // the least interesting landform there is.
        Island big = null; float bigPeak = -1f;
        foreach (var i in Island.All)
        {
            if (i == null || i.IsHome) continue;
            float peak = 0f;
            for (int a = 0; a < 32; a++)
                for (int k = 1; k <= 5; k++)
                {
                    float ang = a / 32f * Mathf.PI * 2f;
                    float d = i.RadiusAt(ang) * k / 6f;
                    peak = Mathf.Max(peak, Island.TerrainHeight(
                        i.transform.position.x + Mathf.Sin(ang) * d,
                        i.transform.position.z + Mathf.Cos(ang) * d));
                }
            if (peak > bigPeak) { bigPeak = peak; big = i; }
        }
        if (motor == null || big == null) { Debug.LogError("BigLook: nothing to shoot"); yield break; }

        if (anchor != null) anchor.CastOff();
        yield return null;

        // Off the shore on the bearing with the most relief behind it.
        float bestAng = 0f, bestH = -1f;
        for (int a = 0; a < 64; a++)
        {
            float ang = a / 64f * Mathf.PI * 2f;
            Vector3 p = big.transform.position
                + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * big.RadiusAt(ang) * 0.45f;
            float h = Island.TerrainHeight(p.x, p.z);
            if (h > bestH) { bestH = h; bestAng = ang; }
        }
        Vector3 dir = new Vector3(Mathf.Sin(bestAng), 0f, Mathf.Cos(bestAng));
        Vector3 at = big.transform.position + dir * (big.RadiusAt(bestAng) + 420f);
        at.y = motor.transform.position.y;
        var rb = motor.GetComponent<Rigidbody>();
        motor.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-dir));
        if (rb != null) { rb.position = at; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        motor.AnchorPoint = at;

        Debug.Log($"BigLook: {big.name}, peak {bestH:F0} m, max r {big.MaxRadius:F0} m — "
            + $"ship stood off 420 m; waiting for chunks");
        yield return new WaitForSeconds(8f);
        IslandLook.Tag = "vs-reference";
        IslandLook.Execute();
    }
}
