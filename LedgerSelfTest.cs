using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoneyTracker
{
    /// <summary>
    /// 多人账本自检（开发用）：MoneyTracker.exe --ledgertest [输出目录]
    ///
    /// 这一段是「多人账本 + 切换 + 盈亏比较」的验收脚本，干两件事：
    ///   1. 真的建账本、真的记几笔、真的来回切、真的删账本，然后对着手算的数字核；
    ///   2. 把界面离屏画成 PNG（明细页在两个账本下的样子、报表页的对比卡、账本管理窗口），
    ///      深色主题也画一份，用来确认没有白块。
    ///
    /// 跑完会把 db.json / db.json.bak 还原成跑之前的样子，不动用户的日常数据。
    /// </summary>
    public static class LedgerSelfTest
    {
        private static StringBuilder _log;
        private static int _pass, _fail;

        private static void Check(string name, bool ok, string detail = "")
        {
            if (ok) _pass++; else _fail++;
            _log.AppendLine((ok ? "[通过] " : "[失败] ") + name + (detail.Length > 0 ? "  —— " + detail : ""));
        }

        public static void Run(string outDir)
        {
            _log = new StringBuilder();
            _pass = _fail = 0;
            Directory.CreateDirectory(outDir);

            _log.AppendLine("=== 多人账本自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            _log.AppendLine("数据目录: " + Store.Dir);
            _log.AppendLine("输出目录: " + Path.GetFullPath(outDir));

            // 0) 先记住跑之前的样子（db.json 和它的备份都要救回来）
            string keepDb = null, keepBak = null;
            try { if (File.Exists(Store.FilePath)) keepDb = File.ReadAllText(Store.FilePath); } catch { }
            try { if (File.Exists(Store.BackupPath)) keepBak = File.ReadAllText(Store.BackupPath); } catch { }

            try
            {
                BuildWorld();
                DataChecks();
                DeleteChecks();
                LegacyChecks();
                UiChecks(outDir);
            }
            catch (Exception ex)
            {
                _fail++;
                _log.AppendLine("[失败] 自检本身抛异常了 —— " + ex.Message);
                _log.AppendLine(ex.StackTrace);
            }

            // 收尾：把数据还原成跑之前的样子
            // keepDb 为空 = 跑之前压根没有 db.json（全新解压出来还没记过账，
            // 或者把 exe 放在空目录里跑自检）。这种情况必须把自检造出来的那份删掉，
            // 不然用户跑完自检打开程序，会看见几条「自检-」开头的假数据。
            // 下面 .bak 那两行本来就是这个写法，上面这行原先漏了对称处理。
            try
            {
                if (keepDb != null) File.WriteAllText(Store.FilePath, keepDb);
                else if (File.Exists(Store.FilePath)) File.Delete(Store.FilePath);
            }
            catch { }
            try
            {
                if (keepBak != null) File.WriteAllText(Store.BackupPath, keepBak);
                else if (File.Exists(Store.BackupPath)) File.Delete(Store.BackupPath);
            }
            catch { }
            Store.Load();
            _log.AppendLine("---");
            _log.AppendLine("收尾：db.json 已还原（记录数 " + Store.Data.transactions.Count + "，账本数 " + Store.Ledgers.Count + "）");
            _log.AppendLine($"结果：通过 {_pass} 项，失败 {_fail} 项");
            _log.AppendLine(_fail == 0 ? "自检全部通过。" : "有失败项，看上面带 [失败] 的行。");

            File.WriteAllText(Path.Combine(outDir, "ledger-selftest-log.txt"), _log.ToString(), new UTF8Encoding(false));
            Console.WriteLine(_log.ToString());
        }

        // ==================== 造一个干净的世界 ====================

        private static string LA => "ledger-A";
        private static string LB => "ledger-B";
        private static string LC => "ledger-C";

        private static void BuildWorld()
        {
            var data = Store.CreateDefault();
            data.transactions.Clear();
            data.budgets.Clear();
            data.ledgers.Clear();
            data.ledgers.Add(new Ledger { id = LA, name = "我", note = "主账本", color = "#2F6FED" });
            data.ledgers.Add(new Ledger { id = LB, name = "老婆", note = "她的私房账" });   // 故意不设颜色
            data.settings.currentLedger = LA;
            Store.ReplaceAll(data);

            _log.AppendLine("");
            _log.AppendLine("---- 一、账本与切换 ----");
            Check("起手有两个账本", Store.Ledgers.Count == 2, Store.Ledgers.Count.ToString());
            Check("当前账本是「我」", Store.CurrentLedgerId == LA, Store.CurrentLedgerId);

            // 甲：三笔（ledgerId 故意留空，验证新记录会落到当前账本）
            Add(LA, "2026-09-02", "expense", 12345, "餐饮", "早餐");
            Add(LA, "2026-09-10", "income", 800000, "工资", "九月工资");
            Add(LA, "2026-09-20", "expense", 5000, "交通", "地铁");
            Add(LA, "2026-08-15", "expense", 100000, "居住", "八月房租");     // 放到别的月份，用来检验区间

            // 乙：两笔
            Add(LB, "2026-09-05", "expense", 30000, "购物", "买菜");
            Add(LB, "2026-09-15", "income", 200000, "兼职", "接了个活");

            Check("新记的账自动落到当前账本（甲 4 笔）", Store.LedgerCount(LA) == 4, Store.LedgerCount(LA).ToString());
            Check("乙 2 笔", Store.LedgerCount(LB) == 2, Store.LedgerCount(LB).ToString());

            // 切到乙再切回甲，看数据有没有被动过
            var beforeA = Ids(LA);
            Store.SetCurrentLedger(LB);
            Check("切换成功（当前＝老婆）", Store.CurrentLedgerId == LB, Store.CurrentLedgerId);
            Check("切到乙时，明细里只有乙的 2 笔", Store.Scoped().Count() == 2 && Store.Scoped().All(t => t.ledgerId == LB));
            Store.SetCurrentLedger(LA);
            Check("切回甲，甲的记录一条不多一条不少", Ids(LA).SequenceEqual(beforeA), Ids(LA).Count + " 笔");
            Check("切回甲，界面上的明细也只有甲", Store.Scoped().Count() == 4 && Store.Scoped().All(t => t.ledgerId == LA));
        }

        private static void Add(string ledgerId, string date, string type, long cents, string cat, string note)
        {
            Store.SetCurrentLedger(ledgerId);
            var t = new Transaction
            {
                type = type, amount = cents, date = date, time = "12:00", category = cat,
                account = "现金", merchant = note, note = note, source = "manual"
                // ledgerId 故意不填：靠 Store.AddTx 的兜底落到当前账本
            };
            t.hash = Util.HashOf(t);
            Store.AddTx(t, true);
        }

        private static List<string> Ids(string ledgerId)
            => Store.QueryScoped(ledgerId).Select(t => t.id).OrderBy(x => x).ToList();

        // ==================== 二、数字对不对 ====================

        private static void DataChecks()
        {
            _log.AppendLine("");
            _log.AppendLine("---- 二、盈亏数字与区间 ----");

            var all = Store.LedgerStats();
            var a = all.FirstOrDefault(s => s.id == LA);
            var b = all.FirstOrDefault(s => s.id == LB);

            Check("对比表里两个账本都在", a != null && b != null, all.Count + " 本");

            // 甲：收入 8000.00，支出 123.45+50.00+1000.00
            long inA = 800000, outA = 12345 + 5000 + 100000;
            Check("甲的收入对得上", a != null && a.income == inA, a == null ? "—" : a.income.ToString());
            Check("甲的支出对得上", a != null && a.expense == outA, a == null ? "—" : a.expense.ToString());
            Check("甲的盈亏 = 收入 − 支出", a != null && a.net == inA - outA, a == null ? "—" : a.net.ToString());

            // 乙：收入 2000.00，支出 300.00
            Check("乙的盈亏对得上", b != null && b.net == 200000 - 30000, b == null ? "—" : b.net.ToString());

            // 区间：只看 9 月，甲那笔 8 月房租不该算进来
            var sep = Store.LedgerStats("2026-09-01", "2026-09-30");
            var aSep = sep.FirstOrDefault(s => s.id == LA);
            Check("按统计区间过滤：9 月的甲不含 8 月那笔",
                aSep != null && aSep.expense == 12345 + 5000 && aSep.count == 3,
                aSep == null ? "—" : aSep.expense + " / " + aSep.count + " 笔");

            var aug = Store.LedgerStats("2026-08-01", "2026-08-31");
            var aAug = aug.FirstOrDefault(s => s.id == LA);
            Check("8 月区间里甲只有那一笔房租", aAug != null && aAug.expense == 100000 && aAug.count == 1,
                aAug == null ? "—" : aAug.count + " 笔");

            // 横向比：各账本盈亏之和 = 全库盈亏（不能多算也不能漏算）
            long sumNet = Store.LedgerStats().Sum(s => s.net);
            var every = Store.Query().ToList();
            long expectNet = every.Where(t => t.type == "income").Sum(t => t.amount)
                          - every.Where(t => t.type == "expense").Sum(t => t.amount);
            Check("各账本盈亏加起来 = 全库盈亏", sumNet == expectNet, sumNet + " vs " + expectNet);

            // 转账不算收支
            Store.SetCurrentLedger(LA);
            var tr = new Transaction
            {
                type = "transfer", amount = 999999, date = "2026-09-21", time = "09:00",
                category = "转账", account = "现金", toAccount = "微信", merchant = "转自己"
            };
            Store.AddTx(tr, true);
            var aTr = Store.LedgerStats().FirstOrDefault(s => s.id == LA);
            Check("转账不影响盈亏", aTr != null && aTr.net == inA - outA, aTr == null ? "—" : aTr.net.ToString());
            Store.DeleteTx(tr.id);
            Check("删掉那笔转账后回到原样", Store.LedgerStats().FirstOrDefault(s => s.id == LA).net == inA - outA);

            // 两台账本里长得一模一样的账，谁都不该被去重规则吃掉
            _log.AppendLine("");
            _log.AppendLine("---- 三、两个账本里各记一笔一样的账 ----");
            Add(LA, "2026-09-25", "expense", 2500, "餐饮", "同款奶茶");
            Add(LB, "2026-09-25", "expense", 2500, "餐饮", "同款奶茶");
            Check("同一笔账两个账本各留一条", Store.LedgerCount(LA) == 5 && Store.LedgerCount(LB) == 3,
                Store.LedgerCount(LA) + " / " + Store.LedgerCount(LB));
            int totalBefore = Store.Data.transactions.Count;
            Store.Save();
            Store.Load();   // 重新读一遍：Normalize 里的去重会跑
            Check("重新载入后两条都还在（去重没有跨账本误删）", Store.Data.transactions.Count == totalBefore,
                Store.Data.transactions.Count + " vs " + totalBefore);

            // 改一笔时账本不能丢（EditWindow 的克隆不带 ledgerId，靠 Store 兜住）
            var one = Store.QueryScoped(LA).First();
            var copy = new Transaction
            {
                id = one.id, type = one.type, amount = one.amount + 100, date = one.date,
                category = one.category, account = one.account, note = "改过的"
            };
            bool upd = Store.UpdateTx(copy);
            var after = Store.Data.transactions.FirstOrDefault(x => x.id == one.id);
            Check("改一笔以后，它还留在原来那个账本里", upd && after != null && after.ledgerId == LA,
                after == null ? "记录没了" : after.ledgerId);
        }

        // ==================== 四、删账本的边界 ====================

        private static void DeleteChecks()
        {
            _log.AppendLine("");
            _log.AppendLine("---- 四、删账本 ----");

            // 1) 有记录、又没指定接手账本 → 必须拒绝，并且什么都不许动
            int total = Store.Data.transactions.Count;
            int cntB = Store.LedgerCount(LB);
            bool ok = Store.DeleteLedger(LB);
            Check("有记录的账本，不指定接手账本删不掉", !ok, Store.LastError ?? "");
            Check("被拒绝以后，账本还在", Store.FindLedger(LB) != null);
            Check("被拒绝以后，记录数一点没变", Store.Data.transactions.Count == total, Store.Data.transactions.Count + " vs " + total);
            Check("被拒绝以后，乙的记录还在乙名下", Store.LedgerCount(LB) == cntB, Store.LedgerCount(LB) + " vs " + cntB);

            // 2) 不能拿自己当接手账本
            ok = Store.DeleteLedger(LB, LB);
            Check("接手账本不能是自己", !ok && Store.FindLedger(LB) != null, Store.LastError ?? "");

            // 3) 指定接手账本 → 记录全转过去，一条都不能少
            Store.SetCurrentLedger(LB);       // 顺便测「删掉的正好是当前账本」
            ok = Store.DeleteLedger(LB, LA);
            Check("指定接手账本后，乙删掉了", ok, Store.LastError ?? "");
            Check("账本列表里没有乙了", Store.FindLedger(LB) == null);
            Check("全库记录数守恒（没有凭空消失）", Store.Data.transactions.Count == total,
                Store.Data.transactions.Count + " vs " + total);
            Check("原来乙的记录现在都归甲", Store.LedgerCount(LA) == total, Store.LedgerCount(LA) + " vs " + total);
            Check("删掉的正好是当前账本时，当前账本自动换成接手的那个",
                Store.CurrentLedgerId == LA && Store.FindLedger(Store.CurrentLedgerId) != null, Store.CurrentLedgerId);

            // 4) 空账本可以直接删
            var c = Store.AddLedger("空账本", null, "试一试");
            Check("新建的账本是空的", Store.LedgerCount(c.id) == 0);
            Check("空账本可以直接删", Store.DeleteLedger(c.id) && Store.FindLedger(c.id) == null, Store.LastError ?? "");

            // 5) 最后一个账本删不掉
            while (Store.Ledgers.Count > 1) Store.DeleteLedger(Store.Ledgers[1].id, Store.Ledgers[0].id);
            Check("只剩一个账本时，它删不掉", !Store.DeleteLedger(Store.CurrentLedgerId), Store.LastError ?? "");
            Check("删不掉以后记录还在", Store.Data.transactions.Count == total, Store.Data.transactions.Count.ToString());

            // 6) 名册被清空过也不会崩：Normalize 会补一个默认账本
            Store.Data.ledgers.Clear();
            Store.Save();
            Store.Load();
            Check("账本名册空了以后，重新载入会自动补一个", Store.Ledgers.Count == 1, Store.Ledgers.Count.ToString());
            Check("自动补出来的账本收得住老记录", Store.Scoped().Count() == Store.Data.transactions.Count,
                Store.Scoped().Count() + " vs " + Store.Data.transactions.Count);
        }

        // ==================== 五、老数据（没有账本字段） ====================

        private static void LegacyChecks()
        {
            _log.AppendLine("");
            _log.AppendLine("---- 五、老数据兼容（没有 ledgers / ledgerId 的库）----");

            string old = @"{
  ""format"": ""money-tracker"",
  ""version"": 1,
  ""transactions"": [
    { ""id"": ""old-1"", ""type"": ""expense"", ""amount"": 1500, ""date"": ""2026-05-01"", ""time"": ""08:00"", ""category"": ""餐饮"", ""account"": ""现金"", ""merchant"": ""老数据一"", ""hash"": ""h-old-1"" },
    { ""id"": ""old-2"", ""type"": ""income"", ""amount"": 60000, ""date"": ""2026-05-02"", ""time"": ""09:00"", ""category"": ""工资"", ""account"": ""现金"", ""merchant"": ""老数据二"", ""hash"": ""h-old-2"" },
    { ""id"": ""old-3"", ""type"": ""expense"", ""amount"": 800, ""date"": ""2026-05-03"", ""time"": ""10:00"", ""category"": ""交通"", ""account"": ""现金"", ""merchant"": ""老数据三"", ""hash"": ""h-old-3"" }
  ],
  ""accounts"": [ { ""id"": ""acc-1"", ""name"": ""现金"", ""kind"": ""cash"" } ],
  ""budgets"": [],
  ""rules"": [],
  ""settings"": { ""theme"": ""light"" }
}";
            File.WriteAllText(Store.FilePath, old, new UTF8Encoding(false));
            Store.Load();

            Check("老库载入后补出了一个默认账本", Store.Ledgers.Count == 1, Store.Ledgers.Count + " 个");
            Check("老库的 currentLedger 指向真实存在的账本",
                Store.FindLedger(Store.CurrentLedgerId) != null, Store.CurrentLedgerId);
            Check("三笔老记录一条都没丢，全都归到第一个账本",
                Store.Data.transactions.Count == 3 && Store.Data.transactions.All(t => t.ledgerId == Store.Ledgers[0].id),
                Store.Data.transactions.Count + " 笔");
            Check("当前账本的明细就把这三笔都显示出来", Store.Scoped().Count() == 3, Store.Scoped().Count() + " 笔");
        }

        // ==================== 六、界面：数字 + 截图 ====================

        private static void UiChecks(string outDir)
        {
            _log.AppendLine("");
            _log.AppendLine("---- 六、界面 ----");

            // 重新造一份好看的、跨几个月的数据，专门用来出图
            Scene();

            string idA = Store.Ledgers[0].id;
            string idB = Store.Ledgers[1].id;

            // 明细页：两个账本下条数必须不一样，而且各自对得上
            Store.SetCurrentLedger(idA);
            var pageA = new ListPage();
            int showA = pageA.grid.Items.Count;
            int wantA = Store.QueryScoped(month: Util.ThisMonth()).Count();
            Check("明细页（甲）显示的条数 = 这个账本本月的笔数", showA == wantA, showA + " vs " + wantA);

            Store.SetCurrentLedger(idB);
            var pageB = new ListPage();
            int showB = pageB.grid.Items.Count;
            int wantB = Store.QueryScoped(month: Util.ThisMonth()).Count();
            Check("明细页（乙）显示的条数 = 这个账本本月的笔数", showB == wantB, showB + " vs " + wantB);

            long netA = Store.LedgerSum(idA).income - Store.LedgerSum(idA).expense;
            long netB = Store.LedgerSum(idB).income - Store.LedgerSum(idB).expense;
            Check("两个账本的盈亏不一样（确实在分开算）", netA != netB, netA + " vs " + netB);

            // 报表页：结余要跟着账本走，对比卡里要把所有账本都列出来
            Store.SetCurrentLedger(idA);
            var repA = new ReportView();
            string textA = repA.txtNet.Text;
            int rowsA = repA.listCompare.Items.Count;

            Store.SetCurrentLedger(idB);
            var repB = new ReportView();
            string textB = repB.txtNet.Text;

            Check("报表页的结余跟着账本变", textA != textB, "甲=" + textA + " 乙=" + textB);
            Check("报表页（甲）的结余 = 甲账本的手算数字", textA == Util.Money(netA), textA + " vs " + Util.Money(netA));
            Check("报表页（乙）的结余 = 乙账本的手算数字", textB == Util.Money(netB), textB + " vs " + Util.Money(netB));
            Check("盈亏对比卡把每个账本都列了一行", rowsA == Store.Ledgers.Count, rowsA + " 行 / " + Store.Ledgers.Count + " 本");

            // 截图 + 界面级验证：侧边栏切换器、切换后当前页立刻刷新
            Store.SetCurrentLedger(idA);
            var winA = ThemeShot("list", idA, "light");
            SidebarChecks(winA, idA, "甲");

            // 真的走一遍「用鼠标点侧边栏那一行」的代码路径（不是直接调 Store）
            int rowsBefore = PageRows(winA);
            ClickRow(winA, idB);
            int rowsAfter = PageRows(winA);
            Check("点侧边栏那一行能切账本（走的是按钮点击那条路）", Store.CurrentLedgerId == idB, Store.CurrentLedgerId);
            Check("切换账本后，同一个窗口里的明细页立刻跟着刷新（条数变了）", rowsBefore != rowsAfter,
                rowsBefore + " -> " + rowsAfter);
            Check("刷新后的条数 = 乙这个账本本月的笔数",
                rowsAfter == Store.QueryScoped(month: Util.ThisMonth()).Count(),
                rowsAfter + " vs " + Store.QueryScoped(month: Util.ThisMonth()).Count());
            SidebarChecks(winA, idB, "切到乙之后");
            Shot(winA, Path.Combine(outDir, "ledger-list-lb-light.png"), 1240, 830);

            ClickRow(winA, idA);
            Check("再点一下能切回来", Store.CurrentLedgerId == idA, Store.CurrentLedgerId);
            Check("切回来以后明细条数回到原来的数", PageRows(winA) == rowsBefore, PageRows(winA) + " vs " + rowsBefore);
            SidebarChecks(winA, idA, "切回甲之后");
            Shot(winA, Path.Combine(outDir, "ledger-list-la-light.png"), 1240, 830);

            var winRep = ThemeShot("report", idA, "light");
            Shot(winRep, Path.Combine(outDir, "ledger-report-la-light.png"), 1240, 1080);

            var winRepDark = ThemeShot("report", idB, "dark");
            Shot(winRepDark, Path.Combine(outDir, "ledger-report-lb-dark.png"), 1240, 1080);

            var winRec = ThemeShot("record", idA, "light");
            Shot(winRec, Path.Combine(outDir, "ledger-record-la-light.png"), 1240, 830);

            ShotLedgerWindow(Path.Combine(outDir, "ledger-window-light.png"), "light");
            ShotLedgerWindow(Path.Combine(outDir, "ledger-window-dark.png"), "dark");

            foreach (var f in new[] { "ledger-list-la-light.png", "ledger-list-lb-light.png", "ledger-report-la-light.png",
                                      "ledger-report-lb-dark.png", "ledger-window-light.png", "ledger-window-dark.png" })
            {
                var p = Path.Combine(outDir, f);
                Check("出了图：" + f, File.Exists(p) && new FileInfo(p).Length > 5000,
                    File.Exists(p) ? Store.SizeText(new FileInfo(p).Length) : "没生成");
            }
        }

        /// <summary>模拟鼠标点侧边栏里的某个账本行（走真正的按钮点击事件）</summary>
        private static void ClickRow(MainWindow win, string ledgerId)
        {
            var row = win.listLedgers.Children.OfType<Button>().FirstOrDefault(r => (r.Tag as string) == ledgerId);
            row?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, row));
        }

        /// <summary>明细页当前显示了多少行</summary>
        private static int PageRows(MainWindow win)
            => win.Host.Content is ListPage lp ? lp.grid.Items.Count : -1;

        private static string RowText(Button row, int column)
            => ((row?.Content as Grid)?.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == column))?.Text ?? "";

        /// <summary>侧边栏账本切换器：每个账本一行，当前那行打勾，数字要和真数据对得上</summary>
        private static void SidebarChecks(MainWindow win, string ledgerId, string label)
        {
            var rows = win.listLedgers.Children.OfType<Button>().ToList();
            Check($"侧边栏（{label}）把每个账本都列出来了", rows.Count == Store.Ledgers.Count,
                rows.Count + " 行 / " + Store.Ledgers.Count + " 本");

            var cur = rows.FirstOrDefault(r => (r.Tag as string) == ledgerId);
            Check($"侧边栏（{label}）当前账本那一行打了勾",
                cur != null && RowText(cur, 2) == "✓ " + Store.LedgerCount(ledgerId),
                cur == null ? "没找到这一行" : RowText(cur, 2));

            Check($"侧边栏（{label}）只有一个账本被标成当前",
                rows.Count(r => RowText(r, 2).StartsWith("✓")) == 1,
                rows.Count(r => RowText(r, 2).StartsWith("✓")) + " 行带勾");

            Check($"侧边栏（{label}）显示的名字就是账本真名",
                cur != null && RowText(cur, 1).StartsWith(Store.LedgerName(ledgerId)),
                cur == null ? "没找到" : RowText(cur, 1));
        }

        /// <summary>造一份跨几个月的像样数据：两个人各记各的，数字差异明显，便于肉眼核对</summary>
        private static void Scene()
        {
            var data = Store.CreateDefault();
            data.transactions.Clear();
            data.budgets.Clear();
            data.ledgers.Clear();
            data.ledgers.Add(new Ledger { id = LA, name = "我", note = "两个人的共同开销都记这儿", color = "#2F6FED" });
            data.ledgers.Add(new Ledger { id = LB, name = "老婆", note = "她自己的零花", color = "#E0568F" });
            data.ledgers.Add(new Ledger { id = LC, name = "小店", note = "副业流水", color = "#26A269", archived = false });
            data.settings.currentLedger = LA;
            Store.ReplaceAll(data);

            var rnd = new Random(20261101);
            var today = DateTime.Today;
            var food = new[] { "美团外卖", "楼下快餐", "瑞幸咖啡", "沙县小吃", "星巴克" };
            var shop = new[] { "淘宝", "京东", "永辉超市", "名创优品" };

            for (int m = 3; m >= 0; m--)
            {
                var month = today.AddMonths(-m);
                int maxDay = m == 0 ? today.Day : DateTime.DaysInMonth(month.Year, month.Month);
                string D(int d) => new DateTime(month.Year, month.Month, Math.Min(d, maxDay)).ToString("yyyy-MM-dd");

                // 我：工资 + 房租 + 一堆小开销
                if (maxDay >= 10) AddAt(LA, D(10), "income", 1200000 + rnd.Next(-30000, 50000), "工资", "月薪", "招商银行", "某某公司");
                AddAt(LA, D(1), "expense", 320000, "居住", "房租房贷", "招商银行", "房东");
                for (int i = 0; i < 16; i++)
                    AddAt(LA, D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(1200, 6000), "餐饮", "外卖", "微信", food[rnd.Next(food.Length)]);
                for (int i = 0; i < 6; i++)
                    AddAt(LA, D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(300, 4000), "交通", "打车", "支付宝", "滴滴出行");
                for (int i = 0; i < 4; i++)
                    AddAt(LA, D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(3000, 20000), "购物", "日用品", "信用卡", shop[rnd.Next(shop.Length)]);

                // 老婆：家用 + 一点收入
                AddAt(LB, D(8), "income", 700000, "工资", "月薪", "招商银行", "某某公司");
                for (int i = 0; i < 10; i++)
                    AddAt(LB, D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(800, 5000), "餐饮", "聚餐", "支付宝", food[rnd.Next(food.Length)]);
                for (int i = 0; i < 3; i++)
                    AddAt(LB, D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(4000, 30000), "购物", "服饰", "信用卡", shop[rnd.Next(shop.Length)]);

                // 小店：只有收入和小额支出
                for (int i = 0; i < 5; i++)
                    AddAt(LC, D(rnd.Next(1, maxDay + 1)), "income", rnd.Next(20000, 90000), "兼职", "接单", "支付宝", "客户下单");
                for (int i = 0; i < 3; i++)
                    AddAt(LC, D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(1000, 8000), "其他支出", "", "支付宝", "进货");
            }
            Store.Save();
        }

        private static void AddAt(string ledgerId, string date, string type, long cents, string cat, string sub, string acc, string merchant)
        {
            var t = new Transaction
            {
                type = type, amount = cents, date = date, time = "12:00", category = cat, subcategory = sub,
                account = acc, merchant = merchant, note = "", source = "manual", ledgerId = ledgerId
            };
            t.hash = Util.HashOf(t) + ledgerId;   // 两个人各自的账不该互相判重
            Store.Data.transactions.Add(t);
        }

        // ==================== 出图 ====================

        private static MainWindow ThemeShot(string page, string ledgerId, string theme)
        {
            Store.SetCurrentLedger(ledgerId);
            Store.Data.settings.theme = theme;
            ThemeManager.Apply(theme, null);
            var win = new MainWindow();
            win.Init();
            win.Navigate(page);
            return win;
        }

        private static void Shot(MainWindow win, string path, int w, int h)
        {
            ThemeManager.Apply(Store.Data.settings.theme, Store.Data.settings.accent);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            win.RenderTo(path, w, h);
        }

        private static void ShotLedgerWindow(string path, string theme)
        {
            Store.Data.settings.theme = theme;
            ThemeManager.Apply(theme, null);
            var win = new LedgerWindow();
            win.Prepare();
            var root = (FrameworkElement)win.Content;
            // 窗口内容是带 20px 外边距的，画布要比内容大一圈才不会被切掉
            root.Width = 700;
            root.Height = 560;
            int w = 740, h = 600;
            for (int i = 0; i < 3; i++)
            {
                root.Measure(new Size(w, h));
                root.Arrange(new Rect(0, 0, w, h));
                root.UpdateLayout();
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var fs = File.Create(path);
            enc.Save(fs);
        }
    }
}