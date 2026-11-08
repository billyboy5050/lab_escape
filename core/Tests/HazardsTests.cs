#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>Ported from tests/hazards.test.ts.</summary>
    public class HazardsTests
    {
        private static Dictionary<string, PrimeDecision> Decisions(GameState s)
        {
            var w = Phases.CreateWorld(C, s.Clone());
            return HazardAi.PrimeDecisions(w).ToDictionary(d => d.Hazard.Id);
        }

        private static void AssertDecision(PrimeDecision d, bool prime, int step, string? reason = null)
        {
            Assert.That(d.Prime, Is.EqualTo(prime));
            Assert.That(d.Step, Is.EqualTo(step));
            if (reason != null) Assert.That(d.Reason, Is.EqualTo(reason));
        }

        [Test]
        public void Step1HoldsWheneverAnEliteWouldBeHit()
        {
            var d = Decisions(Scenario(new ScenarioSpec { Player = "E5", Units = { U("warden", "D4") } }));
            AssertDecision(d["panel"], false, 1);
        }

        [Test]
        public void Step2ACombinedKillPrimesEveryHazardThatWouldHitThePlayerEvenThroughSoldiers()
        {
            // G5 is in the gun lane (2) and next to vent G6 (1): 3 damage against 3 HP. A Guard also stands in the vent area.
            var d = Decisions(Scenario(new ScenarioSpec { Player = "G5", PlayerHp = 3, Units = { U("guard", "H7") } }));
            AssertDecision(d["gun"], true, 2);
            AssertDecision(d["vent_g6"], true, 2);
        }

        [Test]
        public void Step2FailsWhenTheTotalIsShortOfThePlayerHpAndTheRuleFallsThrough()
        {
            var d = Decisions(Scenario(new ScenarioSpec { Player = "G5", PlayerHp = 4, Units = { U("guard", "H7") } }));
            AssertDecision(d["gun"], true, 3);
            AssertDecision(d["vent_g6"], false, 4, "A soldier would be hit");
        }

        [Test]
        public void Step3PrimesWhenOnlyThePlayerSideWouldBeHit()
        {
            var d = Decisions(Scenario(new ScenarioSpec { Player = "A8", Units = { U("hatchling", "D6") } }));
            AssertDecision(d["panel"], true, 3);
        }

        [Test]
        public void Step4HoldsWhenASoldierWouldBeHitOrNothingWouldBeHit()
        {
            var d = Decisions(Scenario(new ScenarioSpec { Player = "C5", Units = { U("guard", "A6") } }));
            AssertDecision(d["vent_b5"], false, 4, "A soldier would be hit");
            AssertDecision(d["vent_g6"], false, 4, "Nothing to hit");
        }

        [Test]
        public void TheWallGunRanksAPlayerSideObjectFirstInItsLaneAsAMinion()
        {
            var d = Decisions(Scenario(new ScenarioSpec { Player = "A8", Objects = { Shield("E5") } }));
            AssertDecision(d["gun"], true, 3);
        }

        [Test]
        public void TheWallGunASoldierFirstInItsLaneShieldsThePlayerBehindIt()
        {
            var d = Decisions(Scenario(new ScenarioSpec { Player = "C5", Units = { U("guard", "F5") } }));
            AssertDecision(d["gun"], false, 4);
        }

        [Test]
        public void RecordsWhichStepProducedEachDecisionForTheDebugOverlay()
        {
            var r = Run(Scenario(new ScenarioSpec { Player = "A6", Units = { U("guard", "H1") } }), EndTurn);
            var last = r.State.Hazards.First(h => h.Id == "vent_b5").Last!;
            Assert.That(last.Stage, Is.EqualTo(HazardStage.Prime));
            Assert.That(last.Result, Is.EqualTo(HazardResult.Prime));
            Assert.That(last.Step, Is.EqualTo(3));
        }

        [Test]
        public void ACommittedHazardFiresOnABaitedSoldier()
        {
            // Round 2: the player stands in vent B5's area and draws the cue.
            var s = Scenario(new ScenarioSpec { Player = "A6", Units = { U("guard", "H1") } });
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<HazardPrimed>(r.Events).Select(e => e.Hazard), Does.Contain("vent_b5"));
            s = r.State;
            // Round 3: the player leaves; a soldier now stands in the area when the vent fires.
            s.Units[0].Pos = new Pos(0, 7);
            s.Units[1].Pos = new Pos(0, 4);
            s.Units[1].Statuses.Pinned = Pin();
            r = Run(s, EndTurn);
            var fired = EventsOf<HazardFired>(r.Events).First(e => e.Hazard == "vent_b5");
            Assert.That(fired.Victims, Is.EqualTo(new[] { EntityRef.Unit(1) }));
            Assert.That(EventsOf<IntercomLine>(r.Events).Select(e => e.Line), Does.Contain("hazardHitsSoldier"));
        }

        [Test]
        public void CancelsWithAShortCooldownIfAnEliteWouldBeHit()
        {
            var s = Scenario(new ScenarioSpec { Player = "A6", Units = { U("guard", "H1") } });
            s = Run(s, EndTurn).State;
            s.Units[0].Pos = new Pos(0, 7);
            var u = s.Units[1];
            u.Def = "warden";
            u.Elite = true;
            u.Armor = 2;
            u.Hp = 20;
            u.MaxHp = 20;
            u.Pos = new Pos(0, 4);
            u.Statuses = new Statuses { Pinned = Pin() };
            u.Cooldowns["purge"] = 0;
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<HazardCancelled>(r.Events).First(e => e.Hazard == "vent_b5").Reason, Is.EqualTo("An elite would be hit"));
        }

        [Test]
        public void CancelsIfNothingWouldBeHit()
        {
            var s = Scenario(new ScenarioSpec { Player = "A6", Units = { U("guard", "H1") } });
            s = Run(s, EndTurn).State;
            s = Run(s, Move("A7", "A8")).State;
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<HazardCancelled>(r.Events).First(e => e.Hazard == "vent_b5").Reason, Is.EqualTo("Nothing to hit"));
        }

        private static GameState Boxed(GameState s)
        {
            s.Objects.AddRange(new[] { ShieldAt(90, 6, 0, 9, 1), ShieldAt(91, 6, 1, 9, 1) });
            return s;
        }

        [Test]
        public void FiresInRoundNPrimesAgainLaterAndACancelCanPrimeAgainNextRound()
        {
            var s = Boxed(Scenario(new ScenarioSpec { Player = "A6", Round = 1, Units = { U("guard", "H1") } }));
            var primed = new List<int>();
            var fired = new List<int>();
            for (var i = 0; i < 5; i++)
            {
                var round = s.Round;
                var step = Run(s, EndTurn);
                if (EventsOf<HazardPrimed>(step.Events).Any(e => e.Hazard == "vent_b5")) primed.Add(round);
                if (EventsOf<HazardFired>(step.Events).Any(e => e.Hazard == "vent_b5")) fired.Add(round);
                s = step.State;
            }
            Assert.That(primed, Is.EqualTo(new[] { 1, 5 }));
            Assert.That(fired, Is.EqualTo(new[] { 2 }));
            var r6 = Run(s, EndTurn);
            Assert.That(EventsOf<HazardFired>(r6.Events).Any(e => e.Hazard == "vent_b5"), Is.True);

            // Cancel timing.
            var t = Boxed(Scenario(new ScenarioSpec { Player = "A6", Round = 1, Units = { U("guard", "H1") } }));
            t = Run(t, EndTurn).State; // round 1: primes
            t = Run(t, Move("A7", "A8")).State;
            var r = Run(t, EndTurn); // round 2: cancels (nothing in the area)
            Assert.That(EventsOf<HazardCancelled>(r.Events).Any(e => e.Hazard == "vent_b5"), Is.True);
            t = r.State;
            t = Run(t, Move("A7", "A6")).State;
            r = Run(t, EndTurn); // round 3: primes again
            Assert.That(EventsOf<HazardPrimed>(r.Events).Any(e => e.Hazard == "vent_b5"), Is.True);
        }

        [Test]
        public void VentGasIsOneHitOfPoisonTypeDamageThatIgnoresArmorAndDoesNotApplyPoison()
        {
            var s = Scenario(new ScenarioSpec { Player = "A6", Units = { U("guard", "H1") } });
            s = Run(s, EndTurn).State;
            var r = Run(s, EndTurn);
            var hit = EventsOf<DamageDealt>(r.Events).First(e => e.Source.Kind == SourceKind.Hazard);
            Assert.That(hit.DamageType, Is.EqualTo(DamageType.Poison));
            Assert.That(hit.Amount, Is.EqualTo(1));
            Assert.That(EventsOf<StatusApplied>(r.Events).Any(e => e.Status == StatusId.Poison), Is.False);
        }

        [Test]
        public void ThePanelGridDischargesOntoEveryUnitOnOrAdjacentToD5OrE5()
        {
            var s = Scenario(new ScenarioSpec { Player = "F4", Units = { U("hatchling", "C5"), U("guard", "H1") } });
            s = Run(s, EndTurn).State;
            // Pin everyone in place for the firing round.
            foreach (var u in s.Units)
            {
                if (u.Id != 0) u.Statuses.Pinned = Pin();
            }
            s.Units[0].Pos = new Pos(5, 3);
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<HazardFired>(r.Events).Any(e => e.Hazard == "panel"), Is.True);
            Assert.That(EventsOf<DamageDealt>(r.Events).Where(e => e.Source.TextId == "panel").All(e => e.DamageType == DamageType.Electric && e.Raw == 2), Is.True);
        }
    }
}
