#nullable enable
using NUnit.Framework;

namespace LabEscape.Core.Tests
{
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
