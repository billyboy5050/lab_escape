#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>The retired web client's sounds, plus a landing.</summary>
    public enum Sfx
    {
        Shot, EnemyShot, GunFire, Acid, Zap, Blast, Flame, Gas, Hit, Hurt, Block, Death, Heal, Deploy, Wave, Place, Hatch, Charge,
        Step, Reload, Intercom, Win, Lose, Select, EndTurn, Ui, Land,
    }

    /// <summary>
    /// Which sound each moment of the fight makes, and what each sound is made of. Ported from the web client, with one
    /// difference: the web client plays a frame's sounds when the frame starts, and here each sound plays on its own cue, so a
    /// shot is heard as it leaves and its hit as it lands. A primed hazard makes no sound: the spec gives hazards a subtle visual
    /// cue and nothing more.
    /// </summary>
    public static class SoundCues
    {
        /// <summary>Most important first, as the web client orders them: when too many come at once, the last are dropped.</summary>
        public static readonly Sfx[] Priority =
        {
            Sfx.Win, Sfx.Lose, Sfx.Hurt, Sfx.Blast, Sfx.Charge, Sfx.GunFire, Sfx.Death, Sfx.Wave, Sfx.Zap, Sfx.Flame, Sfx.Gas, Sfx.Acid,
            Sfx.EnemyShot, Sfx.Shot, Sfx.Hit, Sfx.Land, Sfx.Block, Sfx.Hatch, Sfx.Heal, Sfx.Deploy, Sfx.Place, Sfx.Reload, Sfx.Intercom,
            Sfx.Step, Sfx.EndTurn, Sfx.Select, Sfx.Ui,
        };

        /// <summary>
        /// The sound a cue makes, if any. A wind-up and a strike are silent: the blow is heard when it lands. The end of the fight
        /// is heard when its card is shown, so a skipped fight still ends with it.
        /// </summary>
        public static Sfx? For(Cue cue)
        {
            if (cue.Kind != CueKind.Event) return null;
            switch (cue.Event)
            {
                case ProjectileFired p:
                    if (p.Source.Side == SourceSide.Hazard) return Sfx.GunFire;
                    if (p.Source.Via.Contains("acid") || p.Source.Via.Contains("parasite") || p.Source.Def == "spitter") return Sfx.Acid;
                    return p.Source.Side == SourceSide.Enemy ? Sfx.EnemyShot : Sfx.Shot;
                case ChainArc _:
                    return Sfx.Zap;
                case AreaEffect a:
                    switch (a.Kind)
                    {
                        case AreaKind.Blast:
                        case AreaKind.Burst:
                            return Sfx.Blast;
                        case AreaKind.Sweep:
                            return Sfx.Flame;
                        case AreaKind.Panel:
                            return Sfx.Zap;
                        default:
                            return Sfx.Gas;
                    }
                case DamageDealt d:
                    if (d.Amount > 0) return d.Target.Kind == EntityKind.Unit && d.Target.Id == 0 ? Sfx.Hurt : Sfx.Hit;
                    return d.Absorbed > 0 ? Sfx.Block : (Sfx?)null;
                case UnitDied _:
                    return Sfx.Death;
                case Healed _:
                    return Sfx.Heal;
                case UnitSpawned u:
                    if (u.Cause == "egg" || u.Cause == "parasite") return Sfx.Hatch;
                    // A wave's units are heard as the wave's alarm and as they land.
                    return u.Cause.StartsWith("wave", StringComparison.Ordinal) ? (Sfx?)null : Sfx.Deploy;
                case WaveSpawned _:
                    return Sfx.Wave;
                case ObjectPlaced o:
                    // A corpse is placed as its unit dies, which is heard already.
                    return o.Object.Kind == ObjectKind.Corpse ? (Sfx?)null : Sfx.Place;
                case EggHatched _:
                    return Sfx.Hatch;
                case Charged _:
                    return Sfx.Charge;
                case UnitMoved _:
                    return Sfx.Step;
                case Reloaded _:
                    return Sfx.Reload;
                case DroneRedeployed _:
                    return Sfx.Deploy;
                case IntercomLine _:
                    return Sfx.Intercom;
                default:
                    return null;
            }
        }

        /// <summary>What each sound is made of: the web client's recipes, voice for voice.</summary>
        public static IReadOnlyList<Voice> Recipe(Sfx s)
        {
            switch (s)
            {
                case Sfx.Shot:
                    return new[] { Voice.Hiss(0.08f, 0.55f, Filter.Bandpass, 2600, 1100), Voice.Tone(Waveform.Square, 900, 170, 0.09f, 0.12f) };
                case Sfx.EnemyShot:
                    return new[] { Voice.Hiss(0.1f, 0.5f, Filter.Bandpass, 1500, 600), Voice.Tone(Waveform.Square, 520, 110, 0.11f, 0.12f) };
                case Sfx.GunFire:
                    return new[] { 0f, 0.07f, 0.14f }
                        .SelectMany(d => new[] { Voice.Hiss(0.07f, 0.6f, Filter.Bandpass, 1800, 500, d), Voice.Tone(Waveform.Square, 300, 80, 0.07f, 0.1f, d) })
                        .ToArray();
                case Sfx.Acid:
                    return new[] { Voice.Hiss(0.22f, 0.45f, Filter.Lowpass, 1800, 260), Voice.Tone(Waveform.Sine, 320, 90, 0.2f, 0.25f) };
                case Sfx.Zap:
                    return new[]
                    {
                        Voice.Tone(Waveform.Sawtooth, 1500, 280, 0.18f, 0.12f), Voice.Tone(Waveform.Square, 2300, 900, 0.12f, 0.05f, 0.03f),
                        Voice.Hiss(0.16f, 0.18f, Filter.Highpass, 4200, 2600),
                    };
                case Sfx.Blast:
                    return new[] { Voice.Hiss(0.7f, 0.95f, Filter.Lowpass, 1700, 70), Voice.Tone(Waveform.Sine, 130, 32, 0.55f, 0.65f) };
                case Sfx.Flame:
                    return new[] { Voice.Hiss(0.55f, 0.55f, Filter.Bandpass, 700, 280, 0f, 0.7f), Voice.Hiss(0.55f, 0.35f, Filter.Lowpass, 320, 180) };
                case Sfx.Gas:
                    return new[] { Voice.Hiss(0.8f, 0.3f, Filter.Highpass, 3200, 1400) };
                case Sfx.Hit:
                    return new[] { Voice.Tone(Waveform.Sine, 190, 60, 0.11f, 0.38f), Voice.Hiss(0.05f, 0.28f, Filter.Lowpass, 2200, 500) };
                case Sfx.Hurt:
                    return new[]
                    {
                        Voice.Tone(Waveform.Sawtooth, 230, 70, 0.26f, 0.22f), Voice.Tone(Waveform.Sine, 115, 45, 0.32f, 0.45f),
                        Voice.Hiss(0.1f, 0.3f, Filter.Lowpass, 1400, 300),
                    };
                case Sfx.Block:
                    return new[] { Voice.Tone(Waveform.Triangle, 1250, 1150, 0.09f, 0.14f), Voice.Tone(Waveform.Triangle, 1880, 1760, 0.12f, 0.07f, 0.02f) };
                case Sfx.Death:
                    return new[] { Voice.Tone(Waveform.Sawtooth, 520, 50, 0.42f, 0.18f), Voice.Hiss(0.38f, 0.35f, Filter.Lowpass, 1300, 90) };
                case Sfx.Heal:
                    return new[]
                    {
                        Voice.Tone(Waveform.Sine, 660, 660, 0.1f, 0.16f), Voice.Tone(Waveform.Sine, 880, 880, 0.1f, 0.16f, 0.08f),
                        Voice.Tone(Waveform.Sine, 1320, 1320, 0.18f, 0.13f, 0.16f),
                    };
                case Sfx.Deploy:
                    return new[] { Voice.Tone(Waveform.Square, 300, 900, 0.18f, 0.1f), Voice.Tone(Waveform.Sine, 900, 1250, 0.12f, 0.12f, 0.16f) };
                case Sfx.Wave:
                    return new[] { 0f, 0.4f }
                        .SelectMany(d => new[] { Voice.Tone(Waveform.Square, 720, 720, 0.18f, 0.1f, d), Voice.Tone(Waveform.Square, 540, 540, 0.18f, 0.1f, d + 0.2f) })
                        .ToArray();
                case Sfx.Place:
                    return new[] { Voice.Tone(Waveform.Triangle, 520, 380, 0.07f, 0.2f), Voice.Hiss(0.04f, 0.2f, Filter.Highpass, 3000, 3000) };
                case Sfx.Hatch:
                    return new[] { Voice.Hiss(0.32f, 0.4f, Filter.Lowpass, 950, 180), Voice.Tone(Waveform.Sine, 190, 430, 0.26f, 0.2f) };
                case Sfx.Charge:
                    return new[]
                    {
                        Voice.Hiss(0.45f, 0.6f, Filter.Bandpass, 280, 1600), Voice.Tone(Waveform.Sine, 95, 38, 0.42f, 0.55f, 0.3f),
                        Voice.Hiss(0.25f, 0.6f, Filter.Lowpass, 1200, 100, 0.32f),
                    };
                case Sfx.Step:
                    return new[] { Voice.Hiss(0.05f, 0.12f, Filter.Lowpass, 650, 280) };
                case Sfx.Reload:
                    return new[] { Voice.Hiss(0.035f, 0.4f, Filter.Highpass, 2600, 2600), Voice.Hiss(0.035f, 0.4f, Filter.Highpass, 1800, 1800, 0.09f) };
                case Sfx.Intercom:
                    return new[]
                    {
                        Voice.Hiss(0.09f, 0.22f, Filter.Bandpass, 1800, 1800, 0f, 3f), Voice.Tone(Waveform.Sine, 1000, 1000, 0.06f, 0.07f, 0.09f),
                        Voice.Hiss(0.06f, 0.18f, Filter.Bandpass, 1800, 1800, 0.16f, 3f),
                    };
                case Sfx.Win:
                    return new[] { 523f, 659f, 784f, 1047f }.Select((f, i) => Voice.Tone(Waveform.Triangle, f, f, 0.28f, 0.2f, i * 0.11f)).ToArray();
                case Sfx.Lose:
                    return new[] { Voice.Tone(Waveform.Sawtooth, 220, 52, 1.2f, 0.2f), Voice.Tone(Waveform.Sine, 110, 38, 1.2f, 0.32f) };
                case Sfx.Select:
                    return new[] { Voice.Tone(Waveform.Square, 1300, 1300, 0.035f, 0.05f) };
                case Sfx.EndTurn:
                    return new[] { Voice.Tone(Waveform.Triangle, 660, 660, 0.08f, 0.14f), Voice.Tone(Waveform.Triangle, 440, 440, 0.12f, 0.14f, 0.09f) };
                case Sfx.Ui:
                    return new[] { Voice.Tone(Waveform.Triangle, 900, 900, 0.045f, 0.09f) };
                case Sfx.Land:
                    // Not in the web client, which has no landings: a soft, low thud, below a hit.
                    return new[] { Voice.Tone(Waveform.Sine, 120, 40, 0.18f, 0.42f), Voice.Hiss(0.12f, 0.3f, Filter.Lowpass, 700, 120) };
                default:
                    throw new ArgumentOutOfRangeException(nameof(s), s, null);
            }
        }
    }

    /// <summary>
    /// Keeps a burst of cues a sound and not a wall of noise, as the web client's cap of four sounds a frame does: within a short
    /// window at most four sounds play, a sound already playing is not started again, and the most important go first.
    /// </summary>
    public sealed class SoundGate
    {
        public const float Window = 0.06f;
        public const int MostAtOnce = 4;

        private readonly List<(Sfx Sound, float At)> _recent = new List<(Sfx, float)>();

        /// <summary>Of the sounds asked for at `now`, the ones to play.</summary>
        public List<Sfx> Take(IEnumerable<Sfx> asked, float now)
        {
            _recent.RemoveAll(r => now - r.At >= Window || now < r.At);
            var room = MostAtOnce - _recent.Count;
            var picked = asked.Distinct()
                .Where(s => _recent.All(r => r.Sound != s))
                .OrderBy(s => Array.IndexOf(SoundCues.Priority, s))
                .Take(Math.Max(0, room))
                .ToList();
            foreach (var s in picked) _recent.Add((s, now));
            return picked;
        }
    }
}
