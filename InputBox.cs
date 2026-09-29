using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MoneyTracker
{
    /// <summary>
    /// 一个很小的「请输入」对话框。WPF 没有内置的，就自己拼一个。
    /// 故意用纯代码搭而不是 XAML：这点东西不值得再开一个文件，而且它要能在任何窗口里随时弹。
    /// </summary>
    public static class InputBox
    {
        /// <summary>返回用户输入的文字；点取消或直接关掉返回 null。</summary>
        public static string Show(Window owner, string title, string prompt, string initial)
        {
            var win = new Window
            {
                Title = title,
                Width = 400,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            if (owner != null) win.Owner = owner;

            // 主题资源是从 Application 上取的，窗口自己没显式设也能拿到
            if (Application.Current != null)
            {
                if (Application.Current.TryFindResource("BgBrush") is object bg) win.Background = (System.Windows.Media.Brush)bg;
                if (Application.Current.TryFindResource("UiFont") is object f) win.FontFamily = (System.Windows.Media.FontFamily)f;
            }

            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            if (Application.Current != null && Application.Current.TryFindResource("Muted") is Style ms) lbl.Style = ms;
            Grid.SetRow(lbl, 0);
            root.Children.Add(lbl);

            var box = new TextBox { Text = initial ?? "" };
            Grid.SetRow(box, 1);
            root.Children.Add(box);

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            string result = null;

            var cancel = new Button { Content = "取消", Padding = new Thickness(20, 8, 20, 8) };
            if (Application.Current != null && Application.Current.TryFindResource("Btn") is Style bs) cancel.Style = bs;
            cancel.Click += (s, e) => { win.DialogResult = false; };

            var ok = new Button
            {
                Content = "确定",
                Padding = new Thickness(24, 8, 24, 8),
                Margin = new Thickness(10, 0, 0, 0),
                IsDefault = true
            };
            if (Application.Current != null && Application.Current.TryFindResource("BtnPrimary") is Style ps) ok.Style = ps;
            ok.Click += (s, e) => { result = box.Text; win.DialogResult = true; };

            bar.Children.Add(cancel);
            bar.Children.Add(ok);
            Grid.SetRow(bar, 2);
            root.Children.Add(bar);

            win.Content = root;
            win.Loaded += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };
            // Esc 关掉，跟其他对话框一致
            win.KeyDown += (s, e) => { if (e.Key == Key.Escape) win.DialogResult = false; };

            return win.ShowDialog() == true ? result : null;
        }
    }
}