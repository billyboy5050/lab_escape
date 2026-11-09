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

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AWinningLinePlaysToVictory()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = Records.CheckCompatible(d.Content!, JsonIn.Parse(File.ReadAllText(GameContent.RepoPath("reports", "winning-line-hybrid.json"))));
            d.StartFight(record.Loadout, record.Settings, "hybrid");
            d.Flow!.SetSpeed(FightSpeed.Fast);
            var n = 0;
            foreach (var cmd in record.Commands)
            {
                n++;
                yield return WaitUntilIdle(d);
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
