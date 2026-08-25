using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// The storm colours never reach the ocean shader: _SS_Storminess reads 0.000
/// at the GPU while SeaStateController says 0.59. Somewhere between the
/// weather and the global, the value is lost.
///
/// Walks the chain link by link, over several frames, because a single sample
/// cannot tell "never set" from "set and then eased back down". Each link
/// prints what it believes, so the first one that disagrees with its input is
/// the fault.
///
/// Play mode, Sea.unity. Writes Temp/sky-chain.txt.
public class SkyChainCheck : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SkyChainCheck: not in play mode"); return; }
        new GameObject("SkyChainCheck").AddComponent<SkyChainCheck>();
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(5f);

        var sb = new StringBuilder();
        var sky = SkyDirector.Instance;
        var ctrl = SeaStateController.Instance;
        var motor = FindAnyObjectByType<ShipMotor>();

        sb.AppendLine("SkyChainCheck");
        sb.AppendLine("SkyDirector.Instance      " + (sky != null ? sky.name : "NULL"));
        if (sky != null)
        {
            sb.AppendLine("  component enabled       " + sky.enabled);
            sb.AppendLine("  gameObject activeInHier " + sky.gameObject.activeInHierarchy);
            var t = typeof(SkyDirector);
            var f = t.GetField("forceStorm", System.Reflection.BindingFlags.NonPublic
                                           | System.Reflection.BindingFlags.Instance);
            sb.AppendLine("  forceStorm (serialised) " + (f != null ? f.GetValue(sky).ToString() : "?"));
            var shipF = t.GetField("ship", System.Reflection.BindingFlags.NonPublic
                                          | System.Reflection.BindingFlags.Instance);
            var shipVal = shipF != null ? shipF.GetValue(sky) as Transform : null;
            sb.AppendLine("  ship reference          " + (shipVal != null ? shipVal.name : "NULL  <-- SampleWeather returns 0 when this is null"));
        }
        sb.AppendLine();
        sb.AppendLine("  frame   ctrl.Severity  ctrl.Storminess  sky.Storminess  GLOBAL _SS_Storminess");

        for (int i = 0; i < 8; i++)
        {
            float sev = ctrl != null ? ctrl.Severity01 : -1f;
            float cs = ctrl != null ? ctrl.Storminess01 : -1f;
            float ss = sky != null ? sky.Storminess01 : -1f;
            float g = Shader.GetGlobalFloat("_SS_Storminess");
            sb.AppendLine($"  {i,5}   {sev,13:F3}  {cs,15:F3}  {ss,14:F3}  {g,21:F3}");
            yield return new WaitForSeconds(1f);
        }

        if (motor != null)
        {
            Vector2 p = new Vector2(motor.transform.position.x, motor.transform.position.z);
            sb.AppendLine();
            sb.AppendLine("ship at " + motor.transform.position.ToString("F0"));
            sb.AppendLine("  SeaSeverityAt(ship)     "
                + (ctrl != null ? ctrl.SeaSeverityAt(p).ToString("F3") : "-"));
            sb.AppendLine("  RegionField.Evaluate    "
                + (RegionField.Instance != null ? RegionField.Instance.Evaluate(p).ToString("F3") : "-"));
        }

        System.IO.File.WriteAllText("Temp/sky-chain.txt", sb.ToString());
        Debug.Log("SkyChainCheck: done — Temp/sky-chain.txt\n" + sb);
        Destroy(gameObject);
    }
}
