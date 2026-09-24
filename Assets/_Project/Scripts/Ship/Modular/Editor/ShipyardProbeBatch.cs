using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SeaSick.Ship.Modular.EditorTools
{
    /// **Runs ShipyardRefitProbe in batch mode** on the modular-ships WORKTREE
    /// project (never the main editor):
    ///
    ///   Unity -batchmode -projectPath SeaSick-modular
    ///         -executeMethod SeaSick.Ship.Modular.EditorTools.ShipyardProbeBatch.Run
    ///
    /// Opens Sea.unity, enters play mode, waits for the world to build, starts
    /// the probe, waits for Logs/ShipyardRefitProbe.txt to be (re)written, and
    /// exits 0 (report written) or 2 (timed out). The probe reads and writes the
    /// shared persistentDataPath save only through its own temp paths, but the
    /// caller should still back the real save up around the run.
    public static class ShipyardProbeBatch
    {
        const string Report = "Logs/ShipyardRefitProbe.txt";
        const double SettleSeconds = 10.0;
        const double TimeoutSeconds = 600.0;
        static double started, enteredPlay = -1.0;
        static bool launched;
        static System.DateTime reportBefore;

        public static void Run()
        {
            reportBefore = File.Exists(Report) ? File.GetLastWriteTimeUtc(Report) : System.DateTime.MinValue;
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sea.unity", OpenSceneMode.Single);
            started = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode) enteredPlay = EditorApplication.timeSinceStartup;
            };
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > TimeoutSeconds)
            {
                Debug.LogError("[ShipyardProbeBatch] timed out");
                EditorApplication.update -= Tick;
                EditorApplication.Exit(2);
                return;
            }
            if (enteredPlay < 0.0) return;
            if (!launched && now - enteredPlay >= SettleSeconds)
            {
                launched = true;
                Debug.Log("[ShipyardProbeBatch] starting ShipyardRefitProbe");
                ShipyardRefitProbe.Execute();
                return;
            }
            if (launched && File.Exists(Report) && File.GetLastWriteTimeUtc(Report) > reportBefore
                && Object.FindAnyObjectByType<ShipyardRefitProbe>() == null)
            {
                Debug.Log("[ShipyardProbeBatch] report written");
                EditorApplication.update -= Tick;
                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
        }
    }
}
