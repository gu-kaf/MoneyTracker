using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    public partial class ReportView : UserControl, IRefreshable
    {
        private bool _ready;
        private List<Transaction> _list = new List<Transaction>();
        private string _from = "", _to = "";

        public ReportView()
        {
            InitializeComponent();
            cmbScope.SelectedIndex = 2; // 默认近 6 个月
            _ready = true;
            Load();
        }

        public void Refresh() { _ready = false; BuildMonths(); _ready = true; Load(); }

        private void BuildMonths()
        {
            var keep = cmbMonth.SelectedItem as string;
            cmbMonth.Items.Clear();
            foreach (var m in Util.LastMonths(24)) cmbMonth.Items.Add(m);
            cmbMonth.SelectedItem = keep != null && cmbMonth.Items.Contains(keep) ? keep : null;
        }

        private void Scope_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            _ready = false;
            cmbMonth.SelectedIndex = -1;   // 选了区间就清掉单月
            _ready = true;
            Load();
        }

        private void Month_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || cmbMonth.SelectedItem == null) return;
            _ready = false;
            cmbScope.SelectedIndex = -1;   // 选了单月就清掉区间
            _ready = true;
            Load();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

        private void Load()
        {
            var today = DateTime.Today;
            if (cmbMonth.SelectedItem is string m)
            {
                var (f, t) = Util.MonthRange(m);
                _from = f; _to = t;
            }
            else
            {
                switch (Math.Max(0, cmbScope.SelectedIndex))
                {
                    case 0: _from = new DateTime(today.Year, today.Month, 1).ToString("yyyy-MM-dd"); _to = today.ToString("yyyy-MM-dd"); break;
                    case 1: _from = today.AddMonths(-2).ToString("yyyy-MM-01"); _to = today.ToString("yyyy-MM-dd"); break;
                    case 2: _from = today.AddMonths(-5).ToString("yyyy-MM-01"); _to = today.ToString("yyyy-MM-dd"); break;
                    case 3: _from = new DateTime(today.Year, 1, 1).ToString("yyyy-MM-dd"); _to = today.ToString("yyyy-MM-dd"); break;
                    default: _from = ""; _to = ""; break;
                }
            }

            _list = Store.QueryScoped(from: string.IsNullOrEmpty(_from) ? null : _from,
                                to: string.IsNullOrEmpty(_to) ? null : _to).ToList();

            long outSum = _list.Where(t => t.type == "expense").Sum(t => t.amount);
            long inSum = _list.Where(t => t.type == "income").Sum(t => t.amount);
            var days = _list.Select(t => t.date).Distinct().Count();

            txtOut.Text = Util.Money(outSum);
            txtIn.Text = Util.Money(inSum);
            txtNet.Text = Util.Money(inSum - outSum);
            txtNet.Foreground = (Brush)FindResource(inSum - outSum >= 0 ? "TextBrush" : "ExpenseBrush");
            txtOutSub.Text = $"{_list.Count(t => t.type == "expense")} 笔";
            txtInSub.Text = $"{_list.Count(t => t.type == "income")} 笔";
            txtNetSub.Text = _list.Count + " 笔总计";
            txtAvg.Text = Util.Money(days > 0 ? outSum / days : 0);
            txtAvgSub.Text = days + " 天有记录";

            // 支出分类
            var byCat = _list.Where(t => t.type == "expense")
                             .GroupBy(t => string.IsNullOrEmpty(t.category) ? "未分类" : t.category)
                             .Select(g => new { cat = g.Key, sum = (double)g.Sum(x => x.amount) / 100.0 })
                             .OrderByDescending(x => x.sum).ToList();

            var donut = byCat.Take(8).Select(x => (x.cat, x.sum)).ToList();
            if (byCat.Count > 8)
            {
                var rest = byCat.Skip(8).Sum(x => x.sum);
                donut.Add(("其他 " + (byCat.Count - 8) + " 类", rest));
            }
            chartDonut.Data = donut;
            chartDonut.CenterTitle = "总支出";
            chartDonut.CenterValue = Util.Money(outSum);
            chartDonut.InvalidateVisual();

            chartRank.Data = byCat.Take(9).Select(x => (x.cat, x.sum)).ToList();
            chartRank.InvalidateVisual();

            // 月度
            var byMonth = _list.GroupBy(t => t.date != null && t.date.Length >= 7 ? t.date.Substring(0, 7) : (t.date ?? ""))
                               .OrderBy(g => g.Key).ToList();
            chartMonthly.Data = byMonth.Select(g => (Short(g.Key), g.Where(t => t.type == "expense").Sum(t => t.amount) / 100.0)).ToList();
            chartMonthly.Data2 = byMonth.Select(g => (Short(g.Key), g.Where(t => t.type == "income").Sum(t => t.amount) / 100.0)).ToList();
            chartMonthly.InvalidateVisual();

            // 累计结余
            double run = 0;
            var line = new List<(string, double)>();
            foreach (var g in byMonth)
            {
                run += (g.Where(t => t.type == "income").Sum(t => t.amount)
                      - g.Where(t => t.type == "expense").Sum(t => t.amount)) / 100.0;
                line.Add((Short(g.Key), run));
            }
            chartBalance.Data = line;
            chartBalance.InvalidateVisual();

            // 账户
            listAccounts.Items.Clear();
            foreach (var a in Store.Data.accounts.Where(a => !a.archived).OrderByDescending(a => Store.BalanceOf(a.name)))
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var dot = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(ChartBase.ColorAt(listAccounts.Items.Count)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
                };
                var name = new TextBlock
                {
                    Text = a.name, Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5
                };
                var val = new TextBlock
                {
                    Text = Util.Money(Store.BalanceOf(a.name)),
                    FontFamily = (FontFamily)FindResource("NumFont"), FontSize = 12.5,
                    Foreground = (Brush)FindResource(Store.BalanceOf(a.name) >= 0 ? "TextBrush" : "ExpenseBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(name, 1); Grid.SetColumn(val, 2);
                g.Children.Add(dot); g.Children.Add(name); g.Children.Add(val);
                listAccounts.Items.Add(g);
            }
            txtAssets.Text = Util.Money(Store.NetAssets());

            BuildCompare();
        }

        // ==================== 各账本盈亏比较 ====================

        /// <summary>把「各账本盈亏比较」那张卡滚进视野（侧边栏「盈亏对比」按钮用）</summary>
        public void FocusCompare()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { cardCompare.BringIntoView(); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch { }
        }

        /// <summary>
        /// 每个账本在同一个统计区间里的 收入 / 支出 / 盈亏，横向排开。
        /// 数字全部按「分」算，显示走 Util.Money；一行都不改数据，只是算给眼睛看。
        /// </summary>
        private void BuildCompare()
        {
            listCompare.Items.Clear();
            rowCompareTotal.Children.Clear();

            var stats = Store.LedgerStats(string.IsNullOrEmpty(_from) ? null : _from,
                                          string.IsNullOrEmpty(_to) ? null : _to)
                              .OrderByDescending(s => s.expense).ToList();

            txtCompareRange.Text = string.IsNullOrEmpty(_from) || string.IsNullOrEmpty(_to)
                ? "· 全部时间"
                : "· " + _from + " 至 " + _to;

            if (stats.Count <= 1)
                txtCompareHint.Text = "现在只有一本账。侧边栏「＋ 新建账本」加一本，这里就能横向比了";
            else
                txtCompareHint.Text = "点一行＝切到那个账本，这里跟着重算";

            long maxIn = Math.Max(1, stats.Count == 0 ? 1 : stats.Max(s => s.income));
            long maxOut = Math.Max(1, stats.Count == 0 ? 1 : stats.Max(s => s.expense));

            foreach (var s in stats) listCompare.Items.Add(CompareRow(s, maxIn, maxOut));

            // 合计（只统计这张卡里出现的账本，和上面的数一一对得上）
            long totIn = stats.Sum(s => s.income), totOut = stats.Sum(s => s.expense);
            long totNet = totIn - totOut;
            int totCount = stats.Sum(s => s.count);

            // 合计行。踩过的坑记在这里，免得以后又改回去：
            // 右边这些数字不能用 HorizontalAlignment=Right 来靠右。
            // 那样元素是按「自己的尺寸」摆位置的，而这里的尺寸在
            // 嵌套 Grid 里会被算塌成几十像素宽，结果数字被裁得只剩开头两个字
            // （之前只显示「支 20,」就是这个原因，跟列宽无关）。
            // 正确做法：让元素先被撑满整列，再靠 TextAlignment 在内部右对齐。
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new TextBlock
            {
                Text = $"合计（{stats.Count} 本账 · {totCount} 笔）",
                Foreground = (Brush)FindResource("TextBrush"), FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            var mid = new TextBlock
            {
                Text = "收 " + Util.Money(totIn) + "　支 " + Util.Money(totOut),
                Foreground = (Brush)FindResource("SubBrush"), FontFamily = (FontFamily)FindResource("NumFont"),
                FontSize = 12.5, Margin = new Thickness(16, 0, 0, 0),
                TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            var right = new TextBlock
            {
                Text = "盈亏 " + Util.Yuan(totNet, true),
                Foreground = (Brush)FindResource(totNet >= 0 ? "IncomeBrush" : "ExpenseBrush"),
                FontFamily = (FontFamily)FindResource("NumFont"), FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(16, 0, 0, 0),
                TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(mid, 1);
            Grid.SetColumn(right, 2);
            g.Children.Add(left);
            g.Children.Add(mid);
            g.Children.Add(right);
            rowCompareTotal.Children.Add(g);
        }

        /// <summary>一个账本一行：名字 + 盈亏 + 收入/支出两条横条</summary>
        private Button CompareRow(Store.LedgerStat s, long maxIn, long maxOut)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("BtnGhost"),
                // 必须显式横向撑满：ItemsControl 里的按钮默认按内容宽度摆，
                // 盈亏数字会停在中间，右边空出一大截，对不齐上面标题行的右边缘。
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Tag = s.id,
                Background = s.current ? (Brush)FindResource("SelectedBrush") : Brushes.Transparent,
                ToolTip = s.current ? "现在看的就是这个账本" : "点一下切到「" + s.name + "」"
            };

            var stack = new StackPanel();

            // 第一行：色点 + 名字 + 盈亏
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                Background = LedgerUi.BrushOfHex(s.color), VerticalAlignment = VerticalAlignment.Center
            };
            var name = new TextBlock
            {
                Text = s.name + (s.current ? "（正在看）" : "") + (s.archived ? "（已归档）" : ""),
                Foreground = (Brush)FindResource("TextBrush"),
                FontSize = 13,
                Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var net = new TextBlock
            {
                Text = "盈亏 " + Util.Yuan(s.net, true),
                Foreground = (Brush)FindResource(s.net >= 0 ? "IncomeBrush" : "ExpenseBrush"),
                FontFamily = (FontFamily)FindResource("NumFont"),
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(net, 2);
            head.Children.Add(dot);
            head.Children.Add(name);
            head.Children.Add(net);
            stack.Children.Add(head);

            stack.Children.Add(Bar("收入", s.income, maxIn, "IncomeBrush"));
            stack.Children.Add(Bar("支出", s.expense, maxOut, "ExpenseBrush"));

            btn.Content = stack;
            if (!s.current) btn.Click += (a, b) => Store.SetCurrentLedger(s.id);
            return btn;
        }

        /// <summary>一条横条：标签 + 长度按最大值等比 + 金额。值都是分，除最大值的比例不变。</summary>
        private FrameworkElement Bar(string label, long val, long max, string brushKey)
        {
            double ratio = max <= 0 ? 0 : (double)val / max;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            var g = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var lab = new TextBlock
            {
                Text = label, Style = (Style)FindResource("Muted"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            var track = new Grid { Height = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, ratio), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star) });
            var fill = new Border
            {
                Background = (Brush)FindResource(brushKey),
                CornerRadius = new CornerRadius(4), Margin = new Thickness(0)
            };
            Grid.SetColumn(fill, 0);
            track.Children.Add(fill);
            var trackBg = new Border
            {
                Background = (Brush)FindResource("Surface2Brush"),
                CornerRadius = new CornerRadius(4),
                Child = track
            };

            var num = new TextBlock
            {
                Text = Util.Money(val),
                Foreground = (Brush)FindResource("SubBrush"),
                FontFamily = (FontFamily)FindResource("NumFont"), FontSize = 12,
                TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(trackBg, 1);
            Grid.SetColumn(num, 2);
            g.Children.Add(lab);
            g.Children.Add(trackBg);
            g.Children.Add(num);
            return g;
        }

        private static string Short(string ym)
            => ym != null && ym.Length >= 7 ? ym.Substring(5) + "月" : ym;

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

        private void Guard(Action act)
        {
            try { act(); }
            catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ExportXlsx_Click(object sender, RoutedEventArgs e) => Guard(() =>
        {
            if (_list.Count == 0) { MessageBox.Show("这个范围里没有记录。", "提示"); return; }
            var path = AskSave(Exporters.SuggestName("记账报表", "xlsx"), "Excel 工作簿 (*.xlsx)|*.xlsx");
            if (path == null) return;
            Exporters.Xlsx(_list, path);
            Done(path);
        });

        private void ExportDocx_Click(object sender, RoutedEventArgs e) => Guard(() =>
        {
            if (_list.Count == 0) { MessageBox.Show("这个范围里没有记录。", "提示"); return; }
            var path = AskSave(Exporters.SuggestName("记账报表", "docx"), "Word 文档 (*.docx)|*.docx");
            if (path == null) return;
            Exporters.DocxReport(_list, path, "记账报表　" + (cmbMonth.SelectedItem as string ?? "区间汇总"));
            Done(path);
        });

        private void ExportCsv_Click(object sender, RoutedEventArgs e) => Guard(() =>
        {
            if (_list.Count == 0) { MessageBox.Show("这个范围里没有记录。", "提示"); return; }
            var path = AskSave(Exporters.SuggestName("记账明细", "csv"), "CSV 文件 (*.csv)|*.csv");
            if (path == null) return;
            Exporters.Csv(_list, path);
            Done(path);
        });

        private void Done(string path)
        {
            var r = MessageBox.Show($"导出好了：\n\n{path}\n\n要现在打开它吗？", "导出完成",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { }
            }
        }
    }
}