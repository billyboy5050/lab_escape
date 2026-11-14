#nullable enable
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class SimultaneousDeathsTests
    {
        [Test]
        public void StillReportsTheEnemiesThatDiedTogetherWithThePlayer()
        {
            var s = Scenario(new ScenarioSpec { Player = "D4", PlayerHp = 1, Units = { U("guard", "E4", hp: 1), U("guard", "D5", hp: 1) } });
            var tiles = new[] { T("D4"), T("E4"), T("D5") };
            var run = InWorld(s, w => w.AreaDamage(tiles, 50, DamageType.Explosive, w.PlayerEffect("test")));
            Assert.That(run.S.Outcome?.Result, Is.EqualTo(FightResult.Lose));
            Assert.That(EventsOf<UnitDied>(run.Events).Select(e => e.Id), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(EventsOf<FightEnded>(run.Events), Has.Count.EqualTo(1));
            Assert.That(run.S.Units.Any(u => u.Dead), Is.False);
        }

        [Test]
        public void LeavesNoIntercomLineQueuedBehindThePlayerDeathLine()
        {
            var s = Scenario(new ScenarioSpec { Player = "D4", PlayerHp = 1, Units = { U("medic", "E4", hp: 1), U("warden", "D5", hp: 1) } });
            var run = InWorld(s, w => w.AreaDamage(new[] { T("D4"), T("E4"), T("D5") }, 50, DamageType.Explosive, w.PlayerEffect("test")));
            Assert.That(run.S.Outcome?.Result, Is.EqualTo(FightResult.Lose));
            Assert.That(run.S.Intercom.Queue, Is.Empty);
        }
    }
}
