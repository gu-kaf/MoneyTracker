using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MoneyTracker
{
    public partial class EditWindow : Window
    {
        private readonly Transaction _t;
        private readonly bool _isNew;
        private readonly bool _copy;
        private bool _guardSub;      // 程序自己在改子分类控件时，别让事件又反过来改一遍
        private bool _loading;       // 构造里在灌初值，这期间别让类型切换把「垫付」的勾清掉

        /// <summary>窗口正在编辑的那份数据。自检和自动化用它核对 id 有没有被换掉。</summary>
        public Transaction Editing => _t;

        /// <param name="src">源交易</param>
        /// <param name="asCopy">true = 复制成新的一笔（保存时插入，不覆盖原记录）</param>
        public EditWindow(Transaction src, bool asCopy = false)
        {
            InitializeComponent();
            _loading = true;
            _copy = asCopy;
            _isNew = asCopy;
            _t = Clone(src);
            // 复制才换新 id；改这一笔必须留着原 id，否则 UpdateTx 按 id 找不到人，
            // 会静默什么都没改（之前就是这个 bug：改完保存跟没改一样）。
            if (asCopy) _t.id = Guid.NewGuid().ToString("N");

            Title = asCopy ? "复制为新的一笔" : "修改这一笔";
            txtHeader.Text = asCopy ? "复制为新的一笔" : "修改这一笔";
            btnDelete.Visibility = asCopy ? Visibility.Collapsed : Visibility.Visible;

            txtAmount.Text = Util.Money(_t.amount);
            txtTime.Text = _t.time;
            txtDate.Text = string.IsNullOrWhiteSpace(_t.date) ? DateTime.Today.ToString("yyyy-MM-dd") : _t.date;
            txtMerchant.Text = _t.merchant;
            txtNote.Text = _t.note;
            txtTags.Text = string.Join(", ", _t.tags ?? new List<string>());
            chkExclude.IsChecked = _t.excludeFromBudget;

            cmbType.SelectedIndex = _t.type == "income" ? 1 : _t.type == "transfer" ? 2 : 0;

            foreach (var a in Store.Data.accounts.Where(x => !x.archived))
            {
                cmbAcc.Items.Add(a.name);
                cmbTo.Items.Add(a.name);
            }
            cmbAcc.SelectedItem = cmbAcc.Items.Contains(_t.account) ? _t.account : (cmbAcc.Items.Count > 0 ? cmbAcc.Items[0] : null);
            cmbTo.SelectedItem = cmbTo.Items.Contains(_t.toAccount) ? _t.toAccount : (cmbTo.Items.Count > 1 ? cmbTo.Items[1] : null);

            BuildCats();
            Type_Changed(null, null);

            // 替别人付的钱：老记录没这几个字段，读出来就是空/false，不会报错
            txtForWhom.Text = _t.forWhom ?? "";
            chkAdvance.IsChecked = _t.isAdvance;
            chkReimbursed.IsChecked = _t.isReimbursed;
            BuildForWhomChips();
            Advance_Changed(null, null);
            _loading = false;
        }

        private static Transaction Clone(Transaction s) => new Transaction
        {
            id = s.id,
            type = s.type,
            amount = s.amount,
            date = s.date,
            time = s.time,
            category = s.category,
            subcategory = s.subcategory,
            account = s.account,
            toAccount = s.toAccount,
            merchant = s.merchant,
            note = s.note,
            tags = new List<string>(s.tags ?? new List<string>()),
            source = s.source,
            excludeFromBudget = s.excludeFromBudget,
            // 这几个字段必须跟着克隆，不然「改一笔」保存时会被悄悄清掉：
            // ledgerId 清掉记录就跑到默认账本去了，垫付标记清掉欠款就凭空少一笔。
            ledgerId = s.ledgerId,
            forWhom = s.forWhom,
            isAdvance = s.isAdvance,
            isReimbursed = s.isReimbursed,
            createdAt = s.createdAt
        };

        private string CurrentType
            => (cmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "expense";

        private void Type_Changed(object sender, SelectionChangedEventArgs e)
        {
            bool transfer = CurrentType == "transfer";
            boxTo.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;
            lblAcc.Text = transfer ? "转出账户" : "账户";

            // 「垫付」只对支出有意义。用户自己把类型切成收入/转账时顺手取消掉，
            // 免得留下一条收入却挂着垫付的账（构造里灌初值时不碰，见 _loading）。
            if (chkAdvance == null) return;
            bool expense = CurrentType == "expense";
            chkAdvance.Visibility = expense ? Visibility.Visible : Visibility.Collapsed;
            if (!expense && !_loading) chkAdvance.IsChecked = false;
            chkReimbursed.Visibility = expense ? Visibility.Visible : Visibility.Collapsed;
            Advance_Changed(null, null);
        }

        // ==================== 替谁花的 / 垫付 ====================

        /// <summary>「替谁花的」候选：自己的事 + 以前填过的名字，点一下直接填进去</summary>
        private void BuildForWhomChips()
        {
            panelForWhom.Children.Clear();
            AddForWhomChip("", Advances.SelfLabel);
            foreach (var p in Advances.KnownPeople()) AddForWhomChip(p, p);
            HighlightForWhom();
        }

        private void AddForWhomChip(string value, string label)
        {
            var btn = new Button
            {
                Content = label,
                Tag = value,
                Style = (Style)FindResource("Chip"),
                FontSize = 12,
                Padding = new Thickness(10, 5, 10, 5)
            };
            btn.Click += (s, e) => txtForWhom.Text = (string)((Button)s).Tag;
            panelForWhom.Children.Add(btn);
        }

        private void HighlightForWhom()
        {
            var typed = (txtForWhom.Text ?? "").Trim();
            foreach (var b in panelForWhom.Children.OfType<Button>())
                b.Style = (Style)FindResource((string)b.Tag == typed ? "ChipOn" : "Chip");
        }

        /// <summary>手打的新名字也做成按钮，下次不用再打一遍</summary>
        private void ForWhom_Changed(object sender, TextChangedEventArgs e)
        {
            if (panelForWhom == null) return;
            var typed = (txtForWhom.Text ?? "").Trim();
            if (typed.Length > 0 && !panelForWhom.Children.OfType<Button>().Any(b => (string)b.Tag == typed))
                AddForWhomChip(typed, typed);
            HighlightForWhom();
        }

        /// <summary>只有勾了「垫付」，才谈得上「对方已经还了」</summary>
        private void Advance_Changed(object sender, RoutedEventArgs e)
        {
            if (chkReimbursed == null) return;
            bool adv = chkAdvance.IsChecked == true;
            chkReimbursed.IsEnabled = adv;
            if (!adv) chkReimbursed.IsChecked = false;

            txtAdvanceHint.Text = CurrentType != "expense"
                ? "「垫付」只对支出有意义，收入/转账不用管它；上面的抬头照样可以填。"
                : (adv ? "记完这笔，账户页的「谁欠我多少」会按名字把它算进去。"
                       : "勾上「垫付」，账户页才会把这笔算进「谁欠我多少钱」。");
        }

        private void BuildCats()
        {
            bool income = CurrentType == "income";
            cmbCat.Items.Clear();
            if (CurrentType != "transfer")
                foreach (var c in income ? Categories.IncomeNames : Categories.ExpenseNames) cmbCat.Items.Add(c);
            if (cmbCat.Items.Contains(_t.category)) cmbCat.SelectedItem = _t.category;
            else if (cmbCat.Items.Count > 0 && _t.category != "转账") cmbCat.SelectedIndex = 0;
            BuildSubs();
        }

        private void Cat_Changed(object sender, SelectionChangedEventArgs e) => BuildSubs();

        /// <summary>「不限（留空）」是下拉里的第一项，代表这笔不细分</summary>
        private const string SubNone = "不限（留空）";

        /// <summary>二级分类：下拉里给推荐 + 自己以前用过的，旁边还有输入框可以随便写。
        /// 选「不限（留空）」或者把输入框清掉都行——留空不会挡着保存。</summary>
        private void BuildSubs()
        {
            var keep = txtSub.Text ?? "";                   // 用户手打的东西不能因为切分类就丢了
            if (keep.Length == 0 && !_guardSub) keep = _t.subcategory ?? "";

            _guardSub = true;
            cmbSub.Items.Clear();
            cmbSub.Items.Add(SubNone);
            var cat = cmbCat.SelectedItem as string;
            if (!string.IsNullOrEmpty(cat))
                foreach (var s in CategoryTree.Options(cat, CurrentType, keep)) cmbSub.Items.Add(s);

            var sel = cmbSub.Items.Contains(keep) ? keep : SubNone;
            cmbSub.SelectedItem = sel;
            _guardSub = false;

            txtSub.Text = sel is string s2 && s2 != SubNone ? s2 : "";
        }

        private void Sub_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_guardSub || txtSub == null) return;
            var picked = cmbSub.SelectedItem as string;
            txtSub.Text = picked != null && picked != SubNone ? picked : "";
        }

        private void SubText_Changed(object sender, TextChangedEventArgs e)
        {
            if (_guardSub || cmbSub == null) return;

            // 打出来的新词得能出现在下拉里，不然下次又得重打一遍
            var typed = (txtSub.Text ?? "").Trim();
            if (typed.Length > 0 && !cmbSub.Items.Contains(typed))
            {
                _guardSub = true;
                cmbSub.Items.Add(typed);
                _guardSub = false;
            }
        }

        private void ClearSub_Click(object sender, RoutedEventArgs e)
        {
            _guardSub = true;
            txtSub.Text = "";
            cmbSub.SelectedItem = SubNone;
            _guardSub = false;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            long cents = Util.ToCents(txtAmount.Text);
            if (cents <= 0)
            {
                MessageBox.Show("金额得大于 0。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (CurrentType == "transfer" && (cmbAcc.SelectedItem as string) == (cmbTo.SelectedItem as string))
            {
                MessageBox.Show("转出和转入不能是同一个账户。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _t.type = CurrentType;
            _t.amount = cents;
            _t.date = Util.ParseDate(txtDate.Text) ?? DateTime.Today.ToString("yyyy-MM-dd");
            _t.time = string.IsNullOrWhiteSpace(txtTime.Text) ? "" : txtTime.Text.Trim();
            _t.category = CurrentType == "transfer" ? "转账" : ((cmbCat.SelectedItem as string) ?? _t.category);
            _t.subcategory = (txtSub.Text ?? "").Trim();
            _t.account = cmbAcc.SelectedItem as string ?? "现金";
            _t.toAccount = CurrentType == "transfer" ? (cmbTo.SelectedItem as string ?? "") : "";
            _t.merchant = txtMerchant.Text.Trim();
            _t.note = txtNote.Text.Trim();
            _t.tags = txtTags.Text.Split(new[] { ',', '，', ';', '；', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            _t.excludeFromBudget = chkExclude.IsChecked == true;
            // 替别人付的钱（可选项，不填就是空）
            _t.forWhom = (txtForWhom.Text ?? "").Trim();
            _t.isAdvance = CurrentType == "expense" && chkAdvance.IsChecked == true;
            _t.isReimbursed = _t.isAdvance && chkReimbursed.IsChecked == true;
            _t.hash = Util.HashOf(_t);
            _t.updatedAt = Util.NowStamp();

            if (_isNew) Store.AddTx(_t, true);
            else if (!Store.UpdateTx(_t))
            {
                MessageBox.Show("没找到这一笔，可能它已经被删掉了。\n你这次的修改没有保存。",
                    "改不动", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定删掉这一笔吗？删了就没了。", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!_copy) Store.DeleteTx(_t.id);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}