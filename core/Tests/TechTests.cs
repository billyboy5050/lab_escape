#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class TechTests
    {
        private static GameState PinAll(GameState s)
        {
            foreach (var u in s.Units)
            {
                if (u.Team == Team.Enemy) u.Statuses.Pinned = Pin();
            }
            return s;
        }

        [Test]
        public void ProximityMinePlacesWithin3UpToFourAtOnce()
        {
            var s = Scenario(new ScenarioSpec { Player = "D8", Ap = 9, Units = { U("medic", "H1") } });
            foreach (var t in new[] { "A8", "B7", "D5", "E6" }) s = Run(s, Ability("proximity_mine", t)).State;
            Assert.That(s.Objects.Where(o => o.Kind == ObjectKind.Mine).ToList(), Has.Count.EqualTo(4));
            var st = Action(C, s, "proximity_mine");
            Assert.That(st.Usable, Is.False);
            Assert.That(st.Reason, Does.Contain("Mine cap"));
        }

        [Test]
        public void AnyGroundUnitEnteringAMineSetsItOff()
        {
            var s = PinAll(Scenario(new ScenarioSpec { Player = "B8", Objects = { Mine("B6") }, Units = { U("guard", "B3") } }));
            var r = Run(s, Move("B7", "B6"));
            Assert.That(EventsOf<ObjectTriggered>(r.Events), Has.Count.EqualTo(1));
            Assert.That(UnitById(r.State, 0)!.Hp, Is.EqualTo(5));
            Assert.That(r.State.Objects.Any(o => o.Kind == ObjectKind.Mine), Is.False);
            Assert.That(r.Commits, Is.True);
        }

        [Test]
        public void FlyingUnitsDoNotTriggerMines()
        {
            var s = PinAll(Scenario(new ScenarioSpec { Player = "A8", Objects = { Mine("D7") }, Units = { U("drone", "C7"), U("medic", "F7") } }));
            Assert.That(EventsOf<ObjectTriggered>(Run(s, EndTurn).Events), Is.Empty);
        }

        [Test]
        public void PickUpRemovesAnAdjacentMineForOneAp()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Objects = { Mine("C7") } });
            var r = Run(s, new PickUpMineCommand("C7"));
            Assert.That(r.State.Objects, Is.Empty);
            Assert.That(r.State.Ap, Is.EqualTo(2));
        }

        [Test]
        public void TurretShootsTheNearestEnemyUsingAmmoAndAtZeroAmmoOnlyBlocks()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("medic", "E4") } });
            s = Run(s, Ability("auto_turret", "C7")).State;
            var turret = s.Units.First(u => u.Def == "turret");
            PinAll(s);
            var r = Run(s, EndTurn);
            var shot = EventsOf<DamageDealt>(r.Events).First(e => e.Source.Via == "turret_shot");
            Assert.That(shot.Target.Id, Is.EqualTo(1));
            Assert.That(shot.Amount, Is.EqualTo(2));
            Assert.That(UnitById(r.State, turret.Id)!.Ammo, Is.EqualTo(3));
            var empty = r.State.Clone();
            UnitById(empty, turret.Id)!.Ammo = 0;
            PinAll(empty);
            var r2 = Run(empty, EndTurn);
            Assert.That(EventsOf<DamageDealt>(r2.Events).Any(e => e.Source.Via == "turret_shot"), Is.False);
        }

        [Test]
        public void ReloadRefillsATurretWithin2ForOneAp()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("turret", "C7", ammo: 1) } });
            var r = Run(s, new ReloadCommand("C7"));
            Assert.That(UnitAt(r.State, "C7")!.Ammo, Is.EqualTo(4));
            Assert.That(r.State.Ap, Is.EqualTo(2));
        }

        [Test]
        public void DroneZapsTwiceFliesHomeDocksAndRedeploysWithFullChargesKeepingItsId()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Units = { U("drone", "D7"), U("medic", "F7") } });
            var charges = new List<int>();
            for (var i = 0; i < 2; i++)
            {
                PinAll(s);
                var step = Run(s, EndTurn);
                Assert.That(EventsOf<ChainArc>(step.Events), Is.Not.Empty);
                s = step.State;
                charges.Add(UnitById(s, 1)!.Charges!.Value);
            }
            Assert.That(charges, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(UnitById(s, 1)!.DroneState, Is.EqualTo(DroneState.Returning));
            Assert.That(UnitById(s, 2)!.Hp, Is.EqualTo(3));
            PinAll(s);
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<DroneDocked>(r.Events), Has.Count.EqualTo(1));
            Assert.That(EventsOf<ChainArc>(r.Events), Is.Empty);
            s = r.State;
            Assert.That(UnitById(s, 1)!.DroneState, Is.EqualTo(DroneState.Docked));
            // Docked drones still count toward the minion cap.
            Assert.That(s.Units.Where(u => u.Kind == UnitKind.Minion).ToList(), Has.Count.EqualTo(1));
            var re = Run(s, new RedeployCommand(1, "B8"));
            var d = UnitById(re.State, 1)!;
            Assert.That(d.DroneState, Is.EqualTo(DroneState.Deployed));
            Assert.That(d.Charges, Is.EqualTo(2));
            Assert.That(Name(d.Pos), Is.EqualTo("B8"));
        }

        [Test]
        public void DroneDocksAfterTwoReturningPhasesEvenWhenItCannotReachThePlayer()
        {
            var s = PinAll(Scenario(new ScenarioSpec { Player = "A8", Units = { U("drone", "H1", charges: 0, droneState: DroneState.Returning), U("medic", "H8") } }));
            var r = Run(s, EndTurn);
            Assert.That(UnitById(r.State, 1)!.DroneState, Is.EqualTo(DroneState.Returning));
            PinAll(r.State);
            r = Run(r.State, EndTurn);
            Assert.That(UnitById(r.State, 1)!.DroneState, Is.EqualTo(DroneState.Docked));
        }

        [Test]
        public void TheChainHitsEveryOtherUnitAdjacentToTheTargetNeverTheDrone()
        {
            var s = PinAll(Scenario(new ScenarioSpec { Player = "A8", Units = { U("drone", "E7"), U("medic", "F7"), U("hatchling", "G8") } }));
            var zap = EventsOf<DamageDealt>(Run(s, EndTurn).Events).Where(e => e.Source.Via == "zap");
            Assert.That(zap.Select(e => new[] { e.Target.Id, e.Amount }), Is.EqualTo(new[] { new[] { 2, 1 }, new[] { 3, 1 } }));
        }

        [Test]
        public void BarrierShieldPlacesTwoSegmentsAtMostTwoPlacements()
        {
            var s = Scenario(new ScenarioSpec { Player = "D8", Ap = 9 });
            s = Run(s, Ability("barrier_shield", "D6", "E6")).State;
            s = Run(s, Ability("barrier_shield", "D7", "C7")).State;
            Assert.That(s.Objects.Where(o => o.Kind == ObjectKind.Shield).Select(o => o.Hp), Is.EqualTo(new int?[] { 4, 4, 4, 4 }));
            Assert.That(Action(C, s, "barrier_shield").Reason, Does.Contain("Shield cap"));
        }

        [Test]
        public void BarrierShieldBlocksMovementAndProjectilesButNotSight()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Objects = { Shield("B7") }, Units = { U("guard", "B5") } });
            Assert.That(Engine.Step(C, s, Move("B7")).Ok, Is.False);
            // Sight passes the shield, so the Guard can be targeted, but the shot stops in the segment.
            Assert.That(Action(C, s, "sidearm").Options.Select(o => Name(o.Target)), Is.EqualTo(new[] { "B5" }));
            var r = Run(s, Sidearm("B5"));
            Assert.That(EventsOf<DamageDealt>(r.Events)[0].Target.Kind, Is.EqualTo(EntityKind.Object));
        }

        [Test]
        public void GrapplePullUnitDragsTheFirstUnitHitUntilAdjacentToThePlayer()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5") } });
            var r = Run(s, Ability("grapple_hook", "B5", mode: "unit"));
            Assert.That(Name(UnitById(r.State, 1)!.Pos), Is.EqualTo("B7"));
        }

        [Test]
        public void GrapplePullSelfStopsOnTheLastFreeTileBeforeABlocker()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5") } });
            var r = Run(s, Ability("grapple_hook", "B5", mode: "self"));
            Assert.That(Name(UnitById(r.State, 0)!.Pos), Is.EqualTo("B6"));
        }

        [Test]
        public void GrappleCannotPullTheWarden()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("warden", "B5") } });
            Assert.That(Action(C, s, "grapple_hook").Options.Any(o => o.Mode == "unit"), Is.False);
        }

        [Test]
        public void APulledGroundUnitSetsOffAMineOnTheWay()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("guard", "B5") }, Objects = { Mine("B6") } });
            var r = Run(s, Ability("grapple_hook", "B5", mode: "unit"));
            Assert.That(EventsOf<ObjectTriggered>(r.Events), Has.Count.EqualTo(1));
            Assert.That(UnitById(r.State, 1)!.Hp, Is.EqualTo(3));
            Assert.That(UnitById(r.State, 0)!.Hp, Is.EqualTo(8));
        }

        [Test]
        public void MinionCapBlocksSummonsAtFiveIncludingADockedDrone()
        {
            var s = Scenario(new ScenarioSpec { Player = "D8", Units = { U("turret", "A8"), U("turret", "B8"), U("hatchling", "H8"), U("hatchling", "H7"), U("drone", "H6", droneState: DroneState.Docked) } });
            Assert.That(Action(C, s, "auto_turret").Reason, Does.Contain("Minion cap"));
            Assert.That(Action(C, s, "scout_drone").Reason, Does.Contain("Minion cap"));
        }

        [Test]
        public void AnEggStillHatchesAtTheCapButHatchlingsOverTheCapDoNotSpawn()
        {
            var s = Scenario(new ScenarioSpec { Player = "D8", Units = { U("turret", "A8"), U("turret", "B8"), U("turret", "H8"), U("turret", "H7") }, Objects = { O(ObjectKind.Egg, "E7", timer: 1) } });
            var h = EventsOf<EggHatched>(Run(s, EndTurn).Events)[0];
            Assert.That(h.Spawned, Has.Count.EqualTo(1));
            Assert.That(h.Capped, Is.EqualTo(1));
        }

        [Test]
        public void IntentsShowEachMinionAndEnemyPlanRecomputedFromTheCurrentState()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("turret", "C7"), U("guard", "E4") } });
            var rep = Previews.ComputeIntents(C, s);
            var turret = rep.Intents.First(i => i.Def == "turret");
            Assert.That(turret.Hits.Select(h => new[] { h.Ref.Id, h.Damage }), Is.EqualTo(new[] { new[] { 2, 2 } }));
            // The Guard shoots the nearest player-side unit it has a clear shot at: the turret.
            var guard = rep.Intents.First(i => i.Def == "guard");
            Assert.That(guard.Hits.Select(h => new[] { h.Ref.Id, h.Damage }), Is.EqualTo(new[] { new[] { 1, 3 } }));
            Assert.That(rep.PlayerDamage, Is.EqualTo(0));
            // The real end of turn matches the intents.
            var r = Run(s, EndTurn);
            Assert.That(DamageTo(r.Events, 2), Is.EqualTo(2));
            Assert.That(DamageTo(r.Events, 1), Is.EqualTo(3));
        }

        [Test]
        public void TheKillerKeepsItsIntentAndLaterUnitsAreMarkedAsNotActing()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", PlayerHp = 6, Units = { U("guard", "B6"), U("guard", "C7"), U("medic", "H1") } });
            var rep = Previews.ComputeIntents(C, s);
            Assert.That(rep.PlayerDies, Is.True);
            var killer = rep.Intents.First(i => i.Id == 2);
            Assert.That(killer.Hits.Select(h => (h.Ref.Id, h.Damage, h.Killed)), Is.EqualTo(new[] { (0, 3, true) }));
            Assert.That(rep.Intents.First(i => i.Id == 3).Label, Does.Contain("fight ends first"));
        }

        [Test]
        public void AUnitThatHasNothingToDoIsIdleAndHoldsNotDead()
        {
            // The second turret has no ammo, so it does nothing on its turn; it is still alive and must not be shown dying.
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("turret", "C7"), U("turret", "A7", ammo: 0), U("guard", "E4") } });
            var empty = Previews.ComputeIntents(C, s).Intents.First(i => i.Id == 2);
            Assert.That(empty.DiesFirst, Is.False);
            Assert.That(empty.Idle, Is.True);
            Assert.That(empty.Label, Is.EqualTo("Holds"));
        }

        [Test]
        public void AUnitKilledBySomeoneActingEarlierIsMarkedAsDyingBeforeItActs()
        {
            // The turret (minion phase) shoots the 1 HP guard before the enemy phase, so the guard never gets its turn.
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("turret", "C7"), U("guard", "E4", hp: 1) } });
            var guard = Previews.ComputeIntents(C, s).Intents.First(i => i.Def == "guard");
            Assert.That(guard.DiesFirst, Is.True);
            Assert.That(guard.Label, Is.EqualTo("Dies before acting"));
        }

        [Test]
        public void AUnitInItsArrivalRoundShowsItsDestinationOnly()
        {
            var s = Scenario(new ScenarioSpec { Player = "E8", Units = { U("guard", "A1", arrival: true) } });
            var rep = Previews.ComputeIntents(C, s);
            Assert.That(rep.Intents[0].Arrival, Is.True);
            Assert.That(rep.Intents[0].Hits, Is.Empty);
            Assert.That(Name(rep.Intents[0].To), Is.EqualTo("B3"));
        }
    }
}
