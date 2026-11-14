#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class UpgradesTests
    {
        private static List<string> Ups(params string[] ids) => ids.ToList();

        [Test]
        public void T1PiercingRoundsIgnoresTwoArmor()
        {
            var plain = Scenario(new ScenarioSpec { Player = "B8", Units = { U("warden", "B5") } });
            Assert.That(UnitById(Run(plain, Sidearm("B5")).State, 1)!.Hp, Is.EqualTo(20));
            var s = Scenario(new ScenarioSpec { Player = "B8", Upgrades = Ups(Up.T1), Units = { U("warden", "B5"), Dummy("A7", ammo: 4) } });
            s.Units[1].Statuses.Pinned = Pin();
            var r = Run(s, Sidearm("B5"));
            Assert.That(UnitById(r.State, 1)!.Hp, Is.EqualTo(18));
            r = Run(r.State, EndTurn);
            var shot = EventsOf<DamageDealt>(r.Events).First(e => e.Source.Via == "turret_shot");
            Assert.That(shot.Target.Id, Is.EqualTo(1));
            Assert.That(shot.Amount, Is.EqualTo(2));
            Assert.That(shot.Absorbed, Is.EqualTo(0));
        }

        [Test]
        public void T2ShotTriggeredMinesTheSidearmCanTargetYourMine()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Upgrades = Ups(Up.T2), Objects = { Mine("E5") }, Units = { U("guard", "E4") } });
            Assert.That(Action(C, s, "sidearm").Options.Select(o => Name(o.Target)), Does.Contain("E5"));
            var r = Run(s, Sidearm("E5"));
            Assert.That(EventsOf<ObjectTriggered>(r.Events), Has.Count.EqualTo(1));
            Assert.That(UnitById(r.State, 1)!.Hp, Is.EqualTo(3));
            // Without the upgrade a mine is not a target, and other projectiles pass over mines.
            var plain = Scenario(new ScenarioSpec { Player = "B8", Objects = { Mine("E5") }, Units = { U("guard", "E4") } });
            Assert.That(Action(C, plain, "sidearm").Options.Select(o => Name(o.Target)), Does.Not.Contain("E5"));
        }

        [Test]
        public void T3SuppressingFireAnEnemyHitByATurretShotIsSlowedForItsNextMove()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Upgrades = Ups(Up.T3), Units = { Dummy("C7", ammo: 4), U("guard", "E4") } });
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<StatusApplied>(r.Events).First(e => e.Status == StatusId.Slowed).Target, Is.EqualTo(2));
            // The slow lasts one round: it is gone after the environment phase.
            Assert.That(UnitById(r.State, 2)!.Statuses.Slowed, Is.Null);
        }

        [Test]
        public void T4GrapplePinAPulledUnitTakesKineticOnLandingAndIsPinned()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Upgrades = Ups(Up.T4), Units = { U("guard", "B5") } });
            var r = Run(s, Ability("grapple_hook", "B5", mode: "unit"));
            var g = UnitById(r.State, 1)!;
            Assert.That(g.Hp, Is.EqualTo(5));
            Assert.That(g.Statuses.Pinned?.Remaining, Is.EqualTo(1));
            var end = Run(r.State, EndTurn);
            Assert.That(EventsOf<UnitMoved>(end.Events).Any(e => e.Id == 1), Is.False);
        }

        [Test]
        public void T5ReflectiveShieldTheFirstProjectileEachRoundIsSentBackAlongItsLine()
        {
            // The Guard on B5 shoots at the player on B8; the segment on B6 sends the shot back into the Guard.
            var s = Scenario(new ScenarioSpec { Player = "B8", Upgrades = Ups(Up.T5), Objects = { Shield("B6") }, Units = { U("guard", "B5") } });
            s.Units[1].Statuses.Pinned = Pin();
            var r = Run(s, EndTurn);
            var shots = EventsOf<ProjectileFired>(r.Events).Where(e => e.Source.Def == "guard");
            Assert.That(shots.Select(e => e.Reflected == true), Is.EqualTo(new[] { false, true }));
            var hits = EventsOf<DamageDealt>(r.Events).Where(e => e.Source.Def == "guard").ToList();
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0].Target, Is.EqualTo(EntityRef.Unit(1)));
            Assert.That(hits[0].Amount, Is.EqualTo(3));
            Assert.That(r.State.Objects.First(o => o.Kind == ObjectKind.Shield).Hp, Is.EqualTo(4));
        }

        [Test]
        public void T5ReflectiveShieldASecondProjectileInTheSameRoundDamagesTheSegment()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Upgrades = Ups(Up.T5), Objects = { Shield("B6") }, Units = { U("guard", "B5") } });
            var run = InWorld(s, w =>
            {
                var g = w.Unit(1)!;
                var payload = new ShotPayload { Source = w.UnitSource(g, "shot"), Damage = 3, DamageType = DamageType.Kinetic, Reflectable = true };
                Projectiles.FireProjectile(w, g.Pos, Projectiles.ShotPath(g.Pos, T("B8")), payload);
                Projectiles.FireProjectile(w, g.Pos, Projectiles.ShotPath(g.Pos, T("B8")), payload);
            });
            Assert.That(EventsOf<ProjectileFired>(run.Events).Select(e => e.Reflected == true), Is.EqualTo(new[] { false, true, false }));
            Assert.That(run.S.Objects.First(o => o.Kind == ObjectKind.Shield).Hp, Is.EqualTo(1));
        }

        [Test]
        public void T6OverchargedDroneThreeChargesAndTheChainJumpsOneExtraHop()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Upgrades = Ups(Up.T6), Ap = 3 });
            var r = Run(s, Ability("scout_drone", "B8"));
            Assert.That(r.State.Units.First(u => u.Def == "drone").Charges, Is.EqualTo(3));
            // The drone zaps the Medic on E7; the chain reaches F8 (adjacent to the Medic), and the extra hop reaches G7.
            var t = Scenario(new ScenarioSpec { Player = "A8", Upgrades = Ups(Up.T6), Units = { U("drone", "D8"), U("medic", "E7"), Dummy("F8"), Dummy("G7") } });
            t.Units[2].Statuses.Pinned = Pin();
            var zapped = EventsOf<DamageDealt>(Run(t, EndTurn).Events).Where(e => e.Source.Via == "zap").Select(e => e.Target.Id);
            Assert.That(zapped, Is.EqualTo(new[] { 2, 3, 4 }));
        }

        [Test]
        public void A1SporeBurstWhenAPoisonedUnitDiesItPoisonsItsArea()
        {
            var s = Scenario(new ScenarioSpec { Player = "E8", Upgrades = Ups(Up.A1), Units = { U("guard", "E3", hp: 2, statuses: PoisonedFor(3, "test")), U("medic", "F3") } });
            var r = Run(s, Sidearm("E3"));
            Assert.That(EventsOf<AreaEffect>(r.Events).Any(e => e.Kind == AreaKind.Burst), Is.True);
            Assert.That(UnitById(r.State, 2)!.Statuses.Poison?.Remaining, Is.EqualTo(3));
        }

        [Test]
        public void A1SporeBurstStillFiresWhenThePoisonedUnitWasTheLastEnemyBeforeTheWin()
        {
            var s = Scenario(new ScenarioSpec { Player = "E8", WavesSpawned = 3, Upgrades = Ups(Up.A1), Units = { U("guard", "E3", hp: 2, statuses: PoisonedFor(3, "test")), U("hatchling", "F3") } });
            var r = Run(s, Sidearm("E3"));
            Assert.That(r.State.Outcome?.Result, Is.EqualTo(FightResult.Win));
            Assert.That(EventsOf<AreaEffect>(r.Events).Any(e => e.Kind == AreaKind.Burst), Is.True);
            Assert.That(UnitById(r.State, 2)!.Statuses.Poison?.Remaining, Is.EqualTo(3));
            var t = r.Events.Select(e => e.T).ToList();
            Assert.That(t.LastIndexOf("AreaEffect"), Is.LessThan(t.IndexOf("FightEnded")));
        }

        [Test]
        public void AQueuedDeathEffectThatKillsThePlayerStillLosesTheFightEvenAfterTheLastEnemyFell()
        {
            // A death hook of the last enemy queues damage that kills a one-HP player. Loss takes priority over the win.
            var s = Scenario(new ScenarioSpec { Player = "E4", PlayerHp = 1, WavesSpawned = 3, Units = { U("guard", "E3", hp: 1) } });
            var run = InWorld(s, world =>
            {
                var guard = world.S.Units.First(u => u.Def == "guard");
                world.DeathHooks.Add((ww, dead) =>
                {
                    if (dead.Id != guard.Id) return;
                    ww.Enqueue("test_blast", "u" + dead.Id, () => ww.DamageUnit(ww.S.Units[0], 5, DamageType.Kinetic, ww.PlayerEffect("test", SourceKind.Unit, dead.Id, dead.Def)));
                });
                world.DamageUnit(guard, 5, DamageType.Kinetic, world.PlayerEffect("test", SourceKind.Unit, 0, "player"));
            });
            Assert.That(run.S.Outcome?.Result, Is.EqualTo(FightResult.Lose));
        }

        [Test]
        public void A2AcidBroodWhenAnyHatchlingDiesAdjacentUnitsGainCorrode()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Upgrades = Ups(Up.A2), Units = { U("hatchling", "E4", hp: 1), U("warden", "F4") } });
            var run = InWorld(s, w => w.DamageUnit(w.Unit(1)!, 1, DamageType.Kinetic, w.UnitSource(w.Unit(2)!, "melee")));
            Assert.That(UnitById(run.S, 2)!.Statuses.Corrode?.Stacks, Is.EqualTo(2));
        }

        [Test]
        public void A3ParasiteJumpWhenAHostDiesTheParasiteJumpsToTheNearestEnemy()
        {
            var s = Scenario(new ScenarioSpec { Player = "E8", Upgrades = Ups(Up.A3), Units = { U("guard", "E5", hp: 2, statuses: ParasiteFor(1, "test")), U("medic", "G4"), U("guard", "H1") } });
            var r = Run(s, Sidearm("E5"));
            Assert.That(UnitById(r.State, 2)!.Statuses.Parasite?.Remaining, Is.EqualTo(3));
            Assert.That(UnitById(r.State, 3)!.Statuses.Parasite, Is.Null);
            // The host still bursts a hatchling.
            Assert.That(EventsOf<UnitSpawned>(r.Events).Select(e => e.Unit.Def), Is.EqualTo(new[] { "hatchling" }));
        }

        [Test]
        public void A4FastBroodCorpseEggsHatchInOneRound()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Upgrades = Ups(Up.A4), Objects = { O(ObjectKind.Corpse, "C7", of: "guard") } });
            var r = Run(s, Ability("brood_egg", "C7"));
            Assert.That(r.State.Objects.First(o => o.Kind == ObjectKind.Egg).Timer, Is.EqualTo(1));
        }

        [Test]
        public void H1SporeMinesAlsoPoisonEveryUnitInTheBlastArea()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Upgrades = Ups(Up.H1), Objects = { Mine("E7") }, Units = { U("guard", "E6"), U("medic", "F8") } });
            var run = InWorld(s, w => w.MoveAlong(w.Unit(1)!, new[] { new Pos(4, 6) }, MoveMode.Walk));
            Assert.That(UnitById(run.S, 1)!.Statuses.Poison?.Remaining, Is.EqualTo(3));
            Assert.That(UnitById(run.S, 2)!.Statuses.Poison?.Remaining, Is.EqualTo(3));
        }

        [Test]
        public void H2PlagueDroneZapsAlsoPoisonTheTargetAndEveryUnitTheChainHits()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Upgrades = Ups(Up.H2), Units = { U("drone", "D8"), U("medic", "E6"), Dummy("E5") } });
            s.Units[2].Statuses.Pinned = Pin();
            var poisoned = EventsOf<StatusApplied>(Run(s, EndTurn).Events).Where(e => e.Status == StatusId.Poison && !e.Spread).Select(e => e.Target);
            Assert.That(poisoned, Is.EqualTo(new[] { 2, 3 }));
        }

        [Test]
        public void TheOncePerChainRuleSuppressesALoopBuiltFromSporeBurstAndAcidBrood()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Upgrades = Ups(Up.A1, Up.A2), Units = { U("hatchling", "E7", hp: 1, statuses: PoisonedFor(2, "test")), U("medic", "F7") } });
            var run = InWorld(s, w =>
            {
                // A feedback hook that re-raises the death triggers of the unit that died, as a loop would.
                DeathHook echo = (w2, dead) => w2.Enqueue("test_echo", "u" + dead.Id, () =>
                {
                    foreach (var h in w2.DeathHooks.ToList()) h(w2, dead);
                });
                w.DeathHooks.Add(echo);
                w.DamageUnit(w.Unit(1)!, 1, DamageType.Kinetic, w.UnitSource(w.Unit(2)!, "melee"));
            });
            Assert.That(EventsOf<AreaEffect>(run.Events).Where(e => e.Kind == AreaKind.Burst), Has.Exactly(1).Items);
            Assert.That(EventsOf<StatusApplied>(run.Events).Where(e => e.Status == StatusId.Corrode), Has.Exactly(1).Items);
            Assert.That(EventsOf<ChainSuppressed>(run.Events).Select(e => $"{e.Effect}@{e.Source}:{Names.Of(e.Reason)}"), Is.EqualTo(new[]
            {
                "A1_spore_burst@u1:repeat",
                "A2_acid_brood@u1:repeat",
                "test_echo@u1:repeat",
            }));
        }

        [Test]
        public void StopsARunawayChainAt20TriggerGenerations()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("medic", "H1") } });
            var n = 0;
            var run = InWorld(s, w =>
            {
                void Grow()
                {
                    var id = n++;
                    w.Enqueue("test_grow", "g" + id, Grow);
                }
                Grow();
            });
            Assert.That(n, Is.EqualTo(21));
            var suppressed = EventsOf<ChainSuppressed>(run.Events);
            Assert.That(suppressed.Select(e => e.ToJson()), Is.EqualTo(new[] { "{\"t\":\"ChainSuppressed\",\"effect\":\"test_grow\",\"source\":\"g20\",\"reason\":\"depth\"}" }));
        }

        [Test]
        public void EveryPresetIsAValidLoadoutAndStartsAFight()
        {
            foreach (var p in C.Presets)
            {
                var loadout = new Loadout(p.Abilities, p.Upgrades);
                Assert.That(Engine.LoadoutProblems(C, loadout), Is.Empty, p.Id);
                Assert.That(Engine.NewGame(C, loadout).State.Units, Has.Count.EqualTo(5));
            }
        }

        [Test]
        public void AnUpgradeNeedsItsRequiredAbilities()
        {
            Assert.That(Engine.LoadoutProblems(C, new Loadout(new[] { "sidearm" }, new[] { Up.T2 }))[0], Does.Contain("Shot-Triggered Mines needs"));
            Assert.That(Engine.LoadoutProblems(C, new Loadout(new[] { "auto_turret" }, new[] { Up.T1 })), Is.Empty);
        }

        [Test]
        public void AtMostEightAbilitiesAndThreeUpgradesNoDuplicates()
        {
            Assert.That(Engine.LoadoutProblems(C, new Loadout(C.AbilityOrder.Take(9), new string[0])), Does.Contain("More than 8 abilities"));
            Assert.That(Engine.LoadoutProblems(C, new Loadout(new[] { "sidearm", "sidearm" }, new string[0])), Does.Contain("Duplicate ability"));
        }
    }
}
