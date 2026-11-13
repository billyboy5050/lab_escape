#nullable enable
using System.IO;
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The incident report: what the fight's summary says, laid out as the web client's result screen has it.</summary>
    public class ReportModelTests
    {
        private static readonly Content C = GameContent.Load();

        private static (FightRecord Record, FightSummary Summary, GameState State) Won()
        {
            var rec = Records.CheckCompatible(C, JsonIn.Parse(File.ReadAllText(GameContent.RepoPath("reports", "winning-line-hybrid.json"))));
            var r = Records.Replay(C, rec);
            return (rec, Summaries.Summarize(r.Events, r.State), r.State);
        }

        private static (FightSession Session, FightSummary Summary) Lost()
        {
            var p = C.Presets.First(x => x.Id == "tech");
            var session = new FightSession(C, new Loadout(p.Abilities, p.Upgrades), new FightSettings());
            Assert.That(session.Apply(new DebugSetHp(0, 1)).Ok, Is.True);
            for (var i = 0; i < 60 && session.State.Outcome == null; i++) session.Apply(new EndTurnCommand());
            Assert.That(session.State.Outcome?.Result, Is.EqualTo(FightResult.Lose), "a player at 1 HP who never acts is beaten");
            return (session, session.Summary());
        }

        [Test]
        public void AWonFightIsALockdownBrokenWithItsRoundsAndWaves()
        {
            var (rec, summary, state) = Won();
            var m = ReportModel.From(C, C.DefaultRoom, summary, state, rec);
            Assert.That(m.Won, Is.True);
            Assert.That(m.Heading, Is.EqualTo("Lockdown broken"));
            Assert.That(m.Kicker, Is.EqualTo($"Incident report · {C.DefaultRoom.Name}"));
            Assert.That(m.Pills[0], Is.EqualTo($"Won {summary.Rounds} round{(summary.Rounds == 1 ? "" : "s")}"));
            Assert.That(m.Pills[1], Is.EqualTo($"Waves seen {summary.WavesSpawned}/{C.DefaultRoom.Waves.Count}"));
            Assert.That(m.Pills[2], Does.StartWith("Friendly-fire share of your damage ").And.EndWith("%"));
            Assert.That(m.Loadout, Does.StartWith("Loadout: ").And.Contain(C.Abilities[summary.Loadout.Abilities[0]].Name));
        }

        [Test]
        public void TheBarsAreTheSummarysTalliesInTheWebClientsOrder()
        {
            var (rec, summary, state) = Won();
            var m = ReportModel.From(C, C.DefaultRoom, summary, state, rec);
            Assert.That(m.Sections.Select(s => s.Title), Is.EqualTo(new[] { "Damage you took, by source", "Damage dealt to enemies, by source", "Damage your side took (minions, shields, eggs)" }));
            var tallies = new[] { summary.PlayerDamageTaken, summary.EnemyDamageTaken, summary.SideDamageTaken };
            for (var i = 0; i < 3; i++)
            {
                Assert.That(m.Sections[i].Bars.Select(b => b.Category), Is.EqualTo(new[] { SourceCategory.Enemy, SourceCategory.Hazard, SourceCategory.Player, SourceCategory.Minion }));
                Assert.That(m.Sections[i].Bars.Select(b => b.Value), Is.EqualTo(ReportModel.CategoryOrder.Select(cat => tallies[i][cat])));
                Assert.That(m.Sections[i].Bars.Select(b => b.Label), Is.EqualTo(new[] { "Enemies", "Lab hazards", "Your own abilities", "Your own minions" }));
                Assert.That(m.Sections[i].Max, Is.GreaterThanOrEqualTo(1));
                Assert.That(m.Sections[i].Bars.Max(b => b.Value), Is.LessThanOrEqualTo(m.Sections[i].Max));
            }
            Assert.That(m.Sections[0].Note, Is.EqualTo($"Friendly fire: {summary.FriendlyFireToPlayer} of {summary.PlayerDamageTaken.Total} ({(summary.PlayerDamageTaken.Total > 0 ? (int)Stats.RoundHalfUp(100.0 * summary.FriendlyFireToPlayer / summary.PlayerDamageTaken.Total) : 0)}%)"));
            Assert.That(m.Sections[2].Note, Is.EqualTo($"Friendly fire to your side: {summary.FriendlyFireToSide}"));
        }

        [Test]
        public void TheFightsFactsAreInTheWebClientsOrderAndSaySoWhenThereIsNothing()
        {
            var (rec, summary, state) = Won();
            var m = ReportModel.From(C, C.DefaultRoom, summary, state, rec);
            Assert.That(m.Facts.Select(f => f.Label), Is.EqualTo(new[] { "Kills", "Peak minions", "Hazard fires", "Abilities used", "Median turn time", "Warden" }));
            var facts = m.Facts.ToDictionary(f => f.Label, f => f.Value);
            foreach (var kill in summary.Kills) Assert.That(facts["Kills"], Does.Contain($"{kill.Value} {C.Units[kill.Key].Name}"));
            Assert.That(facts["Peak minions"], Is.EqualTo($"{summary.PeakMinions} of {C.Rules.Caps.Minions} (lost {summary.MinionsLost})"));
            foreach (var used in summary.AbilitiesUsed) Assert.That(facts["Abilities used"], Does.Contain($"x{used.Value}"));
            Assert.That(facts["Median turn time"], Is.EqualTo("-"), "a recorded line made with no clock has no turn times");
            Assert.That(facts["Warden"], Does.Contain("reached you").Or.Contain("never reached you"));
            Assert.That(m.Telemetry, Is.EqualTo($"A replay file reproduces this fight exactly (engine {rec.EngineVersion}, content {rec.ContentHash})."));
        }

        [Test]
        public void TheMedianTurnTimeIsInSecondsToOnePlace()
        {
            var (rec, summary, state) = Won();
            rec.Meta = rec.Meta ?? new RecordMeta();
            rec.Meta.TurnTimesMs = new System.Collections.Generic.List<double> { 1000, 3000, 8000 };
            var m = ReportModel.From(C, C.DefaultRoom, summary, state, rec);
            Assert.That(m.Facts.First(f => f.Label == "Median turn time").Value, Is.EqualTo("3.0 s"));
        }

        [Test]
        public void ALostFightNamesWhatKilledYou()
        {
            var (session, summary) = Lost();
            var m = ReportModel.From(C, C.DefaultRoom, summary, session.State, session.Record);
            Assert.That(m.Won, Is.False);
            Assert.That(m.Heading, Is.EqualTo("Intruder down"));
            Assert.That(m.Verdict, Does.StartWith("Cause of death: "));
            if (summary.CauseOfDeath != null) Assert.That(m.Verdict, Does.Contain(summary.CauseOfDeath.Source).And.Contain(summary.CauseOfDeath.Via));
            Assert.That(m.Pills[0], Does.StartWith("Survived "));
        }

        [Test]
        public void FriendlyFirePastTheAlarmIsWarnedAboutAndAtTheAlarmItIsNot()
        {
            var (rec, summary, state) = Won();
            summary.PlayerDamageTaken = new DamageTally { Enemy = 13, Player = 7 };
            summary.FriendlyFireToPlayer = 7;
            Assert.That(ReportModel.From(C, C.DefaultRoom, summary, state, rec).FriendlyFireWarns, Is.False, "35% is the alarm, not past it");
            summary.FriendlyFireToPlayer = 8;
            summary.PlayerDamageTaken = new DamageTally { Enemy = 12, Player = 8 };
            var m = ReportModel.From(C, C.DefaultRoom, summary, state, rec);
            Assert.That(m.FriendlyFireWarns, Is.True);
            Assert.That(m.Sections[0].NoteWarns, Is.True);
            summary.PlayerDamageTaken = new DamageTally();
            summary.FriendlyFireToPlayer = 0;
            Assert.That(ReportModel.From(C, C.DefaultRoom, summary, state, rec).Pills[2], Is.EqualTo("Friendly-fire share of your damage 0%"), "no damage is no division by zero");
        }

        [Test]
        public void TheIntercomLinesAreTheOnesTheFightHeard()
        {
            var (rec, summary, state) = Won();
            var m = ReportModel.From(C, C.DefaultRoom, summary, state, rec);
            Assert.That(m.Intercom.Select(l => l.Text), Is.EqualTo(state.Intercom.Log.Select(l => l.Text)));
            Assert.That(m.Intercom.Select(l => l.Round), Is.EqualTo(state.Intercom.Log.Select(l => l.Round)));
        }
    }
}
