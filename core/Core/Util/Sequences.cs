#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    /// <summary>JavaScript's array behaviour where the .NET equivalent differs.</summary>
    public static class Sequences
    {
        /// <summary>
        /// A stable sort, as Array.prototype.sort is. List.Sort is unstable, so it could reorder items that compare equal and
        /// change which unit acts or is hit first. OrderBy is documented to be stable.
        /// </summary>
        public static List<T> StableSorted<T>(IEnumerable<T> items, Comparison<T> comparison)
        {
            return items.OrderBy(x => x, Comparer<T>.Create(comparison)).ToList();
        }

        /// <summary>The list in reverse, as a new list.</summary>
        public static List<T> Reversed<T>(IReadOnlyList<T> items)
        {
            var result = new List<T>(items.Count);
            for (var i = items.Count - 1; i >= 0; i--) result.Add(items[i]);
            return result;
        }

        /// <summary>Removes and returns the first item, as Array.prototype.shift does.</summary>
        public static T Shift<T>(List<T> items)
        {
            var first = items[0];
            items.RemoveAt(0);
            return first;
        }
    }
}
