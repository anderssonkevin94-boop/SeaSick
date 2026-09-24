using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SeaSick.Ship.Modular.EditorTools
{
    /// **Phone build of the modular-ships BRANCH**, run in batch mode on the
    /// worktree project (never the main editor):
    ///
    ///   Unity -batchmode -projectPath SeaSick-modular -buildTarget iOS
    ///         -executeMethod SeaSick.Ship.Modular.EditorTools.ModularPhoneBuild.Run
    ///         -iosOut /Users/kevinandersson/Desktop/SeaSick/Builds/iOS
    ///
    /// Writes into the MAIN repo's existing Xcode project (append mode) so the
    /// signing team Kevin picked in Xcode survives, exactly like Dev/Editor/
    /// Build.IOS does for the main project. Development player. Writes
    /// Logs/modular-phone-build.txt and exits 0 on success, 1 on failure.
    public static class ModularPhoneBuild
    {
        public static void Run()
        {
            string outDir = Arg("-iosOut");
            var log = new System.Text.StringBuilder();
            int code = 1;
            try
            {
                if (string.IsNullOrEmpty(outDir)) throw new System.Exception("-iosOut <Xcode project folder> is required");
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0) throw new System.Exception("no enabled scenes in EditorBuildSettings");
                var options = BuildOptions.Development;
                if (Directory.Exists(outDir)) options |= BuildOptions.AcceptExternalModificationsToPlayer;
                log.AppendLine($"STARTED {System.DateTime.Now} out={outDir} options={options} scenes={string.Join(",", scenes)}");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = outDir,
                    target = BuildTarget.iOS, targetGroup = BuildTargetGroup.iOS, options = options,
                });
                var sum = report.summary;
                log.AppendLine($"RESULT {sum.result} size={sum.totalSize / (1024 * 1024)}MB time={sum.totalTime.TotalSeconds:F0}s errors={sum.totalErrors} warnings={sum.totalWarnings}");
                foreach (var step in report.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception) log.AppendLine($"ERR {step.name}: {m.content}");
                code = sum.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/modular-phone-build.txt", log.ToString());
            Debug.Log("[ModularPhoneBuild]\n" + log);
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
