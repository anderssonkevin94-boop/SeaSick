using UnityEngine;

namespace SeaSick.Save
{
    /// **The 5-minute autosave heartbeat** (Kevin, 2026-09-26), sitting
    /// next to the existing event-driven autosaves (docking, refits,
    /// buildings, voyage start) and the app-lifecycle ones
    /// (`OnApplicationPause`/`OnApplicationQuit`, wherever they live now).
    /// All of them funnel through the same `SaveGame.Autosave(reason)`, so
    /// this file only owns the clock.
    ///
    /// Self-installs the same way `GameBoot` does -- once a
    /// `VoyageManager` exists in the scene -- so a lab scene without a ship
    /// never grows one. Unlike `GameBoot` it is `DontDestroyOnLoad`: a
    /// mid-game slot load reloads the scene, and the heartbeat should not
    /// reset to zero (or double up) across that reload.
    ///
    /// Counts only while eligible: unpaused (`Time.timeScale > 0` -- zero
    /// while the New/Continue overlay is up, or anything else that freezes
    /// the game the same way), not `SaveGame.Suppressed` (a probe driving
    /// the session), and not `SaveGame.Restoring` (a load in flight).
    /// `SaveGame.Autosave` re-checks `GameBoot.Decided` itself, so nothing
    /// here fires before the player has actually chosen New or Continue --
    /// the accumulator just keeps counting real seconds regardless, same as
    /// it would once they have.
    [DefaultExecutionOrder(-250)]
    public class SaveAutosaveTimer : MonoBehaviour
    {
        public const float IntervalSeconds = 300f;

        static SaveAutosaveTimer instance;
        float accumulated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Object.FindAnyObjectByType<SaveAutosaveTimer>() != null) return;
            if (Object.FindAnyObjectByType<SeaSick.Voyage.VoyageManager>() == null) return;
            var go = new GameObject("SaveAutosaveTimer");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SaveAutosaveTimer>();
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (Time.timeScale <= 0f || SaveGame.Suppressed || SaveGame.Restoring) return;
            accumulated += Time.unscaledDeltaTime;
            if (accumulated < IntervalSeconds) return;
            accumulated = 0f;
            SaveGame.Autosave("timer");
        }
    }
}
