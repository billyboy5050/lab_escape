#nullable enable
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>The expected values below were produced by src/util/hash.ts, so these tests compare the two implementations.</summary>
    public class HashTests
    {
        [TestCase("", "0bdcb81aee8d83", "10047792def104", "00d08b1d38333f")]
        [TestCase("a", "1c2ba782c97901", "077cbc4bf769d0", "1a04f8b991a49b")]
        [TestCase("hello world", "0b9417d15d1014", "144aa694233d12", "0711238afe2201")]
        [TestCase("\u00e9", "1efd8451e926d5", "12862c007ce895", "0f51782b0fc537")]
        [TestCase("\U0001F600", "10ca036ef9b4ae", "008946dec6a9fb", "18b48fa8bd2c15")]
        [TestCase("{\"a\":1}", "1034b77cc1d846", "132f75e3f02e45", "17650923b2dc63")]
        public void HashStringMatchesTypeScript(string text, string seed0, string seed12345, string seedMinus7)
        {
            Assert.That(Hash.HashString(text), Is.EqualTo(seed0));
            Assert.That(Hash.HashString(text, 12345), Is.EqualTo(seed12345));
            Assert.That(Hash.HashString(text, -7), Is.EqualTo(seedMinus7));
        }

        [Test]
        public void HashStringMatchesTypeScriptOnALoneSurrogate()
        {
            // Not a [TestCase]: attribute arguments are stored as UTF-8, which cannot hold a lone surrogate.
            Assert.That(Hash.HashString("\ud800"), Is.EqualTo("1fb54e0827e5c5"));
            Assert.That(Hash.HashString("\ud800", 12345), Is.EqualTo("00d3d5d8b90664"));
        }

        [Test]
        public void HashStringMatchesTypeScriptOnALongString()
        {
            Assert.That(Hash.HashString(new string('x', 1000)), Is.EqualTo("1a7942aee5273b"));
        }

        [Test]
        public void NumbersAreWrittenAsJavaScriptWritesThem()
        {
            var json = JsonIn.Parse("[0.4,1e21,1e-7,123456789012345680000,1.5e300,5e-324,0.000001,-0,100,1.0,0.30000000000000004,1.7976931348623157e308,1e20,-2.5,1e-6,12345.678]");
            Assert.That(Hash.CanonicalJson(json), Is.EqualTo("[0.4,1e+21,1e-7,123456789012345680000,1.5e+300,5e-324,0.000001,0,100,1,0.30000000000000004,1.7976931348623157e+308,100000000000000000000,-2.5,0.000001,12345.678]"));
            Assert.That(Hash.HashJson(json), Is.EqualTo("121c76213e0ccd"));
        }

        [Test]
        public void KeysAreOrderedAsJavaScriptOrdersThem()
        {
            // Array-index keys first in numeric order, then the rest by UTF-16 code unit, so the emoji sorts before U+FFFF.
            var json = JsonIn.Parse("{\"b\":1,\"a\":2,\"\\u00e9\":3,\"Z\":4,\"10\":5,\"9\":6,\"\":7,\"\\ud83d\\ude00\":8,\"\\uffff\":9,\"a b\":10,\"09\":11,\"4294967295\":12,\"4294967294\":13}");
            Assert.That(Hash.CanonicalJson(json), Is.EqualTo("{\"9\":6,\"10\":5,\"4294967294\":13,\"\":7,\"09\":11,\"4294967295\":12,\"Z\":4,\"a\":2,\"a b\":10,\"b\":1,\"\u00e9\":3,\"\U0001F600\":8,\"\uffff\":9}"));
            Assert.That(Hash.HashJson(json), Is.EqualTo("02af0660018a36"));
        }

        [Test]
        public void StringsAreEscapedAsJsonStringifyEscapesThem()
        {
            // Built directly because JSON.NET's reader replaces a lone surrogate, which content files have no reason to hold.
            var json = new JArray("quote\"backslash\\", "\u0001\u001f\u007f", "\b\f\n\r\t", "\u2028\u2029", "\u00e9\U0001F600", "\ud800", "\udc00x", "<>&'/");
            Assert.That(Hash.CanonicalJson(json), Is.EqualTo("[\"quote\\\"backslash\\\\\",\"\\u0001\\u001f\u007f\",\"\\b\\f\\n\\r\\t\",\"\u2028\u2029\",\"\u00e9\U0001F600\",\"\\ud800\",\"\\udc00x\",\"<>&'/\"]"));
            Assert.That(Hash.HashJson(json), Is.EqualTo("1ff4d375540a63"));
        }

        [Test]
        public void TextThatLooksLikeADateStaysText()
        {
            var json = JsonIn.Parse("{\"nested\":{\"z\":[{\"y\":1,\"x\":null},true,false],\"a\":{\"d\":\"2026-11-08T10:00:00Z\"}}}");
            Assert.That(Hash.CanonicalJson(json), Is.EqualTo("{\"nested\":{\"a\":{\"d\":\"2026-11-08T10:00:00Z\"},\"z\":[{\"x\":null,\"y\":1},true,false]}}"));
            Assert.That(Hash.HashJson(json), Is.EqualTo("06cdd51f87185b"));
        }

        [Test]
        public void TheShippedContentHasTheHashTheGoldenReplaysRecord()
        {
            Assert.That(C.Hash, Is.EqualTo("08a289f83c157f"));
        }
    }

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
