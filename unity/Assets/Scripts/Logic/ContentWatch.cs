#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Client
{
    public enum WatchKind
    {
        /// <summary>Nothing to act on.</summary>
        Nothing,

        /// <summary>The files changed and make valid content with a hash the game does not have yet.</summary>
        Reloaded,

        /// <summary>The files changed and are not valid content: the game keeps what it has, and says why.</summary>
        Rejected,

        /// <summary>The files changed back to content the game already has, after a rejection: the error can go.</summary>
        Restored,
    }

    /// <summary>What <see cref="ContentWatch.Poll"/> found.</summary>
    public readonly struct WatchResult
    {
        public readonly WatchKind Kind;
        public readonly Content? Content;
        public readonly IReadOnlyList<string> Problems;

        public WatchResult(WatchKind kind, Content? content = null, IReadOnlyList<string>? problems = null)
        {
            Kind = kind;
            Content = content;
            Problems = problems ?? Array.Empty<string>();
        }

        public static readonly WatchResult Nothing = new WatchResult(WatchKind.Nothing);

        /// <summary>The first problem, and how many more there are: what fits in a toast.</summary>
        public string Summary => Problems.Count == 0 ? "" : Problems.Count == 1 ? Problems[0] : $"{Problems[0]} (and {Problems.Count - 1} more)";
    }

    /// <summary>
    /// Notices that the files in a content/ folder were saved and reads them again, so tuning needs no restart (spec, "Hot reload of
    /// the data files"). It has no Unity code and no clock: the game calls <see cref="Poll"/> a couple of times a second, and a
    /// poll that finds the files changed waits for the next to find them as they were, so a save caught half-way is not read.
    /// Content is accepted only if it validates, and anything that fails, a missing file or bad JSON included, is a rejection that
    /// leaves the game's values alone (CLAUDE.md, invariant 3).
    /// </summary>
    public sealed class ContentWatch
    {
        private readonly string _folder;
        private string _applied;
        private string? _pending;
        private bool _rejected;

        /// <summary>
        /// The folder is stamped now, so make the watch before the first load: a save between the two is then read. Set
        /// <see cref="Hash"/> to the hash of the content the game is using.
        /// </summary>
        public ContentWatch(string folder)
        {
            _folder = folder;
            _applied = Stamp();
        }

        public string Folder => _folder;

        /// <summary>A list of problems, one to a line, for the first `max` and a count of the rest.</summary>
        public static string FormatProblems(IReadOnlyList<string> problems, int max = 6)
        {
            var text = string.Join("\n", problems.Take(max).Select(p => "- " + p));
            return problems.Count > max ? text + $"\n- and {problems.Count - max} more" : text;
        }

        /// <summary>The hash of the content the game holds, or null if it holds none yet. Poll compares what it reads with it.</summary>
        public string? Hash { get; set; }

        /// <summary>True from a rejection until the files make acceptable content again.</summary>
        public bool Rejected => _rejected;

        /// <summary>One look at the folder. At most one in two finds anything, since a change must hold still for a poll first.</summary>
        public WatchResult Poll()
        {
            var now = Stamp();
            if (now == _applied)
            {
                _pending = null;
                return WatchResult.Nothing;
            }
            if (now != _pending)
            {
                _pending = now;
                return WatchResult.Nothing;
            }
            _applied = now;
            _pending = null;
            return Read();
        }

        private WatchResult Read()
        {
            var problems = new List<string>();
            var root = new JObject();
            foreach (var name in ContentFiles.FileNames)
            {
                try
                {
                    root[name] = JsonIn.Parse(File.ReadAllText(Path.Combine(_folder, name + ".json")));
                }
                catch (Exception e)
                {
                    problems.Add($"{name}.json: {e.Message}");
                }
            }
            if (problems.Count == 0)
            {
                try
                {
                    var content = ContentLoader.Build(new ContentFiles(root));
                    if (content.Hash == Hash)
                    {
                        var wasRejected = _rejected;
                        _rejected = false;
                        return wasRejected ? new WatchResult(WatchKind.Restored) : WatchResult.Nothing;
                    }
                    _rejected = false;
                    Hash = content.Hash;
                    return new WatchResult(WatchKind.Reloaded, content);
                }
                catch (ContentError e)
                {
                    problems.AddRange(e.Problems);
                }
                catch (Exception e)
                {
                    problems.Add("Content could not be read: " + e.Message);
                }
            }
            _rejected = true;
            return new WatchResult(WatchKind.Rejected, null, problems);
        }

        /// <summary>The write time and size of each content file, which change whenever one is saved; "-" stands for a file that is not there.</summary>
        private string Stamp()
        {
            return string.Join("|", ContentFiles.FileNames.Select(name =>
            {
                var file = new FileInfo(Path.Combine(_folder, name + ".json"));
                return file.Exists ? file.LastWriteTimeUtc.Ticks + ":" + file.Length : "-";
            }));
        }
    }
}
