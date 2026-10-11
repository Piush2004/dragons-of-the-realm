using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlockRealm.EditorTools
{
    /// <summary>
    /// Command-line and menu builds.
    ///   Unity.exe -batchmode -quit -projectPath unity/BlockRealm -executeMethod BlockRealm.EditorTools.BuildScript.BuildWindows
    /// The same player is the client and (with -batchmode -nographics -server) the dedicated server.
    /// </summary>
    public static class BuildScript
    {
        private const string Output = "Builds/Windows/BlockRealm.exe";

        [MenuItem("BlockRealm/Build Windows player")]
        public static void BuildWindows()
        {
            ProjectSetup.Run();
            string[] scenes = { "Assets/Scenes/Main.unity" };
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            UnityEditor.Build.Reporting.BuildSummary s = report.summary;
            Debug.Log("[BlockRealm] build " + s.result + ": " + s.totalErrors + " errors, " + s.totalWarnings + " warnings, " +
                      (s.totalSize / (1024 * 1024)) + " MB, " + s.totalTime);
            if (Application.isBatchMode)
                EditorApplication.Exit(s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
