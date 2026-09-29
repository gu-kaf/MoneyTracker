using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MoneyTracker
{
    /// <summary>所有导出都走这里，明细页、报表页、设置页共用同一套实现</summary>
    public static class Exporters
    {
        public static string RangeName()
        {
            if (Store.Data.transactions.Count == 0) return "记账数据";
            var ds = Store.Data.transactions.Select(t => t.date).Where(d => !string.IsNullOrEmpty(d)).OrderBy(d => d);
            if (!ds.Any()) return "记账数据";
            // 只有一天的数据时，起止日期是同一个，文件名里别写两遍
            return ds.First() == ds.Last()
                ? ds.First().Replace("-", "")
                : (ds.First() + "_" + ds.Last()).Replace("-", "");
        }

        public static string SuggestName(string prefix, string ext)
            => $"{prefix}_{RangeName()}.{ext}";

        private static string CsvCell(string s)
        {
            s ??= "";
            return (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        // ==================== CSV ====================

        public static void Csv(List<Transaction> list, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("日期,时间,类型,金额,分类,子分类,账户,转入账户,商户,备注,标签,来源,是否计入预算");
            foreach (var t in list)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    t.date, t.time, Util.TypeLabel(t.type), Util.Money(t.amount),
                    t.category, t.subcategory, t.account, t.toAccount,
                    t.merchant, t.note, string.Join("|", t.tags ?? new List<string>()),
                    SourceText(t.source), t.excludeFromBudget ? "否" : "是"
                }.Select(CsvCell)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); // 带 BOM，Excel 直接打开不乱码
        }

        public static string SourceText(string s) => s switch
        {
            "auto" => "规则",
            "csv" => "CSV",
            "import" => "导入",
            _ => "手动"
        };

        // ==================== JSON 整库备份 ====================

        public static void JsonBackup(string path, bool onlyFiltered = false, List<Transaction> filtered = null)
        {
            Store.Data.exportedAt = Util.NowStamp();
            object payload = onlyFiltered
                ? new { format = "money-tracker", version = 1, exportedAt = Store.Data.exportedAt, transactions = filtered }
                : (object)Store.Data;
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }), new UTF8Encoding(false));
        }

        // ==================== Excel ====================

        public static void Xlsx(List<Transaction> list, string path, bool withAccounts = true)
        {
            var sheets = new List<Sheet>();

            sheets.Add(new Sheet
            {
                Name = "明细",
                Headers = new[] { "日期", "时间", "类型", "金额(元)", "分类", "子分类", "账户", "转入账户", "商户/对方", "备注", "标签", "来源", "计入预算" },
                Rows = list.Select(t => new[]
                {
                    t.date, t.time, Util.TypeLabel(t.type), Util.Money(t.amount),
                    t.category, t.subcategory, t.account, t.toAccount, t.merchant, t.note,
                    string.Join("|", t.tags ?? new List<string>()), SourceText(t.source),
                    t.excludeFromBudget ? "否" : "是"
                }).ToList(),
                MoneyColumns = new List<int> { 3 }
            });

            var byCat = list.Where(t => t.type == "expense")
                            .GroupBy(t => string.IsNullOrEmpty(t.category) ? "未分类" : t.category)
                            .Select(g => new { cat = g.Key, n = g.Count(), sum = g.Sum(x => x.amount) })
                            .OrderByDescending(x => x.sum).ToList();
            long total = byCat.Sum(x => x.sum);
            sheets.Add(new Sheet
            {
                Name = "分类汇总",
                Headers = new[] { "分类", "笔数", "金额(元)", "占比" },
                Rows = byCat.Select(x => new[]
                {
                    x.cat, x.n.ToString(), Util.Money(x.sum),
                    total > 0 ? (x.sum * 100.0 / total).ToString("0.0") + "%" : "0%"
                }).ToList(),
                MoneyColumns = new List<int> { 2 }
            });

            var byMonth = list.GroupBy(t => t.date != null && t.date.Length >= 7 ? t.date.Substring(0, 7) : (t.date ?? ""))
                              .OrderBy(g => g.Key).ToList();
            sheets.Add(new Sheet
            {
                Name = "月度汇总",
                Headers = new[] { "月份", "支出(元)", "收入(元)", "结余(元)", "笔数" },
                Rows = byMonth.Select(g => new[]
                {
                    g.Key,
                    Util.Money(g.Where(t => t.type == "expense").Sum(t => t.amount)),
                    Util.Money(g.Where(t => t.type == "income").Sum(t => t.amount)),
                    Util.Money(g.Where(t => t.type == "income").Sum(t => t.amount) - g.Where(t => t.type == "expense").Sum(t => t.amount)),
                    g.Count().ToString()
                }).ToList(),
                MoneyColumns = new List<int> { 1, 2, 3 }
            });

            var byAcc = list.Where(t => t.type != "transfer").GroupBy(t => t.account)
                            .Select(g => new
                            {
                                acc = g.Key,
                                n = g.Count(),
                                outSum = g.Where(t => t.type == "expense").Sum(t => t.amount),
                                inSum = g.Where(t => t.type == "income").Sum(t => t.amount)
                            }).OrderByDescending(x => x.outSum).ToList();
            sheets.Add(new Sheet
            {
                Name = "账户流水",
                Headers = new[] { "账户", "笔数", "支出(元)", "收入(元)", "净流入(元)" },
                Rows = byAcc.Select(x => new[]
                {
                    x.acc, x.n.ToString(), Util.Money(x.outSum), Util.Money(x.inSum), Util.Money(x.inSum - x.outSum)
                }).ToList(),
                MoneyColumns = new List<int> { 2, 3, 4 }
            });

            if (withAccounts)
            {
                sheets.Add(new Sheet
                {
                    Name = "账户余额",
                    Headers = new[] { "账户", "类型", "当前余额(元)" },
                    Rows = Store.Data.accounts.Where(a => !a.archived)
                        .Select(a => new[] { a.name, KindText(a.kind), Util.Money(Store.BalanceOf(a.name)) }).ToList(),
                    MoneyColumns = new List<int> { 2 }
                });
            }

            XlsxWriter.Write(path, sheets);
        }

        public static string KindText(string kind) => kind switch
        {
            "cash" => "现金",
            "wallet" => "电子钱包",
            "debit" => "储蓄卡",
            "credit" => "信用卡",
            "investment" => "投资",
            _ => "其他"
        };

        // ==================== Word ====================

        public static void DocxReport(List<Transaction> list, string path, string title = "记账报表")
        {
            var b = new DocxBuilder();
            var dates = list.Select(t => t.date).Where(d => !string.IsNullOrEmpty(d)).OrderBy(d => d).ToList();

            b.Title(title);
            b.Muted($"统计区间：{(dates.Any() ? dates.First() : "-")} 至 {(dates.Any() ? dates.Last() : "-")}"
                  + $"　·　共 {list.Count} 笔　·　导出时间 {DateTime.Now:yyyy-MM-dd HH:mm}");

            long outSum = list.Where(t => t.type == "expense").Sum(t => t.amount);
            long inSum = list.Where(t => t.type == "income").Sum(t => t.amount);
            int days = dates.Distinct().Count();

            b.Heading("一、总览");
            b.KeyValue("支出合计", Util.Yuan(outSum));
            b.KeyValue("收入合计", Util.Yuan(inSum));
            b.KeyValue("结余", Util.Yuan(inSum - outSum));
            b.KeyValue("记录笔数", list.Count + " 笔");
            b.KeyValue("有记录的天数", days + " 天");
            b.KeyValue("日均支出", Util.Yuan(days > 0 ? outSum / days : 0));

            var byCat = list.Where(t => t.type == "expense")
                            .GroupBy(t => string.IsNullOrEmpty(t.category) ? "未分类" : t.category)
                            .Select(g => new { cat = g.Key, n = g.Count(), sum = g.Sum(x => x.amount) })
                            .OrderByDescending(x => x.sum).ToList();
            long total = byCat.Sum(x => x.sum);
            b.Heading("二、支出分类排行");
            if (byCat.Count == 0) b.Paragraph("这段时间没有支出记录。");
            else b.Table(new[] { "排名", "分类", "笔数", "金额", "占比" },
                byCat.Select((x, i) => new[]
                {
                    (i + 1).ToString(), x.cat, x.n + " 笔", Util.Yuan(x.sum),
                    total > 0 ? (x.sum * 100.0 / total).ToString("0.0") + "%" : "0%"
                }).ToList());

            b.Heading("三、月度走势");
            var byMonth = list.GroupBy(t => t.date != null && t.date.Length >= 7 ? t.date.Substring(0, 7) : (t.date ?? ""))
                              .OrderBy(g => g.Key).ToList();
            b.Table(new[] { "月份", "支出", "收入", "结余" },
                byMonth.Select(g => new[]
                {
                    g.Key,
                    Util.Yuan(g.Where(t => t.type == "expense").Sum(t => t.amount)),
                    Util.Yuan(g.Where(t => t.type == "income").Sum(t => t.amount)),
                    Util.Yuan(g.Where(t => t.type == "income").Sum(t => t.amount) - g.Where(t => t.type == "expense").Sum(t => t.amount))
                }).ToList());

            var top = list.Where(t => t.type == "expense").OrderByDescending(t => t.amount).Take(10).ToList();
            if (top.Count > 0)
            {
                b.Heading("四、这十笔最花钱");
                b.Table(new[] { "日期", "金额", "分类", "商户/对方", "备注" },
                    top.Select(t => new[] { t.date, Util.Yuan(t.amount), t.category, t.merchant, t.note }).ToList());
            }

            var accs = Store.Data.accounts.Where(a => !a.archived).ToList();
            if (accs.Count > 0)
            {
                b.Heading("五、账户余额");
                b.Table(new[] { "账户", "类型", "余额" },
                    accs.Select(a => new[] { a.name, KindText(a.kind), Util.Yuan(Store.BalanceOf(a.name)) }).ToList());
                b.KeyValue("净资产合计", Util.Yuan(Store.NetAssets()));
            }

            b.PageBreak();
            b.Heading("六、全部明细");
            b.Muted($"下面是从 {dates.FirstOrDefault() ?? "-"} 起的全部 {list.Count} 笔记录。");
            b.Table(new[] { "日期", "类型", "金额", "分类", "账户", "商户/对方", "备注" },
                list.Select(t => new[]
                {
                    t.date, Util.TypeLabel(t.type), Util.Yuan(t.amount), t.category, t.account, t.merchant, t.note
                }).ToList());

            b.Save(path);
        }
    }
}