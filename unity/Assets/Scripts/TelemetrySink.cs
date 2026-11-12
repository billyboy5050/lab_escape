#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>PlayerPrefs as the store for the tester and the attempt counts. Telemetry reads what it gives back as untrusted.</summary>
    public sealed class PlayerPrefsStore : IPrefs
    {
        public string? Get(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, "") : null;

        public void Set(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Where a finished fight's telemetry goes. In the editor, the repository's `telemetry/`, which git ignores and the playtest
    /// report reads, as the web client's dev server does; in a Mac or Windows build, `telemetry/` in the app's data folder; in
    /// the web build, which has no disk, nowhere: the end card offers each file as a download instead.
    /// </summary>
    public static class TelemetrySink
    {
        /// <summary>Set by the tests and the smoke run, so a scripted fight never lands among a playtest's fights.</summary>
        public static string? RootOverride;

        public static bool HasDisk => RootOverride != null || Application.platform != RuntimePlatform.WebGLPlayer;

        /// <summary>The folder that holds one folder per saved fight, or null in the web build.</summary>
        public static string? Root
        {
            get
            {
                if (RootOverride != null) return RootOverride;
                if (!HasDisk) return null;
#if UNITY_EDITOR
                return GameContent.RepoPath("telemetry");
#else
                return Path.Combine(Application.persistentDataPath, "telemetry");
#endif
            }
        }

        /// <summary>Writes a finished fight's three files. Returns its folder, or null where there is no disk.</summary>
        public static string? Save(FightSession s, double unixMs)
        {
            var root = Root;
            if (root == null) return null;
            var dir = Path.Combine(root, Telemetry.FolderName(unixMs, s));
            Directory.CreateDirectory(dir);
            foreach (var (name, text) in Telemetry.Files(s)) File.WriteAllText(Path.Combine(dir, name), text);
            return dir;
        }

        /// <summary>
        /// Hands one of the fight's files to the browser to save, named as the web client names its downloads:
        /// `replay-2026-11-12T15-30-04-512Z.json`. Does nothing outside the web build.
        /// </summary>
        public static void Download(FightSession s, string file, double unixMs)
        {
            var stamp = Labels.IsoTime(unixMs).Replace(':', '-').Replace('.', '-');
            foreach (var (name, text) in Telemetry.Files(s))
            {
                if (name != file) continue;
                var dot = name.IndexOf('.');
                var saveAs = $"{name.Substring(0, dot)}-{stamp}{name.Substring(dot)}";
                var type = name.EndsWith(".jsonl", StringComparison.Ordinal) ? "application/x-ndjson" : "application/json";
#if UNITY_WEBGL && !UNITY_EDITOR
                LabDownload(saveAs, text, type);
#else
                Debug.Log($"[Telemetry] {saveAs} ({type}) would be downloaded in the web build");
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void LabDownload(string name, string text, string type);
#endif

        public static double NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
