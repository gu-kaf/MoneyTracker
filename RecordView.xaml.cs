using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    public partial class RecordView : UserControl, IRefreshable
    {
        private string _type = "expense";
        private string _amount = "";      // 用户敲的原始数字串
        private string _cat = "";
        private string _sub = "";

        public RecordView()
        {
            InitializeComponent();
            txtDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
            txtTime.Text = DateTime.Now.ToString("HH:mm");
            RebuildAccounts();
            BuildCategories();
            BuildForWhomChips();
            RenderAmount();
            LoadRecent();
            ApplyTypeStyle();
        }

        public void Refresh()
        {
            RebuildAccounts();
            BuildCategories();
            BuildForWhomChips();
            LoadRecent();
        }

        // ==================== 类型 ====================

        private void Today_Click(object sender, RoutedEventArgs e)
            => txtDate.Text = DateTime.Today.ToString("yyyy-MM-dd");

        private void Yesterday_Click(object sender, RoutedEventArgs e)
            => txtDate.Text = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

        private void Type_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string t)
            {
                _type = t;
                _cat = ""; _sub = "";
                BuildCategories();
                ApplyTypeStyle();
            }
        }

        private void ApplyTypeStyle()
        {
            foreach (var (btn, key) in new[] { (tabExpense, "expense"), (tabIncome, "income"), (tabTransfer, "transfer") })
            {
                bool on = _type == key;
                btn.Style = (Style)FindResource(on ? "BtnPrimary" : "Btn");
            }
            boxToAccount.Visibility = _type == "transfer" ? Visibility.Visible : Visibility.Collapsed;
            lblAccount.Text = _type == "transfer" ? "转出账户" : "账户";
            txtAmount.Foreground = (Brush)FindResource(_type == "income" ? "IncomeBrush" : "ExpenseBrush");
            txtAmountHint.Text = _type == "income" ? "收入金额" : (_type == "transfer" ? "转账金额" : "支出金额");

            // 垫付只对支出有意义：收入/转账时把这个开关收起来，"替谁花的"还留着记人情往来
            bool expense = _type == "expense";
            chkAdvance.Visibility = expense ? Visibility.Visible : Visibility.Collapsed;
            if (!expense) chkAdvance.IsChecked = false;
            txtAdvanceHint.Text = expense
                ? "勾上之后，账户页的「谁欠我多少」会按名字攒起来。不填不影响记账。"
                : "「垫付」只对支出有意义；这一笔的抬头照样可以填在这里。";
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
            var typed = CurrentForWhom();
            foreach (var b in panelForWhom.Children.OfType<Button>())
                b.Style = (Style)FindResource((string)b.Tag == typed ? "ChipOn" : "Chip");
        }

        /// <summary>手打了一个新名字就顺手把它做成按钮，下次不用再打（跟二级分类一个套路）</summary>
        private void ForWhom_Changed(object sender, TextChangedEventArgs e)
        {
            if (panelForWhom == null) return;
            var typed = CurrentForWhom();
            if (typed.Length > 0 && !panelForWhom.Children.OfType<Button>().Any(b => (string)b.Tag == typed))
                AddForWhomChip(typed, typed);
            HighlightForWhom();
        }

        /// <summary>「替谁花的」的当前值：空串 = 自己的事</summary>
        private string CurrentForWhom() => (txtForWhom.Text ?? "").Trim();

        /// <summary>垫付是「这一笔」的事，记完就清掉，别让下一笔白白继承上一笔的抬头</summary>
        private void ResetForWhom()
        {
            txtForWhom.Text = "";
            chkAdvance.IsChecked = false;
            BuildForWhomChips();
        }

        // ==================== 金额键盘 ====================

        private void Key_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is string k)) return;
            switch (k)
            {
                case "back":
                    _amount = _amount.Length > 0 ? _amount.Substring(0, _amount.Length - 1) : "";
                    break;
                case "clear":
                    _amount = "";
                    break;
                case ".":
                    if (_amount.Contains(".")) return;
                    _amount = (_amount.Length == 0 ? "0" : _amount) + ".";
                    break;
                case "+10":
                    _amount = Util.Money(Util.ToCents(_amount) + 1000);
                    break;
                case "+50":
                    _amount = Util.Money(Util.ToCents(_amount) + 5000);
                    break;
                default:
                    if (_amount.Contains(".") && _amount.Split('.')[1].Length >= 2) return;
                    if (_amount == "0") _amount = "";
                    if (_amount.Replace(".", "").Length >= 9) return;
                    _amount += k;
                    break;
            }
            RenderAmount();
        }

        private void RenderAmount()
        {
            txtAmount.Text = string.IsNullOrEmpty(_amount) || _amount == "." ? "0.00" : _amount;
        }

        // ==================== 分类 ====================

        private void BuildCategories()
        {
            panelCat.Children.Clear();
            var names = _type == "income" ? Categories.IncomeNames : Categories.ExpenseNames;
            foreach (var n in names)
            {
                var btn = new Button
                {
                    Content = n,
                    Tag = n,
                    Style = (Style)FindResource(_cat == n ? "ChipOn" : "Chip")
                };
                btn.Click += (s, e) =>
                {
                    _cat = (string)((Button)s).Tag;
                    _sub = "";
                    BuildCategories();
                };
                panelCat.Children.Add(btn);
            }
            BuildSubs();
        }

        /// <summary>二级分类：一级分类没选就只显示一句提示，选了才铺开快选按钮。
        /// 二级分类永远可以空着——空着照样能保存，报表里就是"没细分"。</summary>
        private void BuildSubs()
        {
            panelSub.Children.Clear();
            panelSubFreq.Children.Clear();

            bool has = _cat.Length > 0;
            boxSubEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            boxSubPick.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) return;

            // 点一下按钮会重建面板，所以先把焦点状态记下来，重建完再还回去
            bool typing = txtSub.IsKeyboardFocusWithin;

            txtSubHint.Text = $"二级分类（{_cat} · 可以不选）";
            txtSubAllHint.Text = $"「{_cat}」下面这些";

            BuildSubRow(panelSubFreq, CategoryTree.FrequentlyUsed(_type));
            BuildSubRow(panelSub, CategoryTree.Options(_cat, _type, _sub));

            if (!typing) txtSub.Text = _sub;
            txtSubPlaceholder.Visibility = string.IsNullOrEmpty(txtSub.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (typing) txtSub.Focus();
        }

        private void BuildSubRow(WrapPanel panel, IEnumerable<string> items)
        {
            foreach (var s in items)
            {
                var name = s;
                var btn = new Button
                {
                    Content = name,
                    Tag = name,
                    Style = (Style)FindResource(_sub == name ? "ChipOn" : "Chip")
                };
                btn.Click += (s2, e) => SetSub(name);
                panel.Children.Add(btn);
            }
        }

        /// <summary>选中/取消一个二级分类。点已经亮着的那个就是取消——比再找一个"不限"按钮快</summary>
        private void SetSub(string sub)
        {
            _sub = _sub == sub ? "" : sub;
            txtSub.Text = _sub;                       // 重建面板之前先写进去
            BuildSubs();
        }

        private void SubText_Changed(object sender, TextChangedEventArgs e)
        {
            _sub = (txtSub.Text ?? "").Trim();
            txtSubPlaceholder.Visibility = string.IsNullOrEmpty(txtSub.Text) ? Visibility.Visible : Visibility.Collapsed;

            if (txtSub.IsKeyboardFocusWithin)
            {
                // 正在打字：只跟着变按钮的亮暗，别重建控件把光标顶跑
                HighlightSub();
                return;
            }
            BuildSubs();
        }

        private void ClearSub_Click(object sender, RoutedEventArgs e)
        {
            _sub = "";
            txtSub.Text = "";
            BuildSubs();
        }

        /// <summary>手打了一个新词就顺手把它变成按钮，下次不用再打</summary>
        private void HighlightSub()
        {
            foreach (var panel in new[] { panelSub, panelSubFreq })
                foreach (var c in panel.Children.OfType<Button>())
                    c.Style = (Style)FindResource((string)c.Tag == _sub ? "ChipOn" : "Chip");
        }

        private void Merchant_Changed(object sender, TextChangedEventArgs e)
        {
            // 边写边提示：这条会被哪条规则抓走
            if (_cat.Length > 0) { txtCatHint.Text = "选择分类"; return; }
            var probe = new Transaction { merchant = txtMerchant.Text, note = txtNote.Text, account = CurrentAccount() };
            var r = Store.MatchRule(probe);
            txtCatHint.Text = r == null ? "选择分类" : $"选择分类　（规则会自动归到「{r.setCategory}」）";
        }

        // ==================== 账户 ====================

        private string CurrentAccount()
            => cmbAccount.SelectedItem as string ?? "现金";

        private void RebuildAccounts()
        {
            var keep = cmbAccount.SelectedItem as string;
            var keepTo = cmbToAccount.SelectedItem as string;
            cmbAccount.Items.Clear();
            cmbToAccount.Items.Clear();
            foreach (var a in Store.Data.accounts.Where(x => !x.archived))
            {
                cmbAccount.Items.Add(a.name);
                cmbToAccount.Items.Add(a.name);
            }
            cmbAccount.SelectedItem = keep != null && cmbAccount.Items.Contains(keep) ? keep : (cmbAccount.Items.Count > 0 ? cmbAccount.Items[0] : null);
            cmbToAccount.SelectedItem = keepTo != null && cmbToAccount.Items.Contains(keepTo) ? keepTo : (cmbToAccount.Items.Count > 1 ? cmbToAccount.Items[1] : null);
        }

        // ==================== 保存 ====================

        private void Save_Click(object sender, RoutedEventArgs e) => DoSave(false);
        private void SaveMore_Click(object sender, RoutedEventArgs e) => DoSave(true);

        private void DoSave(bool keepGoing)
        {
            long cents = Util.ToCents(_amount);
            if (cents <= 0)
            {
                Tip("先输入金额喵", true);
                return;
            }
            if (_type == "transfer" && CurrentAccount() == (cmbToAccount.SelectedItem as string))
            {
                Tip("转出和转入不能是同一个账户", true);
                return;
            }

            var t = new Transaction
            {
                type = _type,
                amount = cents,
                date = Util.ParseDate(txtDate.Text) ?? DateTime.Today.ToString("yyyy-MM-dd"),
                time = string.IsNullOrWhiteSpace(txtTime.Text) ? DateTime.Now.ToString("HH:mm") : txtTime.Text.Trim(),
                category = _cat,
                subcategory = _sub,
                account = CurrentAccount(),
                toAccount = _type == "transfer" ? (cmbToAccount.SelectedItem as string ?? "") : "",
                merchant = txtMerchant.Text.Trim(),
                note = txtNote.Text.Trim(),
                excludeFromBudget = chkExcludeBudget.IsChecked == true,
                // 替别人付的钱：都是可选项，不填就是空/自己的事，不影响记账
                forWhom = CurrentForWhom(),
                isAdvance = _type == "expense" && chkAdvance.IsChecked == true,
                isReimbursed = false,
                tags = txtTags.Text.Split(new[] { ',', '，', ';', '；', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries).ToList()
            };
            if (t.type == "transfer") t.category = "转账";

            // 分类没选就让规则来定；选了就尊重用户
            if (string.IsNullOrEmpty(t.category)) Store.ApplyRules(t);
            if (string.IsNullOrEmpty(t.category)) t.category = t.type == "income" ? "其他收入" : (t.type == "transfer" ? "转账" : "其他支出");

            t.hash = Util.HashOf(t);
            if (Store.IsDuplicate(t))
            {
                var r = MessageBox.Show(
                    $"看起来和已有记录重复了：\n\n{t.date}　{Util.Yuan(t.amount)}　{t.merchant}\n\n还要再记一笔吗？",
                    "可能重复", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }

            Store.AddTx(t, true);
            string adv = t.isAdvance ? "　垫付 · " + Advances.PersonOf(t) : "";
            Tip($"已记下 {Util.Yuan(t.amount)}　{Util.TypeLabel(t.type)} · {t.category}"
                + (string.IsNullOrEmpty(t.subcategory) ? "" : " · " + t.subcategory) + adv);

            if (keepGoing)
            {
                _amount = "";
                RenderAmount();
                txtMerchant.Text = "";
                txtNote.Text = "";
                txtTags.Text = "";
            }
            else
            {
                _amount = ""; _cat = ""; _sub = "";
                txtMerchant.Text = ""; txtNote.Text = ""; txtTags.Text = "";
                RenderAmount();
                BuildCategories();
            }
            ResetForWhom();
            LoadRecent();
        }

        private void Tip(string msg, bool warn = false)
        {
            txtTip.Text = msg;
            txtTip.Foreground = (Brush)FindResource(warn ? "ExpenseBrush" : "SubBrush");
        }

        // ==================== 最近几笔 ====================

        private void LoadRecent()
        {
            listRecent.Items.Clear();
            foreach (var t in Store.Scoped().Take(5))
            {
                var row = new Button
                {
                    Style = (Style)FindResource("BtnGhost"),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 4),
                    Tag = t
                };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new TextBlock
                {
                    Text = $"{t.date.Substring(5)}　{t.category}" + (string.IsNullOrEmpty(t.merchant) ? "" : "　" + t.merchant),
                    Foreground = (Brush)FindResource("SubBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var right = new TextBlock
                {
                    Text = (t.type == "income" ? "+" : "-") + Util.Money(t.amount),
                    FontFamily = (FontFamily)FindResource("NumFont"),
                    Foreground = (Brush)FindResource(t.type == "income" ? "IncomeBrush" : "ExpenseBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(right, 1);
                g.Children.Add(left);
                g.Children.Add(right);
                row.Content = g;
                row.Click += (s, e) =>
                {
                    var tx = (Transaction)((Button)s).Tag;
                    _type = tx.type;
                    _cat = tx.category;
                    _sub = tx.subcategory;
                    RebuildAccounts();
                    cmbAccount.SelectedItem = tx.account;
                    BuildCategories();
                    ApplyTypeStyle();
                    Tip($"已套用「{tx.category}」的分类与账户");
                };
                listRecent.Items.Add(row);
            }
            if (listRecent.Items.Count == 0)
            {
                listRecent.Items.Add(new TextBlock
                {
                    Text = "还没有记录，记第一笔吧",
                    Foreground = (Brush)FindResource("SubBrush"),
                    Margin = new Thickness(2, 4, 0, 0)
                });
            }
        }
    }
}