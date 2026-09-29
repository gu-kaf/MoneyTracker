using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MoneyTracker
{
    public static class Util
    {
        /// <summary>分 -> "1,234.56"</summary>
        public static string Money(long cents, bool sign = false)
        {
            string s = (Math.Abs(cents) / 100.0).ToString("N2", CultureInfo.InvariantCulture);
            if (sign) s = (cents < 0 ? "-" : "+") + s;
            return s;
        }

        /// <summary>分 -> "¥1,234.56"</summary>
        public static string Yuan(long cents, bool sign = false)
        {
            var f = App.Data?.settings?.currency ?? "¥";
            return f + Money(cents, sign);
        }

        /// <summary>"38.50" / "¥38.50" / "-38.5" / "1,234.56" -> 分（解析失败返回 0）</summary>
        public static long ToCents(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var sb = new StringBuilder();
            bool neg = false, dot = false;
            foreach (char c in text)
            {
                if (c == '-' || c == '\u2212') { neg = true; continue; }
                if (c == '.') { if (dot) continue; dot = true; sb.Append(c); continue; }
                if (c >= '0' && c <= '9') sb.Append(c);
            }
            if (sb.Length == 0) return 0;
            if (!decimal.TryParse(sb.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return 0;
            long cents = (long)Math.Round(d * 100m, MidpointRounding.AwayFromZero);
            return neg ? -cents : cents;
        }

        /// <summary>把各种日期写法解析成 yyyy-MM-dd，认不出返回 null</summary>
        public static string ParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().Replace("年", "-").Replace("月", "-").Replace("日", "").Replace("/", "-").Replace(".", "-");
            var fmts = new[] { "yyyy-M-d", "yyyy-M-d H:mm:ss", "yyyy-M-d H:mm", "yyyy-M-dTHH:mm:ss",
                               "M-d-yyyy", "yyyyMMdd", "yyyy-M", "M-d" };
            foreach (var f in fmts)
            {
                if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d.ToString("yyyy-MM-dd");
            }
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                return d2.ToString("yyyy-MM-dd");
            return null;
        }

        /// <summary>从日期串里取 HH:mm，取不到返回空</summary>
        public static string ParseTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var m = System.Text.RegularExpressions.Regex.Match(raw, @"(\d{1,2}):(\d{2})(?::(\d{2}))?");
            if (!m.Success) return "";
            int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value);
            if (h > 23 || mi > 59) return "";
            return h.ToString("00") + ":" + mi.ToString("00");
        }

        /// <summary>交易指纹：日期|金额|商户或备注|账户（和共享规格一致，用于导入去重）</summary>
        public static string HashOf(Transaction t)
        {
            string key = (t.date ?? "") + "|" + t.amount + "|" + (string.IsNullOrEmpty(t.merchant) ? (t.note ?? "") : t.merchant) + "|" + (t.account ?? "");
            return Md5(key);
        }

        public static string Md5(string s)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(s ?? ""));
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string Today() => DateTime.Now.ToString("yyyy-MM-dd");
        public static string ThisMonth() => DateTime.Now.ToString("yyyy-MM");
        public static string NowStamp() => DateTime.Now.ToString("s");

        /// <summary>某月的第一天/最后一天</summary>
        public static (string from, string to) MonthRange(string ym)
        {
            if (!DateTime.TryParse(ym + "-01", out var d)) d = DateTime.Now;
            return (d.ToString("yyyy-MM-01"), d.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"));
        }

        public static string[] LastMonths(int n, string endYm = null)
        {
            DateTime d = DateTime.TryParse((endYm ?? ThisMonth()) + "-01", out var x) ? x : DateTime.Now;
            var list = new List<string>();
            for (int i = n - 1; i >= 0; i--) list.Add(d.AddMonths(-i).ToString("yyyy-MM"));
            return list.ToArray();
        }

        /// <summary>交易对现金流的影响（转账不算收支）</summary>
        public static long CashFlow(Transaction t)
            => t.type == "income" ? t.amount : (t.type == "expense" ? -t.amount : 0);

        public static string TypeLabel(string t) => t == "income" ? "收入" : (t == "transfer" ? "转账" : "支出");
    }
}