#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class AlienTests
    {
        [Test]
        public void LungeLeapsOverUnitsThenMayHitAnAdjacentUnit()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "C8"), U("guard", "E7") } });
            var lands = Action(C, s, "lunge").Options.Select(o => Name(o.Target)).Distinct().OrderBy(x => x, System.StringComparer.Ordinal);
            Assert.That(lands, Is.EqualTo(new[] { "A8", "B5", "B6", "B7", "D8", "E8" }));
            var r = Run(s, Ability("lunge", "D8", "E7"));
            Assert.That(Name(UnitById(r.State, 0)!.Pos), Is.EqualTo("D8"));
            Assert.That(UnitById(r.State, 2)!.Hp, Is.EqualTo(4));
        }

        [Test]
        public void LungeCannotLeapOverPillarsWallsOrShieldsAndCanBeUsedPurelyToMove()
        {
            var s = Scenario(new ScenarioSpec { Player = "B7", Objects = { Shield("C7") } });
            var lands = Action(C, s, "lunge").Options.Select(o => Name(o.Target)).ToList();
            Assert.That(lands, Does.Not.Contain("D7"));
            Assert.That(lands, Does.Not.Contain("B4"));
            Assert.That(lands, Does.Contain("B5"));
            var r = Run(s, Ability("lunge", "B5", null));
            Assert.That(Name(UnitById(r.State, 0)!.Pos), Is.EqualTo("B5"));
        }

        [Test]
        public void SporePodPoisonsEveryUnitInTheAreaFriendOrFoeForThreeRounds()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("guard", "D3"), Dummy("E4"), U("medic", "F5") } });
            var r = Run(s, Ability("spore_pod", "E4"));
            Assert.That(EventsOf<StatusApplied>(r.Events).Select(e => new[] { e.Target, e.Value }), Is.EqualTo(new[] { new[] { 1, 3 }, new[] { 2, 3 }, new[] { 3, 3 } }));
        }

        [Test]
        public void PoisonSpreadsToUpToThreeNewUnitsARoundInIdOrderAtEqualDuration()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { Dummy("D7", statuses: PoisonedFor(3)), Dummy("C7"), Dummy("E7"), Dummy("C8"), Dummy("D8"), Dummy("E8") } });
            var run = InWorld(s, EnvironmentRules.StatusTickStep);
            var spread = EventsOf<StatusApplied>(run.Events).Where(e => e.Spread);
            Assert.That(spread.Select(e => new[] { e.Target, e.Value }), Is.EqualTo(new[] { new[] { 2, 2 }, new[] { 3, 2 }, new[] { 4, 2 } }));
        }

        [Test]
        public void PoisonInfectsInOrderOfTheDurationACandidateWouldInheritHighestFirst()
        {
            var c = ContentWith(("rules.statuses.poison.spreadCap", 1));
            var s = Scenario(new ScenarioSpec { Content = c, Player = "H8", Units = { Dummy("A7", statuses: PoisonedFor(1)), Dummy("A8"), Dummy("E7", statuses: PoisonedFor(3)), Dummy("E8") } });
            var run = InWorld(s, EnvironmentRules.SpreadPoison, c);
            Assert.That(EventsOf<StatusApplied>(run.Events).Select(e => new[] { e.Target, e.Value }), Is.EqualTo(new[] { new[] { 4, 3 } }));
        }

        [Test]
        public void PoisonDiesOutInACluster()
        {
            var s = Scenario(new ScenarioSpec { Player = "H8", Units = { Dummy("A7", statuses: PoisonedFor(3)), Dummy("B7"), Dummy("C7"), Dummy("D7") } });
            var ticks = new SortedDictionary<int, int>();
            for (var round = 0; round < 5; round++)
            {
                var r = Run(s, EndTurn);
                foreach (var e in EventsOf<DamageDealt>(r.Events).Where(e => e.Source.Via == "poison"))
                {
                    ticks.TryGetValue(e.Target.Id, out var n);
                    ticks[e.Target.Id] = n + 1;
                }
                s = r.State;
            }
            Assert.That(ticks.Select(p => new[] { p.Key, p.Value }), Is.EqualTo(new[] { new[] { 1, 3 }, new[] { 2, 2 }, new[] { 3, 1 } }));
            Assert.That(s.Units.All(u => u.Statuses.Poison == null), Is.True);
        }

        [Test]
        public void PoisonCanSpreadToThePlayer()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { Dummy("C8", statuses: PoisonedFor(3)) } });
            var run = InWorld(s, EnvironmentRules.StatusTickStep);
            Assert.That(EventsOf<StatusApplied>(run.Events).Select(e => e.Target), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void DirectReapplicationRefreshesToThree()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("guard", "D3", statuses: PoisonedFor(1)) } });
            var r = Run(s, Ability("spore_pod", "D3"));
            Assert.That(UnitById(r.State, 1)!.Statuses.Poison?.Remaining, Is.EqualTo(3));
        }

        [Test]
        public void AFloorEggLaidInRoundNHatchesTwoAtTheEndOfRoundNAndTheyActInNPlus1()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("medic", "H1") } });
            s = Run(s, Ability("brood_egg", "D7")).State;
            var egg = s.Objects.First(o => o.Kind == ObjectKind.Egg);
            Assert.That(egg.Timer, Is.EqualTo(1));
            Assert.That(egg.Hp, Is.EqualTo(3));
            Assert.That(egg.Host, Is.EqualTo("floor"));
            var r = Run(s, EndTurn);
            var h = EventsOf<EggHatched>(r.Events)[0];
            Assert.That(h.Spawned, Has.Count.EqualTo(2));
            Assert.That(EventsOf<UnitSpawned>(r.Events).Select(e => Name(e.Unit.Pos)), Is.EqualTo(new[] { "D7", "D6" }));
            // They did not act in the round they hatched.
            Assert.That(EventsOf<UnitMoved>(r.Events).Any(e => h.Spawned.Contains(e.Id)), Is.False);
        }

        [Test]
        public void AGuardCorpseHostsASpitterAndIsConsumedAndAFlamerCorpseGivesABurster()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Objects = { O(ObjectKind.Corpse, "C7", of: "guard"), O(ObjectKind.Corpse, "E7", of: "flamer") }, Units = { U("medic", "H1") }, Ap = 6 });
            s = Run(s, Ability("brood_egg", "C7")).State;
            s = Run(s, Ability("brood_egg", "E7")).State;
            Assert.That(s.Objects.Where(o => o.Kind == ObjectKind.Corpse), Is.Empty);
            Assert.That(s.Objects.Where(o => o.Kind == ObjectKind.Egg).Select(o => (o.HatchInto, o.Timer)), Is.EqualTo(new[] { ("spitter", (int?)2), ("burster", (int?)2) }));
            s = Run(s, EndTurn).State;
            Assert.That(s.Objects.Where(o => o.Kind == ObjectKind.Egg).Select(o => o.Timer), Is.EqualTo(new int?[] { 1, 1 }));
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<UnitSpawned>(r.Events).Select(e => e.Unit.Def), Is.EqualTo(new[] { "spitter", "burster" }));
        }

        [Test]
        public void AMedicOrWardenCorpseCannotHost()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Objects = { O(ObjectKind.Corpse, "C7", of: "medic"), O(ObjectKind.Corpse, "B7", of: "warden") } });
            var tiles = Action(C, s, "brood_egg").Options.Select(o => Name(o.Target)).ToList();
            Assert.That(tiles, Does.Not.Contain("C7"));
            Assert.That(tiles, Does.Not.Contain("B7"));
        }

        [Test]
        public void AnEggDestroyedBeforeItHatchesProducesNothing()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Objects = { O(ObjectKind.Egg, "B5", timer: 2, host: "guard", hatchInto: "spitter") } });
            var run = InWorld(s, w => w.DamageObject(w.S.Objects[0], 3, DamageType.Kinetic, w.PlayerEffect("test")));
            Assert.That(EventsOf<ObjectDestroyed>(run.Events), Has.Count.EqualTo(1));
            Assert.That(run.S.Objects, Is.Empty);
        }

        [Test]
        public void BroodEggCannotTargetALivingUnit()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "C7") } });
            Assert.That(Engine.Step(C, s, Ability("brood_egg", "C7")).Ok, Is.False);
        }

        [Test]
        public void ParasiteDealsTwoARoundCannotStackAndReapplyingRestartsTheThreeRounds()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Ap = 6, Units = { U("warden", "B5") } });
            s.Units[1].Statuses.Pinned = Pin();
            s = Run(s, Ability("parasite", "B5")).State;
            Assert.That(UnitById(s, 1)!.Statuses.Parasite?.Remaining, Is.EqualTo(3));
            var end = Run(s, EndTurn);
            var para = EventsOf<DamageDealt>(end.Events).Where(e => e.Source.Via == "parasite");
            Assert.That(para.Select(e => e.Amount), Is.EqualTo(new[] { 2 }));
            Assert.That(UnitById(end.State, 1)!.Statuses.Parasite?.Remaining, Is.EqualTo(2));
            var t = end.State;
            t.Units[1].Statuses.Pinned = Pin();
            t = Run(t, Ability("parasite", Name(UnitById(t, 1)!.Pos))).State;
            Assert.That(UnitById(t, 1)!.Statuses.Parasite?.Remaining, Is.EqualTo(3));
        }

        [Test]
        public void WhenTheHostDiesABasicHatchlingSpawnsOnItsTile()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5", hp: 2, statuses: ParasiteFor(3)) } });
            var spawn = EventsOf<UnitSpawned>(Run(s, Sidearm("B5")).Events)[0];
            Assert.That(spawn.Unit.Def, Is.EqualTo("hatchling"));
            Assert.That(Name(spawn.Unit.Pos), Is.EqualTo("B5"));
            Assert.That(spawn.Unit.Team, Is.EqualTo(Team.Player));
        }

        [Test]
        public void TheLastEnemyOfTheFinalWaveStillHatchesItsHatchlingBeforeTheWinIsDeclared()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", WavesSpawned = 3, Units = { U("guard", "B5", hp: 2, statuses: ParasiteFor(3)) } });
            var r = Run(s, Sidearm("B5"));
            Assert.That(r.State.Outcome?.Result, Is.EqualTo(FightResult.Win));
            Assert.That(EventsOf<UnitSpawned>(r.Events).Select(e => e.Unit.Def), Is.EqualTo(new[] { "hatchling" }));
            var t = r.Events.Select(e => e.T).ToList();
            Assert.That(t.IndexOf("UnitSpawned"), Is.GreaterThan(t.IndexOf("UnitDied")));
            Assert.That(t.IndexOf("UnitSpawned"), Is.LessThan(t.IndexOf("FightEnded")));
        }

        [Test]
        public void AParasiteTickThatKillsSpawnsTheHatchlingAfterTheTickStep()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("medic", "H1", hp: 2, statuses: ParasiteFor(3)) } });
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<UnitDied>(r.Events).Select(e => e.Def), Is.EqualTo(new[] { "medic" }));
            Assert.That(UnitAt(r.State, "H1")?.Def ?? r.State.Units.FirstOrDefault(u => u.Def == "hatchling")?.Def, Is.EqualTo("hatchling"));
        }

        [Test]
        public void ABasicHatchlingMovesTowardTheNearestEnemyAndBites()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("hatchling", "D8"), U("medic", "D5") } });
            s.Units[2].Statuses.Pinned = Pin();
            var bites = EventsOf<DamageDealt>(Run(s, EndTurn).Events).Where(e => e.Source.Via == "bite");
            Assert.That(bites.Select(e => new[] { e.Target.Id, e.Amount }), Is.EqualTo(new[] { new[] { 2, 1 } }));
        }

        [Test]
        public void ASpitterMovesUntilAnEnemyIsWithinRangeThenSpits()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("spitter", "D8"), U("medic", "E3") } });
            s.Units[2].Statuses.Pinned = Pin();
            var spit = EventsOf<DamageDealt>(Run(s, EndTurn).Events).Where(e => e.Source.Via == "spit");
            Assert.That(spit.Select(e => (e.Target.Id, e.Amount, e.DamageType)), Is.EqualTo(new[] { (2, 1, DamageType.Acid) }));
        }

        [Test]
        public void ABursterDetonatesAndDiesWhenAdjacentToAnEnemy()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("burster", "D8"), U("guard", "D5"), U("hatchling", "H8") } });
            s.Units[2].Statuses.Pinned = Pin();
            var r = Run(s, EndTurn);
            var blast = EventsOf<DamageDealt>(r.Events).Where(e => e.Source.Via == "burst");
            Assert.That(blast.Any(e => e.Target.Id == 2 && e.Amount == 3), Is.True);
            Assert.That(EventsOf<UnitDied>(r.Events).Any(e => e.Def == "burster"), Is.True);
        }
    }
}
