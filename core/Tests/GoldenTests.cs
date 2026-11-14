#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>The golden fights replay identically: first recorded by the TypeScript engine, this was the port's central proof.</summary>
    public class GoldenTests
    {
        private static readonly List<GoldenFight> AllFights = Golden.Fights(ContentLoader.FilesFromDirectory(Path.Combine(RepoRoot, "content")), JsonIn.Parse(File.ReadAllText(Path.Combine(RepoRoot, "golden", "rooms.json"))));

        private static string[] Fights() => AllFights.Select(f => f.Name).ToArray();

        private static Content ContentOf(string name) => AllFights.First(f => f.Name == name).Content;

        private static JToken GoldenFile(string name) => JsonIn.Parse(File.ReadAllText(Path.Combine(RepoRoot, "golden", name + ".json")));

        private static (List<GameEvent> Events, GameState State) Replay(string name, JToken record)
        {
            var content = ContentOf(name);
            var loadout = record["loadout"]!;
            var maxWaves = record["settings"]?["maxWaves"];
            var settings = new FightSettings { MaxWaves = maxWaves?.Value<int>(), Room = record["settings"]?["room"]?.Value<string>() };
            var game = Engine.NewGame(content, new Loadout(loadout["abilities"]!.Values<string>()!, loadout["upgrades"]!.Values<string>()!), settings);
            var state = game.State;
            var events = new List<GameEvent>(game.Events);
            var i = 0;
            foreach (var token in (JArray)record["commands"]!)
            {
                i++;
                var r = Engine.Step(content, state, Command.Parse(token));
                Assert.That(r.Ok, Is.True, $"Command {i} ({token["type"]}) was rejected: {r.Error}");
                state = r.State;
                events.AddRange(r.Events);
            }
            return (events, state);
        }

        [TestCaseSource(nameof(Fights))]
        public void RecordWasMadeWithThisEngineAndContent(string name)
        {
            var record = GoldenFile(name)["record"]!;
            Assert.That(record["engineVersion"]!.Value<string>(), Is.EqualTo(GameStates.EngineVersion));
            Assert.That(ContentOf(name).Hash, Is.EqualTo(record["contentHash"]!.Value<string>()), "the content hash must equal the hash the golden replay recorded");
        }

        [TestCaseSource(nameof(Fights))]
        public void EventLogIsIdenticalLineByLine(string name)
        {
            var expected = File.ReadAllText(Path.Combine(RepoRoot, "golden", name + ".events.jsonl")).TrimEnd('\n').Split('\n');
            var (events, _) = Replay(name, GoldenFile(name)["record"]!);
            for (var i = 0; i < expected.Length && i < events.Count; i++)
            {
                Assert.That(events[i].ToJson(), Is.EqualTo(expected[i]), $"event {i} differs");
            }
            Assert.That(events.Count, Is.EqualTo(expected.Length));
        }

        [TestCaseSource(nameof(Fights))]
        public void HashesOfEventsAndFinalStateMatch(string name)
        {
            var golden = GoldenFile(name);
            var (events, state) = Replay(name, golden["record"]!);
            var eventArray = new JArray(events.Select(e => JsonIn.Parse(e.ToJson())));
            Assert.That(events.Count, Is.EqualTo(golden["eventCount"]!.Value<int>()));
            Assert.That(Hash.HashJson(eventArray), Is.EqualTo(golden["eventsHash"]!.Value<string>()));
            Assert.That(Hash.HashJson(JsonIn.Parse(state.ToJson())), Is.EqualTo(golden["finalStateHash"]!.Value<string>()));
            Assert.That(state.Outcome?.Result, Is.EqualTo(Names.Parse<FightResult>(golden["outcome"]!.Value<string>()!)));
            Assert.That(state.Round, Is.EqualTo(golden["rounds"]!.Value<int>()));
        }

        [TestCaseSource(nameof(Fights))]
        public void TheGoldenCheckPasses(string name)
        {
            var expected = JsText.Trim(File.ReadAllText(Path.Combine(RepoRoot, "golden", name + ".events.jsonl"))).Split('\n');
            var r = Golden.Verify(ContentOf(name), GoldenFile(name), expected);
            Assert.That(r.Problem ?? "ok", Is.EqualTo("ok"));
        }

        [TestCaseSource(nameof(Fights))]
        public void RecordingAGoldenWritesTheCommittedFileExactly(string name)
        {
            var fight = AllFights.First(f => f.Name == name);
            var (golden, events) = Golden.Record(fight.Content, fight.Preset, fight.Settings);
            Assert.That(Hash.Stringify(golden, 2) + "\n", Is.EqualTo(File.ReadAllText(Path.Combine(RepoRoot, "golden", name + ".json"))));
            Assert.That(Records.EventsToJsonl(events), Is.EqualTo(File.ReadAllText(Path.Combine(RepoRoot, "golden", name + ".events.jsonl"))));
        }

        [Test]
        public void AChangedEventLogIsReportedWithWhereItFirstDiffers()
        {
            var golden = GoldenFile("tech");
            golden["eventsHash"] = "changed";
            var expected = JsText.Trim(File.ReadAllText(Path.Combine(RepoRoot, "golden", "tech.events.jsonl"))).Split('\n');
            expected[3] = "{}";
            var r = Golden.Verify(C, golden, expected);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.FirstDifference, Is.EqualTo(3));
            Assert.That(r.Problem, Does.Contain("first difference at event 3"));
        }
    }
}
