#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>The port's central proof: the fights recorded by the TypeScript engine replay identically here.</summary>
    public class GoldenTests
    {
        private static string[] Presets() => C.Presets.Select(p => p.Id).ToArray();

        private static JToken Golden(string preset) => JsonIn.Parse(File.ReadAllText(Path.Combine(RepoRoot, "golden", preset + ".json")));

        private static (List<GameEvent> Events, GameState State) Replay(JToken record)
        {
            var loadout = record["loadout"]!;
            var maxWaves = record["settings"]?["maxWaves"];
            var settings = new FightSettings { MaxWaves = maxWaves?.Value<int>() };
            var game = Engine.NewGame(C, new Loadout(loadout["abilities"]!.Values<string>()!, loadout["upgrades"]!.Values<string>()!), settings);
            var state = game.State;
            var events = new List<GameEvent>(game.Events);
            var i = 0;
            foreach (var token in (JArray)record["commands"]!)
            {
                i++;
                var r = Engine.Step(C, state, Command.Parse(token));
                Assert.That(r.Ok, Is.True, $"Command {i} ({token["type"]}) was rejected: {r.Error}");
                state = r.State;
                events.AddRange(r.Events);
            }
            return (events, state);
        }

        [TestCaseSource(nameof(Presets))]
        public void RecordWasMadeWithThisEngineAndContent(string preset)
        {
            var record = Golden(preset)["record"]!;
            Assert.That(record["engineVersion"]!.Value<string>(), Is.EqualTo(GameStates.EngineVersion));
            Assert.That(C.Hash, Is.EqualTo(record["contentHash"]!.Value<string>()), "the C# content hash must equal the hash the TypeScript engine recorded");
        }

        [TestCaseSource(nameof(Presets))]
        public void EventLogIsIdenticalLineByLine(string preset)
        {
            var expected = File.ReadAllText(Path.Combine(RepoRoot, "golden", preset + ".events.jsonl")).TrimEnd('\n').Split('\n');
            var (events, _) = Replay(Golden(preset)["record"]!);
            for (var i = 0; i < expected.Length && i < events.Count; i++)
            {
                Assert.That(events[i].ToJson(), Is.EqualTo(expected[i]), $"event {i} differs");
            }
            Assert.That(events.Count, Is.EqualTo(expected.Length));
        }

        [TestCaseSource(nameof(Presets))]
        public void HashesOfEventsAndFinalStateMatch(string preset)
        {
            var golden = Golden(preset);
            var (events, state) = Replay(golden["record"]!);
            var eventArray = new JArray(events.Select(e => JsonIn.Parse(e.ToJson())));
            Assert.That(events.Count, Is.EqualTo(golden["eventCount"]!.Value<int>()));
            Assert.That(Hash.HashJson(eventArray), Is.EqualTo(golden["eventsHash"]!.Value<string>()));
            Assert.That(Hash.HashJson(JsonIn.Parse(state.ToJson())), Is.EqualTo(golden["finalStateHash"]!.Value<string>()));
            Assert.That(state.Outcome?.Result, Is.EqualTo(Names.Parse<FightResult>(golden["outcome"]!.Value<string>()!)));
            Assert.That(state.Round, Is.EqualTo(golden["rounds"]!.Value<int>()));
        }
    }
}
