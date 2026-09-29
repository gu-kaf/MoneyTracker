using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    public partial class BudgetView : UserControl, IRefreshable
    {
        private bool _ready;
        private string _month = Util.ThisMonth();

        public BudgetView()
        {
            InitializeComponent();
            _ready = true;
            Load();
        }

        public void Refresh()
        {
            _ready = false;
            Load();
            _ready = true;
        }

        private void Month_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || cmbMonth.SelectedItem == null) return;
            _month = cmbMonth.SelectedItem as string;
            Load();
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            _month = DateTime.Parse(_month + "-01").AddMonths(-1).ToString("yyyy-MM");
            cmbMonth.SelectedItem = _month;
            if (cmbMonth.SelectedItem == null) Load();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            _month = DateTime.Parse(_month + "-01").AddMonths(1).ToString("yyyy-MM");
            cmbMonth.SelectedItem = _month;
            if (cmbMonth.SelectedItem == null) Load();
        }

        private long SpentOf(string scope)
        {
            var (f, t) = Util.MonthRange(_month);
            var q = Store.Scoped().Where(x => x.type == "expense" && !x.excludeFromBudget
                        && string.CompareOrdinal(x.date, f) >= 0 && string.CompareOrdinal(x.date, t) <= 0);
            if (scope != "total") q = q.Where(x => x.category == scope);
            return q.Sum(x => x.amount);
        }

        private void Load()
        {
            if (!_ready || cmbMonth.Items.Count == 0)
            {
                var keep = _month;
                cmbMonth.Items.Clear();
                foreach (var m in Util.LastMonths(24).Reverse()) cmbMonth.Items.Add(m);
                cmbMonth.SelectedItem = cmbMonth.Items.Contains(keep) ? keep : cmbMonth.Items[cmbMonth.Items.Count - 1];
            }

            var budgets = Store.Data.budgets.Where(b => b.period == "monthly").ToList();
            var total = budgets.FirstOrDefault(b => b.scope == "total");
            long totalAmount = total?.amount ?? 0;
            long totalSpent = SpentOf("total");
            long allSpent = SpentOf("total");

            // ---- 总预算 ----
            if (total != null && totalAmount > 0)
            {
                txtTotalTitle.Text = $"{_month} 总预算";
                txtTotalUsed.Text = Util.Money(totalSpent);
                txtTotalUsed.Foreground = (Brush)FindResource(totalSpent > totalAmount ? "ExpenseBrush" : "TextBrush");
                txtTotalOf.Text = " / " + Util.Money(totalAmount);

                double ratio = Math.Min(1.0, (double)totalSpent / totalAmount);
                SetRatio(colTotalFill, colTotalRest, ratio);
                var over = totalSpent > totalAmount;
                barTotalFill.Background = (Brush)FindResource(
                    over ? "ExpenseBrush" : ratio >= total.alertThreshold ? "AccentBrush" : "IncomeBrush");

                long left = totalAmount - totalSpent;
                txtTotalLeft.Text = Util.Money(left);
                txtTotalLeft.Foreground = (Brush)FindResource(left >= 0 ? "IncomeBrush" : "ExpenseBrush");

                var (f, t2) = Util.MonthRange(_month);
                int daysLeft = Math.Max(0, (DateTime.Parse(t2) - DateTime.Today).Days + (DateTime.Today.ToString("yyyy-MM") == _month ? 1 : 0));
                txtDaysLeft.Text = daysLeft + " 天";
                txtTotalHint.Text = over
                    ? $"已经超了 {Util.Money(totalSpent - totalAmount)}，接下来收着点花"
                    : $"已用 {(totalSpent * 100.0 / totalAmount):0.0}%，日均还能花 {Util.Money(daysLeft > 0 ? left / daysLeft : left)}";
            }
            else
            {
                txtTotalTitle.Text = $"{_month} 还没有总预算";
                txtTotalUsed.Text = Util.Money(totalSpent);
                txtTotalOf.Text = "（未设置上限）";
                SetRatio(colTotalFill, colTotalRest, 1);
                barTotalFill.Background = (Brush)FindResource("AccentBrush");
                txtTotalLeft.Text = "—";
                txtDaysLeft.Text = "—";
                txtTotalHint.Text = "点右上角「新增一项预算」，把范围选成「全部支出」就能设总预算。";
            }

            // ---- 分类预算 ----
            listBudgets.Items.Clear();
            var cats = budgets.Where(b => b.scope != "total").OrderByDescending(b => b.amount).ToList();
            txtEmpty.Visibility = cats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtCount.Text = cats.Count > 0 ? $"共 {cats.Count} 项" : "";

            foreach (var b in cats)
            {
                long spent = SpentOf(b.scope);
                double ratio = b.amount > 0 ? (double)spent / b.amount : 0;
                bool over = spent > b.amount;

                var outer = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var left = new StackPanel();
                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var name = new TextBlock
                {
                    Text = b.scope, FontSize = 13.5,
                    Foreground = (Brush)FindResource("TextBrush")
                };
                var pct = new TextBlock
                {
                    Text = $"{(ratio * 100):0.0}%", HorizontalAlignment = HorizontalAlignment.Right,
                    FontFamily = (FontFamily)FindResource("NumFont"), FontSize = 12.5,
                    Foreground = (Brush)FindResource(over ? "ExpenseBrush" : ratio >= b.alertThreshold ? "AccentBrush" : "SubBrush")
                };
                Grid.SetColumn(pct, 1);
                head.Children.Add(name); head.Children.Add(pct);
                left.Children.Add(head);

                var barBg = new Border
                {
                    Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 7, 0, 0),
                    Background = (Brush)FindResource("Surface2Brush")
                };
                var barGrid = new Grid();
                barGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(Math.Max(0.001, Math.Min(1.0, ratio)), GridUnitType.Star)
                });
                barGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(Math.Max(0.001, 1 - Math.Min(1.0, ratio)), GridUnitType.Star)
                });
                var fill = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = (Brush)FindResource(over ? "ExpenseBrush" : ratio >= b.alertThreshold ? "AccentBrush" : "IncomeBrush")
                };
                Grid.SetColumn(fill, 0);
                barGrid.Children.Add(fill);
                barBg.Child = barGrid;
                left.Children.Add(barBg);

                var sub = new TextBlock
                {
                    Text = $"已用 {Util.Money(spent)} / {Util.Money(b.amount)}　" + (over
                        ? $"超支 {Util.Money(spent - b.amount)}"
                        : $"还剩 {Util.Money(b.amount - spent)}"),
                    FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
                    Foreground = (Brush)FindResource(over ? "ExpenseBrush" : "SubBrush")
                };
                left.Children.Add(sub);

                // 改 / 删
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
                var edit = new Button { Style = (Style)FindResource("BtnGhost"), Content = "改", Tag = b, Padding = new Thickness(10, 4, 10, 4) };
                edit.Click += (s, e) => EditBudget((Budget)((Button)s).Tag);
                var del = new Button { Style = (Style)FindResource("BtnGhost"), Content = "删", Tag = b, Padding = new Thickness(10, 4, 10, 4) };
                del.Click += (s, e) => DeleteBudget((Budget)((Button)s).Tag);
                btns.Children.Add(edit); btns.Children.Add(del);

                Grid.SetColumn(btns, 1);
                outer.Children.Add(left); outer.Children.Add(btns);
                listBudgets.Items.Add(outer);
            }

            // ---- 提醒 ----
            listAlerts.Items.Clear();
            var (f2, t3) = Util.MonthRange(_month);
            var monthTx = Store.Scoped().Where(x => x.type == "expense" && !x.excludeFromBudget
                && string.CompareOrdinal(x.date, f2) >= 0 && string.CompareOrdinal(x.date, t3) <= 0).ToList();

            void Alert(string text, bool warn)
            {
                listAlerts.Items.Add(new TextBlock
                {
                    Text = (warn ? "· " : "· ") + text,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 3),
                    Foreground = (Brush)FindResource(warn ? "ExpenseBrush" : "SubBrush"),
                    FontSize = 13
                });
            }

            int n = 0;
            foreach (var b in budgets)
            {
                if (b.amount <= 0) continue;
                long spent = SpentOf(b.scope);
                double r = (double)spent / b.amount;
                if (r > 1)
                {
                    Alert($"「{(b.scope == "total" ? "总预算" : b.scope)}」超支 {Util.Money(spent - b.amount)}，超了 {(r * 100 - 100):0.0}%", true);
                    n++;
                }
                else if (r >= b.alertThreshold)
                {
                    Alert($"「{(b.scope == "total" ? "总预算" : b.scope)}」已经用掉 {(r * 100):0.0}%，快到上限了", true);
                    n++;
                }
            }

            // 没设预算但花得多的分类
            var noBudget = monthTx.GroupBy(t => t.category)
                .Where(g => g.Sum(x => x.amount) > 0 && !budgets.Any(b => b.scope == g.Key))
                .Select(g => new { cat = g.Key, sum = g.Sum(x => x.amount) })
                .OrderByDescending(x => x.sum).Take(3).ToList();
            foreach (var x in noBudget)
            {
                if (x.sum > allSpent * 0.15)
                {
                    Alert($"「{x.cat}」花了 {Util.Money(x.sum)}，占了本月支出的 {(x.sum * 100.0 / Math.Max(1, allSpent)):0.0}%，还没设预算", false);
                    n++;
                }
            }

            if (n == 0) Alert("这个月各项都在预算内，保持住。", false);
            boxAlert.Visibility = Visibility.Visible;
        }

        // ==================== 增删改 ====================

        /// <summary>用比例列把进度条填到正确的位置（不用像素计算，缩放和高 DPI 都不会错）</summary>
        private static void SetRatio(ColumnDefinition fill, ColumnDefinition rest, double ratio)
        {
            double r = Math.Max(0, Math.Min(1.0, ratio));
            fill.Width = new GridLength(Math.Max(0.0001, r), GridUnitType.Star);
            rest.Width = new GridLength(Math.Max(0.0001, 1 - r), GridUnitType.Star);
        }

        private void Add_Click(object sender, RoutedEventArgs e) => EditBudget(null);

        private void EditBudget(Budget b)
        {
            var w = new BudgetWindow(b) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) Store.RaiseChanged();
        }

        private void DeleteBudget(Budget b)
        {
            if (MessageBox.Show($"删掉「{(b.scope == "total" ? "总预算" : b.scope)}」这一项预算？", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Store.Data.budgets.Remove(b);
            Store.Save();
            Store.RaiseChanged();
        }

        /// <summary>按近 3 个月的实际支出自动生成一套分类预算</summary>
        private void AutoFill_Click(object sender, RoutedEventArgs e)
        {
            var months = Util.LastMonths(3).Concat(Util.LastMonths(3, DateTime.Parse(_month + "-01").AddMonths(-1).ToString("yyyy-MM")))
                                            .Distinct().ToArray();
            var from = months.Min() + "-01";
            var to = months.Max() + "-31";

            var byCat = Store.Scoped()
                .Where(t => t.type == "expense" && !t.excludeFromBudget
                         && string.CompareOrdinal(t.date, from) >= 0 && string.CompareOrdinal(t.date, to) <= 0)
                .GroupBy(t => t.category)
                .Select(g => new { cat = g.Key, avg = g.Sum(x => x.amount) / months.Length })
                .Where(x => x.avg > 500)          // 平均每月超过 5 块才建预算
                .OrderByDescending(x => x.avg).ToList();

            if (byCat.Count == 0)
            {
                MessageBox.Show("最近几个月没有足够的支出数据，先记几笔再用这个功能。", "提示");
                return;
            }

            int added = 0, updated = 0;
            foreach (var x in byCat)
            {
                var exist = Store.Data.budgets.FirstOrDefault(b => b.period == "monthly" && b.scope == x.cat);
                long amt = (long)(Math.Round(x.avg / 1000.0) * 1000); // 取整到 10 元
                if (exist == null)
                {
                    Store.Data.budgets.Add(new Budget { period = "monthly", scope = x.cat, amount = amt, alertThreshold = 0.85 });
                    added++;
                }
                else if (exist.amount == 0) { exist.amount = amt; updated++; }
            }

            // 总预算 = 各分类之和 * 1.05（留点余地）
            long sum = Store.Data.budgets.Where(b => b.period == "monthly" && b.scope != "total").Sum(b => b.amount);
            var t = Store.Data.budgets.FirstOrDefault(b => b.period == "monthly" && b.scope == "total");
            if (t == null) Store.Data.budgets.Add(new Budget { period = "monthly", scope = "total", amount = (long)(sum * 1.05), alertThreshold = 0.8 });
            else { t.amount = (long)(sum * 1.05); updated++; }

            Store.Save();
            Store.RaiseChanged();
            MessageBox.Show($"好了：新增 {added} 项，更新 {updated} 项。金额是按最近 3 个月的平均支出算的，你可以逐项改。",
                "已生成预算");
        }
    }
}