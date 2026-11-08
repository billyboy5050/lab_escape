#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>Ported from tests/los.test.ts.</summary>
    public class LosTests
    {
        private static readonly Board Board = C.Board;
        private static readonly List<Pos> AllTiles = Board.AllTiles();

        /// <summary>Sight with a plain trace from a to b (what the spec warns against).</summary>
        private static bool PlainLos(Pos a, Pos b)
        {
            var pts = Grid.Bresenham(a, b);
            for (var i = 1; i < pts.Count - 1; i++)
            {
                if (Board.BlocksSight(pts[i])) return false;
            }
            return true;
        }

        [Test]
        public void IsSymmetricForEveryPairOfTilesOnArmoryLockdown()
        {
            foreach (var a in AllTiles)
            {
                foreach (var b in AllTiles) Assert.That(Board.HasLos(a, b), Is.EqualTo(Board.HasLos(b, a)), $"{Name(a)} <-> {Name(b)}");
            }
        }

        [Test]
        public void UsesTheSameTileListInBothDirections()
        {
            foreach (var a in AllTiles)
            {
                foreach (var b in AllTiles)
                {
                    var ab = Grid.LineTiles(a, b).Select(Name);
                    var ba = Grid.LineTiles(b, a).Select(Name).Reverse();
                    Assert.That(ab, Is.EqualTo(ba));
                }
            }
        }

        [Test]
        public void StartsEveryLineAtTheTileItIsTracedFrom()
        {
            Assert.That(Name(Grid.LineTiles(T("B8"), T("C3"))[0]), Is.EqualTo("B8"));
            Assert.That(Name(Grid.LineTiles(T("C3"), T("B8"))[0]), Is.EqualTo("C3"));
        }

        [Test]
        public void APlainTraceDisagreesByDirectionFor88TilePairsOnThisMap()
        {
            // The spec's figure counts pairs of tiles that do not themselves block sight (64 minus 2 walls and 3 pillars).
            var open = AllTiles.Where(p => !Board.BlocksSight(p)).ToList();
            Assert.That(open, Has.Count.EqualTo(59));
            var unordered = 0;
            for (var i = 0; i < open.Count; i++)
            {
                for (var j = i + 1; j < open.Count; j++)
                {
                    if (PlainLos(open[i], open[j]) != PlainLos(open[j], open[i])) unordered++;
                }
            }
            Assert.That(unordered, Is.EqualTo(88));
        }

        [Test]
        public void IsBlockedByPillarsAndWallsOnly()
        {
            Assert.That(Board.HasLos(T("B3"), T("B5")), Is.False); // pillar B4
            Assert.That(Board.HasLos(T("A3"), T("A5")), Is.False); // wall A4
            Assert.That(Board.HasLos(T("C1"), T("F1")), Is.True); // shutter D1, E1 does not block sight
            Assert.That(Board.HasLos(T("H4"), T("H6")), Is.True); // wall gun H5 does not block sight
            Assert.That(Board.HasLos(T("C5"), T("C7")), Is.False); // pillar C6
        }

        [Test]
        public void AdjacentTilesAlwaysSeeEachOtherIncludingDiagonallyPastAPillar()
        {
            foreach (var a in AllTiles)
            {
                foreach (var b in Board.Neighbors8(a)) Assert.That(Board.HasLos(a, b), Is.True);
            }
            Assert.That(Board.HasLos(T("B5"), T("C6")), Is.True);
        }

        [Test]
        public void HandlesCornerCasesThatGrazeAPillar()
        {
            // C3 to B8 passes C4, C5, B6, B7: clear of B4 and C6.
            Assert.That(Grid.LineTiles(T("C3"), T("B8")).Select(Name), Is.EqualTo(new[] { "C3", "C4", "C5", "B6", "B7", "B8" }));
            Assert.That(Board.HasLos(T("C3"), T("B8")), Is.True);
            // D3 to B8 passes C6.
            Assert.That(Grid.LineTiles(T("D3"), T("B8")).Select(Name), Does.Contain("C6"));
            Assert.That(Board.HasLos(T("D3"), T("B8")), Is.False);
            // A shallow diagonal past F6.
            var line = Grid.LineTiles(T("E5"), T("G7"));
            Assert.That(Board.HasLos(T("E5"), T("G7")), Is.EqualTo(line.Skip(1).Take(line.Count - 2).All(p => !Board.BlocksSight(p))));
        }

        [Test]
        public void ATargetStandingOnAPillarTileIsNotHiddenByThatPillar()
        {
            // Endpoints never block their own line.
            Assert.That(Board.HasLos(T("B3"), T("B4")), Is.True);
        }
    }

    /// <summary>Ported from tests/simultaneous-deaths.test.ts.</summary>
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

    /// <summary>Ported from tests/stats.test.ts.</summary>
    public class StatsTests
    {
        [Test]
        public void TakesTheMiddleValueOfAnOddCount() => Assert.That(Stats.Median(new double[] { 30, 10, 20 }), Is.EqualTo(20));

        [Test]
        public void AveragesTheTwoMiddleValuesOfAnEvenCount()
        {
            Assert.That(Stats.Median(new double[] { 10000, 90000 }), Is.EqualTo(50000));
            Assert.That(Stats.Median(new double[] { 4, 1, 3, 2 }), Is.EqualTo(2.5));
        }

        [Test]
        public void DoesNotReorderItsInputAndIsZeroForAnEmptyList()
        {
            var xs = new double[] { 3, 1, 2 };
            Stats.Median(xs);
            Assert.That(xs, Is.EqualTo(new double[] { 3, 1, 2 }));
            Assert.That(Stats.Median(new double[0]), Is.EqualTo(0));
        }
    }
}
