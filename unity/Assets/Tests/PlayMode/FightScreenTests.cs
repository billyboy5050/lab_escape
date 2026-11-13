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
    /// <summary>The real fight screen: board, HUD and director together. Any error logged while these run fails them.</summary>
    public class FightScreenTests
    {
        private string _telemetry = "";

        /// <summary>Every test's fights save their telemetry to a folder of its own, never to the repository's telemetry/.</summary>
        [SetUp]
        public void SendTelemetryAside()
        {
            _telemetry = Path.Combine(Path.GetTempPath(), "lab-escape-telemetry-" + System.Guid.NewGuid().ToString("N"));
            TelemetrySink.Isolate(_telemetry);
            Motion.Reduced = false;
        }

        [TearDown]
        public void ClearTelemetry()
        {
            TelemetrySink.Reset();
            if (Directory.Exists(_telemetry)) Directory.Delete(_telemetry, true);
        }

        private string[] SavedFolders() => Directory.Exists(_telemetry) ? Directory.GetDirectories(_telemetry) : new string[0];

        private static IEnumerator WaitForDirector()
        {
            while (FightDirector.Instance == null || FightDirector.Instance.Content == null) yield return null;
        }

        private static IEnumerator WaitUntilIdle(FightDirector d)
        {
            while (!d.Idle && d.Flow!.Live.Outcome == null) yield return null;
            while (d.Flow!.Busy) yield return null;
        }

        private static FightRecord WinningLine(FightDirector d, string preset)
        {
            return Records.CheckCompatible(d.Content!, JsonIn.Parse(File.ReadAllText(GameContent.RepoPath("reports", $"winning-line-{preset}.json"))));
        }

        /// <summary>
        /// The board shows exactly the state: every unit and object in it has a figure, on its tile, showing its HP, and nothing
        /// else has one. Staging lets the board lag the state while a frame plays; this must hold again whenever the queue is empty.
        /// </summary>
        private static void AssertBoardIsLevel(FightDirector d, string when)
        {
            var s = d.Flow!.Live;
            var board = d.Board!;
            var units = s.Units.Where(u => !u.Dead && !u.Docked).ToList();
            Assert.That(board.Units.Keys.OrderBy(k => k), Is.EqualTo(units.Select(u => u.Id).OrderBy(k => k)), when + ": the figures are the units");
            foreach (var u in units)
            {
                var v = board.Units[u.Id];
                Assert.That(v.Tile, Is.EqualTo(u.Pos), when + $": unit {u.Id} is on its tile");
                Assert.That(v.ShownHp, Is.EqualTo(u.Hp), when + $": unit {u.Id} shows its HP");
                Assert.That((v.Ground - Iso.Tile(u.Pos.X, u.Pos.Y)).magnitude, Is.LessThan(0.6f), when + $": unit {u.Id} stands where its tile is, give or take a shove");
            }
            var objects = s.Objects.Where(o => !o.Destroyed).ToList();
            Assert.That(board.Objects.Keys.OrderBy(k => k), Is.EqualTo(objects.Select(o => o.Id).OrderBy(k => k)), when + ": the props are the objects");
            foreach (var o in objects) Assert.That(board.Objects[o.Id].ShownHp, Is.EqualTo(o.Hp), when + $": object {o.Id} shows its HP");
        }

        /// <summary>After a jump (a skip or an undo) nothing is still on its way: every prop is on its tile and can be seen.</summary>
        private static void AssertPropsHaveArrived(FightDirector d, string when)
        {
            foreach (var o in d.Flow!.Live.Objects.Where(x => !x.Destroyed))
            {
                var p = d.Board!.Objects[o.Id];
                Assert.That(p.Arrived, Is.True, when + $": {o.Kind} {o.Id} has arrived");
                Assert.That((p.Position - Iso.Tile(o.Pos.X, o.Pos.Y)).magnitude, Is.LessThan(0.001f), when + $": {o.Kind} {o.Id} is on its tile");
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AWinningLinePlaysToVictory()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = WinningLine(d, "hybrid");
            var countsBefore = PlayerPrefs.GetString(Telemetry.AttemptsKey, "<none>");
            d.StartFight(record.Loadout, record.Settings, "hybrid");
            d.Flow!.SetSpeed(FightSpeed.Fast);
            var n = 0;
            foreach (var cmd in record.Commands)
            {
                n++;
                yield return WaitUntilIdle(d);
                AssertBoardIsLevel(d, $"before command {n}");
                d.SetPointer(SmokeRun.TileOf(cmd));
                yield return null;
                d.SetPointer(null);
                var r = d.Commit(cmd);
                Assert.That(r!.Ok, Is.True, $"command {n}: {r.Error}");
            }
            yield return WaitUntilIdle(d);
            yield return null;
            Assert.That(d.Flow!.Live.Outcome?.Result, Is.EqualTo(FightResult.Win));
            Assert.That(d.EndScreenShown, Is.True);
            AssertBoardIsLevel(d, "at the end");

            // The end card gives the fight in four figures, from its summary.
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var figures = root.Q("end-figures").Query<Label>(className: "figure-value").ToList().Select(l => l.text).ToList();
            var summary = d.Flow!.Session.Summary();
            Assert.That(figures, Is.EqualTo(new[]
            {
                summary.Rounds.ToString(),
                $"{d.Flow!.Live.Units.First(u => u.Id == 0).Hp}/{d.Flow!.Live.Units.First(u => u.Id == 0).MaxHp}",
                summary.Kills.Values.Sum().ToString(),
                summary.PlayerDamageTaken.Total.ToString(),
            }));

            Assert.That(PlayerPrefs.GetString(Telemetry.AttemptsKey, "<none>"), Is.EqualTo(countsBefore), "a scripted fight uses up no playtest attempt");

            // The fight's telemetry is saved: the web client's three files, and a replay of what was played.
            var saved = SavedFolders();
            Assert.That(saved.Length, Is.EqualTo(1), "one folder for the fight");
            Assert.That(Directory.GetFiles(saved[0]).Select(Path.GetFileName).OrderBy(n => n), Is.EqualTo(new[] { "events.jsonl", "replay.json", "summary.json" }));
            Assert.That(Path.GetFileName(saved[0]), Does.EndWith("-hybrid-win"));
            var replay = Telemetry.LoadReplay(d.Content!, File.ReadAllText(Path.Combine(saved[0], "replay.json")));
            Assert.That(replay.Commands.Select(c => c.ToJson()), Is.EqualTo(record.Commands.Select(c => c.ToJson())));

            // Back on the start screen, the fight is offered to watch again.
            var change = root.Q<Button>("change");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = change;
                change.SendEvent(e);
            }
            yield return null;
            Assert.That(d.Hud.StartVisible, Is.True);
            var listed = root.Q("recent").Query<Button>().ToList().Select(b => b.text).ToList();
            Assert.That(listed, Has.Count.EqualTo(1));
            Assert.That(listed[0], Does.EndWith("hybrid win"));
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator ASavedFightPlaysBackToItsEndAndSavesNothing()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            d.PlayReplayFile(GameContent.RepoPath("reports", "winning-line-tech.json"));
            Assert.That(d.Replaying, Is.True);
            d.Flow!.SetSpeed(FightSpeed.Skip);
            // The player's input does nothing while it plays.
            d.EndTurn();
            while (d.Flow!.Live.Outcome == null || d.Flow.Busy) yield return null;
            yield return null;
            Assert.That(d.Flow!.Live.Outcome?.Result, Is.EqualTo(FightResult.Win));
            Assert.That(d.Replaying, Is.False);
            Assert.That(d.EndScreenShown, Is.True);
            Assert.That(SavedFolders(), Is.Empty, "a replay is not saved again");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AReplayThatDoesNotFitIsRefusedOnTheStartScreen()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            Directory.CreateDirectory(_telemetry);
            var file = Path.Combine(_telemetry, "broken.json");
            File.WriteAllText(file, "{ \"format\": \"lab-escape/replay@1\" }");
            d.PlayReplayFile(file);
            yield return null;
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            Assert.That(d.Hud.StartVisible, Is.True);
            Assert.That(d.Flow, Is.Null);
            Assert.That(root.Q<Label>("start-error").text, Does.StartWith("Could not play back broken.json: "));
            Assert.That(FightDirector.ReplayName("/x/2026-11-12T15-30-04-512Z-tech-win/replay.json"), Is.EqualTo("2026-11-12T15-30-04-512Z-tech-win"));
            Assert.That(FightDirector.ReplayName("fights/one.json"), Is.EqualTo("one.json"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheFirstWaveIsSeenToArrive()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var p = d.Content!.Presets[0];
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            d.Flow!.SetSpeed(FightSpeed.Fast);
            Assert.That(d.Flow.Busy, Is.True, "the entrance is playing");
            Assert.That(d.Board!.Units.Keys, Is.EqualTo(new[] { 0 }), "the room opens on the player alone");
            yield return null;
            Assert.That(d.Board!.Units.Keys, Is.EqualTo(new[] { 0 }), "and nobody else is there while the title is up");
            yield return WaitUntilIdle(d);
            Assert.That(d.Board!.Units.Count, Is.GreaterThan(1));
            AssertBoardIsLevel(d, "after the entrance");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AUnitsHpFallsWhenTheBlowLandsAndNotBefore()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = WinningLine(d, "tech");
            d.StartFight(record.Loadout, record.Settings, "tech");
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);
            // The line's second command is a Sidearm shot that wounds a guard.
            Assert.That(d.Commit(record.Commands[0])!.Ok, Is.True);
            d.Flow!.SetSpeed(FightSpeed.Normal);
            Assert.That(d.Commit(record.Commands[1])!.Ok, Is.True);

            var stage = d.Flow!.Stage;
            var blow = stage.Cues.First(c => c.Kind == CueKind.Event && c.Event is DamageDealt);
            var hurt = (DamageDealt)blow.Event;
            var view = d.Board!.Units[hurt.Target.Id];
            var before = view.ShownHp;
            Assert.That(blow.At, Is.GreaterThan(0f), "the shot has to be gathered and to fly first");
            Assert.That(d.Flow!.Live.Units.First(u => u.Id == hurt.Target.Id).Hp, Is.EqualTo(hurt.HpAfter), "the engine has already resolved it");
            Assert.That(before, Is.EqualTo(hurt.HpAfter + hurt.HpLost), "and the board has not shown it yet");

            var sawItWait = false;
            while (ReferenceEquals(d.Flow!.Stage, stage) && d.Flow.FrameTime < blow.At)
            {
                Assert.That(view.ShownHp, Is.EqualTo(before), $"{d.Flow.FrameTime:0.000}s into the frame, before the blow at {blow.At:0.000}s");
                sawItWait = true;
                yield return null;
            }
            Assert.That(sawItWait, Is.True);
            yield return WaitUntilIdle(d);
            Assert.That(view.ShownHp, Is.EqualTo(hurt.HpAfter));
            AssertBoardIsLevel(d, "after the shot");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ASkipPartWayThroughLeavesTheBoardLevel()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = WinningLine(d, "tech");
            d.StartFight(record.Loadout, record.Settings, "tech");
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);
            // Up to the end of the second turn, where turrets and guards trade shots and a guard dies.
            for (var i = 0; i < 7; i++) Assert.That(d.Commit(record.Commands[i])!.Ok, Is.True);
            d.Flow!.SetSpeed(FightSpeed.Normal);
            var frames = 0;
            d.Flow.FrameShown += (state, stage) => frames++;
            Assert.That(d.Commit(record.Commands[7])!.Ok, Is.True);
            // Into the third frame of the turn, and a few drawn frames past its start, whatever the machine's speed.
            while (frames < 3 && d.Flow!.Busy) yield return null;
            for (var i = 0; i < 3 && d.Flow!.Busy; i++) yield return null;
            Assert.That(d.Flow!.Busy, Is.True, "still in the middle of the turn's animation");
            d.Flow.Skip();
            yield return null;
            Assert.That(d.Flow!.Busy, Is.False);
            AssertBoardIsLevel(d, "after the skip");
            AssertPropsHaveArrived(d, "after the skip");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ASkipLeavesNoPropStillOnItsWay()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = WinningLine(d, "tech");
            d.StartFight(record.Loadout, record.Settings, "tech");
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);

            // The eighth command ends a turn in which a guard dies. Its corpse waits, unseen, for the body to fall.
            for (var i = 0; i < 7; i++) Assert.That(d.Commit(record.Commands[i])!.Ok, Is.True);
            d.Flow!.SetSpeed(FightSpeed.Normal);
            Assert.That(d.Commit(record.Commands[7])!.Ok, Is.True);
            while (d.Flow!.Busy && !d.Board!.Objects.Values.Any(p => p.Data.Kind == ObjectKind.Corpse && !p.Arrived)) yield return null;
            Assert.That(d.Flow!.Busy, Is.True, "a corpse is waiting for its body while the turn is still playing");
            d.Flow.Skip();
            yield return null;
            AssertBoardIsLevel(d, "after skipping a death");
            AssertPropsHaveArrived(d, "after skipping a death");

            // The fourteenth throws a mine, which is in the air for a moment.
            d.Flow!.SetSpeed(FightSpeed.Skip);
            for (var i = 8; i < 13; i++) Assert.That(d.Commit(record.Commands[i])!.Ok, Is.True);
            d.Flow!.SetSpeed(FightSpeed.Normal);
            Assert.That(d.Commit(record.Commands[13])!.Ok, Is.True);
            while (d.Flow!.Busy && !d.Board!.Objects.Values.Any(p => p.Data.Kind == ObjectKind.Mine && !p.Arrived)) yield return null;
            Assert.That(d.Flow!.Busy, Is.True, "the mine is in the air while its frame is still playing");
            d.Flow.Skip();
            yield return null;
            AssertBoardIsLevel(d, "after skipping a throw");
            AssertPropsHaveArrived(d, "after skipping a throw");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator EveryPresetShowsEveryMovePreviewAndEveryAction()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            foreach (var p in d.Content!.Presets)
            {
                d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
                d.Flow!.SetSpeed(FightSpeed.Skip);
                yield return WaitUntilIdle(d);
                yield return null;
                foreach (var tile in d.Model!.Moves.Select(m => m.Tile).Take(6).ToList())
                {
                    d.SetPointer(tile);
                    yield return null;
                }
                foreach (var a in d.Model!.Statuses.Where(s => s.Usable && s.Id != "sprint").ToList())
                {
                    d.SelectAction(a.Id);
                    yield return null;
                    Assert.That(d.Model!.ModeId, Is.EqualTo(a.Id));
                    foreach (var t in d.Model!.FirstTiles.Take(4).ToList())
                    {
                        d.SetPointer(t);
                        yield return null;
                    }
                    d.Model!.Clear();
                }
                d.SetPointer(null);
                d.EndTurn();
                yield return WaitUntilIdle(d);
                Assert.That(d.Flow!.Live.Round, Is.EqualTo(2));
            }
        }

        private static IEnumerator StartedFight(FightDirector d)
        {
            var p = d.Content!.Presets[0];
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);
            yield return null;
            yield return null;
        }

        private static Vector2 ScreenPoint(FightDirector d, Vector3 world) => d.IsoCamera.Cam.WorldToScreenPoint(world);

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AScreenPointOverTheFloorPicksThatTile()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            var occupied = d.Flow!.Live.Units.Where(u => !u.Dead).Select(u => u.Pos).ToList();
            var board = d.Content!.DefaultRoom.Board;
            var checkedTiles = 0;
            foreach (var t in board.AllTiles().Where(p => !occupied.Any(o => System.Math.Abs(o.X - p.X) + System.Math.Abs(o.Y - p.Y) <= 2)))
            {
                Assert.That(d.Board!.PickTile(ScreenPoint(d, Iso.Tile(t.X, t.Y))), Is.EqualTo(t));
                checkedTiles++;
            }
            Assert.That(checkedTiles, Is.GreaterThan(10));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator PickingTheFloorStillWorksZoomedAndPanned()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            d.IsoCamera.Zoom(10f);
            d.IsoCamera.PanPixels(new Vector2(40f, -25f));
            yield return null;
            var t = new Pos(4, 4);
            Assert.That(d.Board!.PickTile(ScreenPoint(d, Iso.Tile(t.X, t.Y))), Is.EqualTo(t));
            d.IsoCamera.Zoom(-1000f);
            yield return null;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ClickingAFigurePicksTheTileItStandsOn()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            var u = d.Flow!.Live.Units.First(x => x.Team == Team.Enemy && !x.Dead);
            var head = ScreenPoint(d, Iso.Tile(u.Pos.X, u.Pos.Y, 0.7f));
            Assert.That(d.Board!.PickTile(head), Is.EqualTo(u.Pos), "the point above the feet is over the tile behind on the floor");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator PanelsBlockTheBoardAndTheBoardAreaDoesNot()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            var free = d.Hud.FreeScreenArea();
            Assert.That(d.Hud.PointerOverUi(free.center), Is.False, "the middle of the board area");
            Assert.That(d.Hud.PointerOverUi(new Vector2(free.xMax + 40f, free.center.y)), Is.True, "the side column");
            Assert.That(d.Hud.PointerOverUi(new Vector2(free.center.x, free.yMin - 30f)), Is.True, "the action panel");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheEndTurnButtonEndsTheTurn()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var button = root.Q<Button>("end-turn");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
            yield return WaitUntilIdle(d);
            Assert.That(d.Flow!.Live.Round, Is.EqualTo(2));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheBoardShowsItsCoordinatesOnScreen()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var labels = root.Query<Label>(className: "coord").ToList();
            var board = d.Content!.RoomFor(d.Flow!.Live.Settings.Room).Board;
            var expected = Enumerable.Range(0, board.Width).Select(x => Tiles.Name(new Pos(x, 0)).Substring(0, 1))
                .Concat(Enumerable.Range(0, board.Height).Select(y => Tiles.Name(new Pos(0, y)).Substring(1)));
            Assert.That(labels.Select(l => l.text), Is.EquivalentTo(expected));
            var screen = root.worldBound;
            foreach (var l in labels) Assert.That(screen.Contains(l.worldBound.center), Is.True, $"{l.text} is on screen");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator PointingAtAnActionShowsItsRuleInTheScanner()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var action = d.Model!.Statuses.First(a => d.Content!.Abilities.ContainsKey(a.Id));
            d.SetActionPointer(action.Id);
            yield return null;
            yield return null;
            var lines = root.Q("info-body").Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(lines, Has.Member(d.Content!.Abilities[action.Id].Text));
            d.SetActionPointer(null);
            yield return null;
            yield return null;
            lines = root.Q("info-body").Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(lines, Has.No.Member(d.Content!.Abilities[action.Id].Text));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator UndoPutsTheFigureBack()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var p = d.Content!.Presets[0];
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);
            var start = d.Flow!.Live.Units.First(u => u.Id == 0).Pos;
            d.ClickTile(d.Model!.Moves.First().Tile);
            yield return WaitUntilIdle(d);
            Assert.That(d.Flow!.Live.Units.First(u => u.Id == 0).Pos, Is.Not.EqualTo(start));
            Assert.That(d.Flow!.Undo(), Is.True);
            yield return null;
            Assert.That(d.Flow!.Live.Units.First(u => u.Id == 0).Pos, Is.EqualTo(start));
        }
    }
}
