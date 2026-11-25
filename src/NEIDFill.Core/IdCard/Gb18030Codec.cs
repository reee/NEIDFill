using System;
using System.Text;

namespace NEIDFill.Core.IdCard
{
    /// <summary>
    /// 厂商 DLL 字段缓冲解码。GBK 是 GB18030 子集：取到首个 NUL 为止按 GB18030 解码，
    /// 与系统区域代码页无关（原程序 StringBuilder+CharSet.Ansi 编组在非中文区域会把 GBK 姓名解成乱码）。
    /// </summary>
    public static class Gb18030Codec
    {
        public static readonly Encoding Encoding = GetEncoding("GB18030");

        private static Encoding GetEncoding(string name)
        {
            try { return Encoding.GetEncoding(name); }
            catch (ArgumentException) { return Encoding.UTF8; } // 极端精简系统缺代码页：不至于崩，仅可能乱码
        }

        public static string Decode(byte[] buf)
        {
            if (buf == null) return "";
            int end = 0;
            while (end < buf.Length && buf[end] != 0) end++;
            return Encoding.GetString(buf, 0, end).Trim();
        }
    }
}
