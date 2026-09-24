using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SeaSick.Ship.Modular.EditorTools
{
    /// **Runs the shipyard probes in batch mode** on the modular-ships
    /// WORKTREE project (never the main editor):
    ///
    ///   Unity -batchmode -projectPath SeaSick-modular
    ///         -executeMethod SeaSick.Ship.Modular.EditorTools.ShipyardProbeBatch.Run
    ///         [-shipyardProbe refit|ui|both]      (default both)
    ///
    /// Opens Sea.unity, enters play mode, waits for the world to build, then
    /// runs the chosen probes one after the other in the same play session:
    /// ShipyardRefitProbe (Logs/ShipyardRefitProbe.txt), then ShipyardUiProbe
    /// (Logs/ShipyardUiProbe.txt). Each is started only once the previous
    /// report is (re)written and its runner is gone. Exits 0 when every chosen
    /// report was written (PASS/FAIL is read from the reports, not the exit
    /// code), 2 on timeout. ShipyardUiProbe reverts the ship to the standard
    /// steamer itself, so it does not depend on what the refit probe left.
    /// The probes write the save only through their own temp paths, but the
    /// caller should still back the real save up around the run.
    public static class ShipyardProbeBatch
    {
        const double SettleSeconds = 10.0;
        const double BetweenSeconds = 2.0;
        const double TimeoutSeconds = 900.0;

        struct Step
        {
            public string name, report;
            public System.Action start;
            public System.Func<bool> runnerAlive;
        }

        static readonly List<Step> steps = new List<Step>();
        static int index = -1;
        static double started, enteredPlay = -1.0, nextAt;
        static bool running;
        static System.DateTime reportBefore;

        public static void Run()
        {
            string which = Arg("-shipyardProbe") ?? "both";
            steps.Clear();
            index = -1;
            running = false;
            if (which == "refit" || which == "both")
                steps.Add(new Step
                {
                    name = "ShipyardRefitProbe", report = "Logs/ShipyardRefitProbe.txt",
                    start = ShipyardRefitProbe.Execute,
                    runnerAlive = () => Object.FindAnyObjectByType<ShipyardRefitProbe>() != null,
                });
            if (which == "ui" || which == "both")
                steps.Add(new Step
                {
                    name = "ShipyardUiProbe", report = "Logs/ShipyardUiProbe.txt",
                    start = ShipyardUiProbe.Execute,
                    runnerAlive = () => Object.FindAnyObjectByType<ShipyardUiProbe>() != null,
                });
            if (steps.Count == 0)
            {
                Debug.LogError("[ShipyardProbeBatch] -shipyardProbe must be refit, ui or both (got '" + which + "')");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("[ShipyardProbeBatch] probes: " + which);

            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sea.unity", OpenSceneMode.Single);
            started = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode) enteredPlay = EditorApplication.timeSinceStartup;
            };
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1].Trim().ToLowerInvariant();
            return null;
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > TimeoutSeconds)
            {
                Debug.LogError("[ShipyardProbeBatch] timed out" + (index >= 0 && index < steps.Count ? " in " + steps[index].name : ""));
                EditorApplication.update -= Tick;
                EditorApplication.Exit(2);
                return;
            }
            if (enteredPlay < 0.0) return;

            if (!running)
            {
                // Before the first probe: the settle delay. Between probes: a short one.
                if (index < 0 && now - enteredPlay < SettleSeconds) return;
                if (index >= 0 && now < nextAt) return;
                index++;
                if (index >= steps.Count)
                {
                    Debug.Log("[ShipyardProbeBatch] all reports written");
                    EditorApplication.update -= Tick;
                    EditorApplication.isPlaying = false;
                    EditorApplication.Exit(0);
                    return;
                }
                var s = steps[index];
                reportBefore = File.Exists(s.report) ? File.GetLastWriteTimeUtc(s.report) : System.DateTime.MinValue;
                running = true;
                Debug.Log("[ShipyardProbeBatch] starting " + s.name);
                s.start();
                return;
            }

            var cur = steps[index];
            if (File.Exists(cur.report) && File.GetLastWriteTimeUtc(cur.report) > reportBefore && !cur.runnerAlive())
            {
                Debug.Log("[ShipyardProbeBatch] " + cur.name + " report written");
                running = false;
                nextAt = now + BetweenSeconds;
            }
        }
    }
}
