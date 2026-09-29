using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MoneyTracker
{
    /// <summary>
    /// 备份与回档：程序自己管的快照，随时能退回某一个时间点。
    /// 设置页这么用：if (BackupWindow.ShowDialog(this)) Store.RaiseChanged();
    /// 或者更省事：if (BackupWindow.ShowDialog(this)) { /* 刷新界面 */ }
    /// </summary>
    public partial class BackupWindow : Window
    {
        /// <summary>这次对话里到底有没有真的回档过（调用方据此决定要不要刷新界面）</summary>
        public bool Restored { get; private set; }

        public BackupWindow()
        {
            InitializeComponent();
            txtDir.Text = "存放位置：" + Store.BackupDir;
            txtKeep.Text = "5";
            Load();
        }

        /// <summary>
        /// 便捷入口：打开对话框，返回是否发生过回档。
        /// 回档会整库替换，调用方拿到 true 就应该刷新自己的界面。
        /// </summary>
        public static bool ShowDialog(Window owner)
        {
            var w = new BackupWindow();
            // Owner 必须是已经显示出来的窗口，否则 WPF 会直接抛错
            if (owner != null && owner.IsLoaded) w.Owner = owner;
            ((Window)w).ShowDialog();       // 显式当普通窗口调，免得和这个静态方法绕在一起
            return w.Restored;
        }

        // ==================== 列表 ====================

        /// <summary>重新读一遍快照目录，列表、计数、当前状态一起更新</summary>
        private void Load()
        {
            List<Store.SnapshotInfo> list;
            try { list = Store.ListSnapshots(); }
            catch { list = new List<Store.SnapshotInfo>(); }

            lst.ItemsSource = list;
            txtEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtCount.Text = list.Count == 0 ? "还没有快照" : $"快照（共 {list.Count} 个）";

            long size = 0;
            try { if (System.IO.File.Exists(Store.FilePath)) size = new System.IO.FileInfo(Store.FilePath).Length; } catch { }
            long bytes = 0;
            foreach (var s in list) bytes += s.Size;
            txtNow.Text = $"现在：{Store.Data.transactions.Count} 笔记录 · 数据库 {Store.SizeText(size)} · 快照一共占 {Store.SizeText(bytes)}";

            btnPrune.IsEnabled = list.Count > 0;
        }

        private void Tip(string text)
        {
            txtTip.Text = text ?? "";
            txtTip.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static Store.SnapshotInfo RowOf(object sender)
            => (sender as FrameworkElement)?.DataContext as Store.SnapshotInfo;

        // ==================== 立即备份 ====================

        private void Snap_Click(object sender, RoutedEventArgs e)
        {
            var name = Store.Snapshot("手动备份");
            if (name == null)
            {
                Tip(null);
                MessageBox.Show("备份没成功：" + (Store.LastError ?? "原因不明"), "备份",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var info = Store.ListSnapshots().FirstOrDefault(x => x.FileName == name);
            Load();
            Tip(info != null
                ? $"备份好了：{name}（{info.Count} 笔记录，{info.SizeText}）"
                : "备份好了：" + name);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Store.BackupDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打不开文件夹：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ==================== 回档 ====================

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var s = RowOf(sender);
            if (s == null) return;
            if (!s.Readable)
            {
                MessageBox.Show("这个快照文件读不出来，可能已经坏了，换一个时间点试试。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int now = Store.Data.transactions.Count;
            var msg = $"要把数据退回「{s.TimeText}  {s.ReasonText}」这一份吗？\n\n" +
                      $"这一份里有 {s.Count} 笔记录，现在有 {now} 笔。\n" +
                      "回档前会自动把当前状态再存一份（说明写着「回档前自动备份」），退错了还能撤回来。";
            if (MessageBox.Show(msg, "确认回档", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (!Store.RestoreSnapshot(s.FileName))
            {
                Load();
                Tip(null);
                MessageBox.Show("回档没成功：" + (Store.LastError ?? "原因不明"), "回档",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Restored = true;
            Load();
            Tip($"已经回到「{s.TimeText}」的样子。");
            MessageBox.Show($"回档完成，现在是 {Store.Data.transactions.Count} 笔记录。\n\n" +
                            "如果发现退错了，就回档到刚才那份「回档前自动备份」。",
                "回档完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==================== 删除 / 清理 ====================

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var s = RowOf(sender);
            if (s == null) return;
            if (MessageBox.Show($"要删掉「{s.TimeText}  {s.ReasonText}」这一份快照吗？\n\n删了就少一个能回档的时间点了。",
                    "删除快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (Store.DeleteSnapshot(s.FileName))
            {
                Load();
                Tip("删掉了：" + s.FileName);
            }
            else
            {
                Load();
                MessageBox.Show("删除失败：" + (Store.LastError ?? "原因不明"), "删除",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Prune_Click(object sender, RoutedEventArgs e)
        {
            int keep = 5;
            int.TryParse((txtKeep.Text ?? "").Trim(), out keep);
            keep = Math.Max(1, Math.Min(200, keep));
            txtKeep.Text = keep.ToString();

            var all = Store.ListSnapshots();
            if (all.Count <= keep)
            {
                Load();
                Tip($"现在一共 {all.Count} 个快照，不用清理。");
                return;
            }

            if (MessageBox.Show($"只留最近 {keep} 个，剩下的 {all.Count - keep} 个会删掉，要继续吗？",
                    "清理旧快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            int n = Store.PruneSnapshots(keep);
            var err = Store.LastError;          // 后面的刷新会把 LastError 清掉，先拿在手里
            int left = Store.ListSnapshots().Count;
            Load();
            Tip($"清理了 {n} 个旧快照，现在还剩 {left} 个。");
            if (!string.IsNullOrEmpty(err))
                MessageBox.Show(err, "清理", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}