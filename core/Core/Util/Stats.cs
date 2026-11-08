#nullable enable
using System;
using System.Collections.Generic;

namespace LabEscape.Core
{
    /// <summary>Numeric helpers shared by the rules and reports.</summary>
    public static class Stats
    {
        /// <summary>The median of a list; with an even count, the mean of the two middle values. 0 for an empty list.</summary>
        public static double Median(IReadOnlyList<double> xs)
        {
            if (xs.Count == 0) return 0;
            var s = Sequences.StableSorted(xs, (a, b) => a.CompareTo(b));
            var m = s.Count / 2;
            return s.Count % 2 != 0 ? s[m] : (s[m - 1] + s[m]) / 2;
        }

        /// <summary>Math.round: to the nearest whole number, with a tie going up. Math.Round would send a tie to the even number.</summary>
        public static double RoundHalfUp(double x)
        {
            var floor = Math.Floor(x);
            return x - floor >= 0.5 ? floor + 1 : floor;
        }
    }
}
