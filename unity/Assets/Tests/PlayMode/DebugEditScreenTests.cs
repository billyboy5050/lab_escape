#nullable enable
using System.Collections;
using System.IO;
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LabEscape.Client.Tests
{
    /// <summary>The debug overlay's edit controls on the real fight screen: each button, what it does to the fight, and when it is off.</summary>
    public class DebugEditScreenTests
    {
        private string _telemetry = "";

        [SetUp]
        public void SendTelemetryAside()
        {
            _telemetry = Path.Combine(Path.GetTempPath(), "lab-escape-telemetry-" + System.Guid.NewGuid().ToString("N"));
            TelemetrySink.Isolate(_telemetry);
            Motion.Reduced = false;
        }

        /// <summary>The director outlives a test: the overlay is closed, and the start screen is put back.</summary>
        [TearDown]
        public void PutTheGameBack()
        {
            var d = FightDirector.Instance;
            if (d != null)
            {
                d.SetDebugOpen(false);
                d.StepMode = false;
                d.ShowStartScreen();
            }
            TelemetrySink.Reset();
            if (Directory.Exists(_telemetry)) Directory.Delete(_telemetry, true);
        }

        private static VisualElement Root => Object.FindAnyObjectByType<UIDocument>().rootVisualElement;

        private static IEnumerator WaitForDirector()
        {
            while (FightDirector.Instance == null || FightDirector.Instance.Content == null) yield return null;
        }

        private static IEnumerator WaitUntilIdle(FightDirector d)
        {
            while (!d.Idle && d.Flow!.Live.Outcome == null) yield return null;
            while (d.Flow!.Busy) yield return null;
        }

        /// <summary>A fight on the Tech preset with the overlay open, waiting for the player.</summary>
        private static IEnumerator OpenFight()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            var p = d.Content!.Presets.First(x => x.Id == "tech");
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), "tech");
            d.SetDebugOpen(true);
            yield return WaitUntilIdle(d);
            yield return null;
            yield return null;
        }

        private static Button Btn(string name) => Root.Q<Button>(name);

        /// <summary>A press of a button, as the keyboard or a click would make it.</summary>
        private static void Press(string name)
        {
            var b = Btn(name);
            Assert.That(b.enabledSelf, Is.True, $"{name} can be pressed");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        private static string Text(string name) => Root.Q<Label>(name).text;

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheEditControlsFitInTheColumnAndLeaveRoomForTheReport()
        {
            yield return OpenFight();
            var column = Root.Q("debug").worldBound;
            Assert.That(column.width, Is.GreaterThan(100f));
            foreach (var name in new[]
            {
                "edit-unit-prev", "edit-unit", "edit-unit-next", "edit-hp-down", "edit-hp", "edit-hp-up", "edit-set-hp", "edit-ap-down", "edit-ap", "edit-ap-up",
                "edit-move-down", "edit-move", "edit-move-up", "edit-set-ap", "edit-spawn-prev", "edit-spawn", "edit-spawn-next", "edit-spawn-go",
                "edit-force-wave", "edit-hold-wave", "edit-restart",
            })
            {
                var box = Root.Q(name).worldBound;
                Assert.That(box.width, Is.GreaterThan(0f), $"{name} is drawn");
                Assert.That(box.xMin, Is.GreaterThanOrEqualTo(column.xMin - 0.5f), $"{name} is not left of the column");
                Assert.That(box.xMax, Is.LessThanOrEqualTo(column.xMax + 0.5f), $"{name} is not right of the column");
                Assert.That(box.yMax, Is.LessThanOrEqualTo(column.yMax + 0.5f), $"{name} is not below the column");
            }
            Assert.That(Root.Q("debug-text").worldBound.height, Is.GreaterThan(80f), "the report still has room");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheControlsStartOnThePlayerAndItsHp()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            Assert.That(Text("edit-unit"), Is.EqualTo("#0 You"));
            Assert.That(Text("edit-hp"), Is.EqualTo(d.Flow!.Live.Units.First(u => u.Id == 0).Hp.ToString()));
            Assert.That(Text("edit-ap"), Is.EqualTo(d.Content!.Rules.Turn.Ap.ToString()));
            Assert.That(Text("edit-move"), Is.EqualTo(d.Content.Rules.Turn.Movement.ToString()));
            Assert.That(Text("edit-spawn"), Is.EqualTo(DebugEdits.SpawnName(d.Content!, "guard")));
            Press("edit-unit-next");
            yield return null;
            Assert.That(Text("edit-unit"), Does.StartWith("#").And.Not.EqualTo("#0 You"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SettingHpIsACommandThatTheBoardAndTheLogFollow()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            for (var i = 0; i < 3; i++) Press("edit-hp-down");
            yield return null;
            var expected = int.Parse(Text("edit-hp"));
            Assert.That(expected, Is.LessThan(d.Flow!.Live.Units.First(u => u.Id == 0).Hp));
            Press("edit-set-hp");
            yield return WaitUntilIdle(d);
            yield return null;
            Assert.That(d.Flow!.Live.Units.First(u => u.Id == 0).Hp, Is.EqualTo(expected));
            Assert.That(d.Board!.Units[0].ShownHp, Is.EqualTo(expected), "the board shows it");
            Assert.That(d.Flow.Session.Record.Commands.Select(c => c.ToJson()), Does.Contain($"{{\"type\":\"debug\",\"op\":\"setHp\",\"unit\":0,\"value\":{expected}}}"));
            Assert.That(d.Flow.Log.Select(l => l.Text), Has.Some.Contains("[debug]").And.Contains($"HP set to {expected}"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SettingApAndMovementChangesWhatThePlayerHasLeft()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            Press("edit-ap-up");
            Press("edit-ap-up");
            Press("edit-move-down");
            yield return null;
            var turn = d.Content!.Rules.Turn;
            Assert.That((Text("edit-ap"), Text("edit-move")), Is.EqualTo(((turn.Ap + 2).ToString(), (turn.Movement - 1).ToString())));
            Press("edit-set-ap");
            yield return WaitUntilIdle(d);
            Assert.That((d.Flow!.Live.Ap, d.Flow.Live.Movement), Is.EqualTo((turn.Ap + 2, turn.Movement - 1)));
            Assert.That(d.Flow.Session.Record.Commands.Last().ToJson(), Is.EqualTo($"{{\"type\":\"debug\",\"op\":\"setAp\",\"ap\":{turn.Ap + 2},\"movement\":{turn.Movement - 1}}}"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ASpawnWaitsForATileAndTheNextClickPlacesIt()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            var room = d.Content!.RoomFor(d.Flow!.Live.Settings.Room);
            var board = new Board(room);
            var free = Enumerable.Range(0, board.Width).SelectMany(x => Enumerable.Range(0, board.Height).Select(y => new Pos(x, y)))
                .First(p => !board.BlocksMove(p) && d.Flow.Live.Units.All(u => u.Dead || u.Pos != p) && d.Flow.Live.Objects.All(o => o.Destroyed || o.Pos != p));
            var before = d.Flow.Live.Units.Count;

            Press("edit-spawn-go");
            yield return null;
            Assert.That(d.Edits.Armed, Is.EqualTo("guard"));
            Assert.That(Text("edit-spawn-go-text"), Does.Contain("Click a tile"));
            Assert.That(Root.Q<Label>("hint").text, Does.Contain("Click a tile to spawn"));

            d.ClickTile(free);
            yield return WaitUntilIdle(d);
            yield return null;
            Assert.That(d.Edits.Armed, Is.Null, "one click places one unit");
            Assert.That(d.Flow!.Live.Units.Count, Is.EqualTo(before + 1));
            var spawned = d.Flow.Live.Units.Last();
            Assert.That((spawned.Def, spawned.Pos), Is.EqualTo(("guard", free)));
            Assert.That(d.Board!.Units.ContainsKey(spawned.Id), Is.True, "the board shows it");
            Assert.That(d.Flow.Session.Record.Commands.Last().ToJson(), Is.EqualTo($"{{\"type\":\"debug\",\"op\":\"spawn\",\"def\":\"guard\",\"target\":\"{Tiles.Name(free)}\"}}"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator PressingSpawnAgainTakesItBackAndClosingTheOverlayDropsIt()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            var commands = d.Flow!.Session.Record.Commands.Count;
            Press("edit-spawn-go");
            Assert.That(d.Edits.Armed, Is.Not.Null);
            Press("edit-spawn-go");
            Assert.That(d.Edits.Armed, Is.Null);
            Press("edit-spawn-go");
            d.SetDebugOpen(false);
            Assert.That(d.Edits.Armed, Is.Null, "a closed overlay leaves no spawn waiting for a click");
            Assert.That(d.Flow.Session.Record.Commands.Count, Is.EqualTo(commands), "and nothing was spawned");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ForcingTheNextWaveBringsItAndTheButtonGoesOffWhenNoneAreLeft()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            var waves = d.Content!.RoomFor(d.Flow!.Live.Settings.Room).Waves.Count;
            while (d.Flow.Live.WavesSpawned < waves)
            {
                var before = d.Flow.Live.WavesSpawned;
                Press("edit-force-wave");
                yield return WaitUntilIdle(d);
                yield return null;
                Assert.That(d.Flow.Live.WavesSpawned, Is.EqualTo(before + 1));
            }
            yield return null;
            Assert.That(Btn("edit-force-wave").enabledSelf, Is.False, "there is no wave to force");
            Assert.That(d.Board!.WavesShown, Is.EqualTo(waves), "the board has shown every wave arrive");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator HoldingWave3BackRestartsTheFightWithTwoWavesAndLettingItGoRestartsAgain()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            Assert.That(Btn("edit-hold-wave").ClassListContains("on"), Is.False);
            var first = d.Flow;

            Press("edit-hold-wave");
            yield return WaitUntilIdle(d);
            yield return null;
            Assert.That(d.Flow, Is.Not.SameAs(first), "a new fight");
            Assert.That(d.Settings.MaxWaves, Is.EqualTo(2));
            Assert.That(d.Flow!.Session.Record.Settings.MaxWaves, Is.EqualTo(2), "held in the fight's own settings, so in its replay");
            Assert.That(Btn("edit-hold-wave").ClassListContains("on"), Is.True);
            Assert.That(d.Flow.Session.Record.Loadout.Abilities, Is.EqualTo(d.Content!.Presets.First(p => p.Id == "tech").Abilities));

            var held = d.Flow;
            Press("edit-hold-wave");
            yield return WaitUntilIdle(d);
            yield return null;
            Assert.That(d.Flow, Is.Not.SameAs(held));
            Assert.That(d.Settings.MaxWaves, Is.Null);
            Assert.That(Btn("edit-hold-wave").ClassListContains("on"), Is.False);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator RestartingStartsTheSameFightAgain()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            Assert.That(d.Commit(new EndTurnCommand())!.Ok, Is.True);
            yield return WaitUntilIdle(d);
            var played = d.Flow;
            Assert.That(played!.Session.Record.Commands, Is.Not.Empty);

            Press("edit-restart");
            yield return WaitUntilIdle(d);
            yield return null;
            Assert.That(d.Flow, Is.Not.SameAs(played));
            Assert.That(d.Flow!.Session.Record.Commands, Is.Empty);
            Assert.That(d.Flow.Session.Record.Loadout.Upgrades, Is.EqualTo(played.Session.Record.Loadout.Upgrades));
            Assert.That(d.Flow.Live.Round, Is.EqualTo(1));
            Assert.That(Text("edit-unit"), Is.EqualTo("#0 You"), "the controls start again with the fight");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheEditsAreOffWhileASavedFightPlaysAndRestartPlaysItAgain()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.SetDebugOpen(true);
            d.PlayReplayFile(GameContent.RepoPath("reports", "winning-line-tech.json"));
            yield return null;
            yield return null;
            Assert.That(d.Replaying, Is.True);
            foreach (var name in new[] { "edit-set-hp", "edit-set-ap", "edit-spawn-go", "edit-force-wave", "edit-hold-wave" })
            {
                Assert.That(Btn(name).enabledSelf, Is.False, $"{name} would change a recording");
            }
            Assert.That(Btn("edit-restart").enabledSelf, Is.True);

            d.SetReplayPaused(true);
            while (d.ReplayNext < 1) { d.StepReplay(); yield return null; }
            Assert.That(d.ReplayNext, Is.GreaterThanOrEqualTo(1));
            Press("edit-restart");
            yield return null;
            Assert.That(d.Replaying, Is.True, "the saved fight plays again");
            Assert.That(d.ReplayNext, Is.EqualTo(0), "from its first command");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator WithNoFightTheEditsAreOff()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            d.ShowStartScreen();
            yield return null;
            foreach (var name in new[] { "edit-set-hp", "edit-set-ap", "edit-spawn-go", "edit-force-wave", "edit-hold-wave", "edit-restart" })
            {
                Assert.That(Btn(name).enabledSelf, Is.False, $"{name} has no fight to act on");
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AnEditedFightsRecordReplaysToTheSameEvents()
        {
            yield return OpenFight();
            var d = FightDirector.Instance!;
            for (var i = 0; i < 3; i++) Press("edit-hp-down");
            Press("edit-set-hp");
            yield return WaitUntilIdle(d);
            Press("edit-force-wave");
            yield return WaitUntilIdle(d);
            var record = d.Flow!.Session.Record;
            Assert.That(record.Commands.Select(c => c.Type), Is.EqualTo(new[] { "debug", "debug" }));
            var replay = Records.Replay(d.Content!, Telemetry.LoadReplay(d.Content!, record.ToToken().ToString()));
            Assert.That(Records.EventsToJsonl(replay.Events), Is.EqualTo(Records.EventsToJsonl(d.Flow.Session.Events)));
        }
    }
}
