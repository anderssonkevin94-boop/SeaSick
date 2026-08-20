using UnityEngine;

/// Reads back what the ocean material is ACTUALLY using at runtime. A material
/// asset stores its own copy of any property it overrides, so changing a
/// default in the shader can silently do nothing — the same trap the scene
/// plays with serialized component fields.
public static class MatCheck
{
    public static void Execute()
    {
        var rend = Object.FindAnyObjectByType<SeaSick.Ocean.OceanRenderer>();
        var mr = rend != null ? rend.GetComponent<MeshRenderer>() : null;
        var m = mr != null ? mr.sharedMaterial : null;
        if (m == null) { Debug.LogError("MATCHECK: no ocean material"); return; }

        var f = SeaSick.Ocean.WaveField.Instance;
        string C(string n) => m.HasProperty(n) ? m.GetColor(n).ToString("F3") : "<none>";
        string F(string n) => m.HasProperty(n) ? m.GetFloat(n).ToString("F3") : "<none>";

        Debug.Log($"MATCHECK {m.name} / {m.shader.name}\n" +
                  $"  _DeepColor    {C("_DeepColor")}\n" +
                  $"  _StormDeep    {C("_StormDeep")}\n" +
                  $"  _StormShallow {C("_StormShallow")}\n" +
                  $"  _SkyReflect   {F("_SkyReflect")}\n" +
                  $"  _SkySoft      {F("_SkySoft")}\n" +
                  $"  _SS_Chop global {Shader.GetGlobalFloat("_SS_Chop"):F3}\n" +
                  $"  EffectiveChop {(f != null ? f.EffectiveChop : -1f):F3}  " +
                  $"seaState {(f != null ? f.SeaState01 : -1f):F2}\n" +
                  $"  _SS_SkyHorizon {Shader.GetGlobalVector("_SS_SkyHorizon")}\n" +
                  $"  _SS_Storminess {Shader.GetGlobalFloat("_SS_Storminess"):F2}\n" +
                  $"  ambientSky {RenderSettings.ambientSkyColor} mode {RenderSettings.ambientMode}\n" +
                  $"  sun {(RenderSettings.sun != null ? RenderSettings.sun.intensity : -1f):F2}\n" +
                  $"  fog {RenderSettings.fogStartDistance:F0}..{RenderSettings.fogEndDistance:F0} " +
                  $"{RenderSettings.fogColor.ToString("F3")}");
    }
}
