using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoneyTracker
{
    public class ThemeDef
    {
        public string Id, Name, Desc;
        public bool Dark;
        public string Bg, Surface, Surface2, Border, Text, Sub, Accent, AccentHover, OnAccent, Expense, Income, Hover, Selected;
    }

    /// <summary>
    /// 主题系统：内置多套配色，运行时即时切换，不改文件不重启。
    /// 所有颜色都以 DynamicResource 注入到 Application.Resources，
    /// 界面上写 {DynamicResource BgBrush} 就能跟着变。
    /// </summary>
    public static class ThemeManager
    {
        public static readonly List<ThemeDef> All = new List<ThemeDef>
        {
            new ThemeDef { Id = "light", Name = "浅色", Desc = "干净的默认配色，白天用最舒服", Dark = false,
                Bg = "#F4F5F7", Surface = "#FFFFFF", Surface2 = "#EFF1F4", Border = "#E2E5EA",
                Text = "#1E232E", Sub = "#78808E", Accent = "#2F6FED", AccentHover = "#2359C9", OnAccent = "#FFFFFF",
                Expense = "#E5484D", Income = "#26A269", Hover = "#EDF0F4", Selected = "#E4EDFD" },

            new ThemeDef { Id = "dark", Name = "深色", Desc = "夜间记账不刺眼", Dark = true,
                Bg = "#15171C", Surface = "#1D2027", Surface2 = "#252932", Border = "#333844",
                Text = "#E8EAED", Sub = "#98A0AC", Accent = "#5B8DEF", AccentHover = "#7BA5F5", OnAccent = "#0E1116",
                Expense = "#F2686C", Income = "#4CC38A", Hover = "#2A2F39", Selected = "#26344C" },

            new ThemeDef { Id = "eye", Name = "护眼绿", Desc = "低饱和绿底，长时间看不累", Dark = false,
                Bg = "#EFF3EB", Surface = "#FBFDF8", Surface2 = "#E6EDE0", Border = "#D5E0CB",
                Text = "#242E1C", Sub = "#6A785F", Accent = "#478A3A", AccentHover = "#3A7130", OnAccent = "#FFFFFF",
                Expense = "#C9503F", Income = "#3D8B4F", Hover = "#E2EADA", Selected = "#DCEBD3" },

            new ThemeDef { Id = "midnight", Name = "午夜蓝", Desc = "深蓝夜色，偏冷静", Dark = true,
                Bg = "#0E1622", Surface = "#16202F", Surface2 = "#1D293C", Border = "#293B54",
                Text = "#E3EBF6", Sub = "#8CA0BA", Accent = "#4C9AFF", AccentHover = "#6FB0FF", OnAccent = "#08111C",
                Expense = "#FF7A85", Income = "#46C99A", Hover = "#1F2C40", Selected = "#1E3A5F" },

            new ThemeDef { Id = "warm", Name = "暖阳", Desc = "米黄暖调，像纸一样", Dark = false,
                Bg = "#FAF5ED", Surface = "#FFFDF9", Surface2 = "#F4ECDF", Border = "#E7DBC6",
                Text = "#3A2E20", Sub = "#897960", Accent = "#D6811F", AccentHover = "#B66C12", OnAccent = "#FFFFFF",
                Expense = "#C24A3A", Income = "#3F8A55", Hover = "#F2E9DA", Selected = "#F7E6C8" },

            new ThemeDef { Id = "sakura", Name = "樱花粉", Desc = "粉嫩一点，也可以很认真", Dark = false,
                Bg = "#FDF4F6", Surface = "#FFFBFD", Surface2 = "#F9E9EE", Border = "#F0D5DE",
                Text = "#39262E", Sub = "#92707E", Accent = "#E0568F", AccentHover = "#C74379", OnAccent = "#FFFFFF",
                Expense = "#D94F5C", Income = "#4E9E6E", Hover = "#F8E7EC", Selected = "#FADCE7" },

            new ThemeDef { Id = "contrast", Name = "高对比", Desc = "黑底亮黄，视力不好也能看清", Dark = true,
                Bg = "#000000", Surface = "#0B0B0B", Surface2 = "#161616", Border = "#5A5A5A",
                Text = "#FFFFFF", Sub = "#C8C8C8", Accent = "#FFD400", AccentHover = "#FFE04D", OnAccent = "#000000",
                Expense = "#FF6B6B", Income = "#5BE38A", Hover = "#1F1F1F", Selected = "#3A3300" }
        };

        public static ThemeDef Current { get; private set; } = All[0];

        /// <summary>页面背景的不透明度。没铺背景图时是实心的，铺了之后调低让图透出来。</summary>
        public static int BgSoftAlpha { get; private set; } = 255;

        /// <summary>改页面背景的透明度，顺便把资源刷新掉</summary>
        public static void SetBgSoftAlpha(int alpha)
        {
            BgSoftAlpha = Math.Max(0, Math.Min(255, alpha));
            var res = Application.Current?.Resources;
            if (res == null) return;
            Set(res, "BgSoftBrush", WithAlpha(Current.Bg, BgSoftAlpha));
        }

        /// <summary>给一个颜色套上 0~255 的透明度</summary>
        public static string WithAlpha(string hex, int alpha)
        {
            var c = Parse(hex);
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        /// <summary>可选的强调色（用户想换主色时用这些，也可以直接填 hex）</summary>
        public static readonly (string name, string hex)[] AccentPresets =
        {
            ("默认", ""),
            ("天蓝", "#2F6FED"),
            ("青绿", "#0FA47F"),
            ("紫罗兰", "#7C5CE0"),
            ("橘橙", "#E0731F"),
            ("玫红", "#E0568F"),
            ("正红", "#D93A3A"),
            ("石墨", "#5B6472")
        };

        /// <summary>
        /// 可选字体。原来写死微软雅黑 UI，笔画方硬，看久了累，
        /// 所以默认换成细雅黑（Light 字重的雅黑），并把字体做成可切换。
        /// 每一项都带一串后备字体，某个字重没装也不会掉成宋体。
        /// </summary>
        public static readonly (string id, string name, string family, string desc)[] Fonts =
        {
            ("soft", "细雅黑", "Microsoft YaHei UI Light, Microsoft YaHei UI, Segoe UI, sans-serif",
                "笔画细一些，长时间看着不累（默认）"),
            ("round", "圆润", "YouYuan, 幼圆, Microsoft YaHei UI, sans-serif",
                "圆头圆脑，最软最亲切的一种"),
            ("xihei", "细黑", "STXihei, 华文细黑, Microsoft YaHei UI Light, sans-serif",
                "又细又匀，像铅字印出来的，很安静"),
            ("standard", "标准雅黑", "Microsoft YaHei UI, Segoe UI, sans-serif",
                "Windows 最常见的观感，字迹最清楚"),
            ("deng", "等线", "DengXian Light, DengXian, Microsoft YaHei UI, sans-serif",
                "更方正，字面干净，适合喜欢简洁的"),
            ("kai", "楷体", "KaiTi, STKaiti, Microsoft YaHei UI, sans-serif",
                "像手写，偏文艺，看数字会不太整齐")
        };

        public static (string id, string name, string family, string desc) FindFont(string id)
        {
            foreach (var f in Fonts) if (f.id == id) return f;
            return Fonts[0];
        }

        /// <summary>
        /// 从用户自己挑的字体文件（.ttf / .otf）里读出一个可用的字体族。
        /// 读不出来就返回 null，调用方会退回内置字体，不会白屏。
        /// </summary>
        public static FontFamily LoadFontFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                var uri = new Uri(System.IO.Path.GetFullPath(path));
                // 这里必须写全名：ThemeManager 自己有个 Fonts 列表，会把 WPF 的 Fonts 类遮住
                var fams = System.Windows.Media.Fonts.GetFontFamilies(uri);
                foreach (var f in fams)
                    if (f != null) return f;
            }
            catch { }
            return null;
        }

        /// <summary>A 到 Z 排一遍，给界面下拉用</summary>
        public static bool FontFileExists(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path); }
            catch { return false; }
        }

        /// <summary>
        /// 换字体。字体族本身靠 UiFont 这个动态资源下发，
        /// 字号则拆成几个档位变量，乘以缩放比例。
        /// 选了「自定义」就用用户装的那个字体文件。
        /// </summary>
        public static void ApplyFont(string fontId, int scalePercent)
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            FontFamily fam = null;
            if (fontId == "custom")
                fam = LoadFontFile(Store.Data.settings.fontFile);

            if (fam == null)
            {
                var f = FindFont(fontId);
                fam = new FontFamily(f.family);
            }
            res["UiFont"] = fam;

            double k = Math.Max(80, Math.Min(150, scalePercent <= 0 ? 100 : scalePercent)) / 100.0;
            // 正文比原来大一档、标题台阶拉开一点：字小了本身就显硬
            res["FontH1"] = Math.Round(21 * k, 1);
            res["FontH2"] = Math.Round(15.5 * k, 1);
            res["FontBody"] = Math.Round(13.5 * k, 1);
            res["FontSmall"] = Math.Round(12.5 * k, 1);
            res["FontTiny"] = Math.Round(11.5 * k, 1);
        }

        /// <summary>
        /// 给窗口铺一张背景图：图先缩小再模糊，避免大图卡顿。
        /// 窗口里需要有名为 BgImage / BgVeil / BgBlur 的元素（MainWindow 里有）。
        /// 传空路径就是把背景图撤掉。
        /// </summary>
        public static void ApplyBackground(Window win, string path, int blur, int dim, string fit)
        {
            if (win == null) return;

            var img = win.FindName("BgImage") as System.Windows.Controls.Image;
            var veil = win.FindName("BgVeil") as FrameworkElement;
            var blurEffect = win.FindName("BgBlur") as System.Windows.Media.Effects.BlurEffect;
            if (img == null) return;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                img.Source = null;
                img.Visibility = Visibility.Collapsed;
                if (veil != null) veil.Visibility = Visibility.Collapsed;
                SetBgSoftAlpha(255);
                return;
            }

            try
            {
                // 模糊很吃性能，所以解码时就缩到屏幕大小以内，再交给 GPU 去糊
                int maxSide = 1920;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = maxSide;
                bmp.EndInit();
                bmp.Freeze();

                img.Source = bmp;
                img.Visibility = Visibility.Visible;
                img.Stretch = FitToStretch(fit);

                if (blurEffect != null)
                {
                    blurEffect.Radius = Math.Max(0, Math.Min(60, blur));
                    blurEffect.KernelType = blur >= 3
                        ? System.Windows.Media.Effects.KernelType.Gaussian
                        : System.Windows.Media.Effects.KernelType.Box;
                }

                if (veil != null)
                {
                    // 压暗不靠这层不透明的纱，而是让页面背景自己变透明，
                    // 这样侧边栏还是实心的，只有内容区透出图片，层次更干净。
                    veil.Visibility = Visibility.Collapsed;
                    veil.Opacity = 0;
                }

                int d = Math.Max(0, Math.Min(95, dim));
                // 页面背景越不透明，底下的图越看不见。所以「淡出程度」越大 = 页面越实 = 图越淡。
                // dim 0 时留 75 的底（图很突出但字还压得住），dim 95 时基本盖实。
                SetBgSoftAlpha(255 - (int)((95 - d) * 1.9));
            }
            catch
            {
                // 图坏了、格式不支持，就当没有背景图，别让程序崩
                img.Source = null;
                img.Visibility = Visibility.Collapsed;
                if (veil != null) veil.Visibility = Visibility.Collapsed;
                SetBgSoftAlpha(255);
            }
        }

        private static Stretch FitToStretch(string fit)
        {
            switch ((fit ?? "").Trim().ToLowerInvariant())
            {
                case "uniform": return Stretch.Uniform;
                case "uniformtofill": return Stretch.UniformToFill;
                case "none": return Stretch.None;
                default: return Stretch.UniformToFill;
            }
        }

        public static ThemeDef Find(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

        public static void Apply(string themeId, string accentOverride = null)
        {
            var t = Find(themeId);
            Current = t;

            var res = Application.Current?.Resources;
            if (res == null) return;

            string accent = t.Accent;
            string accentHover = t.AccentHover;
            string onAccent = t.OnAccent;
            if (!string.IsNullOrWhiteSpace(accentOverride))
            {
                accent = accentOverride.Trim();
                accentHover = Shade(accent, t.Dark ? 0.18 : -0.14);
                onAccent = Luminance(accent) > 0.6 ? "#101418" : "#FFFFFF";
            }

            Set(res, "BgBrush", t.Bg);
            // 页面背景单独一份：带透明度，铺了背景图之后图片能透过来
            Set(res, "BgSoftBrush", WithAlpha(t.Bg, BgSoftAlpha));
            Set(res, "SurfaceBrush", t.Surface);
            Set(res, "Surface2Brush", t.Surface2);
            Set(res, "BorderBrush2", t.Border);
            Set(res, "TextBrush", t.Text);
            Set(res, "SubBrush", t.Sub);
            Set(res, "AccentBrush", accent);
            Set(res, "AccentHoverBrush", accentHover);
            Set(res, "OnAccentBrush", onAccent);
            Set(res, "ExpenseBrush", t.Expense);
            Set(res, "IncomeBrush", t.Income);
            Set(res, "HoverBrush", t.Hover);
            Set(res, "SelectedBrush", t.Selected);

            // 让系统原生窗口（标题栏）也跟着主题走
            Set(res, "WindowBgColor", t.Bg);

            ApplyTitleBar(t);
        }

        private static void Set(ResourceDictionary res, string key, string hex)
        {
            var c = Parse(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
            res[key + "Color"] = c;
        }

        public static Color Parse(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch { return Colors.Gray; }
        }

        /// <summary>把颜色整体调亮/调暗，用来算 hover 色</summary>
        public static string Shade(string hex, double amount)
        {
            var c = Parse(hex);
            byte Ch(byte v) => (byte)Math.Max(0, Math.Min(255, v + (amount > 0 ? (255 - v) * amount : v * amount)));
            return $"#{Ch(c.R):X2}{Ch(c.G):X2}{Ch(c.B):X2}";
        }

        public static double Luminance(string hex)
        {
            var c = Parse(hex);
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        private static void ApplyTitleBar(ThemeDef t)
        {
            try
            {
                if (Application.Current?.MainWindow == null) return;
                var helper = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow);
                if (helper.Handle == IntPtr.Zero) return;
                int dark = t.Dark ? 1 : 0;
                DwmSetWindowAttribute(helper.Handle, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
                var color = Parse(t.Bg);
                int argb = (color.B << 16) | (color.G << 8) | color.R;
                DwmSetWindowAttribute(helper.Handle, 35, ref argb, sizeof(int)); // DWMWA_CAPTION_COLOR
                int textArgb = 0; // 让系统自动选标题文字色
                DwmSetWindowAttribute(helper.Handle, 36, ref textArgb, sizeof(int));
            }
            catch { /* 老系统上不支持就跳过，不影响功能 */ }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}