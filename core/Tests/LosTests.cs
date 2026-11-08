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
        private static readonly Board Board = C.DefaultRoom.Board;
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
}
