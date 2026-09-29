using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MoneyTracker
{
    public partial class RuleWindow : Window
    {
        private readonly Rule _r;
        private readonly bool _isNew;

        public RuleWindow(Rule src)
        {
            InitializeComponent();
            _isNew = src == null;
            _r = src ?? new Rule { priority = 60, enabled = true, matchField = "both" };

            txtHeader.Text = _isNew ? "新增一条自动记账规则" : "修改规则";
            Title = txtHeader.Text;

            txtKeyword.Text = _r.keyword;
            cmbField.SelectedIndex = _r.matchField switch { "merchant" => 1, "note" => 2, _ => 0 };
            cmbType.SelectedIndex = _r.setType switch { "expense" => 1, "income" => 2, _ => 0 };
            txtPriority.Text = _r.priority.ToString();
            chkEnabled.IsChecked = _r.enabled;

            foreach (var c in Categories.ExpenseNames) cmbCat.Items.Add(c);
            foreach (var c in Categories.IncomeNames) if (!cmbCat.Items.Contains(c)) cmbCat.Items.Add(c);
            foreach (var c in Store.Data.transactions.Select(t => t.category).Distinct())
                if (!string.IsNullOrEmpty(c) && c != "转账" && !cmbCat.Items.Contains(c)) cmbCat.Items.Add(c);

            cmbCat.SelectedItem = cmbCat.Items.Contains(_r.setCategory) ? _r.setCategory
                                 : (cmbCat.Items.Count > 0 ? cmbCat.Items[0] : null);
            BuildSubs();
        }

        private void Cat_Changed(object sender, SelectionChangedEventArgs e) => BuildSubs();

        private void BuildSubs()
        {
            cmbSub.Items.Clear();
            var cat = cmbCat.SelectedItem as string;
            if (!string.IsNullOrEmpty(cat))
            {
                bool income = Categories.IncomeNames.Contains(cat);
                foreach (var s in Categories.SubsOf(cat, income ? "income" : "expense")) cmbSub.Items.Add(s);
            }
            if (cmbSub.Items.Contains(_r.setSubcategory)) cmbSub.SelectedItem = _r.setSubcategory;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var kw = (txtKeyword.Text ?? "").Trim();
            if (kw.Length == 0)
            {
                MessageBox.Show("关键词不能是空的。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int pri = 60;
            int.TryParse(txtPriority.Text, out pri);
            pri = Math.Max(0, Math.Min(100, pri));

            _r.keyword = kw;
            _r.matchField = cmbField.SelectedIndex switch { 1 => "merchant", 2 => "note", _ => "both" };
            _r.setType = cmbType.SelectedIndex switch { 1 => "expense", 2 => "income", _ => "" };
            _r.setCategory = cmbCat.SelectedItem as string ?? "";
            _r.setSubcategory = cmbSub.SelectedItem as string ?? "";
            _r.priority = pri;
            _r.enabled = chkEnabled.IsChecked == true;

            if (_isNew) Store.Data.rules.Add(_r);
            Store.Save();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}