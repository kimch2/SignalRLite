using System;
using System.Collections.Generic;
using System.Text;

namespace SignalRLite.Utility
{
    /// <summary>
    /// Minimal JSON parser and encoder for the SignalR protocol.
    /// Supports: null, bool, long, double, string, object (Dictionary), array (List).
    /// </summary>
    public static class SimpleJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        public static string Stringify(object value)
        {
            var sb = new StringBuilder(64);
            AppendValue(sb, value);
            return sb.ToString();
        }

        private static object ParseValue(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            if (i >= json.Length) return null;

            switch (json[i])
            {
                case '{': return ParseObject(json, ref i);
                case '[': return ParseArray(json, ref i);
                case '"': return ParseString(json, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default:  return ParseNumber(json, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // skip {
            SkipWhitespace(json, ref i);
            while (i < json.Length && json[i] != '}')
            {
                SkipWhitespace(json, ref i);
                if (json[i] != '"') break;
                string key = ParseString(json, ref i);
                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ':') i++;
                dict[key] = ParseValue(json, ref i);
                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
                SkipWhitespace(json, ref i);
            }
            if (i < json.Length) i++; // skip }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int i)
        {
            var list = new List<object>();
            i++; // skip [
            SkipWhitespace(json, ref i);
            while (i < json.Length && json[i] != ']')
            {
                list.Add(ParseValue(json, ref i));
                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
                SkipWhitespace(json, ref i);
            }
            if (i < json.Length) i++; // skip ]
            return list;
        }

        private static string ParseString(string json, ref int i)
        {
            i++; // skip opening "
            var sb = new StringBuilder();
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"':  sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < json.Length)
                            {
                                sb.Append((char)Convert.ToInt32(json.Substring(i + 1, 4), 16));
                                i += 4;
                            }
                            break;
                        default:   sb.Append(json[i]); break;
                    }
                }
                else
                {
                    sb.Append(json[i]);
                }
                i++;
            }
            if (i < json.Length) i++; // skip closing "
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int i)
        {
            int start = i;
            if (i < json.Length && json[i] == '-') i++;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == 'e' || json[i] == 'E' || json[i] == '+' || json[i] == '-'))
                i++;
            string num = json.Substring(start, i - start);
            if (num.IndexOf('.') >= 0 || num.IndexOf('e') >= 0 || num.IndexOf('E') >= 0)
                return double.Parse(num, System.Globalization.CultureInfo.InvariantCulture);
            if (long.TryParse(num, out long l)) return l;
            return double.Parse(num, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void SkipWhitespace(string json, ref int i)
        {
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n' || json[i] == '\r'))
                i++;
        }

        private static void AppendValue(StringBuilder sb, object value)
        {
            if (value == null)           { sb.Append("null"); return; }
            if (value is bool b)         { sb.Append(b ? "true" : "false"); return; }
            if (value is string s)       { AppendString(sb, s); return; }
            if (value is IDictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    AppendString(sb, kv.Key);
                    sb.Append(':');
                    AppendValue(sb, kv.Value);
                    first = false;
                }
                sb.Append('}');
                return;
            }
            if (value is System.Collections.IList list)
            {
                sb.Append('[');
                for (int j = 0; j < list.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    AppendValue(sb, list[j]);
                }
                sb.Append(']');
                return;
            }
            if (value is int || value is long || value is short || value is byte || value is uint || value is ulong)
            {
                sb.Append(Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (value is float || value is double || value is decimal)
            {
                sb.Append(Convert.ToDouble(value).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            // Fallback: use JsonUtility for [Serializable] objects
            sb.Append(UnityEngine.JsonUtility.ToJson(value));
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
