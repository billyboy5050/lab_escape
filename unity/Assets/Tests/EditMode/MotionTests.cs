#nullable enable
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The reduce-motion setting and the keys that are the system's.</summary>
    public class MotionTests
    {
        [TearDown]
        public void PutMotionBack() => Motion.Reduced = false;

        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, true)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, true)]
        [TestCase(true, true, true, true)]
        public void AControlCommandOrAltKeyBelongsToTheSystem(bool control, bool command, bool alt, bool modified)
        {
            Assert.That(KeyRules.Modified(control, command, alt), Is.EqualTo(modified));
        }

        [Test]
        public void ShiftZDoesNotUndo()
        {
            Assert.That(KeyRules.Undoes(false), Is.True);
            Assert.That(KeyRules.Undoes(true), Is.False);
        }

        [Test]
        public void TheSettingIsRememberedAndOnlyAOneTurnsItOn()
        {
            var prefs = new MemoryPrefs();
            Assert.That(Motion.Load(prefs), Is.False, "off until it is turned on");
            Motion.Save(prefs, true);
            Assert.That(prefs.Get(Motion.PrefKey), Is.EqualTo("1"));
            Assert.That(Motion.Load(prefs), Is.True);
            Motion.Save(prefs, false);
            Assert.That(Motion.Load(prefs), Is.False);
        }

        [TestCase("")]
        [TestCase("0")]
        [TestCase("true")]
        [TestCase("yes")]
        [TestCase(" 1")]
        [TestCase("1 ")]
        [TestCase("11")]
        [TestCase("{\"reduced\":true}")]
        public void WhatTheStoreGivesBackIsUntrustedSoAnythingButOneIsOff(string stored)
        {
            var prefs = new MemoryPrefs();
            prefs.Set(Motion.PrefKey, stored);
            Assert.That(Motion.Load(prefs), Is.False);
        }

        [Test]
        public void TheBoardStopsForABlowAndSlowsForTheLastOne()
        {
            var clock = new BoardClock();
            clock.Freeze(0.1f);
            Assert.That(clock.Frozen, Is.True);
            Assert.That(clock.Step(0.05f), Is.EqualTo(0f).Within(1e-6f), "stopped");
            clock = new BoardClock();
            clock.Slow(0.25f, 1f);
            Assert.That(clock.Step(0.1f), Is.EqualTo(0.025f).Within(1e-5f), "slowed");
        }

        [Test]
        public void TurningItOnPartWayThroughAStopOrASlowReturnsTheBoardToRealTime()
        {
            var clock = new BoardClock();
            clock.Freeze(0.5f);
            clock.Slow(0.25f, 5f);
            Assert.That(clock.Step(0.1f), Is.EqualTo(0f).Within(1e-6f), "stopped");
            Motion.Reduced = true;
            Assert.That(clock.Frozen, Is.True, "the stop has not been told yet");
            Assert.That(clock.Step(0.1f), Is.EqualTo(0.1f).Within(1e-6f), "and it ends at once");
            Assert.That(clock.Frozen, Is.False);
            Assert.That(clock.Step(0.1f), Is.EqualTo(0.1f).Within(1e-6f), "the slow-down too");
        }

        [Test]
        public void WithReducedMotionTheBoardNeverStopsAndNeverSlows()
        {
            Motion.Reduced = true;
            var clock = new BoardClock();
            clock.Freeze(0.1f);
            clock.Slow(0.25f, 1f);
            Assert.That(clock.Frozen, Is.False);
            Assert.That(clock.Step(0.1f), Is.EqualTo(0.1f).Within(1e-6f), "real time, whatever the blow");
        }
    }
}
