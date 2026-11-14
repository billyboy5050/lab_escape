#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Sim;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class DeterminismTests
    {
        /// <summary>States drawn by playing a random bot for a random number of commands, the same way the TypeScript test does.</summary>
        private static List<GameState> RandomStates(int count, uint seed)
        {
            var rng = new Rng(seed);
            var output = new List<GameState>();
            var presets = C.Presets.Select(p => p.Id).ToList();
            while (output.Count < count)
            {
                var s = Engine.NewGame(C, PresetLoadout(presets[rng.Int(presets.Count)])).State;
                var bot = new RandomBot(rng.NextU32());
                var steps = rng.Int(40);
                for (var i = 0; i < steps && s.Outcome == null; i++) s = Engine.Step(C, s, bot.Decide(C, s)).State;
                if (s.Outcome == null) output.Add(s);
            }
            return output;
        }

        private static readonly List<GameState> States = RandomStates(100, 7);

        [Test]
        public void TheDryRunPreviewMatchesTheRealResultFor100RandomStatesAndCommands()
        {
            var rng = new Rng(99);
            foreach (var s in States)
            {
                var before = s.ToJson();
                var cmds = Actions.LegalCommands(C, s);
                var cmd = cmds[rng.Int(cmds.Count)];
                var preview = Previews.PreviewCommand(C, s, cmd);
                var real = Engine.Step(C, s, cmd);
                Assert.That(preview.Ok, Is.True);
                Assert.That(preview.Events.Select(e => e.ToJson()), Is.EqualTo(real.Events.Select(e => e.ToJson())));
                Assert.That(preview.State!.ToJson(), Is.EqualTo(real.State.ToJson()));
                // Neither the preview nor the step touched the input state.
                Assert.That(s.ToJson(), Is.EqualTo(before));
            }
        }

        [Test]
        public void IntentsShowExactlyWhatTheMinionAndEnemyPhasesDoWhenTheTurnEnds()
        {
            foreach (var s in States.Take(50))
            {
                var rep = Previews.ComputeIntents(C, s);
                var real = Engine.Step(C, s, EndTurn);
                var envAt = real.Events.FindIndex(e => e is PhaseStarted p && p.Phase == Phase.Environment);
                var actorEvents = (envAt >= 0 ? real.Events.Take(envAt) : real.Events).Where(e => !(e is FightEnded)).Select(e => e.ToJson());
                var simEvents = rep.Frames.SelectMany(f => f.Events).Where(e => !(e is FightEnded)).Select(e => e.ToJson());
                Assert.That(simEvents, Is.EqualTo(actorEvents));
            }
        }

        [Test]
        public void ReplayingACommandLogReproducesTheFightExactly()
        {
            var ids = new[] { "tech", "alien", "hybrid" };
            for (var i = 0; i < ids.Length; i++)
            {
                var r = Runner.RunFight(C, PresetLoadout(ids[i]), new GreedyBot(100 + i));
                Assert.That(Records.Replay(C, r.Record).State.ToJson(), Is.EqualTo(r.State.ToJson()));
            }
        }

        [Test]
        public void AReplayIsOnlyValidAgainstTheSameEngineVersionAndContentHash()
        {
            var r = Runner.RunFight(C, PresetLoadout("tech"), new RandomBot(3));
            var tuned = ContentWith(("units.warden.hp", 16));
            Assert.That(tuned.Hash, Is.Not.EqualTo(C.Hash));
            Assert.Throws<ReplayMismatch>(() => Records.CheckCompatible(tuned, r.Record));
            Assert.Throws<ReplayMismatch>(() => Records.Replay(tuned, r.Record));
        }

        [Test]
        public void TheSameBotAndSeedAlwaysPlaysTheSameFight()
        {
            var a = Runner.RunFight(C, PresetLoadout("hybrid"), new GreedyBot(5));
            var b = Runner.RunFight(C, PresetLoadout("hybrid"), new GreedyBot(5));
            Assert.That(a.Record.Commands.Select(x => x.ToJson()), Is.EqualTo(b.Record.Commands.Select(x => x.ToJson())));
            Assert.That(a.Summary.ToToken().ToString(), Is.EqualTo(b.Summary.ToToken().ToString()));
        }

        [Test]
        public void FreeMovesCanBeUndoneSeveralLevelsDeepUntilApIsSpent()
        {
            var f = new FightSession(C, PresetLoadout("tech"), now: () => 0);
            var start = f.State;
            Assert.That(f.Apply(Move("B7")).Ok, Is.True);
            Assert.That(f.Apply(Move("C7")).Ok, Is.True);
            Assert.That(f.CanUndo, Is.True);
            f.Undo();
            f.Undo();
            Assert.That(f.State.ToJson(), Is.EqualTo(start.ToJson()));
            Assert.That(f.Record.Commands, Is.Empty);
            f.Apply(Move("B7"));
            f.Apply(new SprintCommand());
            Assert.That(f.CanUndo, Is.False);
        }

        [Test]
        public void AMoveThatSetsSomethingOffCommitsTheTurn()
        {
            var f = new FightSession(C, PresetLoadout("tech"), now: () => 0);
            f.Apply(Ability("proximity_mine", "B6"));
            Assert.That(f.CanUndo, Is.False);
            var r = f.Apply(Move("B7", "B6"));
            Assert.That(r.Commits, Is.True);
            Assert.That(f.CanUndo, Is.False);
        }

        [Test]
        public void TheCommandLogOfASessionReplaysToTheSameState()
        {
            var f = new FightSession(C, PresetLoadout("alien"), now: () => 0);
            var bot = new GreedyBot(11);
            for (var i = 0; i < 30 && !f.Over; i++) f.Apply(bot.Decide(C, f.State));
            Assert.That(Records.Replay(C, f.Record).State.ToJson(), Is.EqualTo(f.State.ToJson()));
        }

        [Test]
        public void CloningLeavesNoSharedMutableStateBehind()
        {
            var original = States.First(s => s.Units.Count > 1);
            var before = original.ToJson();
            var copy = original.Clone();
            foreach (var u in copy.Units)
            {
                u.Hp = 1;
                u.Pos = new Pos(0, 0);
                u.Statuses.Poison = new TimedStatus(9, Src());
                u.Cooldowns["test"] = 7;
            }
            foreach (var h in copy.Hazards) h.Tiles.Clear();
            copy.Objects.Clear();
            copy.Intercom.Queue.Add("x");
            copy.Intercom.Played.Add("x");
            copy.Loadout.Abilities.Add("x");
            copy.Settings.MaxWaves = 1;
            Assert.That(original.ToJson(), Is.EqualTo(before));
        }
    }
}
