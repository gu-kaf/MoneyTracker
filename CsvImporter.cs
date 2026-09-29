using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MoneyTracker
{
    public class ImportResult
    {
        public string File = "";
        public string Format = "generic";
        public int Total, Added, Skipped, Duplicated;
        public List<string> SkipReasons = new List<string>();
        public List<string> Errors = new List<string>();
        public long OutSum, InSum;

        public string Summary =>
            $"{Path.GetFileName(File)}：认出 {Total} 行，新导入 {Added} 笔，跳过 {Skipped} 笔，重复 {Duplicated} 笔";
    }

    /// <summary>
    /// CSV / 账单导入。认识三种格式：支付宝账单、微信支付账单、普通表格导出的 CSV。
    /// 编码自动识别（UTF-8 / 带 BOM / GBK），字段靠表头名匹配，不依赖固定列序。
    /// </summary>
    public static class CsvImporter
    {
        public static ImportResult ImportFile(string path, bool allowDuplicate = false, bool dryRun = false)
        {
            var res = new ImportResult { File = path };
            try
            {
                var text = ReadAllTextSmart(path);
                var rows = ParseCsv(text);
                if (rows.Count == 0) { res.Errors.Add("文件是空的，或者没能解析出任何一行。"); return res; }

                // 1) 找表头行
                int headerIdx = -1, amountCol = -1;
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    if (IsPreamble(r)) continue;
                    int ac = r.FindIndex(c => c != null && (c.Contains("金额") || c.Contains("amount", StringComparison.OrdinalIgnoreCase)));
                    if (ac >= 0 && r.Count >= 4) { headerIdx = i; amountCol = ac; break; }
                }
                if (headerIdx < 0) { res.Errors.Add("找不到表头行（需要至少有一列是「金额」）。"); return res; }

                var header = rows[headerIdx].Select(Norm).ToList();
                res.Format = Detect(header);

                // 先精确匹配，再退化成包含匹配。
                // 这里必须精确优先：支付宝表里有一列就叫「类型」（值是"即时到账交易"），
                // 微信表里有「交易类型」，如果用包含匹配，它们会把「收/支」列抢走，
                // 结果整份账单的收支方向全反过来。
                int Col(params string[] names)
                {
                    var want = names.Select(Norm).ToArray();
                    for (int i = 0; i < header.Count; i++)
                        foreach (var n in want)
                            if (header[i] == n) return i;
                    for (int i = 0; i < header.Count; i++)
                        foreach (var n in want)
                            if (header[i].Contains(n)) return i;
                    return -1;
                }

                int cTime = Col("交易创建时间", "交易时间", "付款时间", "记账日期", "日期", "时间", "date");
                int cAmount = amountCol;
                int cDir = Col("收/支", "收支类型", "收支", "资金流向", "收付款", "inout");
                int cMerchant = Col("交易对方", "交易对象", "商户名称", "商户", "对方", "商家", "payee");
                int cItem = Col("商品名称", "商品说明", "商品", "备注信息", "备注", "说明", "note");
                int cPay = Col("支付方式", "付款方式", "收付款方式", "账户", "account");
                int cStatus = Col("交易状态", "当前状态", "状态", "status");
                int cCat = Col("交易分类", "分类", "category");

                // 2) 逐行转换
                var accs = Store.Data.accounts.Select(a => a.name).ToList();
                if (accs.Count == 0) accs.Add("现金");

                for (int i = headerIdx + 1; i < rows.Count; i++)
                {
                    var r = rows[i];
                    if (r.Count == 0 || r.All(string.IsNullOrWhiteSpace)) continue;
                    if (r[0] != null && (r[0].StartsWith("---") || r[0].Contains("列表"))) continue;

                    string Get(int idx) => idx >= 0 && idx < r.Count ? (r[idx] ?? "").Trim() : "";

                    string dateRaw = Get(cTime);
                    string date = Util.ParseDate(dateRaw);
                    if (date == null)
                    {
                        // 有些导出把日期和金额都塞在一列，再试一次
                        date = Util.ParseDate(Get(0));
                        if (date == null)
                        {
                            if (!string.IsNullOrWhiteSpace(dateRaw))
                            {
                                res.Skipped++;
                                AddReason(res, $"日期无法解析：{Trunc(dateRaw)}");
                            }
                            continue;
                        }
                    }

                    long amount = Util.ToCents(Get(cAmount));
                    if (amount == 0)
                    {
                        res.Skipped++;
                        AddReason(res, "金额是 0 或读不出来");
                        continue;
                    }
                    amount = Math.Abs(amount);

                    // 收支方向
                    string dirRaw = Get(cDir);
                    string dir = dirRaw.Replace(" ", "");
                    bool isIncome;
                    if (dir.Contains("不计")) { res.Skipped++; AddReason(res, "收/支为「不计收支」，不算进收支统计"); continue; }
                    if (dir.Contains("收入") || dir.Contains("入账") || dir.Contains("收款") || dir.Contains("已收")) isIncome = true;
                    else if (dir.Contains("支出") || dir.Contains("出账") || dir.Contains("付款") || dir.Contains("已支")) isIncome = false;
                    else if (dir == "/" || dir == "\\" || dir == "-" || dir.Length == 0)
                    {
                        // 账单没给方向，只能看金额本身有没有负号；看不出来就按支出算
                        isIncome = Get(cAmount).TrimStart().StartsWith("-") ? false : false;
                    }
                    else
                    {
                        // 方向写着但从没见过（比如某些银行写「借」「贷」）
                        if (dir.Contains("贷") || dir.Contains("存")) isIncome = true;
                        else isIncome = false;
                        AddReason(res, $"「{Trunc(dirRaw)}」这个收支写法没见过，先按支出算");
                    }

                    // 状态过滤：退款/关闭的不要
                    string status = Get(cStatus);
                    if (!IsSuccessStatus(status))
                    {
                        res.Skipped++;
                        AddReason(res, $"状态是「{Trunc(status)}」，属于退款或未成功");
                        continue;
                    }

                    // 账户：优先匹配已有账户名
                    string pay = Get(cPay);
                    string account = accs.FirstOrDefault(a => !string.IsNullOrEmpty(pay) && pay.Contains(a)) ?? "现金";

                    string merchant = Get(cMerchant);
                    string note = Get(cItem);

                    var t = new Transaction
                    {
                        type = isIncome ? "income" : "expense",
                        amount = amount,
                        date = date,
                        time = Util.ParseTime(dateRaw.Length > 10 ? dateRaw : ""),
                        merchant = merchant,
                        note = note,
                        account = account,
                        source = "import",
                        // 导进来的账记在当前账本名下（多人共用一个程序：谁导入就算谁的）
                        ledgerId = Store.CurrentLedgerId
                    };

                    // 分类：账单自带分类优先，其次走规则
                    string rawCat = Get(cCat);
                    if (!string.IsNullOrWhiteSpace(rawCat) && rawCat != "/")
                        t.category = Categories.Normalize(rawCat, t.type);
                    else
                    {
                        Store.ApplyRules(t);
                        if (string.IsNullOrEmpty(t.category) || t.source != "auto")
                        {
                            var probe = new Transaction { merchant = merchant, note = note, type = t.type, account = account };
                            var rule = Store.MatchRule(probe);
                            if (rule != null && !string.IsNullOrEmpty(rule.setCategory))
                            {
                                t.category = rule.setCategory;
                                t.subcategory = rule.setSubcategory;
                                t.source = "auto";
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(t.category))
                    {
                        // 规则没命中也不要马上认输：账单的商品名常常直接写着
                        //「餐饮消费」「外卖订单」「话费充值」这种词，拿它当分类线索。
                        var guess = Categories.Normalize(merchant + " " + note, t.type);
                        if (guess != "其他支出" && guess != "其他收入")
                        {
                            t.category = guess;
                            t.source = "auto";
                        }
                        else
                        {
                            t.category = t.type == "income" ? "其他收入" : "其他支出";
                        }
                    }

                    t.hash = Util.HashOf(t);

                    // 判重只在当前账本里比：别人账本里有一笔长得一样的，不该挡住我导入
                    if (Store.IsDuplicate(t))
                    {
                        res.Duplicated++;
                        if (!allowDuplicate) continue;
                    }

                    res.Total++;
                    if (!dryRun)
                    {
                        if (!allowDuplicate && Store.IsDuplicate(t)) { res.Duplicated++; continue; }
                        Store.Data.transactions.Add(t);
                        res.Added++;
                        if (t.type == "income") res.InSum += t.amount; else res.OutSum += t.amount;
                    }
                    else res.Added++;
                }

                if (!dryRun && res.Added > 0) { Store.Save(); Store.RaiseChanged(); }
            }
            catch (Exception ex)
            {
                res.Errors.Add("读取失败：" + ex.Message);
            }
            return res;
        }

        // ==================== 文件夹批量导入 ====================

        public static List<ImportResult> ImportFolder(string dir, bool allowDuplicate = false)
        {
            var list = new List<ImportResult>();
            var files = Directory.GetFiles(dir, "*.csv", SearchOption.TopDirectoryOnly)
                                 .Concat(Directory.GetFiles(dir, "*.CSV", SearchOption.TopDirectoryOnly))
                                 .Distinct().OrderBy(f => f).ToList();
            if (files.Count == 0)
            {
                list.Add(new ImportResult { File = dir, Errors = { "这个文件夹里没有 csv 文件。" } });
                return list;
            }
            foreach (var f in files) list.Add(ImportFile(f, allowDuplicate));
            Store.Save();
            Store.RaiseChanged();
            return list;
        }

        // ==================== 工具 ====================

        private static void AddReason(ImportResult res, string reason)
        {
            if (res.SkipReasons.Count < 40 && !res.SkipReasons.Contains(reason))
                res.SkipReasons.Add(reason);
        }

        private static string Trunc(string s) => s == null ? "" : (s.Length > 24 ? s.Substring(0, 24) + "…" : s);

        private static string Norm(string s)
            => (s ?? "").Trim().Trim('\uFEFF').Replace(" ", "").Replace("\t", "").ToLowerInvariant();

        /// <summary>账单文件前面那一堆说明行（账号、起止日期、导出类型…）不是表头</summary>
        private static bool IsPreamble(List<string> cells)
        {
            if (cells.Count == 0) return true;
            var joined = string.Join("", cells.Where(c => c != null)).Trim();
            if (joined.Length == 0) return true;
            if (joined.StartsWith("---")) return true;
            var keys = new[] { "起始日期", "终止日期", "起始时间", "终止时间", "微信昵称", "导出类型",
                               "账号", "电子客户回单", "支付宝交易记录", "微信支付账单" };
            if (keys.Any(k => joined.Contains(k))) return true;
            // 只有一列内容、且没有「金额」的，基本都是说明行
            if (cells.Count <= 2 && !joined.Contains("金额")) return true;
            return false;
        }

        private static string Detect(List<string> header)
        {
            var h = string.Join("|", header);
            if (h.Contains("交易创建时间") || h.Contains("支付宝")) return "alipay";
            if (h.Contains("微信昵称") || h.Contains("当前状态") || h.Contains("交易单号")) return "wechat";
            return "generic";
        }

        /// <summary>只有明确成功的才导入；退款、关闭、失败一律跳过</summary>
        private static bool IsSuccessStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return true;
            var s = status.Replace(" ", "");
            if (s == "/" || s == "\\" || s == "-") return true;

            var bad = new[] { "退款", "已关闭", "交易关闭", "失败", "已撤销", "已取消", "未支付", "等待付款",
                              "对方已退还", "已退还", "冲正" };
            if (bad.Any(b => s.Contains(b))) return false;

            var good = new[] { "支付成功", "交易成功", "已收钱", "已转账", "已存入", "已完成", "成功", "已到账", "收入" };
            return good.Any(g => s.Contains(g));
        }

        // ==================== 编码识别 ====================

        public static string ReadAllTextSmart(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return "";
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            // 先按严格 UTF-8 试，失败说明多半是 GBK
            try
            {
                var strict = new UTF8Encoding(false, true);
                return strict.GetString(bytes);
            }
            catch
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return Encoding.GetEncoding(936).GetString(bytes);   // GBK
                }
                catch
                {
                    return Encoding.UTF8.GetString(bytes).Replace("\uFFFD", "");
                }
            }
        }

        /// <summary>把一段文本按 CSV 规则切成行和列（支持引号包裹、引号内逗号/换行）</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else cell.Append(c);
                }
                else
                {
                    if (c == '"') inQuote = true;
                    else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                    else if (c == '\t' && row.Count == 0 && !text.Contains(',')) { row.Add(cell.ToString()); cell.Clear(); }
                    else if (c == '\r') { /* 忽略，等 \n */ }
                    else if (c == '\n')
                    {
                        row.Add(cell.ToString()); cell.Clear();
                        rows.Add(row); row = new List<string>();
                    }
                    else cell.Append(c);
                }
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>导入前先看一眼能认成什么样（不写库）</summary>
        public static ImportResult Preview(string path) => ImportFile(path, false, true);
    }
}