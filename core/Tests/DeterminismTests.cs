#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>
    /// Ported from tests/determinism.test.ts. The tests that need the greedy bot, command records or the fight session come
    /// across with the simulator and telemetry in M10.
    /// </summary>
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
            foreach (var id in new[] { "tech", "alien", "hybrid" })
            {
                var bot = new RandomBot(100);
                var game = Engine.NewGame(C, PresetLoadout(id));
                var state = game.State;
                var log = new List<Command>();
                while (state.Outcome == null)
                {
                    var cmd = bot.Decide(C, state);
                    log.Add(cmd);
                    state = Run(state, cmd).State;
                }
                var replay = Engine.NewGame(C, PresetLoadout(id)).State;
                foreach (var cmd in log) replay = Run(replay, Command.Parse(JsonIn.Parse(cmd.ToJson()))).State;
                Assert.That(replay.ToJson(), Is.EqualTo(state.ToJson()));
            }
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
