#nullable enable
using System;
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LabEscape.Client
{
    /// <summary>
    /// For tests and scripted runs: a copy of the repository's content/ in a folder of its own, which can be edited and broken
    /// without touching the real files. Every write gets a write time of its own, so a save is never mistaken for no change.
    /// Editor only, because it copies from the repository.
    /// </summary>
    public sealed class ScratchContent : IDisposable
    {
        private int _tick;

        public string Folder { get; }

        public ScratchContent()
        {
            Folder = Path.Combine(Path.GetTempPath(), "lab-escape-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            foreach (var name in ContentFiles.FileNames) Write(name, File.ReadAllText(GameContent.RepoPath("content", name + ".json")));
        }

        public string PathOf(string file) => Path.Combine(Folder, file + ".json");

        public string Read(string file) => File.ReadAllText(PathOf(file));

        /// <summary>Saves a file, as an editor would.</summary>
        public void Write(string file, string text)
        {
            File.WriteAllText(PathOf(file), text);
            File.SetLastWriteTimeUtc(PathOf(file), new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(++_tick));
        }

        /// <summary>Saves a file again as it is: the write time moves and the content does not.</summary>
        public void Touch(string file) => Write(file, Read(file));

        public void Delete(string file) => File.Delete(PathOf(file));

        /// <summary>Parses a file, lets `change` edit it, and saves it.</summary>
        public void Edit(string file, Action<JToken> change)
        {
            var token = JToken.Parse(Read(file));
            change(token);
            Write(file, token.ToString(Formatting.Indented));
        }

        /// <summary>Sets a unit's `hp` to any JSON value, a bad one included.</summary>
        public void SetUnitHp(string unit, JToken hp)
        {
            Edit("units", t => ((JArray)t).First(u => u["id"]?.Value<string>() == unit)["hp"] = hp);
        }

        public void Dispose()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        }
    }
}
