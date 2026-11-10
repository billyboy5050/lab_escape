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

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AWinningLinePlaysToVictory()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = WinningLine(d, "hybrid");
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
