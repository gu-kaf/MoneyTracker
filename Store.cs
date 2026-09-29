using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoneyTracker
{
    /// <summary>
    /// 数据存储：exe 同目录下的 data\db.json。
    /// 便携式设计——整个程序文件夹拷走就是完整数据，删文件夹就删干净了，
    /// 不往注册表、不往系统目录写任何东西。
    /// </summary>
    public static class Store
    {
        public static AppData Data { get; private set; }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static string Dir
        {
            get
            {
                var d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string FilePath => Path.Combine(Dir, "db.json");
        public static string BackupPath => Path.Combine(Dir, "db.json.bak");

        public static event Action Changed;

        public static void RaiseChanged() => Changed?.Invoke();

        public static void Load()
        {
            Data = null;
            if (File.Exists(FilePath))
            {
                try { Data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), JsonOpts); }
                catch { Data = null; }
            }
            if (Data == null && File.Exists(BackupPath))
            {
                try { Data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(BackupPath), JsonOpts); }
                catch { Data = null; }
            }
            if (Data == null) Data = CreateDefault();
            Normalize();
        }

        /// <summary>全新库：预置账户 + 预置自动记账规则</summary>
        public static AppData CreateDefault()
        {
            var d = new AppData
            {
                accounts = DefaultAccounts.Build(),
                rules = DefaultRules.Build(),
                budgets = new List<Budget>(),
                transactions = new List<Transaction>(),
                settings = new AppSettings()
            };
            return d;
        }

        private static void Normalize()
        {
            Data.accounts ??= new List<Account>();
            Data.transactions ??= new List<Transaction>();
            Data.budgets ??= new List<Budget>();
            Data.rules ??= new List<Rule>();
            Data.ledgers ??= new List<Ledger>();
            Data.settings ??= new AppSettings();

            if (Data.accounts.Count == 0) Data.accounts = DefaultAccounts.Build();
            if (Data.rules.Count == 0) Data.rules = DefaultRules.Build();

            // 分类体系：用户那一份存在设置里；老数据没这两个字段，这里灌出厂默认
            Categories.Ensure();

            // 多人账本：至少要有一个，而且 currentLedger 必须指向真实存在的账本
            // （删过账本、或者从别人那拷来的 db 都可能指向一个已经没有的 id）
            if (Data.ledgers.Count == 0)
                Data.ledgers.Add(new Ledger { name = "我", note = "默认账本" });
            foreach (var l in Data.ledgers)
            {
                if (string.IsNullOrEmpty(l.id)) l.id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrEmpty(l.name)) l.name = "未命名";
            }
            string firstLedger = Data.ledgers[0].id;
            if (Data.ledgers.All(l => l.id != Data.settings.currentLedger))
                Data.settings.currentLedger = firstLedger;
            // 老记录没有 ledgerId，统一归到第一个账本，不会从明细里凭空消失
            foreach (var t in Data.transactions)
                if (string.IsNullOrEmpty(t.ledgerId)) t.ledgerId = firstLedger;

            // 记录挂在一个已经不存在的账本上（手改过 db.json、或者库是从别处拷来的）：
            // 同样收回到第一个账本。不然这些记录会从所有页面上消失——文件里还在，界面上看不见，
            // 那和丢了没区别。多人账本这块「数据不能凭空不见」是硬要求。
            foreach (var t in Data.transactions)
                if (Data.ledgers.All(l => l.id != t.ledgerId)) t.ledgerId = firstLedger;

            foreach (var t in Data.transactions)
            {
                t.tags ??= new List<string>();
                if (string.IsNullOrEmpty(t.hash)) t.hash = Util.HashOf(t);
            }
            // 同一指纹只留一条——但要按账本分组：两个人各记一笔一模一样的账是完全正常的，
            // 不分账本去重会把别人账本里的记录直接删掉（那是真丢数据）
            foreach (var e in Data.transactions.GroupBy(t => (t.ledgerId ?? "") + "|" + t.hash).Where(g => g.Count() > 1))
            {
                foreach (var dup in e.Skip(1)) Data.transactions.Remove(dup);
            }
        }

        public static void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Data, JsonOpts);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(FilePath))
                {
                    try { File.Replace(tmp, FilePath, BackupPath); }
                    catch { File.Copy(tmp, FilePath, true); File.Delete(tmp); }
                }
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("保存失败：" + ex.Message, "记账本", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        // ==================== 交易 ====================

        /// <summary>整库替换（从备份恢复时用）</summary>
        public static void ReplaceAll(AppData data)
        {
            Data = data ?? CreateDefault();
            Normalize();
            Save();
            RaiseChanged();
        }

        public static void AddTx(Transaction t, bool allowDuplicate = false)
        {
            if (string.IsNullOrEmpty(t.hash)) t.hash = Util.HashOf(t);
            t.updatedAt = Util.NowStamp();
            // 没写账本的新记录，就记在当前账本名下（多人共用一个程序时，
            // 「我刚记的这笔去哪了」是最要命的 bug，所以这里是兜底）
            if (string.IsNullOrEmpty(t.ledgerId)) t.ledgerId = CurrentLedgerId;
            if (!allowDuplicate && IsDuplicate(t)) return;
            Data.transactions.Add(t);
            Save();
            RaiseChanged();
        }

        /// <summary>同指纹判重——只在同一个账本里比。两个人各记一笔长得一样的账不算重复。</summary>
        public static bool IsDuplicate(Transaction t)
        {
            if (string.IsNullOrEmpty(t.hash)) t.hash = Util.HashOf(t);
            var id = t.ledgerId ?? "";
            return Data.transactions.Any(x => x.hash == t.hash && (x.ledgerId ?? "") == id);
        }

        /// <summary>改一笔。返回 false 表示没找到那条记录（以前是静默返回，出过
        /// 「改完保存跟没改一样」的 bug，现在让调用方知道失败了）。</summary>
        public static bool UpdateTx(Transaction t)
        {
            var old = Data.transactions.FirstOrDefault(x => x.id == t.id);
            if (old == null) return false;
            // 调用方（比如编辑窗口）可能没带上账本字段，那就沿用它原来的账本，
            // 免得「改一笔」把它从本来的账本里挪走、甚至挪到别人账本里
            if (string.IsNullOrEmpty(t.ledgerId)) t.ledgerId = old.ledgerId;
            t.hash = Util.HashOf(t);
            t.updatedAt = Util.NowStamp();
            var i = Data.transactions.IndexOf(old);
            Data.transactions[i] = t;
            Save();
            RaiseChanged();
            return true;
        }

        public static void DeleteTx(string id)
        {
            Data.transactions.RemoveAll(x => x.id == id);
            Save();
            RaiseChanged();
        }

        public static void DeleteTx(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids);
            Data.transactions.RemoveAll(x => set.Contains(x.id));
            Save();
            RaiseChanged();
        }

        public static IEnumerable<Transaction> Query(string from = null, string to = null, string keyword = null,
            string type = null, string category = null, string account = null, string month = null)
        {
            IEnumerable<Transaction> q = Data.transactions;
            if (!string.IsNullOrEmpty(month))
            {
                var (f, t) = Util.MonthRange(month);
                q = q.Where(x => string.CompareOrdinal(x.date, f) >= 0 && string.CompareOrdinal(x.date, t) <= 0);
            }
            if (!string.IsNullOrEmpty(from)) q = q.Where(x => string.CompareOrdinal(x.date, from) >= 0);
            if (!string.IsNullOrEmpty(to)) q = q.Where(x => string.CompareOrdinal(x.date, to) <= 0);
            if (!string.IsNullOrEmpty(type) && type != "all") q = q.Where(x => x.type == type);
            if (!string.IsNullOrEmpty(category)) q = q.Where(x => x.category == category);
            if (!string.IsNullOrEmpty(account)) q = q.Where(x => x.account == account);
            if (!string.IsNullOrEmpty(keyword))
            {
                var k = keyword.Trim();
                q = q.Where(x => (x.merchant ?? "").Contains(k) || (x.note ?? "").Contains(k)
                              || (x.category ?? "").Contains(k) || (x.subcategory ?? "").Contains(k)
                              || (x.account ?? "").Contains(k) || (x.tags != null && x.tags.Any(g => g.Contains(k))));
            }
            return q.OrderByDescending(x => x.date).ThenByDescending(x => x.time).ThenByDescending(x => x.createdAt);
        }

        // ==================== 账户余额 ====================

        public static long BalanceOf(string accountName)
        {
            var acc = Data.accounts.FirstOrDefault(a => a.name == accountName);
            long bal = acc?.initialBalance ?? 0;
            foreach (var t in Data.transactions)
            {
                if (t.type == "expense" && t.account == accountName) bal -= t.amount;
                else if (t.type == "income" && t.account == accountName) bal += t.amount;
                else if (t.type == "transfer")
                {
                    if (t.account == accountName) bal -= t.amount;
                    if (t.toAccount == accountName) bal += t.amount;
                }
            }
            return bal;
        }

        public static long NetAssets()
            => Data.accounts.Where(a => a.includeInAssets && !a.archived).Sum(a => BalanceOf(a.name));

        // ==================== 规则引擎 ====================

        /// <summary>按关键词匹配规则，命中就返回要套用的分类（优先级高的先看）</summary>
        public static Rule MatchRule(Transaction t)
        {
            foreach (var r in Data.rules.Where(r => r.enabled).OrderByDescending(r => r.priority))
            {
                var kws = (r.keyword ?? "").Split(new[] { '|', '，', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                            .Select(s => s.Trim()).Where(s => s.Length > 0);
                string target = r.matchField == "merchant" ? (t.merchant ?? "")
                              : r.matchField == "note" ? (t.note ?? "") + " " + (t.merchant ?? "")
                              : (t.merchant ?? "") + " " + (t.note ?? "");
                foreach (var k in kws)
                {
                    if (target.Contains(k)) { r.hitCount++; return r; }
                }
            }
            return null;
        }

        public static void ApplyRules(Transaction t)
        {
            var r = MatchRule(t);
            if (r == null) return;
            if (!string.IsNullOrEmpty(r.setType)) t.type = r.setType;
            if (!string.IsNullOrEmpty(r.setCategory)) t.category = r.setCategory;
            if (!string.IsNullOrEmpty(r.setSubcategory)) t.subcategory = r.setSubcategory;
            t.source = "auto";
        }

        /// <summary>对历史记录重跑规则（导入规则后一键回填）</summary>
        public static int RerunRules()
        {
            int n = 0;
            foreach (var t in Data.transactions)
            {
                var before = t.category + "|" + t.subcategory;
                var r = MatchRule(t);
                if (r == null) continue;
                if (!string.IsNullOrEmpty(r.setCategory)) t.category = r.setCategory;
                if (!string.IsNullOrEmpty(r.setSubcategory)) t.subcategory = r.setSubcategory;
                if (!string.IsNullOrEmpty(r.setType)) t.type = r.setType;
                if (before != t.category + "|" + t.subcategory) { t.source = "auto"; n++; }
            }
            if (n > 0) { Save(); RaiseChanged(); }
            return n;
        }

        // ==================== 快照（程序内备份 / 回档） ====================
        // 一个快照 = backups 目录下的一个 JSON 文件，里面装的就是「当时那整份 AppData」，
        // 格式和 db.json 一模一样，所以拷出去也能直接当整库备份用。
        // 说明文字（"手动备份"这类）单独记在同目录的 index.json 里，不往数据本身里塞东西。
        // 这里只加新方法，不动上面任何已有逻辑。

        /// <summary>最后一次操作的出错原因（界面可以直接拿去提示用户）</summary>
        public static string LastError { get; private set; }

        /// <summary>快照目录：数据目录下的 backups\</summary>
        public static string BackupDir
        {
            get
            {
                var d = Path.Combine(Dir, "backups");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        private static string IndexPath => Path.Combine(BackupDir, "index.json");

        /// <summary>快照目录里的说明清单：文件名 -> 说明 + 建立时间</summary>
        private class SnapshotNote
        {
            public string reason { get; set; } = "";
            public string at { get; set; } = "";
        }

        /// <summary>一个快照的概况，界面直接绑定这个</summary>
        public class SnapshotInfo
        {
            public string FileName { get; set; } = "";
            public DateTime Time { get; set; }
            public string Reason { get; set; } = "";
            public long Size { get; set; }
            public int Count { get; set; }
            public bool Readable { get; set; } = true;

            /// <summary>排序用。同一秒里连建好几个快照时，靠它才分得清先后</summary>
            public DateTime SortTime { get; set; }

            public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");
            public string ReasonText => !Readable ? "文件读不出来，可能坏了"
                                    : (string.IsNullOrWhiteSpace(Reason) ? "未说明" : Reason);
            public string CountText => Readable ? Count + " 笔" : "—";
            public string SizeText => Store.SizeText(Size);
        }

        /// <summary>字节数 -> "12.3 KB"，给界面用</summary>
        public static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024)
                return Math.Round(bytes / 1024.0, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            return Math.Round(bytes / 1024.0 / 1024.0, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>快照文件名：20260929-181530.json；同一秒里再建一个就加后缀 -1、-2</summary>
        private static string UniqueName(DateTime when)
        {
            var stamp = when.ToString("yyyyMMdd-HHmmss");
            var name = stamp + ".json";
            int n = 1;
            while (File.Exists(Path.Combine(BackupDir, name))) name = stamp + "-" + (n++) + ".json";
            return name;
        }

        /// <summary>
        /// 给当前数据留一个快照，成功返回文件名（如 20260929-181530.json），失败返回 null。
        /// 说明写短一点就行：「手动备份」「导入账单前」「回档前自动备份」。
        /// </summary>
        public static string Snapshot(string reason = "手动备份")
        {
            LastError = null;
            try
            {
                Directory.CreateDirectory(BackupDir);
                var when = DateTime.Now;
                var name = UniqueName(when);
                var path = Path.Combine(BackupDir, name);

                // db.json 读得出来就原样复制一份；还没有文件或者文件坏了，就用内存里的数据另写一份
                bool copied = false;
                if (File.Exists(FilePath))
                {
                    try
                    {
                        var text = File.ReadAllText(FilePath);
                        var check = JsonSerializer.Deserialize<AppData>(text, JsonOpts);
                        if (check != null) { File.WriteAllText(path, text); copied = true; }
                    }
                    catch { copied = false; }
                }
                if (!copied) File.WriteAllText(path, JsonSerializer.Serialize(Data ?? CreateDefault(), JsonOpts));

                var idx = ReadIndex();
                idx[name] = new SnapshotNote
                {
                    reason = string.IsNullOrWhiteSpace(reason) ? "手动备份" : reason.Trim(),
                    at = when.ToString("o")     // 带上毫秒，同一秒里连建几个也分得清先后
                };
                WriteIndex(idx);
                return name;
            }
            catch (Exception ex)
            {
                LastError = "备份失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>列出所有快照，最新的排最前</summary>
        public static List<SnapshotInfo> ListSnapshots()
        {
            LastError = null;
            var list = new List<SnapshotInfo>();
            try
            {
                Directory.CreateDirectory(BackupDir);
                var idx = ReadIndex();

                foreach (var f in Directory.GetFiles(BackupDir, "*.json"))
                {
                    var name = Path.GetFileName(f);
                    if (string.Equals(name, "index.json", StringComparison.OrdinalIgnoreCase)) continue; // 说明清单不是快照

                    var info = new SnapshotInfo { FileName = name, Time = TimeFromFile(name, f) };
                    try
                    {
                        info.Size = new FileInfo(f).Length;
                        info.SortTime = File.GetLastWriteTime(f);   // 兜底：文件自己的修改时间
                    }
                    catch { }
                    try
                    {
                        var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(f), JsonOpts);
                        if (data == null) throw new InvalidDataException("空文件");
                        info.Count = data.transactions?.Count ?? 0;
                        info.Readable = true;
                    }
                    catch { info.Readable = false; }

                    if (idx.TryGetValue(name, out var note) && note != null)
                    {
                        info.Reason = note.reason ?? "";
                        if (DateTime.TryParse(note.at, out var at))
                        {
                            info.Time = at;
                            info.SortTime = at;
                        }
                    }
                    list.Add(info);
                }
            }
            catch (Exception ex)
            {
                LastError = "读取快照目录失败：" + ex.Message;
            }
            return list.OrderByDescending(x => x.SortTime).ThenByDescending(x => x.FileName).ToList();
        }

        /// <summary>从文件名里的时间戳取时间，取不到就用文件的修改时间</summary>
        private static DateTime TimeFromFile(string name, string path)
        {
            try
            {
                var s = Path.GetFileNameWithoutExtension(name);
                if (s != null && s.Length >= 15 &&
                    DateTime.TryParseExact(s.Substring(0, 15), "yyyyMMdd-HHmmss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var t))
                    return t;
                return File.GetLastWriteTime(path);
            }
            catch { return DateTime.Now; }
        }

        /// <summary>
        /// 回档到某个快照（fileName 是 ListSnapshots 给出的文件名）。
        /// 回档前一定会先给当前状态留一份「回档前自动备份」，所以回档本身也能反悔；
        /// 万一这份自动备份写不出来，就宁可不回档。
        /// </summary>
        public static bool RestoreSnapshot(string fileName)
        {
            LastError = null;
            var path = ResolvePath(fileName);
            if (path == null) { LastError = "快照文件名不对。"; return false; }
            if (!File.Exists(path)) { LastError = "找不到这个快照文件。"; return false; }

            AppData data = null;
            try { data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), JsonOpts); }
            catch { data = null; }
            if (data == null) { LastError = "这个快照读不出来，可能文件坏了。"; return false; }

            var safety = Snapshot("回档前自动备份");
            if (safety == null)
            {
                LastError = "回档前的自动备份没成功（" + (LastError ?? "原因不明") + "），为了不丢数据就先不回档了。";
                return false;
            }

            ReplaceAll(data);   // 现有逻辑：整库替换 + 保存 + 通知界面刷新
            return true;
        }

        /// <summary>只保留最近 keep 个快照，多出来的删掉，返回删掉几个（keep 小于 1 时什么都不做）</summary>
        public static int PruneSnapshots(int keep)
        {
            LastError = null;
            if (keep < 1)
            {
                LastError = "至少要保留 1 个快照，所以这次没有清理。";
                return 0;
            }
            int deleted = 0;
            try
            {
                foreach (var s in ListSnapshots().Skip(keep))
                    if (DeleteSnapshot(s.FileName)) deleted++;
            }
            catch (Exception ex)
            {
                LastError = "清理失败：" + ex.Message;
            }
            return deleted;
        }

        /// <summary>删掉一个快照，返回是否删成功</summary>
        public static bool DeleteSnapshot(string fileName)
        {
            LastError = null;
            try
            {
                var path = ResolvePath(fileName);
                if (path == null) { LastError = "快照文件名不对。"; return false; }
                if (!File.Exists(path)) { LastError = "这个快照已经不在了。"; return false; }

                File.Delete(path);
                var idx = ReadIndex();
                if (idx.Remove(Path.GetFileName(path))) WriteIndex(idx);
                return true;
            }
            catch (Exception ex)
            {
                LastError = "删除失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>把文件名安全地变成 backups 目录里的完整路径（只认文件名，挡掉 ..\ 这种路径穿越）</summary>
        private static string ResolvePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var raw = fileName.Trim();
            var name = Path.GetFileName(raw);
            if (string.IsNullOrEmpty(name) || name != raw) return null;             // 带了目录的一律拒绝
            if (name.Contains("..")) return null;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(name, "index.json", StringComparison.OrdinalIgnoreCase)) return null;

            var root = Path.GetFullPath(BackupDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(BackupDir, name));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        /// <summary>读说明清单；文件不在或者坏了就当成空的，绝不让它影响快照本身</summary>
        private static Dictionary<string, SnapshotNote> ReadIndex()
        {
            var empty = new Dictionary<string, SnapshotNote>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(IndexPath)) return empty;
                var d = JsonSerializer.Deserialize<Dictionary<string, SnapshotNote>>(File.ReadAllText(IndexPath), JsonOpts);
                return d ?? empty;
            }
            catch { return empty; }
        }

        /// <summary>写说明清单；写不进去也不影响快照，下次列表里显示「未说明」而已</summary>
        private static void WriteIndex(Dictionary<string, SnapshotNote> idx)
        {
            try
            {
                var tmp = IndexPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(idx, JsonOpts));
                File.Copy(tmp, IndexPath, true);
                File.Delete(tmp);
            }
            catch { }
        }

        // ==================== 多人账本 ====================
        // 一个账本 = 一个人。所有记录都躺在同一份 db.json 里，靠 Transaction.ledgerId 分流。
        // 切换账本不换文件、不换目录，所以「切过去看一眼再切回来」不会丢数据。
        //
        // 这一段只加新方法（Query 是全量查询的老语义，原样保留：导出、备份、自检还在用它）。
        // 界面上要按账本过滤的地方，一律走 QueryScoped / Scoped。

        /// <summary>查询时传这个账本 id = 不过滤账本（全量），导出和对比用</summary>
        public const string AllLedgers = "*";

        /// <summary>标识色：用户没指定时按账本顺序从这套颜色里取</summary>
        private static readonly string[] LedgerColors =
        {
            "#2F6FED", "#E5484D", "#26A269", "#E08A1F", "#7C5CE0",
            "#0FA47F", "#E0568F", "#4C9AFF", "#C9503F", "#5B6472"
        };

        /// <summary>全部账本（活列表，改完记得 Save）。顺手把 null 补掉，免得往临时表里加账本</summary>
        public static List<Ledger> Ledgers
        {
            get
            {
                if (Data == null) return new List<Ledger>();
                return Data.ledgers ?? (Data.ledgers = new List<Ledger>());
            }
        }

        /// <summary>当前正在看的账本。数据坏了、指向了一个不存在的 id 时退回第一个，绝不返回 null 崩界面。</summary>
        public static Ledger CurrentLedger
        {
            get
            {
                var ls = Ledgers;
                if (ls.Count == 0) return null;
                var id = Data.settings != null ? Data.settings.currentLedger : null;
                return ls.FirstOrDefault(l => l.id == id) ?? ls[0];
            }
        }

        public static string CurrentLedgerId => CurrentLedger?.id ?? "";

        public static Ledger FindLedger(string id)
            => string.IsNullOrEmpty(id) || id == AllLedgers ? null : Ledgers.FirstOrDefault(l => l.id == id);

        /// <summary>账本显示名（找不到就给个不会崩的兜底名）</summary>
        public static string LedgerName(string id)
        {
            if (id == AllLedgers) return "全部账本";
            var l = FindLedger(id);
            return l == null ? "未命名账本" : l.name;
        }

        /// <summary>账本在列表里的序号（用来决定默认标识色）</summary>
        public static int LedgerIndex(string id)
        {
            var ls = Ledgers;
            for (int i = 0; i < ls.Count; i++) if (ls[i].id == id) return i;
            return -1;
        }

        /// <summary>账本的标识色：用户设了就用用户的，没设就按顺序给一个</summary>
        public static string LedgerColorOf(Ledger l)
        {
            if (l == null) return LedgerColors[0];
            var c = (l.color ?? "").Trim();
            if (IsHexColor(c)) return c;
            int i = LedgerIndex(l.id);
            return LedgerColors[((i < 0 ? 0 : i) % LedgerColors.Length + LedgerColors.Length) % LedgerColors.Length];
        }

        /// <summary>#RGB / #RRGGBB 才算合法颜色，别的（空、乱填）一律当没设</summary>
        public static bool IsHexColor(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var t = s.Trim();
            if (t.Length != 4 && t.Length != 7) return false;
            if (t[0] != '#') return false;
            for (int i = 1; i < t.Length; i++)
            {
                char c = t[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>下一个自动标识色（新建账本时先给用户看一眼）</summary>
        public static string NextLedgerColor() => LedgerColors[Ledgers.Count % LedgerColors.Length];

        /// <summary>可选的标识色（账本管理窗口拿它画色块）</summary>
        public static IReadOnlyList<string> LedgerPalette => LedgerColors;

        /// <summary>切换当前账本。切成功会存盘并通知所有页面刷新。</summary>
        public static bool SetCurrentLedger(string id)
        {
            LastError = null;
            var l = FindLedger(id);
            if (l == null) { LastError = "找不到这个账本。"; return false; }
            if (Data.settings.currentLedger == l.id) return true;   // 已经是它了，不用白存一次盘

            Data.settings.currentLedger = l.id;
            Save();
            RaiseChanged();
            return true;
        }

        /// <summary>新建账本。makeCurrent=true 时建完直接切过去。</summary>
        public static Ledger AddLedger(string name, string color = null, string note = null, bool makeCurrent = false)
        {
            LastError = null;
            var l = new Ledger
            {
                name = string.IsNullOrWhiteSpace(name) ? "新账本" : name.Trim(),
                color = IsHexColor(color) ? color.Trim() : NextLedgerColor(),
                note = (note ?? "").Trim(),
                createdAt = Util.NowStamp()
            };
            Ledgers.Add(l);
            if (makeCurrent) Data.settings.currentLedger = l.id;
            Save();
            RaiseChanged();
            return l;
        }

        /// <summary>改账本的名称/颜色/备注/归档。名称留空会被拒绝（不然界面上就没法认了）。</summary>
        public static bool UpdateLedger(string id, string name, string color, string note, bool archived)
        {
            LastError = null;
            var l = FindLedger(id);
            if (l == null) { LastError = "找不到这个账本。"; return false; }
            if (string.IsNullOrWhiteSpace(name)) { LastError = "账本名称不能空着。"; return false; }

            l.name = name.Trim();
            l.color = IsHexColor(color) ? color.Trim() : "";
            l.note = (note ?? "").Trim();
            l.archived = archived;
            Save();
            RaiseChanged();
            return true;
        }

        /// <summary>归档 / 取消归档。归档只是收起来，记录一笔都不动。</summary>
        public static bool ArchiveLedger(string id, bool archived)
        {
            LastError = null;
            var l = FindLedger(id);
            if (l == null) { LastError = "找不到这个账本。"; return false; }
            if (l.archived == archived) return true;
            l.archived = archived;
            Save();
            RaiseChanged();
            return true;
        }

        /// <summary>这个账本里有几笔记录</summary>
        public static int LedgerCount(string id)
            => string.IsNullOrEmpty(id) || id == AllLedgers ? 0 : Data.transactions.Count(x => (x.ledgerId ?? "") == id);

        /// <summary>这个账本的收支概况（分）</summary>
        public static (long income, long expense, int count) LedgerSum(string id, string from = null, string to = null)
        {
            var tx = QueryScoped(id, from: from, to: to).ToList();
            return (tx.Where(t => t.type == "income").Sum(t => t.amount),
                    tx.Where(t => t.type == "expense").Sum(t => t.amount),
                    tx.Count);
        }

        /// <summary>把一个账本的记录整体转到另一个账本，返回转了几笔。记录本身一条不动，只换归属。</summary>
        public static int MoveRecords(string fromId, string toId)
        {
            LastError = null;
            if (FindLedger(fromId) == null || FindLedger(toId) == null) { LastError = "账本对不上，没转。"; return 0; }
            if (fromId == toId) { LastError = "转出和转入是同一个账本。"; return 0; }

            int n = 0;
            foreach (var t in Data.transactions.Where(x => (x.ledgerId ?? "") == fromId))
            {
                t.ledgerId = toId;
                n++;
            }
            if (n > 0) { Save(); RaiseChanged(); }
            return n;
        }

        /// <summary>
        /// 删账本。默认绝不允许把记录一起删掉：
        ///   · 账本里没记录 → 直接删；
        ///   · 账本里有记录 → 必须给一个接手记录的账本（moveToId），记录转过去、账本才删；
        ///   · 真要把记录也一起删掉，得显式传 deleteRecords=true（界面上是二次确认的那个勾）。
        /// 最后一个账本永远删不掉——不然记录会全部无家可归。
        /// </summary>
        public static bool DeleteLedger(string id, string moveToId = null, bool deleteRecords = false)
        {
            LastError = null;
            var l = FindLedger(id);
            if (l == null) { LastError = "找不到这个账本。"; return false; }
            if (Ledgers.Count <= 1) { LastError = "至少得留一个账本，这个删不了。"; return false; }

            int n = LedgerCount(id);
            if (n > 0 && !deleteRecords)
            {
                var to = FindLedger(moveToId);
                if (to == null || to.id == id)
                {
                    LastError = "这个账本里还有 " + n + " 笔记录，删之前得先选一个接收它们的账本。";
                    return false;
                }
                foreach (var t in Data.transactions.Where(x => (x.ledgerId ?? "") == id)) t.ledgerId = to.id;
            }
            else if (n > 0)
            {
                Data.transactions.RemoveAll(x => (x.ledgerId ?? "") == id);
            }

            Ledgers.Remove(l);

            // 删掉的正好是当前账本：换到接手记录的那个，没有就换到第一个
            if (Data.settings.currentLedger == id)
            {
                var to = FindLedger(moveToId) ?? Ledgers[0];
                Data.settings.currentLedger = to.id;
            }
            Save();
            RaiseChanged();
            return true;
        }

        /// <summary>当前账本的记录（按时间倒序，和 Query 的顺序一致）</summary>
        public static IEnumerable<Transaction> Scoped(string ledgerId = null)
            => QueryScoped(ledgerId);

        /// <summary>
        /// 按账本过滤的查询：参数和 Query 一模一样，只是多一层账本。
        /// ledgerId 传 null/空 = 当前账本，传 AllLedgers("*") = 全部账本。
        /// </summary>
        public static IEnumerable<Transaction> QueryScoped(string ledgerId = null, string from = null, string to = null,
            string keyword = null, string type = null, string category = null, string account = null, string month = null)
        {
            var id = string.IsNullOrEmpty(ledgerId) ? CurrentLedgerId : ledgerId;
            var q = Query(from, to, keyword, type, category, account, month);
            if (id == AllLedgers) return q;
            return q.Where(x => (x.ledgerId ?? "") == id);
        }

        /// <summary>一个账本在某个区间里的收支概况，界面上横向对比用这个</summary>
        public class LedgerStat
        {
            public string id { get; set; } = "";
            public string name { get; set; } = "";
            public string color { get; set; } = "";
            public string note { get; set; } = "";
            public bool archived { get; set; }
            public bool current { get; set; }
            public int count { get; set; }
            public long income { get; set; }
            public long expense { get; set; }
            public long transfer { get; set; }

            /// <summary>盈亏 = 收入 − 支出（转账不算收支）</summary>
            public long net => income - expense;

            public string IncomeText => Util.Money(income);
            public string ExpenseText => Util.Money(expense);
            public string NetText => Util.Money(net, true);
        }

        /// <summary>
        /// 每个账本在 [from, to] 区间里的收入 / 支出 / 盈亏。
        /// 默认不含归档账本（除非它就是当前账本，免得切过去之后对比表里找不到自己）。
        /// </summary>
        public static List<LedgerStat> LedgerStats(string from = null, string to = null, bool includeArchived = false)
        {
            var list = new List<LedgerStat>();
            string cur = CurrentLedgerId;
            foreach (var l in Ledgers)
            {
                if (l.archived && !includeArchived && l.id != cur) continue;
                var tx = QueryScoped(l.id, from: from, to: to).ToList();
                list.Add(new LedgerStat
                {
                    id = l.id,
                    name = l.name,
                    color = LedgerColorOf(l),
                    note = l.note,
                    archived = l.archived,
                    current = l.id == cur,
                    count = tx.Count,
                    income = tx.Where(t => t.type == "income").Sum(t => t.amount),
                    expense = tx.Where(t => t.type == "expense").Sum(t => t.amount),
                    transfer = tx.Where(t => t.type == "transfer").Sum(t => t.amount)
                });
            }
            return list;
        }
    }
}