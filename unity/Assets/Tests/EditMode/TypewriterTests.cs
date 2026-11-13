#nullable enable
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The terminals' text, printed a character at a time as the web client's `useTypewriter` does.</summary>
    public class TypewriterTests
    {
        private static readonly string[] Lines = { "abc", "", "de" };

        [Test]
        public void NothingIsPrintedUntilTheDelayHasPassed()
        {
            var t = new Typewriter(Lines, 10f, 0.5f);
            t.Tick(0.4f);
            Assert.That(t.Count, Is.EqualTo(0));
            Assert.That(t.Shown(), Is.EqualTo(new[] { "", "", "" }));
            Assert.That(t.Done, Is.False);
        }

        [Test]
        public void LinesPrintInTurnAtTheSpeedGiven()
        {
            var t = new Typewriter(Lines, 10f, 0f);
            t.Tick(0.2f);
            Assert.That(t.Shown(), Is.EqualTo(new[] { "ab", "", "" }));
            Assert.That(t.TypingLine(), Is.EqualTo(0));
            t.Tick(0.2f);
            Assert.That(t.Shown(), Is.EqualTo(new[] { "abc", "", "d" }), "an empty line takes no time");
            Assert.That(t.TypingLine(), Is.EqualTo(2));
            t.Tick(0.1f);
            Assert.That(t.Shown(), Is.EqualTo(Lines));
            Assert.That(t.Done, Is.True);
            Assert.That(t.TypingLine(), Is.EqualTo(-1));
        }

        [Test]
        public void FinishPrintsTheRestAtOnceAndMoreTimeChangesNothing()
        {
            var t = new Typewriter(Lines, 10f, 5f);
            t.Finish();
            Assert.That(t.Shown(), Is.EqualTo(Lines));
            t.Tick(100f);
            Assert.That(t.Shown(), Is.EqualTo(Lines));
        }

        [Test]
        public void NoTextIsDoneAtOnce()
        {
            var t = new Typewriter(new string[0], 10f, 1f);
            Assert.That(t.Done, Is.True);
            Assert.That(t.Shown(), Is.Empty);
        }

        [Test]
        public void TheTitleSaysWhereYouAreAndEndsWithTheMessage()
        {
            var c = GameContent.Load();
            var lines = ScreenText.TitleLines(c);
            Assert.That(lines.Count, Is.GreaterThan(ScreenText.TitleStatusLines));
            Assert.That(lines[ScreenText.TitleStatusLines - 1], Does.Contain(c.DefaultRoom.Name.ToUpperInvariant()));
            Assert.That(lines.Last(), Does.Contain("Get out. Fast."));
            Assert.That(ScreenText.TitleFooter(c), Does.Contain(c.Hash).And.Contain(GameStates.EngineVersion.ToString()));
        }
    }
}
