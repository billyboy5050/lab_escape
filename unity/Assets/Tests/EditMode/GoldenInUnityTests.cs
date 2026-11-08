#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>
    /// The rules core compiled by Unity, on Unity's runtime, must play the fights the TypeScript engine recorded exactly as the .NET
    /// build of the same source does. A difference here would be a runtime difference that dotnet test cannot see.
    /// </summary>
    public class GoldenInUnityTests
    {
        private static readonly string[] Armory = { "alien", "hybrid", "tech" };
        private static readonly string[] Rooms = { "room-closet", "room-hall", "room-hallways" };

        private static string[] Fights() => Armory.Concat(Rooms).ToArray();

        private static Content ContentFor(string fight)
        {
            var files = ContentLoader.FilesFromDirectory(GameContent.RepoPath("content"));
            if (!fight.StartsWith("room-", System.StringComparison.Ordinal)) return ContentLoader.Build(files);
            files.Root["rooms"] = JsonIn.Parse(File.ReadAllText(GameContent.RepoPath("golden", "rooms.json")));
            return ContentLoader.Build(files);
        }

        [TestCaseSource(nameof(Fights))]
        public void EventLogIsIdenticalLineByLine(string name)
        {
            var c = ContentFor(name);
            var record = Records.CheckCompatible(c, JsonIn.Parse(File.ReadAllText(GameContent.RepoPath("golden", name + ".json")))["record"]);
            var game = Engine.NewGame(c, record.Loadout, record.Settings);
            var state = game.State;
            var events = new List<GameEvent>(game.Events);
            foreach (var cmd in record.Commands)
            {
                var r = Engine.Step(c, state, cmd);
                Assert.That(r.Ok, Is.True, r.Error);
                state = r.State;
                events.AddRange(r.Events);
            }
            var expected = File.ReadAllText(GameContent.RepoPath("golden", name + ".events.jsonl")).TrimEnd('\n').Split('\n');
            Assert.That(events.Count, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++) Assert.That(events[i].ToJson(), Is.EqualTo(expected[i]), $"event {i} differs");
        }

        [Test]
        public void ContentHashMatchesTheHashRecordedByTypeScript()
        {
            var c = GameContent.Load();
            var golden = JObject.Parse(File.ReadAllText(GameContent.RepoPath("golden", "hybrid.json")));
            Assert.That(c.Hash, Is.EqualTo(golden["record"]!["contentHash"]!.Value<string>()));
        }
    }
}
