#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The fight's sound: which cue makes which sound, the gate that keeps a burst to a few, and the synth that makes them.</summary>
    public class SoundTests
    {
        private const int Rate = 48000;

        private static SourceRef Unit(SourceSide side, int id, string def, string via) =>
            new SourceRef(side, side == SourceSide.Enemy ? SourceCategory.Enemy : SourceCategory.Player, SourceKind.Unit, id, def, via);

        private static SourceRef Hazard(string id) => new SourceRef(SourceSide.Hazard, SourceCategory.Hazard, SourceKind.Hazard, id, "gun", "gun");

        private static Sfx? Of(GameEvent e, CueKind kind = CueKind.Event) => SoundCues.For(new Cue { Kind = kind, Event = e });

        private static DamageDealt Damage(int target, int amount, int absorbed = 0) => new DamageDealt
        {
            Target = EntityRef.Unit(target), Source = Unit(SourceSide.Enemy, 3, "guard", "shot"), Raw = amount + absorbed, Amount = amount, Absorbed = absorbed,
        };

        [Test]
        public void ABlowIsHeardAsHurtOnThePlayerAndAsAHitOnAnyoneElse()
        {
            Assert.That(Of(Damage(0, 2)), Is.EqualTo(Sfx.Hurt));
            Assert.That(Of(Damage(5, 2)), Is.EqualTo(Sfx.Hit));
            Assert.That(Of(Damage(5, 0, absorbed: 2)), Is.EqualTo(Sfx.Block));
            Assert.That(Of(Damage(5, 0)), Is.Null);
        }

        [Test]
        public void AShotSoundsLikeWhatFiredIt()
        {
            Assert.That(Of(new ProjectileFired { Source = Unit(SourceSide.Player, 0, "player", "sidearm") }), Is.EqualTo(Sfx.Shot));
            Assert.That(Of(new ProjectileFired { Source = Unit(SourceSide.Enemy, 3, "guard", "shot") }), Is.EqualTo(Sfx.EnemyShot));
            Assert.That(Of(new ProjectileFired { Source = Unit(SourceSide.Player, 0, "player", "acid_spit") }), Is.EqualTo(Sfx.Acid));
            Assert.That(Of(new ProjectileFired { Source = Unit(SourceSide.Enemy, 7, "spitter", "spit") }), Is.EqualTo(Sfx.Acid));
            Assert.That(Of(new ProjectileFired { Source = Hazard("gun") }), Is.EqualTo(Sfx.GunFire));
        }

        [Test]
        public void AWindUpAndAStrikeAreSilentAndTheBlowIsHeardWhenItLands()
        {
            var blow = Damage(5, 2);
            Assert.That(Of(blow, CueKind.Windup), Is.Null);
            Assert.That(Of(blow, CueKind.Strike), Is.Null);
            Assert.That(Of(blow), Is.EqualTo(Sfx.Hit));
        }

        [Test]
        public void APrimedHazardMakesNoSound()
        {
            // The spec gives a primed hazard a subtle visual cue and nothing more.
            Assert.That(Of(new HazardPrimed()), Is.Null);
        }

        [Test]
        public void AWavesUnitsAreHeardAsTheWaveAndHatchlingsAsAHatch()
        {
            Assert.That(Of(new UnitSpawned { Cause = "wave" }), Is.Null);
            Assert.That(Of(new WaveSpawned()), Is.EqualTo(Sfx.Wave));
            Assert.That(Of(new UnitSpawned { Cause = "egg" }), Is.EqualTo(Sfx.Hatch));
            Assert.That(Of(new UnitSpawned { Cause = "turret" }), Is.EqualTo(Sfx.Deploy));
        }

        [Test]
        public void ACorpseIsSilentAndAMineClicksDown()
        {
            Assert.That(Of(new ObjectPlaced { Object = new BoardObject { Kind = ObjectKind.Corpse } }), Is.Null);
            Assert.That(Of(new ObjectPlaced { Object = new BoardObject { Kind = ObjectKind.Mine } }), Is.EqualTo(Sfx.Place));
        }

        [Test]
        public void TheEndOfTheFightIsNotACueSound()
        {
            // It plays when the end card is shown, so a skipped fight still ends with it.
            Assert.That(Of(new FightEnded { Result = FightResult.Win }), Is.Null);
        }

        [Test]
        public void TheGatePlaysAtMostFourAtOnceTheMostImportantFirst()
        {
            var gate = new SoundGate();
            var first = gate.Take(new[] { Sfx.Hit, Sfx.Hit, Sfx.Step, Sfx.Shot, Sfx.Hurt, Sfx.Zap, Sfx.Blast }, 0f);
            Assert.That(first, Is.EqualTo(new[] { Sfx.Hurt, Sfx.Blast, Sfx.Zap, Sfx.Shot }));
            Assert.That(gate.Take(new[] { Sfx.Death }, 0.03f), Is.Empty, "the window is full");
            Assert.That(gate.Take(new[] { Sfx.Death }, 0.07f), Is.EqualTo(new[] { Sfx.Death }), "the window has passed");
            Assert.That(gate.Take(new[] { Sfx.Death, Sfx.Hit }, 0.1f), Is.EqualTo(new[] { Sfx.Hit }), "a sound already playing is not started again");
        }

        [Test]
        public void EverySoundIsInThePriorityOrderOnce()
        {
            var all = Enum.GetValues(typeof(Sfx)).Cast<Sfx>().ToList();
            Assert.That(SoundCues.Priority, Is.EquivalentTo(all));
        }

        [Test]
        public void EverySoundRendersToAudibleSamplesOfItsLength()
        {
            foreach (var s in Enum.GetValues(typeof(Sfx)).Cast<Sfx>())
            {
                var recipe = SoundCues.Recipe(s);
                var data = Synth.Render(recipe, Rate);
                var seconds = recipe.Max(v => v.Delay + v.Duration) + 0.02f;
                Assert.That(data.Length, Is.EqualTo((int)Math.Ceiling(seconds * Rate)).Within(1), $"{s}: length");
                Assert.That(data.All(x => !float.IsNaN(x) && x >= -1f && x <= 1f), Is.True, $"{s}: samples in range");
                Assert.That(data.Max(Math.Abs), Is.GreaterThan(0.002f), $"{s}: not silent");
            }
        }

        [Test]
        public void TheEnvelopeRisesToItsVolumeAndDiesAway()
        {
            Assert.That(Synth.Envelope(0f, 0.2f, 0.5f), Is.LessThan(0.001f));
            Assert.That(Synth.Envelope(0.012f, 0.2f, 0.5f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(Synth.Envelope(0.1f, 0.2f, 0.5f), Is.LessThan(0.5f).And.GreaterThan(0.001f));
            Assert.That(Synth.Envelope(0.2f, 0.2f, 0.5f), Is.LessThan(0.001f));
        }

        [Test]
        public void TheSameSoundRendersTheSameEveryTime()
        {
            Assert.That(Synth.Render(SoundCues.Recipe(Sfx.Blast), Rate), Is.EqualTo(Synth.Render(SoundCues.Recipe(Sfx.Blast), Rate)));
        }
    }
}
