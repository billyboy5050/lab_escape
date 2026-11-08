#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LabEscape.Core;

namespace LabEscape.Sim
{
    /// <summary>The command line, read by the same rules as Node's parseArgs in strict mode.</summary>
    public sealed class ParsedArgs
    {
        private static readonly string[] Strings = { "bot", "preset", "seeds", "from", "workers", "size", "csv", "out", "informed-from", "informed-attempts", "informed-preset", "param", "values" };
        private static readonly string[] Repeatable = { "set", "exclude-tester" };
        private static readonly string[] Flags = { "hold-wave3", "record", "include-unlabelled", "help" };

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);

        public List<string> Positionals { get; } = new List<string>();

        public string? Value(string name) => _values.TryGetValue(name, out var v) ? v : null;
        public IReadOnlyList<string> List(string name) => _lists.TryGetValue(name, out var v) ? v : (IReadOnlyList<string>)new string[0];
        public bool Flag(string name) => _flags.Contains(name);

        public static ParsedArgs Parse(IReadOnlyList<string> args)
        {
            var p = new ParsedArgs();
            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                if (arg == "--")
                {
                    p.Positionals.AddRange(args.Skip(i + 1));
                    break;
                }
                if (arg == "-h")
                {
                    p._flags.Add("help");
                    continue;
                }
                if (arg.Length < 2 || arg[0] != '-')
                {
                    p.Positionals.Add(arg);
                    continue;
                }
                if (!arg.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option '{arg}'");
                var equals = arg.IndexOf('=');
                var name = equals >= 0 ? arg.Substring(2, equals - 2) : arg.Substring(2);
                if (Flags.Contains(name))
                {
                    if (equals >= 0) throw new ArgumentException($"Option '--{name}' does not take an argument");
                    p._flags.Add(name);
                    continue;
                }
                if (!Strings.Contains(name) && !Repeatable.Contains(name)) throw new ArgumentException($"Unknown option '--{name}'");
                string value;
                if (equals >= 0)
                {
                    value = arg.Substring(equals + 1);
                }
                else
                {
                    if (i + 1 >= args.Count) throw new ArgumentException($"Option '--{name} <value>' argument missing");
                    value = args[++i];
                    if (value.Length > 1 && value[0] == '-') throw new ArgumentException($"Option '--{name}' argument is ambiguous. To give a value starting with a dash use '--{name}=-XYZ'.");
                }
                if (Repeatable.Contains(name))
                {
                    if (!p._lists.ContainsKey(name)) p._lists[name] = new List<string>();
                    p._lists[name].Add(value);
                }
                else
                {
                    p._values[name] = value;
                }
            }
            return p;
        }
    }

    public static class Options
    {
        private static readonly Regex DecimalLiteral = new Regex(@"^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)([eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant);

        /// <summary>JavaScript's Number(text), for the forms a command line can hold.</summary>
        private static double JsNumber(string raw)
        {
            var s = JsText.Trim(raw);
            if (s.Length == 0) return 0;
            if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            if (s.Length > 2 && s[0] == '0' && "xXoObB".IndexOf(s[1]) >= 0)
            {
                var radix = char.ToLowerInvariant(s[1]) == 'x' ? 16 : char.ToLowerInvariant(s[1]) == 'o' ? 8 : 2;
                var total = 0.0;
                foreach (var ch in s.Substring(2))
                {
                    var digit = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'a' && ch <= 'f' ? ch - 'a' + 10 : ch >= 'A' && ch <= 'F' ? ch - 'A' + 10 : 99;
                    if (digit >= radix) return double.NaN;
                    total = total * radix + digit;
                }
                return total;
            }
            return DecimalLiteral.IsMatch(s) ? double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) : double.NaN;
        }

        /// <summary>
        /// Parses a whole-number option. Anything else (a typo such as --workers foo, a decimal, a number below min) is an error
        /// that names the option. Returns null when the option was not given.
        /// </summary>
        public static long? ParseInteger(string name, string? raw, long? min = null)
        {
            if (raw == null) return null;
            var n = JsNumber(raw);
            var whole = !double.IsNaN(n) && !double.IsInfinity(n) && Math.Floor(n) == n && Math.Abs(n) < 9.2e18;
            if (JsText.Trim(raw).Length == 0 || !whole || (min.HasValue && n < min.Value))
            {
                throw new ArgumentException($"--{name} must be a whole number{(min.HasValue ? $" of at least {min.Value}" : "")} (got \"{raw}\")");
            }
            return (long)n;
        }
    }
}
