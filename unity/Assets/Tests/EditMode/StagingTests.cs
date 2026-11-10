#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>
    /// The staging lays a frame's events out in time. These check the one promise the animation rests on: a cause is on screen
    /// before its effect, in every frame of every recorded fight as well as in the cases written out here.
    /// </summary>
    public class StagingTests
    {
        private static readonly Content C = GameContent.Load();
        private static readonly float Beat = (float)C.Rules.Ui.SecondsPerActor;
        private const float Slack = 0.0005f;

        private static readonly string[] RecordedFights =
        {
            "golden/alien.json", "golden/hybrid.json", "golden/tech.json",
            "reports/winning-line-alien.json", "reports/winning-line-hybrid.json", "reports/winning-line-tech.json",
        };

        private static List<(GameState Before, Frame Frame)> FramesOf(string path)
        {
            var raw = JsonIn.Parse(File.ReadAllText(GameContent.RepoPath(path.Split('/'))));
            var record = Records.CheckCompatible(C, raw["record"] ?? raw);
            var replay = Records.Replay(C, record, frames: true);
            var frames = new List<(GameState, Frame)>();
            var before = GameStates.CreateInitial(C, record.Loadout, record.Settings);
            foreach (var f in replay.Frames)
            {
                frames.Add((before, f));
                before = f.State;
            }
            return frames;
        }

        private static Cue CueOf(Stage stage, GameEvent e) => stage.Cues.Single(c => c.Kind == CueKind.Event && ReferenceEquals(c.Event, e));

        [TestCaseSource(nameof(RecordedFights))]
        public void InEveryRecordedFrameACauseComesBeforeItsEffect(string path)
        {
            var frames = FramesOf(path);
            Assert.That(frames.Count, Is.GreaterThan(50));
            var n = 0;
            foreach (var (before, frame) in frames)
            {
                n++;
                var where = $"{path} frame {n} ({frame.Label})";
                var stage = Staging.Plan(C, before, frame.Events);

                Assert.That(stage.Cues.Count(c => c.Kind == CueKind.Event), Is.EqualTo(frame.Events.Count), where + ": one cue for each event");
                var last = 0f;
                foreach (var cue in stage.Cues)
                {
                    Assert.That(cue.At, Is.GreaterThanOrEqualTo(last), where + ": cues are in time order");
                    Assert.That(cue.At + cue.Duration, Is.LessThanOrEqualTo(stage.Hold + Slack), where + $": {cue.Event.T} is over before the frame is");
                    last = cue.At;
                }

                ProjectileFired? shot = null;
                var moved = new Dictionary<int, float>();
                var blows = new Dictionary<EntityRef, float>();
                foreach (var e in frame.Events)
                {
                    var cue = CueOf(stage, e);
                    switch (e)
                    {
                        case UnitMoved m:
                            if (moved.TryGetValue(m.Id, out var arrived)) Assert.That(cue.At, Is.GreaterThanOrEqualTo(arrived - Slack), where + ": a second move waits for the first");
                            moved[m.Id] = cue.At + cue.Duration;
                            break;
                        case ProjectileFired p:
                            shot = p;
                            if (p.Source.Kind == SourceKind.Unit && moved.TryGetValue(p.Source.NumberId, out var stood))
                            {
                                Assert.That(cue.At, Is.GreaterThan(stood), where + ": a unit fires after it has arrived, not on the way");
                            }
                            break;
                        case DamageDealt d:
                            if (shot != null && shot.Hit == d.Target)
                            {
                                var flight = CueOf(stage, shot);
                                Assert.That(cue.At, Is.GreaterThanOrEqualTo(flight.At + flight.Duration - Slack), where + ": damage lands when the shot does");
                            }
                            if (d.Killed) blows[d.Target] = cue.At;
                            break;
                        case UnitDied u:
                            if (blows.TryGetValue(EntityRef.Unit(u.Id), out var blow)) Assert.That(cue.At, Is.EqualTo(blow).Within(Slack), where + ": a unit falls at the blow that killed it");
                            break;
                        case ObjectDestroyed o:
                            if (blows.TryGetValue(EntityRef.Object(o.Id), out var broke)) Assert.That(cue.At, Is.EqualTo(broke).Within(Slack), where + ": an object breaks at the blow that broke it");
                            break;
                    }
                }

                foreach (var windup in stage.Cues.Where(c => c.Kind == CueKind.Windup))
                {
                    var release = stage.Cues.First(c => c.Kind != CueKind.Windup && c.Index == windup.Index);
                    Assert.That(release.At, Is.EqualTo(windup.At + windup.Duration).Within(Slack), where + ": the wind-up ends as the unit acts");
                }
            }
        }

        [Test]
        public void TheRecordedFightsUseEveryKindOfStaging()
        {
            // If the replays stopped exercising a path, the test above would pass by seeing nothing.
            var stages = RecordedFights.SelectMany(FramesOf).Select(f => Staging.Plan(C, f.Before, f.Frame.Events)).ToList();
            Assert.That(stages.Any(s => s.Cues.Any(c => c.Kind == CueKind.Strike)), Is.True, "a melee strike");
            Assert.That(stages.Any(s => s.Cues.Any(c => c.Kind == CueKind.Windup && c.Event is UnitMoved)), Is.True, "a charge or a lunge");
            Assert.That(stages.Any(s => s.Has<AreaEffect>() && s.Has<UnitDied>()), Is.True, "a blast that kills");
            Assert.That(stages.Any(s => s.Has<ChainArc>()), Is.True, "a chain arc");
            Assert.That(stages.Any(s => s.Has<Charged>()), Is.True, "a charge");
        }

        // ------------------------------------------------------------------ cases written out

        private static GameState Fight()
        {
            var p = C.Presets.First(x => x.Id == "hybrid");
            var flow = new FightFlow(C, new Loadout(p.Abilities, p.Upgrades), new FightSettings());
            flow.Begin();
            flow.Skip();
            return flow.Live;
        }

        private static SourceRef By(Unit u, string via) => new SourceRef(u.Team == Team.Enemy ? SourceSide.Enemy : SourceSide.Player, u.Team == Team.Enemy ? SourceCategory.Enemy : SourceCategory.Player, SourceKind.Unit, u.Id, u.Def, via);

        private static ProjectileFired ShotAt(Unit from, Unit to) => new ProjectileFired { Source = By(from, "shot"), From = from.Pos, Path = new List<Pos> { to.Pos }, Hit = EntityRef.Unit(to.Id), HitPos = to.Pos, Reflected = false };

        private static DamageDealt Hurt(Unit from, Unit to, int amount, bool killed = false, string via = "shot") => new DamageDealt
        {
            Target = EntityRef.Unit(to.Id), TargetDef = to.Def, TargetSide = to.Team, Pos = to.Pos, Source = By(from, via), Raw = amount, Amount = amount, HpLost = amount, HpAfter = killed ? 0 : to.Hp - amount, Killed = killed,
        };

        [Test]
        public void AShotIsGatheredFiredAndThenLands()
        {
            var s = Fight();
            var player = s.Units.First(u => u.Id == 0);
            var guard = s.Units.First(u => u.Team == Team.Enemy);
            var shot = ShotAt(guard, player);
            var hurt = Hurt(guard, player, 2);
            var stage = Staging.Plan(C, s, new GameEvent[] { shot, hurt });

            var windup = stage.Cues.Single(c => c.Kind == CueKind.Windup);
            var fired = CueOf(stage, shot);
            var landed = CueOf(stage, hurt);
            Assert.That(windup.Actor, Is.EqualTo(guard.Id));
            Assert.That(windup.At, Is.EqualTo(0f));
            Assert.That(fired.At, Is.EqualTo(Staging.Windup * Beat).Within(Slack));
            Assert.That(fired.Duration, Is.GreaterThan(0f));
            Assert.That(landed.At, Is.EqualTo(fired.At + fired.Duration).Within(Slack));
            Assert.That(landed.HasOrigin, Is.True, "the blow knows where it came from, so the target is knocked away from it");
            Assert.That(new Pos((int)landed.OriginX, (int)landed.OriginY), Is.EqualTo(guard.Pos));
            Assert.That(stage.Hold, Is.EqualTo(landed.At + Staging.HitGap * Beat).Within(Slack), "the reaction is seen before the next unit acts");
        }

        [Test]
        public void AShotAcrossTheRoomFliesLongerThanOneNextDoor()
        {
            var s = Fight();
            var player = s.Units.First(u => u.Id == 0);
            var near = new Unit { Id = 90, Def = "guard", Team = Team.Enemy, Kind = UnitKind.Enemy, Pos = new Pos(player.Pos.X + 1, player.Pos.Y), Hp = 6, MaxHp = 6 };
            var far = new Unit { Id = 91, Def = "guard", Team = Team.Enemy, Kind = UnitKind.Enemy, Pos = new Pos(player.Pos.X + 6, player.Pos.Y), Hp = 6, MaxHp = 6 };
            var a = ShotAt(player, near);
            var b = ShotAt(player, far);
            Assert.That(CueOf(Staging.Plan(C, s, new GameEvent[] { b }), b).Duration, Is.GreaterThan(CueOf(Staging.Plan(C, s, new GameEvent[] { a }), a).Duration));
        }

        [Test]
        public void ABlowFromTheNextTileWithNothingInFlightIsAStrike()
        {
            var s = Fight();
            var biter = new Unit { Id = 90, Def = "hatchling", Team = Team.Player, Kind = UnitKind.Minion, Pos = new Pos(4, 4), Hp = 2, MaxHp = 2 };
            var prey = new Unit { Id = 91, Def = "guard", Team = Team.Enemy, Kind = UnitKind.Enemy, Pos = new Pos(5, 4), Hp = 6, MaxHp = 6 };
            s = s.Clone();
            s.Units.Add(biter);
            s.Units.Add(prey);
            var bite = Hurt(biter, prey, 1, via: "bite");
            var stage = Staging.Plan(C, s, new GameEvent[] { bite });

            var strike = stage.Cues.Single(c => c.Kind == CueKind.Strike);
            Assert.That(strike.Actor, Is.EqualTo(biter.Id));
            Assert.That(strike.Toward, Is.EqualTo(prey.Pos));
            Assert.That(CueOf(stage, bite).At, Is.EqualTo(strike.At + strike.Duration).Within(Slack), "the blow lands at the end of the lunge");
        }

        [Test]
        public void StatusDamageHasNoStrikeAndTicksDownALine()
        {
            var s = Fight();
            var guards = s.Units.Where(u => u.Team == Team.Enemy).Take(2).ToList();
            var poison = new SourceRef(SourceSide.Player, SourceCategory.Player, SourceKind.Unit, 0, "player", "poison");
            DamageDealt Tick(Unit u) => new DamageDealt { Target = EntityRef.Unit(u.Id), TargetDef = u.Def, TargetSide = u.Team, Pos = u.Pos, Source = poison, Amount = 1, HpLost = 1, HpAfter = u.Hp - 1 };
            var first = Tick(guards[0]);
            var second = Tick(guards[1]);
            var stage = Staging.Plan(C, s, new GameEvent[] { first, second });
            Assert.That(stage.Cues.Any(c => c.Kind != CueKind.Event), Is.False, "the player is nowhere near: nobody lunges");
            Assert.That(CueOf(stage, second).At, Is.GreaterThan(CueOf(stage, first).At));
            Assert.That(CueOf(stage, first).HasOrigin, Is.False);
        }

        [Test]
        public void ABlastReachesFartherTilesLaterAndAUnitFallsAtItsOwnBlow()
        {
            var s = Fight();
            var guards = s.Units.Where(u => u.Team == Team.Enemy).Take(2).ToList();
            var mine = new SourceRef(SourceSide.Player, SourceCategory.Player, SourceKind.Object, 7, "mine", "mine");
            var centre = guards[0].Pos;
            var tiles = new List<Pos> { centre };
            var blast = new AreaEffect { Kind = AreaKind.Blast, Tiles = tiles, Source = mine };
            DamageDealt Blown(Unit u, bool killed) => new DamageDealt { Target = EntityRef.Unit(u.Id), TargetDef = u.Def, TargetSide = u.Team, Pos = u.Pos, Source = mine, Amount = 3, HpLost = 3, HpAfter = killed ? 0 : 3, Killed = killed };
            var near = Blown(guards[0], false);
            var far = Blown(guards[1], true);
            var died = new UnitDied { Id = guards[1].Id, Def = guards[1].Def, Team = Team.Enemy, Kind = UnitKind.Enemy, Pos = guards[1].Pos };
            var stage = Staging.Plan(C, s, new GameEvent[] { blast, near, far, died });

            Assert.That(CueOf(stage, near).At, Is.EqualTo(CueOf(stage, blast).At).Within(Slack), "the tile at the middle goes with the blast");
            Assert.That(CueOf(stage, far).At, Is.GreaterThan(CueOf(stage, near).At), "the other unit is further out");
            Assert.That(CueOf(stage, died).At, Is.EqualTo(CueOf(stage, far).At).Within(Slack));
            Assert.That(CueOf(stage, died).HasOrigin, Is.True, "the body is thrown away from the blast");
        }

        [Test]
        public void AFrameWithNothingToShowHoldsNothingAndABannerHoldsBriefly()
        {
            var s = Fight();
            Assert.That(Staging.Plan(C, s, new GameEvent[0]).Hold, Is.EqualTo(0f));
            Assert.That(Staging.Plan(C, s, new GameEvent[] { new AiDecision { Id = 1, Def = "guard", Summary = "waits" } }).Hold, Is.EqualTo(0f));
            var banner = Staging.Plan(C, s, new GameEvent[] { new PhaseStarted { Round = 1, Phase = Phase.Enemy } });
            Assert.That(banner.Hold, Is.EqualTo(Staging.PhaseHold * Beat).Within(Slack));
        }

        [Test]
        public void TheFirstActionOfAPhaseWaitsForItsBanner()
        {
            var s = Fight();
            var player = s.Units.First(u => u.Id == 0);
            var guard = s.Units.First(u => u.Team == Team.Enemy);
            var shot = ShotAt(guard, player);
            var stage = Staging.Plan(C, s, new GameEvent[] { new PhaseStarted { Round = 1, Phase = Phase.Enemy }, shot });
            Assert.That(stage.Cues.Single(c => c.Kind == CueKind.Windup).At, Is.EqualTo(Staging.PhasePause * Beat).Within(Slack));
        }

        [Test]
        public void AWaveFallsInOneAfterAnother()
        {
            var s = GameStates.CreateInitial(C, new Loadout(new List<string>(), new List<string>()));
            var spawns = Enumerable.Range(1, 3).Select(i => new UnitSpawned { Unit = new Unit { Id = i, Def = "guard", Team = Team.Enemy, Kind = UnitKind.Enemy, Pos = new Pos(i, 1), Hp = 6, MaxHp = 6 }, Cause = "wave 1" }).ToList();
            var events = spawns.Cast<GameEvent>().Append(new WaveSpawned { Wave = 1, Units = new List<int> { 1, 2, 3 } }).ToList();
            var stage = Staging.Plan(C, s, events);
            var falls = spawns.Select(e => CueOf(stage, e)).ToList();
            Assert.That(falls[1].At, Is.EqualTo(falls[0].At + Staging.WaveStagger * Beat).Within(Slack));
            Assert.That(falls[2].At, Is.EqualTo(falls[1].At + Staging.WaveStagger * Beat).Within(Slack));
            Assert.That(falls.All(c => c.Duration > 0f), Is.True);
            Assert.That(CueOf(stage, events.Last()).At, Is.EqualTo(falls[0].At), "the alarm goes with the first of them");
            Assert.That(stage.Hold, Is.GreaterThan(falls[2].At + falls[2].Duration), "the last has landed before anything else starts");
        }

        [Test]
        public void HeavierUnitsTakeLongerOverAStep()
        {
            var warden = new Unit { Id = 1, Def = "warden", MaxHp = C.Units["warden"].Hp, Armor = C.Units["warden"].Armor };
            var hatchling = new Unit { Id = 2, Def = "hatchling", MaxHp = C.Units["hatchling"].Hp };
            var player = new Unit { Id = 0, Def = "player", MaxHp = C.Units["player"].Hp, Armor = C.Units["player"].Armor };
            Assert.That(Heft.Of(C, player), Is.EqualTo(1f));
            Assert.That(Heft.Of(C, warden), Is.GreaterThan(Heft.Of(C, player)));
            Assert.That(Heft.Of(C, hatchling), Is.LessThan(Heft.Of(C, player)));
            Assert.That(Staging.StepFor(Heft.Of(C, warden)), Is.GreaterThan(Staging.StepFor(Heft.Of(C, hatchling))));
        }

        // ------------------------------------------------------------------ the clock and the queue

        [Test]
        public void ABlowStopsTheBoardClockAndTheLongestStopWins()
        {
            var clock = new BoardClock();
            clock.Freeze(0.05f);
            clock.Freeze(0.03f);
            Assert.That(clock.Step(0.02f), Is.EqualTo(0f));
            Assert.That(clock.Step(0.02f), Is.EqualTo(0f));
            Assert.That(clock.Step(0.02f), Is.EqualTo(0.01f).Within(0.0001f), "what is left of the step after the stop still counts");
            Assert.That(clock.Step(0.02f), Is.EqualTo(0.02f));
        }

        [Test]
        public void BlowsOneAfterAnotherShareOneStop()
        {
            // A blast reaches three units a few hundredths apart: the board stops once, for the longest of them.
            var clock = new BoardClock();
            var stopped = 0f;
            float Run(float seconds)
            {
                var ran = 0f;
                for (var t = 0f; t < seconds - 0.0001f; t += 0.01f) ran += clock.Step(0.01f);
                stopped += seconds - ran;
                return ran;
            }
            clock.Freeze(0.07f);
            Run(0.1f);
            clock.Freeze(0.09f);
            Run(0.1f);
            clock.Freeze(0.06f);
            Run(0.1f);
            Assert.That(stopped, Is.EqualTo(0.09f).Within(0.001f));

            Run(0.3f);
            clock.Freeze(0.05f);
            Run(0.1f);
            Assert.That(stopped, Is.EqualTo(0.14f).Within(0.001f), "a blow after a pause is a new one");
        }

        [Test]
        public void TheSlowedClockEasesBackToFullSpeed()
        {
            var clock = new BoardClock();
            clock.Slow(0.25f, 0.9f);
            Assert.That(clock.Step(0.1f), Is.EqualTo(0.025f).Within(0.0001f));
            var total = 0f;
            for (var i = 0; i < 20; i++) total += clock.Step(0.05f);
            Assert.That(clock.Step(0.05f), Is.EqualTo(0.05f), "and it is over");
            Assert.That(total, Is.GreaterThan(20 * 0.05f * 0.25f));
        }

        [Test]
        public void AFrameHoldsTheQueueForAsLongAsItsStage()
        {
            var p = C.Presets.First(x => x.Id == "hybrid");
            var flow = new FightFlow(C, new Loadout(p.Abilities, p.Upgrades), new FightSettings());
            flow.Begin();
            flow.Skip();
            var model = new ActionModel(C);
            model.Refresh(flow.Live, flow.Idle);
            var shown = new List<Stage>();
            flow.FrameShown += (s, stage) => shown.Add(stage);
            var move = model.Moves.OrderByDescending(m => m.Path.Count).First();
            Assert.That(flow.Apply(Actions.MoveCommandFor(move.Path)).Ok, Is.True);

            Assert.That(shown.Count, Is.EqualTo(1));
            Assert.That(flow.Stage, Is.SameAs(shown[0]));
            Assert.That(flow.Stage.Hold, Is.GreaterThan(move.Path.Count * Staging.Step * Beat * 0.99f), "a longer walk takes longer");
            flow.Tick(flow.Stage.Hold * 0.5f);
            Assert.That(flow.Busy, Is.True);
            Assert.That(flow.FrameTime, Is.EqualTo(shown[0].Hold * 0.5f).Within(Slack));
            flow.Tick(shown[0].Hold);
            Assert.That(flow.Busy, Is.False);
        }

        [Test]
        public void TheEntranceHoldsTheFirstFrameBackAndASkipCutsIt()
        {
            var p = C.Presets.First(x => x.Id == "hybrid");
            var flow = new FightFlow(C, new Loadout(p.Abilities, p.Upgrades), new FightSettings());
            var shown = 0;
            flow.FrameShown += (s, stage) => shown++;
            flow.Begin(1f);
            Assert.That(flow.Busy, Is.True);
            flow.Tick(0.6f);
            Assert.That(shown, Is.EqualTo(0), "nothing arrives during the lead");
            flow.Tick(0.6f);
            Assert.That(shown, Is.GreaterThan(0));

            var cut = new FightFlow(C, new Loadout(p.Abilities, p.Upgrades), new FightSettings());
            cut.Begin(1f);
            cut.Skip();
            Assert.That(cut.Busy, Is.False);
            Assert.That(cut.Idle, Is.True);
        }

        [Test]
        public void TheFastSpeedDoublesTheClockInsteadOfShorteningFrames()
        {
            var p = C.Presets.First(x => x.Id == "hybrid");
            var flow = new FightFlow(C, new Loadout(p.Abilities, p.Upgrades), new FightSettings());
            Assert.That(flow.TimeScale, Is.EqualTo(1f));
            flow.SetSpeed(FightSpeed.Fast);
            Assert.That(flow.TimeScale, Is.EqualTo(2f));
        }
    }
}
