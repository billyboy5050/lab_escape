#nullable enable
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The debug overlay's edits: what the controls hold, and that each edit is a command the engine runs and the replay keeps.</summary>
    public class DebugEditsTests
    {
        private static readonly Content C = GameContent.Load();

        private static FightFlow Settled(string preset = "tech", FightSettings? settings = null)
        {
            var p = C.Presets.First(x => x.Id == preset);
            var flow = new FightFlow(C, new Loadout(p.Abilities, p.Upgrades), settings ?? new FightSettings());
            flow.Begin();
            flow.Skip();
            return flow;
        }

        [Test]
        public void TheFirstUnitPickedIsThePlayerAtItsOwnHp()
        {
            var flow = Settled();
            var edits = new DebugEdits();
            edits.Sync(C, flow.Live);
            Assert.That(edits.UnitId, Is.EqualTo(0));
            Assert.That(edits.Hp, Is.EqualTo(flow.Live.Units.First(u => u.Id == 0).Hp));
            Assert.That(DebugEdits.UnitLabel(C, flow.Live, 0), Is.EqualTo("#0 You"));
        }

        [Test]
        public void UnitsAreCycledBothWaysAndWrapRound()
        {
            var flow = Settled();
            var live = DebugEdits.Units(flow.Live);
            Assert.That(live.Count, Is.GreaterThan(1));
            var edits = new DebugEdits();
            edits.Sync(C, flow.Live);
            edits.CycleUnit(flow.Live, 1);
            Assert.That(edits.UnitId, Is.EqualTo(live[1].Id));
            Assert.That(edits.Hp, Is.EqualTo(live[1].Hp), "a unit picked brings its own HP");
            edits.CycleUnit(flow.Live, -1);
            edits.CycleUnit(flow.Live, -1);
            Assert.That(edits.UnitId, Is.EqualTo(live[live.Count - 1].Id), "back past the first wraps to the last");
            edits.CycleUnit(flow.Live, 1);
            Assert.That(edits.UnitId, Is.EqualTo(live[0].Id));
        }

        [Test]
        public void ADeadUnitCannotStayPicked()
        {
            var flow = Settled();
            var edits = new DebugEdits();
            edits.Sync(C, flow.Live);
            edits.CycleUnit(flow.Live, 1);
            var picked = edits.UnitId;
            var gone = flow.Live.Units.First(u => u.Id == picked);
            gone.Dead = true;
            edits.Sync(C, flow.Live);
            Assert.That(edits.UnitId, Is.Not.EqualTo(picked));
            Assert.That(DebugEdits.Units(flow.Live).Select(u => u.Id), Does.Contain(edits.UnitId));
            gone.Dead = false;
        }

        [Test]
        public void NewFightsStartWithNothingPickedAndNothingWaiting()
        {
            var flow = Settled();
            var edits = new DebugEdits();
            edits.Sync(C, flow.Live);
            edits.Armed = "guard";
            edits.Reset(C);
            Assert.That((edits.Ap, edits.Move), Is.EqualTo((C.Rules.Turn.Ap, C.Rules.Turn.Movement)));
            Assert.That(edits.UnitId, Is.EqualTo(-1));
            Assert.That(edits.Armed, Is.Null);
        }

        [Test]
        public void TheSteppersStayInTheRangesTheWebPanelHas()
        {
            var edits = new DebugEdits();
            for (var i = 0; i < 200; i++) edits.StepHp(-1);
            Assert.That(edits.Hp, Is.EqualTo(1));
            for (var i = 0; i < 200; i++) edits.StepHp(1);
            Assert.That(edits.Hp, Is.EqualTo(DebugEdits.MaxHp));
            for (var i = 0; i < 50; i++)
            {
                edits.StepAp(-1);
                edits.StepMove(-1);
            }
            Assert.That((edits.Ap, edits.Move), Is.EqualTo((0, 0)));
            for (var i = 0; i < 50; i++)
            {
                edits.StepAp(1);
                edits.StepMove(1);
            }
            Assert.That((edits.Ap, edits.Move), Is.EqualTo((DebugEdits.MaxAp, DebugEdits.MaxAp)));
        }

        [Test]
        public void TheKindsToSpawnAreEveryUnitButThePlayer()
        {
            var defs = DebugEdits.SpawnDefs(C);
            Assert.That(defs, Does.Not.Contain("player"));
            Assert.That(defs, Is.EquivalentTo(C.Units.Select(p => p.Key).Where(d => d != "player")));
            var edits = new DebugEdits();
            edits.Sync(C, Settled().Live);
            var first = edits.SpawnDef;
            edits.CycleSpawn(C, 1);
            Assert.That(edits.SpawnDef, Is.EqualTo(defs[(defs.IndexOf(first) + 1) % defs.Count]));
            edits.CycleSpawn(C, -1);
            Assert.That(edits.SpawnDef, Is.EqualTo(first));
            edits.CycleSpawn(C, -1);
            Assert.That(edits.SpawnDef, Is.Not.EqualTo(first));
            edits.Armed = edits.SpawnDef;
            edits.CycleSpawn(C, 1);
            Assert.That(edits.Armed, Is.EqualTo(edits.SpawnDef), "a spawn waiting for a tile follows the kind picked");
        }

        [Test]
        public void EachEditIsTheCommandTheCoreAlreadyKnows()
        {
            var edits = new DebugEdits();
            edits.Reset(C);
            edits.Sync(C, Settled().Live);
            edits.StepHp(2);
            Assert.That(edits.SetHp().ToJson(), Is.EqualTo($"{{\"type\":\"debug\",\"op\":\"setHp\",\"unit\":0,\"value\":{edits.Hp}}}"));
            edits.StepAp(2);
            edits.StepMove(-1);
            Assert.That(edits.SetApAndMove().ToJson(), Is.EqualTo($"{{\"type\":\"debug\",\"op\":\"setAp\",\"ap\":{C.Rules.Turn.Ap + 2},\"movement\":{C.Rules.Turn.Movement - 1}}}"));
            Assert.That(edits.Spawn("guard", new Pos(4, 3)).ToJson(), Is.EqualTo("{\"type\":\"debug\",\"op\":\"spawn\",\"def\":\"guard\",\"target\":\"E4\"}"));
            Assert.That(edits.ForceWave().ToJson(), Is.EqualTo("{\"type\":\"debug\",\"op\":\"forceWave\"}"));
        }

        [Test]
        public void SettingHpAndApReachesTheFight()
        {
            var flow = Settled();
            var edits = new DebugEdits();
            edits.Reset(C);
            edits.Sync(C, flow.Live);
            edits.StepHp(-100);
            Assert.That(flow.Apply(edits.SetHp()).Ok, Is.True);
            flow.Skip();
            Assert.That(flow.Live.Units.First(u => u.Id == 0).Hp, Is.EqualTo(1));
            edits.StepAp(-10);
            edits.StepMove(2);
            Assert.That(flow.Apply(edits.SetApAndMove()).Ok, Is.True);
            flow.Skip();
            Assert.That((flow.Live.Ap, flow.Live.Movement), Is.EqualTo((0, C.Rules.Turn.Movement + 2)));
            Assert.That(flow.Log.Select(l => l.Text), Does.Contain($"[debug] AP 0, movement {C.Rules.Turn.Movement + 2}"));
        }

        [Test]
        public void ASpawnedUnitStandsOnTheTileClickedOrTheNearestFreeOne()
        {
            var flow = Settled();
            var edits = new DebugEdits();
            edits.Sync(C, flow.Live);
            var before = flow.Live.Units.Count;
            var board = new Board(C.DefaultRoom);
            var free = Enumerable.Range(0, board.Width).SelectMany(x => Enumerable.Range(0, board.Height).Select(y => new Pos(x, y)))
                .First(p => !board.BlocksMove(p) && flow.Live.Units.All(u => u.Dead || u.Pos != p) && flow.Live.Objects.All(o => o.Destroyed || o.Pos != p));
            Assert.That(flow.Apply(edits.Spawn("guard", free)).Ok, Is.True);
            flow.Skip();
            Assert.That(flow.Live.Units.Count, Is.EqualTo(before + 1));
            Assert.That(flow.Live.Units.Last().Def, Is.EqualTo("guard"));
            Assert.That(flow.Live.Units.Last().Pos, Is.EqualTo(free));
            // The same tile again is taken, so the unit goes to the nearest free tile.
            Assert.That(flow.Apply(edits.Spawn("guard", free)).Ok, Is.True);
            flow.Skip();
            Assert.That(flow.Live.Units.Count, Is.EqualTo(before + 2));
            Assert.That(flow.Live.Units.Last().Pos, Is.Not.EqualTo(free));
        }

        [Test]
        public void ForcingWavesBringsThemUntilNoneAreLeft()
        {
            var flow = Settled();
            var edits = new DebugEdits();
            var waves = C.DefaultRoom.Waves.Count;
            Assert.That(flow.Live.WavesSpawned, Is.EqualTo(1));
            while (DebugEdits.CanForceWave(C, flow.Live))
            {
                Assert.That(flow.Apply(edits.ForceWave()).Ok, Is.True);
                flow.Skip();
            }
            Assert.That(flow.Live.WavesSpawned, Is.EqualTo(waves));
            Assert.That(flow.Apply(edits.ForceWave()).Ok, Is.False, "the engine refuses when there is none");
        }

        [Test]
        public void HoldingWave3BackLeavesTwoWavesInPlayAndNoThirdToForce()
        {
            var flow = Settled(settings: new FightSettings { MaxWaves = 2 });
            Assert.That(flow.Live.Settings.MaxWaves, Is.EqualTo(2));
            var edits = new DebugEdits();
            Assert.That(flow.Apply(edits.ForceWave()).Ok, Is.True);
            flow.Skip();
            Assert.That(flow.Live.WavesSpawned, Is.EqualTo(2));
        }

        [Test]
        public void AnEditedFightIsInItsRecordAndReplaysToTheSameEvents()
        {
            var flow = Settled();
            var edits = new DebugEdits();
            edits.Sync(C, flow.Live);
            edits.StepHp(-100);
            flow.Apply(edits.SetHp());
            flow.Skip();
            flow.Apply(edits.ForceWave());
            flow.Skip();
            flow.Apply(edits.Spawn("guard", new Pos(6, 6)));
            flow.Skip();
            flow.Apply(new EndTurnCommand());
            flow.Skip();

            var record = flow.Session.Record;
            Assert.That(record.Commands.Select(c => c.Type), Is.EqualTo(new[] { "debug", "debug", "debug", "endTurn" }));
            // The saved file loads again: a fight with edits is a valid replay.
            var loaded = Telemetry.LoadReplay(C, record.ToToken().ToString());
            var again = Records.Replay(C, loaded);
            Assert.That(Records.EventsToJsonl(again.Events), Is.EqualTo(Records.EventsToJsonl(flow.Session.Events)));
        }
    }
}
