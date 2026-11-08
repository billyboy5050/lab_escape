#nullable enable
using NUnit.Framework;

namespace LabEscape.Core.Tests
{
    public class TileTests
    {
        [TestCase("A1", 0, 0)]
        [TestCase("D5", 3, 4)]
        [TestCase("h8", 7, 7)]
        [TestCase(" B2\n", 1, 1)]
        [TestCase("A10", 0, 9)]
        [TestCase("A0", 0, -1)]
        public void ParsesTileNames(string name, int x, int y) => Assert.That(Tiles.Parse(name), Is.EqualTo(new Pos(x, y)));

        [TestCase("")]
        [TestCase("A")]
        [TestCase("11")]
        [TestCase("A100")]
        [TestCase("\u00c91")]
        [TestCase("A\u0663")] // An Arabic-Indic digit, which .NET's \d would have accepted.
        public void RejectsWhatIsNotATileName(string name) => Assert.That(Tiles.IsName(name), Is.False);

        [Test]
        public void NamesTiles() => Assert.That(Tiles.Name(new Pos(3, 4)), Is.EqualTo("D5"));
    }
}
