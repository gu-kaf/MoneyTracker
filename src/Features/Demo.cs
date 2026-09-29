using System;
using System.Collections.Generic;

namespace MoneyTracker
{
    /// <summary>示例数据：让报表、预算、分析一眼就能看到效果。设置页里有按钮可以随时清掉。</summary>
    public static class Demo
    {
        public static int Fill(int months = 4)
        {
            var rnd = new Random(20260929);
            var merchantsFood = new[] { "美团外卖", "饿了么", "楼下快餐", "沙县小吃", "星巴克", "瑞幸咖啡", "肯德基" };
            var merchantsShop = new[] { "淘宝", "京东", "拼多多", "永辉超市", "盒马鲜生", "名创优品" };
            var merchantsTraffic = new[] { "滴滴出行", "地铁", "公交", "高德打车", "加油站" };
            var merchantsFun = new[] { "万达影城", "Steam", "网易云音乐", "健身房", "书店" };

            var today = DateTime.Today;
            int n = 0;

            for (int m = months - 1; m >= 0; m--)
            {
                var month = today.AddMonths(-m);
                int days = DateTime.DaysInMonth(month.Year, month.Month);
                int maxDay = m == 0 ? today.Day : days;

                void Add(string date, string type, long cents, string cat, string sub, string acc, string mer, string note)
                {
                    var t = new Transaction
                    {
                        type = type,
                        amount = cents,
                        date = date,
                        time = $"{rnd.Next(7, 22):00}:{rnd.Next(0, 59):00}",
                        category = cat,
                        subcategory = sub,
                        account = acc,
                        merchant = mer,
                        note = note,
                        source = "manual",
                        // 示例数据也记在当前账本上，不然切了账本以后界面上什么都看不到
                        ledgerId = Store.CurrentLedgerId
                    };
                    t.hash = Util.HashOf(t);
                    if (Store.IsDuplicate(t)) return;
                    Store.Data.transactions.Add(t);
                    n++;
                }

                string D(int day) => new DateTime(month.Year, month.Month, Math.Min(day, maxDay)).ToString("yyyy-MM-dd");

                // 工资
                if (maxDay >= 10) Add(D(10), "income", 1200000 + rnd.Next(-50000, 80000), "工资", "月薪", "招商银行", "某某公司", "月薪");
                // 房租
                Add(D(1), "expense", 320000, "居住", "房租房贷", "招商银行", "房东", "房租");
                // 水电燃气
                if (maxDay >= 8) Add(D(8), "expense", 8000 + rnd.Next(4000), "居住", "水电燃气", "支付宝", "国家电网", "电费");

                // 餐饮：每月 18~26 笔
                int foodCount = rnd.Next(18, 27);
                for (int i = 0; i < foodCount; i++)
                {
                    var mer = merchantsFood[rnd.Next(merchantsFood.Length)];
                    long cents = mer.Contains("星巴克") || mer.Contains("瑞幸") ? rnd.Next(1500, 4000) : rnd.Next(900, 6000);
                    Add(D(rnd.Next(1, maxDay + 1)), "expense", cents, "餐饮",
                        mer.Contains("咖啡") ? "零食饮料" : "外卖", rnd.Next(2) == 0 ? "微信" : "支付宝", mer, "");
                }

                // 交通
                for (int i = 0; i < rnd.Next(6, 12); i++)
                {
                    var mer = merchantsTraffic[rnd.Next(merchantsTraffic.Length)];
                    Add(D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(300, 6000), "交通", "打车", "支付宝", mer, "");
                }

                // 购物
                for (int i = 0; i < rnd.Next(3, 8); i++)
                {
                    var mer = merchantsShop[rnd.Next(merchantsShop.Length)];
                    Add(D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(2000, 30000), "购物", "日用品", "信用卡", mer, "");
                }

                // 娱乐 / 订阅
                for (int i = 0; i < rnd.Next(2, 5); i++)
                {
                    var mer = merchantsFun[rnd.Next(merchantsFun.Length)];
                    Add(D(rnd.Next(1, maxDay + 1)), "expense", rnd.Next(1500, 15000), "娱乐", "电影演出", "微信", mer, "");
                }
                if (maxDay >= 15) Add(D(15), "expense", 2500, "通讯", "会员订阅", "微信", "爱奇艺", "包月会员");
                if (maxDay >= 20) Add(D(20), "expense", 2500, "通讯", "会员订阅", "微信", "网易云音乐", "包月会员");

                // 偶尔的收入
                if (rnd.Next(2) == 0) Add(D(rnd.Next(1, maxDay + 1)), "income", rnd.Next(20000, 90000), "兼职", "外快", "支付宝", "接单", "外快");
                if (rnd.Next(3) == 0) Add(D(rnd.Next(1, maxDay + 1)), "income", rnd.Next(5000, 30000), "退款", "退货", "支付宝", "淘宝退款", "退货");
            }

            // 预算
            if (Store.Data.budgets.Count == 0)
            {
                Store.Data.budgets.Add(new Budget { period = "monthly", scope = "total", amount = 600000, alertThreshold = 0.8 });
                Store.Data.budgets.Add(new Budget { period = "monthly", scope = "餐饮", amount = 180000, alertThreshold = 0.85 });
                Store.Data.budgets.Add(new Budget { period = "monthly", scope = "交通", amount = 60000, alertThreshold = 0.85 });
                Store.Data.budgets.Add(new Budget { period = "monthly", scope = "购物", amount = 150000, alertThreshold = 0.9 });
                Store.Data.budgets.Add(new Budget { period = "monthly", scope = "娱乐", amount = 80000, alertThreshold = 0.9 });
            }

            Store.Save();
            Store.RaiseChanged();
            return n;
        }

        /// <summary>清空所有交易（保留账户、规则、设置）</summary>
        public static void ClearTransactions()
        {
            Store.Data.transactions.Clear();
            Store.Data.budgets.Clear();
            Store.Save();
            Store.RaiseChanged();
        }
    }
}