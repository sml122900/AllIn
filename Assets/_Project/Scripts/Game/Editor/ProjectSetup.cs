using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AllIn.Game.Editor
{
    /// <summary>
    /// One-shot project wiring so nothing has to be hand-edited in the editor:
    /// imports TMP Essential Resources (shaders + settings), creates the empty bootstrap scenes and
    /// registers them in Build Settings. Scenes stay empty — everything is built in code at runtime.
    ///   Menu: AllIn/Setup Project
    ///   Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod AllIn.Game.Editor.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        public const string LoopScene = "Assets/_Project/Scenes/Loop.unity";
        public const string HoldemScene = "Assets/_Project/Scenes/Holdem.unity";

        [MenuItem("AllIn/Setup Project")]
        public static void Run()
        {
            ImportTmpEssentials();
            EnsureScene(LoopScene);
            EnsureScene(HoldemScene);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(LoopScene, true),
                new EditorBuildSettingsScene(HoldemScene, true),
            };
            AssetDatabase.SaveAssets();
            Debug.Log("[AllIn] setup done");
        }

        /// <summary>Batch: -executeMethod AllIn.Game.Editor.ProjectSetup.BuildWindows → Builds/Win64/AllIn.exe</summary>
        public static void BuildWindows()
        {
            var report = UnityEditor.BuildPipeline.BuildPlayer(new[] { LoopScene, HoldemScene }, "Builds/Win64/AllIn.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("[AllIn] build " + report.summary.result + " errors=" + report.summary.totalErrors);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        static void ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return;
            string pkg = Directory.GetDirectories("Library/PackageCache", "com.unity.ugui@*")
                .Select(d => Path.Combine(d, "Package Resources", "TMP Essential Resources.unitypackage"))
                .FirstOrDefault(File.Exists);
            if (pkg == null)
            {
                Debug.LogError("[AllIn] TMP Essential Resources.unitypackage not found");
                return;
            }
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh();
        }

        static void EnsureScene(string path)
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
