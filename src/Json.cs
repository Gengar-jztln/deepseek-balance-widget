using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DeepSeekPet
{
    /// <summary>
    /// 极简 JSON 读写实现，只覆盖本程序需要的子集：
    /// 对象 / 数组 / 字符串 / 数字 / 布尔 / null。避免引入外部依赖。
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON 文本为空");
            int i = 0;
            object value = ParseValue(text, ref i);
            SkipWhitespace(text, ref i);
            if (i < text.Length) throw new FormatException("JSON 结尾存在多余字符");
            return value;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            Dictionary<string, object> obj = Parse(text) as Dictionary<string, object>;
            if (obj == null) throw new FormatException("JSON 根节点不是对象");
            return obj;
        }

        public static string Write(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        // ---------------- 读取辅助 ----------------

        public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            object value;
            if (!obj.TryGetValue(key, out value)) return null;
            return value as Dictionary<string, object>;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            object value;
            if (!obj.TryGetValue(key, out value)) return null;
            return value as List<object>;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback)
        {
            if (obj == null) return fallback;
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return fallback;
            string s = value as string;
            return s != null ? s : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static decimal GetDecimal(Dictionary<string, object> obj, string key, decimal fallback)
        {
            if (obj == null) return fallback;
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return fallback;
            if (value is decimal) return (decimal)value;
            if (value is double) return Convert.ToDecimal((double)value, CultureInfo.InvariantCulture);
            string s = value as string;
            decimal parsed;
            if (s != null && decimal.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback)
        {
            decimal d = GetDecimal(obj, key, fallback);
            try { return (int)decimal.Round(d, MidpointRounding.AwayFromZero); }
            catch { return fallback; }
        }

        public static long GetLong(Dictionary<string, object> obj, string key, long fallback)
        {
            decimal d = GetDecimal(obj, key, fallback);
            try { return (long)decimal.Round(d, MidpointRounding.AwayFromZero); }
            catch { return fallback; }
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback)
        {
            if (obj == null) return fallback;
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return fallback;
            if (value is bool) return (bool)value;
            string s = value as string;
            if (s != null)
            {
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return fallback;
        }

        /// <summary>把金额字符串（如 "10.64"）安全转换为 decimal。</summary>
        public static bool TryMoney(string raw, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrEmpty(raw)) return false;
            return decimal.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        // ---------------- 解析实现 ----------------

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        private static void ExpectLiteral(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new FormatException("非法 JSON 字面量，位置 " + i.ToString(CultureInfo.InvariantCulture));
            i += literal.Length;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObjectBody(s, ref i);
                case '[': return ParseArrayBody(s, ref i);
                case '"': return ParseStringBody(s, ref i);
                case 't': ExpectLiteral(s, ref i, "true"); return true;
                case 'f': ExpectLiteral(s, ref i, "false"); return false;
                case 'n': ExpectLiteral(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObjectBody(string s, ref int i)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 对象缺少键，位置 " + i.ToString(CultureInfo.InvariantCulture));
                string key = ParseStringBody(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 对象缺少冒号，位置 " + i.ToString(CultureInfo.InvariantCulture));
                i++;
                result[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("JSON 对象分隔符非法，位置 " + i.ToString(CultureInfo.InvariantCulture));
            }
            return result;
        }

        private static List<object> ParseArrayBody(string s, ref int i)
        {
            List<object> result = new List<object>();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }
            while (true)
            {
                result.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("JSON 数组分隔符非法，位置 " + i.ToString(CultureInfo.InvariantCulture));
            }
            return result;
        }

        private static string ParseStringBody(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // 开引号
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串未闭合");
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("JSON 转义未完成");
                    char e = s[i++];
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
                            if (i + 4 > s.Length) throw new FormatException("JSON Unicode 转义不完整");
                            sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new FormatException("非法 JSON 转义字符: \\" + e);
                    }
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (start == i) throw new FormatException("JSON 数字格式非法，位置 " + start.ToString(CultureInfo.InvariantCulture));
            string raw = s.Substring(start, i - start);
            decimal d;
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
            double dbl;
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out dbl)) return dbl;
            throw new FormatException("无法解析 JSON 数字: " + raw);
        }

        // ---------------- 写出实现 ----------------

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            if (value == null) { sb.Append("null"); return; }

            Dictionary<string, object> obj = value as Dictionary<string, object>;
            if (obj != null) { WriteObject(sb, obj, depth); return; }

            System.Collections.IDictionary dictionary = value as System.Collections.IDictionary;
            if (dictionary != null)
            {
                Dictionary<string, object> copy = new Dictionary<string, object>();
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                    copy[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] = entry.Value;
                WriteObject(sb, copy, depth);
                return;
            }

            System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
            if (list != null && !(value is string)) { WriteArray(sb, list, depth); return; }

            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }

            if (value is decimal)
            {
                sb.Append(((decimal)value).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is double || value is float)
            {
                sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, Dictionary<string, object> obj, int depth)
        {
            if (obj.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> pair in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('\n');
                Indent(sb, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(": ");
                WriteValue(sb, pair.Value, depth + 1);
            }
            sb.Append('\n');
            Indent(sb, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, System.Collections.IEnumerable list, int depth)
        {
            List<object> items = new List<object>();
            foreach (object item in list) items.Add(item);
            if (items.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[');
            for (int k = 0; k < items.Count; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append('\n');
                Indent(sb, depth + 1);
                WriteValue(sb, items[k], depth + 1);
            }
            sb.Append('\n');
            Indent(sb, depth);
            sb.Append(']');
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
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
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
