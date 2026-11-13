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
    /// <summary>The debug overlay on the real fight screen: its panel, its marks on the board, step mode, and a replay's pause and step.</summary>
    public class DebugOverlayScreenTests
    {
        private string _telemetry = "";

        [SetUp]
        public void SendTelemetryAside()
        {
            _telemetry = Path.Combine(Path.GetTempPath(), "lab-escape-telemetry-" + System.Guid.NewGuid().ToString("N"));
            TelemetrySink.Isolate(_telemetry);
            Motion.Reduced = false;
        }

        /// <summary>The director outlives a test, so the overlay is closed and step mode turned off for the tests that follow.</summary>
        [TearDown]
        public void CloseTheOverlay()
        {
            var d = FightDirector.Instance;
            if (d != null)
            {
                d.SetDebugOpen(false);
                d.StepMode = false;
            }
            TelemetrySink.Reset();
            if (Directory.Exists(_telemetry)) Directory.Delete(_telemetry, true);
        }

        private static IEnumerator WaitForDirector()
        {
            while (FightDirector.Instance == null || FightDirector.Instance.Content == null) yield return null;
        }

        private static IEnumerator WaitUntilIdle(FightDirector d)
        {
            while (!d.Idle && d.Flow!.Live.Outcome == null) yield return null;
            while (d.Flow!.Busy) yield return null;
        }

        private static FightRecord WinningLine(FightDirector d, string preset) =>
            Records.CheckCompatible(d.Content!, JsonIn.Parse(File.ReadAllText(GameContent.RepoPath("reports", $"winning-line-{preset}.json"))));

        private static VisualElement Root => Object.FindAnyObjectByType<UIDocument>().rootVisualElement;

        private static System.Collections.Generic.List<string> Texts(string className) => Root.Query<Label>(className: className).ToList().Select(l => l.text).ToList();

        private static IEnumerator StartedFight(FightDirector d)
        {
            var p = d.Content!.Presets[0];
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);
            yield return null;
            yield return null;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheOverlayShowsItsPanelAndItsMarksAndCoversNoBoard()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            yield return StartedFight(d);
            var free = d.Hud.FreeScreenArea();
            Assert.That(Texts("debug-id"), Is.Empty, "closed, it marks nothing");
            Assert.That(Root.Q("debug").ClassListContains("hidden"), Is.True);

            d.SetDebugOpen(true);
            yield return null;
            yield return null;
            var s = d.Flow!.Live;
            Assert.That(Root.Q("debug").ClassListContains("hidden"), Is.False);
            Assert.That(Root.Q<Label>("debug-version").text, Is.EqualTo(DebugReport.Header(d.Content!)));
            Assert.That(d.Hud.FreeScreenArea().width, Is.LessThan(free.width), "the column takes its room from the board area");
            Assert.That(d.Hud.PointerOverUi(new Vector2(d.Hud.FreeScreenArea().xMin - 60f, d.Hud.FreeScreenArea().center.y)), Is.True, "and the column blocks the board under it");

            // Every figure carries its ID; every later wave's spawn tile and every hazard tile is labelled.
            Assert.That(Texts("debug-id"), Is.EquivalentTo(d.Board!.Units.Keys.Select(id => $"#{id}")));
            var spawns = DebugReport.Spawns(d.Content!, s, s.WavesSpawned);
            Assert.That(spawns, Is.Not.Empty);
            Assert.That(Texts("debug-spawn"), Is.EquivalentTo(spawns.Select(m => $"W{m.Wave}")));
            Assert.That(Texts("debug-hazard"), Is.EquivalentTo(s.Hazards.SelectMany(h => h.Tiles.Select(_ => DebugReport.HazardMark(h)))));

            // The panel has the wave timer and every hazard.
            var panel = Root.Q("debug-text").Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(panel, Has.Member(DebugReport.NextWave(d.Content!, s, s.WavesSpawned)));
            foreach (var h in s.Hazards) Assert.That(panel, Has.Member($"{Words.HazardName(d.Content!, s, h.Id)}: {DebugReport.StateText(h)}"));

            // A toggle takes its marks off the board.
            d.DebugToggles.Ids = false;
            yield return null;
            Assert.That(Texts("debug-id"), Is.Empty);
            d.DebugToggles.Ids = true;

            d.SetDebugOpen(false);
            yield return null;
            yield return null;
            Assert.That(Texts("debug-id").Concat(Texts("debug-mark")), Is.Empty, "closed again, it leaves nothing behind");
            Assert.That(d.Hud.FreeScreenArea().width, Is.EqualTo(free.width).Within(1f));
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator ASavedFightCanBePausedAndSteppedOneCommandAtATime()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = WinningLine(d, "tech");
            d.PlayReplayFile(GameContent.RepoPath("reports", "winning-line-tech.json"));
            d.SetReplayPaused(true);
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);
            for (var i = 0; i < 20; i++) yield return null;
            Assert.That(d.ReplayNext, Is.EqualTo(0), "paused, it plays nothing");
            Assert.That(d.Replaying, Is.True);

            for (var n = 1; n <= 3; n++)
            {
                d.Next(false);
                yield return null;
                yield return WaitUntilIdle(d);
                for (var i = 0; i < 10; i++) yield return null;
                Assert.That(d.ReplayNext, Is.EqualTo(n), $"N plays one command, and only one ({n})");
                Assert.That(d.Flow!.Session.Record.Commands.Select(c => c.ToJson()), Is.EqualTo(record.Commands.Take(n).Select(c => c.ToJson())));
            }

            // Step mode holds the frames of a stepped command, and N then steps through them.
            d.SetDebugOpen(true);
            d.StepMode = true;
            var at = d.ReplayNext;
            while (at < record.Commands.Count && !(record.Commands[at] is EndTurnCommand)) at++;
            d.Flow!.SetSpeed(FightSpeed.Skip);
            while (d.ReplayNext < at)
            {
                d.Next(false);
                yield return null;
                yield return WaitUntilIdle(d);
            }
            d.Flow!.SetSpeed(FightSpeed.Fast);
            d.Next(false);
            var waited = 0;
            while (!d.Flow!.Holding && waited++ < 2000) yield return null;
            Assert.That(d.Flow!.Holding, Is.True, "an end of turn plays several frames, and the first is held");
            var stage = d.Flow!.Stage;
            for (var i = 0; i < 20; i++) yield return null;
            Assert.That(d.Flow!.Stage, Is.SameAs(stage), "held");
            d.Next(false);
            Assert.That(d.Flow!.Stage, Is.Not.SameAs(stage), "N shows the next");
            var presses = 0;
            while (d.Flow!.Busy && presses++ < 200)
            {
                d.Next(true);
                yield return null;
            }
            Assert.That(d.Flow!.Busy, Is.False);
            Assert.That(d.ReplayNext, Is.EqualTo(at + 1), "and the saved fight stays paused");
            d.StepMode = false;

            // P plays it on to its end.
            d.Flow!.SetSpeed(FightSpeed.Skip);
            d.SetReplayPaused(false);
            while (d.Flow!.Live.Outcome == null || d.Flow.Busy) yield return null;
            yield return null;
            Assert.That(d.Flow!.Live.Outcome?.Result, Is.EqualTo(FightResult.Win));
            Assert.That(d.Replaying, Is.False);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AHazardsMarkMovesOnWhenItsCuePlaysAndNotBefore()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ScriptedPointer = true;
            var record = WinningLine(d, "tech");
            d.StartFight(record.Loadout, record.Settings, "tech");
            d.Flow!.SetSpeed(FightSpeed.Skip);
            yield return WaitUntilIdle(d);
            d.SetDebugOpen(true);

            // Up to the first command after which a hazard primes.
            var at = -1;
            for (var i = 0; i < record.Commands.Count; i++)
            {
                var r = Engine.Step(d.Content!, d.Flow!.Live, record.Commands[i]);
                if (r.Events.OfType<HazardPrimed>().Any())
                {
                    at = i;
                    break;
                }
                Assert.That(d.Commit(record.Commands[i])!.Ok, Is.True);
                yield return WaitUntilIdle(d);
            }
            Assert.That(at, Is.GreaterThanOrEqualTo(0), "a hazard primes in the winning line");
            var primed = Engine.Step(d.Content!, d.Flow!.Live, record.Commands[at]).Events.OfType<HazardPrimed>().First().Hazard;
            Assert.That(d.Board!.Hazards.First(h => h.Id == primed).State, Is.Not.EqualTo(HazardState.Primed));

            // Its mark as the frame that primes it reaches the screen, and as the priming cue plays.
            HazardState? atFrame = null, atCue = null;
            d.Flow!.FrameShown += (state, stage) =>
            {
                if (stage.Cues.Any(c => c.Event is HazardPrimed p && p.Hazard == primed)) atFrame = d.Board!.Hazards.First(h => h.Id == primed).State;
            };
            d.Board!.CuePlayed += cue =>
            {
                if (cue.Kind == CueKind.Event && cue.Event is HazardPrimed p && p.Hazard == primed) atCue = d.Board!.Hazards.First(h => h.Id == primed).State;
            };
            d.Flow!.SetSpeed(FightSpeed.Fast);
            Assert.That(d.Commit(record.Commands[at])!.Ok, Is.True);
            Assert.That(d.Flow!.Live.Hazards.First(h => h.Id == primed).State, Is.EqualTo(HazardState.Primed), "the engine has primed it already");
            while (d.Flow!.Busy)
            {
                var cue = d.Flow.Stage.Cues.FirstOrDefault(c => c.Kind == CueKind.Event && c.Event is HazardPrimed p && p.Hazard == primed);
                if (cue != null && d.Flow.FrameTime < cue.At)
                {
                    Assert.That(d.Board!.Hazards.First(h => h.Id == primed).State, Is.Not.EqualTo(HazardState.Primed), $"{d.Flow.FrameTime:0.000}s into the frame, before its cue at {cue.At:0.000}s");
                }
                yield return null;
            }
            Assert.That(atFrame, Is.Not.Null.And.Not.EqualTo(HazardState.Primed), "the frame that primes it has reached the screen, and its mark has not moved");
            Assert.That(atCue, Is.EqualTo(HazardState.Primed), "its mark moves as its cue plays");
            yield return null;
            Assert.That(d.Board!.Hazards.Select(h => (h.Id, h.State, h.Cooldown)), Is.EqualTo(d.Flow!.Live.Hazards.Select(h => (h.Id, h.State, h.Cooldown))), "level with the state between frames");
            Assert.That(Texts("debug-hazard"), Has.Member("PRIMED"));
        }
    }
}
