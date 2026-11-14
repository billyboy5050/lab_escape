#nullable enable
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LabEscape.ArtTrial.Editor
{
    /// <summary>
    /// The art trial's web build: the game's own scene with the trial's scene beside it, so the download carries the trial's models,
    /// textures and animations as a game would. Run ArtTrial.Capture first, which saves that scene. In batch mode:
    /// -executeMethod LabEscape.ArtTrial.Editor.ArtTrialBuild.Web, which builds into Builds/ArtTrialWeb.
    /// </summary>
    public static class ArtTrialBuild
    {
        private const int MaxTexture = 1024;

        [MenuItem("Lab Escape/Art Trial/Build Web")]
        public static void Web()
        {
            CapTextures();
            if (!File.Exists(ArtTrial.ScenePath))
            {
                Debug.LogError($"[ArtTrial] {ArtTrial.ScenePath} is missing: run Lab Escape/Art Trial/Capture first");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Fight.unity", ArtTrial.ScenePath },
                locationPathName = "Builds/ArtTrialWeb",
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            Debug.Log($"[ArtTrial] Build: {report.summary.result}, {report.summary.totalSize} bytes, {report.summary.totalErrors} errors");
            // What the trial's assets add before compression, by kind.
            var packed = report.packedAssets.SelectMany(p => p.contents).Where(c => c.sourceAssetPath.StartsWith(ArtTrial.Packs)).ToList();
            foreach (var g in packed.GroupBy(c => c.type.Name).OrderByDescending(g => g.Sum(c => (long)c.packedSize)))
            {
                Debug.Log($"[ArtTrial] packed {g.Key}: {g.Sum(c => (long)c.packedSize)} bytes in {g.Count()} objects");
            }
            foreach (var c in packed.OrderByDescending(c => c.packedSize).Take(12))
            {
                Debug.Log($"[ArtTrial] largest: {c.packedSize} {c.type.Name} {c.sourceAssetPath}");
            }
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Holds the packs' textures to 1024 pixels, as a web build would carry them, with normal maps marked. PackImport does this on
        /// import, but textures imported before the scripts compiled missed it.
        /// </summary>
        private static void CapTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtTrial.Packs }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter t)) continue;
                var normal = path.Contains("_Normal");
                if (t.maxTextureSize == MaxTexture && (!normal || t.textureType == TextureImporterType.NormalMap)) continue;
                t.maxTextureSize = MaxTexture;
                if (normal) t.textureType = TextureImporterType.NormalMap;
                t.SaveAndReimport();
            }
        }
    }
}
