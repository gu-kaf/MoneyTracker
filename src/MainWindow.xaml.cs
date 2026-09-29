using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();
        private string _current = "";

        public MainWindow()
        {
            InitializeComponent();

            foreach (var t in ThemeManager.All) cmbTheme.Items.Add(t.Name);
            cmbTheme.SelectedIndex = Math.Max(0, ThemeManager.All.FindIndex(t => t.Id == Store.Data.settings.theme));

            Loaded += (s, e) => Init();

            // 「净资产」是全程序口径（账户是共用的，不按账本分），旁边挂个说明免得误会
            txtNet.ToolTip = "所有账户、所有账本合计的净资产";

            Store.Changed += () =>
            {
                RefreshStatus();
                if (_pages.TryGetValue(_current, out var p) && p is IRefreshable r) r.Refresh();
            };
        }

        /// <summary>初始化（正常启动时由 Loaded 触发；离屏出图时手动调用）</summary>
        public void Init()
        {
            ThemeManager.Apply(Store.Data.settings.theme, Store.Data.settings.accent);
            // 字体和背景图是"跟窗口走"的，不能只在设置页里应用：
            // 不经过设置页就直接开程序（或者离屏出图）的时候也得是用户设好的样子。
            ThemeManager.ApplyFont(Store.Data.settings.fontId, Store.Data.settings.fontScale);
            ThemeManager.ApplyBackground(this, Store.Data.settings.bgImage,
                Store.Data.settings.bgBlur, Store.Data.settings.bgDim, Store.Data.settings.bgFit);
            Navigate("record");
            RefreshStatus();
        }

        /// <summary>把当前界面离屏渲染成 PNG——不需要真实窗口，用于自检界面效果</summary>
        public void RenderTo(string path, int w, int h)
        {
            var root = (FrameworkElement)Content;
            root.Width = w;
            root.Height = h;
            for (int i = 0; i < 3; i++)
            {
                root.Measure(new Size(w, h));
                root.Arrange(new Rect(0, 0, w, h));
                root.UpdateLayout();
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string key) Navigate(key);
        }

        public void Navigate(string key)
        {
            if (_current == key && _pages.ContainsKey(key)) return;
            _current = key;

            if (!_pages.TryGetValue(key, out var page))
            {
                page = key switch
                {
                    "record" => (UserControl)new RecordView(),
                    "list" => new ListPage(),
                    "report" => new ReportView(),
                    "budget" => new BudgetView(),
                    "analysis" => new AnalysisView(),
                    "settings" => new SettingsView(),
                    _ => new RecordView()
                };
                _pages[key] = page;
            }

            Host.Content = page;
            SyncNav(key);
            txtTitle.Text = key switch
            {
                "record" => "记账",
                "list" => "明细",
                "report" => "报表",
                "budget" => "预算",
                "analysis" => "分析",
                "settings" => "设置",
                _ => ""
            };
            if (page is IRefreshable r) r.Refresh();
            RefreshStatus();
        }

        /// <summary>让左边导航的高亮跟着当前页面走（包括从「盈亏对比」这种入口跳过来的情况）</summary>
        private void SyncNav(string key)
        {
            foreach (var rb in new[] { navRecord, navList, navReport, navBudget, navAnalysis, navSettings })
                rb.IsChecked = (rb.Tag as string) == key;
        }

        public void RefreshStatus()
        {
            RefreshLedgerList();

            long net = Store.NetAssets();
            txtNet.Text = "净资产 " + Util.Yuan(net);
            txtNet.Foreground = (System.Windows.Media.Brush)FindResource(net >= 0 ? "TextBrush" : "ExpenseBrush");

            var cur = Store.CurrentLedger;
            txtSubtitle.Text = cur == null
                ? Store.Data.transactions.Count + " 笔记录"
                : $"账本「{cur.name}」 · {Store.LedgerCount(cur.id)} 笔记录";
            txtStore.Text = "数据文件\n" + Store.FilePath;
        }

        // ==================== 账本切换 ====================

        /// <summary>
        /// 侧边栏那份账本清单：一眼看出现在是谁的账本，点一下切过去。
        /// 每次数据变化都重建一遍（账本没几个，重建比维护增量状态可靠）。
        /// </summary>
        private void RefreshLedgerList()
        {
            listLedgers.Children.Clear();
            string curId = Store.CurrentLedgerId;

            foreach (var l in Store.Ledgers)
            {
                bool on = l.id == curId;
                int n = Store.LedgerCount(l.id);

                var row = new Button
                {
                    Style = (Style)FindResource("BtnGhost"),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(8, 6, 8, 6),
                    Tag = l.id,
                    // 当前账本用「选中色」铺底，和左侧导航选中一眼是同一种"这里是现在的位置"
                    Background = on ? (Brush)FindResource("SelectedBrush") : Brushes.Transparent,
                    ToolTip = LedgerUi.DisplayName(l)
                              + (string.IsNullOrWhiteSpace(l.note) ? "" : "\n" + l.note)
                              + "\n" + n + " 笔记录"
                              + (on ? "\n（当前正在看这个账本）" : "\n点一下切到这个账本")
                };

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var dot = new Border
                {
                    Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                    Background = LedgerUi.BrushOf(l),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var name = new TextBlock
                {
                    Text = l.name + (l.archived ? "（归档）" : ""),
                    Foreground = (Brush)FindResource(on ? "TextBrush" : "SubBrush"),
                    FontSize = 12.5,
                    // 原本选中时给中文名加粗。细雅黑没有真粗体，WPF 只能拿细笔画算法撑出
                    // 假粗，字形发糊，反而不好看。选中态改用字色 + 底色 + 勾来区分，
                    // 所以这里统一 Normal。
                    FontWeight = FontWeights.Normal,
                    Margin = new Thickness(8, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var cnt = new TextBlock
                {
                    Text = on ? "✓ " + n : n.ToString(),
                    Foreground = (Brush)FindResource(on ? "AccentBrush" : "SubBrush"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid.SetColumn(name, 1);
                Grid.SetColumn(cnt, 2);
                g.Children.Add(dot);
                g.Children.Add(name);
                g.Children.Add(cnt);
                row.Content = g;
                row.Click += LedgerRow_Click;
                listLedgers.Children.Add(row);
            }

            if (Store.Ledgers.Count == 0)
                listLedgers.Children.Add(new TextBlock
                {
                    Text = "还没有账本", Style = (Style)FindResource("Muted"), Margin = new Thickness(6, 4, 6, 4)
                });
        }

        private void LedgerRow_Click(object sender, RoutedEventArgs e)
        {
            var id = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(id) || id == Store.CurrentLedgerId) return;
            // SetCurrentLedger 自己会 Save + RaiseChanged，状态栏和当前页面跟着刷新
            if (!Store.SetCurrentLedger(id))
                MessageBox.Show(Store.LastError ?? "切换账本失败。", "账本",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ManageLedgers_Click(object sender, RoutedEventArgs e) => OpenLedgers();

        private void Compare_Click(object sender, RoutedEventArgs e) => ShowCompare();

        private void AddLedger_Click(object sender, RoutedEventArgs e) => OpenLedgers(startNew: true);

        /// <summary>打开账本管理窗口（startNew=true 直接进"新建"状态）</summary>
        public void OpenLedgers(string selectId = null, bool startNew = false)
        {
            var w = new LedgerWindow(selectId, startNew);
            if (IsLoaded && IsVisible) w.Owner = this;
            w.ShowDialog();

            // 窗口里改过的账本可能包括当前账本的名称/颜色，回来整体刷一遍
            RefreshStatus();
            if (_pages.TryGetValue(_current, out var p) && p is IRefreshable r) r.Refresh();
        }

        // ==================== 账本盈亏对比（跳报表页） ====================

        /// <summary>切到报表页并把「各账本盈亏比较」那张卡滚进视野</summary>
        public void ShowCompare()
        {
            Navigate("report");
            if (_pages.TryGetValue("report", out var p) && p is ReportView rv) rv.FocusCompare();
        }

        private void Theme_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTheme.SelectedIndex < 0) return;
            var t = ThemeManager.All[cmbTheme.SelectedIndex];
            Store.Data.settings.theme = t.Id;
            ThemeManager.Apply(t.Id, Store.Data.settings.accent);
            Store.Save();
        }
    }

    /// <summary>页面实现这个接口，就能在数据变化时被主窗口通知刷新</summary>
    public interface IRefreshable
    {
        void Refresh();
    }
}