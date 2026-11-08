#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>Ported from tests/intercom.test.ts.</summary>
    public class IntercomTests
    {
        [Test]
        public void PlaysTheRound1LineWhenTheFightStarts()
        {
            var g = Engine.NewGame(C, LoadoutOf("sidearm"));
            Assert.That(EventsOf<IntercomLine>(g.Events).Select(e => e.Line), Is.EqualTo(new[] { "round1" }));
            Assert.That(g.State.Intercom.Log[0].Round, Is.EqualTo(1));
            Assert.That(g.State.Intercom.Log[0].Line, Is.EqualTo("round1"));
        }

        [Test]
        public void PlaysTheVagueWaveHintsAtTheStartOfRounds3And6()
        {
            var s = Scenario(new ScenarioSpec { Player = "A8", Round = 2, Units = { U("guard", "H1") } });
            s.Objects.AddRange(new[] { ShieldAt(90, 6, 0, 9, 2), ShieldAt(91, 6, 1, 9, 2) });
            var lines = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var r = Run(s, EndTurn);
                lines.AddRange(EventsOf<IntercomLine>(r.Events).Select(e => $"{r.State.Round}:{e.Line}"));
                s = r.State;
            }
            Assert.That(lines.Where(l => l.EndsWith("round3") || l.EndsWith("round6")), Is.EqualTo(new[] { "3:round3", "6:round6" }));
        }

        private static GameState TwoDeaths() => Scenario(new ScenarioSpec { Player = "A8", Units = { U("flamer", "E3", hp: 1), U("medic", "F3", hp: 1), U("guard", "H8") } });

        [Test]
        public void PlaysAtMostOneLinePerPhaseAndTheRestQueueInTableOrder()
        {
            var run = InWorld(TwoDeaths(), w => w.AreaDamage(new[] { new Pos(4, 2), new Pos(5, 2) }, 5, DamageType.Acid, Src()));
            // Medic is down comes before Burner is down in the table, so it plays first; the other waits.
            Assert.That(EventsOf<IntercomLine>(run.Events).Select(e => e.Line), Is.EqualTo(new[] { "medicDies" }));
            Assert.That(run.S.Intercom.Queue, Is.EqualTo(new[] { "flamerDies" }));
        }

        [Test]
        public void AQueuedLinePlaysInTheNextPhase()
        {
            var mid = InWorld(TwoDeaths(), w => w.AreaDamage(new[] { new Pos(4, 2), new Pos(5, 2) }, 5, DamageType.Acid, Src())).S;
            Assert.That(EventsOf<IntercomLine>(Run(mid, EndTurn).Events)[0].Line, Is.EqualTo("flamerDies"));
        }

        [Test]
        public void ThePlayersDeathLinePlaysAtOnceAndClearsTheQueue()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", PlayerHp = 3, Units = { U("guard", "B6") } });
            s.Intercom.Queue = new List<string> { "flamerDies" };
            s.Intercom.LineThisPhase = true;
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<IntercomLine>(r.Events).Last().Line, Is.EqualTo("playerDies"));
            Assert.That(r.State.Intercom.Queue, Is.Empty);
        }

        [Test]
        public void EachLinePlaysAtMostOncePerFight()
        {
            var s = Scenario(new ScenarioSpec { Player = "A6", Units = { U("guard", "H1") } });
            s.Intercom.Played.Add("firstHazardPrimed");
            var r = Run(s, EndTurn);
            Assert.That(EventsOf<HazardPrimed>(r.Events), Is.Not.Empty);
            Assert.That(EventsOf<IntercomLine>(r.Events).Any(e => e.Line == "firstHazardPrimed"), Is.False);
        }

        private static readonly Content TwoHpLines = BuildTwoHpLines();

        private static Content BuildTwoHpLines()
        {
            var lines = new JArray(((JArray)C.Files["intercom"]!["lines"]!).Where(l => l["trigger"]!["type"]!.Value<string>() != "hpBelow"));
            lines.Add(JToken.FromObject(new { id = "wardenHalf", trigger = new { type = "hpBelow", def = "warden", threshold = 10 }, text = "Warden, report." }));
            lines.Add(JToken.FromObject(new { id = "wardenCritical", trigger = new { type = "hpBelow", def = "warden", threshold = 5 }, text = "Warden, fall back." }));
            return ContentWith(("intercom.lines", lines));
        }

        private static (string[] Lines, string[] Queue) Hit(int hp, int amount)
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", Units = { U("warden", "B5", hp: hp) }, Content = TwoHpLines });
            var r = InWorld(s, w => w.DamageUnit(w.Unit(1)!, amount, DamageType.Acid, Src()), TwoHpLines);
            return (EventsOf<IntercomLine>(r.Events).Select(e => e.Line).ToArray(), r.S.Intercom.Queue.ToArray());
        }

        [Test]
        public void PlaysOnlyTheLineWhoseThresholdWasCrossedNotTheLowerOneAsWell()
        {
            // 11 to 9 crosses 10 but not 5: the 5 HP warning must not be played or queued early.
            var (lines, queue) = Hit(11, 2);
            Assert.That(lines, Is.EqualTo(new[] { "wardenHalf" }));
            Assert.That(queue, Is.Empty);
        }

        [Test]
        public void PlaysTheLowerLineWhenItsOwnThresholdIsCrossedLater()
        {
            var (lines, queue) = Hit(6, 2);
            Assert.That(lines, Is.EqualTo(new[] { "wardenCritical" }));
            Assert.That(queue, Is.Empty);
        }

        [Test]
        public void OneHitAcrossBothThresholdsRaisesBothLinesTheSecondQueuedForTheNextPhase()
        {
            var (lines, queue) = Hit(12, 8);
            Assert.That(lines, Is.EqualTo(new[] { "wardenHalf" }));
            Assert.That(queue, Is.EqualTo(new[] { "wardenCritical" }));
        }

        [Test]
        public void ReportsTheWardenBelow10HpAndThePlayerBelow4Hp()
        {
            var s = Scenario(new ScenarioSpec { Player = "B8", PlayerHp = 5, Units = { U("warden", "B5", hp: 11) } });
            var run = InWorld(s, w =>
            {
                w.DamageUnit(w.Unit(1)!, 2, DamageType.Acid, Src());
                w.DamageUnit(w.Player!, 2, DamageType.Acid, Src());
            });
            Assert.That(EventsOf<IntercomLine>(run.Events).Select(e => e.Line), Is.EqualTo(new[] { "wardenLow" }));
        }
    }
}
