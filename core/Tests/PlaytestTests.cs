#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class PlaytestTests
    {
        private static PlaytestFight F(string name = "f", string? tester = "P1", double? attempt = 1, string? preset = "hybrid", FightResult outcome = FightResult.Lose, int rounds = 5, double ff = 0.15, int damage = 6, SourceCategory? cause = SourceCategory.Enemy, int hazardFires = 2, double[]? swarm = null, bool complete = true)
        {
            return new PlaytestFight { Name = name, Tester = tester, Attempt = attempt, Preset = preset, Outcome = outcome, Rounds = rounds, FriendlyFireShare = ff, PlayerDamage = damage, CauseCategory = cause, HazardFires = hazardFires, FullSwarmTurnSeconds = (swarm ?? new double[0]).ToList(), TurnTimesComplete = complete };
        }

        private const FightResult Win = FightResult.Win;

        private static MetricCheck Check(IReadOnlyList<PlaytestFight> fights, string metric, PlaytestOptions? opts = null) => Playtest.PlaytestChecks(fights, opts).First(c => c.Metric.StartsWith(metric, StringComparison.Ordinal));
        private static PlaytestOptions Opts(double from, double attempts) => new PlaytestOptions { InformedFrom = from, InformedAttempts = attempts, InformedPreset = "hybrid" };

        [Test]
        public void FirstAttemptsLostCountsTestersAtAttempt1Only()
        {
            var fights = new[] { F(tester: "P1"), F(tester: "P2"), F(tester: "P3"), F(tester: "P4"), F(tester: "P5", outcome: Win), F(tester: "P1", attempt: 2, outcome: Win) };
            var c = Check(fights, "First attempts lost");
            Assert.That(c.Value, Is.EqualTo("4 of 5 testers (80.0%); 3 with no fight after the loss"));
            Assert.That(c.Status, Is.EqualTo("ok"));
            Assert.That(Check(fights.Take(2).ToList(), "First attempts lost").Status, Is.EqualTo("warn"));
            Assert.That(Check(new[] { F(outcome: Win), F(tester: "P2", outcome: Win), F(tester: "P3") }, "First attempts lost").Status, Is.EqualTo("alarm"));
            Assert.That(Check(new[] { F(tester: null) }, "First attempts lost").Status, Is.EqualTo("n/a"));
        }

        [Test]
        public void FirstAttemptsOnePerTesterTheEarliest()
        {
            var fights = new[] { F(name: "1"), F(name: "2", outcome: Win), F(tester: "P2", name: "3"), F(tester: "P2", name: "4", attempt: 2, outcome: Win) };
            Assert.That(Check(fights, "First attempts lost").Value, Is.EqualTo("2 of 2 testers (100.0%); 1 with no fight after the loss"));
        }

        [Test]
        public void InformedRoundOnlyTheAttemptsItHas()
        {
            var fights = new[] { 2, 3, 4, 5, 6 }.Select(a => F(attempt: a, outcome: Win)).ToList();
            Assert.That(Check(fights, "Informed win rate").Value, Is.EqualTo("3 of 3 (100.0%); attempt 2: 1/1, attempt 3: 1/1, attempt 4: 1/1"));
            Assert.That(Check(fights, "Informed win rate", Opts(2, 2)).Value, Does.Contain("2 of 2"));
        }

        [Test]
        public void FirstTimeLossesEveryLostAttemptBeforeTheInformedRoundOnePerTesterAndAttempt()
        {
            var fights = new[] { F(rounds: 5), F(attempt: 2, rounds: 2), F(attempt: 2, rounds: 9, name: "dup"), F(attempt: 3, rounds: 1) };
            Assert.That(Check(fights, "First-time losses", Opts(3, 3)).Value, Is.EqualTo("median loss in round 3.5"));
            Assert.That(Check(fights, "First-time losses").Value, Is.EqualTo("median loss in round 5"));
        }

        [Test]
        public void FirstTimeLossesMedianRoundOfLostFirstAttempts()
        {
            Assert.That(Check(new[] { F(rounds: 4), F(tester: "P2", rounds: 6) }, "First-time losses").Status, Is.EqualTo("ok"));
            Assert.That(Check(new[] { F(rounds: 3) }, "First-time losses").Status, Is.EqualTo("warn"));
            Assert.That(Check(new[] { F(rounds: 2) }, "First-time losses").Status, Is.EqualTo("alarm"));
        }

        [Test]
        public void InformedWinRatePoolsTheChosenPresetFromTheChosenAttemptAndShowsEachAttempt()
        {
            var informed = new[] { F(attempt: 2, outcome: Win), F(tester: "P2", attempt: 2), F(attempt: 3), F(tester: "P2", attempt: 3, outcome: Win) };
            var noise = new[] { F(attempt: 1, outcome: Win), F(attempt: 2, preset: "tech", outcome: Win) };
            var c = Check(informed.Concat(noise).ToList(), "Informed win rate");
            Assert.That(c.Value, Is.EqualTo("2 of 4 (50.0%); attempt 2: 1/2, attempt 3: 1/2"));
            Assert.That(c.Status, Is.EqualTo("ok"));
            Assert.That(Check(new[] { F(attempt: 2, outcome: Win) }, "Informed win rate").Status, Is.EqualTo("alarm"));
            Assert.That(Check(informed, "Informed win rate", Opts(3, 3)).Value, Does.Contain("attempt 3: 1/2"));
            Assert.That(Check(new[] { F() }, "Informed win rate").Status, Is.EqualTo("n/a"));
        }

        [Test]
        public void InformedRoundOneFightPerTesterAndAttemptTheEarliest()
        {
            var fights = new[] { F(name: "1", attempt: 2, outcome: Win), F(name: "2", attempt: 2), F(name: "3", attempt: 3) };
            Assert.That(Check(fights, "Informed win rate").Value, Is.EqualTo("1 of 2 (50.0%); attempt 2: 1/1, attempt 3: 0/1"));
        }

        [Test]
        public void WinningFightsMedianRounds()
        {
            Assert.That(Check(new[] { F(outcome: Win, rounds: 10) }, "Length of winning").Status, Is.EqualTo("ok"));
            Assert.That(Check(new[] { F(outcome: Win, rounds: 7) }, "Length of winning").Status, Is.EqualTo("warn"));
            Assert.That(Check(new[] { F(outcome: Win, rounds: 17) }, "Length of winning").Status, Is.EqualTo("alarm"));
            Assert.That(Check(new[] { F() }, "Length of winning").Status, Is.EqualTo("n/a"));
        }

        [TestCase(new[] { 20.0, 40.0, 100.0 }, "40.0 s over 3 turns", "ok")]
        [TestCase(new[] { 59.96 }, "60.0 s over 1 turns", "warn")] // The figure shown is the figure judged: 59.96 s shows as 60.0 s and is not "under 60 s".
        [TestCase(new[] { 90.04 }, "90.0 s over 1 turns", "warn")]
        public void TurnTimeMedianSecondsOverTheFullSwarmTurns(double[] swarm, string value, string status)
        {
            var c = Check(new[] { F(swarm: swarm) }, "Median turn time");
            Assert.That(c.Value, Is.EqualTo(value));
            Assert.That(c.Status, Is.EqualTo(status));
        }

        [Test]
        public void TurnTimeStatusesAndTheNoteForFightsLeftOut()
        {
            Assert.That(Check(new[] { F(swarm: new[] { 70.0 }) }, "Median turn time").Status, Is.EqualTo("warn"));
            Assert.That(Check(new[] { F(swarm: new[] { 91.0 }) }, "Median turn time").Status, Is.EqualTo("alarm"));
            Assert.That(Check(new[] { F() }, "Median turn time").Status, Is.EqualTo("n/a"));
            Assert.That(Check(new[] { F(swarm: new[] { 30.0 }), F(complete: false) }, "Median turn time").Value, Is.EqualTo("30.0 s over 1 turns; 1 of 2 fights left out for missing or incomplete turn times"));
        }

        [Test]
        public void FriendlyFireDeathSourcesAndHazardFires()
        {
            Assert.That(Check(new[] { F(ff: 0.4) }, "Share of player damage").Status, Is.EqualTo("alarm"));
            Assert.That(Check(new[] { F(ff: 0.02) }, "Share of player damage").Status, Is.EqualTo("alarm"));
            Assert.That(Check(new[] { F(ff: 0.04) }, "Share of player damage").Status, Is.EqualTo("warn"));
            // Fights where the player took no damage have no share, and are left out of the mean.
            var flawless = Enumerable.Range(0, 9).Select(_ => F(ff: 0, damage: 0)).ToList();
            var c = Check(new[] { F(ff: 0.1) }.Concat(flawless).ToList(), "Share of player damage");
            Assert.That(c.Value, Is.EqualTo("10.0% (1 fights where the player took damage)"));
            Assert.That(c.Status, Is.EqualTo("ok"));
            Assert.That(Check(flawless, "Share of player damage").Status, Is.EqualTo("n/a"));
            var high = Check(new[] { F(ff: 0.352) }, "Share of player damage");
            Assert.That(high.Value, Does.Contain("35.2%"));
            Assert.That(high.Status, Is.EqualTo("alarm"));
            var hazard = Check(new[] { F(cause: SourceCategory.Hazard), F(cause: SourceCategory.Hazard), F(cause: SourceCategory.Enemy) }, "Share of deaths");
            Assert.That(hazard.Value, Is.EqualTo("hazard 66.7%, enemy 33.3%"));
            Assert.That(hazard.Status, Is.EqualTo("alarm"));
            Assert.That(Check(new[] { F(cause: SourceCategory.Enemy), F(cause: SourceCategory.Enemy), F(cause: SourceCategory.Minion) }, "Share of deaths").Status, Is.EqualTo("warn"));
            Assert.That(Check(new[] { F(cause: null), F(cause: SourceCategory.Enemy) }, "Share of deaths").Value, Is.EqualTo("enemy 50.0%, round cap 50.0%"));
            Assert.That(Check(new[] { F(hazardFires: 0) }, "Hazard fires").Status, Is.EqualTo("alarm"));
            Assert.That(Check(new[] { F(hazardFires: 1) }, "Hazard fires").Status, Is.EqualTo("warn"));
            Assert.That(Check(new[] { F(outcome: Win) }, "Share of deaths").Status, Is.EqualTo("n/a"));
            Assert.That(Check(new PlaytestFight[0], "Hazard fires").Status, Is.EqualTo("n/a"));
        }

        private string _root = "";

        [SetUp]
        public void MakeFolder()
        {
            _root = Path.Combine(Path.GetTempPath(), "lab-escape-telemetry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void RemoveFolder() => Directory.Delete(_root, recursive: true);

        private static FightRecord PlayFight(int seed, RecordMeta? meta = null) => Runner.RunFight(C, PresetLoadout("hybrid"), Batch.MakeBot(Batch.GreedyKind, seed), meta: meta).Record;
        private static RecordMeta Labelled(string tester = "P1", double? attempt = 1) => new RecordMeta { Preset = "hybrid", Tester = tester, Attempt = attempt };

        private void Save(string name, JToken rec)
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
            File.WriteAllText(Path.Combine(_root, name, "replay.json"), rec.ToString());
        }

        private static JObject Edited(FightRecord rec, Action<JObject> edit)
        {
            var token = rec.ToToken();
            edit(token);
            return token;
        }

        private static FightRecord WithTimes(FightRecord rec, List<double>? times)
        {
            var token = rec.ToToken();
            var meta = (JObject)token["meta"]!;
            if (times == null) meta.Remove("turnTimesMs");
            else meta["turnTimesMs"] = new JArray(times);
            return Records.CheckCompatible(C, token);
        }

        [Test]
        public void PairsEachTurnTimeWithTheSwarmAtTheEndOfThatTurnAndCountsOnlyFullSwarms()
        {
            var rec = PlayFight(1, Labelled());
            var last = rec.Commands[rec.Commands.Count - 1];
            var turns = rec.Commands.Count(c => c is EndTurnCommand) + (last is EndTurnCommand ? 0 : 1);
            // One time per turn, 1 second for the first turn, then 2, 3, ...
            var times = Enumerable.Range(1, turns).Select(i => i * 1000.0).ToList();
            var f = Playtest.AnalyseFight(C, WithTimes(rec, times), "x", out _);
            Assert.That(f, Is.Not.Null);
            Assert.That(f!.Tester, Is.EqualTo("P1"));
            Assert.That(f.Attempt, Is.EqualTo(1));
            Assert.That(f.FullSwarmTurnSeconds.Count, Is.LessThanOrEqualTo(turns));
            foreach (var s in f.FullSwarmTurnSeconds) Assert.That(s, Is.GreaterThanOrEqualTo(1));
            Assert.That(f.TurnTimesComplete, Is.True);
            // A missing time shifts the rest onto the wrong turns, so the fight gives no turn time at all.
            var shortened = Playtest.AnalyseFight(C, WithTimes(rec, times.Skip(1).ToList()), "z", out _);
            Assert.That(shortened!.TurnTimesComplete, Is.False);
            Assert.That(shortened.FullSwarmTurnSeconds, Is.Empty);
            // With no turn times recorded there is nothing to measure.
            Assert.That(Playtest.AnalyseFight(C, WithTimes(rec, null), "y", out _)!.FullSwarmTurnSeconds, Is.Empty);
        }

        [Test]
        public void ReadsGoodFightsAndReportsEveryFolderItSkipsWithTheReason()
        {
            Save("a-good", PlayFight(1, Labelled()).ToToken());
            Save("b-other-engine", Edited(PlayFight(2), r => r["engineVersion"] = "0.0.1"));
            Save("c-cheat", Edited(PlayFight(3), r => r["commands"] = JArray.Parse("[{type:'debug',op:'forceWave'}]")));
            Save("f-held-back", Edited(PlayFight(4), r => r["settings"] = JObject.Parse("{maxWaves:2}")));
            Save("d-junk", JObject.Parse("{nope:true}"));
            Directory.CreateDirectory(Path.Combine(_root, "e-empty"));
            File.WriteAllText(Path.Combine(_root, "loose-file.txt"), "ignored");
            var load = Playtest.LoadTelemetry(C, _root);
            Assert.That(load.Fights.Select(f => f.Name), Is.EqualTo(new[] { "a-good" }));
            var reasons = load.Skipped.ToDictionary(s => s.Name, s => s.Reason);
            Assert.That(reasons["b-other-engine"], Does.Contain("engine"));
            Assert.That(reasons["c-cheat"], Is.EqualTo("used debug commands"));
            Assert.That(reasons["f-held-back"], Is.EqualTo("played with wave 3 held back"));
            Assert.That(reasons["d-junk"], Is.Not.Empty);
            Assert.That(reasons["e-empty"], Is.EqualTo("no replay.json"));
            var text = Playtest.FormatPlaytest(_root, load);
            Assert.That(text, Does.Contain("Fights counted: 1; skipped: 5"));
            Assert.That(text, Does.Contain("P1: a1 hybrid"));
            Assert.That(text, Does.Contain("["));
        }

        [Test]
        public void ReportsAFileThatIsNotJsonAsSkipped()
        {
            Directory.CreateDirectory(Path.Combine(_root, "broken"));
            File.WriteAllText(Path.Combine(_root, "broken", "replay.json"), "{ not json");
            var load = Playtest.LoadTelemetry(C, _root);
            Assert.That(load.Fights, Is.Empty);
            Assert.That(load.Skipped.Single().Name, Is.EqualTo("broken"));
        }

        [Test]
        public void SkipsFightsWithNoTesterIdUnlessAskedToCountThem()
        {
            Save("a-labelled", PlayFight(1, Labelled()).ToToken());
            Save("b-ordinary-play", PlayFight(2, new RecordMeta { Preset = "hybrid" }).ToToken());
            var load = Playtest.LoadTelemetry(C, _root);
            Assert.That(load.Fights.Select(f => f.Name), Is.EqualTo(new[] { "a-labelled" }));
            Assert.That(load.Skipped.Single().Name, Is.EqualTo("b-ordinary-play"));
            Assert.That(load.Skipped.Single().Reason, Does.Contain("no tester ID"));
            Assert.That(Playtest.LoadTelemetry(C, _root, includeUnlabelled: true).Fights.Select(f => f.Name), Is.EqualTo(new[] { "a-labelled", "b-ordinary-play" }));
        }

        [Test]
        public void RefusesAReplayWithANegativeTurnTime()
        {
            var meta = Labelled();
            meta.TurnTimesMs = new List<double> { -5000 };
            Save("a-negative", PlayFight(1, meta).ToToken());
            var load = Playtest.LoadTelemetry(C, _root);
            Assert.That(load.Fights, Is.Empty);
            Assert.That(load.Skipped[0].Reason, Does.Contain("turnTimesMs[0]"));
        }

        [Test]
        public void TakesThePresetFromTheLoadoutNotFromWhatTheReplaySaysAboutItself()
        {
            var meta = Labelled();
            meta.Preset = "tech";
            Assert.That(Playtest.AnalyseFight(C, PlayFight(1, meta), "x", out _)!.Preset, Is.EqualTo("hybrid"));
        }

        [Test]
        public void LeavesOutTheTestersItIsToldTo()
        {
            Save("a", PlayFight(1, Labelled("DEV")).ToToken());
            Save("b", PlayFight(2, Labelled("P1")).ToToken());
            var load = Playtest.LoadTelemetry(C, _root, excludeTesters: new[] { "DEV" });
            Assert.That(load.Fights.Select(f => f.Tester), Is.EqualTo(new[] { "P1" }));
            Assert.That(load.Skipped.Single().Name, Is.EqualTo("a"));
            Assert.That(load.Skipped.Single().Reason, Does.Contain("DEV excluded"));
        }

        [Test]
        public void StillShowsEveryMetricAsNaWhenNothingWasCounted()
        {
            var load = new TelemetryLoad { Skipped = { new SkippedFight { Name = "x", Reason = "unfinished" } } };
            var text = Playtest.FormatPlaytest("telemetry", load);
            Assert.That(text, Does.Contain("Fights counted: 0; skipped: 1"));
            Assert.That(text.Split("[N/A").Length - 1, Is.EqualTo(8));
            Assert.That(text, Does.Not.Contain("By tester"));
        }

        [Test]
        public void RefusesAFightWithATesterIdButNoAttemptNumber()
        {
            Save("a-no-attempt", PlayFight(1, Labelled(attempt: null)).ToToken());
            var load = Playtest.LoadTelemetry(C, _root);
            Assert.That(load.Fights, Is.Empty);
            Assert.That(load.Skipped.Single().Reason, Is.EqualTo("has a tester ID but no attempt number"));
        }

        [Test]
        public void SaysWhatIsWrongWhenTheFolderDoesNotExist()
        {
            var e = Assert.Throws<DirectoryNotFoundException>(() => Playtest.LoadTelemetry(C, Path.Combine(Path.GetTempPath(), "lab-escape-no-such-folder")));
            Assert.That(e!.Message, Does.Contain("No telemetry folder"));
        }
    }
}
