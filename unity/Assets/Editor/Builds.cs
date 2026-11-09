#nullable enable
using System.IO;
using LabEscape.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LabEscape.Client.Editor
{
    /// <summary>Copies content/*.json into Resources before a build, since a player cannot read the repository.</summary>
    public sealed class ContentBuildStep : IPreprocessBuildWithReport
    {
        private const string Folder = "Assets/Resources/Content";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => CopyContent();

        public static void CopyContent()
        {
            Directory.CreateDirectory(Folder);
            foreach (var name in ContentFiles.FileNames)
            {
                File.Copy(GameContent.RepoPath("content", name + ".json"), Path.Combine(Folder, name + ".json"), true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }

    /// <summary>Player builds, from the menu or from the command line with -executeMethod.</summary>
    public static class Builds
    {
        private const string Scene = "Assets/Scenes/Fight.unity";

        [MenuItem("Lab Escape/Build Mac")]
        public static void Mac() => Build(BuildTarget.StandaloneOSX, "Builds/Mac/LabEscape.app");

        [MenuItem("Lab Escape/Build Web")]
        public static void Web() => Build(BuildTarget.WebGL, "Builds/Web");

        private static void Build(BuildTarget target, string path)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scene }, locationPathName = path, target = target, options = BuildOptions.None });
            Debug.Log($"Build {target}: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Makes the two assets that have to exist as files: the empty scene a build starts from and the panel settings, whose shader
        /// references only the editor can fill in. Their results are committed, so this only needs running again if they are lost.
        /// </summary>
        [MenuItem("Lab Escape/Recreate Scene And Panel")]
        public static void CreateAssets()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, Scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };

            const string panelPath = "Assets/Resources/UI/Panel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, panelPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Resources/UI/Theme.tss");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
        }
    }
}
