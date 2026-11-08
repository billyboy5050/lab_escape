#nullable enable
using System.Collections.Generic;
using LabEscape.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>Ported from tests/telemetry.test.ts.</summary>
    public class TelemetryTests
    {
        private static Loadout Kit() => LoadoutOf("sidearm", "proximity_mine");

        private sealed class Clock
        {
            public double T;
        }

        /// <summary>A session on a hand-built state, with a clock the test moves.</summary>
        private static (FightSession F, Clock Clock) SessionOn(GameState state)
        {
            var clock = new Clock();
            var f = new FightSession(C, Kit(), null, null, () => clock.T);
            f.State = state;
            return (f, clock);
        }

        private static List<double> Times(FightSession f) => f.Record.Meta!.TurnTimesMs!;
        private static GameState At(string player, int? hp = null) => Scenario(new ScenarioSpec { Player = player, PlayerHp = hp });
        private static GameState WithGuard(string at, int? hp = null, int? waves = null, int? playerHp = null) => Scenario(new ScenarioSpec { Player = "B8", PlayerHp = playerHp, WavesSpawned = waves, Units = { U("guard", at, hp: hp) } });

        [Test]
        public void TurnTimesStartWhenTheClientSaysThePlayerCanAct()
        {
            var (f, clock) = SessionOn(At("B8"));
            clock.T = 5000; // The opening plays.
            f.TurnReady();
            clock.T = 15000;
            f.Apply(EndTurn);
            clock.T = 25000; // The minion, enemy and environment phases play out.
            f.TurnReady();
            clock.T = 30000;
            f.Apply(EndTurn);
            Assert.That(Times(f), Is.EqualTo(new[] { 10000.0, 5000.0 }));
        }

        [Test]
        public void TurnTimesIgnoreARepeatedTurnReadyInTheSameTurn()
        {
            var (f, clock) = SessionOn(At("B8"));
            f.TurnReady();
            clock.T = 4000;
            f.Apply(Move("B7")); // A free move; the animation ends and the client calls TurnReady again.
            f.TurnReady();
            clock.T = 9000;
            f.Apply(EndTurn);
            Assert.That(Times(f), Is.EqualTo(new[] { 9000.0 }));
        }

        [Test]
        public void TurnTimesAreTimedFromTheEndOfThePreviousTurnWhenNothingCallsTurnReady()
        {
            var (f, clock) = SessionOn(At("B8"));
            clock.T = 10;
            f.Apply(EndTurn);
            clock.T = 25;
            f.Apply(EndTurn);
            Assert.That(Times(f), Is.EqualTo(new[] { 10.0, 15.0 }));
        }

        [Test]
        public void TurnTimesAreRoundedAsMathRoundDoes()
        {
            var (f, clock) = SessionOn(At("B8"));
            clock.T = 2.5;
            f.Apply(EndTurn);
            Assert.That(Times(f), Is.EqualTo(new[] { 3.0 }));
        }

        [Test]
        public void TurnTimesIncludeTheLastTurnWhenAnActionEndsTheFight()
        {
            var (f, clock) = SessionOn(WithGuard("B6", hp: 2, waves: 3));
            f.TurnReady();
            clock.T = 4000;
            f.Apply(Sidearm("B6"));
            Assert.That(f.State.Outcome?.Result, Is.EqualTo(FightResult.Win));
            Assert.That(Times(f), Is.EqualTo(new[] { 4000.0 }));
        }

        [Test]
        public void TurnTimesRecordATurnOnceWhenEndTurnItselfEndsTheFight()
        {
            var (f, clock) = SessionOn(WithGuard("B6", playerHp: 2));
            f.TurnReady();
            clock.T = 7000;
            f.Apply(EndTurn);
            Assert.That(f.State.Outcome?.Result, Is.EqualTo(FightResult.Lose));
            Assert.That(Times(f), Is.EqualTo(new[] { 7000.0 }));
        }

        [Test]
        public void TurnTimesDoNotRecordATurnForAnActionThatLeavesTheFightGoing()
        {
            var (f, clock) = SessionOn(WithGuard("B6"));
            f.TurnReady();
            clock.T = 3000;
            f.Apply(Sidearm("B6"));
            Assert.That(f.State.Outcome, Is.Null);
            Assert.That(Times(f), Is.Empty);
        }

        private static RecordMeta Original() => new RecordMeta { Created = "2026-10-04T00:00:00.000Z", Label = "client", Preset = "tech", Seed = 9, TurnTimesMs = new List<double> { 4000, 6500 } };

        [Test]
        public void TurnTimesAreLeftAsRecordedWhenTheSessionIsToldItIsReplaying()
        {
            var clock = new Clock();
            var f = new FightSession(C, Kit(), null, Original(), () => clock.T, timed: false);
            f.State = At("B8");
            clock.T = 120; // The autoplay's own delays.
            f.Apply(EndTurn);
            clock.T = 5000;
            f.Apply(EndTurn);
            Assert.That(JToken.DeepEquals(f.Record.Meta!.ToToken(), Original().ToToken()), Is.True);
        }

        [Test]
        public void AReplaySessionStillCarriesTheRestOfTheOriginalMetadata()
        {
            var f = new FightSession(C, Kit(), null, Original(), () => 0, timed: false);
            Assert.That(f.Record.Meta!.Created, Is.EqualTo("2026-10-04T00:00:00.000Z"));
            Assert.That(f.Record.Meta.Label, Is.EqualTo("client"));
            Assert.That(f.Record.Meta.Preset, Is.EqualTo("tech"));
            Assert.That(f.Record.Meta.Seed, Is.EqualTo(9));
        }

        [Test]
        public void TurnTimesAreMeasuredAsUsualForALiveFightTheDefault()
        {
            var clock = new Clock();
            var f = new FightSession(C, Kit(), null, null, () => clock.T);
            f.State = At("B8");
            clock.T = 7000;
            f.Apply(EndTurn);
            Assert.That(Times(f), Is.EqualTo(new[] { 7000.0 }));
        }

        [Test]
        public void ASessionRecordsWhenItStartedAndPutsThatBeforeTheCallersMetadata()
        {
            var f = new FightSession(C, Kit(), null, new RecordMeta { Label = "client" }, () => 1793966400123);
            Assert.That(f.Record.Meta!.ToToken().ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"created\":\"2026-11-06T12:00:00.123Z\",\"turnTimesMs\":[],\"label\":\"client\"}"));
        }

        private static GameState GuardAtB5() => Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5") } });

        private static void Play(FightSession f, Command cmd)
        {
            Assert.That(f.Apply(cmd).Ok, Is.True, $"{cmd.ToJson()} was rejected");
        }

        [Test]
        public void UndoWorksForFreeMovement()
        {
            var (f, _) = SessionOn(GuardAtB5());
            Play(f, Move("B7"));
            Assert.That(f.CanUndo, Is.True);
            Assert.That(f.Undo(), Is.True);
            Assert.That(f.State.Units[0].Pos, Is.EqualTo(new Pos(1, 7)));
            Assert.That(f.Record.Commands, Is.Empty);
        }

        [Test]
        public void UndoIsLostOnceApIsSpent()
        {
            var (f, _) = SessionOn(GuardAtB5());
            Play(f, Move("B7"));
            Play(f, Sidearm("B5"));
            Assert.That(f.CanUndo, Is.False);
        }

        [Test]
        public void UndoIsNotOfferedForAMoveMadeAfterApWasSpentThoughThatMoveTriggersNothing()
        {
            var (f, _) = SessionOn(GuardAtB5());
            Play(f, Sidearm("B5"));
            Assert.That(f.State.Committed, Is.True);
            Play(f, Move("B7"));
            Assert.That(f.CanUndo, Is.False);
            Assert.That(f.Undo(), Is.False);
            Assert.That(f.State.Units[0].Pos, Is.EqualTo(new Pos(1, 6)));
        }

        [Test]
        public void UndoComesBackOnTheNextTurn()
        {
            var (f, _) = SessionOn(GuardAtB5());
            Play(f, Sidearm("B5"));
            Play(f, EndTurn);
            Assert.That(f.State.Committed, Is.False);
            Play(f, Move("B7"));
            Assert.That(f.CanUndo, Is.True);
        }

        [Test]
        public void SummaryCountsPickUpMineLikeTheOtherUtilityActions()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Objects = { Mine("B7") } });
            var r = Run(s, new PickUpMineCommand("B7"));
            var used = Summaries.Summarize(r.Events, r.State).AbilitiesUsed;
            Assert.That(used.Count, Is.EqualTo(1));
            Assert.That(used["pick_up_mine"], Is.EqualTo(1));
        }

        [Test]
        public void SummaryDoesNotCountAMineThatWasSetOffOrAnyOtherRemoval()
        {
            var s = Scenario(new ScenarioSpec { Player = "A7", Objects = { Mine("B7") } });
            var r = Run(s, Move("B7"));
            Assert.That(Summaries.Summarize(r.Events, r.State).AbilitiesUsed.ContainsKey("pick_up_mine"), Is.False);
        }

        // Ported from tests/tester-labels.test.ts. The rest of that file tests the web client.

        [Test]
        public void CleanTesterKeepsAPlainIdAndTrimsIt() => Assert.That(Labels.CleanTester(" P3 "), Is.EqualTo("P3"));

        [Test]
        public void CleanTesterReplacesAnythingThatIsNotAWordCharacterOrDash() => Assert.That(Labels.CleanTester("P3/../x y"), Is.EqualTo("P3____x_y"));

        [Test]
        public void CleanTesterCutsLongIds() => Assert.That(Labels.CleanTester(new string('x', 50)), Has.Length.EqualTo(Labels.MaxTesterLength));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void CleanTesterTreatsThisAsNoTester(string? raw) => Assert.That(Labels.CleanTester(raw), Is.Null);

        private const double T0 = 1791288000123; // 2026-10-06T12:00:00.123Z

        [Test]
        public void FolderNameIsPlainWithoutATester() => Assert.That(Labels.TelemetryFolderName(T0, new RecordMeta { Preset = "hybrid" }, "win"), Is.EqualTo("2026-10-06T12-00-00-123Z-hybrid-win"));

        [Test]
        public void FolderNameAddsTheTesterAndAttemptAfterTheTime() => Assert.That(Labels.TelemetryFolderName(T0, new RecordMeta { Preset = "hybrid", Tester = "P3", Attempt = 2 }, "lose"), Is.EqualTo("2026-10-06T12-00-00-123Z-P3-a2-hybrid-lose"));

        [Test]
        public void FolderNameFallsBackToCustomWithoutAPreset() => Assert.That(Labels.TelemetryFolderName(T0, new RecordMeta { Tester = "P3" }, "win"), Is.EqualTo("2026-10-06T12-00-00-123Z-P3-custom-win"));

        [Test]
        public void FolderNameStaysInsideTheFolderEvenForAHostileTester() => Assert.That(Labels.TelemetryFolderName(T0, new RecordMeta { Tester = "../../etc" }, "win"), Does.Not.Contain("/").And.Not.Contain("\\"));

        private static JObject WithMeta(string meta)
        {
            var r = Records.NewRecord(C, LoadoutOf("sidearm")).ToToken();
            r["meta"] = JObject.Parse(meta);
            return r;
        }

        [Test]
        public void ReplayMetaAcceptsATesterAndAnAttempt() => Assert.That(Records.RecordProblems(WithMeta("{tester:'P3',attempt:1}")), Is.Empty);

        [TestCase("{tester:3}")]
        [TestCase("{attempt:0}")]
        [TestCase("{attempt:1.5}")]
        [TestCase("{attempt:'2'}")]
        public void ReplayMetaRejects(string meta) => Assert.That(Records.RecordProblems(WithMeta(meta)), Is.Not.Empty);
    }
}
