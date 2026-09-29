using System.Windows;

namespace MoneyTracker
{
    /// <summary>给侧边导航按钮挂一个小图标字符用的附加属性</summary>
    public static class Nav
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Nav),
                new PropertyMetadata(""));

        public static void SetIcon(DependencyObject o, string v) => o.SetValue(IconProperty, v);
        public static string GetIcon(DependencyObject o) => (string)o.GetValue(IconProperty);
    }
}