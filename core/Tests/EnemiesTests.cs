#nullable enable
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class EnemiesTests
    {
        [Test]
        public void Wave1SpawnsAtTheStartOfRound1WithIdsInTableOrder()
        {
            var g = Engine.NewGame(C, LoadoutOf("sidearm"));
            Assert.That(g.State.Units.Skip(1).Select(u => $"{u.Id}:{u.Def}@{Name(u.Pos)}"), Is.EqualTo(new[] { "1:guard@C3", "2:guard@G3", "3:guard@B2", "4:medic@E2" }));
        }

        [Test]
        public void AnOccupiedSpawnTileMovesTheUnitToTheNearestFreeTile()
        {
            var s = Scenario(new ScenarioSpec { Player = "H8", Round = 3, WavesSpawned = 1, Objects = { Shield("A3") } });
            var r = Run(s, EndTurn);
            var wave = EventsOf<WaveSpawned>(r.Events)[0];
            var placed = wave.Units.Select(id => (UnitById(r.State, id) ?? EventsOf<UnitSpawned>(r.Events).First(e => e.Unit.Id == id).Unit).Def);
            Assert.That(placed, Is.EqualTo(new[] { "guard", "guard", "flamer" }));
            Assert.That(EventsOf<UnitSpawned>(r.Events).Select(e => Name(e.Unit.Pos)), Is.EqualTo(new[] { "A2", "A1", "H3" }));
        }

        [Test]
        public void AWaveDealsNoDamageInItsArrivalRound()
        {
            var g = Engine.NewGame(C, LoadoutOf("sidearm"));
            var r = Run(g.State, EndTurn);
            Assert.That(EventsOf<DamageDealt>(r.Events), Is.Empty);
            Assert.That(EventsOf<UnitMoved>(r.Events), Is.Not.Empty);
        }

        [Test]
        public void WithoutTheArrivalRoundTheOpeningRoundAlreadyHurts()
        {
            var c = ContentWith(("rules.arrivalRound", "none"));
            var g = Engine.NewGame(c, LoadoutOf("sidearm"));
            var r = Run(g.State, EndTurn, c);
            Assert.That(EventsOf<DamageDealt>(r.Events).Any(e => e.Target.Id == 0), Is.True);
        }

        [Test]
        public void TheArrivalRoundCanBeLimitedToWave1()
        {
            var c = ContentWith(("rules.arrivalRound", "first"));
            var s = Scenario(new ScenarioSpec { Content = c, Player = "H8", Round = 3, WavesSpawned = 1 });
            var r = Run(s, EndTurn, c);
            Assert.That(EventsOf<UnitSpawned>(r.Events).All(e => e.Unit.ArrivalRound == null), Is.True);
        }

        [Test]
        public void WithEarlyWaveWhenEmptyAnEmptyRoomBringsTheNextWaveForward()
        {
            var c = ContentWith(("rules.earlyWaveWhenEmpty", true));
            var s = Scenario(new ScenarioSpec { Content = c, Player = "H8", Round = 2, WavesSpawned = 1 });
            var r = Run(s, EndTurn, c);
            Assert.That(EventsOf<WaveSpawned>(r.Events).Select(e => e.Wave), Is.EqualTo(new[] { 2 }));
            Assert.That(r.State.Round, Is.EqualTo(3));
        }

        [Test]
        public void AGuardNeedsAClearShot()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5"), U("guard", "B6") } });
            InWorld(s, w => Assert.That(EnemyAi.GuardShotFrom(w, w.Unit(1)!, T("B5")), Is.Null));
            // A hatchling in the line is itself the nearest target: the Guard still fires.
            var s2 = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5"), U("hatchling", "B6") } });
            InWorld(s2, w => Assert.That(EnemyAi.GuardShotFrom(w, w.Unit(1)!, T("B5"))?.Target.Id, Is.EqualTo(2)));
            // A shield segment in the line does not stop the Guard firing at the player; the shot hits the shield.
            var s3 = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5") }, Objects = { Shield("B6") } });
            var run = InWorld(s3, w =>
            {
                Assert.That(EnemyAi.GuardShotFrom(w, w.Unit(1)!, T("B5"))?.Target.Id, Is.EqualTo(0));
                EnemyAi.GuardTurn(w, w.Unit(1)!, false);
            });
            Assert.That(EventsOf<DamageDealt>(run.Events)[0].Target.Kind, Is.EqualTo(EntityKind.Object));
        }

        [Test]
        public void AGuardMovesToTheReachableTileThatGivesAShotInTheFewestSteps()
        {
            var s = Scenario(new ScenarioSpec { Player = "E8", Units = { U("guard", "A1") } });
            var run = InWorld(s, w => EnemyAi.GuardTurn(w, w.Unit(1)!, false));
            Assert.That(Name(UnitById(run.S, 1)!.Pos), Is.EqualTo("B3"));
            Assert.That(UnitById(run.S, 0)!.Hp, Is.EqualTo(5));
        }

        [Test]
        public void WhenNoTileGivesAShotAGuardClosesInOnTheNearestPlayerSideUnit()
        {
            var s = Scenario(new ScenarioSpec { Player = "H8", Units = { U("guard", "A1") } });
            var run = InWorld(s, w => EnemyAi.GuardTurn(w, w.Unit(1)!, false));
            Assert.That(UnitById(run.S, 1)!.Pos, Is.Not.EqualTo(T("A1")));
            Assert.That(UnitById(run.S, 0)!.Hp, Is.EqualTo(8));
        }

        [Test]
        public void InItsArrivalRoundAGuardWalksToItsFiringTileButDoesNotShoot()
        {
            var s = Scenario(new ScenarioSpec { Player = "E8", Units = { U("guard", "A1", arrival: true) } });
            var run = InWorld(s, w => EnemyAi.GuardTurn(w, w.Unit(1)!, true));
            Assert.That(Name(UnitById(run.S, 1)!.Pos), Is.EqualTo("B3"));
            Assert.That(EventsOf<ProjectileFired>(run.Events), Is.Empty);
        }

        [Test]
        public void TheMedicHealsTheDamagedAllyWithTheLowestHp()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("medic", "E2"), U("guard", "E3", hp: 3), U("guard", "F3", hp: 4) } });
            var run = InWorld(s, w => EnemyAi.MedicTurn(w, w.Unit(1)!, false));
            Assert.That(UnitById(run.S, 2)!.Hp, Is.EqualTo(5));
            Assert.That(UnitById(run.S, 3)!.Hp, Is.EqualTo(4));
        }

        [Test]
        public void TheMedicPrefersATileAtLeastFourFromTheNearestPlayerSideUnit()
        {
            var s = Scenario(new ScenarioSpec { Player = "E7", Units = { U("medic", "E4"), U("guard", "E2", hp: 3) } });
            var run = InWorld(s, w => EnemyAi.MedicTurn(w, w.Unit(1)!, false));
            var m = UnitById(run.S, 1)!;
            Assert.That(Grid.Cheb(m.Pos, new Pos(4, 6)), Is.GreaterThanOrEqualTo(4));
            Assert.That(UnitById(run.S, 2)!.Hp, Is.EqualTo(5));
        }

        [Test]
        public void WithNobodyHurtTheMedicFollowsTheNearestGuard()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("medic", "H1"), U("guard", "C3") } });
            var run = InWorld(s, w => EnemyAi.MedicTurn(w, w.Unit(1)!, false));
            Assert.That(UnitById(run.S, 1)!.Pos, Is.Not.EqualTo(T("H1")));
        }

        [Test]
        public void TheFlamerScoresPerPlayerSideUnitHitBonusForThePlayerAndPenaltyPerAlly()
        {
            var s = Scenario(new ScenarioSpec { Player = "E4", Units = { U("flamer", "E2"), U("guard", "E3"), U("hatchling", "E5") } });
            InWorld(s, w =>
            {
                var f = w.Unit(1)!;
                var south = EnemyAi.FlamerOptions(w, f, new[] { f.Pos }, _ => 0).First(o => o.Dir.Y == 1);
                Assert.That(south.Score, Is.EqualTo(-3 + 6 + 2));
                Assert.That(south.Hits, Is.EqualTo(new[] { 0, 2, 3 }));
            });
        }

        [Test]
        public void TheFlamerTakesTheHighestPositiveScoreHittingItsOwnAllyIfThatStillScoresBest()
        {
            var s = Scenario(new ScenarioSpec { Player = "E4", Units = { U("flamer", "E2"), U("guard", "E3"), U("hatchling", "E5") } });
            var run = InWorld(s, w => EnemyAi.FlamerTurn(w, w.Unit(1)!, false));
            Assert.That(EventsOf<DamageDealt>(run.Events).Select(e => new[] { e.Target.Id, e.Amount }), Is.EqualTo(new[] { new[] { 0, 3 }, new[] { 2, 3 }, new[] { 3, 3 } }));
        }

        [Test]
        public void WithNoPositiveOptionTheFlamerMovesTowardTheNearestPlayerSideUnit()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("flamer", "H1") } });
            var run = InWorld(s, w => EnemyAi.FlamerTurn(w, w.Unit(1)!, false));
            Assert.That(EventsOf<AreaEffect>(run.Events), Is.Empty);
            Assert.That(UnitById(run.S, 1)!.Pos, Is.Not.EqualTo(T("H1")));
        }

        [Test]
        public void WallsAndPillarsEndTheSweepLine()
        {
            var s = Scenario(new ScenarioSpec { Player = "B5", Units = { U("flamer", "B3") } });
            InWorld(s, w =>
            {
                var opts = EnemyAi.FlamerOptions(w, w.Unit(1)!, new[] { T("B3") }, _ => 0);
                Assert.That(opts.First(o => o.Dir.Y == 1).Hits, Is.Empty); // B4 pillar blocks the line to B5
            });
        }

        [Test]
        public void TheWardenChargesAndStopsAgainstTheFirstBodyInTheLane()
        {
            var s = Scenario(new ScenarioSpec { Player = "D7", Units = { U("warden", "D2"), U("hatchling", "D5") } });
            var run = InWorld(s, w => EnemyAi.WardenTurn(w, w.Unit(1)!, false));
            Assert.That(Name(UnitById(run.S, 1)!.Pos), Is.EqualTo("D4"));
            Assert.That(EventsOf<Charged>(run.Events)[0].Hit, Is.EqualTo(EntityRef.Unit(2)));
            Assert.That(UnitById(run.S, 2), Is.Null);
            Assert.That(UnitById(run.S, 0)!.Hp, Is.EqualTo(8));
        }

        [Test]
        public void TheWardenNeedsToMoveAtLeastOneTileAndAnAllyInTheLaneBlocksIt()
        {
            var blocked = Scenario(new ScenarioSpec { Player = "D7", Units = { U("warden", "D2"), U("hatchling", "D3") } });
            InWorld(blocked, w => Assert.That(EnemyAi.ChargePlans(w, w.Unit(1)!, T("D2")).Where(p => p.Dir.Y == 1), Is.Empty));
            var ally = Scenario(new ScenarioSpec { Player = "D6", Units = { U("warden", "D2"), U("guard", "D4") } });
            InWorld(ally, w => Assert.That(EnemyAi.ChargePlans(w, w.Unit(1)!, T("D2")), Is.Empty));
        }

        [Test]
        public void TheWardenChargesATargetFiveTilesAwayButNotSix()
        {
            var five = Scenario(new ScenarioSpec { Player = "D7", Units = { U("warden", "D2") } });
            InWorld(five, w => Assert.That(EnemyAi.ChargePlans(w, w.Unit(1)!, T("D2")), Has.Count.EqualTo(1)));
            var six = Scenario(new ScenarioSpec { Player = "D8", Units = { U("warden", "D2") } });
            InWorld(six, w => Assert.That(EnemyAi.ChargePlans(w, w.Unit(1)!, T("D2")), Is.Empty));
        }

        [Test]
        public void TheWardenMeleesThePlayerWhenAdjacentAndNoChargeIsAvailable()
        {
            var s = Scenario(new ScenarioSpec { Player = "D3", Units = { U("warden", "D2") } });
            var run = InWorld(s, w => EnemyAi.WardenTurn(w, w.Unit(1)!, false));
            Assert.That(UnitById(run.S, 0)!.Hp, Is.EqualTo(5));
        }

        [Test]
        public void TheWardenPrefersAnApproachTileThatLinesUpAChargeLane()
        {
            // E4 would be closer, but G2 and D5 line up a lane at the player on G5; G2 wins on the lower row.
            var s = Scenario(new ScenarioSpec { Player = "G5", Units = { U("warden", "D2") } });
            var run = InWorld(s, w => EnemyAi.WardenTurn(w, w.Unit(1)!, false));
            Assert.That(Name(UnitById(run.S, 1)!.Pos), Is.EqualTo("G2"));
            Assert.That(UnitById(run.S, 0)!.Hp, Is.EqualTo(8));
            // Next round it charges down the lane.
            var next = InWorld(run.S, w => EnemyAi.WardenTurn(w, w.Unit(1)!, false));
            Assert.That(EventsOf<Charged>(next.Events), Has.Count.EqualTo(1));
            Assert.That(UnitById(next.S, 0)!.Hp, Is.EqualTo(4));
        }

        [Test]
        public void ARingOfHatchlingsCannotStallTheWarden()
        {
            var s = Scenario(new ScenarioSpec { Player = "H8", Units = { U("warden", "A1"), U("hatchling", "B1"), U("hatchling", "A2", hp: 1) } });
            var run = InWorld(s, w =>
            {
                Assert.That(Pathing.Reachable(w, w.Unit(1)!, 3).Dests, Has.Count.EqualTo(1));
                EnemyAi.WardenTurn(w, w.Unit(1)!, false);
            });
            Assert.That(EventsOf<DamageDealt>(run.Events)[0].Target.Id, Is.EqualTo(3)); // lowest HP adjacent minion
        }

        [Test]
        public void PurgeClearsPoisonStartsCooldownAndBlocksSpreadForTheRestOfTheRound()
        {
            var s = Scenario(new ScenarioSpec { Player = "H8", Units = { U("warden", "A1", statuses: PoisonedFor(3)), U("guard", "B1", statuses: PoisonedFor(3)) } });
            // Keep both in place for the test.
            foreach (var u in s.Units.Skip(1)) u.Statuses.Pinned = new TimedStatus(1, Src("spore_pod"));
            var r = Run(s, EndTurn);
            var wd = UnitById(r.State, 1)!;
            Assert.That(EventsOf<Purged>(r.Events), Has.Count.EqualTo(1));
            Assert.That(wd.Statuses.Poison, Is.Null); // the poisoned Guard next to it did not reinfect it
            Assert.That(wd.Cooldowns["purge"], Is.EqualTo(1));
            // Next round it can be reinfected by spread, and Purge is still cooling down.
            r = Run(r.State, EndTurn);
            wd = UnitById(r.State, 1)!;
            Assert.That(EventsOf<Purged>(r.Events), Is.Empty);
            Assert.That(wd.Statuses.Poison, Is.Not.Null);
            r = Run(r.State, EndTurn);
            Assert.That(EventsOf<Purged>(r.Events), Has.Count.EqualTo(1));
        }
    }
}
