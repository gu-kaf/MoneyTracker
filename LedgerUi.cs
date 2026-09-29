using System;
using System.Windows.Media;

namespace MoneyTracker
{
    /// <summary>
    /// 账本在界面上的小工具：标识色 → 画笔、名字首字。
    /// 放在这儿是因为侧边栏切换器、账本管理窗口、报表对比卡三处都要用同一种取色方式，
    /// 各写一份迟早会不一样。
    /// </summary>
    public static class LedgerUi
    {
        /// <summary>账本的标识色（用户设的 / 自动按顺序给的，统一走 Store 那一套）</summary>
        public static Color ColorOf(Ledger l) => ColorOfHex(Store.LedgerColorOf(l));

        /// <summary>"#2F6FED" → Color；认不出来给个中性灰，绝不抛异常</summary>
        public static Color ColorOfHex(string hex)
        {
            try
            {
                if (Store.IsHexColor(hex))
                {
                    var s = hex.Trim();
                    if (s.Length == 4)   // #abc → #aabbcc
                        s = "#" + s[1] + s[1] + s[2] + s[2] + s[3] + s[3];
                    var c = (Color)ColorConverter.ConvertFromString(s);
                    return c;
                }
            }
            catch { }
            return Color.FromRgb(0x8A, 0x8A, 0x8A);
        }

        /// <summary>账本的标识色画笔（冻结过，可以跨线程画）</summary>
        public static SolidColorBrush BrushOf(Ledger l) => BrushOfHex(Store.LedgerColorOf(l));

        public static SolidColorBrush BrushOfHex(string hex)
        {
            var b = new SolidColorBrush(ColorOfHex(hex));
            b.Freeze();
            return b;
        }

        /// <summary>账本名的首字，做成小色块里的字用</summary>
        public static string InitialOf(Ledger l)
        {
            var n = (l?.name ?? "").Trim();
            return n.Length == 0 ? "账" : n.Substring(0, 1);
        }

        /// <summary>账本的完整显示名：「我」「老婆（归档）」这种</summary>
        public static string DisplayName(Ledger l)
        {
            if (l == null) return "未命名账本";
            return l.archived ? l.name + "（已归档）" : l.name;
        }
    }
}