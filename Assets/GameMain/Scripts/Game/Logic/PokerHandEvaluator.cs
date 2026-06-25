/****************************************************
    文件：PokerHandEvaluator.cs
    作者：k0itoyuu
    日期：2026/06/24
    功能：牌型判断器 —— 根据出牌列表按优先级判定牌型，
          返回 EnumPokerHand + 对应 Add/Mul 分值。
          算法仿照 Balatro evaluate_poker_hand。
*****************************************************/
using System.Collections.Generic;
using System.Linq;
using Yuu.Data;

namespace Yuu
{
    /// <summary>
    /// 牌型判定结果。
    /// </summary>
    public class PokerHandResult
    {
        /// <summary>判定牌型。</summary>
        public EnumPokerHand HandType { get; set; }

        /// <summary>加码值。</summary>
        public int Add { get; set; }

        /// <summary>乘倍值。</summary>
        public int Mul { get; set; }

        public override string ToString()
        {
            return string.Format("{0}  Add={1}  Mul={2}", HandType, Add, Mul);
        }
    }

    /// <summary>
    /// 牌型判断器。
    /// 输入出牌列表，按 Balatro 优先级从高到低判定最佳牌型。
    /// </summary>
    public static class PokerHandEvaluator
    {
        // ============================================================
        //  辅助函数 —— 对应 Balatro misc_functions: get_X_same / get_flush / get_straight
        // ============================================================

        /// <summary>
        /// 获取点数出现次数 >= x 的分组。
        /// 返回 List<(rank, count)>，按 count 降序。
        /// </summary>
        private static List<(int rank, int count)> GetRankGroups(List<CardData> cards)
        {
            Dictionary<int, int> rankCount = new Dictionary<int, int>();
            foreach (var card in cards)
            {
                if (rankCount.ContainsKey(card.Rank))
                    rankCount[card.Rank]++;
                else
                    rankCount[card.Rank] = 1;
            }

            // 按 count 降序、rank 降序排序
            var groups = rankCount
                .Select(kv => (rank: kv.Key, count: kv.Value))
                .OrderByDescending(g => g.count)
                .ThenByDescending(g => g.rank)
                .ToList();

            return groups;
        }

        /// <summary>
        /// 检查是否存在 count >= x 的牌组。
        /// 返回 (存在?, 该点数, 具体数量)。
        /// </summary>
        private static bool HasXSame(int x, List<CardData> cards, out int rank, out int count)
        {
            var groups = GetRankGroups(cards);
            foreach (var g in groups)
            {
                if (g.count >= x)
                {
                    rank = g.rank;
                    count = g.count;
                    return true;
                }
            }
            rank = 0;
            count = 0;
            return false;
        }

        /// <summary>
        /// 获取出现 count == x 的分组列表（用于对子、三条等精确匹配）。
        /// </summary>
        private static List<int> GetExactXSame(int x, List<CardData> cards)
        {
            var groups = GetRankGroups(cards);
            List<int> result = new List<int>();
            foreach (var g in groups)
            {
                if (g.count == x)
                    result.Add(g.rank);
            }
            return result;
        }

        /// <summary>
        /// 判断是否同花（所有牌花色相同）。
        /// 需要至少 5 张牌。
        /// </summary>
        private static bool IsFlush(List<CardData> cards)
        {
            if (cards == null || cards.Count < 5)
                return false;

            string suit = cards[0].Suit;
            for (int i = 1; i < cards.Count; i++)
            {
                if (cards[i].Suit != suit)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 判断是否顺子（点数连续）。
        /// 需要至少 5 张牌，A 可作 1 或 14。
        /// 返回 (是顺子?, 最小点数, 最大点数)。
        /// </summary>
        private static bool IsStraight(List<CardData> cards, out int lowRank, out int highRank)
        {
            lowRank = 0;
            highRank = 0;

            if (cards == null || cards.Count < 5)
                return false;

            // 去重排序
            var ranks = cards.Select(c => c.Rank).Distinct().OrderBy(r => r).ToList();

            // 标准顺子检查
            if (IsConsecutive(ranks, out lowRank, out highRank))
                return true;

            // A-2-3-4-5 特殊处理: 将 A(14) 视为 1
            if (ranks.Contains(14))
            {
                var lowRanks = ranks.Select(r => r == 14 ? 1 : r).Distinct().OrderBy(r => r).ToList();
                if (IsConsecutive(lowRanks, out lowRank, out highRank))
                {
                    // lowRank 为 1，highRank 为 5
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查排序后的点数列表是否连续。
        /// </summary>
        private static bool IsConsecutive(List<int> sortedRanks, out int low, out int high)
        {
            low = 0;
            high = 0;

            if (sortedRanks.Count < 5)
                return false;

            // 尝试找最长连续段
            int bestStart = 0;
            int bestLen = 1;
            int curStart = 0;
            int curLen = 1;

            for (int i = 1; i < sortedRanks.Count; i++)
            {
                if (sortedRanks[i] == sortedRanks[i - 1] + 1)
                {
                    curLen++;
                }
                else
                {
                    if (curLen > bestLen)
                    {
                        bestLen = curLen;
                        bestStart = curStart;
                    }
                    curStart = i;
                    curLen = 1;
                }
            }
            if (curLen > bestLen)
            {
                bestLen = curLen;
                bestStart = curStart;
            }

            if (bestLen >= 5)
            {
                low = sortedRanks[bestStart];
                high = sortedRanks[bestStart + bestLen - 1];
                return true;
            }

            return false;
        }

        // ============================================================
        //  主判定 —— 对应 Balatro evaluate_poker_hand
        //  按优先级从高到低依次判定，返回第一个匹配的牌型
        // ============================================================

        /// <summary>
        /// 判定牌型。返回最佳匹配的 PokerHandResult（含 Add/Mul）。
        /// 若配置表未加载则 Add/Mul 为 0。
        /// </summary>
        /// <param name="cards">出牌列表（1-5 张）。</param>
        /// <returns>判定结果。</returns>
        public static PokerHandResult Evaluate(List<CardData> cards)
        {
            if (cards == null || cards.Count == 0)
                return new PokerHandResult { HandType = EnumPokerHand.None, Add = 0, Mul = 0 };

            int cardCount = cards.Count;
            var groups = GetRankGroups(cards);
            bool flush = IsFlush(cards);
            bool straight = IsStraight(cards, out int straightLow, out int straightHigh);

            // 最高点数
            int highestRank = cards.Max(c => c.Rank);

            EnumPokerHand handType;

            // ---- 按优先级判定 ----

            // Flush Five: 5 张同点 + 同花
            if (cardCount >= 5 && HasXSame(5, cards, out _, out _) && flush)
            {
                handType = EnumPokerHand.FlushFiveOfAKind;
            }
            // Flush House: 三条 + 对子 + 同花
            else if (cardCount >= 5 && HasXSame(3, cards, out _, out _) && HasXSame(2, cards, out int pairRank2, out _) && flush)
            {
                // 确保三条和对子是不同的点数
                var threes = GetExactXSame(3, cards);
                var twos = GetExactXSame(2, cards);
                if (threes.Count > 0 && twos.Count > 0 && threes[0] != twos[0])
                    handType = EnumPokerHand.FlushFullHouse;
                else
                    handType = EnumPokerHand.Flush;
            }
            // Five of a Kind: 5 张同点
            else if (cardCount >= 5 && HasXSame(5, cards, out _, out _))
            {
                handType = EnumPokerHand.FiveOfAKind;
            }
            // Straight Flush
            else if (straight && flush && cardCount >= 5)
            {
                handType = EnumPokerHand.StraightFlush;
            }
            // Four of a Kind
            else if (HasXSame(4, cards, out _, out _))
            {
                handType = EnumPokerHand.FourOfAKind;
            }
            // Full House: 三条 + 对子
            else if (HasXSame(3, cards, out _, out _) && HasXSame(2, cards, out _, out _))
            {
                var threes2 = GetExactXSame(3, cards);
                var twos2 = GetExactXSame(2, cards);
                if (threes2.Count > 0 && twos2.Count > 0 && threes2[0] != twos2[0])
                    handType = EnumPokerHand.FullHouse;
                else
                    handType = EnumPokerHand.ThreeOfAKind; // 降级：同点数的三条和对子视为三条
            }
            // Flush
            else if (flush && cardCount >= 5)
            {
                handType = EnumPokerHand.Flush;
            }
            // Straight
            else if (straight && cardCount >= 5)
            {
                handType = EnumPokerHand.Straight;
            }
            // Three of a Kind
            else if (HasXSame(3, cards, out _, out _))
            {
                handType = EnumPokerHand.ThreeOfAKind;
            }
            // Two Pair
            else if (GetExactXSame(2, cards).Count >= 2)
            {
                handType = EnumPokerHand.TwoPair;
            }
            // One Pair
            else if (HasXSame(2, cards, out _, out _))
            {
                handType = EnumPokerHand.OnePair;
            }
            // High Card
            else
            {
                handType = EnumPokerHand.HighCard;
            }

            // 从配置表获取 Add / Mul
            return BuildResult(handType);
        }

        /// <summary>
        /// 根据牌型枚举查询配置表构建结果。
        /// </summary>
        private static PokerHandResult BuildResult(EnumPokerHand handType)
        {
            int add = 0;
            int mul = 0;

            var data = GameEntry.Data.GetData<DataPokerHands>();
            if (data != null)
            {
                var pokerHandData = data.GetPokerHandData((int)handType);
                if (pokerHandData != null)
                {
                    add = pokerHandData.Add;
                    mul = pokerHandData.Mul;
                }
            }

            return new PokerHandResult
            {
                HandType = handType,
                Add = add,
                Mul = mul,
            };
        }
    }
}
