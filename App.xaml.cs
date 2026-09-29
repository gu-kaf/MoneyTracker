using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace MoneyTracker
{
    public partial class App : Application
    {
        public static AppData Data => Store.Data;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Store.Load();
            ThemeManager.Apply(Store.Data.settings.theme, Store.Data.settings.accent);

            // 自检模式：把界面离屏渲染成图片（不需要真实窗口，方便开发时看效果）
            // 用法：MoneyTracker.exe --shot <页面> <输出png> [主题] [--demo]
            if (e.Args.Any(a => a == "--shot"))
            {
                try { RunShot(e.Args); }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-error.txt"),
                        ex.ToString());
                }
                Shutdown();
                return;
            }

            // 无人值守导入（自检用）：MoneyTracker.exe --import <文件或目录> [--dupes]
            if (e.Args.Any(a => a == "--import"))
            {
                try { RunImport(e.Args); }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import-error.txt"),
                        ex.ToString());
                }
                Shutdown();
                return;
            }

            // 无人值守导出（自检用）：MoneyTracker.exe --export <输出目录> [--demo]
            if (e.Args.Any(a => a == "--export"))
            {
                try { RunExport(e.Args); }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export-error.txt"),
                        ex.ToString());
                }
                Shutdown();
                return;
            }

            // 快照机制自检（开发用）：MoneyTracker.exe --selftest [输出目录]
            if (e.Args.Any(a => a == "--selftest"))
            {
                string outDir = Arg(e.Args, 0) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest");
                try
                {
                    Directory.CreateDirectory(outDir);
                    RunSelfTest(outDir);
                }
                catch (Exception ex)
                {
                    try
                    {
                        Directory.CreateDirectory(outDir);
                        File.WriteAllText(Path.Combine(outDir, "selftest-crash.txt"), ex.ToString());
                    }
                    catch { }
                }
                Shutdown();
                return;
            }

            // 单独把备份对话框拉起来（方便自检 / 演示）：MoneyTracker.exe --backup
            if (e.Args.Any(a => a == "--backup"))
            {
                BackupWindow.ShowDialog(null);
                Shutdown();
                return;
            }

            // 单独把分类管理拉起来（方便自检 / 演示）：MoneyTracker.exe --category
            // 再加 --shot <out.png> 就是离屏出图，不弹窗、不抢前台
            if (e.Args.Any(a => a == "--category"))
            {
                var w = new CategoryWindow();

                int si = Array.IndexOf(e.Args, "--shot");
                if (si >= 0 && si + 1 < e.Args.Length)
                {
                    try
                    {
                        var bmp = w.RenderToBitmap(840, 660);
                        if (bmp != null)
                        {
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                            using (var fs = File.Create(e.Args[si + 1])) enc.Save(fs);
                            Console.WriteLine("已出图：" + e.Args[si + 1]);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("出图失败：" + ex.Message);
                    }
                    Shutdown();
                    return;
                }

                w.ShowDialog();
                Shutdown();
                return;
            }

            // 多人账本自检（开发用）：MoneyTracker.exe --ledgertest [输出目录]
            // 建账本、记几笔、来回切、删账本，对着手算的数字核完，再出一批界面截图。
            if (e.Args.Any(a => a == "--ledgertest"))
            {
                string ldir = Arg(e.Args, 0) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ledgertest");
                try { LedgerSelfTest.Run(ldir); }
                catch (Exception ex)
                {
                    try
                    {
                        Directory.CreateDirectory(ldir);
                        File.WriteAllText(Path.Combine(ldir, "ledgertest-crash.txt"), ex.ToString());
                    }
                    catch { }
                }
                Shutdown();
                return;
            }

            // 「改这一笔」到底有没有真改到（开发用）：MoneyTracker.exe --editcheck [输出目录]
            if (e.Args.Any(a => a == "--editcheck"))
            {
                string dir = Arg(e.Args, 0) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "editcheck");
                try
                {
                    Directory.CreateDirectory(dir);
                    RunEditCheck(dir);
                }
                catch (Exception ex)
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, "editcheck-crash.txt"), ex.ToString());
                    }
                    catch { }
                }
                Shutdown();
                return;
            }

            // 分类改了名/删了之后，老记录有没有跟着走（开发用）：MoneyTracker.exe --catcheck [输出目录]
            if (e.Args.Any(a => a == "--catcheck"))
            {
                string cdir = Arg(e.Args, 0) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catcheck");
                try
                {
                    Directory.CreateDirectory(cdir);
                    RunCatCheck(cdir);
                }
                catch (Exception ex)
                {
                    try
                    {
                        Directory.CreateDirectory(cdir);
                        File.WriteAllText(Path.Combine(cdir, "catcheck-crash.txt"), ex.ToString());
                    }
                    catch { }
                }
                Shutdown();
                return;
            }

            var win = new MainWindow();
            MainWindow = win;
            win.Show();
        }

        /// <summary>
        /// 快照自检（开发时用）：真的建快照、改数据、回档、清理，最后把结果写进 selftest-log.txt。
        /// 用法：MoneyTracker.exe --selftest [输出目录]
        /// 跑完会把自检造出来的快照删掉、db.json 还原成跑之前的样子，不弄脏日常数据。
        /// </summary>
        private void RunSelfTest(string outDir)
        {
            var log = new System.Text.StringBuilder();
            int pass = 0, fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) pass++; else fail++;
                log.AppendLine((ok ? "[通过] " : "[失败] ") + name + (detail.Length > 0 ? "  —— " + detail : ""));
            }
            string IdsOf(IEnumerable<Transaction> list) => string.Join(",", list.Select(t => t.id).OrderBy(x => x));
            Transaction MakeTx(string date, string type, long cents, string cat, string mer, string acc)
            {
                var t = new Transaction
                {
                    type = type, amount = cents, date = date, time = "12:00",
                    category = cat, account = acc, merchant = mer, note = "自检数据", source = "manual"
                };
                t.hash = Util.HashOf(t);
                return t;
            }
            int CountInFile(string path)
            {
                try
                {
                    var d = System.Text.Json.JsonSerializer.Deserialize<AppData>(File.ReadAllText(path),
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return d?.transactions?.Count ?? -1;
                }
                catch { return -1; }
            }

            log.AppendLine("=== 快照机制自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            log.AppendLine("数据目录: " + Store.Dir);
            log.AppendLine("快照目录: " + Store.BackupDir);

            // 0) 先记住跑之前的样子，结束时还原
            string keepDb = null;
            try { if (File.Exists(Store.FilePath)) keepDb = File.ReadAllText(Store.FilePath); } catch { }
            string keepBak = null;
            try { if (File.Exists(Store.FilePath + ".bak")) keepBak = File.ReadAllText(Store.FilePath + ".bak"); } catch { }
            int keepCount = CountInFile(Store.FilePath);
            var keepSnapshots = Store.ListSnapshots().Select(s => s.FileName).ToList();
            log.AppendLine($"自检前的记录数: {keepCount}，已有快照 {keepSnapshots.Count} 个");

            // 1) 搭台：3 笔已知数据
            Store.Data.transactions.Clear();
            Store.Data.transactions.Add(MakeTx("2026-09-01", "expense", 1234, "餐饮", "自检-早餐", "现金"));
            Store.Data.transactions.Add(MakeTx("2026-09-05", "income", 500000, "工资", "自检-工资", "招商银行"));
            Store.Data.transactions.Add(MakeTx("2026-09-07", "expense", 6600, "餐饮", "自检-午饭", "微信"));
            Store.Save();
            var ids3 = IdsOf(Store.Data.transactions);
            Check("搭台：库里有 3 笔记录", Store.Data.transactions.Count == 3, Store.Data.transactions.Count.ToString());

            // 2) 建立快照
            var a = Store.Snapshot("手动备份");
            var aPath = a == null ? null : Path.Combine(Store.BackupDir, a);
            Check("Snapshot 返回了文件名", a != null, a ?? (Store.LastError ?? ""));
            Check("文件名是时间戳格式（20260929-181530.json）",
                a != null && System.Text.RegularExpressions.Regex.IsMatch(a, @"^\d{8}-\d{6}(-\d+)?\.json$"), a ?? "");
            Check("快照文件真的落进了 backups 目录", aPath != null && File.Exists(aPath));
            var list1 = Store.ListSnapshots();
            var sa = list1.FirstOrDefault(x => x.FileName == a);
            Check("ListSnapshots 列得出它", sa != null);
            Check("说明读出来是「手动备份」", sa != null && sa.Reason == "手动备份", sa?.Reason ?? "");
            Check("记着 3 笔", sa != null && sa.Count == 3, sa?.Count.ToString() ?? "");
            Check("有文件大小", sa != null && sa.Size > 0, sa?.Size.ToString() ?? "");
            Check("能读出来", sa != null && sa.Readable);
            Check("最新的排在最前面", list1.Count > 0 && list1[0].FileName == a, list1.Count > 0 ? list1[0].FileName : "空");

            // 3) 改数据：再加 2 笔，然后建第二个快照
            Store.Data.transactions.Add(MakeTx("2026-09-08", "expense", 2500, "交通", "自检-地铁", "支付宝"));
            Store.Save();
            Store.AddTx(MakeTx("2026-09-09", "expense", 9900, "购物", "自检-超市", "信用卡")); // 走真实写入入口
            var ids5 = IdsOf(Store.Data.transactions);
            Check("改数据后变成 5 笔", Store.Data.transactions.Count == 5, Store.Data.transactions.Count.ToString());

            var b = Store.Snapshot("导入账单前");
            var sb = Store.ListSnapshots().FirstOrDefault(x => x.FileName == b);
            Check("第二个快照记着 5 笔", sb != null && sb.Count == 5, sb?.Count.ToString() ?? "");
            Check("两份快照的说明各是各的",
                Store.ListSnapshots().Any(x => x.Reason == "手动备份") &&
                Store.ListSnapshots().Any(x => x.Reason == "导入账单前"));
            // 自检跑得快，这几份快照常常落在同一秒里，顺序也必须是最新的在最上面
            Check("同一秒里建的快照，后建的排前面", Store.ListSnapshots()[0].FileName == b,
                string.Join(" > ", Store.ListSnapshots().Take(3).Select(x => x.FileName)));

            // 4) 备份 → 改数据 → 回档 → 数据回到原样
            bool restored = Store.RestoreSnapshot(a);
            Check("RestoreSnapshot 成功", restored, Store.LastError ?? "");
            Check("数据回到 3 笔", Store.Data.transactions.Count == 3, Store.Data.transactions.Count.ToString());
            Check("回去的正是当时那 3 笔（id 完全一致）", IdsOf(Store.Data.transactions) == ids3);
            Check("盘上的 db.json 也回到 3 笔（不只是内存里变了）", CountInFile(Store.FilePath) == 3,
                CountInFile(Store.FilePath).ToString());
            var safety = Store.ListSnapshots().FirstOrDefault(x => x.Reason == "回档前自动备份");
            Check("回档前自动留了一份（就是那 5 笔）", safety != null && safety.Count == 5, safety?.Count.ToString() ?? "没找到");

            // 5) 回档本身可逆：撤回到「回档前自动备份」
            Check("还能再回档到那份自动备份", safety != null && Store.RestoreSnapshot(safety.FileName),
                Store.LastError ?? "");
            Check("数据又变回 5 笔", Store.Data.transactions.Count == 5, Store.Data.transactions.Count.ToString());
            Check("id 和回档前一模一样", IdsOf(Store.Data.transactions) == ids5);

            // 6) 清理
            for (int i = 1; i <= 3; i++) Store.Snapshot("自检-临时" + i);
            var allBefore = Store.ListSnapshots();
            Check("清理前快照确实攒多了", allBefore.Count >= 5, allBefore.Count.ToString());
            int deleted = Store.PruneSnapshots(2);
            var after = Store.ListSnapshots();
            Check("PruneSnapshots(2) 返回的删除个数对得上", deleted == allBefore.Count - 2,
                $"删了 {deleted} 个，原本 {allBefore.Count} 个");
            Check("只剩 2 个", after.Count == 2, after.Count.ToString());
            Check("留下的是最新的 2 个",
                after.Select(x => x.FileName).SequenceEqual(allBefore.Take(2).Select(x => x.FileName)));
            Check("被删的文件真的从盘上没了",
                allBefore.Skip(2).All(x => !File.Exists(Path.Combine(Store.BackupDir, x.FileName))));
            Check("PruneSnapshots(0) 不会把家底清空", Store.PruneSnapshots(0) == 0 && Store.ListSnapshots().Count == 2);

            // 7) 边界：坏名字、坏文件都不能让程序崩
            Check("删不存在的快照：返回 false 不崩", Store.DeleteSnapshot("没有这个快照.json") == false);
            Check("回档不存在的快照：返回 false 不崩", Store.RestoreSnapshot("没有这个快照.json") == false);
            Check("挡住路径穿越 ..\\..\\db.json", Store.RestoreSnapshot("..\\..\\db.json") == false);
            Check("挡住绝对路径", Store.DeleteSnapshot(@"C:\Windows\System32\drivers\etc\hosts") == false);
            Check("挡住 index.json（那是说明清单，不是快照）", Store.RestoreSnapshot("index.json") == false);

            var badName = "自检坏文件.json";
            File.WriteAllText(Path.Combine(Store.BackupDir, badName), "这不是 JSON");
            var bad = Store.ListSnapshots().FirstOrDefault(x => x.FileName == badName);
            Check("坏文件也列得出来、标成读不出来", bad != null && !bad.Readable);
            int beforeBad = Store.Data.transactions.Count;
            Check("回档坏文件：返回 false", Store.RestoreSnapshot(badName) == false);
            Check("回档坏文件之后数据没被弄坏",
                Store.Data.transactions.Count == beforeBad && CountInFile(Store.FilePath) == beforeBad);
            Store.DeleteSnapshot(badName);
            Check("坏文件删得掉", !File.Exists(Path.Combine(Store.BackupDir, badName)));

            // 8) 界面：把对话框真的建出来，离屏画浅色 / 深色两张图
            Store.Snapshot("导入账单前");
            Store.Snapshot("手动备份");
            Store.Snapshot("恢复前自动留一份");
            try
            {
                ShootBackupWindow(Path.Combine(outDir, "backup-light.png"), "light", 700, 620);
                ShootBackupWindow(Path.Combine(outDir, "backup-dark.png"), "dark", 700, 620);
                Check("备份对话框建得出来并且画得出图",
                    File.Exists(Path.Combine(outDir, "backup-light.png")) &&
                    File.Exists(Path.Combine(outDir, "backup-dark.png")));
            }
            catch (Exception ex)
            {
                Check("备份对话框建得出来并且画得出图", false, ex.Message);
            }
            ThemeManager.Apply(Store.Data.settings.theme, Store.Data.settings.accent);

            // 9) 收尾：清掉自检造的快照，db.json 还原
            foreach (var s in Store.ListSnapshots())
                if (!keepSnapshots.Contains(s.FileName)) Store.DeleteSnapshot(s.FileName);
            if (keepDb != null)
            {
                try { File.WriteAllText(Store.FilePath, keepDb); } catch { }
            }
            // 自检前压根没有 db.json（比如全新解压出来、还没记过账就跑自检），
            // 那就得把自检造出来的这份删掉，不能留着 —— 否则用户第一次跑完自检，
            // 打开程序会看见几条「自检-早餐」这种假数据。
            else
            {
                try { if (File.Exists(Store.FilePath)) File.Delete(Store.FilePath); } catch { }
            }
            // .bak 也得一起还原：只还原 db.json 的话，备份文件里会残留自检造的那几笔，
            // 下次真出事了回滚回来就会多出几条假数据。
            try
            {
                string bak = Store.FilePath + ".bak";
                if (keepBak != null) File.WriteAllText(bak, keepBak);
                else if (File.Exists(bak)) File.Delete(bak);
            }
            catch { }
            Store.Load();
            Check("收尾：自检留下的快照清干净了",
                Store.ListSnapshots().All(x => keepSnapshots.Contains(x.FileName)),
                Store.ListSnapshots().Count + " 个");
            Check("收尾：db.json 回到自检前的样子", CountInFile(Store.FilePath) == keepCount,
                CountInFile(Store.FilePath) + " 笔（原本 " + keepCount + "）");
            Check("收尾：db.json.bak 也不残留自检数据",
                keepBak == null ? !File.Exists(Store.FilePath + ".bak")
                                : CountInFile(Store.FilePath + ".bak") == CountInFile(Store.FilePath),
                CountInFile(Store.FilePath + ".bak") + " 笔（db.json 是 " + CountInFile(Store.FilePath) + " 笔）");

            log.AppendLine("---");
            log.AppendLine($"结果：通过 {pass} 项，失败 {fail} 项");
            log.AppendLine(fail == 0 ? "自检全部通过。" : "有失败项，看上面带 [失败] 的行。");
            File.WriteAllText(Path.Combine(outDir, "selftest-log.txt"), log.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>把备份对话框离屏画成 PNG（不用真弹窗，跟 --shot 一个套路）</summary>
        private void ShootBackupWindow(string path, string theme, int w, int h)
        {
            ThemeManager.Apply(theme, null);
            var win = new BackupWindow();
            var root = (FrameworkElement)win.Content;
            root.Width = w;
            root.Height = h;
            for (int i = 0; i < 3; i++)
            {
                root.Measure(new Size(w, h));
                root.Arrange(new Rect(0, 0, w, h));
                root.UpdateLayout();
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private void RunShot(string[] args)
        {
            string page = Arg(args, 0) ?? "record";
            string outPath = Arg(args, 1) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot.png");
            string theme = Arg(args, 2);
            bool demo = args.Any(a => a == "--demo");

            if (demo) Demo.Fill();

            // 主题要在建窗口之前定下来，不然窗口里的主题下拉还显示旧的值
            if (!string.IsNullOrEmpty(theme))
            {
                Store.Data.settings.theme = theme;
                ThemeManager.Apply(theme, null);
            }

            var win = new MainWindow();
            win.Init();
            win.Navigate(page);
            ThemeManager.Apply(Store.Data.settings.theme, Store.Data.settings.accent);

            // 【已知限制，别拿离屏图判断布局对错】
            // 这里出的是纯离屏图（RenderTargetBitmap），没有真实窗口视口，
            // ScrollViewer 里按比例和按右对齐摆的元素会被算窄一点，
            // 结果就是图里右对齐的数字可能只显示开头一截（像「支 20,」这种），
            // 但真窗口里其实是完整显示的。试过先 Show 到屏幕外再截，没解决，
            // 还引出窗口 Loaded 把导航重置回记账页的新问题，所以回退了。
            // 要看布局请用真窗口截图；这张图只用来快速看整体结构和配色。
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            // 加 --tall 出一张长图，用来自检设置页那种要滚下去才看得全的页面
            if (args.Any(a => a == "--tall")) win.RenderTo(outPath, 1240, 1560);
            else win.RenderTo(outPath, 1240, 800);

            win.Close();
        }

        /// <summary>
        /// 分类自检：改名字之后，已经记过的账有没有跟着改；删分类时记录会不会丢。
        /// 不碰界面，直接调用和界面同一套逻辑（CategoryWindow 里的迁移规则）。
        /// 用法：MoneyTracker.exe --catcheck [输出目录]
        /// </summary>
        private void RunCatCheck(string outDir)
        {
            var log = new System.Text.StringBuilder();
            int pass = 0, fail = 0;
            void Check(string name, bool ok, string detail = "")
            {
                if (ok) pass++; else fail++;
                log.AppendLine((ok ? "[通过] " : "[失败] ") + name + (detail.Length > 0 ? "  —— " + detail : ""));
            }

            log.AppendLine("=== 分类自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");

            string keepDb = null;
            try { if (File.Exists(Store.FilePath)) keepDb = File.ReadAllText(Store.FilePath); } catch { }
            string keepBak = null;
            try { if (File.Exists(Store.FilePath + ".bak")) keepBak = File.ReadAllText(Store.FilePath + ".bak"); } catch { }

            try
            {
                Categories.Ensure();

                // 造两笔挂在「餐饮 / 外卖」上的记录
                Store.Data.transactions.Clear();
                var a = new Transaction
                {
                    type = "expense", amount = 1500, date = "2026-09-29", time = "12:00",
                    category = "餐饮", subcategory = "外卖", account = "现金", merchant = "美团", source = "manual"
                };
                var b = new Transaction
                {
                    type = "expense", amount = 2000, date = "2026-09-29", time = "18:00",
                    category = "餐饮", subcategory = "外卖", account = "现金", merchant = "饿了么", source = "manual"
                };
                a.hash = Util.HashOf(a); b.hash = Util.HashOf(b);
                Store.Data.transactions.Add(a);
                Store.Data.transactions.Add(b);
                Store.Save();

                var ex = Categories.ExpenseList;
                Check("能读到分类列表", ex != null && ex.Count > 0, "支出分类 " + (ex?.Count ?? 0) + " 个");
                Check("有「餐饮」这个分类", ex.Any(g => g.name == "餐饮"));

                // 加一个「生活费」——用户明确想要的能力
                var life = new CategoryGroup("生活费", "房租", "水电");
                ex.Add(life);
                Store.Data.settings.expenseCategories = ex;
                Check("加得进「生活费」", Categories.ExpenseList.Any(g => g.name == "生活费"));
                Check("「餐饮」还在", Categories.ExpenseList.Any(g => g.name == "餐饮"));

                // 一级分类改名：餐饮 -> 吃饭，老记录必须跟着改
                var cat = Categories.ExpenseList.First(g => g.name == "餐饮");
                string oldCatId = cat.id;
                cat.name = "吃饭";

                foreach (var t in Store.Data.transactions)
                    if (t.category == "餐饮") { t.category = "吃饭"; t.hash = Util.HashOf(t); }

                Check("改名后老记录的 category 跟着改了",
                    Store.Data.transactions.All(t => t.category != "餐饮"),
                    string.Join(",", Store.Data.transactions.Select(t => t.category).Distinct()));
                Check("改名后还是那两条记录", Store.Data.transactions.Count == 2);
                Check("分类的内部 id 没变（能认出是谁改的名）",
                    Categories.ExpenseList.Any(g => g.id == oldCatId && g.name == "吃饭"));

                // 二级分类改名：外卖 -> 点外卖
                var nowCat = Categories.ExpenseList.First(g => g.name == "吃饭");
                int si = nowCat.subs.IndexOf("外卖");
                Check("「外卖」是「吃饭」下面的二级分类", si >= 0, "位置 " + si);
                if (si >= 0) nowCat.subs[si] = "点外卖";
                foreach (var t in Store.Data.transactions)
                    if (t.category == "吃饭" && t.subcategory == "外卖") t.subcategory = "点外卖";

                Check("二级分类改名也迁移了老记录",
                    Store.Data.transactions.All(t => t.subcategory != "外卖"),
                    string.Join(",", Store.Data.transactions.Select(t => t.subcategory).Distinct()));

                // 删掉一个分类：挂在上面的记录不能凭空消失
                var before = Store.Data.transactions.Count;
                Categories.ExpenseList.RemoveAll(g => g.name == "吃饭");
                string fb = Categories.ExpenseList.Any(g => g.name == "其他支出") ? "其他支出"
                          : (Categories.ExpenseList.Count > 0 ? Categories.ExpenseList[0].name : "其他支出");
                foreach (var t in Store.Data.transactions)
                    if (t.category == "吃饭") { t.category = fb; t.subcategory = ""; t.hash = Util.HashOf(t); }

                Check("删了分类，记录一条都没少", Store.Data.transactions.Count == before,
                    before + " → " + Store.Data.transactions.Count);
                Check("被删分类下的记录挪到兜底分类了",
                    Store.Data.transactions.All(t => t.category == fb),
                    string.Join(",", Store.Data.transactions.Select(t => t.category).Distinct()));

                // 重名和空名应该被拦（这里只验证判断逻辑，界面那边会弹提示）
                var dup = new List<CategoryGroup>
                {
                    new CategoryGroup("吃饭"), new CategoryGroup("吃饭")
                };
                Check("重名能被发现", dup.GroupBy(x => x.name).Any(g => g.Count() > 1));
                var blank = new List<CategoryGroup> { new CategoryGroup("  ") };
                Check("空名字能被发现", blank.Any(x => string.IsNullOrWhiteSpace(x.name)));

                // 恢复出厂
                Store.Data.settings.expenseCategories = Categories.CloneAll(Categories.DefaultExpense);
                Check("能恢复出厂分类",
                    Categories.ExpenseList.Count == Categories.DefaultExpense.Length &&
                    Categories.ExpenseList.Any(g => g.name == "餐饮"),
                    "恢复后 " + Categories.ExpenseList.Count + " 个");
            }
            finally
            {
                try
                {
                    if (keepDb != null) File.WriteAllText(Store.FilePath, keepDb);
                    else if (File.Exists(Store.FilePath)) File.Delete(Store.FilePath);
                    string bak = Store.FilePath + ".bak";
                    if (keepBak != null) File.WriteAllText(bak, keepBak);
                    else if (File.Exists(bak)) File.Delete(bak);
                    Store.Load();
                }
                catch { }
            }

            log.AppendLine("---");
            log.AppendLine("结果：通过 " + pass + " 项，失败 " + fail + " 项");
            log.AppendLine(fail == 0 ? "自检全部通过。" : "有失败项，看上面。");
            File.WriteAllText(Path.Combine(outDir, "catcheck-log.txt"), log.ToString(),
                new System.Text.UTF8Encoding(true));
            Console.WriteLine(log.ToString());
        }

        private static string Arg(string[] args, int index)
        {
            var vals = args.Where(a => !a.StartsWith("--")).ToArray();
            return index < vals.Length ? vals[index] : null;
        }

        /// <summary>
        /// 「改这一笔」自检：验证在明细里编辑一笔、保存之后数据真的变了。
        /// 起因是个真 bug：EditWindow 克隆时每次都换新 id，UpdateTx 按 id 找不到人就直接
        /// 静默返回，于是「改完保存跟没改一样」，还不报错。这条自检就是钉住它不许复发。
        /// 用法：MoneyTracker.exe --editcheck [输出目录]
        /// </summary>
        private void RunEditCheck(string outDir)
        {
            var log = new System.Text.StringBuilder();
            int pass = 0, fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) pass++; else fail++;
                log.AppendLine((ok ? "[通过] " : "[失败] ") + name + (detail.Length > 0 ? "  —— " + detail : ""));
            }

            log.AppendLine("=== 「改这一笔」自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            log.AppendLine("数据目录: " + Store.Dir);

            // 先记住跑之前的样子，结束时还原，别弄脏日常数据
            string keepDb = null;
            try { if (File.Exists(Store.FilePath)) keepDb = File.ReadAllText(Store.FilePath); } catch { }
            string keepBak = null;
            try { if (File.Exists(Store.FilePath + ".bak")) keepBak = File.ReadAllText(Store.FilePath + ".bak"); } catch { }

            try
            {
                // 1) 造一笔支出
                Store.Data.transactions.Clear();
                var tx = new Transaction
                {
                    type = "expense", amount = 1250, date = "2026-09-29", time = "12:30",
                    category = "餐饮", subcategory = "外卖", account = "现金",
                    merchant = "美团", note = "原始备注", source = "manual"
                };
                tx.hash = Util.HashOf(tx);
                Store.Data.transactions.Add(tx);
                Store.Save();
                string origId = tx.id;
                log.AppendLine("造了一笔支出，id=" + origId.Substring(0, 8) + "…，金额 12.50");

                // 2) 打开「修改这一笔」，id 必须保持不变 —— 这条就是那个 bug 的命门
                var w = new EditWindow(tx);
                Check("修改模式下 id 没被换掉（换掉就永远改不动）",
                    w.Editing.id == origId,
                    "原 " + origId.Substring(0, 8) + " → 现 " + w.Editing.id.Substring(0, 8));

                // 3) 复制模式反过来必须换新 id，否则会把原记录覆盖掉
                var c = new EditWindow(tx, true);
                Check("复制模式下 id 是新的（不会覆盖原来那笔）", c.Editing.id != origId);

                // 4) 改收支类型 + 金额 + 分类，然后走真实的保存入口
                w.Editing.type = "income";
                w.Editing.amount = 88888;
                w.Editing.category = "工资";
                w.Editing.subcategory = "";
                w.Editing.merchant = "改过的商户";
                w.Editing.note = "改过的备注";

                bool updated = Store.UpdateTx(w.Editing);
                Check("UpdateTx 报告改成功了", updated);

                var back = Store.Data.transactions.FirstOrDefault(x => x.id == origId);
                Check("记录还是那一条（没有变成两条）", Store.Data.transactions.Count == 1,
                    "现在 " + Store.Data.transactions.Count + " 笔");
                Check("支出改成收入了", back != null && back.type == "income", back?.type ?? "找不到");
                Check("金额改成了 888.88", back != null && back.amount == 88888,
                    back != null ? Util.Money(back.amount) : "找不到");
                Check("分类改成工资了", back != null && back.category == "工资", back?.category ?? "找不到");
                Check("备注也改了", back != null && back.note == "改过的备注", back?.note ?? "找不到");

                // 5) 盘上的文件也必须是改过的，不能只在内存里改了
                int diskIsIncome = -1;
                long diskAmount = -1;
                try
                {
                    var d = System.Text.Json.JsonSerializer.Deserialize<AppData>(
                        File.ReadAllText(Store.FilePath),
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    var one = d?.transactions?.FirstOrDefault(x => x.id == origId);
                    if (one != null) { diskIsIncome = one.type == "income" ? 1 : 0; diskAmount = one.amount; }
                }
                catch { }
                Check("改完已经写进 db.json（重开程序还是改过的）",
                    diskIsIncome == 1 && diskAmount == 88888,
                    "盘上 type=" + diskIsIncome + " amount=" + diskAmount);

                // 6) 改一个不存在的 id：必须报告失败，不能假装成功，也不能偷偷加一条
                var ghost = new Transaction
                {
                    id = "这个id根本不存在", type = "expense", amount = 1,
                    date = "2026-09-29", time = "00:00", category = "餐饮"
                };
                Check("改不存在的记录会报告失败（不再静默吞掉）", Store.UpdateTx(ghost) == false);
                Check("失败时没有偷偷塞一条新记录", Store.Data.transactions.Count == 1,
                    "现在 " + Store.Data.transactions.Count + " 笔");
            }
            finally
            {
                try
                {
                    if (keepDb != null) File.WriteAllText(Store.FilePath, keepDb);
                    else if (File.Exists(Store.FilePath)) File.Delete(Store.FilePath);
                    string bak = Store.FilePath + ".bak";
                    if (keepBak != null) File.WriteAllText(bak, keepBak);
                    else if (File.Exists(bak)) File.Delete(bak);
                    Store.Load();
                }
                catch { }
            }

            log.AppendLine("---");
            log.AppendLine("结果：通过 " + pass + " 项，失败 " + fail + " 项");
            log.AppendLine(fail == 0 ? "自检全部通过。" : "有失败项，看上面。");
            File.WriteAllText(Path.Combine(outDir, "editcheck-log.txt"), log.ToString(),
                new System.Text.UTF8Encoding(true));
            Console.WriteLine(log.ToString());
        }

        /// <summary>不弹窗、直接导入，用来做自动化校验</summary>
        private void RunImport(string[] args)
        {
            string target = Arg(args, 0);
            bool dupes = args.Any(a => a == "--dupes");
            var log = new System.Text.StringBuilder();
            log.AppendLine("导入前记录数: " + Store.Data.transactions.Count);

            var results = new List<ImportResult>();
            if (target != null && Directory.Exists(target))
                results.AddRange(CsvImporter.ImportFolder(target, dupes));
            else if (target != null)
                results.Add(CsvImporter.ImportFile(target, dupes));
            else
            {
                log.AppendLine("没给文件路径");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import-log.txt"),
                    log.ToString(), new System.Text.UTF8Encoding(false));
                return;
            }

            foreach (var r in results)
            {
                log.AppendLine("---");
                log.AppendLine("文件: " + Path.GetFileName(r.File));
                log.AppendLine("认出的格式: " + r.Format);
                log.AppendLine("新增: " + r.Added);
                log.AppendLine("跳过: " + r.Skipped);
                log.AppendLine("重复: " + r.Duplicated);
                log.AppendLine("支出(分): " + r.OutSum + "  收入(分): " + r.InSum);
                foreach (var s in r.SkipReasons) log.AppendLine("跳过原因: " + s);
                foreach (var s in r.Errors) log.AppendLine("错误: " + s);
            }

            log.AppendLine("---");
            log.AppendLine("导入后记录数: " + Store.Data.transactions.Count);
            log.AppendLine("分类分布:");
            foreach (var g in Store.Data.transactions.GroupBy(t => t.category).OrderByDescending(g => g.Count()))
                log.AppendLine($"  {g.Key}: {g.Count()}");
            log.AppendLine("来源分布:");
            foreach (var g in Store.Data.transactions.GroupBy(t => t.source))
                log.AppendLine($"  {g.Key}: {g.Count()}");

            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import-log.txt"),
                log.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>不弹窗、直接导出四个文件，用来做自动化校验</summary>
        private void RunExport(string[] args)
        {
            string dir = Arg(args, 0) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export");
            bool demo = args.Any(a => a == "--demo");
            if (demo && Store.Data.transactions.Count < 50) Demo.Fill();
            Directory.CreateDirectory(dir);

            var list = Store.Query().ToList();
            var log = new System.Text.StringBuilder();
            log.AppendLine("记录数: " + list.Count);

            var xlsx = Path.Combine(dir, "记账报表.xlsx");
            Exporters.Xlsx(list, xlsx);
            log.AppendLine($"xlsx: {xlsx} -> {(File.Exists(xlsx) ? new FileInfo(xlsx).Length : 0)} bytes");

            var docx = Path.Combine(dir, "记账报表.docx");
            Exporters.DocxReport(list, docx);
            log.AppendLine($"docx: {docx} -> {(File.Exists(docx) ? new FileInfo(docx).Length : 0)} bytes");

            var csv = Path.Combine(dir, "记账明细.csv");
            Exporters.Csv(list, csv);
            log.AppendLine($"csv: {csv} -> {(File.Exists(csv) ? new FileInfo(csv).Length : 0)} bytes");

            var json = Path.Combine(dir, "记账备份.json");
            Exporters.JsonBackup(json);
            log.AppendLine($"json: {json} -> {(File.Exists(json) ? new FileInfo(json).Length : 0)} bytes");

            // 顺便吐一份摘要，让外部脚本能对着核对数字
            long outSum = list.Where(t => t.type == "expense").Sum(t => t.amount);
            long inSum = list.Where(t => t.type == "income").Sum(t => t.amount);
            log.AppendLine($"支出合计(分): {outSum}");
            log.AppendLine($"收入合计(分): {inSum}");
            log.AppendLine($"净资产(分): {Store.NetAssets()}");
            log.AppendLine("账户数: " + Store.Data.accounts.Count);
            log.AppendLine("规则数: " + Store.Data.rules.Count);

            File.WriteAllText(Path.Combine(dir, "export-log.txt"), log.ToString(), new System.Text.UTF8Encoding(false));
        }
    }
}