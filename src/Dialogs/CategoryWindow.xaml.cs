using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MoneyTracker
{
    /// <summary>
    /// 分类管理：一级分类和二级分类都能自己加、改名、删、挪位置。
    /// 编辑的是一份副本，点「保存」才写回去，所以中途反悔点「取消」不会动到任何东西。
    /// </summary>
    public partial class CategoryWindow : Window
    {
        /// <summary>列表里的一行：分类名 +「下面 5 项」这种小字</summary>
        public class CatRow
        {
            public CategoryGroup G { get; set; }
            public string Name => G.name;
            public string SubText => (G.subs == null || G.subs.Count == 0)
                ? "（没有细分）"
                : "（" + G.subs.Count + " 项细分）";
        }

        private List<CategoryGroup> _expense;          // 正在编辑的支出分类（副本）
        private List<CategoryGroup> _income;           // 正在编辑的收入分类（副本）
        private List<CategoryGroup> _origExpense;      // 打开时的原样，用来算「哪些改了名」
        private List<CategoryGroup> _origIncome;
        private bool _isIncome;

        public CategoryWindow()
        {
            InitializeComponent();

            _origExpense = Categories.CloneAll(Categories.ExpenseList);
            _origIncome = Categories.CloneAll(Categories.IncomeList);
            _expense = Categories.CloneAll(_origExpense);
            _income = Categories.CloneAll(_origIncome);

            // 直接在构造函数里铺内容，不放在 Loaded 里：
            // 离屏截图时窗口不会真的显示，Loaded 不触发，列表就会是空的。
            SwitchType(false);
        }

        /// <summary>离屏截图用：把窗口内容渲染成一张图，不显示窗口、不抢前台。</summary>
        public System.Windows.Media.Imaging.BitmapSource RenderToBitmap(int w, int h)
        {
            var root = Content as System.Windows.FrameworkElement;
            if (root == null) return null;

            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(root);
            rtb.Freeze();
            return rtb;
        }

        private List<CategoryGroup> Cur => _isIncome ? _income : _expense;

        private void SwitchExpense_Click(object sender, RoutedEventArgs e) => SwitchType(false);
        private void SwitchIncome_Click(object sender, RoutedEventArgs e) => SwitchType(true);

        private void SwitchType(bool income)
        {
            _isIncome = income;
            btnExpense.Style = (Style)FindResource(income ? "Chip" : "ChipOn");
            btnIncome.Style = (Style)FindResource(income ? "ChipOn" : "Chip");
            ReloadCats(0);
        }

        private void ReloadCats(int selectIndex)
        {
            lstCat.Items.Clear();
            foreach (var g in Cur) lstCat.Items.Add(new CatRow { G = g });
            if (lstCat.Items.Count > 0)
                lstCat.SelectedIndex = Math.Max(0, Math.Min(selectIndex, lstCat.Items.Count - 1));
            else
                ReloadSubs();
        }

        private CategoryGroup Selected =>
            (lstCat.SelectedItem as CatRow)?.G;

        private void Cat_Selected(object sender, SelectionChangedEventArgs e) => ReloadSubs();

        private void ReloadSubs()
        {
            lstSub.Items.Clear();
            var g = Selected;
            if (g == null)
            {
                txtSubTitle.Text = "二级分类";
                txtSubHint.Visibility = Visibility.Visible;
                return;
            }

            txtSubTitle.Text = "「" + g.name + "」下面的二级分类";
            foreach (var s in g.subs ?? new List<string>()) lstSub.Items.Add(s);
            txtSubHint.Visibility = (g.subs == null || g.subs.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==================== 一级分类 ====================

        private void AddCat_Click(object sender, RoutedEventArgs e)
        {
            string name = InputBox.Show(this, "新的一级分类", "分类名字，比如「生活费」：", "");
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            if (Cur.Any(x => x.name == name))
            {
                Warn("已经有一个叫「" + name + "」的分类了。");
                return;
            }

            Cur.Add(new CategoryGroup(name));
            ReloadCats(Cur.Count - 1);
        }

        private void RenameCat_Click(object sender, RoutedEventArgs e)
        {
            var g = Selected;
            if (g == null) return;

            string name = InputBox.Show(this, "给「" + g.name + "」改名", "新名字：", g.name);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0 || name == g.name) return;
            if (Cur.Any(x => x != g && x.name == name))
            {
                Warn("已经有一个叫「" + name + "」的分类了。");
                return;
            }

            g.name = name;
            int i = lstCat.SelectedIndex;
            ReloadCats(i);
        }

        private void DeleteCat_Click(object sender, RoutedEventArgs e)
        {
            var g = Selected;
            if (g == null) return;
            if (Cur.Count <= 1)
            {
                Warn("至少要留一个分类，不能全删光。");
                return;
            }

            // 有多少记录挂在这个分类上？删了它们得有地方去。
            int used = CountUsing(g.name, null);
            string msg = "确定删掉「" + g.name + "」吗？";
            if (used > 0)
                msg += "\n\n现在有 " + used + " 笔记录用的是这个分类。\n"
                     + "删掉之后，这些记录会被转到「" + FallbackName() + "」，记录本身不会丢。";
            if (MessageBox.Show(msg, "删除分类", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            Cur.Remove(g);
            ReloadCats(lstCat.SelectedIndex);
        }

        private void UpCat_Click(object sender, RoutedEventArgs e) => MoveCat(-1);
        private void DownCat_Click(object sender, RoutedEventArgs e) => MoveCat(1);

        private void MoveCat(int delta)
        {
            int i = lstCat.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= Cur.Count) return;
            var t = Cur[i]; Cur[i] = Cur[j]; Cur[j] = t;
            ReloadCats(j);
        }

        // ==================== 二级分类 ====================

        private void AddSub_Click(object sender, RoutedEventArgs e)
        {
            var g = Selected;
            if (g == null) return;

            string name = InputBox.Show(this, "给「" + g.name + "」加二级分类",
                "比如「外卖」「早餐」：", "");
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            g.subs ??= new List<string>();
            if (g.subs.Contains(name))
            {
                Warn("「" + g.name + "」下面已经有「" + name + "」了。");
                return;
            }

            g.subs.Add(name);
            RefreshSelectedRow();
            lstSub.SelectedIndex = g.subs.Count - 1;
        }

        private void RenameSub_Click(object sender, RoutedEventArgs e)
        {
            var g = Selected;
            if (g == null || lstSub.SelectedIndex < 0) return;

            int i = lstSub.SelectedIndex;
            string old = (string)lstSub.SelectedItem;
            string name = InputBox.Show(this, "给「" + old + "」改名", "新名字：", old);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0 || name == old) return;
            if (g.subs.Contains(name))
            {
                Warn("「" + g.name + "」下面已经有「" + name + "」了。");
                return;
            }

            g.subs[i] = name;
            RefreshSelectedRow();
            lstSub.SelectedIndex = i;
        }

        private void DeleteSub_Click(object sender, RoutedEventArgs e)
        {
            var g = Selected;
            if (g == null || lstSub.SelectedIndex < 0) return;

            string name = (string)lstSub.SelectedItem;
            int used = CountUsing(g.name, name);
            string msg = "确定删掉「" + g.name + " · " + name + "」吗？";
            if (used > 0)
                msg += "\n\n有 " + used + " 笔记录用的是它。删掉之后这些记录的二级分类会被清空"
                     + "（一级分类「" + g.name + "」保留），记录不会丢。";
            if (MessageBox.Show(msg, "删除二级分类", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            int i = lstSub.SelectedIndex;
            g.subs.RemoveAt(i);
            RefreshSelectedRow();
            if (lstSub.Items.Count > 0) lstSub.SelectedIndex = Math.Min(i, lstSub.Items.Count - 1);
        }

        private void UpSub_Click(object sender, RoutedEventArgs e) => MoveSub(-1);
        private void DownSub_Click(object sender, RoutedEventArgs e) => MoveSub(1);

        private void MoveSub(int delta)
        {
            var g = Selected;
            if (g == null) return;
            int i = lstSub.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= g.subs.Count) return;
            var t = g.subs[i]; g.subs[i] = g.subs[j]; g.subs[j] = t;
            RefreshSelectedRow();
            lstSub.SelectedIndex = j;
        }

        /// <summary>改完二级分类，左边那行的小字（「N 项细分」）要跟着更新</summary>
        private void RefreshSelectedRow()
        {
            int i = lstCat.SelectedIndex;
            var g = Selected;
            if (i < 0 || g == null) return;
            lstCat.Items.RemoveAt(i);
            lstCat.Items.Insert(i, new CatRow { G = g });
            lstCat.SelectedIndex = i;
        }

        // ==================== 统计与兜底 ====================

        /// <summary>数一下有多少笔记录挂在这个分类上。sub 传 null 表示整个一级分类。</summary>
        private int CountUsing(string cat, string sub)
        {
            string type = _isIncome ? "income" : "expense";
            return Store.Data.transactions.Count(t =>
                t.category == cat &&
                (sub == null || t.subcategory == sub) &&
                MatchesType(t, type));
        }

        private static bool MatchesType(Transaction t, string type)
            => type == "income" ? t.type == "income" : t.type != "income";

        /// <summary>删掉分类之后，那些记录挪到哪儿去</summary>
        private string FallbackName()
        {
            string other = _isIncome ? "其他收入" : "其他支出";
            if (Cur.Any(x => x.name == other)) return other;
            return Cur.Count > 0 ? Cur[0].name : other;
        }

        private void Warn(string msg)
            => MessageBox.Show(msg, "分类管理", MessageBoxButton.OK, MessageBoxImage.Information);

        // ==================== 保存 ====================

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                "把分类名单恢复成刚装好的样子？\n\n你自己加的分类会没掉，但已经记的账不会丢"
                + "（它们只是挂到默认分类上）。",
                "恢复出厂分类", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (_isIncome) { _income = Categories.CloneAll(Categories.DefaultIncome); _origIncome = Categories.CloneAll(Categories.DefaultIncome); }
            else { _expense = Categories.CloneAll(Categories.DefaultExpense); _origExpense = Categories.CloneAll(Categories.DefaultExpense); }
            ReloadCats(0);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // 校验：不能有空名字、不能重复
            foreach (var pair in new[] { (_expense, "支出"), (_income, "收入") })
            {
                var list = pair.Item1;
                if (list.Count == 0) { Warn(pair.Item2 + "分类不能一个都不留。"); return; }
                if (list.Any(x => string.IsNullOrWhiteSpace(x.name)))
                {
                    Warn(pair.Item2 + "分类里有名字是空的，请补上或者删掉它。");
                    return;
                }
                var dup = list.GroupBy(x => x.name).FirstOrDefault(g => g.Count() > 1);
                if (dup != null) { Warn(pair.Item2 + "分类里有重复的名字：「" + dup.Key + "」。"); return; }
            }

            // 记录改名，好把已经记过的账一起改过来
            var catRenames = new List<(string oldName, string newName, bool income)>();
            CollectRenames(_origExpense, _expense, false, catRenames);
            CollectRenames(_origIncome, _income, true, catRenames);

            var subRenames = new List<(string cat, string oldSub, string newSub, bool income)>();
            CollectSubRenames(_origExpense, _expense, false, subRenames);
            CollectSubRenames(_origIncome, _income, true, subRenames);

            int touched = 0;

            // 改名迁移：凡是记录里还用着旧名字的，都换成新名字
            foreach (var (oldName, newName, income) in catRenames)
            {
                string type = income ? "income" : "expense";
                foreach (var t in Store.Data.transactions)
                {
                    if (!MatchesType(t, type) || t.category != oldName) continue;
                    t.category = newName;
                    t.hash = Util.HashOf(t);
                    touched++;
                }
            }
            foreach (var (cat, oldSub, newSub, income) in subRenames)
            {
                string type = income ? "income" : "expense";
                foreach (var t in Store.Data.transactions)
                {
                    if (!MatchesType(t, type) || t.category != cat || t.subcategory != oldSub) continue;
                    t.subcategory = newSub;
                    t.hash = Util.HashOf(t);
                    touched++;
                }
            }

            // 删掉的分类，底下的记录要有地方去（否则明细里会挂着一个不存在的分类）
            touched += RehomeDeleted(_origExpense, _expense, false);
            touched += RehomeDeleted(_origIncome, _income, true);

            Store.Data.settings.expenseCategories = _expense;
            Store.Data.settings.incomeCategories = _income;
            Store.Save();
            Store.RaiseChanged();

            if (touched > 0)
                MessageBox.Show("分类已保存，顺手改好了 " + touched + " 处记录。", "分类管理",
                    MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
        }

        /// <summary>按内部 id 认出「这一项原来叫什么」，名字不一样就是改了名</summary>
        private static void CollectRenames(List<CategoryGroup> before, List<CategoryGroup> after,
            bool income, List<(string, string, bool)> into)
        {
            foreach (var now in after)
            {
                var was = before.FirstOrDefault(x => x.id == now.id);
                if (was != null && was.name != now.name)
                    into.Add((was.name, now.name, income));
            }
        }

        /// <summary>
        /// 二级分类没有独立 id，只能按「同一个一级分类下，旧名单里消失、新名单里新出现的」配对。
        /// 只在恰好一对一的时候才认，拿不准就不动，宁可不改也不能改错。
        /// </summary>
        private static void CollectSubRenames(List<CategoryGroup> before, List<CategoryGroup> after,
            bool income, List<(string, string, string, bool)> into)
        {
            foreach (var now in after)
            {
                var was = before.FirstOrDefault(x => x.id == now.id);
                if (was == null) continue;

                var oldSubs = was.subs ?? new List<string>();
                var newSubs = now.subs ?? new List<string>();
                string catName = now.name;   // 用改名后的名字，前面的迁移已经先改过记录的分类了

                var gone = oldSubs.Where(s => !newSubs.Contains(s)).ToList();
                var born = newSubs.Where(s => !oldSubs.Contains(s)).ToList();
                if (gone.Count == 1 && born.Count == 1)
                    into.Add((catName, gone[0], born[0], income));
            }
        }

        /// <summary>分类被删掉了，还把记录留在这个名字上的，挪到兜底分类</summary>
        private int RehomeDeleted(List<CategoryGroup> before, List<CategoryGroup> after, bool income)
        {
            string type = income ? "income" : "expense";
            var alive = after.Select(x => x.name).ToHashSet();
            int n = 0;
            foreach (var t in Store.Data.transactions)
            {
                if (!MatchesType(t, type)) continue;
                if (string.IsNullOrEmpty(t.category)) continue;
                if (alive.Contains(t.category)) continue;

                string fallback = income ? "其他收入" : "其他支出";
                if (!alive.Contains(fallback)) fallback = after.Count > 0 ? after[0].name : fallback;
                t.category = fallback;
                t.subcategory = "";
                t.hash = Util.HashOf(t);
                n++;
            }
            return n;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}