// Linked source (see ArenaMode.Shared/Session/ and docs/ARENA-SESSION-20261021.md, section 4):
// compiled into the gate (netstandard2.1, runs inside the BepInEx preloader), the session
// helper (net472), the client (netstandard2.1), the server (net10.0) and the tests (net10.0).
//
// Therefore this file uses no NuGet package, no Newtonsoft, no System.Text.Json, no Unity and
// no BepInEx type, and only C# 7.3 syntax and APIs that exist on every one of those targets
// (no Span, no Dictionary.TryAdd, no string.Contains(char), no records, no init setters).
//
// It carries no nullable annotations on purpose: the helper's default language version cannot
// parse them, and the server and the tests compile it with nullable analysis switched on. The
// pragma below silences that analysis for this file only; null is an ordinary, documented value
// here (JSON null, "not found").
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ArenaMode.Shared.Session
{
    /// <summary>
    /// A deliberately small, strict JSON reader and writer for the Arena session files (policy
    /// files, the gate report, the helper request). It exists because the preloader gate and the
    /// net472 helper cannot reference a JSON library.
    ///
    /// Reader result types, always plain: <c>Dictionary&lt;string, object&gt;</c> (JSON object, keys
    /// compared ordinally, duplicate keys rejected), <c>List&lt;object&gt;</c> (array),
    /// <c>string</c>, <c>double</c> (every number), <c>bool</c> and <c>null</c>.
    ///
    /// Strict on purpose: no comments, no trailing commas, no single quotes, no unescaped control
    /// characters in strings, no content after the first value. A policy file that is only almost
    /// JSON must be rejected rather than half-read.
    /// </summary>
    public static class ArenaMiniJson
    {
        /// <summary>Nesting limit; deeper input is rejected instead of overflowing the stack.</summary>
        public const int MaxDepth = 64;

        // ------------------------------------------------------------------ reading

        /// <summary>
        /// Parses one JSON document. Never throws: on any problem it returns false, sets
        /// <paramref name="value"/> to null and puts a short message with line and column in
        /// <paramref name="error"/>. A leading byte order mark is skipped. A document that is the
        /// JSON literal <c>null</c> parses successfully with <paramref name="value"/> null.
        /// </summary>
        public static bool TryParse(string json, out object value, out string error)
        {
            value = null;
            error = null;
            if (json == null)
            {
                error = "JSON text is null";
                return false;
            }

            try
            {
                var parser = new Parser(json);
                object parsed = parser.ParseDocument();
                value = parsed;
                return true;
            }
            catch (ParseException ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                // Defensive: the contract says TryParse never throws.
                value = null;
                error = "JSON parse failure: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private sealed class ParseException : Exception
        {
            public ParseException(string message) : base(message) { }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _pos;

            public Parser(string text)
            {
                _text = text;
            }

            public object ParseDocument()
            {
                if (_text.Length > 0 && _text[0] == '﻿') _pos = 1;
                SkipWhitespace();
                if (_pos >= _text.Length) throw Fail("empty JSON document");
                object result = ParseValue(0);
                SkipWhitespace();
                if (_pos < _text.Length) throw Fail("unexpected content after the JSON value");
                return result;
            }

            private ParseException Fail(string message)
            {
                int line = 1;
                int column = 1;
                int limit = Math.Min(_pos, _text.Length);
                for (int i = 0; i < limit; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }

                return new ParseException(message + " (line " + line.ToString(CultureInfo.InvariantCulture)
                    + ", column " + column.ToString(CultureInfo.InvariantCulture) + ")");
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                    else break;
                }
            }

            private object ParseValue(int depth)
            {
                if (depth > MaxDepth) throw Fail("JSON nesting is deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture));
                if (_pos >= _text.Length) throw Fail("unexpected end of JSON text");

                char c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return ParseString();
                    case 't': ExpectLiteral("true"); return true;
                    case 'f': ExpectLiteral("false"); return false;
                    case 'n': ExpectLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Fail("unexpected character '" + c + "'");
                }
            }

            private void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                    throw Fail("invalid literal, expected '" + literal + "'");
                _pos += literal.Length;
                // "truex" must not parse as true followed by garbage that a later rule might miss.
                if (_pos < _text.Length)
                {
                    char next = _text[_pos];
                    if (char.IsLetterOrDigit(next) || next == '_') throw Fail("invalid literal, expected '" + literal + "'");
                }
            }

            private Dictionary<string, object> ParseObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _pos++; // {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == '}')
                {
                    _pos++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length || _text[_pos] != '"') throw Fail("expected a string key in the object");
                    string key = ParseString();
                    if (result.ContainsKey(key)) throw Fail("duplicate key '" + key + "'");
                    SkipWhitespace();
                    if (_pos >= _text.Length || _text[_pos] != ':') throw Fail("expected ':' after the key '" + key + "'");
                    _pos++;
                    SkipWhitespace();
                    result[key] = ParseValue(depth + 1);
                    SkipWhitespace();
                    if (_pos >= _text.Length) throw Fail("unterminated object");
                    char c = _text[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == '}')
                    {
                        _pos++;
                        return result;
                    }

                    throw Fail("expected ',' or '}' in the object");
                }
            }

            private List<object> ParseArray(int depth)
            {
                var result = new List<object>();
                _pos++; // [
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == ']')
                {
                    _pos++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    result.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (_pos >= _text.Length) throw Fail("unterminated array");
                    char c = _text[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == ']')
                    {
                        _pos++;
                        return result;
                    }

                    throw Fail("expected ',' or ']' in the array");
                }
            }

            private string ParseString()
            {
                _pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _text.Length) throw Fail("unterminated string");
                    char c = _text[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ')
                    {
                        _pos--;
                        throw Fail("unescaped control character in a string");
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_pos >= _text.Length) throw Fail("unterminated escape sequence");
                    char e = _text[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw Fail("truncated \\u escape");
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                int digit = HexValue(_text[_pos + i]);
                                if (digit < 0)
                                {
                                    _pos += i;
                                    throw Fail("invalid \\u escape");
                                }

                                code = (code << 4) | digit;
                            }

                            _pos += 4;
                            sb.Append((char)code);
                            break;
                        default:
                            _pos--;
                            throw Fail("invalid escape sequence '\\" + e + "'");
                    }
                }
            }

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private double ParseNumber()
            {
                int start = _pos;
                if (_text[_pos] == '-') _pos++;
                if (_pos >= _text.Length) throw Fail("invalid number");
                if (_text[_pos] == '0')
                {
                    _pos++;
                }
                else if (_text[_pos] >= '1' && _text[_pos] <= '9')
                {
                    while (_pos < _text.Length && _text[_pos] >= '0' && _text[_pos] <= '9') _pos++;
                }
                else
                {
                    throw Fail("invalid number");
                }

                if (_pos < _text.Length && _text[_pos] == '.')
                {
                    _pos++;
                    int fractionStart = _pos;
                    while (_pos < _text.Length && _text[_pos] >= '0' && _text[_pos] <= '9') _pos++;
                    if (_pos == fractionStart) throw Fail("invalid number, digits expected after '.'");
                }

                if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    int exponentStart = _pos;
                    while (_pos < _text.Length && _text[_pos] >= '0' && _text[_pos] <= '9') _pos++;
                    if (_pos == exponentStart) throw Fail("invalid number, digits expected in the exponent");
                }

                double number;
                if (!double.TryParse(_text.Substring(start, _pos - start), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out number)
                    || double.IsInfinity(number) || double.IsNaN(number))
                {
                    _pos = start;
                    throw Fail("number out of range");
                }

                return number;
            }
        }

        // ------------------------------------------------------------------ typed access

        /// <summary>The value as a JSON object, or null when it is anything else.</summary>
        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        /// <summary>The value as a JSON array, or null when it is anything else.</summary>
        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        /// <summary>The value as a string, or null when it is anything else (numbers are not converted).</summary>
        public static string AsString(object value)
        {
            return value as string;
        }

        /// <summary>
        /// The string member <paramref name="key"/> of <paramref name="obj"/>, or null when
        /// <paramref name="obj"/> is not an object, the member is missing, JSON null or not a string.
        /// </summary>
        public static string GetString(object obj, string key)
        {
            object value;
            return TryGetMember(obj, key, out value) ? value as string : null;
        }

        /// <summary>The boolean member, or <paramref name="fallback"/> when missing or not a boolean.</summary>
        public static bool GetBool(object obj, string key, bool fallback)
        {
            object value;
            if (TryGetMember(obj, key, out value) && value is bool) return (bool)value;
            return fallback;
        }

        /// <summary>The numeric member, or <paramref name="fallback"/> when missing or not a number.</summary>
        public static double GetNumber(object obj, string key, double fallback)
        {
            object value;
            if (TryGetMember(obj, key, out value) && value is double) return (double)value;
            return fallback;
        }

        /// <summary>
        /// The numeric member as an int, or <paramref name="fallback"/> when missing, not a number,
        /// fractional or outside the int range (a process id of 12.5 is not a process id).
        /// </summary>
        public static int GetInt(object obj, string key, int fallback)
        {
            object value;
            if (!TryGetMember(obj, key, out value) || !(value is double)) return fallback;
            double number = (double)value;
            if (number < int.MinValue || number > int.MaxValue || Math.Floor(number) != number) return fallback;
            return (int)number;
        }

        /// <summary>The array member, or null when missing or not an array.</summary>
        public static List<object> GetArray(object obj, string key)
        {
            object value;
            return TryGetMember(obj, key, out value) ? value as List<object> : null;
        }

        /// <summary>The object member, or null when missing or not an object.</summary>
        public static Dictionary<string, object> GetObject(object obj, string key)
        {
            object value;
            return TryGetMember(obj, key, out value) ? value as Dictionary<string, object> : null;
        }

        private static bool TryGetMember(object obj, string key, out object value)
        {
            value = null;
            var dictionary = obj as Dictionary<string, object>;
            return dictionary != null && key != null && dictionary.TryGetValue(key, out value);
        }

        // ------------------------------------------------------------------ writing

        /// <summary>Serializes compactly. See <see cref="Write(object, bool)"/>.</summary>
        public static string Write(object value)
        {
            return Write(value, false);
        }

        /// <summary>
        /// Serializes a value tree. Supported: null, string, bool, every integer and floating point
        /// type, enums (as their name), <see cref="IDictionary"/> (keys through ToString; written in
        /// enumeration order, which for a <c>Dictionary</c> without removals is insertion order),
        /// and any other <see cref="IEnumerable"/> as an array. Anything else is written as its
        /// ToString string. NaN and infinities are written as null because JSON has no spelling for
        /// them. Never throws for a finite, acyclic tree; a cyclic tree stops at
        /// <see cref="MaxDepth"/> and writes null below it.
        /// </summary>
        public static string Write(object value, bool indented)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, indented, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, bool indented, int depth)
        {
            if (value == null || depth > MaxDepth)
            {
                sb.Append("null");
                return;
            }

            string text = value as string;
            if (text != null)
            {
                WriteString(sb, text);
                return;
            }

            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }

            if (value is double) { WriteDouble(sb, (double)value); return; }
            if (value is float) { WriteDouble(sb, (double)(float)value); return; }
            if (value is decimal) { sb.Append(((decimal)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is int || value is long || value is short || value is sbyte
                || value is uint || value is ulong || value is ushort || value is byte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is Enum || value is char)
            {
                WriteString(sb, value.ToString());
                return;
            }

            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                WriteObject(sb, dictionary, indented, depth);
                return;
            }

            var sequence = value as IEnumerable;
            if (sequence != null)
            {
                WriteArray(sb, sequence, indented, depth);
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, IDictionary dictionary, bool indented, int depth)
        {
            if (dictionary.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first) sb.Append(',');
                first = false;
                if (indented) NewLine(sb, depth + 1);
                WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty);
                sb.Append(indented ? ": " : ":");
                WriteValue(sb, entry.Value, indented, depth + 1);
            }

            if (indented) NewLine(sb, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable sequence, bool indented, int depth)
        {
            bool first = true;
            foreach (object item in sequence)
            {
                sb.Append(first ? '[' : ',');
                first = false;
                if (indented) NewLine(sb, depth + 1);
                WriteValue(sb, item, indented, depth + 1);
            }

            if (first)
            {
                sb.Append("[]");
                return;
            }

            if (indented) NewLine(sb, depth);
            sb.Append(']');
        }

        private static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void WriteDouble(StringBuilder sb, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                sb.Append("null");
                return;
            }

            // Whole numbers inside the exactly representable range print as integers (ports, pids).
            if (Math.Floor(number) == number && Math.Abs(number) < 9007199254740992d)
            {
                sb.Append(((long)number).ToString(CultureInfo.InvariantCulture));
                return;
            }

            sb.Append(number.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Appends <paramref name="value"/> as a quoted, escaped JSON string.</summary>
        public static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ' || IsLoneSurrogate(value, i))
                            {
                                // Lone surrogates cannot be encoded as UTF-8; keep them as an escape.
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }

                            break;
                    }
                }
            }

            sb.Append('"');
        }

        private static bool IsLoneSurrogate(string value, int index)
        {
            char c = value[index];
            if (char.IsHighSurrogate(c)) return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);
            if (char.IsLowSurrogate(c)) return index == 0 || !char.IsHighSurrogate(value[index - 1]);
            return false;
        }
    }
}
