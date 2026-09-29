using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoneyTracker
{
    /// <summary>规则表格的一行</summary>
    public class RuleRow
    {
        public Rule R { get; set; }
        public string Keyword => R.keyword;
        public string Category => R.setCategory;
        public string Sub => R.setSubcategory;
        public string TypeText => string.IsNullOrEmpty(R.setType) ? "不限" : Util.TypeLabel(R.setType);
        public int Priority => R.priority;
        public string Hits => R.hitCount + " 次";
    }

    /// <summary>账户表格的一行</summary>
    public class AccountRow
    {
        public Account A { get; set; }
        public string Name => A.name;
        public string Kind => Exporters.KindText(A.kind);
        public string Initial => Util.Money(A.initialBalance);
        public string Now => Util.Money(Store.BalanceOf(A.name));
        public string InAssets => A.includeInAssets ? "是" : "否";
    }

    public partial class SettingsView : UserControl, IRefreshable
    {
        private bool _ready;

        public SettingsView()
        {
            InitializeComponent();
            _ready = true;
            BuildThemes();
            BuildAccents();
            LoadPrefs();
            LoadRules();
            LoadAccounts();
            LoadAbout();
            LoadBackupInfo();
            LoadCatInfo();
        }

        public void Refresh()
        {
            LoadRules();
            LoadAccounts();
            LoadAbout();
            LoadBackupInfo();
            LoadCatInfo();
        }

        // ==================== 分类管理 ====================

        private void OpenCategory_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var w = new CategoryWindow();
            if (owner != null) w.Owner = owner;

            if (w.ShowDialog() == true)
            {
                // 分类改过名的话，别处显示的分类名也变了，整个刷新一遍最稳
                Store.RaiseChanged();
                LoadRules();
                LoadAccounts();
                LoadAbout();
            }
            LoadCatInfo();
        }

        /// <summary>顺手在设置页上显示「现在有多少个分类、多少项细分」</summary>
        private void LoadCatInfo()
        {
            if (txtCatInfo == null) return;
            try
            {
                var ex = Categories.ExpenseList;
                var inc = Categories.IncomeList;
                int exSub = ex.Sum(g => (g.subs ?? new List<string>()).Count);
                int incSub = inc.Sum(g => (g.subs ?? new List<string>()).Count);
                txtCatInfo.Text = "现在有 " + ex.Count + " 个支出分类（" + exSub + " 项细分）、"
                    + inc.Count + " 个收入分类（" + incSub + " 项细分）。";
            }
            catch
            {
                txtCatInfo.Text = "";
            }
        }

        // ==================== 外观 ====================

        private void BuildThemes()
        {
            panelThemes.Children.Clear();
            foreach (var t in ThemeManager.All)
            {
                bool on = Store.Data.settings.theme == t.Id;

                var content = new StackPanel { Margin = new Thickness(2) };
                var swatch = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var hex in new[] { t.Bg, t.Surface, t.Accent, t.Expense, t.Income })
                {
                    swatch.Children.Add(new Border
                    {
                        Width = 22, Height = 22, Margin = new Thickness(0, 0, 4, 0),
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(ThemeManager.Parse(hex)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                        BorderThickness = new Thickness(1)
                    });
                }
                content.Children.Add(swatch);
                content.Children.Add(new TextBlock
                {
                    Text = t.Name, FontSize = 13,
                    Foreground = (Brush)FindResource("TextBrush")
                });
                content.Children.Add(new TextBlock
                {
                    Text = t.Desc, FontSize = 11, Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap, Width = 168,
                    Foreground = (Brush)FindResource("SubBrush")
                });

                var btn = new Button
                {
                    Content = content,
                    Width = 196,
                    Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(12, 12, 12, 12),
                    Tag = t.Id,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Style = (Style)FindResource("Btn"),
                    Background = (Brush)FindResource(on ? "SelectedBrush" : "Surface2Brush"),
                    BorderBrush = (Brush)FindResource(on ? "AccentBrush" : "BorderBrush2"),
                    BorderThickness = new Thickness(on ? 2 : 1),
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                btn.Click += (s, e) =>
                {
                    var id = (string)((Button)s).Tag;
                    Store.Data.settings.theme = id;
                    ThemeManager.Apply(id, Store.Data.settings.accent);
                    Store.Save();
                    BuildThemes();
                    ShowNow();
                };
                panelThemes.Children.Add(btn);
            }
            ShowNow();
        }

        private void ShowNow()
        {
            var t = ThemeManager.Current;
            txtThemeNow.Text = $"当前用的是「{t.Name}」" + (t.Dark ? "（深色）" : "（浅色）")
                + (string.IsNullOrWhiteSpace(Store.Data.settings.accent) ? "，强调色跟随主题。" : $"，强调色自定义为 {Store.Data.settings.accent}。");
        }

        private void BuildAccents()
        {
            panelAccents.Children.Clear();
            string cur = Store.Data.settings.accent ?? "";
            foreach (var (name, hex) in ThemeManager.AccentPresets)
            {
                bool on = (hex == "" && string.IsNullOrWhiteSpace(cur)) ||
                          (!string.IsNullOrWhiteSpace(hex) && string.Equals(hex, cur, StringComparison.OrdinalIgnoreCase));
                string show = string.IsNullOrWhiteSpace(hex) ? ThemeManager.Current.Accent : hex;

                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Border
                {
                    Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(ThemeManager.Parse(show)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0)
                });
                sp.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });

                var btn = new Button
                {
                    Content = sp,
                    Style = (Style)FindResource(on ? "ChipOn" : "Chip"),
                    Tag = hex
                };
                btn.Click += (s, e) =>
                {
                    var v = (string)((Button)s).Tag;
                    Store.Data.settings.accent = v;
                    ThemeManager.Apply(Store.Data.settings.theme, string.IsNullOrWhiteSpace(v) ? null : v);
                    Store.Save();
                    BuildAccents();
                    BuildThemes();
                };
                panelAccents.Children.Add(btn);
            }
        }

        private void ApplyAccent_Click(object sender, RoutedEventArgs e)
        {
            var hex = (txtAccentHex.Text ?? "").Trim();
            if (hex.Length > 0 && !hex.StartsWith("#")) hex = "#" + hex;
            if (hex.Length != 7 || !System.Text.RegularExpressions.Regex.IsMatch(hex, @"^#[0-9A-Fa-f]{6}$"))
            {
                MessageBox.Show("色号要写成 #RRGGBB 的形式，比如 #2F6FED。", "格式不对");
                return;
            }
            Store.Data.settings.accent = hex;
            ThemeManager.Apply(Store.Data.settings.theme, hex);
            Store.Save();
            BuildAccents();
            BuildThemes();
        }

        private void ResetAccent_Click(object sender, RoutedEventArgs e)
        {
            Store.Data.settings.accent = "";
            txtAccentHex.Text = "";
            ThemeManager.Apply(Store.Data.settings.theme, null);
            Store.Save();
            BuildAccents();
            BuildThemes();
        }

        // ==================== 字体 ====================

        private void BuildFonts()
        {
            panelFonts.Children.Clear();
            string now = Store.Data.settings.fontId;

            foreach (var f in ThemeManager.Fonts)
            {
                bool on = now == f.id;
                AddFontCard(f.id, f.name, f.desc, f.family, on);
            }

            // 用户装了字体文件的话，多给一张卡片，点它就用自己那个字体
            if (ThemeManager.FontFileExists(Store.Data.settings.fontFile))
            {
                AddFontCard("custom", "我自己装的字体",
                    System.IO.Path.GetFileName(Store.Data.settings.fontFile), null,
                    now == "custom");
            }

            txtFontScale.Text = Store.Data.settings.fontScale + "%";
            if (txtFontFile != null)
            {
                txtFontFile.Text = string.IsNullOrWhiteSpace(Store.Data.settings.fontFile)
                    ? "现在没有用自定义字体文件。"
                    : "正在用：" + Store.Data.settings.fontFile;
            }
        }

        /// <summary>字体选项的一张卡。family 传 null 表示用当前已加载的自定义字体。</summary>
        private void AddFontCard(string id, string name, string desc, string family, bool on)
        {
            var sp = new StackPanel { Margin = new Thickness(2) };

            // 每张卡自己用自己的字体渲染，一眼就能看出区别
            FontFamily fam = family != null
                ? new FontFamily(family)
                : (Application.Current.TryFindResource("UiFont") as FontFamily
                   ?? new FontFamily("Microsoft YaHei UI"));

            sp.Children.Add(new TextBlock
            {
                Text = "记一笔 1234.56",
                FontFamily = fam,
                FontSize = 17,
                Foreground = (Brush)FindResource("TextBrush")
            });
            sp.Children.Add(new TextBlock
            {
                // 这里刻意不加粗：细体字被算法加粗会糊，反而看不清是哪一套
                Text = name,
                FontFamily = fam,
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("TextBrush")
            });
            sp.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 11, Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap, Width = 168,
                Foreground = (Brush)FindResource("SubBrush")
            });

            var btn = new Button
            {
                Content = sp,
                Width = 196,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource("Btn"),
                Tag = id
            };
            if (on)
            {
                btn.BorderBrush = (Brush)FindResource("AccentBrush");
                btn.BorderThickness = new Thickness(2);
            }
            btn.Click += Font_Click;
            panelFonts.Children.Add(btn);
        }

        private void PickFontFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "挑一个字体文件",
                Filter = "字体文件|*.ttf;*.otf;*.ttc|所有文件|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            // 先确认这个文件真能读出字体，免得写进设置变成一个打不开的字体
            var fam = ThemeManager.LoadFontFile(dlg.FileName);
            if (fam == null)
            {
                MessageBox.Show("这个文件读不出字体，可能不是字体文件，或者已经损坏了。",
                    "字体", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Store.Data.settings.fontFile = dlg.FileName;
            Store.Data.settings.fontId = "custom";
            ApplyFont();
            Store.Save();
            BuildFonts();
        }

        private void ClearFontFile_Click(object sender, RoutedEventArgs e)
        {
            Store.Data.settings.fontFile = "";
            if (Store.Data.settings.fontId == "custom") Store.Data.settings.fontId = "soft";
            ApplyFont();
            Store.Save();
            BuildFonts();
        }

        private void Font_Click(object sender, RoutedEventArgs e)
        {
            var b = sender as Button;
            if (b == null || b.Tag == null) return;
            Store.Data.settings.fontId = b.Tag.ToString();
            ApplyFont();
            Store.Save();
            BuildFonts();
        }

        private void FontScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_ready || txtFontScale == null) return;
            Store.Data.settings.fontScale = (int)Math.Round(e.NewValue);
            ApplyFont();
        }

        private void ApplyFont()
        {
            var s = Store.Data.settings;
            ThemeManager.ApplyFont(s.fontId, s.fontScale);
            if (txtFontScale != null) txtFontScale.Text = s.fontScale + "%";
        }

        // ==================== 背景图 ====================

        private void PickBg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "挑一张图当背景",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            Store.Data.settings.bgImage = dlg.FileName;
            ApplyBg();
            Store.Save();
        }

        private void ClearBg_Click(object sender, RoutedEventArgs e)
        {
            Store.Data.settings.bgImage = "";
            ApplyBg();
            Store.Save();
        }

        private void BgBlur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_ready || txtBgBlur == null) return;
            Store.Data.settings.bgBlur = (int)Math.Round(e.NewValue);
            ApplyBg();
        }

        private void BgDim_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_ready || txtBgDim == null) return;
            Store.Data.settings.bgDim = (int)Math.Round(e.NewValue);
            ApplyBg();
        }

        /// <summary>背景图要画在主窗口上，所以要顺着可视树找到宿主窗口</summary>
        private void ApplyBg()
        {
            var s = Store.Data.settings;
            var win = Window.GetWindow(this);

            if (txtBgBlur != null) txtBgBlur.Text = s.bgBlur.ToString();
            if (txtBgDim != null) txtBgDim.Text = s.bgDim.ToString();
            if (sldBgBlur != null && Math.Abs(sldBgBlur.Value - s.bgBlur) > 0.5) sldBgBlur.Value = s.bgBlur;
            if (sldBgDim != null && Math.Abs(sldBgDim.Value - s.bgDim) > 0.5) sldBgDim.Value = s.bgDim;

            if (txtBgPath != null)
            {
                txtBgPath.Text = string.IsNullOrEmpty(s.bgImage)
                    ? "当前没有使用背景图。"
                    : "当前背景图：" + s.bgImage;
            }

            ThemeManager.ApplyBackground(win, s.bgImage, s.bgBlur, s.bgDim, s.bgFit);
        }

        // ==================== 备份与回档 ====================

        private void OpenBackup_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            bool restored = BackupWindow.ShowDialog(owner);

            // 真回档了就把界面上的数据全部重读一遍，不然看到的还是旧数
            if (restored)
            {
                Store.RaiseChanged();
                BuildThemes();
                BuildAccents();
                BuildFonts();
                LoadPrefs();
                LoadRules();
                LoadAccounts();
                LoadAbout();
            }
            LoadBackupInfo();
        }

        /// <summary>在设置页上顺手显示「已经有几个快照、占了多大」</summary>
        private void LoadBackupInfo()
        {
            if (txtBackupInfo == null) return;
            try
            {
                var list = Store.ListSnapshots();
                if (list.Count == 0)
                {
                    txtBackupInfo.Text = "现在还没有任何快照。点进「备份与回档」里按一下「立即备份」就有一个了。";
                    return;
                }

                long total = 0;
                foreach (var s in list) total += s.Size;
                txtBackupInfo.Text = "现在有 " + list.Count + " 个快照，一共 " + Store.SizeText(total)
                    + "，最近一次是 " + list[0].TimeText + "（" + list[0].ReasonText + "）。";
            }
            catch
            {
                txtBackupInfo.Text = "";
            }
        }

        private void LoadPrefs()
        {
            chkRedExpense.IsChecked = Store.Data.settings.redExpense;
            txtCurrency.Text = Store.Data.settings.currency;

            var s = Store.Data.settings;
            if (sldFontScale != null) sldFontScale.Value = s.fontScale;
            if (sldBgBlur != null) sldBgBlur.Value = s.bgBlur;
            if (sldBgDim != null) sldBgDim.Value = s.bgDim;

            BuildFonts();
            ApplyFont();
            ApplyBg();
        }

        private void Pref_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            Store.Data.settings.redExpense = chkRedExpense.IsChecked == true;
            var c = (txtCurrency.Text ?? "").Trim();
            if (c.Length > 0) Store.Data.settings.currency = c;
            Store.Save();
        }

        // ==================== 导入 ====================

        private void ShowImportHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "认哪些文件：\n\n" +
                "1) 支付宝账单（「交易创建时间」「交易对方」「收/支」那一套）\n" +
                "2) 微信支付账单（「交易时间」「交易对方」「当前状态」那一套）\n" +
                "3) 任何表格导出的普通 CSV，只要有日期和金额两列就行\n\n" +
                "自动处理的事：\n" +
                "· 编码自动认（UTF-8、带 BOM、GBK 都行，支付宝微信导出的默认就是 GBK）\n" +
                "· 表头按列名匹配，不要求列的顺序\n" +
                "· 「不计收支」的记录跳过\n" +
                "· 退款、交易关闭、未支付的记录跳过\n" +
                "· 重复的记录（同一天、同金额、同商户、同账户）自动去重\n" +
                "· 认出来的商户会拿去和你的自动记账规则对上，直接分好类\n" +
                "· 支付方式能对上你的账户名就自动归到那个账户，对不上算现金",
                "导入规则说明");
        }

        private void ImportFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "账单 / CSV 文件 (*.csv;*.txt)|*.csv;*.txt|所有文件 (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            var results = new List<ImportResult>();
            foreach (var f in dlg.FileNames)
                results.Add(CsvImporter.ImportFile(f, Store.Data.settings.allowDuplicateImport));

            ShowImport(results);
        }

        private void ImportFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            if (dlg.ShowDialog() != true) return;
            ShowImport(CsvImporter.ImportFolder(dlg.FolderName, Store.Data.settings.allowDuplicateImport));
        }

        private void ShowImport(List<ImportResult> results)
        {
            boxImportResult.Visibility = Visibility.Visible;
            listImportSkip.Items.Clear();

            int added = results.Sum(r => r.Added);
            int skipped = results.Sum(r => r.Skipped);
            int dup = results.Sum(r => r.Duplicated);
            long inSum = results.Sum(r => r.InSum);
            long outSum = results.Sum(r => r.OutSum);

            var fmt = results.Select(r => r.Format).Distinct().ToList();
            string fmtText = fmt.Count == 0 ? "" : string.Join(" + ", fmt.Select(f => f switch
            {
                "alipay" => "支付宝账单",
                "wechat" => "微信账单",
                _ => "普通 CSV"
            }));

            txtImportTitle.Text = added > 0
                ? $"导入完成：新进来 {added} 笔"
                : "没有导入新记录";

            txtImportDetail.Text =
                $"读的是：{fmtText}\n" +
                $"新导入 {added} 笔（支出 {Util.Yuan(outSum)}，收入 {Util.Yuan(inSum)}）\n" +
                $"跳过 {skipped} 笔，重复去掉 {dup} 笔\n\n" +
                string.Join("\n", results.Select(r => "· " + r.Summary)) +
                (results.SelectMany(r => r.Errors).Any()
                    ? "\n\n问题：\n" + string.Join("\n", results.SelectMany(r => r.Errors).Select(x => "· " + x))
                    : "");

            var reasons = results.SelectMany(r => r.SkipReasons).Distinct().Take(12).ToList();
            if (reasons.Count > 0)
            {
                listImportSkip.Items.Add(new TextBlock
                {
                    Text = "跳过这些是因为：",
                    FontSize = 12.5,
                    Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 6, 0, 4)
                });
                foreach (var r in reasons)
                    listImportSkip.Items.Add(new TextBlock
                    {
                        Text = "· " + r, FontSize = 12,
                        Foreground = (Brush)FindResource("SubBrush"), Margin = new Thickness(4, 2, 0, 2),
                        TextWrapping = TextWrapping.Wrap
                    });
            }
        }

        // ==================== 导出 ====================

        private string AskSave(string name, string filter)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = name, Filter = filter,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void Run(Action act)
        {
            try { act(); }
            catch (Exception ex) { MessageBox.Show("出错了：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private List<Transaction> All() => Store.Query().ToList();

        private void ExportXlsx_Click(object sender, RoutedEventArgs e) => Run(() =>
        {
            var list = All();
            if (list.Count == 0) { MessageBox.Show("还没有任何记录。", "提示"); return; }
            var path = AskSave(Exporters.SuggestName("记账报表", "xlsx"), "Excel 工作簿 (*.xlsx)|*.xlsx");
            if (path == null) return;
            Exporters.Xlsx(list, path);
            Finish(path);
        });

        private void ExportDocx_Click(object sender, RoutedEventArgs e) => Run(() =>
        {
            var list = All();
            if (list.Count == 0) { MessageBox.Show("还没有任何记录。", "提示"); return; }
            var path = AskSave(Exporters.SuggestName("记账报表", "docx"), "Word 文档 (*.docx)|*.docx");
            if (path == null) return;
            Exporters.DocxReport(list, path);
            Finish(path);
        });

        private void ExportCsv_Click(object sender, RoutedEventArgs e) => Run(() =>
        {
            var list = All();
            if (list.Count == 0) { MessageBox.Show("还没有任何记录。", "提示"); return; }
            var path = AskSave(Exporters.SuggestName("记账明细", "csv"), "CSV 文件 (*.csv)|*.csv");
            if (path == null) return;
            Exporters.Csv(list, path);
            Finish(path);
        });

        private void ExportJson_Click(object sender, RoutedEventArgs e) => Run(() =>
        {
            var path = AskSave($"记账备份_{DateTime.Now:yyyyMMdd_HHmm}.json", "JSON 文件 (*.json)|*.json");
            if (path == null) return;
            Exporters.JsonBackup(path);
            Finish(path);
        });

        private void Finish(string path)
        {
            var r = MessageBox.Show($"导出好了：\n\n{path}\n\n要现在打开它吗？", "导出完成",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { }
            }
        }

        // ==================== 恢复与重置 ====================

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON 备份 (*.json)|*.json|所有文件 (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var data = System.Text.Json.JsonSerializer.Deserialize<AppData>(json);
                if (data == null)
                {
                    // 也许只是「只有交易」的那种备份
                    var mini = System.Text.Json.JsonSerializer.Deserialize<MiniBackup>(json);
                    if (mini?.transactions == null || mini.transactions.Count == 0)
                    {
                        MessageBox.Show("这个文件读不出记账数据，可能不是本程序导出的备份。", "读不了");
                        return;
                    }
                    data = Store.Data;
                    data.transactions = mini.transactions;
                    foreach (var t in data.transactions) if (string.IsNullOrEmpty(t.hash)) t.hash = Util.HashOf(t);
                }

                var r = MessageBox.Show(
                    $"备份里是 {data.transactions?.Count ?? 0} 笔记录。\n\n" +
                    "恢复会用备份里的数据替换现在全部记录（当前数据会先自动另存一份，随时能翻回来）。要继续吗？",
                    "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;

                // 先备份当前
                var safe = Path.Combine(Store.Dir, $"before-restore-{DateTime.Now:yyyyMMdd_HHmmss}.json");
                Exporters.JsonBackup(safe);

                if (data.accounts == null || data.accounts.Count == 0) data.accounts = Store.Data.accounts;
                if (data.rules == null || data.rules.Count == 0) data.rules = Store.Data.rules;
                if (data.settings == null) data.settings = Store.Data.settings;
                data.budgets ??= new List<Budget>();

                Store.ReplaceAll(data);
                MessageBox.Show($"恢复好了，现在有 {data.transactions.Count} 笔记录。\n\n恢复前的数据存了一份在：\n{safe}",
                    "完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show("恢复失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class MiniBackup
        {
            public List<Transaction> transactions { get; set; }
        }

        private void Demo_Click(object sender, RoutedEventArgs e)
        {
            if (Store.Data.transactions.Count > 0)
            {
                var r = MessageBox.Show("现在已经有一些记录了。示例数据会加在它们后面（不会删你的记录），继续吗？",
                    "生成示例数据", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            int n = Demo.Fill();
            MessageBox.Show($"加了 {n} 笔示例数据，去报表和分析页看看效果。\n\n不想要了就在这个页面点「清空所有记录」。",
                "好了");
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (Store.Data.transactions.Count == 0) { MessageBox.Show("本来就没有记录。", "提示"); return; }
            var r = MessageBox.Show(
                $"要清空全部 {Store.Data.transactions.Count} 笔记录吗？账户、规则、主题设置会保留。\n\n" +
                "清空前会自动存一份备份，万一点错了可以恢复。", "清空数据",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            var safe = Path.Combine(Store.Dir, $"before-clear-{DateTime.Now:yyyyMMdd_HHmmss}.json");
            Exporters.JsonBackup(safe);
            Store.Data.transactions.Clear();
            Store.Data.budgets.Clear();
            Store.Save();
            Store.RaiseChanged();
            MessageBox.Show($"清空了。清空前的数据在这儿：\n{safe}", "完成");
        }

        // ==================== 规则 ====================

        private void LoadRules()
        {
            gridRules.ItemsSource = Store.Data.rules
                .OrderByDescending(r => r.priority)
                .Select(r => new RuleRow { R = r }).ToList();
        }

        private Rule CurrentRule()
        {
            if (gridRules.CurrentItem is RuleRow r) return r.R;
            if (gridRules.SelectedItems.Count > 0) return ((RuleRow)gridRules.SelectedItems[0]).R;
            return null;
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            var w = new RuleWindow(null) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) { LoadRules(); Store.RaiseChanged(); }
        }

        private void Rule_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => RuleEdit_Click(sender, e);

        private void RuleEdit_Click(object sender, RoutedEventArgs e)
        {
            var r = CurrentRule();
            if (r == null) return;
            var w = new RuleWindow(r) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) { LoadRules(); Store.RaiseChanged(); }
        }

        private void RuleDelete_Click(object sender, RoutedEventArgs e)
        {
            var r = CurrentRule();
            if (r == null) return;
            if (MessageBox.Show($"删掉规则「{r.keyword}」？", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Store.Data.rules.Remove(r);
            Store.Save();
            LoadRules();
        }

        private void RerunRules_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "会对所有历史记录重新跑一遍规则。\n\n" +
                "只改那些「还没分类」或者能匹配到更高优先级规则的记录，你手动改过的分类不会被覆盖。要继续吗？",
                "重跑规则", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            int n = Store.RerunRules();
            MessageBox.Show(n > 0 ? $"更新了 {n} 笔记录的分类。" : "没有需要更新的记录。", "完成");
            LoadRules();
        }

        private void ResetRules_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show("恢复成程序预置的那套规则？你自己加的规则会被替换掉。", "恢复预置规则",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            Store.Data.rules = DefaultRules.Build();
            Store.Save();
            LoadRules();
        }

        // ==================== 账户 ====================

        private void LoadAccounts()
        {
            gridAccounts.ItemsSource = Store.Data.accounts.Where(a => !a.archived)
                .Select(a => new AccountRow { A = a }).ToList();
            txtAssets.Text = "净资产 " + Util.Yuan(Store.NetAssets());
        }

        private Account CurrentAccount()
        {
            if (gridAccounts.CurrentItem is AccountRow r) return r.A;
            if (gridAccounts.SelectedItems.Count > 0) return ((AccountRow)gridAccounts.SelectedItems[0]).A;
            return null;
        }

        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            var w = new AccountWindow(null) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) { LoadAccounts(); Store.RaiseChanged(); }
        }

        private void Account_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => AccountEdit_Click(sender, e);

        private void AccountEdit_Click(object sender, RoutedEventArgs e)
        {
            var a = CurrentAccount();
            if (a == null) return;
            var w = new AccountWindow(a) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) { LoadAccounts(); Store.RaiseChanged(); }
        }

        private void AccountDelete_Click(object sender, RoutedEventArgs e)
        {
            var a = CurrentAccount();
            if (a == null) return;
            int used = Store.Data.transactions.Count(t => t.account == a.name || t.toAccount == a.name);
            if (used > 0)
            {
                MessageBox.Show($"「{a.name}」上面还有 {used} 笔记录，不能直接删。\n\n" +
                                "你可以先把那些记录改到别的账户，或者把它的「计入净资产」关掉。", "删不了");
                return;
            }
            if (MessageBox.Show($"删掉账户「{a.name}」？", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Store.Data.accounts.Remove(a);
            Store.Save();
            LoadAccounts();
            Store.RaiseChanged();
        }

        // ==================== 关于 ====================

        private void LoadAbout()
        {
            txtVersion.Text = "版本 1.0.0";
            txtRuntime.Text = $".NET {Environment.Version}　·　{Environment.OSVersion.VersionString}";
            txtDataPath.Text = Store.FilePath;
        }

        private void OpenDataDir_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.Dir) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("打不开文件夹：" + ex.Message, "提示"); }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(Store.FilePath); MessageBox.Show("路径已复制。", "好了"); }
            catch { }
        }
    }
}