#nullable enable
using System.Linq;
using LabEscape.Sim;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class ReportTests
    {
        private static FightRow Row(long seed, DamageTally playerDamage, double friendlyFireShare, FightOutcome outcome = FightOutcome.Win, string? error = null)
        {
            return new FightRow { Seed = seed, Bot = "greedy", Preset = "hybrid", Outcome = outcome, Rounds = 10, PlayerDamage = playerDamage, FriendlyFireShare = friendlyFireShare, HazardFires = 2, PeakMinions = 1, WavesSpawned = 3, Error = error };
        }

        private static readonly DamageTally Zero = new DamageTally();
        private static FightRow Ok(long seed, FightOutcome outcome) => Row(seed, Zero, 0, outcome);
        private static FightRow Failed(long seed, string error) => Row(seed, Zero, 0, FightOutcome.Error, error);

        [Test]
        public void UsesTheSameFriendlyFireFigureForTheHeadlineAndTheAlarmCheck()
        {
            // One fight where 90% of a small amount of damage was friendly, one with a lot of enemy damage and none.
            // The per-fight mean is 45%; pooling the damage first would give 8.2%.
            var rows = new[] { Row(1, new DamageTally { Player = 9, Enemy = 1 }, 0.9), Row(2, new DamageTally { Enemy = 100 }, 0) };
            var stats = Report.Aggregate(rows, C);
            var check = stats.Checks.First(k => k.Metric == "Share of player damage from own effects");
            Assert.That(stats.MeanFriendlyFireShare, Is.EqualTo(0.45).Within(0.005));
            Assert.That(check.Value, Is.EqualTo("45.0%"));
            Assert.That(check.Status, Is.EqualTo("alarm"));
            Assert.That(Report.FormatReport("t", stats), Does.Contain("Mean friendly-fire share of the player's damage: 45.0%"));
        }

        [Test]
        public void TuneShowsOneLineOfFiguresForAValueWhoseFightsAllFinished()
        {
            var lines = Report.FormatTuneRow("16", Report.Aggregate(new[] { Ok(1, FightOutcome.Win), Ok(2, FightOutcome.Lose) }, C));
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0], Does.Match(@"^16\s+50\.0% \(\d+ to \d+\)"));
        }

        [Test]
        public void TuneFlagsAValueWhereOnlySomeFightsErrored()
        {
            var lines = Report.FormatTuneRow("0", Report.Aggregate(new[] { Ok(1, FightOutcome.Win), Ok(2, FightOutcome.Win), Failed(3, "Unknown unit \"x\"") }, C), "Unknown unit \"x\"");
            Assert.That(lines[0], Does.Match(@"^0\s+100\.0%"));
            Assert.That(lines[0], Does.Contain("1 of 3 fights errored"));
            Assert.That(string.Join("\n", lines), Does.Contain("Unknown unit \"x\""));
            Assert.That(string.Join("\n", lines), Does.Contain("the figures cover the 2 that finished"));
        }

        [Test]
        public void TuneShowsNoFiguresAtAllWhenEveryFightErrored()
        {
            var lines = Report.FormatTuneRow("-1", Report.Aggregate(new[] { Failed(1, "boom"), Failed(2, "boom") }, C), "boom");
            Assert.That(lines[0], Does.Match(@"^-1\s+all 2 fights errored"));
            Assert.That(lines[0], Does.Not.Contain("%"));
            Assert.That(string.Join("\n", lines), Does.Contain("boom"));
        }

        [Test]
        public void TheCsvQuotesWhatNeedsQuotingAndLeavesAbsentValuesEmpty()
        {
            var row = Failed(5, "bad \"thing\", with\na newline");
            var lines = Report.RowsToCsv(new[] { row }).Split('\n');
            Assert.That(lines[0], Does.StartWith("seed,bot,preset,abilities"));
            Assert.That(lines[1], Does.StartWith("5,greedy,hybrid,,,error,10,,,,0,0,0,0,0.000,"));
            Assert.That(lines[1], Does.EndWith(",\"bad \"\"thing\"\", with"));
            Assert.That(lines[2], Is.EqualTo("a newline\""));
        }
    }
}
