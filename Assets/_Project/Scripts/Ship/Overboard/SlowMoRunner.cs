using System.Collections;
using UnityEngine;

namespace SeaSick.Ship.Overboard
{
    /// **The fall's brief slow-down** (build brief item 5): `Time.timeScale`
    /// dips to a fraction for half a second of REAL time, then back to 1.
    /// A standalone, persistent object rather than a coroutine on the
    /// `CrewAgent` itself — that body gets deactivated the instant it falls
    /// (`FallOverboard`), which would cancel the coroutine before it ever
    /// restored the timescale.
    public class SlowMoRunner : MonoBehaviour
    {
        static SlowMoRunner instance;

        static SlowMoRunner Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("OverboardSlowMo");
            instance = go.AddComponent<SlowMoRunner>();
            DontDestroyOnLoad(go);
            return instance;
        }

        /// Only if nothing else already touched `Time.timeScale` (never
        /// fights the pause menu's 0) — checked by the caller before this
        /// runs; re-checked here in case two falls land the same frame.
        public static void Run(float scale, float realSeconds)
        {
            if (!Mathf.Approximately(Time.timeScale, 1f)) return;
            Get().StartCoroutine(Get().Dip(scale, realSeconds));
        }

        IEnumerator Dip(float scale, float realSeconds)
        {
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(realSeconds);
            if (Mathf.Approximately(Time.timeScale, scale)) Time.timeScale = 1f;
        }
    }
}
