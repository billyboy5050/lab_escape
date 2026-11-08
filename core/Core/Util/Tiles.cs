#nullable enable
using System;
using System.Globalization;

namespace LabEscape.Core
{
    /// <summary>A board position. Immutable, so sharing one can never alias a mutable object as it can in the TypeScript.</summary>
    public readonly struct Pos : IEquatable<Pos>
    {
        public readonly int X;
        public readonly int Y;

        public Pos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Pos other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Pos p && Equals(p);
        public override int GetHashCode() => (X * 397) ^ Y;
        public static bool operator ==(Pos a, Pos b) => a.Equals(b);
        public static bool operator !=(Pos a, Pos b) => !a.Equals(b);
        public override string ToString() => Tiles.Name(this);
    }

    /// <summary>Tile addressing: columns A.. left to right, rows 1.. top to bottom. "D5" is column D (x=3), row 5 (y=4).</summary>
    public static class Tiles
    {
        /// <summary>The largest room a tile name can address: columns A to Z, rows 1 to 99.</summary>
        public const int MaxColumns = 26;
        public const int MaxRows = 99;

        public static string Name(Pos p) => ((char)(65 + p.X)).ToString() + (p.Y + 1).ToString(CultureInfo.InvariantCulture);

        /// <summary>Parses a tile name. Throws FormatException for anything that is not a tile name.</summary>
        public static Pos Parse(string name)
        {
            if (!TryParse(name, out var p)) throw new FormatException($"Bad tile name \"{name}\"");
            return p;
        }

        public static bool IsName(string? name) => TryParse(name, out _);

        // Written by hand rather than as a regular expression: .NET's \d also matches non-ASCII digits, and $ matches before
        // a final newline, so the same pattern would accept names the TypeScript rejects.
        private static bool TryParse(string? name, out Pos p)
        {
            p = default;
            if (name == null) return false;
            var s = JsText.Trim(name);
            if (s.Length < 2 || s.Length > 3) return false;
            var letter = s[0];
            var isUpper = letter >= 'A' && letter <= 'Z';
            var isLower = letter >= 'a' && letter <= 'z';
            if (!isUpper && !isLower) return false;
            var number = 0;
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (c < '0' || c > '9') return false;
                number = number * 10 + (c - '0');
            }
            p = new Pos((isLower ? letter - 'a' : letter - 'A'), number - 1);
            return true;
        }
    }
}
