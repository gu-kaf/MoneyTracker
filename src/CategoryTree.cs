using System;
using System.Collections.Generic;
using System.Linq;

namespace MoneyTracker
{
    /// <summary>二级分类（子分类）的推荐表。
    /// 为什么单独放一个文件：Categories（Models.cs）是分类的"骨架"，定义一级分类有哪些；
    /// 这里只负责"给一级分类配几个顺手的二级选项"，让界面能直接点，不用手打。
    /// 二级分类永远可以留空——它只是提醒，不是约束。</summary>
    public static class CategoryTree
    {
        /// <summary>表示"没有二级分类"的占位词，界面上一律显示成"不限"</summary>
        public const string None = "";

        // 「其他」永远排在最后，用户想不起来归哪就点它
        private const string Other = "其他";

        // 一级分类 -> 推荐的二级分类。比 Models.cs 里骨架那份写得更细一点，
        // 因为界面上是多点一下就能选，多点选项反而更省事。
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // ---------- 支出 ----------
            { "餐饮", new[] { "早餐", "午餐", "晚餐", "外卖", "零食饮料", "咖啡奶茶", "聚餐", "买菜" } },
            { "购物", new[] { "日用品", "服饰", "数码", "家居", "美妆", "母婴", "图书文具", "网购" } },
            { "交通", new[] { "公交地铁", "打车", "加油", "停车", "火车飞机", "高速过路", "共享单车", "修车保养" } },
            { "通讯", new[] { "话费", "宽带", "会员订阅", "流量包", "快递" } },
            { "居住", new[] { "房租房贷", "水电燃气", "物业", "维修", "家具家电", "宽带电视" } },
            { "医疗", new[] { "门诊", "药品", "体检", "住院手术", "牙科", "保险" } },
            { "娱乐", new[] { "电影演出", "游戏", "旅行", "运动健身", "订阅会员", "KTV酒吧", "书籍影碟" } },
            { "教育", new[] { "书籍", "课程", "培训", "文具", "考试报名" } },
            { "宠物", new[] { "宠物食品", "宠物医疗", "宠物用品", "宠物美容", "寄养" } },
            { "人情", new[] { "红包", "礼物", "请客", "孝敬", "随礼" } },
            { "金融", new[] { "手续费", "利息", "税费", "罚款", "还款", "保险", "理财亏损" } },
            { "其他支出", new[] { "意外支出", "捐赠", "丢失" } },

            // ---------- 收入 ----------
            { "工资", new[] { "月薪", "奖金", "加班费", "补贴", "年终奖" } },
            { "兼职", new[] { "外快", "稿费", "接单", "直播打赏" } },
            { "投资", new[] { "利息", "分红", "理财收益", "基金股票" } },
            { "红包", new[] { "亲友", "平台红包", "活动奖励" } },
            { "报销", new[] { "差旅", "办公", "医疗" } },
            { "退款", new[] { "退货", "返现", "赔付" } },
            { "其他收入", new[] { "闲置转卖", "中奖", "礼金" } }
        };

        /// <summary>一级分类的推荐二级列表（含末尾的「其他」；没有配置就返回空，界面会转成自由输入）</summary>
        public static string[] Suggested(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return new string[0];
            return Table.TryGetValue(category.Trim(), out var subs) ? subs : new string[0];
        }

        /// <summary>用户能不能自己写二级分类——只有「其他支出/其他收入」这种兜底分类才不带推荐，
        /// 所以这两种以外的一级分类都允许自由输入。</summary>
        public static bool AllowCustom(string category) => !string.IsNullOrWhiteSpace(category);

        /// <summary>从已有数据里收集用户自己用过的二级分类，去重后按用得多的排前面。
        /// exclude 传当前选中的那个，免得自己的选项跟自己重复。</summary>
        public static List<string> Custom(string category, string type, string exclude = null)
        {
            var bag = new Dictionary<string, int>();
            foreach (var t in Store.Data.transactions)
            {
                if (string.IsNullOrEmpty(t.subcategory)) continue;
                if (!string.IsNullOrEmpty(category) && t.category != category) continue;
                if (!string.IsNullOrEmpty(type) && t.type != type) continue;
                bag[t.subcategory] = bag.TryGetValue(t.subcategory, out var n) ? n + 1 : 1;
            }
            return bag.Where(kv => kv.Key != exclude)
                      .OrderByDescending(kv => kv.Value)
                      .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                      .Select(kv => kv.Key)
                      .ToList();
        }

        /// <summary>界面上要显示的全部二级选项：自定义的排前面（自己用过的更顺手），
        /// 后面接推荐表，最后保证有「其他」和当前已选的值。全部去重。</summary>
        public static List<string> Options(string category, string type, string current = null)
        {
            var list = new List<string>();
            void Push(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                s = s.Trim();
                if (!list.Contains(s)) list.Add(s);
            }

            foreach (var s in Custom(category, type, current)) Push(s);
            foreach (var s in Suggested(category)) Push(s);
            Push(Other);
            Push(current);
            return list;
        }

        /// <summary>常用列表：优先用数据里最常出现的几个（最多 8 个），没有数据就退回一份固定的小清单。
        /// 放在专门留出来的一行里，省得每次都滚半天。</summary>
        public static List<string> FrequentlyUsed(string type, int max = 8)
        {
            var list = Custom(null, type).Take(max).ToList();
            if (list.Count > 0) return list;
            return type == "income"
                ? new List<string> { "月薪", "奖金", "外快", "亲友", "退货", "分红" }
                : new List<string> { "午餐", "晚餐", "外卖", "零食饮料", "打车", "日用品", "话费", "会员订阅" };
        }

        /// <summary>「其他」在候选里时判断一下，界面上好显示成"其他"而不是空</summary>
        public static bool IsOther(string sub) => string.Equals(sub, Other, StringComparison.Ordinal);

        /// <summary>归一化：把外部的二级分类名映射成我们表里写法，认不出来原样保留（不丢用户数据）。
        /// 现在只处理大小写/空格这种小问题，主要给导入流程留个口子。</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            return raw.Trim();
        }
    }
}