using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MoneyTracker
{
    /// <summary>
    /// 账户类型（kind）的中文名和分层顺序。
    /// 账户页要按「分组 → 类型」两层摆，所以类型得有个确定的先后：现金最先、其他最后。
    /// 老的 kind 值（cash / wallet / debit / credit / investment）原样保留不动。
    /// </summary>
    public static class AccountKindInfo
    {
        /// <summary>分层的显示顺序</summary>
        public static readonly string[] Order = { "cash", "wallet", "debit", "credit", "investment", "other" };

        public static string Label(string kind) => kind switch
        {
            "wallet" => "电子钱包",
            "debit" => "银行卡",
            "credit" => "信用卡 / 借贷",
            "investment" => "投资账户",
            "other" => "其他",
            _ => "现金"
        };

        /// <summary>认不出来的类型一律当现金，不让它掉进一个没名字的层里</summary>
        public static string Normalize(string kind)
            => Array.IndexOf(Order, kind) >= 0 ? kind : "cash";

        public static int Rank(string kind)
        {
            int i = Array.IndexOf(Order, Normalize(kind));
            return i < 0 ? Order.Length : i;
        }

        /// <summary>下拉里的序号 ↔ kind，两处用同一份，别各写一遍</summary>
        public static int IndexOf(string kind) => Array.IndexOf(Order, Normalize(kind));

        public static string At(int index)
            => index >= 0 && index < Order.Length ? Order[index] : "cash";
    }

    /// <summary>
    /// 账户分组。分组名是完全自由的，但「自己」是默认档：
    /// 老数据没有 group 字段（反序列化后是 null 或空串）时，一律落到「自己」，
    /// 这样升级上来的账户绝对不会掉进一个看不见的组里。
    /// </summary>
    public static class AccountGroups
    {
        public const string Self = "自己";

        /// <summary>界面给的几个顺手选项，用户也可以自己起名字</summary>
        public static readonly string[] Preset = { "自己", "家人", "他人", "工作" };

        public static string Of(Account a)
            => a == null || string.IsNullOrWhiteSpace(a.group) ? Self : a.group.Trim();

        /// <summary>数据里出现过的分组名：预置的排前面，用户自己起的按首次出现顺序跟在后面</summary>
        public static List<string> Known()
        {
            var list = new List<string>(Preset);
            foreach (var a in Store.Data?.accounts ?? new List<Account>())
            {
                var g = Of(a);
                if (!list.Contains(g)) list.Add(g);
            }
            return list;
        }

        /// <summary>排序用：预置分组按 Preset 的顺序，自定义的分组排在它们后面</summary>
        public static int Rank(string name)
        {
            int i = Array.IndexOf(Preset, name);
            return i < 0 ? Preset.Length : i;
        }
    }

    /// <summary>
    /// 「替谁花了多少 / 谁欠我多少」里的一行：一个人 + 一共替他花了多少 + 还欠多少 + 已经还了多少。
    /// 三个数都是累计口径（不跟账户页上面的月份走）：欠款本来就是累计的，
    /// 而「一共花了多少」跟「还欠多少」摆在同一行，用户才看得出这两个数的关系。
    /// </summary>
    public class DebtRow
    {
        public string Person { get; set; } = "";
        public long Spent { get; set; }          // 写了他名字的支出合计（含没标垫付的）
        public int SpentCount { get; set; }
        public long Unpaid { get; set; }         // 其中标了垫付、还没还的
        public int UnpaidCount { get; set; }
        public long Repaid { get; set; }         // 其中标了垫付、已经还了的
        public int RepaidCount { get; set; }

        public string SpentText => "一共替他花了 " + Util.Yuan(Spent) + " · " + SpentCount + " 笔";
        public string AmountText => Util.Yuan(Unpaid);
        public string UnpaidText => Unpaid > 0 ? "还欠 " + Util.Yuan(Unpaid) : "没欠了";
        public string DetailText => UnpaidCount > 0
            ? UnpaidCount + " 笔还没还"
            : RepaidCount > 0 ? "已还清 " + RepaidCount + " 笔（" + Util.Yuan(Repaid) + "）"
            : "没标成垫付";
        public string UndoText => Person + " " + Util.Yuan(Repaid) + " · 撤销";

        public bool HasDebt => Unpaid > 0;
        public Visibility SettleVis => HasDebt ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>没欠款、但有已还记录的人才给「撤销」</summary>
        public Visibility UndoVis => !HasDebt && RepaidCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DebtVis => HasDebt ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ZeroVis => HasDebt ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 垫付（替别人先付的钱）的统一口径。界面几处都要用，集中放这里，
    /// 免得明细、账户页、汇总各算各的，算出来的数对不上。
    /// </summary>
    public static class Advances
    {
        /// <summary>标了垫付但没写替谁花的，归到这一个名字下，不能因为没名字就丢掉这笔账</summary>
        public const string NoName = "没写名字";

        /// <summary>「替谁花的」留空时的显示名</summary>
        public const string SelfLabel = "自己的事";

        public static bool IsAdvance(Transaction t) => t != null && t.isAdvance;

        /// <summary>
        /// 当前账本里的记录。
        /// 多人共用一个程序时，明细页看的就是当前这本，所以账户页的支出/收入/欠款
        /// 也按当前账本算，两边的数才对得上（余额是另一回事，见 AccountWindow 的说明）。
        /// 这里只认模型里的 ledgerId + settings.currentLedger，不依赖别的模块加的接口。
        /// </summary>
        public static IEnumerable<Transaction> ScopedTx()
        {
            var all = Store.Data?.transactions ?? new List<Transaction>();
            var cur = Store.Data?.settings?.currentLedger;
            if (string.IsNullOrEmpty(cur)) return all;

            var ledgers = Store.Data.ledgers;
            string first = ledgers != null && ledgers.Count > 0 ? ledgers[0].id : "";
            // 老记录没有 ledgerId，Normalize 会把它们归到第一个账本，这里保持同一个口径
            return all.Where(t => t != null &&
                (string.IsNullOrEmpty(t.ledgerId) ? first : t.ledgerId) == cur);
        }

        /// <summary>算进「别人欠我」的：支出 + 垫付 + 还没还。转账和收入不算欠款。</summary>
        public static bool CountsAsDebt(Transaction t)
            => t != null && t.type == "expense" && t.isAdvance && !t.isReimbursed;

        public static string PersonOf(Transaction t)
            => t == null || string.IsNullOrWhiteSpace(t.forWhom) ? NoName : t.forWhom.Trim();

        /// <summary>明细表那一列显示的字，不是垫付就返回空串（别占地方）</summary>
        public static string Label(Transaction t)
        {
            if (t == null || !t.isAdvance) return "";
            return "垫 · " + PersonOf(t) + (t.isReimbursed ? "（已还）" : "");
        }

        /// <summary>
        /// 每个「替谁花的」的人一行：一共替他花了多少、还欠多少、已经还了多少。
        /// 只写了名字、没标垫付的人也列出来（用户要看的就是「替谁花了多少」），
        /// 标了垫付又没写名字的也不能丢（归到「没写名字」）。
        /// 欠得多的排前面，其次花得多的。
        /// </summary>
        public static List<DebtRow> ByPerson()
        {
            var map = new Dictionary<string, DebtRow>(StringComparer.Ordinal);
            foreach (var t in ScopedTx())
            {
                if (t == null || t.type != "expense") continue;
                bool named = !string.IsNullOrWhiteSpace(t.forWhom);
                if (!named && !t.isAdvance) continue;        // 自己的普通支出不算「替谁花的」

                var key = PersonOf(t);
                if (!map.TryGetValue(key, out var row))
                {
                    row = new DebtRow { Person = key };
                    map[key] = row;
                }
                row.Spent += t.amount;
                row.SpentCount++;
                if (!t.isAdvance) continue;
                if (t.isReimbursed) { row.Repaid += t.amount; row.RepaidCount++; }
                else { row.Unpaid += t.amount; row.UnpaidCount++; }
            }

            return map.Values
                .OrderByDescending(r => r.Unpaid)
                .ThenByDescending(r => r.Spent)
                .ThenBy(r => r.Person, StringComparer.Ordinal)
                .ToList();
        }

        public static long UnpaidTotal() => ByPerson().Sum(r => r.Unpaid);

        /// <summary>一共替别人花了多少（累计），不跟月份走</summary>
        public static long SpentTotal() => ByPerson().Sum(r => r.Spent);

        /// <summary>
        /// 把某个人的垫付整体标成已还（或撤销这个标记），返回改了几笔。
        /// 只碰「支出 + 垫付」的记录，别的记录不动。
        /// </summary>
        public static int SettlePerson(string person, bool reimbursed)
        {
            if (string.IsNullOrWhiteSpace(person)) return 0;
            var key = person.Trim();
            int n = 0;
            foreach (var t in ScopedTx())
            {
                if (t == null || !t.isAdvance || t.type != "expense") continue;
                if (PersonOf(t) != key) continue;
                if (t.isReimbursed == reimbursed) continue;
                t.isReimbursed = reimbursed;
                t.updatedAt = Util.NowStamp();
                n++;
            }
            if (n > 0) { Store.Save(); Store.RaiseChanged(); }
            return n;
        }

        /// <summary>只改指定的这几笔（明细里右键用的），返回改了几笔</summary>
        public static int SetReimbursed(IEnumerable<Transaction> txs, bool reimbursed)
        {
            int n = 0;
            foreach (var t in txs ?? Enumerable.Empty<Transaction>())
            {
                if (t == null || !t.isAdvance || t.isReimbursed == reimbursed) continue;
                t.isReimbursed = reimbursed;
                t.updatedAt = Util.NowStamp();
                n++;
            }
            if (n > 0) { Store.Save(); Store.RaiseChanged(); }
            return n;
        }

        /// <summary>填过的名字，给「替谁花的」当候选（用过越多的越靠前）</summary>
        public static List<string> KnownPeople()
            => ScopedTx()
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.forWhom))
                .GroupBy(t => t.forWhom.Trim())
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key)
                .ToList();
    }
}