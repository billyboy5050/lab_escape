#nullable enable
using System;
using System.Collections.Generic;

namespace LabEscape.Client
{
    public enum Waveform { Sine, Square, Sawtooth, Triangle }

    public enum Filter { Lowpass, Highpass, Bandpass }

    /// <summary>
    /// One voice of a sound, as the retired web client's synth had them: a tone whose pitch sweeps from F0 to F1, or
    /// noise through a filter whose cutoff sweeps from F0 to F1. Both rise to Volume in a few milliseconds and die away over
    /// Duration, starting Delay seconds in.
    /// </summary>
    public sealed class Voice
    {
        public bool Noise;
        public Waveform Waveform;
        public Filter Filter;
        public float F0;
        public float F1;
        public float Duration;
        public float Volume;
        public float Delay;
        public float Q = 1f;

        public static Voice Tone(Waveform waveform, float f0, float f1, float duration, float volume, float delay = 0f) =>
            new Voice { Waveform = waveform, F0 = f0, F1 = f1, Duration = duration, Volume = volume, Delay = delay };

        public static Voice Hiss(float duration, float volume, Filter filter, float f0, float f1, float delay = 0f, float q = 1f) =>
            new Voice { Noise = true, Filter = filter, F0 = f0, F1 = f1, Duration = duration, Volume = volume, Delay = delay, Q = q };
    }

    /// <summary>
    /// Renders a sound's voices to samples, as Web Audio would play them, so the Unity client can make its clips in code and the
    /// project keeps no audio files. Decoration only: nothing here touches the rules, and its noise is a fixed sequence.
    /// </summary>
    public static class Synth
    {
        /// <summary>The web client's master gain.</summary>
        public const float MasterGain = 0.3f;

        /// <summary>A voice stops this long after its decay, as the web client stops its nodes.</summary>
        private const float Tail = 0.02f;

        /// <summary>The level an envelope starts from and dies to: exponential ramps cannot reach zero.</summary>
        private const float Floor = 0.0001f;

        public static float[] Render(IReadOnlyList<Voice> voices, int sampleRate)
        {
            var seconds = 0f;
            foreach (var v in voices) seconds = Math.Max(seconds, v.Delay + v.Duration + Tail);
            var outp = new float[(int)Math.Ceiling(seconds * sampleRate)];
            var noise = NoiseBuffer(sampleRate);
            foreach (var v in voices)
            {
                if (v.Noise) AddNoise(outp, v, sampleRate, noise);
                else AddTone(outp, v, sampleRate);
            }
            for (var i = 0; i < outp.Length; i++) outp[i] = Math.Max(-1f, Math.Min(1f, outp[i] * MasterGain));
            return outp;
        }

        /// <summary>The web client's envelope: from the floor up to the volume in at most 12 ms, then down to the floor by the end.</summary>
        public static float Envelope(float t, float duration, float volume)
        {
            if (t < 0f || t > duration) return t > duration ? Floor : 0f;
            var attack = Math.Min(0.012f, duration / 4f);
            var peak = Math.Max(0.0002f, volume);
            return t < attack ? Ramp(Floor, peak, t / attack) : Ramp(peak, Floor, (t - attack) / (duration - attack));
        }

        private static float Ramp(float from, float to, float k) => from * (float)Math.Pow(to / from, Math.Max(0f, Math.Min(1f, k)));

        /// <summary>The swept value at time t: exponential from f0 to f1 over the duration, then held.</summary>
        private static float Sweep(float f0, float f1, float t, float duration) => Ramp(Math.Max(1f, f0), Math.Max(1f, f1), t / duration);

        private static void AddTone(float[] outp, Voice v, int rate)
        {
            int start = (int)(v.Delay * rate), end = Math.Min(outp.Length, (int)((v.Delay + v.Duration + Tail) * rate));
            double phase = 0;
            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (float)rate;
                var f = Sweep(v.F0, v.F1, t, v.Duration);
                var dt = f / rate;
                outp[i] += Oscillator(v.Waveform, phase, dt) * Envelope(t, v.Duration, v.Volume);
                phase += dt;
                if (phase >= 1.0) phase -= 1.0;
            }
        }

        /// <summary>One cycle runs from phase 0 to 1. Square and sawtooth are smoothed at their jumps, as Web Audio's are band-limited.</summary>
        private static float Oscillator(Waveform w, double phase, double dt)
        {
            switch (w)
            {
                case Waveform.Square:
                    return (float)((phase < 0.5 ? 1.0 : -1.0) + Blep(phase, dt) - Blep((phase + 0.5) % 1.0, dt));
                case Waveform.Sawtooth:
                    return (float)(2.0 * phase - 1.0 - Blep(phase, dt));
                case Waveform.Triangle:
                    return (float)(1.0 - 4.0 * Math.Abs(phase - 0.5));
                default:
                    return (float)Math.Sin(2.0 * Math.PI * phase);
            }
        }

        private static double Blep(double t, double dt)
        {
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1.0;
            }
            if (t > 1.0 - dt)
            {
                t = (t - 1.0) / dt;
                return t * t + t + t + 1.0;
            }
            return 0.0;
        }

        /// <summary>The web client's noise: one second of xorshift from a fixed seed.</summary>
        private static float[] NoiseBuffer(int rate)
        {
            var d = new float[rate];
            var x = 0x2545f491u;
            for (var i = 0; i < d.Length; i++)
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                d[i] = (float)(x / 4294967296.0 * 2.0 - 1.0);
            }
            return d;
        }

        private static void AddNoise(float[] outp, Voice v, int rate, float[] noise)
        {
            int start = (int)(v.Delay * rate), end = Math.Min(outp.Length, (int)((v.Delay + v.Duration + Tail) * rate));
            // The web client starts each noise at a different place in its buffer, and the buffer does not loop.
            var from = (int)(v.Delay * 7919f % 0.5f * rate);
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0, b0 = 0, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (float)rate;
                if ((i - start) % 16 == 0) Coefficients(v, Sweep(v.F0, v.F1, t, v.Duration), rate, out b0, out b1, out b2, out a1, out a2);
                var k = from + (i - start);
                double x0 = k < noise.Length ? noise[k] : 0f;
                var y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1;
                x1 = x0;
                y2 = y1;
                y1 = y0;
                outp[i] += (float)y0 * Envelope(t, v.Duration, v.Volume);
            }
        }

        /// <summary>
        /// A biquad's coefficients, as Web Audio's BiquadFilterNode works them out: for low- and high-pass its Q is in decibels,
        /// for band-pass it is the plain Q.
        /// </summary>
        private static void Coefficients(Voice v, float f, int rate, out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            var w0 = 2.0 * Math.PI * Math.Min(f, rate * 0.49) / rate;
            double cos = Math.Cos(w0), sin = Math.Sin(w0);
            var alpha = v.Filter == Filter.Bandpass ? sin / (2.0 * Math.Max(0.0001, v.Q)) : sin / (2.0 * Math.Pow(10.0, v.Q / 20.0));
            var a0 = 1.0 + alpha;
            switch (v.Filter)
            {
                case Filter.Lowpass:
                    b0 = (1.0 - cos) / 2.0;
                    b1 = 1.0 - cos;
                    b2 = (1.0 - cos) / 2.0;
                    break;
                case Filter.Highpass:
                    b0 = (1.0 + cos) / 2.0;
                    b1 = -(1.0 + cos);
                    b2 = (1.0 + cos) / 2.0;
                    break;
                default:
                    b0 = alpha;
                    b1 = 0.0;
                    b2 = -alpha;
                    break;
            }
            b0 /= a0;
            b1 /= a0;
            b2 /= a0;
            a1 = -2.0 * cos / a0;
            a2 = (1.0 - alpha) / a0;
        }
    }
}
