using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MoneyTracker
{
    /// <summary>表格里一行的显示模型（金额已经转成元、颜色按收支给好了）</summary>
    public class TxRow
    {
        public Transaction Tx { get; set; }
        public string Date => Tx.date;
        public string TypeText => Util.TypeLabel(Tx.type);
        public string AmountText => (Tx.type == "income" ? "+" : Tx.type == "transfer" ? "→" : "-") + Util.Money(Tx.amount);
        public Brush AmountBrush => (Brush)Application.Current.FindResource(
            Tx.type == "income" ? "IncomeBrush" : Tx.type == "transfer" ? "SubBrush" : "ExpenseBrush");
        public string Category => Tx.category;
        /// <summary>二级分类单独占一列，没细分就让它空着，别写"无"占地方</summary>
        public string Sub => Tx.subcategory;
        public string Account => Tx.type == "transfer" ? $"{Tx.account} → {Tx.toAccount}" : Tx.account;
        /// <summary>垫付那一列：不是垫付就空着，垫付的写「垫 · 张三」，还了的再标一下</summary>
        public string Advance => Advances.Label(Tx);
        public Brush AdvanceBrush => (Brush)Application.Current.FindResource(
            Tx.isReimbursed ? "SubBrush" : "ExpenseBrush");
        public string AdvanceTip => !Tx.isAdvance ? "这一笔不是垫付"
            : (Tx.isReimbursed ? "替「" + Advances.PersonOf(Tx) + "」垫的，已经还了"
                               : "替「" + Advances.PersonOf(Tx) + "」垫的，还没还（右键可以标已还）");
        public string Merchant => Tx.merchant;
        public string Note => Tx.note;
        public string SourceText => Exporters.SourceText(Tx.source);
    }

    public partial class ListPage : UserControl, IRefreshable
    {
        private bool _ready;
        private List<Transaction> _current = new List<Transaction>();

        public ListPage()
        {
            InitializeComponent();
            BuildFilters();
            _ready = true;
            Load();
        }

        public void Refresh()
        {
            _ready = false;
            BuildFilters();
            _ready = true;
            Load();
        }

        // ==================== 筛选 ====================

        private void BuildFilters()
        {
            var keepMonth = cmbMonth.SelectedItem as string;
            var keepCat = cmbCat.SelectedItem as string;
            var keepSub = cmbSub.SelectedItem as string;
            var keepAcc = cmbAcc.SelectedItem as string;

            cmbMonth.Items.Clear();
            cmbMonth.Items.Add("全部时间");
            foreach (var m in Util.LastMonths(18)) cmbMonth.Items.Add(m);
            cmbMonth.SelectedItem = keepMonth != null && cmbMonth.Items.Contains(keepMonth) ? keepMonth : Util.ThisMonth();
            if (cmbMonth.SelectedItem == null) cmbMonth.SelectedIndex = 0;

            cmbCat.Items.Clear();
            cmbCat.Items.Add("全部分类");
            foreach (var c in Store.Scoped().Select(t => t.category)
                         .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c))
                cmbCat.Items.Add(c);
            cmbCat.SelectedItem = keepCat != null && cmbCat.Items.Contains(keepCat) ? keepCat : "全部分类";

            BuildSubFilter(keepSub);   // 二级分类的选项跟着一级分类走

            cmbAcc.Items.Clear();
            cmbAcc.Items.Add("全部账户");
            foreach (var a in Store.Data.accounts.Where(a => !a.archived)) cmbAcc.Items.Add(a.name);
            cmbAcc.SelectedItem = keepAcc != null && cmbAcc.Items.Contains(keepAcc) ? keepAcc : "全部账户";

            if (cmbAdvance.SelectedIndex < 0) cmbAdvance.SelectedIndex = 0;
            if (cmbType.SelectedIndex < 0) cmbType.SelectedIndex = 0;
        }

        /// <summary>二级分类下拉：选了一级分类就只列它下面的（含用户自己写过的），没选就列全部。
        /// want 传的是想留住的旧选择，没得留就回到"全部"。</summary>
        private void BuildSubFilter(string want)
        {
            bool ready = _ready;
            _ready = false;

            var cat = cmbCat.SelectedItem as string;
            if (cat == "全部分类") cat = null;

            cmbSub.Items.Clear();
            cmbSub.Items.Add("全部二级分类");
            foreach (var s in Store.Scoped()
                         .Where(t => (cat == null || t.category == cat) && !string.IsNullOrEmpty(t.subcategory))
                         .Select(t => t.subcategory).Distinct().OrderBy(s => s, StringComparer.Ordinal))
                cmbSub.Items.Add(s);

            cmbSub.SelectedItem = want != null && cmbSub.Items.Contains(want) ? want : "全部二级分类";
            _ready = ready;
        }

        /// <summary>一级分类换了，二级分类的候选也得跟着换（选过的那项要是还在就留着）</summary>
        private void Cat_Changed(object sender, EventArgs e)
        {
            if (!_ready) return;
            BuildSubFilter(cmbSub.SelectedItem as string);
            Load();
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            if (!_ready) return;
            Load();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _ready = false;
            cmbMonth.SelectedItem = "全部时间";
            cmbType.SelectedIndex = 0;
            cmbCat.SelectedItem = "全部分类";
            BuildSubFilter(null);            // 一级回到"全部"，二级也得跟着列全部
            cmbAcc.SelectedItem = "全部账户";
            cmbAdvance.SelectedIndex = 0;
            txtKey.Text = "";
            _ready = true;
            Load();
        }

        private void ThisMonth_Click(object sender, RoutedEventArgs e)
        {
            _ready = false;
            cmbMonth.SelectedItem = Util.ThisMonth();
            _ready = true;
            Load();
        }

        private string TypeFilter => cmbType.SelectedIndex switch
        {
            1 => "expense",
            2 => "income",
            3 => "transfer",
            _ => null
        };

        private void Load()
        {
            var month = cmbMonth.SelectedItem as string;
            if (month == "全部时间") month = null;
            var cat = cmbCat.SelectedItem as string;
            if (cat == "全部分类") cat = null;
            var acc = cmbAcc.SelectedItem as string;
            if (acc == "全部账户") acc = null;
            var sub = cmbSub.SelectedItem as string;
            if (sub == "全部二级分类") sub = null;

            // 只查当前账本：多人共用一个程序时，明细页看到的必须是「我」的账
            _current = Store.QueryScoped(month: month, type: TypeFilter, category: cat, account: acc,
                                   keyword: txtKey.Text).ToList();
            // 查询不认识二级分类，就在结果上再筛一道——反正量不大
            if (sub != null) _current = _current.Where(t => t.subcategory == sub).ToList();
            // 垫付：全部 / 只看垫付（含已还）/ 只看未还 / 只看已还
            switch (cmbAdvance.SelectedIndex)
            {
                case 1: _current = _current.Where(t => t.isAdvance).ToList(); break;
                case 2: _current = _current.Where(t => t.isAdvance && !t.isReimbursed).ToList(); break;
                case 3: _current = _current.Where(t => t.isAdvance && t.isReimbursed).ToList(); break;
            }

            grid.ItemsSource = _current.Select(t => new TxRow { Tx = t }).ToList();

            long outSum = _current.Where(t => t.type == "expense").Sum(t => t.amount);
            long inSum = _current.Where(t => t.type == "income").Sum(t => t.amount);
            txtSumOut.Text = Util.Money(outSum);
            txtSumIn.Text = Util.Money(inSum);
            txtSumNet.Text = Util.Money(inSum - outSum);
            txtSumNet.Foreground = (Brush)FindResource(inSum - outSum >= 0 ? "TextBrush" : "ExpenseBrush");
            // 这堆记录里别人还欠我多少（只算标了垫付、还没还的支出）
            txtSumDebt.Text = Util.Money(_current.Where(Advances.CountsAsDebt).Sum(t => t.amount));
            txtCount.Text = $"共 {_current.Count} 笔";
            btnDelSel.IsEnabled = false;
        }

        // ==================== 行操作 ====================

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnDelSel.IsEnabled = grid.SelectedItems.Count > 0;
        }

        private List<Transaction> SelectedTxs()
            => grid.SelectedItems.Cast<TxRow>().Select(r => r.Tx).ToList();

        private Transaction CursorTx()
            => grid.CurrentItem is TxRow r ? r.Tx
             : (grid.SelectedItems.Count > 0 ? ((TxRow)grid.SelectedItems[0]).Tx : null);

        private void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var t = CursorTx();
            if (t == null) return;
            var w = new EditWindow(t) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) Store.RaiseChanged();
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedTxs();
            if (sel.Count == 0) return;
            var r = MessageBox.Show($"要删掉选中的 {sel.Count} 笔记录吗？删了就没了。", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            Store.DeleteTx(sel.Select(t => t.id));
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            var t = CursorTx();
            if (t == null) return;
            var w = new EditWindow(t) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) Store.RaiseChanged();
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
            var t = CursorTx();
            if (t == null) return;
            var w = new EditWindow(t, true) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) Store.RaiseChanged();
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var t = CursorTx();
            if (t == null) return;
            if (MessageBox.Show($"删掉「{t.date} {Util.Yuan(t.amount)} {t.merchant}」？", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Store.DeleteTx(t.id);
        }

        // ==================== 垫付：已还 / 未还 ====================

        private void MenuReimbursed_Click(object sender, RoutedEventArgs e) => SetReimbursed(true);
        private void MenuUnreimbursed_Click(object sender, RoutedEventArgs e) => SetReimbursed(false);

        /// <summary>右键标「已还」/ 撤销。只对勾了垫付的记录有意义，别的给一句人话就行</summary>
        private void SetReimbursed(bool reimbursed)
        {
            var t = CursorTx();
            if (t == null) return;
            if (!t.isAdvance)
            {
                MessageBox.Show("这一笔没标成「垫付」。\n\n双击它，在编辑窗里勾上「垫付：这笔钱对方该还我」，"
                    + "之后才能在这里标已还。", "提示");
                return;
            }
            int n = Advances.SetReimbursed(grid.SelectedItems.Cast<TxRow>().Select(r => r.Tx), reimbursed);
            if (n == 0)
            {
                // 选中的都没变（比如本来就是已还），只处理光标那一笔时给个准话
                n = Advances.SetReimbursed(new[] { t }, reimbursed);
                if (n == 0)
                {
                    MessageBox.Show(reimbursed ? "这一笔已经是「已还」了。" : "这一笔现在不是「已还」状态。", "提示");
                    return;
                }
            }
            // Advances 里已经 RaiseChanged，明细页会自己刷新
        }

        /// <summary>从明细页直接开账户管理：看分组，也看「谁欠我多少」</summary>
        private void OpenAccounts_Click(object sender, RoutedEventArgs e)
        {
            AccountWindow.OpenManager(Window.GetWindow(this));
            Store.RaiseChanged();     // 那边改过什么，这边跟着刷一遍
        }

        /// <summary>把这一笔的商户关键词变成规则，以后同样的商户自动分类</summary>
        private void MenuRule_Click(object sender, RoutedEventArgs e)
        {
            var t = CursorTx();
            if (t == null) return;
            string kw = string.IsNullOrWhiteSpace(t.merchant) ? t.note : t.merchant;
            if (string.IsNullOrWhiteSpace(kw))
            {
                MessageBox.Show("这一笔没有商户或备注，没法从中提取关键词。", "提示");
                return;
            }
            if (Store.Data.rules.Any(r => r.keyword == kw))
            {
                MessageBox.Show($"已经有一条「{kw}」的规则了。", "提示");
                return;
            }
            Store.Data.rules.Add(new Rule
            {
                keyword = kw,
                matchField = "both",
                setCategory = t.category,
                setSubcategory = t.subcategory,
                setType = t.type,
                priority = 60
            });
            Store.Save();
            MessageBox.Show($"好了，以后带「{kw}」的记录会自动归到「{t.category}」。", "规则已添加");
        }

        // ==================== 导出 ====================

        private string AskSave(string name, string filter)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = name,
                Filter = filter,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void NeedData()
        {
            if (_current.Count == 0) throw new InvalidOperationException("现在没有可导出的记录，先把筛选条件放宽一点。");
        }

        private void Guard(Action act)
        {
            try { act(); }
            catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "提示"); }
            catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e) => Guard(() =>
        {
            NeedData();
            var path = AskSave(Exporters.SuggestName("记账明细", "csv"), "CSV 文件 (*.csv)|*.csv");
            if (path == null) return;
            Exporters.Csv(_current, path);
            Done(path);
        });

        private void ExportJson_Click(object sender, RoutedEventArgs e) => Guard(() =>
        {
            var path = AskSave($"记账备份_{DateTime.Now:yyyyMMdd_HHmm}.json", "JSON 文件 (*.json)|*.json");
            if (path == null) return;
            Exporters.JsonBackup(path);
            Done(path);
        });

        private void ExportXlsx_Click(object sender, RoutedEventArgs e) => Guard(() =>
        {
            NeedData();
            var path = AskSave(Exporters.SuggestName("记账报表", "xlsx"), "Excel 工作簿 (*.xlsx)|*.xlsx");
            if (path == null) return;
            Exporters.Xlsx(_current, path);
            Done(path);
        });

        private void ExportDocx_Click(object sender, RoutedEventArgs e) => Guard(() =>
        {
            NeedData();
            var path = AskSave(Exporters.SuggestName("记账报表", "docx"), "Word 文档 (*.docx)|*.docx");
            if (path == null) return;
            Exporters.DocxReport(_current, path);
            Done(path);
        });

        private void Done(string path)
        {
            txtExportTip.Text = "已导出：" + Path.GetFileName(path);
            var r = MessageBox.Show($"导出好了：\n\n{path}\n\n要现在打开它吗？", "导出完成",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch { }
            }
        }
    }
}