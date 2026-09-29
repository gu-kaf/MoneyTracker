using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MoneyTracker
{
    public partial class BudgetWindow : Window
    {
        private readonly Budget _b;
        private readonly bool _isNew;

        public BudgetWindow(Budget src)
        {
            InitializeComponent();
            _isNew = src == null;
            _b = src ?? new Budget { period = "monthly", scope = "total", amount = 0, alertThreshold = 0.8 };

            txtHeader.Text = _isNew ? "新增一项预算" : "修改预算";
            Title = txtHeader.Text;

            cmbScope.Items.Add("全部支出（总预算）");
            foreach (var c in Store.Data.transactions.Select(t => t.category)
                         .Where(c => !string.IsNullOrEmpty(c) && c != "转账").Distinct().OrderBy(c => c))
                cmbScope.Items.Add(c);
            foreach (var c in Categories.ExpenseNames)
                if (!cmbScope.Items.Contains(c)) cmbScope.Items.Add(c);

            cmbScope.SelectedItem = _b.scope == "total"
                ? "全部支出（总预算）"
                : (cmbScope.Items.Contains(_b.scope) ? _b.scope : "全部支出（总预算）");

            txtAmount.Text = _b.amount > 0 ? Util.Money(_b.amount) : "";
            cmbAlert.SelectedIndex = _b.alertThreshold switch
            {
                <= 0.5 => 0,
                0.7 => 1,
                0.8 => 2,
                0.9 => 3,
                _ => 2
            };
            if (_b.alertThreshold <= 0) cmbAlert.SelectedIndex = 4;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            long cents = Util.ToCents(txtAmount.Text);
            if (cents <= 0)
            {
                MessageBox.Show("金额要大于 0。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var scope = cmbScope.SelectedItem as string;
            if (string.IsNullOrEmpty(scope)) { MessageBox.Show("先选一个预算范围。", "提示"); return; }
            if (scope == "全部支出（总预算）") scope = "total";

            // 避免同一范围重复建
            var dup = Store.Data.budgets.FirstOrDefault(x => x != _b && x.period == "monthly" && x.scope == scope);
            if (dup != null)
            {
                if (MessageBox.Show($"「{scope}」已经有一条预算了，要改成这个金额吗？", "已有预算",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                dup.amount = cents;
                dup.alertThreshold = AlertValue();
                Store.Save();
                DialogResult = true;
                return;
            }

            _b.scope = scope;
            _b.amount = cents;
            _b.period = "monthly";
            _b.alertThreshold = AlertValue();

            if (_isNew) Store.Data.budgets.Add(_b);
            Store.Save();
            DialogResult = true;
        }

        private double AlertValue() => cmbAlert.SelectedIndex switch
        {
            0 => 0.5,
            1 => 0.7,
            2 => 0.8,
            3 => 0.9,
            _ => 0
        };

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}