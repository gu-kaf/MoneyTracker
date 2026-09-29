using System;
using System.Collections.Generic;
using System.Linq;

namespace MoneyTracker
{
    // ==================== 基础模型 ====================
    // 金额一律用「分」存整数，绝不用浮点数——和共享规格一致。

    public class Transaction
    {
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string type { get; set; } = "expense";      // expense / income / transfer
        public long amount { get; set; }                   // 分
        public string date { get; set; } = "";             // yyyy-MM-dd
        public string time { get; set; } = "";             // HH:mm
        public string category { get; set; } = "";
        public string subcategory { get; set; } = "";
        public string account { get; set; } = "现金";
        public string toAccount { get; set; } = "";
        public string merchant { get; set; } = "";
        public string note { get; set; } = "";
        public List<string> tags { get; set; } = new List<string>();
        public string source { get; set; } = "manual";     // manual / auto / csv / import
        public bool excludeFromBudget { get; set; }

        // ---- 多人：这一笔记在谁的账本上 ----
        // 空字符串 = 默认账本（老数据没有这个字段，都算默认账本）
        public string ledgerId { get; set; } = "";

        // ---- 替别人花的钱 ----
        public string forWhom { get; set; } = "";          // 替谁花的，空=自己的事
        public bool isAdvance { get; set; }                // 是不是垫付（对方该还我）
        public bool isReimbursed { get; set; }             // 这笔垫付对方还了没有

        public string hash { get; set; } = "";
        public string createdAt { get; set; } = DateTime.Now.ToString("s");
        public string updatedAt { get; set; } = DateTime.Now.ToString("s");
    }

    /// <summary>
    /// 一个账本 = 一个人。多个人共用一个程序，各记各的，还能横向比。
    /// 数据都放在同一份 db.json 里，靠 Transaction.ledgerId 区分，
    /// 这样切换、对比都不用换文件，也不会出现「谁的账本丢了」。
    /// </summary>
    public class Ledger
    {
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string name { get; set; } = "";             // 显示名，比如「我」「老婆」「小店」
        public string color { get; set; } = "";            // 标识色，空=自动按顺序取
        public string note { get; set; } = "";
        public bool archived { get; set; }
        public string createdAt { get; set; } = DateTime.Now.ToString("s");
    }

    public class Account
    {
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string name { get; set; } = "";
        public string kind { get; set; } = "cash";        // cash / debit / credit / wallet / investment

        // ---- 分组与归属：把「自己的」和「别人的」账户在界面上分开摆 ----
        public string group { get; set; } = "自己";        // 分组名：自己 / 家人 / 他人 / 工作 …（自由填）
        public string owner { get; set; } = "";            // 这个账户是谁的，空=自己的
        public bool isOthers { get; set; }                 // 是不是别人的账户（计入「他人」组）

        public long initialBalance { get; set; }
        public bool includeInAssets { get; set; } = true;
        public bool archived { get; set; }
    }

    public class Budget
    {
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string period { get; set; } = "monthly";   // monthly / weekly / yearly
        public string scope { get; set; } = "total";      // total 或分类名
        public long amount { get; set; }                  // 分
        public string startDate { get; set; } = "";
        public double alertThreshold { get; set; } = 0.8;
        public bool rollover { get; set; }
    }

    public class Rule
    {
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string keyword { get; set; } = "";          // 多个关键词用 | 分隔
        public string matchField { get; set; } = "both";   // merchant / note / both
        public string setCategory { get; set; } = "";
        public string setSubcategory { get; set; } = "";
        public string setType { get; set; } = "";          // 空 / expense / income / transfer
        public int priority { get; set; } = 50;
        public int hitCount { get; set; }
        public bool enabled { get; set; } = true;
    }

    public class AppSettings
    {
        public string theme { get; set; } = "light";       // 主题 id
        public string accent { get; set; } = "";           // 覆盖强调色，空=跟随主题
        public int monthStartDay { get; set; } = 1;
        public bool redExpense { get; set; } = true;
        public bool allowDuplicateImport { get; set; }
        public string autoScanDir { get; set; } = "";
        public bool autoScanEnabled { get; set; }
        public int autoScanIntervalSec { get; set; } = 60;
        public string currency { get; set; } = "¥";

        // ---- 外观：字体 ----
        public string fontId { get; set; } = "soft";       // soft / standard / deng / round / kai / custom
        public int fontScale { get; set; } = 100;          // 整体字号百分比，90~125
        public string fontFile { get; set; } = "";         // 用户自己装的字体文件（.ttf/.otf），空=用内置那几套

        // ---- 外观：自定义背景图 ----
        public string bgImage { get; set; } = "";          // 图片路径，空=不用背景图
        public int bgBlur { get; set; } = 18;              // 高斯模糊半径 0~60
        public int bgDim { get; set; } = 62;               // 图片压暗/压白程度 0~95，越大越不抢内容
        public string bgFit { get; set; } = "fill";        // fill / uniform / uniformToFill / tile

        // ---- 分类体系：用户可以自己增删改，所以存在设置里而不是写死在代码里 ----
        // 老数据没有这两个字段（会是 null），用到的地方都会退回内置默认值，不会崩。
        public List<CategoryGroup> expenseCategories { get; set; } = new List<CategoryGroup>();
        public List<CategoryGroup> incomeCategories { get; set; } = new List<CategoryGroup>();

        // ---- 多人账本 ----
        public string currentLedger { get; set; } = "";    // 当前在看谁的账本，空=第一个
    }

    public class AppData
    {
        public string format { get; set; } = "money-tracker";
        public int version { get; set; } = 1;
        public string exportedAt { get; set; } = "";
        public List<Transaction> transactions { get; set; } = new List<Transaction>();
        public List<Account> accounts { get; set; } = new List<Account>();
        public List<Budget> budgets { get; set; } = new List<Budget>();
        public List<Rule> rules { get; set; } = new List<Rule>();
        public List<Ledger> ledgers { get; set; } = new List<Ledger>();
        public AppSettings settings { get; set; } = new AppSettings();
    }

    // ==================== 分类体系 ====================

    public class CategoryGroup
    {
        // 内部标记，用来在「改名」时认出这是原来哪一项（不靠名字猜）。
        // 界面上不显示，用户也不用管；老数据没这个字段，读进来会自动生成。
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string name { get; set; } = "";
        public List<string> subs { get; set; } = new List<string>();
        public CategoryGroup() { }
        public CategoryGroup(string n, params string[] s) { name = n; subs = new List<string>(s); }
    }

    /// <summary>
    /// 分类体系。出厂有一整套默认的，但用户可以自己加、改名、删、排序，
    /// 改过之后存在 settings 里，这里读的就是用户那一份。
    /// 名字（ExpenseNames / SubsOf / Normalize…）跟以前保持一致，调用方不用改。
    /// </summary>
    public static class Categories
    {
        public static readonly CategoryGroup[] DefaultExpense = new[]
        {
            new CategoryGroup("餐饮", "早餐", "午餐", "晚餐", "外卖", "零食饮料", "聚餐"),
            new CategoryGroup("交通", "公交地铁", "打车", "加油", "停车", "火车飞机"),
            new CategoryGroup("购物", "日用品", "服饰", "数码", "家居", "美妆"),
            new CategoryGroup("居住", "房租房贷", "水电燃气", "物业", "维修"),
            new CategoryGroup("娱乐", "电影演出", "游戏", "旅行", "运动健身", "订阅会员"),
            new CategoryGroup("医疗", "门诊", "药品", "体检", "保险"),
            new CategoryGroup("教育", "书籍", "课程", "培训", "文具"),
            new CategoryGroup("通讯", "话费", "宽带", "会员订阅"),
            new CategoryGroup("人情", "红包", "礼物", "请客", "孝敬"),
            new CategoryGroup("宠物", "宠物食品", "宠物医疗", "宠物用品"),
            new CategoryGroup("金融", "手续费", "利息", "税费", "罚款"),
            new CategoryGroup("其他支出")
        };

        public static readonly CategoryGroup[] DefaultIncome = new[]
        {
            new CategoryGroup("工资", "月薪", "奖金", "加班费"),
            new CategoryGroup("兼职", "外快", "稿费", "接单"),
            new CategoryGroup("投资", "利息", "分红", "理财收益"),
            new CategoryGroup("红包", "亲友", "平台红包"),
            new CategoryGroup("报销", "差旅", "办公"),
            new CategoryGroup("退款", "退货", "返现"),
            new CategoryGroup("其他收入")
        };

        /// <summary>当前生效的支出分类（用户改过就是他自己的那套）</summary>
        public static List<CategoryGroup> ExpenseList => Pick(true);

        /// <summary>当前生效的收入分类</summary>
        public static List<CategoryGroup> IncomeList => Pick(false);

        private static List<CategoryGroup> Pick(bool expense)
        {
            try
            {
                var s = Store.Data != null ? Store.Data.settings : null;
                if (s != null)
                {
                    var mine = expense ? s.expenseCategories : s.incomeCategories;
                    if (mine != null && mine.Count > 0) return mine;
                }
            }
            catch { }
            // 用户没改过 / 数据还没读出来：给出厂默认的一份副本，
            // 给副本是怕调用方顺手改了它，把默认值弄脏。
            return (expense ? DefaultExpense : DefaultIncome).Select(Clone).ToList();
        }

        public static CategoryGroup Clone(CategoryGroup g)
            => new CategoryGroup(g.name, (g.subs ?? new List<string>()).ToArray()) { id = g.id };

        public static List<CategoryGroup> CloneAll(IEnumerable<CategoryGroup> src)
            => (src ?? new CategoryGroup[0]).Select(Clone).ToList();

        /// <summary>第一次启动时把出厂默认灌进设置里，之后就以设置里那份为准</summary>
        public static void Ensure()
        {
            var s = Store.Data.settings;
            if (s.expenseCategories == null) s.expenseCategories = new List<CategoryGroup>();
            if (s.incomeCategories == null) s.incomeCategories = new List<CategoryGroup>();
            if (s.expenseCategories.Count == 0) s.expenseCategories = CloneAll(DefaultExpense);
            if (s.incomeCategories.Count == 0) s.incomeCategories = CloneAll(DefaultIncome);
        }

        public static string[] ExpenseNames => ExpenseList.Select(g => g.name).ToArray();
        public static string[] IncomeNames => IncomeList.Select(g => g.name).ToArray();

        public static string[] SubsOf(string cat, string type)
        {
            var list = type == "income" ? IncomeList : ExpenseList;
            var g = list.FirstOrDefault(x => x.name == cat);
            return g == null ? new string[0] : (g.subs ?? new List<string>()).ToArray();
        }

        /// <summary>把外部账单里的分类名映射到我们的体系，认不出来就落到"其他"</summary>
        public static string Normalize(string raw, string type)
        {
            if (string.IsNullOrWhiteSpace(raw)) return type == "income" ? "其他收入" : "其他支出";
            string s = raw.Trim();
            var table = new Dictionary<string, string>
            {
                { "餐饮美食", "餐饮" }, { "餐饮", "餐饮" }, { "外卖", "餐饮" }, { "食品酒水", "餐饮" },
                { "交通出行", "交通" }, { "交通", "交通" }, { "打车", "交通" }, { "公共交通", "交通" },
                { "购物", "购物" }, { "日用百货", "购物" }, { "服饰装扮", "购物" }, { "数码电器", "购物" },
                { "居住", "居住" }, { "房租房贷", "居住" }, { "水电煤", "居住" },
                { "文化休闲", "娱乐" }, { "娱乐", "娱乐" }, { "运动户外", "娱乐" }, { "订阅服务", "通讯" },
                { "医疗健康", "医疗" }, { "医疗", "医疗" }, { "保险", "医疗" },
                { "教育培训", "教育" }, { "教育", "教育" }, { "学习", "教育" },
                { "通讯物流", "通讯" }, { "通讯", "通讯" }, { "话费", "通讯" },
                { "人情往来", "人情" }, { "人情", "人情" }, { "红包", "人情" },
                { "宠物", "宠物" }, { "宠物宝贝", "宠物" },
                { "金融保险", "金融" }, { "金融", "金融" },
                { "转账", "其他支出" }, { "其他", "其他支出" },
                { "工资", "工资" }, { "收入", "其他收入" }, { "退款", "退款" }, { "投资收益", "投资" }
            };
            if (table.TryGetValue(s, out var hit)) return hit;
            var exact = (type == "income" ? IncomeNames : ExpenseNames);
            foreach (var n in exact) if (s.Contains(n) || n.Contains(s)) return n;
            return type == "income" ? "其他收入" : "其他支出";
        }
    }

    // ==================== 预置规则 ====================

    public static class DefaultRules
    {
        public static List<Rule> Build()
        {
            var defs = new (string kw, string cat, string sub, string type, int pri)[]
            {
                ("餐饮|美团|饿了么|肯德基|麦当劳|星巴克|瑞幸|库迪|蜜雪|外卖|餐厅|饭店|食堂|小吃|快餐|烧烤|火锅|米线|面馆|拉面|饺子|包子|料理|寿司|披萨|汉堡|奶茶|咖啡|饮品|早餐|午餐|晚餐|食品|生鲜|水果", "餐饮", "", "expense", 100),
                ("工资|月薪|薪金|代发|薪水|劳务|奖金|补贴", "工资", "", "income", 95),
                ("淘宝|天猫|京东|拼多多|唯品会|抖音商城|超市|永辉|盒马|大润发|华润|沃尔玛|便利店|商城|旗舰店|旗舰|日用品|百货", "购物", "", "expense", 90),
                ("滴滴|高德|花小猪|曹操|地铁|公交|打车|出租车|网约车|12306|铁路|机票|航空|加油|中石化|中石油|停车|高速|高铁|共享单车|单车", "交通", "", "expense", 85),
                ("爱奇艺|腾讯视频|优酷|芒果|网易云|QQ音乐|Spotify|B站|bilibili|会员|订阅|续费|话费|流量|宽带|电信|联通|移动通信", "通讯", "会员订阅", "expense", 80),
                ("房租|租金|水费|电费|燃气|天然气|物业|供暖|国家电网|自来水", "居住", "", "expense", 75),
                ("医院|药房|药店|门诊|体检|挂号|诊所|口腔|牙科", "医疗", "", "expense", 70),
                ("退款|退货|返现|退还|冲正", "退款", "", "income", 60),
                ("红包|转账收|微信红包|群收款", "红包", "", "income", 55),
                ("酒店|民宿|旅馆|携程|去哪儿|飞猪|旅游|门票|景区", "娱乐", "旅游", "expense", 50),
                ("电影|影城|影院|KTV|游戏|Steam|网吧|剧本杀|密室|演唱会|展|话剧", "娱乐", "电影演出", "expense", 48),
                ("宠物|猫粮|狗粮|宠物医院|兽医", "宠物", "", "expense", 46),
                ("学费|培训|课程|网课|书店|图书|当当|考试|报名费|教材", "教育", "", "expense", 44),
                ("保费|保险|医保|社保|公积金", "金融", "保险", "expense", 42),
                ("还款|信用卡还款|花呗|借呗|分期|利息|手续费", "金融", "还款", "expense", 40)
            };
            var list = new List<Rule>();
            foreach (var d in defs)
            {
                list.Add(new Rule
                {
                    id = "rule-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    keyword = d.kw,
                    matchField = "both",
                    setCategory = d.cat,
                    setSubcategory = d.sub,
                    setType = d.type,
                    priority = d.pri,
                    hitCount = 0,
                    enabled = true
                });
            }
            return list;
        }
    }

    public static class DefaultAccounts
    {
        public static List<Account> Build()
        {
            return new List<Account>
            {
                new Account { name = "现金", kind = "cash" },
                new Account { name = "微信", kind = "wallet" },
                new Account { name = "支付宝", kind = "wallet" },
                new Account { name = "招商银行", kind = "debit" },
                new Account { name = "信用卡", kind = "credit" }
            };
        }
    }
}