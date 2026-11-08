#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace LabEscape.Core
{
    /// <summary>
    /// Text exactly as JavaScript produces it. The content hash and the golden event logs are compared byte for byte
    /// against output from the TypeScript engine, so number formatting, string escaping and trimming have to match
    /// JSON.stringify and String.prototype.trim rather than their nearest .NET equivalents.
    /// </summary>
    public static class JsText
    {
        /// <summary>A JSON string literal as JSON.stringify writes it.</summary>
        public static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                if (c < 0x20)
                {
                    AppendEscape(sb, c);
                }
                else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(c).Append(value[++i]);
                }
                else if (char.IsSurrogate(c))
                {
                    AppendEscape(sb, c); // JSON.stringify escapes a lone surrogate.
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append('"');
        }

        private static void AppendEscape(StringBuilder sb, char c)
        {
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        /// <summary>A number as JavaScript's Number.prototype.toString writes it, which is also what JSON.stringify writes.</summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "null"; // JSON.stringify writes null for these.
            if (value == 0) return "0"; // Covers -0, which JavaScript also writes as 0.
            var negative = value < 0;
            var magnitude = Math.Abs(value);
            ShortestDigits(magnitude, out var digits, out var pointPosition);
            var text = Layout(digits, pointPosition);
            return negative ? "-" + text : text;
        }

        public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Number.prototype.toFixed. It works on the double's exact binary value and rounds a tie up, as the ECMAScript
        /// specification says; the committed reports are compared byte for byte, so a .NET format string will not do.
        /// </summary>
        public static string ToFixed(double value, int digits)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsInfinity(value)) return value > 0 ? "Infinity" : "-Infinity";
            if (Math.Abs(value) >= 1e21) return Number(value);
            var bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            var exponent = (int)((bits >> 52) & 0x7ff);
            var mantissa = bits & 0xfffffffffffffL;
            if (exponent == 0) exponent = 1;
            else mantissa |= 1L << 52;
            exponent -= 1075; // The magnitude is exactly mantissa * 2^exponent.
            var scaled = mantissa * BigInteger.Pow(10, digits);
            BigInteger n;
            if (exponent >= 0)
            {
                n = scaled << exponent;
            }
            else
            {
                var divisor = BigInteger.One << -exponent;
                n = BigInteger.DivRem(scaled, divisor, out var remainder);
                if (remainder * 2 >= divisor) n += 1;
            }
            var text = n.ToString(CultureInfo.InvariantCulture);
            if (digits > 0)
            {
                text = text.PadLeft(digits + 1, '0');
                text = text.Substring(0, text.Length - digits) + "." + text.Substring(text.Length - digits);
            }
            return value < 0 ? "-" + text : text;
        }

        /// <summary>
        /// The shortest decimal digits that round-trip to the same double. Tried one precision at a time, because what
        /// double.ToString("R") returns is not the same on every runtime Unity builds for.
        /// </summary>
        private static void ShortestDigits(double magnitude, out string digits, out int pointPosition)
        {
            for (var precision = 0; ; precision++)
            {
                var text = magnitude.ToString("E" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (precision < 16 && double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) != magnitude) continue;
                var e = text.IndexOf('E');
                var mantissa = text.Substring(0, e).Replace(".", "");
                var exponent = int.Parse(text.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                digits = mantissa.TrimEnd('0');
                if (digits.Length == 0) digits = "0";
                pointPosition = exponent + 1;
                return;
            }
        }

        // The layout rules of Number::toString in the ECMAScript specification: k digits, n the position of the decimal point.
        private static string Layout(string digits, int n)
        {
            var k = digits.Length;
            if (k <= n && n <= 21) return digits + new string('0', n - k);
            if (0 < n && n <= 21) return digits.Substring(0, n) + "." + digits.Substring(n);
            if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
            var e = n - 1;
            var sign = e < 0 ? "-" : "+";
            var exponent = Math.Abs(e).ToString(CultureInfo.InvariantCulture);
            if (k == 1) return digits + "e" + sign + exponent;
            return digits.Substring(0, 1) + "." + digits.Substring(1) + "e" + sign + exponent;
        }

        /// <summary>String.prototype.trim: the characters ECMAScript counts as white space or line terminators.</summary>
        public static string Trim(string value)
        {
            var start = 0;
            var end = value.Length;
            while (start < end && IsJsWhiteSpace(value[start])) start++;
            while (end > start && IsJsWhiteSpace(value[end - 1])) end--;
            return value.Substring(start, end - start);
        }

        private static bool IsJsWhiteSpace(char c)
        {
            if (c >= 0x09 && c <= 0x0d) return true; // Tab, line feed, vertical tab, form feed, carriage return.
            if (c == 0x20 || c == 0xa0 || c == 0x2028 || c == 0x2029 || c == 0xfeff) return true;
            return CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
        }
    }

    /// <summary>Writes one line of JSON, with keys in the order they are given, the way JSON.stringify writes an object.</summary>
    public sealed class JsonOut
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private bool _needsComma;

        private void Separate()
        {
            if (_needsComma) _sb.Append(',');
        }

        public JsonOut BeginObject()
        {
            Separate();
            _sb.Append('{');
            _needsComma = false;
            return this;
        }

        public JsonOut EndObject()
        {
            _sb.Append('}');
            _needsComma = true;
            return this;
        }

        public JsonOut BeginArray()
        {
            Separate();
            _sb.Append('[');
            _needsComma = false;
            return this;
        }

        public JsonOut EndArray()
        {
            _sb.Append(']');
            _needsComma = true;
            return this;
        }

        public JsonOut Key(string name)
        {
            Separate();
            JsText.AppendString(_sb, name);
            _sb.Append(':');
            _needsComma = false;
            return this;
        }

        public JsonOut Null() => Raw("null");
        public JsonOut Value(int value) => Raw(JsText.Number(value));
        public JsonOut Value(double value) => Raw(JsText.Number(value));
        public JsonOut Value(bool value) => Raw(value ? "true" : "false");

        public JsonOut Value(string value)
        {
            Separate();
            JsText.AppendString(_sb, value);
            _needsComma = true;
            return this;
        }

        private JsonOut Raw(string text)
        {
            Separate();
            _sb.Append(text);
            _needsComma = true;
            return this;
        }

        public override string ToString() => _sb.ToString();
    }
}
