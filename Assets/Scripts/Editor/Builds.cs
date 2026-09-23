using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpeningBell.EditorTools
{
    /// <summary>
    /// Player builds. The playtest build is a development build so the developer tools work in it (F9 money,
    /// the chart's F10 hidden-market view); a plain <c>-buildWindows64Player</c> build leaves them out. Batch:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod OpeningBell.EditorTools.Builds.Playtest -logFile TestResults/build.log</c>
    /// </summary>
    public static class Builds
    {
        public const string Output = "Builds/OpeningBell.exe";

        [MenuItem("Opening Bell/Build Playtest (development)")]
        public static void Playtest()
        {
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });
            Debug.Log($"Playtest build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
