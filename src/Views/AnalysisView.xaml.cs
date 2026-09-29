using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    public partial class AnalysisView : UserControl, IRefreshable
    {
        private bool _ready;
        private string _month = Util.ThisMonth();

        public AnalysisView()
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

        private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

        private void Load()
        {
            if (cmbMonth.Items.Count == 0 || !cmbMonth.Items.Contains(_month))
            {
                var keep = _month;
                cmbMonth.Items.Clear();
                foreach (var m in Util.LastMonths(24)) cmbMonth.Items.Add(m);
                cmbMonth.SelectedItem = cmbMonth.Items.Contains(keep) ? keep : cmbMonth.Items[0];
                _month = cmbMonth.SelectedItem as string ?? Util.ThisMonth();
            }

            var (f, t) = Util.MonthRange(_month);
            var cur = Store.Scoped().Where(x => !string.IsNullOrEmpty(x.date)
                        && string.CompareOrdinal(x.date, f) >= 0 && string.CompareOrdinal(x.date, t) <= 0).ToList();
            long outSum = cur.Where(x => x.type == "expense").Sum(x => x.amount);
            long inSum = cur.Where(x => x.type == "income").Sum(x => x.amount);

            txtOut.Text = Util.Yuan(outSum);
            txtOut.Foreground = (Brush)FindResource("ExpenseBrush");
            txtIn.Text = Util.Yuan(inSum);

            long net = inSum - outSum;
            txtNet.Text = "结余 " + Util.Yuan(net);
            txtNet.Foreground = (Brush)FindResource(net >= 0 ? "TextBrush" : "ExpenseBrush");

            // 环比
            var prevM = DateTime.Parse(_month + "-01").AddMonths(-1).ToString("yyyy-MM");
            var (pf, pt) = Util.MonthRange(prevM);
            long prevOut = Store.Scoped().Where(x => x.type == "expense" && !string.IsNullOrEmpty(x.date)
                        && string.CompareOrdinal(x.date, pf) >= 0 && string.CompareOrdinal(x.date, pt) <= 0)
                        .Sum(x => x.amount);
            if (prevOut > 0)
            {
                double chg = (outSum - prevOut) * 100.0 / prevOut;
                txtCompare.Text = $"上月 {Util.Money(prevOut)}，{(chg >= 0 ? "多了" : "少了")} {Math.Abs(chg):0.0}%";
                txtCompare.Foreground = (Brush)FindResource(chg > 0 ? "ExpenseBrush" : "IncomeBrush");
            }
            else txtCompare.Text = "上月没有可比数据";

            // 储蓄率
            double rate = inSum > 0 ? (inSum - outSum) * 100.0 / inSum : 0;
            txtRate.Text = inSum > 0
                ? $"{(rate >= 0 ? rate : 0):0.0}%　（健康区间 20%~30%）"
                : "这个月还没有收入记录";
            double maxW = Math.Max(80, boxRate.ActualWidth > 0 ? boxRate.ActualWidth : 320);
            double fillRatio = Math.Min(1, Math.Max(0, rate / 100.0));
            colRateFill.Width = new GridLength(Math.Max(0.0001, fillRatio), GridUnitType.Star);
            colRateRest.Width = new GridLength(Math.Max(0.0001, 1 - fillRatio), GridUnitType.Star);
            barRate.Background = (Brush)FindResource(rate >= 20 ? "IncomeBrush" : rate >= 0 ? "AccentBrush" : "ExpenseBrush");

            // 趋势图
            chartTrend.Data = Analyzer.MonthlyExpense(6, _month);
            chartTrend.InvalidateVisual();

            // 分类
            var byCat = cur.Where(x => x.type == "expense")
                           .GroupBy(x => string.IsNullOrEmpty(x.category) ? "未分类" : x.category)
                           .Select(g => new { cat = g.Key, sum = (double)g.Sum(x => x.amount) / 100.0 })
                           .OrderByDescending(x => x.sum).Take(10).ToList();
            chartCat.Data = byCat.Select(x => (x.cat, x.sum)).ToList();
            chartCat.InvalidateVisual();

            // 洞察
            listInsights.Items.Clear();
            foreach (var ins in Analyzer.Run(_month))
                listInsights.Items.Add(BuildCard(ins));
        }

        private Border BuildCard(Insight ins)
        {
            var surface2 = (Brush)FindResource("Surface2Brush");
            var textBrush = (Brush)FindResource("TextBrush");
            var subBrush = (Brush)FindResource("SubBrush");

            Brush accent;
            switch (ins.Level)
            {
                case "warn": accent = (Brush)FindResource("ExpenseBrush"); break;
                case "good": accent = (Brush)FindResource("IncomeBrush"); break;
                default: accent = (Brush)FindResource("AccentBrush"); break;
            }

            var card = new Border
            {
                Background = surface2,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                Background = accent, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0)
            };
            icon.Child = new TextBlock
            {
                Text = ins.Icon, Foreground = (Brush)FindResource("OnAccentBrush"),
                FontSize = 14, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            g.Children.Add(icon);

            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = ins.Title, FontSize = 13.5,
                Foreground = textBrush, TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrEmpty(ins.Text))
                body.Children.Add(new TextBlock
                {
                    Text = ins.Text, FontSize = 12.5, Foreground = subBrush,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19
                });

            Grid.SetColumn(body, 1);
            g.Children.Add(body);
            card.Child = g;
            return card;
        }
    }
}