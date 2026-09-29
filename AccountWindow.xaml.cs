using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    // ==================== 分组树的行模型 ====================
    // 就是给界面绑的一层壳，数据本身还是 Models.cs 里的 Account。
    // 余额、组内支出这类东西在建立的时候就一次性算好，
    // 模板里只有绑定，不写任何逻辑，别让 XAML 里出现计算。

    /// <summary>一个账户在树里的小方块（名字 + 余额 + 这期间花了多少）</summary>
    public class AccountChip
    {
        public Account A { get; set; }
        public string Name { get; set; } = "";
        public string BadgeText { get; set; } = "";     // 「他人」这种小标记，空就不显示
        public string BalanceText { get; set; } = "";
        public string OutText { get; set; } = "";       // 「· 花 ¥88.88」，这个账户在口径内花掉的钱
        public string Tip { get; set; } = "";
        public Style ChipStyle { get; set; }            // 选中的那个用 ChipOn 亮起来

        public Visibility OutVis => string.IsNullOrEmpty(OutText) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>组里的一层（现金 / 电子钱包 / 银行卡 / 信用卡 / 投资 / 其他）</summary>
    public class KindLayer
    {
        public string Label { get; set; } = "";
        public string SumText { get; set; } = "";
        public List<AccountChip> Items { get; set; } = new List<AccountChip>();
    }

    /// <summary>一个分组（自己 / 家人 / 他人 / 工作 …）</summary>
    public class GroupBlock
    {
        public string Name { get; set; } = "";
        public string CountText { get; set; } = "";
        public string OutText { get; set; } = "";
        public string InText { get; set; } = "";
        public string BalanceText { get; set; } = "";
        public string OwnerText { get; set; } = "";
        public string Note { get; set; } = "";
        public bool IsOthers { get; set; }
        public List<KindLayer> Kinds { get; set; } = new List<KindLayer>();

        public Visibility OthersVis => IsOthers ? Visibility.Visible : Visibility.Collapsed;
        public Visibility OwnerVis => string.IsNullOrEmpty(OwnerText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoteVis => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;
        /// <summary>「未归类」那种没有账户的块不显示「加账户」按钮</summary>
        public Visibility AddVis => Kinds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 账户页：上面是总览，左边按「分组 → 类型」两层摆账户，
    /// 中间编辑选中的那个账户，右边是「谁欠我多少」。
    /// 从设置页的「新增账户 / 修改账户」进来时，还是同一个窗口，只是表单落在对应的账户上。
    /// </summary>
    public partial class AccountWindow : Window
    {
        /// <summary>统计口径里「全部时间」那一项，其余项都是 yyyy-MM 或者「本月」</summary>
        private const string AllPeriod = "全部时间";

        private Account _target;        // 正在改的真实对象（用的就是 Store.Data.accounts 里那一个）；null = 新增
        private string _selectedId = "";// 树上高亮的那个
        private bool _dirty;            // 改过东西 → 关窗时回 true，调用方好刷新

        public AccountWindow(Account src)
        {
            InitializeComponent();
            BuildPeriods();
            BuildGroupChips();
            LoadForm(src);
            ApplyScopeHint();
            ReloadTree();
            ReloadDebts();
            Store.Changed += OnStoreChanged;
            Closed += (s, e) => Store.Changed -= OnStoreChanged;
            Loaded += (s, e) => ClampToScreen();
        }

        /// <summary>
        /// 兜一下窗口位置：窗口是按「主窗口居中」摆的，主窗口要是在屏幕边上
        /// （或者被拖到屏幕外），居中之后这张卡片就会有一半跑到屏幕外面去，
        /// 右边的「替谁花了多少」和「保存」就点不到了。这里只把它拉回工作区，不动大小位置以外的任何东西。
        /// </summary>
        private void ClampToScreen()
        {
            var wa = SystemParameters.WorkArea;
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            if (Width > wa.Width) Width = wa.Width - 16;
            if (Height > wa.Height) Height = wa.Height - 16;
            if (Left + Width > wa.Right) Left = wa.Right - Width;
            if (Top + Height > wa.Bottom) Top = wa.Bottom - Height;
            if (Left < wa.Left) Left = wa.Left;
            if (Top < wa.Top) Top = wa.Top;
        }

        /// <summary>别的页面想直接开账户管理时的入口（不做任何编辑）</summary>
        public static void OpenManager(Window owner)
        {
            var w = new AccountWindow(null) { Owner = owner };
            w.ShowDialog();
        }

        // ==================== 统计口径 ====================

        private void BuildPeriods()
        {
            cmbPeriod.Items.Clear();
            cmbPeriod.Items.Add(AllPeriod);
            foreach (var m in Util.LastMonths(6).Reverse())
                cmbPeriod.Items.Add(m == Util.ThisMonth() ? "本月" : m);
            cmbPeriod.SelectedItem = "本月";
        }

        /// <summary>当前口径对应的 yyyy-MM；「全部时间」返回 null</summary>
        private string CurrentPeriod()
        {
            var s = cmbPeriod.SelectedItem as string;
            if (string.IsNullOrEmpty(s) || s == AllPeriod) return null;
            if (s == "本月") return Util.ThisMonth();
            return s;
        }

        private static bool InPeriod(string date, string ym)
        {
            if (string.IsNullOrEmpty(ym)) return true;
            if (string.IsNullOrEmpty(date)) return false;
            var (f, t) = Util.MonthRange(ym);
            return string.CompareOrdinal(date, f) >= 0 && string.CompareOrdinal(date, t) <= 0;
        }

        private void Period_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (listGroups == null) return;     // 构造还没走完，控件还没生出来
            ReloadTree();
        }

        // ==================== 数据变了跟着刷 ====================

        private void OnStoreChanged()
        {
            BuildGroupChips();
            ApplyScopeHint();
            ReloadTree();
            ReloadDebts();
        }

        /// <summary>
        /// 只有一个账本时不用解释口径；多人共用一个程序时得说清楚，
        /// 不然「账户页的支出」跟「明细页的支出」对不上会让人以为算错了。
        /// </summary>
        private void ApplyScopeHint()
        {
            int ledgers = Store.Data.ledgers != null ? Store.Data.ledgers.Count : 1;
            txtSub.Text = ledgers > 1
                ? "按分组摆开，组里再按类型归拢。支出/收入按当前账本算（跟明细页一致），"
                + "余额和「他人账户」是把所有账本合起来算的。"
                : "按分组摆开，组里再按类型归拢。点左边任意一个账户就能改它。";
        }

        // ==================== 分组树 ====================

        private static void Bump(Dictionary<string, long> map, string key, long v)
            => map[key] = map.TryGetValue(key, out var old) ? old + v : v;

        private void ReloadTree()
        {
            var period = CurrentPeriod();
            var accounts = Store.Data.accounts.Where(a => !a.archived).ToList();

            // 账户名 → 分组。记录是按账户名挂的，所以这里也按名字对。
            var groupOfName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in accounts)
                if (!string.IsNullOrEmpty(a.name)) groupOfName[a.name] = AccountGroups.Of(a);

            // 这个口径下，各分组、各账户的支出/收入。转账不算收支（跟别处口径一致）。
            var outSum = new Dictionary<string, long>(StringComparer.Ordinal);
            var inSum = new Dictionary<string, long>(StringComparer.Ordinal);
            var accOut = new Dictionary<string, long>(StringComparer.Ordinal);
            long orphanOut = 0, orphanIn = 0;
            foreach (var t in Advances.ScopedTx())
            {
                if (t == null || !InPeriod(t.date, period)) continue;
                long flow = Util.CashFlow(t);
                if (flow == 0) continue;

                string g = null;
                if (!string.IsNullOrEmpty(t.account)) groupOfName.TryGetValue(t.account, out g);
                if (string.IsNullOrEmpty(g))
                {
                    // 账户被删掉的历史记录：单独归一堆，免得「各组加起来」对不上总数
                    if (flow < 0) orphanOut += -flow; else orphanIn += flow;
                    continue;
                }
                if (flow < 0)
                {
                    Bump(outSum, g, -flow);
                    if (!string.IsNullOrEmpty(t.account)) Bump(accOut, t.account, -flow);
                }
                else Bump(inSum, g, flow);
            }

            var blocks = new List<GroupBlock>();
            foreach (var grp in accounts.GroupBy(AccountGroups.Of)
                         .OrderBy(g => AccountGroups.Rank(g.Key))
                         .ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                var list = grp.ToList();
                var block = new GroupBlock
                {
                    Name = grp.Key,
                    CountText = list.Count + " 个账户",
                    OutText = Util.Yuan(outSum.TryGetValue(grp.Key, out var o) ? o : 0),
                    InText = Util.Yuan(inSum.TryGetValue(grp.Key, out var i) ? i : 0),
                    BalanceText = Bal(list.Sum(a => Store.BalanceOf(a.name))),
                    IsOthers = list.Any(a => a.isOthers)
                };

                var owners = list.Select(a => (a.owner ?? "").Trim())
                                 .Where(x => x.Length > 0).Distinct().ToList();
                if (owners.Count > 0) block.OwnerText = "归属：" + string.Join("、", owners);

                foreach (var layer in list.GroupBy(a => AccountKindInfo.Normalize(a.kind))
                             .OrderBy(g => AccountKindInfo.Rank(g.Key)))
                {
                    var kind = new KindLayer { Label = AccountKindInfo.Label(layer.Key) };
                    long sum = 0;
                    foreach (var a in layer.OrderByDescending(a => Store.BalanceOf(a.name)))
                    {
                        long bal = Store.BalanceOf(a.name);
                        long spent = accOut.TryGetValue(a.name ?? "", out var so) ? so : 0;
                        sum += bal;
                        kind.Items.Add(new AccountChip
                        {
                            A = a,
                            Name = string.IsNullOrEmpty(a.name) ? "(没名字)" : a.name,
                            BadgeText = a.isOthers ? "他人" : "",
                            BalanceText = Bal(bal),
                            OutText = spent > 0 ? "· 花 " + Util.Yuan(spent) : "",
                            ChipStyle = Res(a.id == _selectedId ? "ChipOn" : "Chip"),
                            Tip = ChipTip(a, bal, spent)
                        });
                    }
                    kind.SumText = Bal(sum) + " · " + kind.Items.Count + " 个";
                    block.Kinds.Add(kind);
                }
                blocks.Add(block);
            }

            if (orphanOut != 0 || orphanIn != 0)
            {
                blocks.Add(new GroupBlock
                {
                    Name = "未归类",
                    CountText = "账户已经不在了",
                    OutText = Util.Yuan(orphanOut),
                    InText = Util.Yuan(orphanIn),
                    BalanceText = Util.Yuan(0),
                    Note = "这些记录的账户名现在不在账户列表里（多半是账户被删了）。单独摆出来，"
                         + "免得各组数字加起来跟总数对不上。"
                });
            }

            listGroups.ItemsSource = blocks;
            txtEmptyTree.Visibility = accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshSummary(accounts);
        }

        /// <summary>
        /// 账户余额的显示口径：欠着的（信用卡、别人的账户）必须看得见负号。
        /// Util.Money 是全程序通用的「不带符号」写法（报表里一直这么用），
        /// 但账户页要回答「这个账户还剩多少」，信用卡 -500 显示成 500 会彻底看反，
        /// 所以只在本页自己补一个负号，不去动公共口径。
        /// </summary>
        private static string Bal(long cents)
            => (cents < 0 ? "-" : "") + Util.Yuan(Math.Abs(cents));

        private static string ChipTip(Account a, long bal, long spent)
        {
            var parts = new List<string> { a.name + "　余额 " + Bal(bal) };
            parts.Add("分组：" + AccountGroups.Of(a) + "　类型：" + AccountKindInfo.Label(a.kind));
            if (!string.IsNullOrWhiteSpace(a.owner)) parts.Add("归属：" + a.owner.Trim());
            if (a.isOthers) parts.Add("别人的账户");
            if (!a.includeInAssets) parts.Add("不计入净资产");
            parts.Add("这个口径下从它花了 " + Util.Yuan(spent));
            parts.Add("点一下就能改它");
            return string.Join("\n", parts);
        }

        private void RefreshSummary(List<Account> accounts = null)
        {
            accounts ??= Store.Data.accounts.Where(a => !a.archived).ToList();
            long own = accounts.Where(a => !a.isOthers && a.includeInAssets).Sum(a => Store.BalanceOf(a.name));
            long others = accounts.Where(a => a.isOthers).Sum(a => Store.BalanceOf(a.name));
            txtOwnAssets.Text = Bal(own);
            txtOtherAssets.Text = Bal(others);
            txtDebtTotal.Text = Bal(Advances.UnpaidTotal());
        }

        /// <summary>点树上的账户 → 载入右边的表单</summary>
        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is AccountChip chip) || chip.A == null) return;
            if (chip.A == _target) return;
            if (!ConfirmDiscard()) return;
            LoadForm(chip.A);
            BuildGroupChips();
            ReloadTree();
            txtTip.Text = "改完记得点「保存」。";
            txtTip.Foreground = (Brush)FindResource("SubBrush");
        }

        // ==================== 表单 ====================

        private void LoadForm(Account a)
        {
            _target = a;
            _selectedId = a?.id ?? "";
            bool isNew = a == null;

            txtHeader.Text = isNew ? "新增账户" : "修改账户";
            Title = isNew ? "新增账户" : "修改账户";
            lblForm.Text = isNew ? "新增账户" : "修改账户";

            txtName.Text = a?.name ?? "";
            txtGroup.Text = a == null ? AccountGroups.Self : AccountGroups.Of(a);
            txtOwner.Text = a?.owner ?? "";
            chkOthers.IsChecked = a != null && a.isOthers;
            cmbKind.SelectedIndex = AccountKindInfo.IndexOf(a?.kind ?? "cash");
            txtInitial.Text = a != null && a.initialBalance != 0 ? Util.Money(a.initialBalance) : "";
            chkAssets.IsChecked = a == null || a.includeInAssets;
            btnDelete.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

            HighlightGroup();
            txtTip.Text = isNew ? "填好点「保存」，它就进左边对应的分组里。" : "改完记得点「保存」。";
            txtTip.Foreground = (Brush)FindResource("SubBrush");
        }

        /// <summary>分组最多就是一句「自己」，空着不写也算自己</summary>
        private string NormalizeGroup()
        {
            var g = (txtGroup.Text ?? "").Trim();
            return g.Length == 0 ? AccountGroups.Self : g;
        }

        private void BuildGroupChips()
        {
            if (panelGroups == null) return;
            panelGroups.Children.Clear();
            foreach (var g in AccountGroups.Known())
            {
                var btn = new Button
                {
                    Content = g,
                    Tag = g,
                    Style = Res("Chip"),
                    FontSize = 12,
                    Padding = new Thickness(10, 5, 10, 5)
                };
                btn.Click += (s, e) => txtGroup.Text = (string)((Button)s).Tag;
                panelGroups.Children.Add(btn);
            }
            HighlightGroup();
        }

        /// <summary>把跟输入框里那个分组名一样的按钮点亮</summary>
        private void HighlightGroup()
        {
            var cur = NormalizeGroup();
            foreach (var c in panelGroups.Children.OfType<Button>())
                c.Style = Res((string)c.Tag == cur ? "ChipOn" : "Chip");
        }

        private void Group_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (panelGroups == null) return;
            HighlightGroup();
        }

        /// <summary>表单上有没有还没保存的改动（换账户前问一句，别让人白填）</summary>
        private bool FormDirty()
        {
            string name = (txtName.Text ?? "").Trim();
            string group = NormalizeGroup();
            string owner = (txtOwner.Text ?? "").Trim();
            bool others = chkOthers.IsChecked == true;
            long init = Util.ToCents(txtInitial.Text);
            bool assets = chkAssets.IsChecked == true;
            string kind = AccountKindInfo.At(cmbKind.SelectedIndex);

            var t = _target;
            if (t == null)
                return name.Length > 0 || owner.Length > 0 || others || init != 0 || !assets
                       || group != AccountGroups.Self || kind != "cash";

            return name != (t.name ?? "") || group != AccountGroups.Of(t) || owner != (t.owner ?? "")
                   || others != t.isOthers || init != t.initialBalance || assets != t.includeInAssets
                   || kind != AccountKindInfo.Normalize(t.kind);
        }

        private bool ConfirmDiscard()
        {
            if (!FormDirty()) return true;
            return MessageBox.Show("表单里还有没保存的改动，继续的话就丢了。要继续吗？", "还没保存",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        // ==================== 增 / 改 / 删 ====================

        private void Add_Click(object sender, RoutedEventArgs e) => StartNew(AccountGroups.Self);

        private void AddToGroup_Click(object sender, RoutedEventArgs e)
        {
            var g = (sender as Button)?.Tag as GroupBlock;
            StartNew(string.IsNullOrWhiteSpace(g?.Name) ? AccountGroups.Self : g.Name);
        }

        /// <summary>把表单清成「新增」，分组预填成指定的那个</summary>
        private void StartNew(string group)
        {
            if (!ConfirmDiscard()) return;
            LoadForm(null);
            txtGroup.Text = group;
            HighlightGroup();
            ReloadTree();
            txtName.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var name = (txtName.Text ?? "").Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("账户名不能是空的。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtName.Focus();
                return;
            }
            if (Store.Data.accounts.Any(a => a != _target && !a.archived && a.name == name))
            {
                MessageBox.Show($"已经有一个叫「{name}」的账户了。", "重名了", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isNew = _target == null;
            string oldName = isNew ? "" : (_target.name ?? "");
            var acc = _target ?? new Account();
            acc.name = name;
            acc.group = NormalizeGroup();
            acc.owner = (txtOwner.Text ?? "").Trim();
            acc.isOthers = chkOthers.IsChecked == true;
            acc.kind = AccountKindInfo.At(cmbKind.SelectedIndex);
            acc.initialBalance = Util.ToCents(txtInitial.Text);
            acc.includeInAssets = chkAssets.IsChecked == true;

            if (isNew) Store.Data.accounts.Add(acc);
            _target = acc;
            _selectedId = acc.id;

            // 改了名字，历史记录里跟着改，不然余额就断了
            if (!isNew && oldName.Length > 0 && oldName != name)
            {
                foreach (var t in Store.Data.transactions)
                {
                    if (t.account == oldName) t.account = name;
                    if (t.toAccount == oldName) t.toAccount = name;
                }
            }

            Store.Save();
            _dirty = true;
            Store.RaiseChanged();

            BuildGroupChips();
            LoadForm(_target);
            ReloadTree();
            ReloadDebts();
            txtTip.Text = isNew
                ? $"已新增「{name}」，放在「{acc.group}」组里。"
                : $"已保存「{name}」。";
            txtTip.Foreground = (Brush)FindResource("IncomeBrush");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var a = _target;
            if (a == null) return;

            int used = Store.Data.transactions.Count(t => t.account == a.name || t.toAccount == a.name);
            var msg = $"要删掉账户「{a.name}」吗？";
            if (used > 0)
                msg += $"\n\n有 {used} 笔记录在用它。删的是账户本身，那些记录还在，"
                     + "只是账户名不再出现在账户页里（会归到「未归类」那一堆）。";
            if (MessageBox.Show(msg, "删除账户", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            Store.Data.accounts.Remove(a);
            Store.Save();
            _dirty = true;
            Store.RaiseChanged();

            LoadForm(null);
            BuildGroupChips();
            ReloadTree();
            ReloadDebts();
            txtTip.Text = $"已删除「{a.name}」。";
            txtTip.Foreground = (Brush)FindResource("SubBrush");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            // 调用方（设置页）看的是 ShowDialog 的返回值：改过东西才回 true，好让它刷新列表
            if (_dirty && DialogResult == null)
            {
                try { DialogResult = true; } catch { }
            }
            Store.Changed -= OnStoreChanged;
            base.OnClosing(e);
        }

        // ==================== 谁欠我多少 ====================

        private void ReloadDebts()
        {
            var rows = Advances.ByPerson();
            listDebts.ItemsSource = rows;
            long total = rows.Sum(r => r.Unpaid);

            txtDebtSum.Text = Util.Yuan(total);
            txtDebtTotal.Text = Bal(total);

            long spent = rows.Sum(r => r.Spent);
            int owing = rows.Count(r => r.HasDebt);
            txtSpentSum.Text = rows.Count == 0
                ? "还没有「替谁花的」记录"
                : "一共替别人花了 " + Util.Yuan(spent) + "，涉及 " + rows.Count + " 个人"
                  + (owing > 0 ? "（其中 " + owing + " 个人还欠着）" : "（都还清了）");

            txtNoDebt.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>一个人还清了：把他所有没还的垫付一起标掉</summary>
        private void Settle_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is DebtRow row)) return;
            var msg = $"「{row.Person}」还没还的 {row.UnpaidCount} 笔垫付（合计 {Util.Yuan(row.Unpaid)}）都算还清了？\n\n"
                    + "标记之后会从欠款里扣掉，这一行右边还能「撤销已还」。";
            if (MessageBox.Show(msg, "还清了", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;

            int n = Advances.SettlePerson(row.Person, true);
            ReloadDebts();
            ReloadTree();
            txtTip.Text = n > 0
                ? $"「{row.Person}」的 {n} 笔垫付已标成还清。钱要是收进了哪个账户，去「记一笔」记条收入，账才对得上。"
                : "没找到可标记的记录。";
            txtTip.Foreground = (Brush)FindResource("IncomeBrush");
        }

        /// <summary>撤销「已还」——点错了能退回来</summary>
        private void Unsettle_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is DebtRow row)) return;
            if (MessageBox.Show($"把「{row.Person}」这 {row.RepaidCount} 笔（{Util.Yuan(row.Repaid)}）重新算成没还？", "撤销",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            int n = Advances.SettlePerson(row.Person, false);
            ReloadDebts();
            ReloadTree();
            txtTip.Text = n > 0 ? $"「{row.Person}」的 {n} 笔又回到没还里了。" : "没找到可撤销的记录。";
            txtTip.Foreground = (Brush)FindResource("SubBrush");
        }

        // ==================== 小工具 ====================

        /// <summary>资源里的样式；万一找不到也别返回 null（空样式会让按钮变回系统样）</summary>
        private static Style Res(string key)
            => Application.Current?.TryFindResource(key) as Style ?? new Style(typeof(Button));
    }
}