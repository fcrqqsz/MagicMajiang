using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MahjongGame.Editor
{
    public static class DedicatedServerBuildMenu
    {
        private const string ServerScenePath = "Assets/Scenes/00_ServerBootstrap.scene";
        private const string DefaultOutputPath = "Builds/DedicatedServer/SuperMajiangServer.exe";

        [MenuItem("Tools/Build/Dedicated Server (Windows)")]
        public static void BuildDedicatedServer()
        {
            if (!File.Exists(ServerScenePath))
            {
                Debug.LogError($"[DedicatedServerBuild] Missing server scene: {ServerScenePath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DefaultOutputPath));

            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { ServerScenePath },
                locationPathName = DefaultOutputPath,
                target = BuildTarget.StandaloneWindows,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[DedicatedServerBuild] Completed: {report.summary.outputPath}");
            }
            else
            {
                Debug.LogError($"[DedicatedServerBuild] Failed: {report.summary.result}");
            }
        }
    }
}
