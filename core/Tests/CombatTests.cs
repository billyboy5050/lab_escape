#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>Ported from tests/combat.test.ts.</summary>
    public class CombatTests
    {
        private static ScenarioSpec WardenSpec() => new ScenarioSpec { Player = "B8", Units = { U("warden", "B5") } };

        [Test]
        public void ArmorSubtractsFromKineticExplosiveAndFireToAMinimumOfZero()
        {
            var run = InWorld(Scenario(WardenSpec()), w =>
            {
                var wd = w.Unit(1)!;
                w.DamageUnit(wd, 2, DamageType.Kinetic, w.UnitSource(w.Player!, "sidearm"));
                w.DamageUnit(wd, 3, DamageType.Explosive, w.UnitSource(w.Player!, "mine"));
                w.DamageUnit(wd, 3, DamageType.Fire, w.UnitSource(w.Player!, "test"));
            });
            var dd = EventsOf<DamageDealt>(run.Events);
            Assert.That(dd.Select(e => new[] { e.Amount, e.Absorbed }), Is.EqualTo(new[] { new[] { 0, 2 }, new[] { 1, 2 }, new[] { 1, 2 } }));
        }

        [Test]
        public void ElectricAcidPoisonAndParasiteDamageIgnoreArmor()
        {
            var run = InWorld(Scenario(WardenSpec()), w =>
            {
                var wd = w.Unit(1)!;
                foreach (var t in new[] { DamageType.Electric, DamageType.Acid, DamageType.Poison, DamageType.Parasite }) w.DamageUnit(wd, 2, t, w.UnitSource(w.Player!, "test"));
            });
            Assert.That(EventsOf<DamageDealt>(run.Events).Select(e => e.Amount), Is.EqualTo(new[] { 2, 2, 2, 2 }));
        }

        [Test]
        public void EachCorrodeStackRemovesOneArmorDownToZero()
        {
            var run = InWorld(Scenario(WardenSpec()), w =>
            {
                var wd = w.Unit(1)!;
                w.ApplyStatus(wd, StatusId.Corrode, 1, w.UnitSource(w.Player!, "acid_spit"));
                w.DamageUnit(wd, 2, DamageType.Kinetic, w.UnitSource(w.Player!, "sidearm"));
                w.ApplyStatus(wd, StatusId.Corrode, 5, w.UnitSource(w.Player!, "acid_spit"));
                w.DamageUnit(wd, 2, DamageType.Kinetic, w.UnitSource(w.Player!, "sidearm"));
                Assert.That(w.ArmorOf(wd), Is.EqualTo(0));
            });
            Assert.That(EventsOf<DamageDealt>(run.Events).Select(e => e.Amount), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void OneAcidSpitRemovesTheWardenArmorForTheRestOfTheFight()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("warden", "B5") }, Ap = 3 });
            var r = Run(s, Ability("acid_spit", "B5"));
            var wd = UnitById(r.State, 1)!;
            Assert.That(wd.Hp, Is.EqualTo(19));
            Assert.That(wd.Statuses.Corrode?.Stacks, Is.EqualTo(2));
            Assert.That(wd.Statuses.Corrode?.Remaining, Is.Null);
            r = Run(r.State, Sidearm("B5"));
            Assert.That(UnitById(r.State, 1)!.Hp, Is.EqualTo(17));
        }

        [Test]
        public void HealingCannotExceedMaximumHp()
        {
            var s = Scenario(new ScenarioSpec { Units = { U("guard", "E3", hp: 5) } });
            var run = InWorld(s, w => w.Heal(w.Unit(1)!, 2, w.UnitSource(w.Unit(1)!, "heal")));
            Assert.That(EventsOf<Healed>(run.Events)[0].Amount, Is.EqualTo(1));
        }

        [Test]
        public void AProjectileHitsTheFirstUnitOnItsLineFriendlyOrNot()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5"), U("hatchling", "B6") } });
            var r = Run(s, Sidearm("B5"));
            Assert.That(DamageTo(r.Events, 2), Is.EqualTo(2));
            Assert.That(DamageTo(r.Events, 1), Is.EqualTo(0));
        }

        [Test]
        public void AMineHurtsYourOwnMinion()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Objects = { Mine("D6") }, Units = { U("turret", "E6"), U("guard", "D4") } });
            // The Guard walks onto the mine while closing in; the turret next to it takes the blast.
            var run = InWorld(s, w => w.MoveAlong(w.Unit(2)!, new[] { new Pos(3, 4), new Pos(3, 5) }, MoveMode.Walk));
            Assert.That(EventsOf<ObjectTriggered>(run.Events), Has.Count.EqualTo(1));
            var toTurret = EventsOf<DamageDealt>(run.Events).First(e => e.Target.Id == 1);
            Assert.That(toTurret.Amount, Is.EqualTo(3));
            Assert.That(toTurret.Source.Side, Is.EqualTo(SourceSide.Player));
        }

        [Test]
        public void ShieldSegmentsStopProjectiles()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5") }, Objects = { Shield("B6") } });
            var r = Run(s, Sidearm("B5"));
            var dd = EventsOf<DamageDealt>(r.Events);
            Assert.That(dd, Has.Count.EqualTo(1));
            Assert.That(dd[0].Target.Kind, Is.EqualTo(EntityKind.Object));
            Assert.That(r.State.Objects.First(o => o.Kind == ObjectKind.Shield).Hp, Is.EqualTo(2));
        }

        [Test]
        public void ProjectilesPassOverMinesAndCorpses()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5") }, Objects = { Mine("B7"), O(ObjectKind.Corpse, "B6") } });
            var r = Run(s, Sidearm("B5"));
            Assert.That(DamageTo(r.Events, 1), Is.EqualTo(2));
            Assert.That(r.State.Objects.Any(o => o.Kind == ObjectKind.Mine), Is.True);
        }

        [Test]
        public void AnEnemyLeavesACorpseAndPlayerSideUnitsLeaveNone()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B6", hp: 2), U("hatchling", "D6", hp: 1) } });
            var run = InWorld(s, w =>
            {
                w.DamageUnit(w.Unit(1)!, 5, DamageType.Acid, w.UnitSource(w.Player!, "test"));
                w.DamageUnit(w.Unit(2)!, 5, DamageType.Acid, w.UnitSource(w.Player!, "test"));
            });
            Assert.That(run.S.Objects.Where(o => o.Kind == ObjectKind.Corpse).Select(o => o.Of), Is.EqualTo(new[] { "guard" }));
        }

        [Test]
        public void AreaDamageHitsEveryTargetFirstThenDeathsAreProcessedInUnitIdOrder()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("hatchling", "D5"), U("guard", "E5", hp: 1), U("hatchling", "D6") } });
            var run = InWorld(s, w => w.AreaDamage(w.B.Square(new Pos(3, 4), 1), 3, DamageType.Explosive, w.PlayerEffect("test")));
            var kinds = run.Events.Select(e => e.T).Where(t => t == "DamageDealt" || t == "UnitDied");
            Assert.That(kinds, Is.EqualTo(new[] { "DamageDealt", "DamageDealt", "DamageDealt", "UnitDied", "UnitDied", "UnitDied" }));
            Assert.That(EventsOf<UnitDied>(run.Events).Select(e => e.Id), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void DamageEventsRecordHpActuallyLostNotOverkill()
        {
            // The TypeScript test also checks the telemetry summary, which is ported with the simulator (M10).
            var s = Scenario(new ScenarioSpec { Player = "B8", PlayerHp = 2, Units = { U("guard", "B6") } });
            var hit = EventsOf<DamageDealt>(Run(s, EndTurn).Events)[0];
            Assert.That(hit.Amount, Is.EqualTo(3));
            Assert.That(hit.HpLost, Is.EqualTo(2));
            Assert.That(hit.Killed, Is.True);
        }

        [Test]
        public void ThePlayerDyingIsAnImmediateLoss()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", PlayerHp = 3, Units = { U("guard", "B6"), U("guard", "C7") } });
            var r = Run(s, EndTurn);
            Assert.That(r.State.Outcome?.Result, Is.EqualTo(FightResult.Lose));
            Assert.That(r.State.Outcome?.Cause, Does.Contain("guard"));
            // The second Guard never acts.
            Assert.That(EventsOf<ProjectileFired>(r.Events), Has.Count.EqualTo(1));
        }

        [Test]
        public void TheFightIsWonWhenTheLastWaveHasSpawnedAndEveryEnemyIsDead()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", WavesSpawned = 3, Units = { U("guard", "B6", hp: 2) } });
            Assert.That(Run(s, Sidearm("B6")).State.Outcome?.Result, Is.EqualTo(FightResult.Win));
        }

        // Row 7 is clear floor. The first mine's blast reaches the Guard on C8 and the player on B7; the second mine is on D7.
        private static StepResult CrossTwoMines(int playerHp)
        {
            var s = Scenario(new ScenarioSpec { Player = "A7", PlayerHp = playerHp, WavesSpawned = 3, Units = { U("guard", "C8", hp: 3) }, Objects = { Mine("B7"), Mine("D7") } });
            return Run(s, Move("B7", "C7", "D7"));
        }

        [Test]
        public void KillingTheLastEnemyOnTheFirstMineOfAMoveDoesNotSkipTheSecondMine()
        {
            var r = CrossTwoMines(8);
            Assert.That(EventsOf<ObjectTriggered>(r.Events), Has.Count.EqualTo(2));
            Assert.That(UnitById(r.State, 0)!.Pos, Is.EqualTo(new Pos(3, 6)));
            Assert.That(r.State.Outcome?.Result, Is.EqualTo(FightResult.Win));
        }

        [Test]
        public void ThePlayerDyingOnALaterMineOfTheSameMoveIsALossNotAWin()
        {
            // 4 HP survives the first blast with 1, and the second blast kills.
            var r = CrossTwoMines(4);
            Assert.That(EventsOf<ObjectTriggered>(r.Events), Has.Count.EqualTo(2));
            Assert.That(EventsOf<UnitDied>(r.Events).Select(e => e.Id), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(r.State.Outcome?.Result, Is.EqualTo(FightResult.Lose));
        }

        [Test]
        public void KillingEverythingBeforeTheLastWaveIsNotAWin()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", WavesSpawned = 2, Units = { U("guard", "B6", hp: 2) } });
            Assert.That(Run(s, Sidearm("B6")).State.Outcome, Is.Null);
        }

        [Test]
        public void Round30EndingUnresolvedIsALoss()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Round = 30, WavesSpawned = 3, Units = { U("guard", "H1") } });
            // Wall the Guard off so the round passes without anyone dying.
            s.Units[0].Pos = new Pos(0, 7);
            s.Objects.AddRange(new[] { ShieldAt(90, 6, 0, 1, 30), ShieldAt(91, 6, 1, 1, 30), ShieldAt(92, 7, 2, 2, 30) });
            var r = Run(s, EndTurn);
            Assert.That(r.State.Outcome?.Result, Is.EqualTo(FightResult.Lose));
            Assert.That(r.State.Outcome?.Cause, Does.Contain("Round 30"));
        }

        [Test]
        public void PoisonAppliedInRoundNDealsDamageInRoundsNToNPlus2ThenEnds()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("guard", "H1", hp: 6) }, WavesSpawned = 2 });
            s.Units[1].Statuses.Poison = new TimedStatus(3, Src("spore_pod"));
            // Keep the Guard out of the fight: box it into the corner with shields.
            s.Objects.AddRange(new[] { ShieldAt(90, 6, 0, 1, 2), ShieldAt(91, 6, 1, 1, 2) });
            var hp = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                s = Run(s, EndTurn).State;
                hp.Add(UnitById(s, 1)!.Hp);
            }
            Assert.That(hp, Is.EqualTo(new[] { 5, 4, 3, 3 }));
            Assert.That(UnitById(s, 1)!.Statuses.Poison, Is.Null);
        }

        [Test]
        public void ACorpseLastsTheWholeNextPlayerPhaseThenDecaysAtStep5()
        {
            var s0 = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B6", hp: 2), U("medic", "H1") } });
            var s = Run(s0, Sidearm("B6")).State;
            Assert.That(s.Objects.Any(o => o.Kind == ObjectKind.Corpse), Is.True);
            s = Run(s, EndTurn).State; // round N ends: decay 2 -> 1
            Assert.That(s.Objects.First(o => o.Kind == ObjectKind.Corpse).Decay, Is.EqualTo(1));
            Assert.That(s.Round, Is.EqualTo(3));
            s = Run(s, EndTurn).State; // round N+1 ends: removed
            Assert.That(s.Objects.Any(o => o.Kind == ObjectKind.Corpse), Is.False);
        }
    }
}
