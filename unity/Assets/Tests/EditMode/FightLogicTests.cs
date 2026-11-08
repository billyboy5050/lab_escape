#nullable enable
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The client's own logic: the animation queue, and which command a click issues.</summary>
    public class FightLogicTests
    {
        private static readonly Content C = GameContent.Load();

        private static Loadout Preset(string id)
        {
            var p = C.Presets.First(x => x.Id == id);
            return new Loadout(p.Abilities, p.Upgrades);
        }

        private static FightFlow Settled(string preset)
        {
            var flow = new FightFlow(C, Preset(preset), new FightSettings());
            flow.Begin();
            flow.Skip();
            return flow;
        }

        private static ActionModel ModelFor(FightFlow flow)
        {
            var m = new ActionModel(C);
            m.Refresh(flow.Live, flow.Idle);
            return m;
        }

        [Test]
        public void TheOpeningPlaysFrameByFrameAndEndsOnTheLiveState()
        {
            var flow = new FightFlow(C, Preset("hybrid"), new FightSettings());
            var shown = 0;
            flow.FrameShown += (s, e) => shown++;
            flow.Begin();
            Assert.That(flow.Busy, Is.True);
            Assert.That(flow.Idle, Is.False);
            for (var i = 0; i < 500 && flow.Busy; i++) flow.Tick(0.05f);
            Assert.That(flow.Busy, Is.False);
            Assert.That(shown, Is.GreaterThan(0));
            Assert.That(flow.Shown, Is.SameAs(flow.Live));
            Assert.That(flow.Idle, Is.True);
        }

        [Test]
        public void SkipShowsEverythingAndKeepsTheLog()
        {
            var flow = new FightFlow(C, Preset("hybrid"), new FightSettings());
            flow.Begin();
            flow.Skip();
            Assert.That(flow.Busy, Is.False);
            Assert.That(flow.Shown, Is.SameAs(flow.Live));
            Assert.That(flow.Log.Select(l => l.Text), Does.Contain("Round 1"));
        }

        [Test]
        public void SkipSpeedNeverQueues()
        {
            var flow = Settled("hybrid");
            flow.SetSpeed(FightSpeed.Skip);
            var r = flow.Apply(new EndTurnCommand());
            Assert.That(r.Ok, Is.True);
            Assert.That(flow.Busy, Is.False);
            Assert.That(flow.Shown, Is.SameAs(flow.Live));
        }

        [Test]
        public void UndoIsRefusedWhileAnimatingAndRestoresAMove()
        {
            var flow = Settled("hybrid");
            var before = flow.Live;
            var move = ModelFor(flow).Moves.First();
            Assert.That(flow.Apply(Actions.MoveCommandFor(move.Path)).Ok, Is.True);
            Assert.That(flow.Busy, Is.True);
            Assert.That(flow.Undo(), Is.False);
            flow.Skip();
            Assert.That(flow.Undo(), Is.True);
            Assert.That(flow.Live, Is.SameAs(before));
            Assert.That(flow.Shown, Is.SameAs(before));
        }

        [Test]
        public void LogLinesKeepRisingAfterAnUndo()
        {
            var flow = Settled("hybrid");
            flow.Apply(Actions.MoveCommandFor(ModelFor(flow).Moves.First().Path));
            flow.Skip();
            Assert.That(flow.Undo(), Is.True);
            var undoLine = flow.Log.Last().Id;
            flow.Apply(new EndTurnCommand());
            flow.Skip();
            Assert.That(flow.Log.Last().Id, Is.GreaterThan(undoLine), "the log panel only appends lines newer than the last it showed");
        }

        [Test]
        public void ClickingAHighlightedTileMoves()
        {
            var flow = Settled("hybrid");
            var m = ModelFor(flow);
            var option = m.Moves.First();
            Assert.That(m.Click(option.Tile), Is.InstanceOf<MoveCommand>());
            Assert.That(m.Click(new Pos(7, 0)), Is.Null, "H1 is not in reach of the start tile");
        }

        [Test]
        public void SelectingAnActionTwiceClearsIt()
        {
            var flow = Settled("hybrid");
            var m = ModelFor(flow);
            var a = m.Statuses.First(x => x.Usable && x.Id != "sprint");
            m.Select(a);
            Assert.That(m.ModeId, Is.EqualTo(a.Id));
            m.Select(a);
            Assert.That(m.ModeId, Is.Null);
        }

        [Test]
        public void BarrierShieldTakesTwoTilesInEitherOrder()
        {
            var flow = Settled("tech");
            var m = ModelFor(flow);
            var a = m.Statuses.First(x => x.Id == "barrier_shield");
            m.Select(a);
            Assert.That(m.TwoStep, Is.True);
            var pair = a.Options.First(o => o.Target2.HasValue);
            Assert.That(m.Click(pair.Target2!.Value), Is.Null, "the first tile only selects");
            Assert.That(m.First, Is.EqualTo(pair.Target2));
            Assert.That(m.SecondTiles, Does.Contain(pair.Target));
            var cmd = m.Click(pair.Target);
            Assert.That(cmd, Is.InstanceOf<AbilityCommand>());
            Assert.That(flow.Apply(cmd!).Ok, Is.True);
        }

        [Test]
        public void ClickingOffTheSecondStepGoesBackToTheFirst()
        {
            var flow = Settled("tech");
            var m = ModelFor(flow);
            var a = m.Statuses.First(x => x.Id == "barrier_shield");
            m.Select(a);
            var pair = a.Options.First(o => o.Target2.HasValue);
            m.Click(pair.Target);
            var off = new Pos(-1, -1);
            Assert.That(m.Click(off), Is.Null);
            Assert.That(m.First, Is.Null);
            Assert.That(m.ModeId, Is.EqualTo("barrier_shield"));
        }

        [Test]
        public void AnActionThatIsNoLongerUsableIsDropped()
        {
            var flow = Settled("hybrid");
            var m = ModelFor(flow);
            m.Select(m.Statuses.First(x => x.Usable && x.Id != "sprint"));
            for (var i = 0; i < C.Rules.Turn.Ap; i++) Assert.That(flow.Apply(new SprintCommand()).Ok, Is.True);
            m.Refresh(flow.Live, flow.Idle);
            Assert.That(m.ModeId, Is.Null, "with no AP left the selected action cannot be used");
        }

        [Test]
        public void EveryCandidateIsAcceptedByTheEngine()
        {
            var flow = Settled("hybrid");
            var m = ModelFor(flow);
            foreach (var tile in m.Moves.Select(x => x.Tile))
            {
                var cmd = m.CandidateFor(tile);
                Assert.That(cmd, Is.Not.Null);
                Assert.That(Previews.PreviewCommand(C, flow.Live, cmd!).Ok, Is.True, tile.ToString());
            }
        }

        [Test]
        public void TheLogSpeaksToThePlayerInTheSecondPerson()
        {
            var f = new EventFormatter(C);
            var line = f.Format(new UnitMoved { Id = 0, From = new Pos(1, 7), To = new Pos(1, 5), Mode = MoveMode.Walk }, null);
            Assert.That(line!.Text, Is.EqualTo("You move B8 \u2192 B6"));
        }

        [Test]
        public void TheFormatterNamesUnitsAfterTheyDie()
        {
            var f = new EventFormatter(C);
            f.Format(new UnitSpawned { Unit = new Unit { Id = 4, Def = "guard", Pos = new Pos(2, 3) }, Cause = "wave1" }, null);
            var died = f.Format(new UnitDied { Id = 4, Def = "guard", Team = Team.Enemy, Pos = new Pos(2, 3) }, null);
            Assert.That(died!.Text, Is.EqualTo($"{C.Units["guard"].Name} 4 dies at C4"));
        }
    }
}
