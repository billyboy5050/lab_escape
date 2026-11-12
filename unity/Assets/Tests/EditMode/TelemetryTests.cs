#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>A fight's telemetry as the Unity client keeps it: the web client's three files, folder names, testers and attempts, and replays.</summary>
    public class TelemetryTests
    {
        private static readonly Content C = GameContent.Load();

        private sealed class Prefs : IPrefs
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
            public void Set(string key, string value) => Values[key] = value;
        }

        private static FightRecord WinningLine(string preset) =>
            Telemetry.LoadReplay(C, File.ReadAllText(GameContent.RepoPath("reports", $"winning-line-{preset}.json")));

        /// <summary>A session that has played a whole winning line, as the client would have.</summary>
        private static FightSession Played(string preset, string? tester = null)
        {
            var rec = WinningLine(preset);
            var meta = new RecordMeta { Label = "unity", Preset = preset };
            if (tester != null) meta.Tester = tester;
            var s = new FightSession(C, rec.Loadout, rec.Settings, meta, () => 1_800_000_000_000);
            foreach (var cmd in rec.Commands) Assert.That(s.Apply(cmd).Ok, Is.True);
            return s;
        }

        [Test]
        public void AFightsFilesAreTheWebClientsThree()
        {
            var s = Played("tech");
            var files = Telemetry.Files(s);
            Assert.That(files.Select(f => f.Name), Is.EqualTo(new[] { "replay.json", "summary.json", "events.jsonl" }));

            var replay = Telemetry.LoadReplay(C, files[0].Text);
            Assert.That(replay.Commands.Count, Is.EqualTo(s.Record.Commands.Count));
            Assert.That(Records.Replay(C, replay).State.Outcome?.Result, Is.EqualTo(FightResult.Win), "the saved replay plays to the same end");

            // Compared as canonical JSON: a number read back from text is typed by how it is written (0 against 0.0).
            Assert.That(Hash.Stringify(JToken.Parse(files[1].Text)), Is.EqualTo(Hash.Stringify(s.Summary().ToToken())), "the summary is the session's");
            Assert.That(files[2].Text.TrimEnd('\n').Split('\n').Length, Is.EqualTo(s.Events.Count), "one line for each event");
        }

        [Test]
        public void TheFolderIsNamedForTheTimeTheTesterAndTheOutcome()
        {
            var s = Played("tech", "P3");
            s.Record.Meta!.Attempt = 2;
            Assert.That(Telemetry.FolderName(1_800_000_000_000, s), Is.EqualTo("2027-01-15T08-00-00-000Z-P3-a2-tech-win"));
            Assert.That(Telemetry.OutcomeWord(null), Is.EqualTo("unfinished"));
        }

        [Test]
        public void ATesterGivenThisRunIsRememberedAndCleaned()
        {
            var prefs = new Prefs();
            Assert.That(Telemetry.ChooseTester(null, prefs), Is.Null);
            Assert.That(Telemetry.ChooseTester("P 3/x", prefs), Is.EqualTo("P_3_x"));
            Assert.That(Telemetry.ChooseTester(null, prefs), Is.EqualTo("P_3_x"), "remembered");
            prefs.Values[Telemetry.TesterKey] = "../../evil";
            Assert.That(Telemetry.ChooseTester(null, prefs), Is.EqualTo("______evil"), "a saved value is cleaned too");
            Assert.That(Telemetry.ChooseTester(Telemetry.NoTester, prefs), Is.Null, "\"-\" forgets the tester");
            Assert.That(Telemetry.ChooseTester(null, prefs), Is.Null);
        }

        [Test]
        public void AttemptsCountPerTester()
        {
            var prefs = new Prefs();
            var a = new RecordMeta { Tester = "P3" };
            var b = new RecordMeta { Tester = "P3" };
            var c = new RecordMeta { Tester = "P4" };
            var nobody = new RecordMeta();
            Telemetry.AssignAttempt(a, prefs);
            Telemetry.AssignAttempt(b, prefs);
            Telemetry.AssignAttempt(c, prefs);
            Telemetry.AssignAttempt(nobody, prefs);
            Assert.That(new[] { a.Attempt, b.Attempt, c.Attempt, nobody.Attempt }, Is.EqualTo(new double?[] { 1, 2, 1, null }));
        }

        [TestCase("not json")]
        [TestCase("[1, 2]")]
        [TestCase("{\"P3\": -4}")]
        [TestCase("{\"P3\": 2.5}")]
        [TestCase("{\"P3\": \"7\"}")]
        [TestCase("{\"P3\": 99999999999}")]
        public void StoredCountsThatAreNotWholeNumbersCountAsNone(string stored)
        {
            var prefs = new Prefs();
            prefs.Values[Telemetry.AttemptsKey] = stored;
            var m = new RecordMeta { Tester = "P3" };
            Telemetry.AssignAttempt(m, prefs);
            Assert.That(m.Attempt, Is.EqualTo(1));
        }

        [Test]
        public void AReplayThatDoesNotFitIsRefusedWithItsReason()
        {
            Assert.That(() => Telemetry.LoadReplay(C, "{ not json"), Throws.TypeOf<ReplayMismatch>().With.Message.Contains("not JSON"));
            Assert.That(() => Telemetry.LoadReplay(C, "[]"), Throws.TypeOf<ReplayMismatch>().With.Message.Contains("not a replay"));
            var raw = JObject.Parse(File.ReadAllText(GameContent.RepoPath("reports", "winning-line-tech.json")));
            raw["contentHash"] = "0000";
            Assert.That(() => Telemetry.LoadReplay(C, raw.ToString()), Throws.TypeOf<ReplayMismatch>().With.Message.Contains("content"));
            Assert.That(WinningLine("alien").Commands.Count, Is.GreaterThan(0));
        }

        [Test]
        public void SavedFightsAreListedNewestFirstAndOnlyWithAReplay()
        {
            var root = Path.Combine(Path.GetTempPath(), "lab-escape-recent-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var name in new[] { "2026-11-10T09-00-00-000Z-tech-win", "2026-11-12T15-30-04-512Z-P3-a2-alien-lose", "2026-11-11T12-00-00-000Z-hybrid-win" })
                {
                    Directory.CreateDirectory(Path.Combine(root, name));
                    File.WriteAllText(Path.Combine(root, name, "replay.json"), "{}");
                }
                Directory.CreateDirectory(Path.Combine(root, "2026-11-13T00-00-00-000Z-no-replay"));
                var recent = Telemetry.Recent(root, 2);
                Assert.That(recent.Select(r => r.Label), Is.EqualTo(new[] { "2026-11-12 15:30  P3 a2 alien lose", "2026-11-11 12:00  hybrid win" }));
                Assert.That(Telemetry.Recent(Path.Combine(root, "missing"), 5), Is.Empty);
                Assert.That(Telemetry.Recent(null, 5), Is.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void QueryParametersAndArgumentsAreRead()
        {
            Assert.That(Telemetry.QueryParam("https://x.test/index.html?tester=P3&replay=fights%2Fone.json#top", "replay"), Is.EqualTo("fights/one.json"));
            Assert.That(Telemetry.QueryParam("https://x.test/?tester=P+3", "tester"), Is.EqualTo("P 3"));
            Assert.That(Telemetry.QueryParam("https://x.test/", "tester"), Is.Null);
            Assert.That(Telemetry.QueryParam("", "tester"), Is.Null);
            Assert.That(Telemetry.Arg(new[] { "game", "-labTester", "P3" }, "-labTester"), Is.EqualTo("P3"));
            Assert.That(Telemetry.Arg(new[] { "game", "-labTester" }, "-labTester"), Is.Null);
        }
    }
}
