#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The debug overlay's text, step mode and the log's debug lines: the parts with no Unity code.</summary>
    public class DebugOverlayTests
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

        private static FightRecord WinningLine(string preset) =>
            Records.CheckCompatible(C, JsonIn.Parse(File.ReadAllText(GameContent.RepoPath("reports", $"winning-line-{preset}.json"))));

        private static string Rounds(int n) => n == 0 ? "this round" : n == 1 ? "in 1 round" : $"in {n} rounds";

        [Test]
        public void TheHeaderNamesTheEngineAndTheContent()
        {
            Assert.That(DebugReport.Header(C), Is.EqualTo($"engine {GameStates.EngineVersion} · content {C.Hash}"));
        }

        [Test]
        public void TheWaveTimerSaysWhenTheNextWaveArrives()
        {
            var s = Settled("tech").Live;
            var waves = DebugReport.Waves(C, s);
            Assert.That(s.WavesSpawned, Is.EqualTo(1), "the opening brings the first wave");
            Assert.That(DebugReport.WaveCount(C, s, 1), Is.EqualTo($"Round {s.Round} · waves spawned 1/{waves.Count}"));
            Assert.That(DebugReport.NextWave(C, s, 1), Does.StartWith($"Next: wave 2 at round {waves[1].Round} ({Rounds(waves[1].Round - s.Round)}"));
            Assert.That(DebugReport.NextWave(C, s, waves.Count), Is.EqualTo("No waves left"));
            Assert.That(DebugReport.WaveList(C, s).ToList(), Has.Count.EqualTo(waves.Count));
            Assert.That(DebugReport.WaveList(C, s).First(), Does.StartWith($"W1 @ R{waves[0].Round}: {waves[0].Units[0].Def} {waves[0].Units[0].Tile}"));
        }

        [Test]
        public void SpawnMarksAreTheTilesOfTheWavesStillToCome()
        {
            var s = Settled("tech").Live;
            var waves = DebugReport.Waves(C, s);
            var expected = waves.Skip(1).SelectMany((w, i) => w.Units.Select(u => (u.Tile, i + 2, u.Def))).ToList();
            var marks = DebugReport.Spawns(C, s, 1).Select(m => (Tiles.Name(m.Tile), m.Wave, m.Def)).ToList();
            Assert.That(marks, Is.EqualTo(expected));
            Assert.That(marks, Is.Not.Empty);
            Assert.That(DebugReport.Spawns(C, s, waves.Count), Is.Empty);
        }

        [Test]
        public void AHazardsStateIsSpelledOutOnThePanelAndMarkedShortOnTheBoard()
        {
            var h = Settled("tech").Live.Hazards[0].Clone();
            h.State = HazardState.Ready;
            Assert.That((DebugReport.StateText(h), DebugReport.HazardMark(h)), Is.EqualTo(("ready", "ready")));
            h.State = HazardState.Primed;
            Assert.That((DebugReport.StateText(h), DebugReport.HazardMark(h)), Is.EqualTo(("primed", "PRIMED")));
            h.State = HazardState.Cooldown;
            h.Cooldown = 2;
            Assert.That((DebugReport.StateText(h), DebugReport.HazardMark(h)), Is.EqualTo(("cooldown 2", "cd2")));
        }

        [Test]
        public void ADecisionSaysWhichStepOfTheValueRuleTookItAndWhy()
        {
            var s = Settled("tech").Live;
            Assert.That(DebugReport.Decision(C, s, null), Is.EqualTo("No decision yet"));
            var hold = new HazardDecision { Round = 3, Stage = HazardStage.Prime, Result = HazardResult.Hold, Step = 1, Reason = "An elite would be hit" };
            hold.Victims.Add(new EntityRef(EntityKind.Unit, 0));
            Assert.That(DebugReport.Decision(C, s, hold), Is.EqualTo("R3 prime: hold, step 1. An elite would be hit. Would hit: You."));
            var fire = new HazardDecision { Round = 4, Stage = HazardStage.Fire, Result = HazardResult.Cancel, Reason = "Nothing to hit" };
            Assert.That(DebugReport.Decision(C, s, fire), Is.EqualTo("R4 fire: cancel. Nothing to hit."));
        }

        [Test]
        public void EveryDecisionInAWinningLineIsExplained()
        {
            var record = WinningLine("alien");
            var flow = new FightFlow(C, record.Loadout, record.Settings);
            flow.SetSpeed(FightSpeed.Skip);
            flow.Begin();
            var decided = 0;
            foreach (var cmd in record.Commands)
            {
                Assert.That(flow.Apply(cmd).Ok, Is.True);
                foreach (var h in flow.Live.Hazards.Where(x => x.Last != null))
                {
                    var text = DebugReport.Decision(C, flow.Live, h.Last);
                    Assert.That(text, Does.StartWith($"R{h.Last!.Round} {Names.Of(h.Last.Stage)}: {Names.Of(h.Last.Result)}"));
                    Assert.That(text, Does.Contain(h.Last.Reason));
                    if (h.Last.Step.HasValue) Assert.That(text, Does.Contain($"step {h.Last.Step}"));
                    decided++;
                }
            }
            Assert.That(decided, Is.GreaterThan(0), "the lab's hazards decided something in the fight");
        }

        [Test]
        public void TheFlamersScoredOptionsAreListedBestFirst()
        {
            var record = WinningLine("tech");
            var flow = new FightFlow(C, record.Loadout, record.Settings);
            flow.SetSpeed(FightSpeed.Skip);
            flow.Begin();
            List<ScoredOptions>? found = null;
            foreach (var cmd in record.Commands)
            {
                Assert.That(flow.Apply(cmd).Ok, Is.True);
                if (flow.Live.Phase != Phase.Player || flow.Live.Outcome != null) continue;
                found = DebugReport.Scored(Previews.ComputeIntents(C, flow.Live));
                if (found.Count > 0) break;
            }
            Assert.That(found, Is.Not.Null.And.Not.Empty, "a Flamer scores its options once it has arrived");
            var flamer = found!.First();
            Assert.That(flow.Live.Units.First(u => u.Id == flamer.Id).Def, Is.EqualTo(flamer.Def));
            var lines = DebugReport.OptionLines(flamer).ToList();
            Assert.That(lines, Has.Count.EqualTo(System.Math.Min(DebugReport.OptionsListed, flamer.Options.Count)));
            var best = flamer.Options.OrderByDescending(o => o.Score).First();
            Assert.That(lines[0], Is.EqualTo($"{Tiles.Name(best.Tile)} {DebugReport.Facing(best.Dir)}  score {best.Score} ({best.Steps} steps)"));
            Assert.That(DebugReport.Scored(null), Is.Empty);
        }

        [Test]
        public void ThePanelHasTheTimerEveryHazardAndTheChainRule()
        {
            var flow = Settled("tech");
            var s = flow.Live;
            var lines = DebugReport.Panel(C, s, s.Hazards, s.WavesSpawned, new List<ScoredOptions>(), flow.Session.Events);
            var text = lines.Select(l => l.Text).ToList();
            Assert.That(lines.Where(l => l.Style == "section").Select(l => l.Text), Is.EqualTo(new[] { "WAVE TIMER (HIDDEN FROM PLAYERS)", "HAZARDS", "CHAIN RULE" }));
            Assert.That(text, Has.Member(DebugReport.NextWave(C, s, s.WavesSpawned)));
            foreach (var h in s.Hazards) Assert.That(text, Has.Member($"{Words.HazardName(C, s, h.Id)}: {DebugReport.StateText(h)}"));
            Assert.That(text.Last(), Is.EqualTo("No suppressed triggers this fight"));
            Assert.That(DebugReport.Chain(new GameEvent[] { new ChainSuppressed(), new ChainSuppressed() }), Is.EqualTo("2 suppressed triggers this fight"));
        }

        [Test]
        public void StepModeHoldsEachFrameUntilAdvanced()
        {
            var flow = Settled("hybrid");
            flow.StepMode = true;
            var shown = 0;
            flow.FrameShown += (s, st) => shown++;
            Assert.That(flow.Apply(new EndTurnCommand()).Ok, Is.True);
            Assert.That(shown, Is.EqualTo(1));
            Assert.That(flow.Pending, Is.GreaterThan(2), "an end of turn plays several frames");
            for (var i = 0; i < 400; i++) flow.Tick(0.05f);
            Assert.That(flow.Holding, Is.True);
            Assert.That(shown, Is.EqualTo(1), "twenty seconds on, the frame is still held");
            Assert.That(flow.FrameTime, Is.EqualTo(flow.Stage.Hold), "held at its end");
            Assert.That(new ActionModel(C).Hint(flow), Does.StartWith("Step mode"));

            var pending = flow.Pending;
            flow.Advance();
            Assert.That(shown, Is.EqualTo(2));
            Assert.That(flow.Pending, Is.EqualTo(pending - 1));
            if (flow.Stage.Hold > 0f) Assert.That(flow.Holding, Is.False, "the next frame plays before it is held");

            // Shift+N runs through to the last frame before a phase starts: the frame after it starts one, or there is none.
            flow.Advance(true);
            if (flow.Busy && flow.Pending > 0)
            {
                flow.Advance();
                Assert.That(flow.FrameEvents.Any(e => e is PhaseStarted), Is.True);
            }

            // Turning it off lets the fight go on by itself.
            flow.StepMode = false;
            for (var i = 0; i < 2000 && flow.Busy; i++) flow.Tick(0.05f);
            Assert.That(flow.Busy, Is.False);
            Assert.That(flow.Shown, Is.SameAs(flow.Live));
        }

        [Test]
        public void AFightSteppedThroughLeavesTheSameLogAsOneSkipped()
        {
            var stepped = Settled("hybrid");
            stepped.StepMode = true;
            var skipped = Settled("hybrid");
            skipped.SetSpeed(FightSpeed.Skip);
            Assert.That(stepped.Apply(new EndTurnCommand()).Ok, Is.True);
            Assert.That(skipped.Apply(new EndTurnCommand()).Ok, Is.True);
            var presses = 0;
            while (stepped.Busy && presses < 500)
            {
                stepped.Tick(0.03f);
                stepped.Advance();
                presses++;
            }
            Assert.That(stepped.Busy, Is.False);
            Assert.That(stepped.Shown, Is.SameAs(stepped.Live));
            Assert.That(stepped.Log.Select(l => l.Text), Is.EqualTo(skipped.Log.Select(l => l.Text)));
        }

        [Test]
        public void DebugLinesAreWrittenOnlyWhenAskedFor()
        {
            var f = new EventFormatter(C);
            var held = new HazardHeld { Hazard = "panel", Step = 1, Reason = "An elite would be hit" };
            Assert.That(f.Format(held, null), Is.Null);
            var line = f.Format(held, null, true)!;
            Assert.That(line.Kind, Is.EqualTo(LogKind.Debug));
            Assert.That(line.Text, Does.StartWith("[debug] ").And.EndWith("holds (step 1: An elite would be hit)"));
            var cancelled = new HazardCancelled { Hazard = "panel", Reason = "Nothing to hit" };
            Assert.That(f.Format(cancelled, null)!.Text, Does.Not.Contain("["));
            Assert.That(f.Format(cancelled, null, true)!.Text, Does.EndWith(" [Nothing to hit]"));
            Assert.That(f.Format(new ChainSuppressed { Effect = "e", Source = "s", Reason = SuppressReason.Repeat }, null), Is.Null);
            Assert.That(f.Format(new ChainSuppressed { Effect = "e", Source = "s", Reason = SuppressReason.Repeat }, null, true)!.Text, Is.EqualTo("[debug] Chain suppressed: e from s (repeat)"));
            Assert.That(f.Format(new DebugEvent { Op = "setHp", Detail = "HP set" }, null)!.Text, Is.EqualTo("[debug] HP set"), "a debug command is always logged");

            // In a fight, the AI's decisions are logged with the overlay's log turned on.
            var flow = Settled("hybrid");
            flow.DebugLog = true;
            flow.SetSpeed(FightSpeed.Skip);
            Assert.That(flow.Apply(new EndTurnCommand()).Ok, Is.True);
            Assert.That(flow.Log.Any(l => l.Kind == LogKind.Debug), Is.True);
        }
    }
}
