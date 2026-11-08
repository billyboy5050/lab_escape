#nullable enable
using System;
using System.Collections.Generic;

namespace LabEscape.Sim
{
    /// <summary>Seeded 32-bit generator (mulberry32). Integer-only output, so every platform draws the same sequence.</summary>
    public sealed class Rng
    {
        private uint _a;

        public Rng(long seed)
        {
            _a = unchecked((uint)seed);
        }

        public uint NextU32()
        {
            unchecked
            {
                _a += 0x6d2b79f5u;
                var t = _a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return t ^ (t >> 14);
            }
        }

        /// <summary>Integer in [0, n).</summary>
        public int Int(int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), "Rng.Int needs n > 0");
            return (int)(NextU32() % (uint)n);
        }

        /// <summary>True with probability num/den.</summary>
        public bool Chance(int num, int den) => Int(den) < num;

        public T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];

        public List<T> Shuffle<T>(List<T> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = Int(i + 1);
                var swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
            return items;
        }
    }
}
