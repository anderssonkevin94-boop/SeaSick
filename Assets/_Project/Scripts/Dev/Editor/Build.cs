using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SeaSick.Dev
{
    /// Batch-mode player builds. Driven by `tools/build-ios.sh`:
    ///   Unity -batchmode -quit -buildTarget iOS -executeMethod SeaSick.Dev.Build.IOS
    /// Writes an Xcode project to `Builds/iOS/` (gitignored). Append mode keeps
    /// Kevin's signing edits in the .xcodeproj across rebuilds; the first build
    /// falls back to a fresh project automatically.
    public static class Build
    {
        const string IosDir = "Builds/iOS";

        public static void IOS()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) Fail("no enabled scenes in EditorBuildSettings");

            var dev = Environment.GetCommandLineArgs().Contains("-development");
            var options = BuildOptions.None;
            if (dev) options |= BuildOptions.Development;
            if (System.IO.Directory.Exists(IosDir)) options |= BuildOptions.AcceptExternalModificationsToPlayer;

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = IosDir,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = options,
            };

            Debug.Log($"[Build] iOS -> {IosDir}  scenes=[{string.Join(", ", scenes)}]  dev={dev}  " +
                      $"bundle={PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)}  " +
                      $"minOS={PlayerSettings.iOS.targetOSVersionString}");

            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            Debug.Log($"[Build] result={sum.result} size={sum.totalSize / (1024 * 1024)} MB " +
                      $"time={sum.totalTime.TotalSeconds:F0}s errors={sum.totalErrors} warnings={sum.totalWarnings}");
            if (sum.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception)
                            Debug.LogError($"[Build] {step.name}: {m.content}");
                Fail($"build {sum.result}");
            }
        }

        static void Fail(string why)
        {
            Debug.LogError("[Build] FAILED: " + why);
            EditorApplication.Exit(1);
        }
    }
}
