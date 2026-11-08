#nullable enable
using System;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Sim
{
    /// <summary>
    /// String.prototype.localeCompare for ASCII text, as ICU orders it in the default (en) locale: punctuation, then digits, then
    /// letters with case ignored, and only then lower case before upper. The simulator runs without culture data, and an ordinal
    /// comparison would sort "P_1" after "P1" and every lower-case name after every upper-case one.
    /// </summary>
    public static class LocaleOrder
    {
        private const string Order = " _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$0123456789aAbBcCdDeEfFgGhHiIjJkKlLmMnNoOpPqQrRsStTuUvVwWxXyYzZ";

        private static int Primary(char c)
        {
            var at = Order.IndexOf(c);
            if (at < 0) return c + Order.Length; // Past every ASCII character, in code-unit order.
            return char.IsLetter(c) ? Order.IndexOf(char.ToLowerInvariant(c)) : at;
        }

        public static int Compare(string a, string b)
        {
            var shared = Math.Min(a.Length, b.Length);
            for (var i = 0; i < shared; i++)
            {
                var d = Primary(a[i]) - Primary(b[i]);
                if (d != 0) return d;
            }
            if (a.Length != b.Length) return a.Length - b.Length;
            for (var i = 0; i < shared; i++)
            {
                if (a[i] != b[i]) return char.IsLower(a[i]) ? -1 : 1;
            }
            return 0;
        }
    }

    /// <summary>JSON.parse's rules: JsonIn alone also accepts comments, single quotes and bare NaN, which a replay file may not hold.</summary>
    public static class StrictJson
    {
        public static JToken Parse(string text)
        {
            using (System.Text.Json.JsonDocument.Parse(text)) { }
            return JsonIn.Parse(text);
        }

        public static bool TryParse(string text, out JToken token)
        {
            try
            {
                token = Parse(text);
                return true;
            }
            catch (Exception e) when (e is System.Text.Json.JsonException || e is Newtonsoft.Json.JsonException)
            {
                token = JValue.CreateNull();
                return false;
            }
        }
    }
}
