#nullable enable
using LabEscape.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LabEscape.Core.Tests
{
    /// <summary>The JavaScript behaviour the reports rely on. Every expected value here was printed by Node.</summary>
    public class JsTextTests
    {
        [TestCase(1.45, 1, "1.4")]
        [TestCase(2.5, 0, "3")]
        [TestCase(0.5, 0, "1")]
        [TestCase(1.5, 0, "2")]
        [TestCase(-0.04, 1, "-0.0")]
        [TestCase(-0.0, 1, "0.0")]
        [TestCase(1.005, 2, "1.00")]
        [TestCase(0.000001, 3, "0.000")]
        [TestCase(123.456, 0, "123")]
        [TestCase(72.4, 1, "72.4")]
        [TestCase(100.0, 1, "100.0")]
        [TestCase(1e21, 2, "1e+21")]
        public void ToFixedRoundsTheExactBinaryValue(double value, int digits, string expected)
        {
            Assert.That(JsText.ToFixed(value, digits), Is.EqualTo(expected));
        }

        [Test]
        public void IndentedJsonMatchesJsonStringifyWithASpaceOfTwo()
        {
            var token = JObject.Parse("{b:{},a:[],c:[1,[2],{d:null}],e:'x\\ny'}");
            var expected = "{\n  \"b\": {},\n  \"a\": [],\n  \"c\": [\n    1,\n    [\n      2\n    ],\n    {\n      \"d\": null\n    }\n  ],\n  \"e\": \"x\\ny\"\n}";
            Assert.That(Hash.Stringify(token, 2), Is.EqualTo(expected));
        }

        [Test]
        public void IndentedJsonPutsArrayIndexKeysFirstAsJavaScriptObjectsDo()
        {
            Assert.That(Hash.Stringify(JObject.Parse("{a:1,'2':2}"), 2), Is.EqualTo("{\n  \"2\": 2,\n  \"a\": 1\n}"));
        }

        [TestCase(2.5, 3)]
        [TestCase(-2.5, -2)]
        [TestCase(0.49999999999999994, 0)]
        [TestCase(599.6, 600)]
        public void RoundHalfUpMatchesMathRound(double x, double expected)
        {
            Assert.That(Stats.RoundHalfUp(x), Is.EqualTo(expected));
        }

        [TestCase("P_1", "P1", -1)]
        [TestCase("p3", "P3", -1)]
        [TestCase("P10", "P2", -1)]
        [TestCase("a-b", "a_b", 1)]
        [TestCase("hazard", "round cap", -1)]
        [TestCase("B", "a", 1)]
        [TestCase("same", "same", 0)]
        [TestCase("ab", "abc", -1)]
        [TestCase("Ab", "aC", -1)]
        public void LocaleOrderSortsAsLocaleCompareDoes(string a, string b, int expected)
        {
            Assert.That(System.Math.Sign(LocaleOrder.Compare(a, b)), Is.EqualTo(expected));
            Assert.That(System.Math.Sign(LocaleOrder.Compare(b, a)), Is.EqualTo(-expected));
        }

        [TestCase("{'a':1}")]
        [TestCase("[1,]")]
        [TestCase("NaN")]
        [TestCase("0x10")]
        [TestCase("/* c */ 1")]
        public void StrictJsonRefusesWhatJsonParseRefuses(string text)
        {
            Assert.That(StrictJson.TryParse(text, out _), Is.False);
        }

        [TestCase("16", JTokenType.Integer)]
        [TestCase("1.5", JTokenType.Float)]
        [TestCase("\"x\"", JTokenType.String)]
        [TestCase("[1]", JTokenType.Array)]
        [TestCase("null", JTokenType.Null)]
        public void StrictJsonAcceptsWhatJsonParseAccepts(string text, JTokenType type)
        {
            Assert.That(StrictJson.TryParse(text, out var token), Is.True);
            Assert.That(token.Type, Is.EqualTo(type));
        }
    }
}
