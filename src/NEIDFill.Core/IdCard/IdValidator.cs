using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NEIDFill.Core.IdCard
{
    /// <summary>
    /// GB 11643-1999 公民身份号码校验：
    /// 18 位 = 6 位数字码(省市区) + 8 位出生日期 + 3 位顺序码 + 1 位校验码(ISO 7064 MOD 11-2)。
    /// </summary>
    public static class IdValidator
    {
        private static readonly HashSet<string> ProvinceCodes = new HashSet<string>
        {
            "11", "12", "13", "14", "15",          // 华北
            "21", "22", "23",                      // 东北
            "31", "32", "33", "34", "35", "36", "37", // 华东
            "41", "42", "43", "44", "45", "46",    // 中南
            "50", "51", "52", "53", "54",          // 西南
            "61", "62", "63", "64", "65",          // 西北
            "71",                                  // 台湾
            "81", "82", "83",                      // 港澳、台湾居民居住证（810000/820000/830000 号段）
            "91"                                   // 国外
        };

        private static readonly int[] Weights = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
        private const string CheckCodes = "10X98765432";

        /// <summary>归一化输入：去除全部 Unicode 空白（含 U+3000 全角空格、制表符）、X 统一大写。</summary>
        public static string? Normalize(string? input)
        {
            if (input == null) return null;
            StringBuilder sb = new StringBuilder(input.Length);
            foreach (char c in input.Trim())
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString().ToUpperInvariant();
        }

        /// <summary>返回校验结果；失败时 error 指出第一个不通过的原因。</summary>
        public static bool Validate(string? id, out string? error)
        {
            error = null;
            id = Normalize(id);

            // 显式判空而非 IsNullOrEmpty：.NET Framework 引用程序集无 null 测试注解，编译器无法据此断定 id 非空
            if (id == null || id.Length == 0)
            {
                error = "身份证号不能为空";
                return false;
            }
            if (id.Length != 18)
            {
                error = "身份证号应为 18 位（二代证）";
                return false;
            }
            if (!Regex.IsMatch(id.Substring(0, 17), @"^[0-9]{17}$"))
            {
                error = "前 17 位必须全部为数字";
                return false;
            }
            if (!Regex.IsMatch(id.Substring(17, 1), @"^[0-9X]$"))
            {
                error = "末位应为数字或 X";
                return false;
            }
            if (!ProvinceCodes.Contains(id.Substring(0, 2)))
            {
                error = "地区码无效";
                return false;
            }
            if (!IsValidBirthday(id.Substring(6, 8)))
            {
                error = "出生日期无效";
                return false;
            }
            if (!VerifyChecksum(id))
            {
                error = "校验位错误（号码可能被误录）";
                return false;
            }
            return true;
        }

        public static bool IsValid(string? id)
        {
            string? ignored;
            return Validate(id, out ignored);
        }

        private static bool IsValidBirthday(string ymd)
        {
            DateTime date;
            if (!DateTime.TryParseExact(ymd, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date))
                return false;
            return date >= new DateTime(1900, 1, 1) && date <= DateTime.Today;
        }

        private static bool VerifyChecksum(string id)
        {
            int sum = 0;
            for (int i = 0; i < 17; i++)
                sum += (id[i] - '0') * Weights[i];
            return CheckCodes[sum % 11] == id[17];
        }
    }
}
