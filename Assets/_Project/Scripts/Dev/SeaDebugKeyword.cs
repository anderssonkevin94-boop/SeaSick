using System.Collections.Generic;
using UnityEngine;

/// Shared owner-counted gate for the `_SEASICK_DEBUG` global shader keyword.
///
/// `Ocean.shader` gates its six `_SS_*` dev uniforms behind
/// `#pragma multi_compile _ _SEASICK_DEBUG`. Seven dev probes/tuners
/// (`CrestProbe`, `SurfProbe`, `ShaderStrip`, `WeatherSheet`, `WaterClarityTuner`,
/// `SeaFoamTuner`, `ShoalShot`) each used to call `Shader.EnableKeyword` on
/// start and `Shader.DisableKeyword` on finish/disable directly, against the
/// SAME global keyword. That is last-writer-wins: e.g. `CrestProbe` turns the
/// keyword on, and if `SeaFoamTuner` or `WaterClarityTuner` happens to be
/// ticked and gets disabled during the probe's run, its `OnDisable` calls
/// `Shader.DisableKeyword` and turns the keyword back OFF out from under the
/// probe -- which is still mid-run and still reading `_SS_*` uniforms. The
/// probe's own foam-only render (`_SS_FoamOnly`) then draws ordinary water
/// because the variant that reads the uniform is not the one active, and it
/// fails SILENTLY: measured as "0 water pixels of 393216" one night.
///
/// The fix is a reference count keyed by owner rather than a bare on/off
/// flag: the keyword is enabled the moment any owner wants it and only
/// disabled once every owner has released it, so one tool finishing never
/// turns off a keyword another tool still needs.
///
/// Domain reload is DISABLED in this project, so `owners` is a static field
/// that would otherwise survive from one play session into the next -- a
/// probe that was stopped mid-run (script recompile, editor stop, exception)
/// rather than allowed to reach its own Finish/OnDisable would leave a stale
/// owner in the set and the keyword permanently on. `ForceClear` is there for
/// probes that already reset other static state on start, and the
/// `RuntimeInitializeOnLoadMethod` below clears it unconditionally at
/// startup so a stale owner from a previous session can never leak the debug
/// ocean into a new one.
public static class SeaDebugKeyword
{
    const string Keyword = "_SEASICK_DEBUG";

    // Used by static call sites (e.g. a static Finish method) that have no
    // `this` to hand in as the owner.
    public static readonly object StaticOwner = new object();

    static readonly HashSet<object> owners = new HashSet<object>();

    public static bool Active => owners.Count > 0;

    public static void Acquire(object owner)
    {
        if (owner == null) return;
        bool wasEmpty = owners.Count == 0;
        // HashSet.Add is already idempotent -- a double Acquire from the same
        // owner is a no-op past the first -- but the keyword is only ever
        // (re)enabled on the empty -> non-empty transition.
        owners.Add(owner);
        if (wasEmpty && owners.Count > 0) Shader.EnableKeyword(Keyword);
    }

    public static void Release(object owner)
    {
        if (owner == null) return;
        // Remove is a no-op if `owner` never acquired (or already released),
        // so a Release from a non-owner cannot disable the keyword out from
        // under whoever actually holds it.
        if (!owners.Remove(owner)) return;
        if (owners.Count == 0) Shader.DisableKeyword(Keyword);
    }

    public static void ForceClear()
    {
        owners.Clear();
        Shader.DisableKeyword(Keyword);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearOnLoad() => ForceClear();
}
