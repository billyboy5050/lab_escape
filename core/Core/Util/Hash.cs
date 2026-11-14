#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LabEscape.Core
{
    /// <summary>
    /// Deterministic, platform-independent hashing for content files and golden replays. The output must stay what the
    /// TypeScript prototype gave, character for character, because every replay, report and saved fight carries a hash.
    /// </summary>
    public static class Hash
    {
        /// <summary>JSON with object keys sorted at every level, so equal data always gives the same string.</summary>
        public static string CanonicalJson(JToken? value)
        {
            var sb = new StringBuilder();
            Write(sb, value, sortKeys: true);
            return sb.ToString();
        }

        /// <summary>JSON.stringify of an already-parsed value, with keys in document order. A missing value is "undefined".</summary>
        public static string Stringify(JToken? value)
        {
            if (value == null) return "undefined";
            var sb = new StringBuilder();
            Write(sb, value, sortKeys: false);
            return sb.ToString();
        }

        /// <summary>JSON.stringify(value, null, space): the multi-line form the reports and replay files are written in.</summary>
        public static string Stringify(JToken? value, int space)
        {
            if (value == null) return "undefined";
            var sb = new StringBuilder();
            WriteIndented(sb, value, new string(' ', space), "");
            return sb.ToString();
        }

        private static void WriteIndented(StringBuilder sb, JToken value, string gap, string indent)
        {
            var inner = indent + gap;
            var first = true;
            switch (value)
            {
                case JObject obj:
                    if (!obj.HasValues)
                    {
                        sb.Append("{}");
                        return;
                    }
                    sb.Append("{\n");
                    foreach (var key in OrderedKeys(obj, sortKeys: false))
                    {
                        if (!first) sb.Append(",\n");
                        first = false;
                        sb.Append(inner);
                        JsText.AppendString(sb, key);
                        sb.Append(": ");
                        WriteIndented(sb, obj[key]!, gap, inner);
                    }
                    sb.Append('\n').Append(indent).Append('}');
                    return;
                case JArray list:
                    if (list.Count == 0)
                    {
                        sb.Append("[]");
                        return;
                    }
                    sb.Append("[\n");
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(",\n");
                        first = false;
                        sb.Append(inner);
                        WriteIndented(sb, item, gap, inner);
                    }
                    sb.Append('\n').Append(indent).Append(']');
                    return;
                default:
                    Write(sb, value, sortKeys: false);
                    return;
            }
        }

        private static void Write(StringBuilder sb, JToken? value, bool sortKeys)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            switch (value.Type)
            {
                case JTokenType.Object:
                    WriteObject(sb, (JObject)value, sortKeys);
                    return;
                case JTokenType.Array:
                    sb.Append('[');
                    var first = true;
                    foreach (var item in (JArray)value)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item, sortKeys);
                    }
                    sb.Append(']');
                    return;
                case JTokenType.Integer:
                case JTokenType.Float:
                    sb.Append(JsText.Number(ToDouble((JValue)value)));
                    return;
                case JTokenType.String:
                    JsText.AppendString(sb, (string)((JValue)value).Value!);
                    return;
                case JTokenType.Boolean:
                    sb.Append((bool)((JValue)value).Value! ? "true" : "false");
                    return;
                case JTokenType.Null:
                    sb.Append("null");
                    return;
                default:
                    throw new InvalidOperationException($"Cannot write a {value.Type} token as JSON");
            }
        }

        /// <summary>
        /// The value as JSON.parse would hold it. An integer too long for a long arrives as a BigInteger, and the explicit
        /// conversion to double truncates where JSON.parse rounds, so it goes through the decimal text instead.
        /// </summary>
        internal static double ToDouble(JValue value)
        {
            if (value.Value is BigInteger big) return double.Parse(big.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            return Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
        }

        private static void WriteObject(StringBuilder sb, JObject obj, bool sortKeys)
        {
            sb.Append('{');
            var first = true;
            foreach (var key in OrderedKeys(obj, sortKeys))
            {
                if (!first) sb.Append(',');
                first = false;
                JsText.AppendString(sb, key);
                sb.Append(':');
                Write(sb, obj[key], sortKeys);
            }
            sb.Append('}');
        }

        /// <summary>
        /// The order JSON.stringify visits keys in. Array-index keys ("0", "12") always come first in ascending numeric order,
        /// however the object was built; the remaining keys follow in sorted order (UTF-16 code units, as Array.sort compares).
        /// </summary>
        private static IEnumerable<string> OrderedKeys(JObject obj, bool sortKeys)
        {
            var indexes = new List<KeyValuePair<uint, string>>();
            var names = new List<string>();
            foreach (var property in obj.Properties())
            {
                if (TryArrayIndex(property.Name, out var index)) indexes.Add(new KeyValuePair<uint, string>(index, property.Name));
                else names.Add(property.Name);
            }
            indexes.Sort((a, b) => a.Key.CompareTo(b.Key));
            if (sortKeys) names.Sort(StringComparer.Ordinal);
            foreach (var pair in indexes) yield return pair.Value;
            foreach (var name in names) yield return name;
        }

        private static bool TryArrayIndex(string key, out uint index)
        {
            index = 0;
            if (key.Length == 0 || key.Length > 10) return false;
            if (key[0] == '0' && key.Length > 1) return false;
            ulong n = 0;
            foreach (var c in key)
            {
                if (c < '0' || c > '9') return false;
                n = n * 10 + (ulong)(c - '0');
            }
            if (n >= uint.MaxValue) return false; // 2^32 - 1 is not an array index.
            index = (uint)n;
            return true;
        }

        /// <summary>cyrb53: a fast 53-bit string hash built only on 32-bit integer maths.</summary>
        public static string HashString(string str, int seed = 0)
        {
            unchecked
            {
                var h1 = 0xdeadbeefu ^ (uint)seed;
                var h2 = 0x41c6ce57u ^ (uint)seed;
                foreach (var ch in str)
                {
                    h1 = (h1 ^ ch) * 2654435761u;
                    h2 = (h2 ^ ch) * 1597334677u;
                }
                h1 = ((h1 ^ (h1 >> 16)) * 2246822507u) ^ ((h2 ^ (h2 >> 13)) * 3266489909u);
                h2 = ((h2 ^ (h2 >> 16)) * 2246822507u) ^ ((h1 ^ (h1 >> 13)) * 3266489909u);
                var n = 4294967296UL * (2097151u & h2) + h1;
                return n.ToString("x", CultureInfo.InvariantCulture).PadLeft(14, '0');
            }
        }

        public static string HashJson(JToken? value) => HashString(CanonicalJson(value));
    }

    /// <summary>Reads JSON the way JSON.parse does: no date guessing, every non-integer a double.</summary>
    public static class JsonIn
    {
        public static JToken Parse(string text)
        {
            using var strings = new System.IO.StringReader(text);
            using var reader = new JsonTextReader(strings)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
            };
            var token = JToken.ReadFrom(reader);
            if (reader.Read()) throw new JsonReaderException("Unexpected content after the JSON value");
            return token;
        }
    }
}
