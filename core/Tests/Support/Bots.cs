#nullable enable
using System.Collections.Generic;

namespace LabEscape.Core.Tests
{
    /// <summary>
    /// Seeded 32-bit generator (mulberry32), the same sequence as src/sim/rng.ts. Lives with the tests until M10 builds the
    /// simulator, which will own it.
    /// </summary>
    public sealed class Rng
    {
        private uint _a;

        public Rng(uint seed)
        {
            _a = seed;
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
        public int Int(int n) => (int)(NextU32() % (uint)n);

        public T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];
    }

    /// <summary>Random legal bot, as in src/sim/bots.ts. Draws from a seeded generator.</summary>
    public sealed class RandomBot
    {
        private readonly Rng _rng;

        public RandomBot(uint seed)
        {
            _rng = new Rng(seed);
        }

        public Command Decide(Content c, GameState s) => _rng.Pick(Actions.LegalCommands(c, s));
    }
}
