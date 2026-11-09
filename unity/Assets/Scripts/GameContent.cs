#nullable enable
using System.IO;
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// Loads content/*.json, the single source of every number. The editor reads the repository's folder directly; a build carries
    /// a copy in Resources, which the build step makes.
    /// </summary>
    public static class GameContent
    {
        public static ContentFiles LoadFiles()
        {
#if UNITY_EDITOR
            return ContentLoader.FilesFromDirectory(RepoPath("content"));
#else
            return ContentLoader.Parse(name =>
            {
                var asset = Resources.Load<TextAsset>("Content/" + name);
                if (asset == null) throw new FileNotFoundException($"content file {name} is missing from this build");
                return asset.text;
            });
#endif
        }

        public static Content Load() => ContentLoader.Build(LoadFiles());

        /// <summary>A path from the repository's root. Only meaningful in the editor, whose project sits in unity/.</summary>
        public static string RepoPath(params string[] parts)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            foreach (var p in parts) root = Path.Combine(root, p);
            return root;
        }
    }
}
