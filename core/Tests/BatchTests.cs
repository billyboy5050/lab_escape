#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class BatchTests
    {
        /// <summary>Everything a row holds, for comparing two batches.</summary>
        private static string Snapshot(IEnumerable<FightRow> rows)
        {
            var list = rows.ToList();
            var use = new JArray(list.Select(r => Summaries.Counts(r.AbilitiesUsed)));
            var records = new JArray(list.Select(r => r.Record == null ? JValue.CreateNull() : (JToken)r.Record.ToToken()));
            return Report.RowsToCsv(list) + use.ToString() + records.ToString();
        }

        [Test]
        public void TheSameBatchWithTheSameSeedsGivesIdenticalResults()
        {
            var spec = new BatchSpec { Bot = Batch.RandomKind, Loadout = LoadoutChoice.OfSweep(), Seeds = Batch.SeedRange(1, 40) };
            Assert.That(Snapshot(Batch.RunBatch(spec, C)), Is.EqualTo(Snapshot(Batch.RunBatch(spec, C))));
        }

        [Test]
        public void GreedyBatchesAreReproducibleToo()
        {
            var spec = new BatchSpec { Bot = Batch.GreedyKind, Loadout = LoadoutChoice.OfPreset("alien"), Seeds = Batch.SeedRange(1, 3) };
            Assert.That(Snapshot(Batch.RunBatch(spec, C)), Is.EqualTo(Snapshot(Batch.RunBatch(spec, C))));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(8)]
        public void TheWorkerCountChangesNothing(int workers)
        {
            var spec = new BatchSpec { Bot = Batch.RandomKind, Loadout = LoadoutChoice.OfSweep(), Seeds = Batch.SeedRange(1, 40), KeepRecords = true };
            Assert.That(Snapshot(Batch.RunBatchParallel(spec, C, workers)), Is.EqualTo(Snapshot(Batch.RunBatch(spec, C))));
        }

        [Test]
        public void RandomBotsFindNoCrashesOrInfiniteLoopsAcrossEveryPreset()
        {
            foreach (var p in C.Presets)
            {
                var rows = Batch.RunBatch(new BatchSpec { Bot = Batch.RandomKind, Loadout = LoadoutChoice.OfPreset(p.Id), Seeds = Batch.SeedRange(1, 60) }, C);
                Assert.That(rows.Where(r => r.Outcome == FightOutcome.Error).Select(r => r.Error), Is.Empty);
            }
        }

        [Test]
        public void ARandomLoadoutIsValidAndInContentOrder()
        {
            for (var seed = 1; seed <= 200; seed++)
            {
                var l = Batch.RandomLoadout(C, seed);
                Assert.That(Engine.LoadoutProblems(C, l), Is.Empty);
                Assert.That(l.Abilities, Has.Count.EqualTo(C.Rules.Loadout.Abilities));
                Assert.That(l.Abilities, Is.EqualTo(C.AbilityOrder.Where(l.Abilities.Contains)));
                Assert.That(l.Upgrades, Is.EqualTo(C.UpgradeOrder.Where(l.Upgrades.Contains)));
            }
        }

        [Test]
        public void AnUnknownPresetIsAnErrorThatNamesItAtAnyWorkerCount()
        {
            var spec = new BatchSpec { Bot = Batch.RandomKind, Loadout = LoadoutChoice.OfPreset("nope"), Seeds = Batch.SeedRange(1, 8) };
            foreach (var workers in new[] { 1, 2 })
            {
                var e = Assert.Throws<ArgumentException>(() => Batch.RunBatchParallel(spec, C, workers));
                Assert.That(e!.Message, Is.EqualTo("Unknown preset \"nope\""));
            }
        }

        [Test]
        public void AFightThatExceedsTheCommandLimitRaisesABotErrorWithItsLog()
        {
            var e = Assert.Throws<BotError>(() => Runner.RunFight(C, PresetLoadout("tech"), new ScriptedBot(Enumerable.Repeat<Command>(new EndTurnCommand(), 50).ToList()), maxCommands: 1));
            Assert.That(e!.Message, Does.StartWith("Command limit (1) reached in round"));
            Assert.That(e.Record.Commands, Has.Count.EqualTo(1));
        }

        [Test]
        public void ABotThatIssuesAnIllegalCommandRaisesABotError()
        {
            var e = Assert.Throws<BotError>(() => Runner.RunFight(C, PresetLoadout("tech"), new ScriptedBot(new Command[] { Move("Z9") })));
            Assert.That(e!.Message, Does.StartWith("Bot scripted issued an illegal command in round 1:"));
            Assert.That(e.Message, Does.EndWith("({\"type\":\"move\",\"path\":[\"Z9\"]})"));
        }

        [Test]
        public void TheGeneratorDrawsTheSameSequenceAsTheTypeScriptOne()
        {
            var rng = new Rng(12345);
            // What the TypeScript simulator's generator drew, so a seed in a committed report still names the same fight.
            Assert.That(new[] { rng.NextU32(), rng.NextU32(), rng.NextU32() }, Is.EqualTo(new uint[] { 4207900869, 1317490944, 2079646450 }));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void RefusesAWorkerCountBelowOne(int workers)
        {
            var spec = new BatchSpec { Bot = Batch.GreedyKind, Loadout = LoadoutChoice.OfPreset("hybrid"), Seeds = new long[] { 1, 2, 3, 4 } };
            var e = Assert.Throws<ArgumentException>(() => Batch.RunBatchParallel(spec, C, workers));
            Assert.That(e!.Message, Does.Contain("workers must be a whole number of at least 1"));
        }

        [Test]
        public void ParseIntegerReturnsNullForAnOptionThatWasNotGiven()
        {
            Assert.That(Options.ParseInteger("workers", null, 1), Is.Null);
        }

        [Test]
        public void ParseIntegerAcceptsWholeNumbers()
        {
            Assert.That(Options.ParseInteger("workers", "4", 1), Is.EqualTo(4));
            Assert.That(Options.ParseInteger("seeds", "1000", 1), Is.EqualTo(1000));
            Assert.That(Options.ParseInteger("from", "0"), Is.EqualTo(0));
            Assert.That(Options.ParseInteger("from", "-3"), Is.EqualTo(-3));
        }

        [TestCase("1e3", 1000)]
        [TestCase("0x10", 16)]
        [TestCase(" 7 ", 7)]
        [TestCase("5.0", 5)]
        public void ParseIntegerReadsNumbersAsJavaScriptDoes(string raw, long expected)
        {
            Assert.That(Options.ParseInteger("seeds", raw, 1), Is.EqualTo(expected));
        }

        [TestCase("foo")]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("2.5")]
        [TestCase("Infinity")]
        [TestCase("NaN")]
        [TestCase("1e999")]
        [TestCase("4x")]
        public void ParseIntegerRejects(string raw)
        {
            var e = Assert.Throws<ArgumentException>(() => Options.ParseInteger("workers", raw, 1));
            Assert.That(e!.Message, Does.StartWith("--workers must be a whole number of at least 1"));
        }

        [Test]
        public void ParseIntegerRejectsANumberBelowTheMinimumAndNamesTheOptionAndTheValue()
        {
            var e = Assert.Throws<ArgumentException>(() => Options.ParseInteger("seeds", "0", 1));
            Assert.That(e!.Message, Is.EqualTo("--seeds must be a whole number of at least 1 (got \"0\")"));
        }

        [Test]
        public void ParseIntegerSaysOnlyWholeNumberWhenThereIsNoMinimum()
        {
            var e = Assert.Throws<ArgumentException>(() => Options.ParseInteger("from", "abc"));
            Assert.That(e!.Message, Is.EqualTo("--from must be a whole number (got \"abc\")"));
        }

        [Test]
        public void TheCommandLineTakesValuesBothWaysAndRepeats()
        {
            var a = ParsedArgs.Parse(new[] { "presets", "--seeds", "10", "--bot=random", "--set", "a=1", "--set", "b=2", "--hold-wave3" });
            Assert.That(a.Positionals, Is.EqualTo(new[] { "presets" }));
            Assert.That(a.Value("seeds"), Is.EqualTo("10"));
            Assert.That(a.Value("bot"), Is.EqualTo("random"));
            Assert.That(a.List("set"), Is.EqualTo(new[] { "a=1", "b=2" }));
            Assert.That(a.Flag("hold-wave3"), Is.True);
            Assert.That(a.Flag("record"), Is.False);
        }

        [TestCase("--nope", "Unknown option '--nope'")]
        [TestCase("-x", "Unknown option '-x'")]
        [TestCase("--record=yes", "Option '--record' does not take an argument")]
        [TestCase("--seeds", "Option '--seeds <value>' argument missing")]
        public void TheCommandLineRefusesWhatNodeRefuses(string arg, string message)
        {
            var e = Assert.Throws<ArgumentException>(() => ParsedArgs.Parse(new[] { arg }));
            Assert.That(e!.Message, Is.EqualTo(message));
        }

        [Test]
        public void ANegativeValueNeedsTheEqualsForm()
        {
            var e = Assert.Throws<ArgumentException>(() => ParsedArgs.Parse(new[] { "--from", "-3" }));
            Assert.That(e!.Message, Does.Contain("ambiguous"));
            Assert.That(ParsedArgs.Parse(new[] { "--from=-3" }).Value("from"), Is.EqualTo("-3"));
        }
    }
}
