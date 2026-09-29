using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    /// <summary>
    /// 账本管理：列出所有账本、新建、改名、换标识色、归档、删除。
    ///
    /// 删除这块是刻意做"啰嗦"的：账本里只要还有记录，默认就删不掉，
    /// 必须先选一个接手记录的账本（记录一笔不动，只换归属）；
    /// 真想连记录一起删，得额外勾那个红字选项再确认一次。
    /// 目的只有一个——账本删了，数据不能凭空消失。
    /// </summary>
    public partial class LedgerWindow : Window
    {
        private string _id = "";            // 正在编辑的账本 id；空 = 新建状态
        private bool _prepared;
        private readonly string _wantId;
        private readonly bool _startNew;

        public LedgerWindow(string selectId = null, bool startNew = false)
        {
            InitializeComponent();
            _wantId = selectId;
            _startNew = startNew;
            Loaded += (s, e) => Prepare();
        }

        /// <summary>把界面填好（正常由 Loaded 触发；离屏出图时手动调用）</summary>
        public void Prepare()
        {
            if (_prepared) return;
            _prepared = true;
            BuildColors();
            ReloadList();
            if (_startNew) StartNew();
            else Select(_wantId ?? Store.CurrentLedgerId);
        }

        // ==================== 左边那份清单 ====================

        private void ReloadList()
        {
            var keep = string.IsNullOrEmpty(_id) ? null : _id;
            listLedgers.Items.Clear();
            foreach (var l in Store.Ledgers) listLedgers.Items.Add(MakeRow(l));
            if (keep != null) Select(keep);
        }

        /// <summary>一行账本：色点 + 名字 + 备注 + 记录数 + 标记</summary>
        private FrameworkElement MakeRow(Ledger l)
        {
            bool on = l.id == Store.CurrentLedgerId;
            int n = Store.LedgerCount(l.id);

            var g = new Grid { Margin = new Thickness(0, 8, 0, 8), Tag = l.id };

            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                Background = LedgerUi.BrushOf(l),
                VerticalAlignment = VerticalAlignment.Center
            };

            var col = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(new TextBlock
            {
                Text = l.name,
                Foreground = (Brush)FindResource("TextBrush"),
                FontSize = 13.5,
                // 不加粗：中文在细体字上会被算法撑出假粗，反而糊
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            if (!string.IsNullOrWhiteSpace(l.note))
                sub.Children.Add(new TextBlock
                {
                    Text = l.note,
                    Foreground = (Brush)FindResource("SubBrush"),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 160
                });
            sub.Children.Add(new TextBlock
            {
                Text = (sub.Children.Count > 0 ? " · " : "") + n + " 笔",
                Foreground = (Brush)FindResource("SubBrush"),
                FontSize = 11
            });
            if (l.id == Store.CurrentLedgerId)
                sub.Children.Add(new TextBlock
                {
                    Text = " · 正在看",
                    Foreground = (Brush)FindResource("AccentBrush"),
                    FontSize = 11
                });
            if (l.archived)
                sub.Children.Add(new TextBlock
                {
                    Text = " · 已归档",
                    Foreground = (Brush)FindResource("SubBrush"),
                    FontSize = 11
                });
            col.Children.Add(sub);

            var badge = new TextBlock
            {
                Text = LedgerUi.InitialOf(l),
                Foreground = (Brush)FindResource("OnAccentBrush"),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var initial = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                Background = LedgerUi.BrushOf(l),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = badge
            };
            if (on) initial.Visibility = Visibility.Visible;

            Grid.SetColumn(col, 1);
            Grid.SetColumn(initial, 2);
            g.Children.Add(dot);
            g.Children.Add(col);
            g.Children.Add(initial);
            return g;
        }

        private void Select(string id)
        {
            listLedgers.SelectedItem = null;
            foreach (var item in listLedgers.Items)
                if (item is FrameworkElement fe && (fe.Tag as string) == id)
                {
                    listLedgers.SelectedItem = item;
                    return;
                }
            if (Store.Ledgers.Count > 0) LoadLedger(Store.Ledgers[0].id);
            else StartNew();
        }

        private void Ledger_Selected(object sender, SelectionChangedEventArgs e)
        {
            if (listLedgers.SelectedItem is FrameworkElement fe && fe.Tag is string id) LoadLedger(id);
        }

        // ==================== 表单 ====================

        private void BuildColors()
        {
            panelColors.Children.Clear();
            foreach (var hex in Store.LedgerPalette)
            {
                var b = new Button
                {
                    Width = 26, Height = 26, Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 7, 7), Tag = hex,
                    Style = (Style)FindResource("Btn"),
                    Background = LedgerUi.BrushOfHex(hex),
                    ToolTip = hex
                };
                b.Click += Swatch_Click;
                panelColors.Children.Add(b);
            }
            var auto = new Button
            {
                Content = "自动", Style = (Style)FindResource("BtnGhost"), Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 7), FontSize = 11.5, Tag = ""
            };
            auto.Click += Swatch_Click;
            panelColors.Children.Add(auto);
            HighlightSwatch();
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            var hex = (sender as Button)?.Tag as string ?? "";
            txtColor.Text = hex;
            HighlightSwatch();
        }

        /// <summary>把当前填着的颜色对应的色块描边highlight出来</summary>
        private void HighlightSwatch()
        {
            var now = (txtColor.Text ?? "").Trim();
            if (!Store.IsHexColor(now)) now = "";
            foreach (var c in panelColors.Children.OfType<Button>())
            {
                bool on = ((c.Tag as string) ?? "") == now;
                c.BorderBrush = (Brush)FindResource(on ? "AccentBrush" : "BorderBrush2");
                c.BorderThickness = new Thickness(on ? 2 : 1);
            }
        }

        public void StartNew()
        {
            _id = "";
            listLedgers.SelectedItem = null;
            txtFormTitle.Text = "新建账本";
            txtName.Text = "";
            txtColor.Text = Store.NextLedgerColor();
            txtNote.Text = "";
            chkArchived.IsChecked = false;
            chkAlsoRecords.IsChecked = false;
            btnDelete.IsEnabled = false;
            btnUse.IsEnabled = false;
            btnSave.Content = "建好它";
            HighlightSwatch();
            RefreshTargets();
            txtDeleteHint.Text = "新的账本里还没有记录。";
            Tip("填个名字，点「建好它」就行。");
            txtName.Focus();
        }

        private void LoadLedger(string id)
        {
            var l = Store.FindLedger(id);
            if (l == null) { StartNew(); return; }

            _id = l.id;
            txtFormTitle.Text = "改这个账本";
            txtName.Text = l.name;
            txtColor.Text = (l.color ?? "").Trim();
            txtNote.Text = l.note ?? "";
            chkArchived.IsChecked = l.archived;
            chkAlsoRecords.IsChecked = false;
            btnDelete.IsEnabled = Store.Ledgers.Count > 1;
            btnUse.IsEnabled = l.id != Store.CurrentLedgerId;
            btnSave.Content = "保存";
            HighlightSwatch();
            RefreshTargets();
            UpdateDeleteHint();
            Tip("");
        }

        private void RefreshTargets()
        {
            var targets = Store.Ledgers.Where(x => x.id != _id).ToList();
            cmbMoveTo.ItemsSource = targets;
            if (targets.Count == 0) { cmbMoveTo.SelectedIndex = -1; return; }

            int i = targets.FindIndex(t => t.id == Store.CurrentLedgerId);
            if (i < 0) i = targets.FindIndex(t => !t.archived);
            cmbMoveTo.SelectedIndex = i < 0 ? 0 : i;
        }

        private void UpdateDeleteHint()
        {
            int n = Store.LedgerCount(_id);
            if (Store.Ledgers.Count <= 1)
            {
                txtDeleteHint.Text = "这是仅剩的账本，删不了——记录总得有个地方待着。";
                return;
            }
            txtDeleteHint.Text = n == 0
                ? "这个账本里没有记录，删掉不影响任何数据。"
                : $"这个账本里有 {n} 笔记录。默认是先把它们转到下面选的那个账本再删（记录一笔都不会少）；"
                  + "只有勾上「连记录一起删掉」才会真的删。";
        }

        private void Tip(string msg, bool warn = false)
        {
            txtTip.Text = msg;
            txtTip.Foreground = (Brush)FindResource(warn ? "ExpenseBrush" : "SubBrush");
        }

        // ==================== 按钮 ====================

        private void New_Click(object sender, RoutedEventArgs e) => StartNew();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_id))
            {
                if (string.IsNullOrWhiteSpace(txtName.Text)) { Tip("先给账本起个名字。", true); txtName.Focus(); return; }
                var l = Store.AddLedger(txtName.Text, txtColor.Text, txtNote.Text);
                ReloadList();
                Select(l.id);
                Tip("建好了。想切过去点左下角「切到这个账本」。");
                return;
            }

            if (!Store.UpdateLedger(_id, txtName.Text, txtColor.Text, txtNote.Text, chkArchived.IsChecked == true))
            {
                Tip(Store.LastError ?? "没保存成功。", true);
                return;
            }
            string keep = _id;
            ReloadList();
            Select(keep);
            Tip("改好了。");
        }

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_id)) return;
            if (!Store.SetCurrentLedger(_id))
            {
                Tip(Store.LastError ?? "切不过去。", true);
                return;
            }
            ReloadList();
            Select(_id);
            LoadLedger(_id);
            Tip("已经切到「" + Store.LedgerName(_id) + "」了。");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var l = Store.FindLedger(_id);
            if (l == null) { Tip("先在上面选一个账本。", true); return; }
            if (Store.Ledgers.Count <= 1) { Tip("这是仅剩的账本，删不了。", true); return; }

            int n = Store.LedgerCount(l.id);
            bool alsoRecords = chkAlsoRecords.IsChecked == true;
            string target = (cmbMoveTo.SelectedItem as Ledger)?.id;

            string ask;
            if (n == 0)
                ask = $"确定删掉账本「{l.name}」吗？\n\n它里面没有记录，删了不影响任何数据。";
            else if (alsoRecords)
                ask = $"「{l.name}」里有 {n} 笔记录，会跟账本一起删掉。\n\n"
                    + "删了就找不回来了（不放心的话先去「备份」里留一份快照）。真的确定吗？";
            else if (string.IsNullOrEmpty(target))
            {
                Tip("先选一个接收记录的账本，或者勾上「连记录一起删掉」。", true);
                return;
            }
            else
                ask = $"「{l.name}」里的 {n} 笔记录会全部转到「{Store.LedgerName(target)}」，然后删掉账本「{l.name}」。\n\n"
                    + "记录一笔都不会少。确定吗？";

            if (MessageBox.Show(ask, "删除账本", MessageBoxButton.OKCancel,
                    alsoRecords && n > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            if (!Store.DeleteLedger(l.id, target, alsoRecords))
            {
                Tip(Store.LastError ?? "没删成功。", true);
                return;
            }

            ReloadList();
            Select(Store.CurrentLedgerId);
            Tip(n > 0 && !alsoRecords
                ? $"账本删了，{n} 笔记录已经转到「{Store.LedgerName(target)}」。"
                : "账本删了。");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}