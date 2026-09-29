using System;
using System.Collections.Generic;
using System.Linq;

namespace MoneyTracker
{
    public class Insight
    {
        public string Title;
        public string Text;
        public string Level = "info";   // info / good / warn
        public string Icon = "·";
    }

    /// <summary>真正的数据分析：所有结论都从你的流水里算出来，没有一句是模板套话</summary>
    public static class Analyzer
    {
        public static List<Insight> Run(string month)
        {
            var list = new List<Insight>();
            var (f, t) = Util.MonthRange(month);
            var cur = Store.Scoped().Where(x => In(x, f, t)).ToList();
            int y = int.Parse(month.Substring(0, 4)), m = int.Parse(month.Substring(5, 2));
            var prevM = new DateTime(y, m, 1).AddMonths(-1).ToString("yyyy-MM");
            var (pf, pt) = Util.MonthRange(prevM);
            var prev = Store.Scoped().Where(x => In(x, pf, pt)).ToList();

            long curOut = cur.Where(x => x.type == "expense").Sum(x => x.amount);
            long curIn = cur.Where(x => x.type == "income").Sum(x => x.amount);
            long prevOut = prev.Where(x => x.type == "expense").Sum(x => x.amount);
            long prevIn = prev.Where(x => x.type == "income").Sum(x => x.amount);

            if (cur.Count == 0)
            {
                list.Add(new Insight { Title = "这个月还没有记录", Text = "先记几笔，分析才有东西可算。" });
                return list;
            }

            // ---------- 1. 环比 ----------
            if (prevOut > 0)
            {
                double chg = (curOut - prevOut) * 100.0 / prevOut;
                var better = chg <= 0;
                list.Add(new Insight
                {
                    Level = better ? "good" : (Math.Abs(chg) > 25 ? "warn" : "info"),
                    Icon = better ? "↓" : "↑",
                    Title = better ? $"比上月少花了 {Math.Abs(chg):0.0}%" : $"比上月多花了 {Math.Abs(chg):0.0}%",
                    Text = $"上月支出 {Util.Yuan(prevOut)}，这个月 {Util.Yuan(curOut)}，" +
                           (better ? $"省下 {Util.Yuan(prevOut - curOut)}。" : $"多花 {Util.Yuan(curOut - prevOut)}。")
                });
            }
            else if (curOut > 0)
            {
                list.Add(new Insight { Title = "上月没有可比数据", Text = $"这个月目前支出 {Util.Yuan(curOut)}。" });
            }

            // ---------- 2. 储蓄率 ----------
            if (curIn > 0)
            {
                double rate = (curIn - curOut) * 100.0 / curIn;
                list.Add(new Insight
                {
                    Level = rate >= 30 ? "good" : rate >= 0 ? "info" : "warn",
                    Icon = "¥",
                    Title = rate >= 0 ? $"这个月存下了 {rate:0.0}% 的收入" : "这个月入不敷出",
                    Text = rate >= 0
                        ? $"收入 {Util.Yuan(curIn)}，支出 {Util.Yuan(curOut)}，结余 {Util.Yuan(curIn - curOut)}。一般存下 20%~30% 算健康。"
                        : $"收入 {Util.Yuan(curIn)}，但花掉了 {Util.Yuan(curOut)}，缺口 {Util.Yuan(curOut - curIn)}，动到老本了。"
                });
            }

            // ---------- 3. 支出结构 ----------
            var byCat = cur.Where(x => x.type == "expense")
                           .GroupBy(x => string.IsNullOrEmpty(x.category) ? "未分类" : x.category)
                           .Select(g => new { cat = g.Key, sum = g.Sum(x => x.amount), n = g.Count() })
                           .OrderByDescending(x => x.sum).ToList();
            if (byCat.Count > 0 && curOut > 0)
            {
                var top = byCat[0];
                double share = top.sum * 100.0 / curOut;
                list.Add(new Insight
                {
                    Level = share > 45 ? "warn" : "info",
                    Icon = "◕",
                    Title = $"钱主要花在「{top.cat}」上，占 {share:0.0}%",
                    Text = $"总共 {Util.Yuan(top.sum)}，{top.n} 笔，" +
                           (byCat.Count > 1 ? $"第二名是「{byCat[1].cat}」{Util.Yuan(byCat[1].sum)}。" : "") +
                           (share > 45 ? "　这一类占比偏高，压一压最有效。" : "")
                });
            }

            // ---------- 4. 单笔最大 ----------
            var big = cur.Where(x => x.type == "expense").OrderByDescending(x => x.amount).FirstOrDefault();
            if (big != null && curOut > 0)
            {
                list.Add(new Insight
                {
                    Icon = "▲",
                    Title = $"最大的一笔：{Util.Yuan(big.amount)}",
                    Text = $"{big.date}　{big.category}{(string.IsNullOrEmpty(big.merchant) ? "" : "　" + big.merchant)}，" +
                           $"占本月支出的 {big.amount * 100.0 / curOut:0.0}%。"
                });
            }

            // ---------- 5. 常去的地方 ----------
            var byMerchant = cur.Where(x => x.type == "expense" && !string.IsNullOrWhiteSpace(x.merchant))
                                .GroupBy(x => x.merchant.Trim())
                                .Select(g => new { mer = g.Key, sum = g.Sum(x => x.amount), n = g.Count() })
                                .OrderByDescending(x => x.sum).Take(3).ToList();
            if (byMerchant.Count > 0)
            {
                list.Add(new Insight
                {
                    Icon = "◎",
                    Title = "最常花钱的地方",
                    Text = string.Join("；", byMerchant.Select((x, i) =>
                        $"{i + 1}. {x.mer} {x.n} 笔共 {Util.Yuan(x.sum)}")) + "。"
                });
            }

            // ---------- 6. 频率与节奏 ----------
            var days = cur.Select(x => x.date).Distinct().Count();
            int dim = DateTime.DaysInMonth(y, m);
            bool isCurrentMonth = month == Util.ThisMonth();
            int elapsed = isCurrentMonth ? DateTime.Today.Day : dim;
            if (days > 0)
            {
                long avgDay = curOut / Math.Max(1, days);
                list.Add(new Insight
                {
                    Icon = "▤",
                    Title = $"记账 {days} 天，日均支出 {Util.Yuan(avgDay)}",
                    Text = $"这个月有 {days} 天有记录（已经过去 {elapsed} 天），平均每天 {cur.Where(x => x.type == "expense").Count() * 1.0 / days:0.0} 笔。"
                });
            }

            // ---------- 7. 工作日 vs 周末 ----------
            long wk = 0, we = 0; int wkn = 0, wen = 0;
            foreach (var x in cur.Where(x => x.type == "expense"))
            {
                if (!DateTime.TryParse(x.date, out var d)) continue;
                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) { we += x.amount; wen++; }
                else { wk += x.amount; wkn++; }
            }
            if (wen > 0 && wkn > 0)
            {
                bool weekendHeavy = wen > 0 && (we / wen) > (wk / wkn) * 1.3;
                list.Add(new Insight
                {
                    Level = weekendHeavy ? "warn" : "info",
                    Icon = "◇",
                    Title = weekendHeavy ? "周末是你花钱最快的时候" : "工作日和周末的花钱节奏差不多",
                    Text = $"周末平均每笔 {Util.Yuan(we / wen)}，工作日平均每笔 {Util.Yuan(wk / wkn)}。"
                           + (weekendHeavy ? "周末出门前先定个额度会好很多。" : "")
                });
            }

            // ---------- 8. 时段 ----------
            var withTime = cur.Where(x => x.type == "expense" && !string.IsNullOrEmpty(x.time)).ToList();
            if (withTime.Count >= 5)
            {
                int Night(Transaction x) => int.TryParse(x.time.Split(':')[0], out var h) ? h : -1;
                var buckets = new (string name, int lo, int hi)[]
                {
                    ("早上 6-10 点", 6, 10), ("中午 10-14 点", 10, 14),
                    ("下午 14-18 点", 14, 18), ("晚上 18-22 点", 18, 22), ("深夜 22-6 点", 22, 30)
                };
                var hit = buckets.Select(b => new
                {
                    b.name,
                    sum = withTime.Where(x => { var h = Night(x); if (h < 0) return false; if (b.hi > 24) return h >= b.lo || h < (b.hi - 24); return h >= b.lo && h < b.hi; }).Sum(x => x.amount)
                }).OrderByDescending(x => x.sum).ToList();
                if (hit[0].sum > 0)
                {
                    list.Add(new Insight
                    {
                        Icon = "◔",
                        Title = $"花钱最多的是{hit[0].name}",
                        Text = $"这段时间花掉 {Util.Yuan(hit[0].sum)}，占 {hit[0].sum * 100.0 / withTime.Sum(x => x.amount):0.0}%。"
                    });
                }
            }

            // ---------- 9. 异常分类 ----------
            foreach (var c in byCat)
            {
                var p = prev.Where(x => x.type == "expense" && (string.IsNullOrEmpty(x.category) ? "未分类" : x.category) == c.cat)
                            .Sum(x => x.amount);
                if (p > 0 && c.sum > p * 1.5 && c.sum - p > 10000)
                {
                    list.Add(new Insight
                    {
                        Level = "warn",
                        Icon = "!",
                        Title = $"「{c.cat}」比上月涨了 {(c.sum * 100.0 / p - 100):0.0}%",
                        Text = $"上月 {Util.Yuan(p)}，这个月已经 {Util.Yuan(c.sum)}，多了 {Util.Yuan(c.sum - p)}。翻一下明细看看是什么。"
                    });
                }
            }

            // ---------- 10. 疑似固定订阅 ----------
            var subs = Store.Scoped().Where(x => x.type == "expense")
                .GroupBy(x => new { x.merchant, x.amount })
                .Where(g => g.Count() >= 2 && !string.IsNullOrWhiteSpace(g.Key.merchant) && g.Key.amount <= 20000)
                .Select(g => new
                {
                    mer = g.Key.merchant,
                    amt = g.Key.amount,
                    n = g.Count(),
                    months = g.Select(x => x.date != null && x.date.Length >= 7 ? x.date.Substring(0, 7) : "").Distinct().Count()
                })
                .Where(x => x.months >= 2 && x.n >= 2)
                .OrderByDescending(x => x.amt * x.months).Take(4).ToList();
            if (subs.Count > 0)
            {
                long perMonth = subs.Sum(x => x.amt);
                list.Add(new Insight
                {
                    Level = "info",
                    Icon = "↻",
                    Title = "疑似自动续费 / 固定订阅",
                    Text = string.Join("；", subs.Select(x => $"{x.mer} 每笔 {Util.Yuan(x.amt)}，出现过 {x.months} 个月"))
                         + $"。这些加起来每月约 {Util.Yuan(perMonth)}，一年就是 {Util.Yuan(perMonth * 12)}，用不上的记得退掉。"
                });
            }

            // ---------- 11. 预测 ----------
            if (isCurrentMonth && elapsed >= 3 && curOut > 0)
            {
                long forecast = (long)(curOut * (double)dim / elapsed);
                long budget = Store.Data.budgets.FirstOrDefault(b => b.period == "monthly" && b.scope == "total")?.amount ?? 0;
                list.Add(new Insight
                {
                    Level = budget > 0 && forecast > budget ? "warn" : "info",
                    Icon = "→",
                    Title = $"照这个速度，这个月大概花 {Util.Yuan(forecast)}",
                    Text = $"已经过 {elapsed} 天花了 {Util.Yuan(curOut)}，本月共 {dim} 天。"
                         + (budget > 0
                            ? (forecast > budget
                                ? $"总预算是 {Util.Yuan(budget)}，会超 {Util.Yuan(forecast - budget)}，后面每天要控制在 {Util.Yuan(Math.Max(0, (budget - curOut) / Math.Max(1, dim - elapsed)))} 以内。"
                                : $"总预算 {Util.Yuan(budget)}，还撑得住。")
                            : "还没设总预算，可以在预算页设一个。")
                });
            }

            // ---------- 12. 省钱抓手 ----------
            var tips = new List<string>();
            if (byCat.Count > 0 && byCat[0].sum * 100.0 / Math.Max(1, curOut) > 35)
                tips.Add($"「{byCat[0].cat}」是最大头，只要这类少花 10% 就能省 {Util.Yuan(byCat[0].sum / 10)}");
            var repeat = cur.Where(x => x.type == "expense" && x.amount <= 5000 && !string.IsNullOrWhiteSpace(x.merchant))
                            .GroupBy(x => x.merchant.Trim()).Where(g => g.Count() >= 8).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (repeat != null)
                tips.Add($"在「{repeat.Key}」买了 {repeat.Count()} 次，多为小额，一年下来是 {Util.Yuan(repeat.Sum(x => x.amount) * 12)} 的量级");
            if (curIn > 0 && (curIn - curOut) * 100.0 / curIn < 10)
                tips.Add("结余不到收入的 10%，先把每月固定支出（房租、订阅）理一遍最见效");
            if (we > 0 && (we / Math.Max(1, wen)) > (wk / Math.Max(1, wkn)) * 1.3)
                tips.Add("周末单笔金额明显更高，出门前设个额度");
            if (tips.Count == 0)
                tips.Add("这个月结构挺健康，没发现明显可以下手的地方");

            list.Add(new Insight
            {
                Icon = "✦",
                Title = "如果只改一件事",
                Text = string.Join("；", tips.Take(3)) + "。"
            });

            // ---------- 13. 连续记账 ----------
            var allDays = Store.Scoped().Select(x => x.date).Where(d => !string.IsNullOrEmpty(d))
                                .Distinct().OrderByDescending(d => d).ToList();
            int streak = 0;
            var cursor = DateTime.Today.Date;
            foreach (var d in allDays)
            {
                if (!DateTime.TryParse(d, out var dd)) continue;
                if (dd.Date == cursor) { streak++; cursor = cursor.AddDays(-1); }
                else if (dd.Date < cursor) break;
            }
            if (streak >= 3)
            {
                list.Add(new Insight
                {
                    Level = "good", Icon = "✓",
                    Title = $"已经连续记账 {streak} 天",
                    Text = "记得越久，报表越准。"
                });
            }

            return list;
        }

        private static bool In(Transaction x, string f, string t)
            => !string.IsNullOrEmpty(x.date) && string.CompareOrdinal(x.date, f) >= 0 && string.CompareOrdinal(x.date, t) <= 0;

        /// <summary>近 N 个月每月的支出额（用于趋势图）</summary>
        public static List<(string, double)> MonthlyExpense(int n, string endMonth = null)
        {
            var res = new List<(string, double)>();
            foreach (var m in Util.LastMonths(n, endMonth))
            {
                var (f, t) = Util.MonthRange(m);
                long sum = Store.Scoped().Where(x => x.type == "expense" && In(x, f, t)).Sum(x => x.amount);
                res.Add((m.Substring(5) + "月", sum / 100.0));
            }
            return res;
        }
    }
}