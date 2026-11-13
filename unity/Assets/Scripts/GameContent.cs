#nullable enable
using System;
using System.IO;
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// Loads content/*.json, the single source of every number. The editor reads the repository's folder directly; a build carries
    /// a copy in Resources, which the build step makes, unless it is told a folder with -labContent.
    /// </summary>
    public static class GameContent
    {
        /// <summary>Tests point this at a copy of content/ they can edit. Null leaves the choice to <see cref="Folder"/>.</summary>
        public static string? FolderOverride { get; set; }

        /// <summary>
        /// The folder content is read from, which is also the folder the game watches for edits (hot reload), or null when there is
        /// none: a build reads its Resources copy, and the web build has no disk. The editor reads the repository's content/, and a
        /// Mac or Windows build reads the folder given as `-labContent <folder>`.
        /// </summary>
        public static string? Folder
        {
            get
            {
                if (FolderOverride != null) return FolderOverride;
#if UNITY_EDITOR
                return RepoPath("content");
#else
                return Telemetry.Arg(Environment.GetCommandLineArgs(), "-labContent");
#endif
            }
        }

        public static ContentFiles LoadFiles()
        {
            var folder = Folder;
            if (folder != null) return ContentLoader.FilesFromDirectory(folder);
            return ContentLoader.Parse(name =>
            {
                var asset = Resources.Load<TextAsset>("Content/" + name);
                if (asset == null) throw new FileNotFoundException($"content file {name} is missing from this build");
                return asset.text;
            });
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
