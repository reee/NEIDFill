using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NEIDFill.Core
{
    /// <summary>
    /// 极简 JSON 解析（设置文件用），不引入第三方 JSON 库。
    /// Parse 返回值约定：对象=Dictionary&lt;string,object?&gt;、数组=List&lt;object?&gt;、
    /// 字符串=string、数字=double、布尔=bool、null=null。
    /// </summary>
    public static class Json
    {
        // 递归深度上限：防止构造的深层嵌套输入触发不可捕获的 StackOverflowException
        private const int MaxDepth = 64;

        public static object? Parse(string? json)
        {
            if (json == null) throw new FormatException("JSON 为空");
            int i = 0;
            object? v = ParseValue(json, ref i, 0);
            SkipWs(json, ref i);
            if (i != json.Length) throw new FormatException("JSON 尾部有多余内容");
            return v;
        }

        // ------------------------------------------------------------- 解析

        private static object? ParseValue(string s, ref int i, int depth)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth);
            if (c == '[') return ParseArray(s, ref i, depth);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object?> ParseObject(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON 嵌套过深");
            var o = new Dictionary<string, object?>();
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("期望对象键名");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("期望 ':'");
                i++;
                o[key] = ParseValue(s, ref i, depth + 1);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 意外结束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return o; }
                throw new FormatException("期望 ',' 或 '}'");
            }
        }

        private static List<object?> ParseArray(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON 嵌套过深");
            var a = new List<object?>();
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 意外结束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new FormatException("期望 ',' 或 ']'");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // '"'
            while (true)
            {
                if (i >= s.Length) throw new FormatException("字符串未闭合");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("转义未闭合");
                char n = s[i++];
                switch (n)
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
                        if (i + 4 > s.Length) throw new FormatException("\\u 转义不完整");
                        // 先逐字符校验 4 位十六进制，再解析：TryParse(HexNumber) 会容忍前后空白与正负号
                        for (int k = 0; k < 4; k++)
                        {
                            char h = s[i + k];
                            if (!((h >= '0' && h <= '9') || (h >= 'a' && h <= 'f') || (h >= 'A' && h <= 'F')))
                                throw new FormatException("非法 \\u 转义: " + s.Substring(i, 4));
                        }
                        ushort code;
                        if (!ushort.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                            throw new FormatException("非法 \\u 转义: " + s.Substring(i, 4));
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw new FormatException("非法转义 \\" + n);
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && ("+-0123456789.eE".IndexOf(s[i]) >= 0)) i++;
            double d;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out d))
                throw new FormatException("非法数字: " + s.Substring(start, i - start));
            return d;
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new FormatException("期望 " + literal);
            i += literal.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }
    }
}
